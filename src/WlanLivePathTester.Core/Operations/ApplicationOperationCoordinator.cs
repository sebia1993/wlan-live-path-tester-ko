using System.Diagnostics;
using System.Text.Json.Serialization;

namespace WlanLivePathTester.Core.Operations;

public enum ApplicationOperationKind
{
    None,
    DownloadMeasurement,
    ProxyRouteResolution,
    RepeatedMeasurement,
    BrowserObservation,
    RouteEvidence,
    RouteComparison,
    WindowsProxyImport,
    RouteComparisonReportSave,
    DiagnosticReportSave,
    NetworkAdapterDiagnostics,
    NetworkEnvironmentCapture
}

public enum ApplicationOperationStartStatus
{
    Started,
    Busy,
    ShutdownPending
}

public enum ApplicationOperationCancellationStatus
{
    Requested,
    AlreadyRequested,
    NotSupported,
    NotActive,
    CallbackFailed
}

public sealed record ApplicationOperationSnapshot(
    bool IsBusy,
    bool ShutdownRequested,
    long? OperationId,
    ApplicationOperationKind Kind,
    DateTimeOffset? StartedAt,
    bool SupportsCancellation,
    bool CancellationRequested,
    bool CancellationCallbackFailed)
{
    public static ApplicationOperationSnapshot Idle(
        bool shutdownRequested = false) =>
        new(
            IsBusy: false,
            ShutdownRequested: shutdownRequested,
            OperationId: null,
            Kind: ApplicationOperationKind.None,
            StartedAt: null,
            SupportsCancellation: false,
            CancellationRequested: false,
            CancellationCallbackFailed: false);
}

public sealed record ApplicationOperationStartResult(
    ApplicationOperationStartStatus Status,
    ApplicationOperationSnapshot Snapshot,
    [property: JsonIgnore]
    ApplicationOperationLease? Lease)
{
    public bool Started =>
        Status == ApplicationOperationStartStatus.Started
        && Lease is not null;
}

public sealed record ApplicationOperationShutdownResult(
    ApplicationOperationCancellationStatus CancellationStatus,
    ApplicationOperationSnapshot FinalSnapshot);

public sealed class ApplicationOperationStateChangedEventArgs(
    ApplicationOperationSnapshot snapshot) : EventArgs
{
    public ApplicationOperationSnapshot Snapshot { get; } = snapshot;
}

[DebuggerDisplay("{Kind} #{OperationId} Completed={IsCompleted}")]
public sealed class ApplicationOperationLease : IDisposable
{
    private readonly ApplicationOperationCoordinator _coordinator;
    private int _completionRequested;

    internal ApplicationOperationLease(
        ApplicationOperationCoordinator coordinator,
        long operationId,
        ApplicationOperationKind kind,
        Task completion)
    {
        _coordinator = coordinator;
        OperationId = operationId;
        Kind = kind;
        Completion = completion;
    }

    public long OperationId { get; }

    public ApplicationOperationKind Kind { get; }

    [JsonIgnore]
    public Task Completion { get; }

    public bool IsCompleted =>
        Volatile.Read(ref _completionRequested) != 0;

    public ApplicationOperationCancellationStatus
        RequestCancellation() =>
        IsCompleted
            ? ApplicationOperationCancellationStatus.NotActive
            : _coordinator.RequestCancellation(OperationId);

    public bool Complete()
    {
        if (Interlocked.Exchange(ref _completionRequested, 1) != 0)
        {
            return false;
        }

        return _coordinator.Complete(OperationId);
    }

    public void Dispose() => Complete();
}

public sealed class ApplicationOperationCoordinator
{
    private readonly object _sync = new();
    private ActiveOperation? _active;
    private long _nextOperationId;
    private bool _shutdownRequested;

    public event EventHandler<ApplicationOperationStateChangedEventArgs>?
        StateChanged;

    public ApplicationOperationSnapshot Snapshot
    {
        get
        {
            lock (_sync)
            {
                return CreateSnapshotUnsafe();
            }
        }
    }

    public bool IsBusy
    {
        get
        {
            lock (_sync)
            {
                return _active is not null;
            }
        }
    }

    public bool ShutdownRequested
    {
        get
        {
            lock (_sync)
            {
                return _shutdownRequested;
            }
        }
    }

    public ApplicationOperationStartResult TryBegin(
        ApplicationOperationKind kind,
        Action? requestCancellation = null)
    {
        ValidateKind(kind);

        ApplicationOperationSnapshot snapshot;
        ApplicationOperationLease? lease = null;
        ApplicationOperationStartStatus status;

        lock (_sync)
        {
            if (_shutdownRequested)
            {
                status = ApplicationOperationStartStatus.ShutdownPending;
                snapshot = CreateSnapshotUnsafe();
            }
            else if (_active is not null)
            {
                status = ApplicationOperationStartStatus.Busy;
                snapshot = CreateSnapshotUnsafe();
            }
            else
            {
                long operationId = checked(++_nextOperationId);
                ActiveOperation active = new(
                    operationId,
                    kind,
                    DateTimeOffset.UtcNow,
                    requestCancellation);
                _active = active;
                snapshot = CreateSnapshotUnsafe();
                lease = new ApplicationOperationLease(
                    this,
                    operationId,
                    kind,
                    active.Completion.Task);
                status = ApplicationOperationStartStatus.Started;
            }
        }

        if (status == ApplicationOperationStartStatus.Started)
        {
            Publish(snapshot);
        }

        return new ApplicationOperationStartResult(
            status,
            snapshot,
            lease);
    }

    public ApplicationOperationCancellationStatus
        RequestCancellation() =>
        RequestCancellation(expectedOperationId: null);

    public Task WaitForIdleAsync(
        CancellationToken cancellationToken = default)
    {
        Task completion;
        lock (_sync)
        {
            completion = _active?.Completion.Task
                ?? Task.CompletedTask;
        }

        return cancellationToken.CanBeCanceled
            ? completion.WaitAsync(cancellationToken)
            : completion;
    }

    public async Task<ApplicationOperationShutdownResult>
        RequestShutdownAsync(
            bool requestCancellation = true,
            CancellationToken cancellationToken = default)
    {
        Task idleTask;
        long? activeOperationId;
        ApplicationOperationSnapshot shutdownSnapshot;

        lock (_sync)
        {
            _shutdownRequested = true;
            activeOperationId = _active?.OperationId;
            idleTask = _active?.Completion.Task
                ?? Task.CompletedTask;
            shutdownSnapshot = CreateSnapshotUnsafe();
        }

        Publish(shutdownSnapshot);

        ApplicationOperationCancellationStatus cancellationStatus =
            requestCancellation && activeOperationId.HasValue
                ? RequestCancellation(activeOperationId.Value)
                : ApplicationOperationCancellationStatus.NotActive;

        if (cancellationToken.CanBeCanceled)
        {
            await idleTask.WaitAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            await idleTask.ConfigureAwait(false);
        }

        return new ApplicationOperationShutdownResult(
            cancellationStatus,
            Snapshot);
    }

    public bool CancelShutdownRequest()
    {
        ApplicationOperationSnapshot snapshot;
        lock (_sync)
        {
            if (!_shutdownRequested)
            {
                return false;
            }

            _shutdownRequested = false;
            snapshot = CreateSnapshotUnsafe();
        }

        Publish(snapshot);
        return true;
    }

    internal ApplicationOperationCancellationStatus
        RequestCancellation(long operationId) =>
        RequestCancellation((long?)operationId);

    internal bool Complete(long operationId)
    {
        TaskCompletionSource<bool>? completion = null;
        ApplicationOperationSnapshot snapshot;

        lock (_sync)
        {
            if (_active?.OperationId != operationId)
            {
                return false;
            }

            completion = _active.Completion;
            _active = null;
            snapshot = CreateSnapshotUnsafe();
        }

        completion.TrySetResult(true);
        Publish(snapshot);
        return true;
    }

    private ApplicationOperationCancellationStatus
        RequestCancellation(long? expectedOperationId)
    {
        Action? callback;
        long operationId;
        ApplicationOperationSnapshot snapshot;

        lock (_sync)
        {
            if (_active is null
                || (expectedOperationId.HasValue
                    && _active.OperationId
                        != expectedOperationId.Value))
            {
                return ApplicationOperationCancellationStatus.NotActive;
            }

            if (_active.CancellationRequested)
            {
                return ApplicationOperationCancellationStatus
                    .AlreadyRequested;
            }

            if (_active.RequestCancellation is null)
            {
                return ApplicationOperationCancellationStatus
                    .NotSupported;
            }

            _active.CancellationRequested = true;
            callback = _active.RequestCancellation;
            operationId = _active.OperationId;
            snapshot = CreateSnapshotUnsafe();
        }

        Publish(snapshot);

        try
        {
            callback();
            return ApplicationOperationCancellationStatus.Requested;
        }
        catch
        {
            ApplicationOperationSnapshot? failureSnapshot = null;
            lock (_sync)
            {
                if (_active?.OperationId == operationId)
                {
                    _active.CancellationCallbackFailed = true;
                    failureSnapshot = CreateSnapshotUnsafe();
                }
            }

            if (failureSnapshot is not null)
            {
                Publish(failureSnapshot);
            }

            return ApplicationOperationCancellationStatus.CallbackFailed;
        }
    }

    private ApplicationOperationSnapshot CreateSnapshotUnsafe()
    {
        if (_active is null)
        {
            return ApplicationOperationSnapshot.Idle(
                _shutdownRequested);
        }

        return new ApplicationOperationSnapshot(
            IsBusy: true,
            ShutdownRequested: _shutdownRequested,
            OperationId: _active.OperationId,
            Kind: _active.Kind,
            StartedAt: _active.StartedAt,
            SupportsCancellation:
                _active.RequestCancellation is not null,
            CancellationRequested:
                _active.CancellationRequested,
            CancellationCallbackFailed:
                _active.CancellationCallbackFailed);
    }

    private void Publish(ApplicationOperationSnapshot snapshot)
    {
        EventHandler<ApplicationOperationStateChangedEventArgs>?
            handlers = StateChanged;
        if (handlers is null)
        {
            return;
        }

        ApplicationOperationStateChangedEventArgs args = new(snapshot);
        foreach (EventHandler<ApplicationOperationStateChangedEventArgs>
                 handler in handlers.GetInvocationList())
        {
            try
            {
                handler(this, args);
            }
            catch
            {
                // Operation state transitions must not be rolled back by a
                // faulty observer. UI adapters can log their own failures.
            }
        }
    }

    private static void ValidateKind(ApplicationOperationKind kind)
    {
        if (kind == ApplicationOperationKind.None
            || !Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(
                nameof(kind),
                kind,
                "실행 작업 종류는 정의된 None 이외 값이어야 합니다.");
        }
    }

    private sealed class ActiveOperation(
        long operationId,
        ApplicationOperationKind kind,
        DateTimeOffset startedAt,
        Action? requestCancellation)
    {
        public long OperationId { get; } = operationId;

        public ApplicationOperationKind Kind { get; } = kind;

        public DateTimeOffset StartedAt { get; } = startedAt;

        public Action? RequestCancellation { get; } =
            requestCancellation;

        public TaskCompletionSource<bool> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool CancellationRequested { get; set; }

        public bool CancellationCallbackFailed { get; set; }
    }
}
