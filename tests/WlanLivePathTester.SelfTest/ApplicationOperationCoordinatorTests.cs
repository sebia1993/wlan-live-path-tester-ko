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
        StartsCompletesAndPublishesState();
        RejectsInvalidAndConcurrentStarts();
        AllowsOnlyOneConcurrentStarter();
        RequestsCancellationAtMostOnce();
        DistinguishesUnsupportedInactiveAndFailedCancellation();
        StaleLeaseCannotCompleteANewerOperation();
        ShutdownCancelsBlocksAndWaits();
        ShutdownCanWaitWithoutCancelAndCanBeReopened();
        WaitForIdleHonorsCallerCancellation();
        FaultyObserverCannotBreakTransitions();
        ConcurrentCancelAndCompleteRemainConsistent();
        SafeJsonDoesNotExposeCallbacksOrLeaseInternals();
        Console.WriteLine(
            "PASS exclusive application operation coordinator tests");
    }

    private static void StartsCompletesAndPublishesState()
    {
        ApplicationOperationCoordinator coordinator = new();
        List<ApplicationOperationSnapshot> events = [];
        coordinator.StateChanged += (_, args) =>
            events.Add(args.Snapshot);

        ApplicationOperationStartResult start = coordinator.TryBegin(
            ApplicationOperationKind.DownloadMeasurement);
        ApplicationOperationLease lease = start.Lease
            ?? throw new InvalidOperationException(
                "Started 결과에는 lease가 필요합니다.");

        Ensure(start.Started
               && start.Status
                   == ApplicationOperationStartStatus.Started,
            "첫 작업은 Started여야 합니다.");
        Ensure(start.Snapshot.IsBusy
               && start.Snapshot.OperationId == lease.OperationId
               && start.Snapshot.Kind
                   == ApplicationOperationKind.DownloadMeasurement,
            "시작 스냅샷에 활성 작업 ID와 종류가 필요합니다.");
        Ensure(!start.Snapshot.SupportsCancellation
               && !lease.Completion.IsCompleted,
            "callback 없는 lease는 취소 불가이며 완료 전 task가 끝나면 안 됩니다.");

        Ensure(lease.Complete()
               && lease.IsCompleted
               && lease.Completion.IsCompletedSuccessfully,
            "첫 완료가 coordinator와 completion task를 끝내야 합니다.");
        Ensure(!coordinator.IsBusy
               && coordinator.Snapshot
                   == ApplicationOperationSnapshot.Idle(),
            "완료 뒤 coordinator는 idle이어야 합니다.");
        Ensure(events.Count == 2
               && events[0].IsBusy
               && !events[1].IsBusy,
            "시작과 완료 상태를 순서대로 발행해야 합니다.");
        Ensure(!lease.Complete(),
            "같은 lease의 중복 완료는 false여야 합니다.");
    }

    private static void RejectsInvalidAndConcurrentStarts()
    {
        ApplicationOperationCoordinator coordinator = new();
        EnsureThrows<ArgumentOutOfRangeException>(() =>
            coordinator.TryBegin(ApplicationOperationKind.None));
        EnsureThrows<ArgumentOutOfRangeException>(() =>
            coordinator.TryBegin((ApplicationOperationKind)999));

        using ApplicationOperationLease first = coordinator.TryBegin(
                ApplicationOperationKind.BrowserObservation)
            .Lease
            ?? throw new InvalidOperationException(
                "첫 관찰 lease가 필요합니다.");
        ApplicationOperationStartResult second = coordinator.TryBegin(
            ApplicationOperationKind.RouteComparison);

        Ensure(!second.Started
               && second.Lease is null
               && second.Status
                   == ApplicationOperationStartStatus.Busy,
            "활성 작업 중 두 번째 lease를 만들면 안 됩니다.");
        Ensure(second.Snapshot.OperationId == first.OperationId
               && second.Snapshot.Kind
                   == ApplicationOperationKind.BrowserObservation,
            "Busy 결과에는 현재 활성 작업 스냅샷이 필요합니다.");
    }

    private static void AllowsOnlyOneConcurrentStarter()
    {
        ApplicationOperationCoordinator coordinator = new();
        ApplicationOperationStartResult?[] starts = new
            ApplicationOperationStartResult?[64];

        Parallel.For(0, starts.Length, index =>
        {
            starts[index] = coordinator.TryBegin(
                ApplicationOperationKind.RouteEvidence);
        });

        ApplicationOperationStartResult[] completed = starts
            .Select(result => result
                ?? throw new InvalidOperationException(
                    "동시 시작 결과가 누락됐습니다."))
            .ToArray();
        ApplicationOperationStartResult[] winners = completed
            .Where(result => result.Started)
            .ToArray();
        Ensure(winners.Length == 1,
            $"동시 시작자는 정확히 한 개여야 합니다: {winners.Length}");
        Ensure(completed.Count(result => result.Status
                    == ApplicationOperationStartStatus.Busy)
               == completed.Length - 1,
            "나머지 동시 시작자는 모두 Busy여야 합니다.");
        winners[0].Lease!.Dispose();
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
            "스냅샷에 취소 요청 상태가 필요합니다.");
    }

    private static void
        DistinguishesUnsupportedInactiveAndFailedCancellation()
    {
        ApplicationOperationCoordinator idleCoordinator = new();
        Ensure(idleCoordinator.RequestCancellation()
               == ApplicationOperationCancellationStatus.NotActive,
            "idle 취소는 NotActive여야 합니다.");

        ApplicationOperationCoordinator unsupportedCoordinator = new();
        ApplicationOperationLease unsupported = unsupportedCoordinator
            .TryBegin(ApplicationOperationKind.DiagnosticReportSave)
            .Lease
            ?? throw new InvalidOperationException(
                "취소 불가 lease가 필요합니다.");
        Ensure(unsupportedCoordinator.RequestCancellation()
               == ApplicationOperationCancellationStatus.NotSupported,
            "callback 없는 작업은 NotSupported여야 합니다.");
        Ensure(!unsupportedCoordinator.Snapshot.CancellationRequested,
            "미지원 취소를 요청 상태로 기록하면 안 됩니다.");
        unsupported.Dispose();
        Ensure(unsupported.RequestCancellation()
               == ApplicationOperationCancellationStatus.NotActive,
            "완료 lease 취소는 NotActive여야 합니다.");

        ApplicationOperationCoordinator failedCoordinator = new();
        using ApplicationOperationLease failed = failedCoordinator
            .TryBegin(
                ApplicationOperationKind.RouteComparisonReportSave,
                () => throw new InvalidOperationException(
                    "secret callback failure"))
            .Lease
            ?? throw new InvalidOperationException(
                "취소 가능한 저장 lease가 필요합니다.");
        Ensure(failed.RequestCancellation()
               == ApplicationOperationCancellationStatus.CallbackFailed,
            "callback 예외는 CallbackFailed로 변환해야 합니다.");
        Ensure(failedCoordinator.IsBusy
               && failedCoordinator.Snapshot.CancellationRequested
               && failedCoordinator.Snapshot
                   .CancellationCallbackFailed,
            "callback 실패를 구조화하되 작업은 실제 완료까지 유지해야 합니다.");
        Ensure(failed.RequestCancellation()
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
            "stale lease 중복 완료는 false여야 합니다.");
        Ensure(coordinator.Snapshot.OperationId
               == second.OperationId
               && coordinator.Snapshot.Kind
                   == ApplicationOperationKind.BrowserObservation,
            "stale lease가 새 작업을 해제하면 안 됩니다.");
        second.Dispose();
    }

    private static void ShutdownCancelsBlocksAndWaits()
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

        Ensure(cancellationCount == 1
               && !shutdown.IsCompleted,
            "종료는 취소를 한 번 요청하고 실제 완료까지 기다려야 합니다.");
        Ensure(coordinator.ShutdownRequested,
            "종료 요청 상태를 유지해야 합니다.");
        Ensure(coordinator.TryBegin(
                ApplicationOperationKind.NetworkEnvironmentCapture)
            .Status == ApplicationOperationStartStatus.ShutdownPending,
            "종료 대기 중 새 작업을 시작하면 안 됩니다.");

        lease.Dispose();
        ApplicationOperationShutdownResult completed = shutdown
            .GetAwaiter()
            .GetResult();
        Ensure(completed.CancellationStatus
               == ApplicationOperationCancellationStatus.Requested,
            "종료 취소 결과를 유지해야 합니다.");
        Ensure(!completed.FinalSnapshot.IsBusy
               && completed.FinalSnapshot.ShutdownRequested,
            "종료 대기 완료 뒤 idle·shutdown 상태여야 합니다.");
    }

    private static void
        ShutdownCanWaitWithoutCancelAndCanBeReopened()
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

        Ensure(cancellationCount == 0
               && !shutdown.IsCompleted,
            "취소 미요청 종료는 callback 없이 자연 완료를 기다려야 합니다.");
        lease.Dispose();
        Ensure(shutdown.GetAwaiter().GetResult().CancellationStatus
               == ApplicationOperationCancellationStatus.NotActive,
            "취소하지 않은 종료 결과는 NotActive여야 합니다.");
        Ensure(coordinator.CancelShutdownRequest()
               && !coordinator.CancelShutdownRequest(),
            "종료 요청은 한 번만 해제돼야 합니다.");
        using ApplicationOperationLease reopened = coordinator.TryBegin(
                ApplicationOperationKind.NetworkAdapterDiagnostics)
            .Lease
            ?? throw new InvalidOperationException(
                "종료 해제 뒤 새 작업을 시작할 수 있어야 합니다.");
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

    private static void FaultyObserverCannotBreakTransitions()
    {
        ApplicationOperationCoordinator coordinator = new();
        int healthyCount = 0;
        coordinator.StateChanged += (_, _) =>
            throw new InvalidOperationException(
                "observer secret failure");
        coordinator.StateChanged += (_, _) =>
            Interlocked.Increment(ref healthyCount);

        using (ApplicationOperationLease lease = coordinator.TryBegin(
                   ApplicationOperationKind.ProxyRouteResolution)
               .Lease
               ?? throw new InvalidOperationException(
                   "observer 테스트 lease가 필요합니다."))
        {
            Ensure(coordinator.IsBusy,
                "observer 실패가 시작 전이를 롤백하면 안 됩니다.");
        }

        Ensure(healthyCount == 2
               && !coordinator.IsBusy,
            "한 observer 실패가 다른 알림 또는 완료를 막으면 안 됩니다.");
    }

    private static void ConcurrentCancelAndCompleteRemainConsistent()
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
        Ensure(!coordinator.IsBusy
               && lease.Completion.IsCompletedSuccessfully,
            "취소·완료 경쟁 뒤 idle과 completion을 보장해야 합니다.");
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
            "lease JSON에 completion Task를 직렬화하면 안 됩니다.");

        using JsonDocument parsed = JsonDocument.Parse(snapshotJson);
        JsonElement root = parsed.RootElement;
        Ensure(root.GetProperty("Kind").GetInt32()
               == (int)ApplicationOperationKind.WindowsProxyImport,
            "기본 JSON의 숫자 enum 값이 활성 작업 종류와 일치해야 합니다.");
        Ensure(root.GetProperty("OperationId").GetInt64()
               == lease.OperationId,
            "안전 JSON에는 활성 작업 ID가 필요합니다.");
        Ensure(root.GetProperty("SupportsCancellation").GetBoolean(),
            "안전 JSON에는 취소 지원 여부가 필요합니다.");
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
