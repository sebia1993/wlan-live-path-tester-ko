using System.Windows.Threading;

namespace WlanLivePathTester.App;

// Owns one debounce timer, replacing the old MainWindow timer. It never reads
// adapters or owns a second application coordinator. The injected operation
// must acquire the same UI/Core lease as user-started work before collecting.
internal sealed class DeferredNetworkRefreshController : IDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _timer;
    private readonly Func<bool> _canRun;
    private readonly Func<Task<bool>> _refresh;
    private readonly Action<string> _reportFailure;
    private bool _pending;
    private bool _running;
    private bool _suspended;
    private bool _disposed;
    private int _refreshRequested;
    private int _notificationQueued;

    internal DeferredNetworkRefreshController(
        Dispatcher dispatcher,
        Func<bool> canRun,
        Func<Task<bool>> refresh,
        Action<string> reportFailure,
        TimeSpan? debounce = null)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(canRun);
        ArgumentNullException.ThrowIfNull(refresh);
        ArgumentNullException.ThrowIfNull(reportFailure);
        dispatcher.VerifyAccess();
        TimeSpan interval = debounce ?? TimeSpan.FromMilliseconds(750);
        if (interval <= TimeSpan.Zero || interval > TimeSpan.FromMinutes(1))
            throw new ArgumentOutOfRangeException(nameof(debounce));
        _dispatcher = dispatcher;
        _canRun = canRun;
        _refresh = refresh;
        _reportFailure = reportFailure;
        _timer = new DispatcherTimer(DispatcherPriority.Background, dispatcher)
        {
            Interval = interval
        };
        _timer.Tick += OnTick;
    }

    internal bool HasPendingRequest
    {
        get { _dispatcher.VerifyAccess(); return _pending || Volatile.Read(ref _refreshRequested) != 0; }
    }
    internal bool IsRunning
    {
        get { _dispatcher.VerifyAccess(); return _running; }
    }
    internal bool IsTimerEnabled
    {
        get { _dispatcher.VerifyAccess(); return _timer.IsEnabled; }
    }

    // NetworkChange callbacks may run on any thread. A burst queues at most one
    // pending dispatcher notification; the refresh bit is retained separately
    // from state-only notifications so they cannot erase a refresh request.
    internal void RequestRefresh()
    {
        if (Volatile.Read(ref _disposed)) return;
        Interlocked.Exchange(ref _refreshRequested, 1);
        QueueStateCheck();
    }

    internal void StateMayHaveChanged() => QueueStateCheck();

    private void QueueStateCheck()
    {
        if (Volatile.Read(ref _disposed) || _dispatcher.HasShutdownStarted
            || _dispatcher.HasShutdownFinished
            || Interlocked.Exchange(ref _notificationQueued, 1) != 0) return;
        try
        {
            _ = _dispatcher.InvokeAsync(() =>
            {
                Interlocked.Exchange(ref _notificationQueued, 0);
                if (_disposed) return;
                if (Interlocked.Exchange(ref _refreshRequested, 0) != 0) _pending = true;
                ScheduleIfReady();
            }, DispatcherPriority.Background);
        }
        catch (InvalidOperationException)
        {
            // Dispatcher shutdown can race the check above. Do not fall back
            // to running UI or native work on this notification thread.
            Interlocked.Exchange(ref _notificationQueued, 0);
        }
    }

    internal void Suspend()
    {
        _dispatcher.VerifyAccess();
        if (_disposed) return;
        _suspended = true;
        _timer.Stop();
    }

    internal void Resume()
    {
        _dispatcher.VerifyAccess();
        if (_disposed) return;
        _suspended = false;
        _pending = true;
        ScheduleIfReady();
    }

    private void ScheduleIfReady()
    {
        _dispatcher.VerifyAccess();
        _timer.Stop();
        if (_disposed || _suspended || _running || !_pending) return;
        try
        {
            if (_canRun()) _timer.Start();
        }
        catch (Exception exception)
        {
            ReportFailure(exception);
        }
    }

    private async void OnTick(object? sender, EventArgs e) =>
        await ProcessPendingAsync();

    // Tests can drain the same timer path without sleeping for a debounce
    // interval. False from refresh means no lease was acquired, not a read
    // failure. Such a request stays pending until another state notification.
    internal async Task ProcessPendingAsync()
    {
        _dispatcher.VerifyAccess();
        _timer.Stop();
        if (_disposed || _suspended || _running) return;
        if (Interlocked.Exchange(ref _refreshRequested, 0) != 0) _pending = true;
        if (!_pending) return;
        bool started = false;
        try
        {
            if (!_canRun()) return;
            _running = true;
            _pending = false;
            started = await _refresh();
            if (!started && !_disposed) _pending = true;
        }
        catch (Exception exception)
        {
            // A fault consumes this attempt. Do not retry continuously or expose
            // raw exception payloads; a new event/manual action may retry.
            ReportFailure(exception);
        }
        finally
        {
            _running = false;
            // A new request arriving while a successful acquisition was being
            // processed needs one later refresh. A rejected acquisition must
            // not spin merely because the readiness predicate stayed true.
            if (started && !_disposed)
            {
                if (Interlocked.Exchange(ref _refreshRequested, 0) != 0) _pending = true;
                ScheduleIfReady();
            }
        }
    }

    private void ReportFailure(Exception exception)
    {
        if (_disposed) return;
        try { _reportFailure(exception.GetType().Name); }
        catch { /* A display observer must not crash the dispatcher timer. */ }
    }

    public void Dispose()
    {
        _dispatcher.VerifyAccess();
        if (_disposed) return;
        Volatile.Write(ref _disposed, true);
        _timer.Stop();
        _timer.Tick -= OnTick;
        _pending = false;
        Interlocked.Exchange(ref _refreshRequested, 0);
        // An in-flight operation retains its own lease until actual completion.
        // Disposing this scheduler neither aborts it nor frees its native state.
    }
}
