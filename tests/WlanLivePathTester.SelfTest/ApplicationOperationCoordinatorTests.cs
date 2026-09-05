using System.Runtime.CompilerServices;
using System.Text.Json;
using WlanLivePathTester.Core.Operations;

namespace WlanLivePathTester.SelfTest;

internal static class ApplicationOperationCoordinatorTests
{
#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Run()
    {
        StartsAndCompletesOneOperation();
        RejectsInvalidKinds();
        RejectsASecondOperationWithTheActiveSnapshot();
        AllowsOnlyOneConcurrentStarter();
        RequestsCancellationAtMostOnce();
        DistinguishesUnsupportedAndInactiveCancellation();
        ContainsCancellationCallbackFailures();
        StaleLeaseCannotCompleteANewerOperation();
        ShutdownBlocksNewStartsCancelsAndWaits();
        ShutdownCanWaitWithoutRequestingCancellation();
        CanceledShutdownCanBeReopened();
        WaitForIdleHonorsCallerCancellation();
        FaultyStateObserverCannotBreakTransitions();
        ConcurrentCancelAndCompleteRemainConsistent();
        SafeJsonDoesNotExposeCallbacksOrLeaseInternals();
        Console.WriteLine(
            "PASS exclusive application operation coordinator tests");
    }

    private static void StartsAndCompletesOneOperation()
    {
        ApplicationOperationCoordinator coordinator = new();
        List<ApplicationOperationSnapshot> events = [];
        coordinator.StateChanged += (_, args) =>
            events.Add(args.Snapshot);

        ApplicationOperationStartResult start = coordinator.TryBegin(
            ApplicationOperationKind.DownloadMeasurement);

        Ensure(start.Started,
            "첫 작업은 시작돼야 합니다.");
        Ensure(start.Status
               == ApplicationOperationStartStatus.Started,
            "첫 작업 상태는 Started여야 합니다.");
        ApplicationOperationLease lease = start.Lease
            ?? throw new InvalidOperationException(
                "Started 결과에는 lease가 필요합니다.");
        Ensure(start.Snapshot.IsBusy
               && start.Snapshot.OperationId == lease.OperationId
               && start.Snapshot.Kind
                   == ApplicationOperationKind.DownloadMeasurement,
            "시작 스냅샷에 활성 작업 ID와 종류가 필요합니다.");
        Ensure(!start.Snapshot.SupportsCancellation,
            "취소 callback이 없는 작업은 취소 불가여야 합니다.");
        Ensure(coordinator.IsBusy,
            "시작 뒤 coordinator가 busy여야 합니다.");
        Ensure(!lease.Completion.IsCompleted,
            "lease 완료 전 completion task가 끝나면 안 됩니다.");

        Ensure(lease.Complete(),
            "첫 lease 완료는 true여야 합니다.");
        Ensure(lease.IsCompleted,
            "완료한 lease 상태를 유지해야 합니다.");
        Ensure(lease.Completion.IsCompletedSuccessfully,
            "lease completion task가 성공 완료돼야 합니다.");
        Ensure(!coordinator.IsBusy
               && coordinator.Snapshot
                   == ApplicationOperationSnapshot.Idle(),
            "완료 뒤 coordinator는 idle이어야 합니다.");
        Ensure(events.Count == 2
               && events[0].IsBusy
               && !events[1].IsBusy,
            "시작과 완료 상태 변경을 순서대로 발행해야 합니다.");
        Ensure(!lease.Complete(),
            "같은 lease의 두 번째 완료는 false여야 합니다.");
    }

    private static void RejectsInvalidKinds()
    {
        ApplicationOperationCoordinator coordinator = new();
        EnsureThrows<ArgumentOutOfRangeException>(() =>
            coordinator.TryBegin(ApplicationOperationKind.None));
        EnsureThrows<ArgumentOutOfRangeException>(() =>
            coordinator.TryBegin((ApplicationOperationKind)999));
        Ensure(!coordinator.IsBusy,
            "잘못된 kind가 coordinator 상태를 변경하면 안 됩니다.");
    }

    private static void
        RejectsASecondOperationWithTheActiveSnapshot()
    {
        ApplicationOperationCoordinator coordinator = new();
        using ApplicationOperationLease first = coordinator.TryBegin(
                ApplicationOperationKind.BrowserObservation)
            .Lease
            ?? throw new InvalidOperationException(
                "첫 관찰 lease가 필요합니다.");

        ApplicationOperationStartResult second = coordinator.TryBegin(
            ApplicationOperationKind.RouteComparison);

        Ensure(!second.Started
               && second.Lease is null,
            "활성 작업 중 두 번째 lease를 만들면 안 됩니다.");
        Ensure(second.Status
               == ApplicationOperationStartStatus.Busy,
            "두 번째 시작 거부 사유는 Busy여야 합니다.");
        Ensure(second.Snapshot.OperationId == first.OperationId
               && second.Snapshot.Kind
                   == ApplicationOperationKind.BrowserObservation,
            "Busy 결과에는 현재 활성 작업 스냅샷이 필요합니다.");
    }

    private static void AllowsOnlyOneConcurrentStarter()
    {
        ApplicationOperationCoordinator coordinator = new();
        ApplicationOperationStartResult[] starts = new
            ApplicationOperationStartResult[64];

        Parallel.For(0, starts.Length, index =>
        {
            starts[index] = coordinator.TryBegin(
                ApplicationOperationKind.RouteEvidence);
        });

        ApplicationOperationStartResult[] winners = starts
            .Where(result => result.Started)
            .ToArray();
        Ensure(winners.Length == 1,
            $"동시 시작자는 정확히 한 개여야 합니다: {winners.Length}");
        Ensure(starts.Count(result =>
                result.Status
                    == ApplicationOperationStartStatus.Busy)
               == starts.Length - 1,
            "나머지 동시 시작자는 모두 Busy여야 합니다.");
        winners[0].Lease!.Dispose();
        Ensure(!coordinator.IsBusy,
            "승자 lease 완료 뒤 idle이어야 합니다.");
    }

    private static void RequestsCancellationAtMostOnce()
    {
        ApplicationOperationCoordinator coordinator = new();
        int callbackCount = 0;
        using ApplicationOperationLease lease = coordinator.TryBegin(
                ApplicationOperationKind.WindowsProxyImport,
                () => Interlocked.Increment(ref callbackCount))
            .Lease
            ?? throw new InvalidOperationException(
                "취소 가능한 lease가 필요합니다.");

        ApplicationOperationCancellationStatus[] results = new
            ApplicationOperationCancellationStatus[32];
        Parallel.For(0, results.Length, index =>
        {
            results[index] = lease.RequestCancellation();
        });

        Ensure(callbackCount == 1,
            "동시 취소 요청에서도 callback은 한 번만 실행돼야 합니다.");
        Ensure(results.Count(status => status
                == ApplicationOperationCancellationStatus.Requested)
               == 1,
            "취소 요청 성공은 한 번이어야 합니다.");
        Ensure(results.Count(status => status
                == ApplicationOperationCancellationStatus
                    .AlreadyRequested)
               == results.Length - 1,
            "후속 취소 요청은 AlreadyRequested여야 합니다.");
        Ensure(coordinator.Snapshot.CancellationRequested
               && coordinator.Snapshot.SupportsCancellation,
            "활성 스냅샷에 취소 요청 상태가 필요합니다.");
    }

    private static void
        DistinguishesUnsupportedAndInactiveCancellation()
    {
        ApplicationOperationCoordinator coordinator = new();
        Ensure(coordinator.RequestCancellation()
               == ApplicationOperationCancellationStatus.NotActive,
            "idle 상태 취소는 NotActive여야 합니다.");

        ApplicationOperationLease lease = coordinator.TryBegin(
                ApplicationOperationKind.DiagnosticReportSave)
            .Lease
            ?? throw new InvalidOperationException(
                "취소 불가 lease가 필요합니다.");
        Ensure(coordinator.RequestCancellation()
               == ApplicationOperationCancellationStatus.NotSupported,
            "callback 없는 활성 작업 취소는 NotSupported여야 합니다.");
        Ensure(!coordinator.Snapshot.CancellationRequested,
            "지원하지 않는 취소를 요청 상태로 기록하면 안 됩니다.");
        lease.Dispose();
        Ensure(lease.RequestCancellation()
               == ApplicationOperationCancellationStatus.NotActive,
            "완료한 lease 취소는 NotActive여야 합니다.");
    }

    private static void ContainsCancellationCallbackFailures()
    {
        ApplicationOperationCoordinator coordinator = new();
        using ApplicationOperationLease lease = coordinator.TryBegin(
                ApplicationOperationKind.RouteComparisonReportSave,
                () => throw new InvalidOperationException(
                    "secret callback failure"))
            .Lease
            ?? throw new InvalidOperationException(
                "취소 가능한 저장 lease가 필요합니다.");

        ApplicationOperationCancellationStatus result =
            lease.RequestCancellation();

        Ensure(result
               == ApplicationOperationCancellationStatus.CallbackFailed,
            "callback 예외는 CallbackFailed로 변환해야 합니다.");
        Ensure(coordinator.IsBusy,
            "callback 예외가 활성 작업을 임의 완료하면 안 됩니다.");
        Ensure(coordinator.Snapshot.CancellationRequested
               && coordinator.Snapshot.CancellationCallbackFailed,
            "취소 요청과 callback 실패를 구조화해야 합니다.");
        Ensure(lease.RequestCancellation()
               == ApplicationOperationCancellationStatus
                   .AlreadyRequested,
            "실패한 callback을 자동 재시도하면 안 됩니다.");
    }

    private static void StaleLeaseCannotCompleteANewerOperation()
    {
        ApplicationOperationCoordinator coordinator = new();
        ApplicationOperationLease first = coordinator.TryBegin(
                ApplicationOperationKind.DownloadMeasurement)
            .Lease
            ?? throw new InvalidOperationException(
                "첫 lease가 필요합니다.");
        Ensure(first.Complete(),
            "첫 lease를 완료해야 합니다.");

        ApplicationOperationLease second = coordinator.TryBegin(
                ApplicationOperationKind.BrowserObservation)
            .Lease
            ?? throw new InvalidOperationException(
                "두 번째 lease가 필요합니다.");
        Ensure(!first.Complete(),
            "stale lease를 다시 완료하면 false여야 합니다.");
        Ensure(coordinator.Snapshot.OperationId
               == second.OperationId
               && coordinator.Snapshot.Kind
                   == ApplicationOperationKind.BrowserObservation,
            "stale lease가 새 작업을 해제하면 안 됩니다.");
        second.Dispose();
    }

    private static void
        ShutdownBlocksNewStartsCancelsAndWaits()
    {
        ApplicationOperationCoordinator coordinator = new();
        int cancellationCount = 0;
        ApplicationOperationLease lease = coordinator.TryBegin(
                ApplicationOperationKind.RepeatedMeasurement,
                () => Interlocked.Increment(ref cancellationCount))
            .Lease
            ?? throw new InvalidOperationException(
                "종료 대기용 lease가 필요합니다.");

        Task<ApplicationOperationShutdownResult> shutdown =
            coordinator.RequestShutdownAsync();

        Ensure(cancellationCount == 1,
            "종료 요청이 활성 작업 취소 callback을 한 번 호출해야 합니다.");
        Ensure(!shutdown.IsCompleted,
            "활성 lease가 끝나기 전 종료 대기가 완료되면 안 됩니다.");
        Ensure(coordinator.ShutdownRequested
               && coordinator.Snapshot.ShutdownRequested,
            "종료 요청 상태를 유지해야 합니다.");
        ApplicationOperationStartResult rejected =
            coordinator.TryBegin(
                ApplicationOperationKind.NetworkEnvironmentCapture);
        Ensure(rejected.Status
               == ApplicationOperationStartStatus.ShutdownPending
               && rejected.Lease is null,
            "종료 대기 중 새 작업을 시작하면 안 됩니다.");

        lease.Dispose();
        ApplicationOperationShutdownResult completed = shutdown
            .GetAwaiter()
            .GetResult();
        Ensure(completed.CancellationStatus
               == ApplicationOperationCancellationStatus.Requested,
            "종료 요청의 취소 결과를 유지해야 합니다.");
        Ensure(!completed.FinalSnapshot.IsBusy
               && completed.FinalSnapshot.ShutdownRequested,
            "종료 대기 완료 뒤 idle·shutdown 상태여야 합니다.");
    }

    private static void
        ShutdownCanWaitWithoutRequestingCancellation()
    {
        ApplicationOperationCoordinator coordinator = new();
        int cancellationCount = 0;
        ApplicationOperationLease lease = coordinator.TryBegin(
                ApplicationOperationKind.RouteComparison,
                () => Interlocked.Increment(ref cancellationCount))
            .Lease
            ?? throw new InvalidOperationException(
                "종료 대기용 lease가 필요합니다.");

        Task<ApplicationOperationShutdownResult> shutdown =
            coordinator.RequestShutdownAsync(
                requestCancellation: false);

        Ensure(cancellationCount == 0,
            "취소 미요청 종료 대기는 callback을 호출하면 안 됩니다.");
        Ensure(!shutdown.IsCompleted,
            "lease 완료 전 종료 대기가 끝나면 안 됩니다.");
        lease.Dispose();
        ApplicationOperationShutdownResult result = shutdown
            .GetAwaiter()
            .GetResult();
        Ensure(result.CancellationStatus
               == ApplicationOperationCancellationStatus.NotActive,
            "취소를 요청하지 않은 종료 결과는 NotActive여야 합니다.");
    }

    private static void CanceledShutdownCanBeReopened()
    {
        ApplicationOperationCoordinator coordinator = new();
        ApplicationOperationShutdownResult shutdown = coordinator
            .RequestShutdownAsync()
            .GetAwaiter()
            .GetResult();
        Ensure(shutdown.FinalSnapshot.ShutdownRequested,
            "종료 요청 뒤 shutdown 상태여야 합니다.");
        Ensure(coordinator.TryBegin(
                ApplicationOperationKind.NetworkAdapterDiagnostics)
            .Status == ApplicationOperationStartStatus.ShutdownPending,
            "shutdown 상태에서 새 작업은 거부돼야 합니다.");

        Ensure(coordinator.CancelShutdownRequest(),
            "첫 종료 취소는 true여야 합니다.");
        Ensure(!coordinator.CancelShutdownRequest(),
            "이미 취소한 종료 요청의 두 번째 취소는 false여야 합니다.");
        using ApplicationOperationLease lease = coordinator.TryBegin(
                ApplicationOperationKind.NetworkAdapterDiagnostics)
            .Lease
            ?? throw new InvalidOperationException(
                "종료 요청 취소 뒤 새 작업을 시작할 수 있어야 합니다.");
        Ensure(!coordinator.ShutdownRequested,
            "종료 요청 취소 상태를 유지해야 합니다.");
    }

    private static void WaitForIdleHonorsCallerCancellation()
    {
        ApplicationOperationCoordinator coordinator = new();
        using ApplicationOperationLease lease = coordinator.TryBegin(
                ApplicationOperationKind.RouteEvidence)
            .Lease
            ?? throw new InvalidOperationException(
                "idle 대기용 lease가 필요합니다.");
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        EnsureThrows<OperationCanceledException>(() =>
            coordinator.WaitForIdleAsync(cancellation.Token)
                .GetAwaiter()
                .GetResult());
        Ensure(coordinator.IsBusy,
            "대기 호출자의 취소가 활성 작업을 완료하면 안 됩니다.");
    }

    private static void FaultyStateObserverCannotBreakTransitions()
    {
        ApplicationOperationCoordinator coordinator = new();
        int healthyObserverCount = 0;
        coordinator.StateChanged += (_, _) =>
            throw new InvalidOperationException(
                "observer secret failure");
        coordinator.StateChanged += (_, _) =>
            Interlocked.Increment(ref healthyObserverCount);

        using (ApplicationOperationLease lease = coordinator.TryBegin(
                   ApplicationOperationKind.ProxyRouteResolution)
               .Lease
               ?? throw new InvalidOperationException(
                   "관찰자 테스트 lease가 필요합니다."))
        {
            Ensure(coordinator.IsBusy,
                "실패한 observer가 시작 전이를 롤백하면 안 됩니다.");
        }

        Ensure(healthyObserverCount == 2,
            "한 observer 실패가 다른 observer의 시작·완료 알림을 막으면 안 됩니다.");
        Ensure(!coordinator.IsBusy,
            "observer 예외와 무관하게 완료돼야 합니다.");
    }

    private static void
        ConcurrentCancelAndCompleteRemainConsistent()
    {
        ApplicationOperationCoordinator coordinator = new();
        int cancellationCount = 0;
        ApplicationOperationLease lease = coordinator.TryBegin(
                ApplicationOperationKind.BrowserObservation,
                () => Interlocked.Increment(ref cancellationCount))
            .Lease
            ?? throw new InvalidOperationException(
                "경쟁 테스트 lease가 필요합니다.");

        Parallel.Invoke(
            () =>
            {
                for (int index = 0; index < 50; index++)
                {
                    lease.RequestCancellation();
                }
            },
            () =>
            {
                for (int index = 0; index < 50; index++)
                {
                    lease.Complete();
                }
            });

        Ensure(cancellationCount is 0 or 1,
            "취소·완료 경쟁에서도 callback은 최대 한 번이어야 합니다.");
        Ensure(!coordinator.IsBusy,
            "취소·완료 경쟁 뒤 coordinator가 idle이어야 합니다.");
        Ensure(lease.Completion.IsCompletedSuccessfully,
            "경쟁 뒤 completion task가 완료돼야 합니다.");
    }

    private static void
        SafeJsonDoesNotExposeCallbacksOrLeaseInternals()
    {
        const string secret =
            "https://secret-operation.example.invalid/token";
        ApplicationOperationCoordinator coordinator = new();
        ApplicationOperationStartResult start = coordinator.TryBegin(
            ApplicationOperationKind.WindowsProxyImport,
            () => GC.KeepAlive(secret));
        ApplicationOperationLease lease = start.Lease
            ?? throw new InvalidOperationException(
                "직렬화 테스트 lease가 필요합니다.");

        string startJson = JsonSerializer.Serialize(start);
        string snapshotJson = JsonSerializer.Serialize(
            coordinator.Snapshot);
        string leaseJson = JsonSerializer.Serialize(lease);
        string combined = startJson + snapshotJson + leaseJson;

        Ensure(!combined.Contains(
                secret,
                StringComparison.OrdinalIgnoreCase),
            "안전 상태 JSON에 callback closure 값이 남으면 안 됩니다.");
        Ensure(!startJson.Contains(
                "Lease",
                StringComparison.Ordinal),
            "start 결과 JSON에 lease 객체를 직렬화하면 안 됩니다.");
        Ensure(!leaseJson.Contains(
                "Completion",
                StringComparison.Ordinal),
            "lease JSON에 Task completion을 직렬화하면 안 됩니다.");
        Ensure(snapshotJson.Contains(
                "WindowsProxyImport",
                StringComparison.Ordinal)
               && snapshotJson.Contains(
                   "OperationId",
                   StringComparison.Ordinal),
            "안전 JSON에는 고정 작업 종류와 ID를 유지해야 합니다.");
        lease.Dispose();
    }

    private static void EnsureThrows<TException>(Action action)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }

        throw new InvalidOperationException(
            $"예상 예외 {typeof(TException).Name}가 발생하지 않았습니다.");
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
