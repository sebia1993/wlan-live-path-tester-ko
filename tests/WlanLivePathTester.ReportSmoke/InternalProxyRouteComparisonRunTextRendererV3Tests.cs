using System.Runtime.CompilerServices;
using WlanLivePathTester.Core.NetworkEnvironment;
using WlanLivePathTester.Core.Proxy;
using WlanLivePathTester.Core.Reporting;
using WlanLivePathTester.Core.Routing;

namespace WlanLivePathTester.ReportSmoke;

internal static class
    InternalProxyRouteComparisonRunTextRendererV3Tests
{
    private const string InternalTarget =
        "https://internal-render-secret.example.invalid/private.bin";
    private const string ProxyHost =
        "proxy-render-secret.example.invalid";
    private const string SecretEmail =
        "render-user@example.invalid";
    private const string SecretIp = "10.99.77.55";
    private const string SecretGuid =
        "44B2C3D4-E5F6-47A8-9123-1234567890AB";
    private const string InternalFingerprint = "0123456789";
    private const string ProxyFingerprint = "abcdef0123";

#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Run()
    {
        RendersCompletedComparisonAndOrderedProxyEntries();
        RendersDirectAndBlockedRunsWithoutInventingEvidence();
        SanitizesUntrustedStructuredDisplayFields();
        DoesNotReflectFreeFormRunComparisonOrRouteText();
        Console.WriteLine(
            "PASS coordinated route run safe text renderer v3 tests");
    }

    private static void
        RendersCompletedComparisonAndOrderedProxyEntries()
    {
        ProxyEndpointRouteAnalysisResult analysis = CreateAnalysis(
        [
            CreateEndpoint(
                sequence: 2,
                hostFingerprint: "112233aabb",
                interfaceFingerprint: ProxyFingerprint,
                NetworkAdapterCategory.Tunnel),
            CreateEndpoint(
                sequence: 1,
                hostFingerprint: "aabbccddee",
                interfaceFingerprint: InternalFingerprint,
                NetworkAdapterCategory.Wireless)
        ]);
        InternalProxyRouteComparisonRunResult run = CreateRun(
            InternalProxyRouteComparisonRunStatus.Completed,
            CreateComparison(
                InternalProxyRouteComparisonStatus.Diverged),
            CreateExecution(analysis));

        string text =
            InternalProxyRouteComparisonRunTextRenderer.Render(run);

        Ensure(text.Contains(
                "실행 상태: Completed",
                StringComparison.Ordinal)
               && text.Contains(
                   "상태 / 관계 / 원인: Diverged / DifferentInterface / DifferentLocalInterface",
                   StringComparison.Ordinal),
            "실행·비교 상태와 관계·원인 코드를 표시해야 합니다.");
        Ensure(text.Contains(
                "INTERNAL_PROXY_ROUTE_DIVERGED",
                StringComparison.Ordinal)
               && text.Contains(
                   "Information",
                   StringComparison.Ordinal),
            "고정 Finding 코드와 심각도를 표시해야 합니다.");
        Ensure(text.IndexOf("#1", StringComparison.Ordinal)
               < text.IndexOf("#2", StringComparison.Ordinal),
            "프록시 후보를 sequence 순서로 표시해야 합니다.");
        Ensure(text.Contains(
                "호스트 지문 aabbccddee",
                StringComparison.Ordinal)
               && text.Contains(
                   $"인터페이스 Wireless / {InternalFingerprint}",
                   StringComparison.Ordinal)
               && text.Contains(
                   $"인터페이스 Tunnel / {ProxyFingerprint}",
                   StringComparison.Ordinal),
            "검증된 호스트·인터페이스 지문과 범주가 필요합니다.");
        Ensure(text.Contains(
                "후보(파싱 / 적용 / 분석 / 성공): 2 / 2 / 2 / 2",
                StringComparison.Ordinal)
               && text.Contains(
                   "DIRECT(존재 / 첫 경로 / fallback): 예 / 아니오 / 예",
                   StringComparison.Ordinal),
            "실행 집계와 DIRECT 위치를 표시해야 합니다.");
    }

    private static void
        RendersDirectAndBlockedRunsWithoutInventingEvidence()
    {
        InternalProxyRouteComparisonRunResult direct = CreateRun(
            InternalProxyRouteComparisonRunStatus.DirectPathSelected,
            comparison: null,
            execution: null) with
        {
            DirectPresent = true,
            DirectIsPrimary = true,
            DirectFallback = false,
            InternalRouteReadPerformed = false,
            ProxyRouteAnalysisPerformed = false
        };
        InternalProxyRouteComparisonRunResult blocked = CreateRun(
            InternalProxyRouteComparisonRunStatus.ProxySourceBlocked,
            comparison: null,
            execution: null) with
        {
            ProxyPlanStatus =
                ProxyDirectiveRouteAnalysisPlanStatus.Blocked,
            ProxyPlanCode =
                ProxyDirectiveRouteAnalysisPlanCode
                    .InvalidSourceDecision,
            InternalRouteReadPerformed = false,
            ProxyRouteAnalysisPerformed = false
        };

        string directText =
            InternalProxyRouteComparisonRunTextRenderer.Render(direct);
        string blockedText =
            InternalProxyRouteComparisonRunTextRenderer.Render(blocked);

        Ensure(directText.Contains(
                "INTERNAL_PROXY_ROUTE_RUN_DIRECT_PRIMARY",
                StringComparison.Ordinal)
               && directText.Contains(
                   "구조화 비교 결과 없음",
                   StringComparison.Ordinal)
               && directText.Contains(
                   "분석된 프록시 후보 없음",
                   StringComparison.Ordinal),
            "DIRECT 우선에서는 프록시·비교 근거를 만들면 안 됩니다.");
        Ensure(blockedText.Contains(
                "INTERNAL_PROXY_ROUTE_RUN_SOURCE_BLOCKED",
                StringComparison.Ordinal)
               && blockedText.Contains(
                   "Blocked / InvalidSourceDecision",
                   StringComparison.Ordinal),
            "차단된 출처의 실행 계획과 Finding을 표시해야 합니다.");
        Ensure(directText.Contains(
                "내부 / 프록시 단계: 미수행 / 미수행",
                StringComparison.Ordinal)
               && blockedText.Contains(
                   "내부 / 프록시 단계: 미수행 / 미수행",
                   StringComparison.Ordinal),
            "zero-read 경계를 결과에 유지해야 합니다.");
    }

    private static void SanitizesUntrustedStructuredDisplayFields()
    {
        ProxyEndpointRouteEvidenceItem unsafeEndpoint = new(
            Sequence: -10,
            EndpointLabel: ProxyHost,
            HostFingerprint: SecretGuid,
            AppliesToScheme: InternalTarget,
            Transport: (ProxyEndpointTransport)999,
            Port: 70000,
            RouteStatus: (DestinationRouteEvidenceStatus)999,
            WlanCorrelationStatus:
                (RouteWlanCorrelationStatus)999,
            SelectedInterfaceFingerprint: SecretGuid,
            SelectedInterfaceCategory:
                (NetworkAdapterCategory)999,
            SelectedInterfaceIsVirtual: null,
            SelectedInterfaceIsVpn: null,
            SelectedInterfaceIsUp: null,
            SelectedInterfaceHasDefaultGateway: null,
            ResolvedAddressCount: -1,
            SuccessfulAddressCount: -2,
            FailedAddressCount: -3,
            Message: SecretEmail,
            Warnings: [SecretIp]);
        ProxyEndpointRouteAnalysisResult analysis = CreateAnalysis(
            [unsafeEndpoint]);
        InternalProxyRouteComparisonRunResult run = CreateRun(
            InternalProxyRouteComparisonRunStatus.Completed,
            CreateComparison(
                InternalProxyRouteComparisonStatus.Incomplete),
            CreateExecution(analysis)) with
        {
            TargetScheme = ProxyHost,
            ParsedProxyEndpointCount = -1,
            ApplicableProxyEndpointCount = -2,
            AnalyzedProxyEndpointCount = -3,
            SuccessfulProxyEndpointCount = -4,
            DistinctProxyInterfaceCount = -5
        };

        string text =
            InternalProxyRouteComparisonRunTextRenderer.Render(run);

        Ensure(text.Contains(
                "외부 대상 스킴: 확인 불가",
                StringComparison.Ordinal)
               && text.Contains(
                   "후보(파싱 / 적용 / 분석 / 성공): 0 / 0 / 0 / 0",
                   StringComparison.Ordinal),
            "허용되지 않는 스킴과 음수 집계를 안전값으로 치환해야 합니다.");
        Ensure(text.Contains(
                "#0 · Unknown · 범위 all · 포트 - · 호스트 지문 없음 · 경로 Unknown",
                StringComparison.Ordinal),
            "알 수 없는 enum·scope·port·fingerprint를 고정 안전값으로 치환해야 합니다.");
        Ensure(text.Contains(
                "인터페이스 확인 불가 / 없음",
                StringComparison.Ordinal)
               && text.Contains(
                   "주소 0/0 성공",
                   StringComparison.Ordinal),
            "알 수 없는 인터페이스와 음수 주소 집계를 안전하게 표시해야 합니다.");
    }

    private static void
        DoesNotReflectFreeFormRunComparisonOrRouteText()
    {
        ProxyEndpointRouteEvidenceItem endpoint =
            CreateEndpoint(
                sequence: 1,
                hostFingerprint: "112233aabb",
                interfaceFingerprint: ProxyFingerprint,
                NetworkAdapterCategory.Tunnel) with
            {
                EndpointLabel = ProxyHost,
                Message = InternalTarget,
                Warnings =
                    [$"{SecretEmail} {SecretIp} {SecretGuid}"]
            };
        ProxyEndpointRouteAnalysisResult analysis = CreateAnalysis(
            [endpoint]) with
        {
            Message = ProxyHost,
            Limitation = InternalTarget,
            Warnings = [SecretEmail, SecretIp]
        };
        InternalProxyRouteComparisonResult comparison =
            CreateComparison(
                InternalProxyRouteComparisonStatus.Diverged) with
            {
                Message = InternalTarget,
                Interpretation = ProxyHost,
                Limitation = SecretEmail,
                NextStep = $"{SecretIp} {SecretGuid}"
            };
        InternalProxyRouteComparisonRunResult run = CreateRun(
            InternalProxyRouteComparisonRunStatus.Completed,
            comparison,
            CreateExecution(analysis)) with
        {
            Message = $"{InternalTarget} {ProxyHost}",
            Limitation = $"{SecretEmail} {SecretIp} {SecretGuid}"
        };

        string text =
            InternalProxyRouteComparisonRunTextRenderer.Render(run);

        foreach (string secret in new[]
                 {
                     InternalTarget,
                     "internal-render-secret.example.invalid",
                     ProxyHost,
                     SecretEmail,
                     SecretIp,
                     SecretGuid
                 })
        {
            Ensure(!text.Contains(
                    secret,
                    StringComparison.OrdinalIgnoreCase),
                $"렌더러가 자유형 실행·비교·route 원문을 반사했습니다: {secret}");
        }

        Ensure(text.Contains(
                "호스트 지문 112233aabb",
                StringComparison.Ordinal)
               && text.Contains(
                   "INTERNAL_PROXY_ROUTE_DIVERGED",
                   StringComparison.Ordinal),
            "검증된 안전 지문과 고정 Finding은 유지해야 합니다.");
    }

    private static InternalProxyRouteComparisonRunResult CreateRun(
        InternalProxyRouteComparisonRunStatus status,
        InternalProxyRouteComparisonResult? comparison,
        ProxyDirectiveRouteAnalysisExecutionResult<
            ProxyEndpointRouteAnalysisResult>? execution) =>
        new(
            CompletedAt: DateTimeOffset.UnixEpoch.AddDays(40),
            Status: status,
            ProxySourceKind:
                ProxyDirectiveSourceKind.ManualProxyConfiguration,
            ProxySelectionStatus:
                ProxyDirectiveSourceSelectionStatus.Selected,
            ProxyPlanStatus:
                ProxyDirectiveRouteAnalysisPlanStatus
                    .AnalyzeProxyEndpoints,
            ProxyPlanCode:
                ProxyDirectiveRouteAnalysisPlanCode.ManualProxySelected,
            ProxyExecutionStatus: execution?.Status
                ?? ProxyDirectiveRouteAnalysisExecutionStatus.Completed,
            ProxyEndpointSourceKind:
                ProxyEndpointSourceKind.ManualServerList,
            ProxyDecision:
                ProxyEndpointDecision.ProxyWithDirectFallback,
            TargetScheme: "https",
            InternalRouteStatus:
                DestinationRouteEvidenceStatus.Success,
            ProxyRouteStatus: execution?.Analysis?.Status
                ?? ProxyEndpointRouteAnalysisStatus.Success,
            Comparison: comparison,
            ParsedProxyEndpointCount: 2,
            ApplicableProxyEndpointCount: 2,
            AnalyzedProxyEndpointCount: 2,
            SuccessfulProxyEndpointCount: 2,
            DistinctProxyInterfaceCount: 1,
            DirectPresent: true,
            DirectIsPrimary: false,
            DirectFallback: true,
            ProxyParseErrorsPresent: false,
            ExpectedWlanIdentityAvailable: true,
            InternalRouteReadPerformed: true,
            ProxyRouteAnalysisPerformed: true,
            Message: InternalTarget,
            Limitation: ProxyHost,
            InternalRouteEvidence: null,
            ProxyExecution: execution);

    private static InternalProxyRouteComparisonResult
        CreateComparison(
            InternalProxyRouteComparisonStatus status)
    {
        InternalProxyRouteRelation relation = status switch
        {
            InternalProxyRouteComparisonStatus.Ready =>
                InternalProxyRouteRelation.SameInterface,
            InternalProxyRouteComparisonStatus.Diverged =>
                InternalProxyRouteRelation.DifferentInterface,
            InternalProxyRouteComparisonStatus.Ambiguous =>
                InternalProxyRouteRelation.MultipleInterfaces,
            _ => InternalProxyRouteRelation.Unknown
        };
        InternalProxyRouteComparisonCode code = status switch
        {
            InternalProxyRouteComparisonStatus.Ready =>
                InternalProxyRouteComparisonCode.SameLocalInterface,
            InternalProxyRouteComparisonStatus.Diverged =>
                InternalProxyRouteComparisonCode
                    .DifferentLocalInterface,
            InternalProxyRouteComparisonStatus.Ambiguous =>
                InternalProxyRouteComparisonCode.ProxyRouteAmbiguous,
            _ => InternalProxyRouteComparisonCode
                .ProxyAnalysisIncomplete
        };

        return new InternalProxyRouteComparisonResult(
            EvaluatedAt: DateTimeOffset.UnixEpoch.AddDays(40),
            Status: status,
            Relation: relation,
            Code: code,
            InternalRouteStatus:
                DestinationRouteEvidenceStatus.Success,
            ProxyExecutionStatus:
                ProxyDirectiveRouteAnalysisExecutionStatus.Completed,
            ProxyAnalysisStatus:
                ProxyEndpointRouteAnalysisStatus.Success,
            ProxySourceKind:
                ProxyDirectiveSourceKind.ManualProxyConfiguration,
            ProxyPlanCode:
                ProxyDirectiveRouteAnalysisPlanCode.ManualProxySelected,
            InternalInterfaceFingerprint: InternalFingerprint,
            InternalInterfaceCategory:
                NetworkAdapterCategory.Wireless,
            ProxyInterfaceFingerprints: [ProxyFingerprint],
            ProxyInterfaceCategories:
                [NetworkAdapterCategory.Tunnel],
            ProxyApplicableEndpointCount: 2,
            ProxyAnalyzedEndpointCount: 2,
            ProxySuccessfulEndpointCount: 2,
            ProxyDistinctInterfaceCount: status
                == InternalProxyRouteComparisonStatus.Ambiguous
                    ? 2
                    : 1,
            ProxySkippedAfterDirectCount: 0,
            ProxyDirectPresent: true,
            ProxyDirectIsPrimary: false,
            ProxyDirectFallbackPresent: true,
            ProxyParseErrorsPresent: false,
            ExactIdentityComparisonPerformed: status is
                InternalProxyRouteComparisonStatus.Ready
                or InternalProxyRouteComparisonStatus.Diverged,
            Message: InternalTarget,
            Interpretation: ProxyHost,
            Limitation: SecretEmail,
            NextStep: $"{SecretIp} {SecretGuid}");
    }

    private static ProxyEndpointRouteAnalysisResult CreateAnalysis(
        IReadOnlyList<ProxyEndpointRouteEvidenceItem> endpoints) =>
        new(
            CapturedAt: DateTimeOffset.UnixEpoch.AddDays(40),
            Status: endpoints.Any(item => !item.IsRouteSuccess)
                ? ProxyEndpointRouteAnalysisStatus.PartialSuccess
                : ProxyEndpointRouteAnalysisStatus.Success,
            SourceKind: ProxyEndpointSourceKind.ManualServerList,
            ProxyDecision:
                ProxyEndpointDecision.ProxyWithDirectFallback,
            TargetScheme: "https",
            DirectPresent: true,
            DirectIsPrimary: false,
            DirectFallback: true,
            DirectSequence: endpoints.Count + 1,
            ParsedEndpointCount: endpoints.Count,
            ApplicableEndpointCount: endpoints.Count,
            AnalyzedEndpointCount: endpoints.Count,
            SkippedAfterDirectCount: 0,
            SuccessfulEndpointCount: endpoints.Count(item =>
                item.IsRouteSuccess),
            DistinctInterfaceCount: endpoints
                .Select(item => item.SelectedInterfaceFingerprint)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count(),
            Endpoints: endpoints,
            Warnings: [SecretEmail],
            Message: ProxyHost,
            Limitation: InternalTarget);

    private static ProxyEndpointRouteEvidenceItem CreateEndpoint(
        int sequence,
        string hostFingerprint,
        string interfaceFingerprint,
        NetworkAdapterCategory category) =>
        new(
            Sequence: sequence,
            EndpointLabel: ProxyHost,
            HostFingerprint: hostFingerprint,
            AppliesToScheme: "https",
            Transport: ProxyEndpointTransport.Http,
            Port: 8080,
            RouteStatus: DestinationRouteEvidenceStatus.Success,
            WlanCorrelationStatus: category
                == NetworkAdapterCategory.Wireless
                    ? RouteWlanCorrelationStatus.Matched
                    : RouteWlanCorrelationStatus.DifferentInterface,
            SelectedInterfaceFingerprint: interfaceFingerprint,
            SelectedInterfaceCategory: category,
            SelectedInterfaceIsVirtual:
                category == NetworkAdapterCategory.Tunnel,
            SelectedInterfaceIsVpn:
                category == NetworkAdapterCategory.Tunnel,
            SelectedInterfaceIsUp: true,
            SelectedInterfaceHasDefaultGateway: true,
            ResolvedAddressCount: 1,
            SuccessfulAddressCount: 1,
            FailedAddressCount: 0,
            Message: InternalTarget,
            Warnings: [SecretEmail]);

    private static ProxyDirectiveRouteAnalysisExecutionResult<
        ProxyEndpointRouteAnalysisResult> CreateExecution(
            ProxyEndpointRouteAnalysisResult analysis)
    {
        ProxyDirectiveSourceSelectionResult selection =
            ProxyDirectiveSourceSelectionPolicy.Select(
                targetDecisionWasEvaluated: false,
                targetDecisionIsDirect: false,
                targetSpecificDirective: null,
                manualProxyConfigured: true,
                manualProxyDirective:
                    $"PROXY {ProxyHost}:8080; DIRECT");
        return ProxyDirectiveRouteAnalysisExecutor.ExecuteAsync(
                selection,
                (_, _) => Task.FromResult(analysis))
            .GetAwaiter()
            .GetResult();
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
