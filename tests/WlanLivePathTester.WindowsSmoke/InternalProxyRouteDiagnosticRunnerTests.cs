using System.Runtime.CompilerServices;
using System.Text.Json;
using WlanLivePathTester.Core.NetworkEnvironment;
using WlanLivePathTester.Core.Proxy;
using WlanLivePathTester.Core.Routing;
using WlanLivePathTester.Windows.Routing;

namespace WlanLivePathTester.WindowsSmoke;

internal static class InternalProxyRouteDiagnosticRunnerTests
{
    private const string WlanInterfaceId =
        "F3B2C3D4-E5F6-47A8-9123-1234567890AB";
    private const string TunnelInterfaceId =
        "04B2C3D4-E5F6-47A8-9123-1234567890AB";
    private const string InternalTarget =
        "internal-private.example.invalid";
    private const string ProxyHost =
        "proxy-private.example.invalid";

#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Run()
    {
        SameWirelessInterfaceCompletesAsReady();
        DifferentTunnelInterfaceCompletesAsDiverged();
        DirectBlockedUnavailableAndPreCanceledReadNothing();
        InternalCancellationPreventsProxyReads();
        ProxyRouteFailureCompletesWithIncompleteComparison();
        DefaultJsonExcludesTargetsAndRawEvidence();
        Console.WriteLine(
            "PASS internal and proxy local route diagnostic runner tests");
    }

    private static void SameWirelessInterfaceCompletesAsReady()
    {
        RecordingInternalReader internalReader = new(
            _ => SuccessEvidence(
                RouteProbePurpose.InternalDirectTarget,
                WlanInterfaceId,
                NetworkAdapterCategory.Wireless));
        RecordingProxyReader proxyReader = new(
            _ => SuccessEvidence(
                RouteProbePurpose.ProxyEndpoint,
                WlanInterfaceId,
                NetworkAdapterCategory.Wireless));
        InternalProxyRouteDiagnosticRunner runner = CreateRunner(
            internalReader,
            proxyReader);

        InternalProxyRouteDiagnosticRunResult result = runner.RunAsync(
                InternalTarget,
                new Uri("https://download.example.invalid/file.bin"),
                TargetProxySnapshot(),
                WlanInterfaceId,
                dnsTimeoutSeconds: 2)
            .GetAwaiter()
            .GetResult();

        Ensure(result.Status
               == InternalProxyRouteDiagnosticRunStatus.Completed,
            "정상 내부·프록시 경로는 Completed여야 합니다.");
        Ensure(result.Comparison?.Status
               == InternalProxyRouteComparisonStatus.Ready,
            "같은 물리 Wi-Fi 인터페이스는 Ready여야 합니다.");
        Ensure(result.SameLocalInterface == true
               && result.HasCompleteComparison,
            "Ready 결과는 같은 인터페이스와 완전 비교를 표시해야 합니다.");
        Ensure(internalReader.Targets.SequenceEqual([InternalTarget])
               && proxyReader.Hosts.SequenceEqual([ProxyHost]),
            "내부 대상과 선택 프록시를 각각 정확히 한 번 조회해야 합니다.");
        Ensure(result.ProxyEndpointCount == 1
               && result.SuccessfulProxyRouteCount == 1
               && result.DirectDirectiveCount == 1,
            "프록시 후보·성공·DIRECT 수를 유지해야 합니다.");
    }

    private static void
        DifferentTunnelInterfaceCompletesAsDiverged()
    {
        RecordingInternalReader internalReader = new(
            _ => SuccessEvidence(
                RouteProbePurpose.InternalDirectTarget,
                WlanInterfaceId,
                NetworkAdapterCategory.Wireless));
        RecordingProxyReader proxyReader = new(
            _ => SuccessEvidence(
                RouteProbePurpose.ProxyEndpoint,
                TunnelInterfaceId,
                NetworkAdapterCategory.Tunnel));
        InternalProxyRouteDiagnosticRunner runner = CreateRunner(
            internalReader,
            proxyReader);

        InternalProxyRouteDiagnosticRunResult result = runner.RunAsync(
                InternalTarget,
                new Uri("https://download.example.invalid/file.bin"),
                TargetProxySnapshot(),
                WlanInterfaceId,
                dnsTimeoutSeconds: 2)
            .GetAwaiter()
            .GetResult();

        Ensure(result.Status
               == InternalProxyRouteDiagnosticRunStatus.Completed,
            "경로가 서로 달라도 진단 실행 자체는 Completed여야 합니다.");
        Ensure(result.Comparison?.Status
               == InternalProxyRouteComparisonStatus.Diverged,
            "내부 Wi-Fi와 프록시 Tunnel은 Diverged여야 합니다.");
        Ensure(result.SameLocalInterface == false
               && result.HasCompleteComparison,
            "Diverged는 근거가 충분한 다른 인터페이스 결론이어야 합니다.");
        Ensure(result.Comparison?.AnyVpnOrTunnelInterface == true,
            "프록시 Tunnel 경로를 안전한 비교 근거에 유지해야 합니다.");
    }

    private static void
        DirectBlockedUnavailableAndPreCanceledReadNothing()
    {
        RecordingInternalReader internalReader = new(_ =>
            throw new InvalidOperationException(
                "조회 금지 상태에서 내부 reader가 호출됐습니다."));
        ThrowingBridge bridge = new();
        InternalProxyRouteDiagnosticRunner runner = new(
            internalReader,
            bridge);
        ProxyDirectiveSourceSnapshot[] snapshots =
        [
            new ProxyDirectiveSourceSnapshot(
                DateTimeOffset.UnixEpoch,
                ProxyDirectiveSourceReadStatus.Success,
                targetDecisionIsDirect: true,
                targetSpecificDirective: null,
                ProxyDirectiveSourceReadStatus.Success,
                manualProxyConfigured: true,
                manualProxyDirective:
                    "PROXY ignored.example.invalid:8080",
                autoDetectEnabled: true,
                pacConfigured: true),
            new ProxyDirectiveSourceSnapshot(
                DateTimeOffset.UnixEpoch,
                ProxyDirectiveSourceReadStatus.Failed,
                targetDecisionIsDirect: false,
                targetSpecificDirective: null,
                ProxyDirectiveSourceReadStatus.Success,
                manualProxyConfigured: true,
                manualProxyDirective:
                    "PROXY ignored-valid.example.invalid:8080",
                autoDetectEnabled: true,
                pacConfigured: true),
            new ProxyDirectiveSourceSnapshot(
                DateTimeOffset.UnixEpoch,
                ProxyDirectiveSourceReadStatus.NotAttempted,
                targetDecisionIsDirect: false,
                targetSpecificDirective: null,
                ProxyDirectiveSourceReadStatus.NotAttempted,
                manualProxyConfigured: false,
                manualProxyDirective: null,
                autoDetectEnabled: false,
                pacConfigured: false)
        ];
        InternalProxyRouteDiagnosticRunStatus[] expected =
        [
            InternalProxyRouteDiagnosticRunStatus.DirectOnly,
            InternalProxyRouteDiagnosticRunStatus.Blocked,
            InternalProxyRouteDiagnosticRunStatus.Unavailable
        ];

        for (int index = 0; index < snapshots.Length; index++)
        {
            InternalProxyRouteDiagnosticRunResult result =
                runner.RunAsync(
                        InternalTarget,
                        new Uri(
                            "https://download.example.invalid/file.bin"),
                        snapshots[index],
                        WlanInterfaceId,
                        dnsTimeoutSeconds: 2)
                    .GetAwaiter()
                    .GetResult();
            Ensure(result.Status == expected[index],
                $"비조회 진단 상태가 잘못됐습니다: {index}");
            Ensure(result.InternalRouteEvidence is null
                   && result.ProxyRouteAnalysis is null
                   && result.Comparison is null,
                "비조회 상태에는 원본 경로·비교 payload가 없어야 합니다.");
        }

        using CancellationTokenSource source = new();
        source.Cancel();
        InternalProxyRouteDiagnosticRunResult canceled =
            runner.RunAsync(
                    InternalTarget,
                    new Uri(
                        "https://download.example.invalid/file.bin"),
                    TargetProxySnapshot(),
                    WlanInterfaceId,
                    dnsTimeoutSeconds: 2,
                    source.Token)
                .GetAwaiter()
                .GetResult();
        Ensure(canceled.Status
               == InternalProxyRouteDiagnosticRunStatus.Canceled,
            "사전 취소는 Canceled여야 합니다.");
        Ensure(internalReader.Targets.Count == 0
               && bridge.Calls == 0,
            "DIRECT·Blocked·Unavailable·사전 취소에서 모든 DNS·경로 호출은 0회여야 합니다.");
    }

    private static void InternalCancellationPreventsProxyReads()
    {
        RecordingInternalReader internalReader = new(
            _ => new DestinationRouteEvidence(
                CapturedAt: DateTimeOffset.UnixEpoch,
                TargetLabel: "합성 내부 대상",
                Purpose: RouteProbePurpose.InternalDirectTarget,
                DnsWasUsed: true,
                ResolvedAddressCount: 0,
                Status: DestinationRouteEvidenceStatus.Canceled,
                SelectedInterface: null,
                AddressEvidence:
                    Array.Empty<RouteAddressEvidence>(),
                Warnings: Array.Empty<string>(),
                Message: "합성 취소"));
        ThrowingBridge bridge = new();
        InternalProxyRouteDiagnosticRunner runner = new(
            internalReader,
            bridge);

        InternalProxyRouteDiagnosticRunResult result = runner.RunAsync(
                InternalTarget,
                new Uri("https://download.example.invalid/file.bin"),
                TargetProxySnapshot(),
                WlanInterfaceId,
                dnsTimeoutSeconds: 2)
            .GetAwaiter()
            .GetResult();

        Ensure(result.Status
               == InternalProxyRouteDiagnosticRunStatus.Canceled,
            "내부 경로 Canceled를 전체 진단 취소로 유지해야 합니다.");
        Ensure(internalReader.Targets.Count == 1
               && bridge.Calls == 0,
            "내부 취소 후 프록시 브리지를 호출하면 안 됩니다.");
    }

    private static void
        ProxyRouteFailureCompletesWithIncompleteComparison()
    {
        RecordingInternalReader internalReader = new(
            _ => SuccessEvidence(
                RouteProbePurpose.InternalDirectTarget,
                WlanInterfaceId,
                NetworkAdapterCategory.Wireless));
        RecordingProxyReader proxyReader = new(
            _ => new DestinationRouteEvidence(
                CapturedAt: DateTimeOffset.UnixEpoch,
                TargetLabel: "합성 프록시 후보",
                Purpose: RouteProbePurpose.ProxyEndpoint,
                DnsWasUsed: true,
                ResolvedAddressCount: 1,
                Status: DestinationRouteEvidenceStatus.RouteNotFound,
                SelectedInterface: null,
                AddressEvidence:
                    Array.Empty<RouteAddressEvidence>(),
                Warnings: Array.Empty<string>(),
                Message: "합성 경로 없음"));
        InternalProxyRouteDiagnosticRunner runner = CreateRunner(
            internalReader,
            proxyReader);

        InternalProxyRouteDiagnosticRunResult result = runner.RunAsync(
                InternalTarget,
                new Uri("https://download.example.invalid/file.bin"),
                TargetProxySnapshot(),
                WlanInterfaceId,
                dnsTimeoutSeconds: 2)
            .GetAwaiter()
            .GetResult();

        Ensure(result.Status
               == InternalProxyRouteDiagnosticRunStatus.Completed,
            "프록시 경로 미확정도 구조화 비교까지 완료할 수 있어야 합니다.");
        Ensure(result.ProxyRouteStatus == "Failed"
               || result.ProxyRouteStatus == "PartialSuccess",
            "프록시 분석 실패 또는 부분 상태를 유지해야 합니다.");
        Ensure(result.Comparison?.Status
               == InternalProxyRouteComparisonStatus.Incomplete,
            "프록시 경로 근거가 없으면 비교는 Incomplete여야 합니다.");
        Ensure(!result.HasCompleteComparison
               && result.SameLocalInterface is null,
            "불완전 근거로 같은·다른 인터페이스 결론을 만들면 안 됩니다.");
    }

    private static void DefaultJsonExcludesTargetsAndRawEvidence()
    {
        RecordingInternalReader internalReader = new(
            _ => SuccessEvidence(
                RouteProbePurpose.InternalDirectTarget,
                WlanInterfaceId,
                NetworkAdapterCategory.Wireless));
        RecordingProxyReader proxyReader = new(
            _ => SuccessEvidence(
                RouteProbePurpose.ProxyEndpoint,
                TunnelInterfaceId,
                NetworkAdapterCategory.Tunnel));
        InternalProxyRouteDiagnosticRunResult result = CreateRunner(
                internalReader,
                proxyReader)
            .RunAsync(
                InternalTarget,
                new Uri("https://download.example.invalid/file.bin"),
                TargetProxySnapshot(),
                WlanInterfaceId,
                dnsTimeoutSeconds: 2)
            .GetAwaiter()
            .GetResult();

        string json = JsonSerializer.Serialize(result);
        foreach (string secret in new[]
                 {
                     InternalTarget,
                     ProxyHost,
                     WlanInterfaceId,
                     TunnelInterfaceId,
                     "Synthetic Adapter",
                     "download.example.invalid"
                 })
        {
            Ensure(!json.Contains(
                    secret,
                    StringComparison.OrdinalIgnoreCase),
                $"기본 진단 결과 JSON에 원문 대상·호스트·GUID가 남았습니다: {secret}");
        }
        Ensure(!json.Contains(
                "internalRouteEvidence",
                StringComparison.OrdinalIgnoreCase)
               && !json.Contains(
                   "proxyRouteAnalysis",
                   StringComparison.OrdinalIgnoreCase)
               && !json.Contains(
                   "\"comparison\":",
                   StringComparison.OrdinalIgnoreCase),
            "기본 JSON에 메모리 전용 원본 경로·비교 payload를 포함하면 안 됩니다.");
        Ensure(json.Contains(
                "\"status\":",
                StringComparison.Ordinal)
               && json.Contains(
                   "\"comparisonStatus\":\"Diverged\"",
                   StringComparison.Ordinal)
               && json.Contains(
                   "\"hasCompleteComparison\":true",
                   StringComparison.Ordinal),
            "안전한 상태·비교 완료 여부는 구조화해 유지해야 합니다.");
    }

    private static InternalProxyRouteDiagnosticRunner CreateRunner(
        RecordingInternalReader internalReader,
        RecordingProxyReader proxyReader)
    {
        ProxyDirectiveRouteBridge bridge = new(
            new ProxyEndpointRouteAnalyzer(proxyReader));
        return new InternalProxyRouteDiagnosticRunner(
            internalReader,
            new BridgeAdapter(bridge));
    }

    private static ProxyDirectiveSourceSnapshot TargetProxySnapshot() =>
        new(
            DateTimeOffset.UnixEpoch,
            ProxyDirectiveSourceReadStatus.Success,
            targetDecisionIsDirect: false,
            targetSpecificDirective:
                $"PROXY {ProxyHost}:8080; DIRECT",
            ProxyDirectiveSourceReadStatus.Success,
            manualProxyConfigured: true,
            manualProxyDirective:
                "PROXY ignored-manual.example.invalid:3128",
            autoDetectEnabled: true,
            pacConfigured: true);

    private static DestinationRouteEvidence SuccessEvidence(
        RouteProbePurpose purpose,
        string interfaceId,
        NetworkAdapterCategory category)
    {
        RouteInterfaceDescriptor descriptor = new(
            InterfaceIdentity: interfaceId,
            DisplayName: "Synthetic Adapter",
            Description: "Synthetic Adapter Description",
            NativeInterfaceType: category.ToString(),
            Category: category,
            OperationalState: NetworkAdapterOperationalState.Up,
            HasDefaultGateway: true,
            IsVirtual: category == NetworkAdapterCategory.Tunnel,
            IsVpn: category == NetworkAdapterCategory.Tunnel);
        return new DestinationRouteEvidence(
            CapturedAt: DateTimeOffset.UnixEpoch,
            TargetLabel: "합성 안전 대상",
            Purpose: purpose,
            DnsWasUsed: true,
            ResolvedAddressCount: 1,
            Status: DestinationRouteEvidenceStatus.Success,
            SelectedInterface: descriptor,
            AddressEvidence:
            [
                new RouteAddressEvidence(
                    RouteAddressFamilyKind.IPv4,
                    RouteAddressEvidenceStatus.Success,
                    descriptor,
                    NativeErrorCode: null,
                    Message: "합성 최적 경로")
            ],
            Warnings: Array.Empty<string>(),
            Message: "합성 최적 경로");
    }

    private sealed class RecordingInternalReader
        : IInternalDirectRouteEvidenceReader
    {
        private readonly Func<string, DestinationRouteEvidence> _factory;

        public RecordingInternalReader(
            Func<string, DestinationRouteEvidence> factory)
        {
            _factory = factory;
        }

        public List<string> Targets { get; } = [];

        public Task<DestinationRouteEvidence> ReadAsync(
            string target,
            string safeLabel,
            int dnsTimeoutSeconds,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Targets.Add(target);
            Ensure(!safeLabel.Contains(
                    target,
                    StringComparison.OrdinalIgnoreCase),
                "내부 route label에 대상 원문이 포함되면 안 됩니다.");
            return Task.FromResult(_factory(target));
        }
    }

    private sealed class RecordingProxyReader
        : IProxyEndpointRouteEvidenceReader
    {
        private readonly Func<string, DestinationRouteEvidence> _factory;

        public RecordingProxyReader(
            Func<string, DestinationRouteEvidence> factory)
        {
            _factory = factory;
        }

        public List<string> Hosts { get; } = [];

        public Task<DestinationRouteEvidence> ReadAsync(
            string host,
            string safeLabel,
            int dnsTimeoutSeconds,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Hosts.Add(host);
            Ensure(!safeLabel.Contains(
                    host,
                    StringComparison.OrdinalIgnoreCase),
                "프록시 route label에 호스트 원문이 포함되면 안 됩니다.");
            return Task.FromResult(_factory(host));
        }
    }

    private sealed class BridgeAdapter
        : IProxyDirectiveRouteBridgeExecutor
    {
        private readonly ProxyDirectiveRouteBridge _bridge;

        public BridgeAdapter(ProxyDirectiveRouteBridge bridge)
        {
            _bridge = bridge;
        }

        public Task<
            ProxyDirectiveRouteAnalysisExecutionResult<
                ProxyEndpointRouteAnalysisResult>> ExecuteAsync(
            ProxyDirectiveSourceSelectionResult selection,
            Uri targetUri,
            string? expectedWlanInterfaceId,
            int dnsTimeoutSeconds,
            CancellationToken cancellationToken) =>
            _bridge.ExecuteAsync(
                selection,
                targetUri,
                expectedWlanInterfaceId,
                dnsTimeoutSeconds,
                cancellationToken);
    }

    private sealed class ThrowingBridge
        : IProxyDirectiveRouteBridgeExecutor
    {
        public int Calls { get; private set; }

        public Task<
            ProxyDirectiveRouteAnalysisExecutionResult<
                ProxyEndpointRouteAnalysisResult>> ExecuteAsync(
            ProxyDirectiveSourceSelectionResult selection,
            Uri targetUri,
            string? expectedWlanInterfaceId,
            int dnsTimeoutSeconds,
            CancellationToken cancellationToken)
        {
            Calls++;
            throw new InvalidOperationException(
                "조회 금지 상태에서 bridge가 호출됐습니다.");
        }
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
