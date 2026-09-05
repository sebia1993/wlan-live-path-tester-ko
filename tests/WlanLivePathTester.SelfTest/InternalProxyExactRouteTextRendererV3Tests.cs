using System.Runtime.CompilerServices;
using WlanLivePathTester.Core.NetworkEnvironment;
using WlanLivePathTester.Core.Proxy;
using WlanLivePathTester.Core.Reporting;
using WlanLivePathTester.Core.Routing;

namespace WlanLivePathTester.SelfTest;

internal static class InternalProxyExactRouteTextRendererV3Tests
{
#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Run()
    {
        RendersOrderedSafeFieldsAndDirectFallback();
        IgnoresEndpointLabelsMessagesWarningsAndExactIdentity();
        ReplacesUnknownEnumsScopesPortsAndFingerprints();
        Console.WriteLine(
            "PASS exact route comparison safe text renderer v3 tests");
    }

    private static void RendersOrderedSafeFieldsAndDirectFallback()
    {
        ProxyEndpointRouteEvidenceItem second = CreateEndpoint(
            sequence: 2,
            hostFingerprint: "abcdef0123",
            interfaceFingerprint: "112233aabb",
            category: NetworkAdapterCategory.Tunnel,
            transport: ProxyEndpointTransport.Https,
            port: 8443);
        ProxyEndpointRouteEvidenceItem first = CreateEndpoint(
            sequence: 1,
            hostFingerprint: "0123456789",
            interfaceFingerprint: "ffeedd0011",
            category: NetworkAdapterCategory.Wireless,
            transport: ProxyEndpointTransport.Http,
            port: 8080);
        ProxyEndpointRouteAnalysisResult analysis = CreateAnalysis(
            [second, first],
            directFallback: true);
        ProxyDirectiveRouteAnalysisExecutionResult<
            ProxyEndpointRouteAnalysisResult> execution = Execute(analysis);
        InternalProxyRouteComparisonResult comparison =
            CreateComparison();

        string text = InternalProxyRouteComparisonTextRenderer.Render(
            comparison,
            execution);

        Ensure(text.Contains(
                "상태: Diverged",
                StringComparison.Ordinal)
               && text.Contains(
                   "관계: DifferentInterface",
                   StringComparison.Ordinal)
               && text.Contains(
                   "판정 코드: DifferentLocalInterface",
                   StringComparison.Ordinal),
            "상태·관계·판정 코드를 표시해야 합니다.");
        Ensure(text.Contains(
                "전체 인터페이스 ID 정확 비교: 수행",
                StringComparison.Ordinal),
            "정확 GUID 비교 여부를 표시해야 합니다.");
        Ensure(text.IndexOf("#1", StringComparison.Ordinal)
               < text.IndexOf("#2", StringComparison.Ordinal),
            "프록시 후보를 원본 sequence 순서로 정렬해야 합니다.");
        Ensure(text.Contains(
                "호스트 지문 0123456789",
                StringComparison.Ordinal)
               && text.Contains(
                   "인터페이스 Wireless/ffeedd0011",
                   StringComparison.Ordinal)
               && text.Contains(
                   "WLAN 상관 Matched",
                   StringComparison.Ordinal),
            "안전한 후보 지문·범주·WLAN 상관을 표시해야 합니다.");
        Ensure(text.Contains(
                "DIRECT: 있음 · 첫 경로: 아니오 · fallback: 있음",
                StringComparison.Ordinal),
            "DIRECT fallback 상태를 표시해야 합니다.");
    }

    private static void
        IgnoresEndpointLabelsMessagesWarningsAndExactIdentity()
    {
        const string secretHost =
            "proxy-render-secret.example.invalid";
        const string secretUrl =
            "https://internal-render-secret.example.invalid/private.bin";
        const string secretGuid =
            "C2B2C3D4-E5F6-47A8-9123-1234567890AB";
        const string secretDescription =
            "Corporate Secret Interface Description";
        ProxyEndpointRouteEvidenceItem endpoint = CreateEndpoint(
            sequence: 1,
            hostFingerprint: "1234567890",
            interfaceFingerprint: "0987654321",
            category: NetworkAdapterCategory.Wireless,
            transport: ProxyEndpointTransport.Http,
            port: 8080) with
        {
            EndpointLabel = secretHost,
            Message = secretUrl,
            Warnings = [secretHost, secretUrl, secretDescription],
            SelectedInterfaceIdentity = secretGuid
        };
        ProxyEndpointRouteAnalysisResult analysis = CreateAnalysis(
            [endpoint]);
        ProxyDirectiveRouteAnalysisExecutionResult<
            ProxyEndpointRouteAnalysisResult> execution = Execute(
                analysis,
                secretHost);

        string text = InternalProxyRouteComparisonTextRenderer.Render(
            CreateComparison(),
            execution);

        foreach (string secret in new[]
                 {
                     secretHost,
                     secretUrl,
                     "internal-render-secret.example.invalid",
                     secretGuid,
                     secretDescription
                 })
        {
            Ensure(!text.Contains(
                    secret,
                    StringComparison.OrdinalIgnoreCase),
                $"렌더러가 비허용 원문 필드를 읽었습니다: {secret}");
        }
        Ensure(text.Contains(
                "호스트 지문 1234567890",
                StringComparison.Ordinal)
               && text.Contains(
                   "인터페이스 Wireless/0987654321",
                   StringComparison.Ordinal),
            "검증된 안전 필드만 유지해야 합니다.");
    }

    private static void
        ReplacesUnknownEnumsScopesPortsAndFingerprints()
    {
        ProxyEndpointRouteEvidenceItem endpoint = new(
            Sequence: -5,
            EndpointLabel: "secret.example.invalid",
            HostFingerprint: "not-a-fingerprint",
            AppliesToScheme: "https://secret.example.invalid/private",
            Transport: (ProxyEndpointTransport)999,
            Port: 99999,
            RouteStatus: (DestinationRouteEvidenceStatus)999,
            WlanCorrelationStatus: (RouteWlanCorrelationStatus)999,
            SelectedInterfaceFingerprint:
                "C2B2C3D4-E5F6-47A8-9123-1234567890AB",
            SelectedInterfaceCategory:
                (NetworkAdapterCategory)999,
            SelectedInterfaceIsVirtual: null,
            SelectedInterfaceIsVpn: null,
            SelectedInterfaceIsUp: null,
            SelectedInterfaceHasDefaultGateway: null,
            ResolvedAddressCount: 0,
            SuccessfulAddressCount: 0,
            FailedAddressCount: 0,
            Message: "10.20.30.40",
            Warnings: ["user@example.invalid"])
        {
            SelectedInterfaceIdentity =
                "D2B2C3D4-E5F6-47A8-9123-1234567890AB"
        };
        ProxyEndpointRouteAnalysisResult analysis = CreateAnalysis(
            [endpoint],
            status: ProxyEndpointRouteAnalysisStatus.Failed);
        InternalProxyRouteComparisonResult comparison =
            CreateComparison() with
            {
                Status = (InternalProxyRouteComparisonStatus)999,
                Relation = (InternalProxyRouteRelation)999,
                Code = (InternalProxyRouteComparisonCode)999,
                InternalRouteStatus = null,
                ProxyExecutionStatus = null,
                ProxyAnalysisStatus = null,
                InternalInterfaceFingerprint = "not-safe",
                InternalInterfaceCategory =
                    (NetworkAdapterCategory)999
            };

        string text = InternalProxyRouteComparisonTextRenderer.Render(
            comparison,
            Execute(analysis));

        Ensure(text.Contains(
                "상태: Incomplete",
                StringComparison.Ordinal)
               && text.Contains(
                   "관계: Unknown",
                   StringComparison.Ordinal)
               && text.Contains(
                   "판정 코드: ProxyAnalysisIncomplete",
                   StringComparison.Ordinal),
            "정의되지 않은 비교 enum은 안전한 fallback으로 표시해야 합니다.");
        Ensure(text.Contains(
                "#0 · Unspecified · 범위 unknown · 포트 - · 호스트 지문 확인 불가 · 경로 Failed · 인터페이스 확인 불가/확인 불가 · WLAN 상관 NotEvaluated",
                StringComparison.Ordinal),
            "정의되지 않은 후보 enum·scope·port·지문을 고정 안전값으로 치환해야 합니다.");
        foreach (string forbidden in new[]
                 {
                     "secret.example.invalid",
                     "C2B2C3D4-E5F6-47A8-9123-1234567890AB",
                     "D2B2C3D4-E5F6-47A8-9123-1234567890AB",
                     "10.20.30.40",
                     "user@example.invalid"
                 })
        {
            Ensure(!text.Contains(
                    forbidden,
                    StringComparison.OrdinalIgnoreCase),
                $"알 수 없는 후보의 원문 값이 표시됐습니다: {forbidden}");
        }
    }

    private static InternalProxyRouteComparisonResult
        CreateComparison() =>
        new(
            EvaluatedAt: DateTimeOffset.UnixEpoch,
            Status: InternalProxyRouteComparisonStatus.Diverged,
            Relation: InternalProxyRouteRelation.DifferentInterface,
            Code:
                InternalProxyRouteComparisonCode.DifferentLocalInterface,
            InternalRouteStatus: DestinationRouteEvidenceStatus.Success,
            ProxyExecutionStatus:
                ProxyDirectiveRouteAnalysisExecutionStatus.Completed,
            ProxyAnalysisStatus:
                ProxyEndpointRouteAnalysisStatus.Success,
            ProxySourceKind:
                ProxyDirectiveSourceKind.TargetSpecificAutoProxy,
            ProxyPlanCode:
                ProxyDirectiveRouteAnalysisPlanCode
                    .TargetSpecificProxySelected,
            InternalInterfaceFingerprint: "aabbccddee",
            InternalInterfaceCategory:
                NetworkAdapterCategory.Wireless,
            ProxyInterfaceFingerprints:
                ["ffeedd0011", "112233aabb"],
            ProxyInterfaceCategories:
                [
                    NetworkAdapterCategory.Wireless,
                    NetworkAdapterCategory.Tunnel
                ],
            ProxyApplicableEndpointCount: 2,
            ProxyAnalyzedEndpointCount: 2,
            ProxySuccessfulEndpointCount: 2,
            ProxyDistinctInterfaceCount: 2,
            ProxySkippedAfterDirectCount: 0,
            ProxyDirectPresent: true,
            ProxyDirectIsPrimary: false,
            ProxyDirectFallbackPresent: true,
            ProxyParseErrorsPresent: false,
            ExactIdentityComparisonPerformed: true,
            Message:
                "내부 DIRECT 대상과 확인된 프록시 엔드포인트가 서로 다른 Windows 로컬 인터페이스를 선택했습니다.",
            Interpretation:
                "현재 PC에서 내부 경로와 프록시 경로의 첫 로컬 송출 NIC가 분리돼 있습니다.",
            Limitation:
                "인터페이스 차이만으로 장애를 확정할 수 없습니다.",
            NextStep:
                "각 인터페이스 범주와 VPN 정책을 확인하십시오.");

    private static ProxyEndpointRouteEvidenceItem CreateEndpoint(
        int sequence,
        string hostFingerprint,
        string interfaceFingerprint,
        NetworkAdapterCategory category,
        ProxyEndpointTransport transport,
        int port) =>
        new(
            Sequence: sequence,
            EndpointLabel: "사용하지 않는 label",
            HostFingerprint: hostFingerprint,
            AppliesToScheme: "https",
            Transport: transport,
            Port: port,
            RouteStatus: DestinationRouteEvidenceStatus.Success,
            WlanCorrelationStatus: category
                == NetworkAdapterCategory.Wireless
                    ? RouteWlanCorrelationStatus.Matched
                    : RouteWlanCorrelationStatus.DifferentInterface,
            SelectedInterfaceFingerprint: interfaceFingerprint,
            SelectedInterfaceCategory: category,
            SelectedInterfaceIsVirtual: category
                == NetworkAdapterCategory.Tunnel,
            SelectedInterfaceIsVpn: category
                == NetworkAdapterCategory.Tunnel,
            SelectedInterfaceIsUp: true,
            SelectedInterfaceHasDefaultGateway: true,
            ResolvedAddressCount: 1,
            SuccessfulAddressCount: 1,
            FailedAddressCount: 0,
            Message: "사용하지 않는 메시지",
            Warnings: Array.Empty<string>());

    private static ProxyEndpointRouteAnalysisResult CreateAnalysis(
        IReadOnlyList<ProxyEndpointRouteEvidenceItem> endpoints,
        bool directFallback = false,
        ProxyEndpointRouteAnalysisStatus status =
            ProxyEndpointRouteAnalysisStatus.Success) =>
        new(
            CapturedAt: DateTimeOffset.UnixEpoch,
            Status: status,
            SourceKind: ProxyEndpointSourceKind.AutoProxyResult,
            ProxyDecision: directFallback
                ? ProxyEndpointDecision.ProxyWithDirectFallback
                : ProxyEndpointDecision.Proxy,
            TargetScheme: "https",
            DirectPresent: directFallback,
            DirectIsPrimary: false,
            DirectFallback: directFallback,
            DirectSequence: directFallback ? 3 : null,
            ParsedEndpointCount: endpoints.Count,
            ApplicableEndpointCount: endpoints.Count,
            AnalyzedEndpointCount: endpoints.Count,
            SkippedAfterDirectCount: 0,
            SuccessfulEndpointCount: endpoints.Count(endpoint =>
                endpoint.IsRouteSuccess),
            DistinctInterfaceCount: endpoints
                .Select(endpoint =>
                    endpoint.SelectedInterfaceFingerprint)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count(),
            Endpoints: endpoints,
            Warnings: Array.Empty<string>(),
            Message: "사용하지 않는 분석 메시지",
            Limitation: "사용하지 않는 분석 한계");

    private static ProxyDirectiveRouteAnalysisExecutionResult<
        ProxyEndpointRouteAnalysisResult> Execute(
            ProxyEndpointRouteAnalysisResult analysis,
            string host = "renderer.example.invalid") =>
        ProxyDirectiveRouteAnalysisExecutor.ExecuteAsync(
                ProxyDirectiveSourceSelectionPolicy.Select(
                    targetDecisionWasEvaluated: true,
                    targetDecisionIsDirect: false,
                    targetSpecificDirective:
                        $"PROXY {host}:8080; DIRECT",
                    manualProxyConfigured: false,
                    manualProxyDirective: null),
                (_, _) => Task.FromResult(analysis))
            .GetAwaiter()
            .GetResult();

    private static void Ensure(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
