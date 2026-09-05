using System.Runtime.CompilerServices;
using System.Text.Json;
using WlanLivePathTester.Core.NetworkEnvironment;
using WlanLivePathTester.Core.Proxy;
using WlanLivePathTester.Core.Routing;
using WlanLivePathTester.Windows.Routing;

namespace WlanLivePathTester.WindowsSmoke;

internal static class InternalProxyRouteComparisonCoordinatorV2Tests
{
    private const string WlanInterfaceId =
        "11B2C3D4-E5F6-47A8-9123-1234567890AB";
    private const string AlternateInterfaceId =
        "22B2C3D4-E5F6-47A8-9123-1234567890AB";
    private const string InternalTarget =
        "internal-sensitive.example.invalid";
    private const string ProxyHost =
        "proxy-sensitive.example.invalid";
    private static readonly Uri ExternalTarget = new(
        "https://download.example.invalid/file.bin");
    private static readonly DateTimeOffset FixedTime =
        DateTimeOffset.UnixEpoch.AddDays(20);

#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Run()
    {
        InvalidAndDirectInputsPerformNoRouteReads();
        BlockedAndUnavailableSourcesPerformNoRouteReads();
        PreCanceledExecutionPerformsNoRouteReads();
        InternalFailureStopsBeforeProxyRouteAnalysis();
        SameExactInterfaceProducesReadyComparison();
        DifferentExactInterfaceProducesDivergedComparison();
        PartialProxyAnalysisCompletesWithIncompleteComparison();
        SafeResultDoesNotSerializeRawInputsOrEvidence();
        InvalidDnsTimeoutPerformsNoRouteReads();
        Console.WriteLine(
            "PASS current internal and proxy route comparison coordinator v2 tests");
    }

    private static void InvalidAndDirectInputsPerformNoRouteReads()
    {
        RecordingInternalReader internalReader = new(
            SuccessRoute(
                "internal",
                WlanInterfaceId,
                NetworkAdapterCategory.Wireless,
                RouteProbePurpose.InternalDirectTarget));
        RecordingProxyReader proxyReader = new((_, _, _, _) =>
            throw new InvalidOperationException(
                "zero-read 입력에서 프록시 reader를 호출하면 안 됩니다."));
        InternalProxyRouteComparisonCoordinator coordinator =
            CreateCoordinator(internalReader, proxyReader);

        InternalProxyRouteComparisonRunResult wrongScheme =
            coordinator.RunManualDirectiveAsync(
                    InternalTarget,
                    $"PROXY {ProxyHost}:8080",
                    new Uri("ftp://download.example.invalid/file.bin"),
                    WlanInterfaceId)
                .GetAwaiter()
                .GetResult();
        InternalProxyRouteComparisonRunResult noApplicable =
            coordinator.RunManualDirectiveAsync(
                    InternalTarget,
                    $"http={ProxyHost}:8080",
                    ExternalTarget,
                    WlanInterfaceId)
                .GetAwaiter()
                .GetResult();
        InternalProxyRouteComparisonRunResult directFirst =
            coordinator.RunManualDirectiveAsync(
                    InternalTarget,
                    $"DIRECT; PROXY {ProxyHost}:8080",
                    ExternalTarget,
                    WlanInterfaceId)
                .GetAwaiter()
                .GetResult();

        Ensure(wrongScheme.Status
               == InternalProxyRouteComparisonRunStatus.InvalidInput,
            "HTTP·HTTPS가 아닌 외부 대상은 InvalidInput이어야 합니다.");
        Ensure(noApplicable.Status
               == InternalProxyRouteComparisonRunStatus.InvalidInput,
            "HTTPS 대상에 적용되지 않는 http= 항목은 InvalidInput이어야 합니다.");
        Ensure(directFirst.Status
               == InternalProxyRouteComparisonRunStatus.DirectPathSelected
               && directFirst.DirectIsPrimary,
            "DIRECT가 먼저면 DirectPathSelected여야 합니다.");
        Ensure(internalReader.CallCount == 0
               && proxyReader.CallCount == 0,
            "잘못된 입력과 DIRECT 우선에서는 모든 route reader 호출이 0회여야 합니다.");
    }

    private static void
        BlockedAndUnavailableSourcesPerformNoRouteReads()
    {
        RecordingInternalReader internalReader = new(
            SuccessRoute(
                "internal",
                WlanInterfaceId,
                NetworkAdapterCategory.Wireless,
                RouteProbePurpose.InternalDirectTarget));
        RecordingProxyReader proxyReader = new((_, _, _, _) =>
            throw new InvalidOperationException(
                "차단된 출처에서 프록시 reader를 호출하면 안 됩니다."));
        InternalProxyRouteComparisonCoordinator coordinator =
            CreateCoordinator(internalReader, proxyReader);
        ProxyDirectiveSourceSelectionResult blocked =
            ProxyDirectiveSourceSelectionPolicy.Select(
                targetDecisionWasEvaluated: true,
                targetDecisionIsDirect: false,
                targetSpecificDirective: "DIRECT",
                manualProxyConfigured: true,
                manualProxyDirective:
                    $"PROXY {ProxyHost}:8080");
        ProxyDirectiveSourceSelectionResult unavailable =
            ProxyDirectiveSourceSelectionPolicy.Select(
                targetDecisionWasEvaluated: false,
                targetDecisionIsDirect: false,
                targetSpecificDirective: null,
                manualProxyConfigured: false,
                manualProxyDirective: null);

        InternalProxyRouteComparisonRunResult blockedResult =
            coordinator.RunAsync(
                    InternalTarget,
                    blocked,
                    ExternalTarget,
                    WlanInterfaceId)
                .GetAwaiter()
                .GetResult();
        InternalProxyRouteComparisonRunResult unavailableResult =
            coordinator.RunAsync(
                    InternalTarget,
                    unavailable,
                    ExternalTarget,
                    WlanInterfaceId)
                .GetAwaiter()
                .GetResult();

        Ensure(blockedResult.Status
               == InternalProxyRouteComparisonRunStatus
                   .ProxySourceBlocked,
            "모순된 대상별 판정은 ProxySourceBlocked여야 합니다.");
        Ensure(unavailableResult.Status
               == InternalProxyRouteComparisonRunStatus
                   .ProxySourceUnavailable,
            "프록시 출처가 없으면 ProxySourceUnavailable이어야 합니다.");
        Ensure(internalReader.CallCount == 0
               && proxyReader.CallCount == 0,
            "Blocked·Unavailable에서는 모든 route reader 호출이 0회여야 합니다.");
    }

    private static void PreCanceledExecutionPerformsNoRouteReads()
    {
        RecordingInternalReader internalReader = new(
            SuccessRoute(
                "internal",
                WlanInterfaceId,
                NetworkAdapterCategory.Wireless,
                RouteProbePurpose.InternalDirectTarget));
        RecordingProxyReader proxyReader = new((_, _, _, _) =>
            throw new InvalidOperationException(
                "사전 취소에서 프록시 reader를 호출하면 안 됩니다."));
        InternalProxyRouteComparisonCoordinator coordinator =
            CreateCoordinator(internalReader, proxyReader);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        InternalProxyRouteComparisonRunResult result =
            coordinator.RunManualDirectiveAsync(
                    InternalTarget,
                    $"PROXY {ProxyHost}:8080",
                    ExternalTarget,
                    WlanInterfaceId,
                    cancellationToken: cancellation.Token)
                .GetAwaiter()
                .GetResult();

        Ensure(result.Status
               == InternalProxyRouteComparisonRunStatus.Canceled,
            "사전 취소는 Canceled여야 합니다.");
        Ensure(internalReader.CallCount == 0
               && proxyReader.CallCount == 0,
            "사전 취소에서는 모든 route reader 호출이 0회여야 합니다.");
    }

    private static void InternalFailureStopsBeforeProxyRouteAnalysis()
    {
        RecordingInternalReader internalReader = new(
            FailedRoute(
                "internal",
                DestinationRouteEvidenceStatus.RouteNotFound,
                RouteProbePurpose.InternalDirectTarget));
        RecordingProxyReader proxyReader = new((_, _, _, _) =>
            throw new InvalidOperationException(
                "내부 경로 실패 뒤 프록시 reader를 호출하면 안 됩니다."));
        InternalProxyRouteComparisonCoordinator coordinator =
            CreateCoordinator(internalReader, proxyReader);

        InternalProxyRouteComparisonRunResult result =
            coordinator.RunManualDirectiveAsync(
                    InternalTarget,
                    $"PROXY {ProxyHost}:8080",
                    ExternalTarget,
                    WlanInterfaceId)
                .GetAwaiter()
                .GetResult();

        Ensure(result.Status
               == InternalProxyRouteComparisonRunStatus
                   .InternalRouteUnavailable,
            "내부 경로 실패는 InternalRouteUnavailable이어야 합니다.");
        Ensure(result.InternalRouteReadPerformed
               && !result.ProxyRouteAnalysisPerformed,
            "내부 reader만 실행됐다는 경계를 유지해야 합니다.");
        Ensure(internalReader.CallCount == 1
               && proxyReader.CallCount == 0,
            "내부 경로가 비교 불가능하면 프록시 DNS·route 분석을 시작하면 안 됩니다.");
    }

    private static void SameExactInterfaceProducesReadyComparison()
    {
        RecordingInternalReader internalReader = new(
            SuccessRoute(
                "internal",
                WlanInterfaceId,
                NetworkAdapterCategory.Wireless,
                RouteProbePurpose.InternalDirectTarget));
        RecordingProxyReader proxyReader = new((_, label, _, _) =>
            Task.FromResult(SuccessRoute(
                label,
                "{" + WlanInterfaceId.ToLowerInvariant() + "}",
                NetworkAdapterCategory.Wireless,
                RouteProbePurpose.ProxyEndpoint)));
        InternalProxyRouteComparisonCoordinator coordinator =
            CreateCoordinator(internalReader, proxyReader);

        InternalProxyRouteComparisonRunResult result =
            coordinator.RunManualDirectiveAsync(
                    InternalTarget,
                    $"PROXY {ProxyHost}:8080; DIRECT",
                    ExternalTarget,
                    WlanInterfaceId)
                .GetAwaiter()
                .GetResult();

        Ensure(result.Status
               == InternalProxyRouteComparisonRunStatus.Completed,
            "정상 실행은 Completed여야 합니다.");
        Ensure(result.Comparison?.Status
               == InternalProxyRouteComparisonStatus.Ready
               && result.Comparison.Relation
                   == InternalProxyRouteRelation.SameInterface,
            "같은 전체 GUID는 Ready·SameInterface여야 합니다.");
        Ensure(result.Comparison.ExactIdentityComparisonPerformed,
            "Ready는 전체 GUID 정확 비교를 수행해야 합니다.");
        Ensure(result.ProxyExecutionStatus
               == ProxyDirectiveRouteAnalysisExecutionStatus.Completed
               && result.ProxyRouteStatus
                   == ProxyEndpointRouteAnalysisStatus.Success,
            "프록시 실행과 경로 분석 상태를 분리해 유지해야 합니다.");
        Ensure(result.ParsedProxyEndpointCount == 1
               && result.AnalyzedProxyEndpointCount == 1
               && result.SuccessfulProxyEndpointCount == 1
               && result.DirectFallback,
            "프록시 후보·성공 수와 DIRECT fallback을 유지해야 합니다.");
        Ensure(internalReader.CallCount == 1
               && proxyReader.CallCount == 1,
            "내부와 적용 프록시 후보를 각각 한 번 확인해야 합니다.");
    }

    private static void
        DifferentExactInterfaceProducesDivergedComparison()
    {
        RecordingInternalReader internalReader = new(
            SuccessRoute(
                "internal",
                WlanInterfaceId,
                NetworkAdapterCategory.Wireless,
                RouteProbePurpose.InternalDirectTarget));
        RecordingProxyReader proxyReader = new((_, label, _, _) =>
            Task.FromResult(SuccessRoute(
                label,
                AlternateInterfaceId,
                NetworkAdapterCategory.Tunnel,
                RouteProbePurpose.ProxyEndpoint)));
        InternalProxyRouteComparisonCoordinator coordinator =
            CreateCoordinator(internalReader, proxyReader);

        InternalProxyRouteComparisonRunResult result =
            coordinator.RunManualDirectiveAsync(
                    InternalTarget,
                    $"PROXY {ProxyHost}:8080",
                    ExternalTarget,
                    WlanInterfaceId)
                .GetAwaiter()
                .GetResult();

        Ensure(result.Status
               == InternalProxyRouteComparisonRunStatus.Completed,
            "경로 분리도 실행 자체는 Completed여야 합니다.");
        Ensure(result.Comparison?.Status
               == InternalProxyRouteComparisonStatus.Diverged
               && result.Comparison.Relation
                   == InternalProxyRouteRelation.DifferentInterface,
            "다른 전체 GUID는 Diverged·DifferentInterface여야 합니다.");
        Ensure(result.Comparison.ExactIdentityComparisonPerformed,
            "Diverged도 전체 GUID 정확 비교 결과여야 합니다.");
    }

    private static void
        PartialProxyAnalysisCompletesWithIncompleteComparison()
    {
        RecordingInternalReader internalReader = new(
            SuccessRoute(
                "internal",
                WlanInterfaceId,
                NetworkAdapterCategory.Wireless,
                RouteProbePurpose.InternalDirectTarget));
        int proxyCall = 0;
        RecordingProxyReader proxyReader = new((_, label, _, _) =>
        {
            proxyCall++;
            return Task.FromResult(proxyCall == 1
                ? SuccessRoute(
                    label,
                    WlanInterfaceId,
                    NetworkAdapterCategory.Wireless,
                    RouteProbePurpose.ProxyEndpoint)
                : FailedRoute(
                    label,
                    DestinationRouteEvidenceStatus.RouteNotFound,
                    RouteProbePurpose.ProxyEndpoint));
        });
        InternalProxyRouteComparisonCoordinator coordinator =
            CreateCoordinator(internalReader, proxyReader);

        InternalProxyRouteComparisonRunResult result =
            coordinator.RunManualDirectiveAsync(
                    InternalTarget,
                    $"PROXY first.{ProxyHost}:8080; PROXY second.{ProxyHost}:8080",
                    ExternalTarget,
                    WlanInterfaceId)
                .GetAwaiter()
                .GetResult();

        Ensure(result.Status
               == InternalProxyRouteComparisonRunStatus.Completed,
            "프록시 부분 실패라도 실행 파이프라인이 종료되면 Completed여야 합니다.");
        Ensure(result.ProxyRouteStatus
               == ProxyEndpointRouteAnalysisStatus.PartialSuccess,
            "한 프록시 후보 실패는 PartialSuccess여야 합니다.");
        Ensure(result.Comparison?.Status
               == InternalProxyRouteComparisonStatus.Incomplete,
            "부분 프록시 근거는 Incomplete 비교여야 합니다.");
        Ensure(result.AnalyzedProxyEndpointCount == 2
               && result.SuccessfulProxyEndpointCount == 1,
            "분석·성공 후보 수를 정확히 유지해야 합니다.");
    }

    private static void SafeResultDoesNotSerializeRawInputsOrEvidence()
    {
        const string internalDescription =
            "Corporate Internal Secret Adapter";
        const string proxyDescription =
            "Corporate Proxy Secret Tunnel";
        RecordingInternalReader internalReader = new(
            SuccessRoute(
                InternalTarget,
                WlanInterfaceId,
                NetworkAdapterCategory.Wireless,
                RouteProbePurpose.InternalDirectTarget,
                internalDescription));
        RecordingProxyReader proxyReader = new((_, label, _, _) =>
            Task.FromResult(SuccessRoute(
                label,
                AlternateInterfaceId,
                NetworkAdapterCategory.Tunnel,
                RouteProbePurpose.ProxyEndpoint,
                proxyDescription)));
        InternalProxyRouteComparisonCoordinator coordinator =
            CreateCoordinator(internalReader, proxyReader);

        InternalProxyRouteComparisonRunResult result =
            coordinator.RunManualDirectiveAsync(
                    InternalTarget,
                    $"PROXY {ProxyHost}:8080; DIRECT",
                    ExternalTarget,
                    WlanInterfaceId)
                .GetAwaiter()
                .GetResult();
        string json = JsonSerializer.Serialize(result);

        string[] forbidden =
        [
            InternalTarget,
            ProxyHost,
            ExternalTarget.Host,
            WlanInterfaceId,
            AlternateInterfaceId,
            internalDescription,
            proxyDescription,
            "InternalRouteEvidence",
            "ProxyExecution",
            "SelectedInterfaceIdentity"
        ];
        foreach (string secret in forbidden)
        {
            Ensure(!json.Contains(
                    secret,
                    StringComparison.OrdinalIgnoreCase),
                $"실행 결과 JSON에 원문 입력·근거가 남았습니다: {secret}");
        }

        Ensure(result.InternalRouteEvidence is not null
               && result.ProxyExecution?.Analysis is not null,
            "같은 프로세스에서는 후속 보고서용 원본 근거를 메모리에 유지해야 합니다.");
        Ensure(json.Contains(
                result.Comparison!.InternalInterfaceFingerprint!,
                StringComparison.Ordinal),
            "공개 결과에는 비가역 인터페이스 지문을 유지할 수 있습니다.");
    }

    private static void InvalidDnsTimeoutPerformsNoRouteReads()
    {
        RecordingInternalReader internalReader = new(
            SuccessRoute(
                "internal",
                WlanInterfaceId,
                NetworkAdapterCategory.Wireless,
                RouteProbePurpose.InternalDirectTarget));
        RecordingProxyReader proxyReader = new((_, _, _, _) =>
            throw new InvalidOperationException(
                "잘못된 timeout에서 호출하면 안 됩니다."));
        InternalProxyRouteComparisonCoordinator coordinator =
            CreateCoordinator(internalReader, proxyReader);

        EnsureThrows<ArgumentOutOfRangeException>(() =>
            coordinator.RunManualDirectiveAsync(
                    InternalTarget,
                    $"PROXY {ProxyHost}:8080",
                    ExternalTarget,
                    WlanInterfaceId,
                    dnsTimeoutSeconds: 0)
                .GetAwaiter()
                .GetResult());
        Ensure(internalReader.CallCount == 0
               && proxyReader.CallCount == 0,
            "잘못된 DNS timeout에서는 모든 reader 호출이 0회여야 합니다.");
    }

    private static InternalProxyRouteComparisonCoordinator
        CreateCoordinator(
            RecordingInternalReader internalReader,
            RecordingProxyReader proxyReader)
    {
        ProxyEndpointRouteAnalyzer endpointAnalyzer = new(proxyReader);
        ProxyDirectiveRouteAnalysisCoordinator proxyCoordinator = new(
            endpointAnalyzer);
        return new InternalProxyRouteComparisonCoordinator(
            internalReader,
            proxyCoordinator,
            new FixedTimeProvider(FixedTime));
    }

    private static DestinationRouteEvidence SuccessRoute(
        string targetLabel,
        string interfaceId,
        NetworkAdapterCategory category,
        RouteProbePurpose purpose,
        string description = "Synthetic Adapter")
    {
        RouteInterfaceDescriptor selected = new(
            InterfaceIdentity: interfaceId,
            DisplayName: description,
            Description: description,
            NativeInterfaceType: category.ToString(),
            Category: category,
            OperationalState: NetworkAdapterOperationalState.Up,
            HasDefaultGateway: true,
            IsVirtual: category == NetworkAdapterCategory.Tunnel,
            IsVpn: category == NetworkAdapterCategory.Tunnel);
        return new DestinationRouteEvidence(
            CapturedAt: FixedTime,
            TargetLabel: targetLabel,
            Purpose: purpose,
            DnsWasUsed: true,
            ResolvedAddressCount: 1,
            Status: DestinationRouteEvidenceStatus.Success,
            SelectedInterface: selected,
            AddressEvidence:
            [
                new RouteAddressEvidence(
                    RouteAddressFamilyKind.IPv4,
                    RouteAddressEvidenceStatus.Success,
                    selected,
                    NativeErrorCode: null,
                    Message: "합성 Windows 최적 경로")
            ],
            Warnings: Array.Empty<string>(),
            Message: "합성 경로 성공");
    }

    private static DestinationRouteEvidence FailedRoute(
        string targetLabel,
        DestinationRouteEvidenceStatus status,
        RouteProbePurpose purpose) =>
        new(
            CapturedAt: FixedTime,
            TargetLabel: targetLabel,
            Purpose: purpose,
            DnsWasUsed: true,
            ResolvedAddressCount: 0,
            Status: status,
            SelectedInterface: null,
            AddressEvidence: Array.Empty<RouteAddressEvidence>(),
            Warnings: Array.Empty<string>(),
            Message: "합성 경로 실패");

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

    private sealed class RecordingInternalReader
        : IInternalDirectRouteEvidenceReader
    {
        private readonly DestinationRouteEvidence _result;

        public RecordingInternalReader(
            DestinationRouteEvidence result)
        {
            _result = result;
        }

        public int CallCount { get; private set; }

        public Task<DestinationRouteEvidence> ReadAsync(
            string target,
            string safeLabel,
            int dnsTimeoutSeconds,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            return Task.FromResult(_result);
        }
    }

    private sealed class RecordingProxyReader
        : IProxyEndpointRouteEvidenceReader
    {
        private readonly Func<string,
            string,
            int,
            CancellationToken,
            Task<DestinationRouteEvidence>> _handler;

        public RecordingProxyReader(
            Func<string,
                string,
                int,
                CancellationToken,
                Task<DestinationRouteEvidence>> handler)
        {
            _handler = handler;
        }

        public int CallCount { get; private set; }

        public Task<DestinationRouteEvidence> ReadAsync(
            string host,
            string safeLabel,
            int dnsTimeoutSeconds,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            return _handler(
                host,
                safeLabel,
                dnsTimeoutSeconds,
                cancellationToken);
        }
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _value;

        public FixedTimeProvider(DateTimeOffset value)
        {
            _value = value;
        }

        public override DateTimeOffset GetUtcNow() => _value;
    }
}
