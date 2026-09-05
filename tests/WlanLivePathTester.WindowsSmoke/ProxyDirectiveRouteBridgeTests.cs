using System.Runtime.CompilerServices;
using System.Text.Json;
using WlanLivePathTester.Core.NetworkEnvironment;
using WlanLivePathTester.Core.Proxy;
using WlanLivePathTester.Core.Routing;
using WlanLivePathTester.Windows.Routing;

namespace WlanLivePathTester.WindowsSmoke;

internal static class ProxyDirectiveRouteBridgeTests
{
    private const string WlanInterfaceId =
        "E3B2C3D4-E5F6-47A8-9123-1234567890AB";

#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Run()
    {
        TargetSpecificProxyUsesOnlyTheSelectedHost();
        ManualMappingsAreFilteredByTheTargetScheme();
        DirectInvalidUnavailableAndPreCanceledReadNothing();
        InvalidTargetSchemeReturnsStructuredAnalysisWithoutReads();
        DefaultSerializationExcludesRawDirectiveAndAnalysisPayload();
        Console.WriteLine(
            "PASS selected proxy directive to existing route analyzer bridge tests");
    }

    private static void TargetSpecificProxyUsesOnlyTheSelectedHost()
    {
        const string selectedHost =
            "selected-target-proxy.example.invalid";
        const string ignoredManualHost =
            "ignored-manual-proxy.example.invalid";
        RecordingReader reader = new(_ => SuccessEvidence());
        ProxyDirectiveSourceSelectionResult selection =
            ProxyDirectiveSourceSelectionPolicy.Select(
                targetDecisionWasEvaluated: true,
                targetDecisionIsDirect: false,
                targetSpecificDirective:
                    $"PROXY {selectedHost}:8080; DIRECT",
                manualProxyConfigured: true,
                manualProxyDirective:
                    $"PROXY {ignoredManualHost}:3128");

        ProxyDirectiveRouteAnalysisExecutionResult<
            ProxyEndpointRouteAnalysisResult> execution = Execute(
                selection,
                reader,
                new Uri("https://download.example.invalid/file.bin"));
        ProxyEndpointRouteAnalysisResult analysis =
            execution.Analysis
            ?? throw new InvalidOperationException(
                "완료된 대상별 프록시 실행에 경로 분석 결과가 필요합니다.");

        Ensure(execution.Status
               == ProxyDirectiveRouteAnalysisExecutionStatus.Completed,
            "선택된 대상별 프록시는 기존 경로 분석기를 완료해야 합니다.");
        Ensure(analysis.Status
               == ProxyEndpointRouteAnalysisStatus.Success,
            "합성 단일 프록시 경로는 Success여야 합니다.");
        Ensure(reader.Hosts.SequenceEqual([selectedHost]),
            "대상별 선택 호스트만 resolver에 전달해야 합니다.");
        Ensure(!reader.Hosts.Contains(
                ignoredManualHost,
                StringComparer.OrdinalIgnoreCase),
            "무시된 수동 프록시 호스트를 조회하면 안 됩니다.");
        Ensure(analysis.DirectFallback,
            "프록시 뒤 DIRECT fallback을 기존 분석 결과에 유지해야 합니다.");
    }

    private static void ManualMappingsAreFilteredByTheTargetScheme()
    {
        const string httpHost = "manual-http.example.invalid";
        const string httpsHost = "manual-https.example.invalid";
        RecordingReader reader = new(_ => SuccessEvidence());
        ProxyDirectiveSourceSelectionResult selection =
            ProxyDirectiveSourceSelectionPolicy.Select(
                targetDecisionWasEvaluated: false,
                targetDecisionIsDirect: false,
                targetSpecificDirective: null,
                manualProxyConfigured: true,
                manualProxyDirective:
                    $"http={httpHost}:8080;https={httpsHost}:8443");

        ProxyDirectiveRouteAnalysisExecutionResult<
            ProxyEndpointRouteAnalysisResult> execution = Execute(
                selection,
                reader,
                new Uri("https://download.example.invalid/file.bin"));
        ProxyEndpointRouteAnalysisResult analysis =
            execution.Analysis
            ?? throw new InvalidOperationException(
                "완료된 수동 프록시 실행에 경로 분석 결과가 필요합니다.");

        Ensure(execution.Status
               == ProxyDirectiveRouteAnalysisExecutionStatus.Completed,
            "유효한 수동 프록시는 분석 콜백을 완료해야 합니다.");
        Ensure(reader.Hosts.SequenceEqual([httpsHost]),
            "HTTPS 대상에는 https= 후보만 기존 분석기에 전달해야 합니다.");
        Ensure(analysis.ApplicableEndpointCount == 1
               && analysis.AnalyzedEndpointCount == 1,
            "현재 대상 스킴에 적용되는 후보 한 개만 분석해야 합니다.");
    }

    private static void
        DirectInvalidUnavailableAndPreCanceledReadNothing()
    {
        RecordingReader reader = new(_ =>
            throw new InvalidOperationException(
                "조회 금지 상태에서 reader가 호출됐습니다."));
        ProxyDirectiveSourceSelectionResult[] selections =
        [
            ProxyDirectiveSourceSelectionPolicy.Select(
                targetDecisionWasEvaluated: true,
                targetDecisionIsDirect: true,
                targetSpecificDirective: null,
                manualProxyConfigured: true,
                manualProxyDirective:
                    "PROXY ignored.example.invalid:8080"),
            ProxyDirectiveSourceSelectionPolicy.Select(
                targetDecisionWasEvaluated: true,
                targetDecisionIsDirect: false,
                targetSpecificDirective: "DIRECT",
                manualProxyConfigured: true,
                manualProxyDirective:
                    "PROXY ignored-valid.example.invalid:8080"),
            ProxyDirectiveSourceSelectionPolicy.Select(
                targetDecisionWasEvaluated: false,
                targetDecisionIsDirect: false,
                targetSpecificDirective: null,
                manualProxyConfigured: false,
                manualProxyDirective: null)
        ];
        ProxyDirectiveRouteAnalysisExecutionStatus[] expected =
        [
            ProxyDirectiveRouteAnalysisExecutionStatus.DirectOnly,
            ProxyDirectiveRouteAnalysisExecutionStatus.Blocked,
            ProxyDirectiveRouteAnalysisExecutionStatus.Unavailable
        ];

        for (int index = 0; index < selections.Length; index++)
        {
            ProxyDirectiveRouteAnalysisExecutionResult<
                ProxyEndpointRouteAnalysisResult> result = Execute(
                    selections[index],
                    reader,
                    new Uri(
                        "https://download.example.invalid/file.bin"));
            Ensure(result.Status == expected[index],
                $"조회 금지 실행 상태가 잘못됐습니다: {index}");
            Ensure(result.Analysis is null,
                "조회 금지 상태에는 경로 분석 payload가 없어야 합니다.");
        }

        using CancellationTokenSource source = new();
        source.Cancel();
        ProxyDirectiveRouteAnalysisExecutionResult<
            ProxyEndpointRouteAnalysisResult> canceled = Execute(
                ProxyDirectiveSourceSelectionPolicy.Select(
                    targetDecisionWasEvaluated: true,
                    targetDecisionIsDirect: false,
                    targetSpecificDirective:
                        "PROXY canceled.example.invalid:8080",
                    manualProxyConfigured: false,
                    manualProxyDirective: null),
                reader,
                new Uri("https://download.example.invalid/file.bin"),
                source.Token);
        Ensure(canceled.Status
               == ProxyDirectiveRouteAnalysisExecutionStatus.Canceled,
            "사전 취소는 Canceled여야 합니다.");
        Ensure(reader.Hosts.Count == 0,
            "DIRECT·Invalid·Unavailable·사전 취소에서 reader 호출은 0회여야 합니다.");
    }

    private static void
        InvalidTargetSchemeReturnsStructuredAnalysisWithoutReads()
    {
        RecordingReader reader = new(_ =>
            throw new InvalidOperationException(
                "지원하지 않는 대상 스킴에서 reader가 호출됐습니다."));
        ProxyDirectiveSourceSelectionResult selection =
            ProxyDirectiveSourceSelectionPolicy.Select(
                targetDecisionWasEvaluated: true,
                targetDecisionIsDirect: false,
                targetSpecificDirective:
                    "PROXY selected.example.invalid:8080",
                manualProxyConfigured: false,
                manualProxyDirective: null);

        ProxyDirectiveRouteAnalysisExecutionResult<
            ProxyEndpointRouteAnalysisResult> execution = Execute(
                selection,
                reader,
                new Uri("ftp://download.example.invalid/file.bin"));
        ProxyEndpointRouteAnalysisResult analysis =
            execution.Analysis
            ?? throw new InvalidOperationException(
                "지원하지 않는 대상 스킴도 구조화 InvalidInput 결과를 반환해야 합니다.");

        Ensure(execution.Status
               == ProxyDirectiveRouteAnalysisExecutionStatus.Completed,
            "브리지 콜백 자체는 구조화 InvalidInput 결과를 반환하므로 Completed여야 합니다.");
        Ensure(analysis.Status
               == ProxyEndpointRouteAnalysisStatus.InvalidInput,
            "HTTP·HTTPS가 아닌 대상은 기존 파서의 InvalidInput이어야 합니다.");
        Ensure(reader.Hosts.Count == 0,
            "잘못된 대상 스킴에서 DNS·route reader를 호출하면 안 됩니다.");
    }

    private static void
        DefaultSerializationExcludesRawDirectiveAndAnalysisPayload()
    {
        const string secretHost =
            "serialization-private-proxy.example.invalid";
        RecordingReader reader = new(_ => SuccessEvidence());
        ProxyDirectiveSourceSelectionResult selection =
            ProxyDirectiveSourceSelectionPolicy.Select(
                targetDecisionWasEvaluated: true,
                targetDecisionIsDirect: false,
                targetSpecificDirective:
                    $"PROXY {secretHost}:8080; DIRECT",
                manualProxyConfigured: false,
                manualProxyDirective: null);
        ProxyDirectiveRouteAnalysisExecutionResult<
            ProxyEndpointRouteAnalysisResult> execution = Execute(
                selection,
                reader,
                new Uri("https://download.example.invalid/file.bin"));

        string json = JsonSerializer.Serialize(execution);
        Ensure(!json.Contains(
                secretHost,
                StringComparison.OrdinalIgnoreCase),
            "실행 결과 JSON에 프록시 호스트 원문이 남으면 안 됩니다.");
        Ensure(!json.Contains(
                selection.SelectedDirectiveText!,
                StringComparison.Ordinal),
            "실행 결과 JSON에 선택 지시문 원문이 남으면 안 됩니다.");
        Ensure(!json.Contains(
                "\"analysis\":",
                StringComparison.OrdinalIgnoreCase),
            "기본 JSON에 메모리 전용 기존 경로 분석 payload를 포함하면 안 됩니다.");
        Ensure(json.Contains(
                "TargetSpecificProxySelected",
                StringComparison.Ordinal),
            "안전한 실행 계획 코드는 유지해야 합니다.");
        Ensure(json.Contains(
                "\"hasCompletedAnalysis\":true",
                StringComparison.Ordinal),
            "분석 payload 없이 완료 여부는 구조화해 유지해야 합니다.");
    }

    private static ProxyDirectiveRouteAnalysisExecutionResult<
        ProxyEndpointRouteAnalysisResult> Execute(
        ProxyDirectiveSourceSelectionResult selection,
        RecordingReader reader,
        Uri targetUri,
        CancellationToken cancellationToken = default) =>
        new ProxyDirectiveRouteBridge(
                new ProxyEndpointRouteAnalyzer(reader))
            .ExecuteAsync(
                selection,
                targetUri,
                WlanInterfaceId,
                dnsTimeoutSeconds: 2,
                cancellationToken)
            .GetAwaiter()
            .GetResult();

    private static DestinationRouteEvidence SuccessEvidence()
    {
        RouteInterfaceDescriptor descriptor = new(
            InterfaceIdentity: WlanInterfaceId,
            DisplayName: "Synthetic Wireless Adapter",
            Description: "Synthetic Wireless Description",
            NativeInterfaceType: "Wireless80211",
            Category: NetworkAdapterCategory.Wireless,
            OperationalState: NetworkAdapterOperationalState.Up,
            HasDefaultGateway: true,
            IsVirtual: false,
            IsVpn: false);
        return new DestinationRouteEvidence(
            CapturedAt: DateTimeOffset.UnixEpoch,
            TargetLabel: "합성 프록시 후보",
            Purpose: RouteProbePurpose.ProxyEndpoint,
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

    private sealed class RecordingReader
        : IProxyEndpointRouteEvidenceReader
    {
        private readonly Func<string, DestinationRouteEvidence> _factory;

        public RecordingReader(
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
                "reader label에 원문 프록시 호스트가 포함되면 안 됩니다.");
            return Task.FromResult(_factory(host));
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
