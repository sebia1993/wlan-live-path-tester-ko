using System.Diagnostics.CodeAnalysis;

namespace WlanLivePathTester.Core.Reporting;

public enum ReportSaveSessionState
{
    Idle,
    Running,
    CancellationRequested,
    Closing,
    Closed
}

/// <summary>
/// Owns one cooperative save and its cancellation lifetime. Contains no WPF or file I/O.
/// A successful save is not changed to cancellation after its completion marker was published.
/// </summary>
public sealed class ReportSaveSession
{
    private readonly object _sync = new();
    private CancellationTokenSource? _active;
    private Task _completion = Task.CompletedTask;
    private Task _cancellation = Task.CompletedTask;
    private bool _cancelRequested;
    private bool _finishing;
    private bool _closed;
    private int _callbackFailed;

    public ReportSaveSessionState State
    {
        get
        {
            lock (_sync)
            {
                if (_closed)
                {
                    return _active is null ? ReportSaveSessionState.Closed
                        : ReportSaveSessionState.Closing;
                }
                return _active is null ? ReportSaveSessionState.Idle
                    : _cancelRequested ? ReportSaveSessionState.CancellationRequested
                    : ReportSaveSessionState.Running;
            }
        }
    }

    public bool IsBusy
    {
        get { lock (_sync) { return _active is not null; } }
    }

    /// <summary>Fixed diagnostic flag only; callback exception text is not retained here.</summary>
    public bool CancellationCallbackFailed => Volatile.Read(ref _callbackFailed) != 0;

    /// <summary>
    /// Starts exactly one delegate on the thread pool. Rejected delegates are never invoked.
    /// The returned task settles after cancellation callbacks and source disposal are finished.
    /// </summary>
    public bool TryStart<T>(Func<CancellationToken, T> save,
        [NotNullWhen(true)] out Task<T>? completion)
    {
        ArgumentNullException.ThrowIfNull(save);
        CancellationTokenSource source;
        TaskCompletionSource<T> finished;
        lock (_sync)
        {
            if (_closed || _active is not null)
            {
                completion = null;
                return false;
            }
            source = new CancellationTokenSource();
            finished = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            _active = source;
            _completion = completion = finished.Task;
            _cancellation = Task.CompletedTask;
            _cancelRequested = false;
            _finishing = false;
            Volatile.Write(ref _callbackFailed, 0);
        }
        _ = ExecuteAsync(save, source, finished);
        return true;
    }

    /// <summary>Idempotent and non-blocking with respect to user cancellation callbacks.</summary>
    public void RequestCancellation()
    {
        lock (_sync) { RequestCancellationLocked(); }
    }

    /// <summary>
    /// Requests cancellation and joins the current save without propagating its exception.
    /// The owner must observe the original task for the result, cancellation or recovery error.
    /// Does not permanently prevent another save after this operation has finished.
    /// </summary>
    public Task CancelAndWaitAsync()
    {
        lock (_sync)
        {
            RequestCancellationLocked();
            return ObserveCompletionAsync(_completion);
        }
    }

    /// <summary>Rejects new work permanently and joins the active save and its cleanup.</summary>
    public Task CloseAsync()
    {
        lock (_sync)
        {
            _closed = true;
            RequestCancellationLocked();
            return ObserveCompletionAsync(_completion);
        }
    }

    private void RequestCancellationLocked()
    {
        if (_active is null || _cancelRequested || _finishing)
        {
            return;
        }
        _cancelRequested = true;
        // CancelAsync transitions the token synchronously, but invokes callbacks asynchronously.
        // Never dispose this source until this returned task and the save have both completed.
        _cancellation = ObserveCancellationAsync(_active);
    }

    private async Task ObserveCancellationAsync(CancellationTokenSource source)
    {
        try { await source.CancelAsync().ConfigureAwait(false); }
        catch (Exception)
        {
            // The save outcome still belongs to its own task; only a fixed warning is exposed.
            Volatile.Write(ref _callbackFailed, 1);
        }
    }

    private async Task ExecuteAsync<T>(Func<CancellationToken, T> save,
        CancellationTokenSource source, TaskCompletionSource<T> finished)
    {
        T? result = default;
        Exception? failure = null;
        try
        {
            result = await Task.Run(() =>
            {
                source.Token.ThrowIfCancellationRequested();
                return save(source.Token);
            }).ConfigureAwait(false);
        }
        catch (Exception exception) { failure = exception; }

        Task cancellation;
        lock (_sync)
        {
            _finishing = true;
            cancellation = _cancellation;
        }
        await cancellation.ConfigureAwait(false);
        lock (_sync)
        {
            // All token callbacks and the delegate have ended. Late cancel requests are gated.
            source.Dispose();
            _active = null;
            _finishing = false;
            if (failure is OperationCanceledException canceled)
            {
                finished.TrySetCanceled(canceled.CancellationToken);
            }
            else if (failure is not null)
            {
                finished.TrySetException(failure);
            }
            else
            {
                // In particular, do not ThrowIfCancellationRequested after a committed save.
                finished.TrySetResult(result!);
            }
        }
    }

    private static async Task ObserveCompletionAsync(Task completion)
    {
        try { await completion.ConfigureAwait(false); }
        catch (Exception)
        {
            // Join only. The original task remains canceled/faulted for the UI to inspect.
        }
    }
}
