using System.Runtime.CompilerServices;
using System.Text.Json;
using WlanLivePathTester.Core.NetworkEnvironment;
using WlanLivePathTester.Core.Proxy;
using WlanLivePathTester.Core.Routing;

namespace WlanLivePathTester.SelfTest;

internal static class InternalProxyExactRouteComparisonV3Tests
{
    private const string InternalInterfaceId =
        "91B2C3D4-E5F6-47A8-9123-1234567890AB";
    private const string AlternateInterfaceId =
        "A2B2C3D4-E5F6-47A8-9123-1234567890AB";
    private const string ThirdInterfaceId =
        "B2B2C3D4-E5F6-47A8-9123-1234567890AB";
    private static readonly DateTimeOffset EvaluatedAt =
        DateTimeOffset.UnixEpoch.AddDays(8);

#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Run()
    {
        SameExactGuidWithDifferentFormattingIsReady();
        DifferentExactGuidIsDiverged();
        MultipleExactProxyGuidsAreAmbiguousEvenWithSpoofedFingerprint();
        StructuredMultipleInterfaceStatesAreAmbiguous();
        DirectBlockedUnavailableAndCanceledAreIncomplete();
        PartialParseOrRouteEvidenceIsIncomplete();
        DisplayFingerprintWithoutExactIdentityIsIncomplete();
        WrongInternalPurposeAndIdentityAreRejected();
        ExactIdentityIsMemoryOnlyAndComparisonResultIsRedacted();
        Console.WriteLine(
            "PASS exact internal DIRECT and proxy route comparison v3 tests");
    }

    private static void SameExactGuidWithDifferentFormattingIsReady()
    {
        DestinationRouteEvidence internalRoute = CreateInternalRoute(
            "{" + InternalInterfaceId.ToLowerInvariant() + "}");
        ProxyEndpointRouteAnalysisResult analysis = CreateAnalysis(
        [
            CreateEndpoint(
                sequence: 1,
                exactInterfaceId: InternalInterfaceId,
                NetworkAdapterCategory.Wireless),
            CreateEndpoint(
                sequence: 2,
                exactInterfaceId:
                    "{" + InternalInterfaceId.ToLowerInvariant() + "}",
                NetworkAdapterCategory.Wireless)
        ],
        directFallback: true,
        directSequence: 3);
        ProxyDirectiveRouteAnalysisExecutionResult<
            ProxyEndpointRouteAnalysisResult> execution = Execute(
                CreateSelectedProxy(
                    "PROXY first.example.invalid:8080; PROXY second.example.invalid:8080; DIRECT"),
                analysis);

        InternalProxyRouteComparisonResult result =
            InternalProxyRouteComparisonEvaluator.Evaluate(
                internalRoute,
                execution,
                EvaluatedAt);

        Ensure(result.Status
               == InternalProxyRouteComparisonStatus.Ready,
            $"같은 정확 GUID 경로는 Ready여야 합니다: {result.Status}");
        Ensure(result.Relation
               == InternalProxyRouteRelation.SameInterface
               && result.Code
                   == InternalProxyRouteComparisonCode
                       .SameLocalInterface,
            "같은 GUID는 SameInterface와 고정 코드를 가져야 합니다.");
        Ensure(result.ExactIdentityComparisonPerformed
               && result.HasCompleteComparableEvidence,
            "Ready는 전체 GUID 정확 비교가 수행된 완전 증거여야 합니다.");
        Ensure(result.ProxyApplicableEndpointCount == 2
               && result.ProxyAnalyzedEndpointCount == 2
               && result.ProxySuccessfulEndpointCount == 2,
            "프록시 후보·분석·성공 수를 유지해야 합니다.");
        Ensure(result.ProxyDirectPresent
               && result.ProxyDirectFallbackPresent
               && !result.ProxyDirectIsPrimary,
            "프록시 뒤 DIRECT fallback 상태를 유지해야 합니다.");
        Ensure(result.ProxyInterfaceFingerprints.Count == 1
               && result.ProxyInterfaceCategories
                   .SequenceEqual([NetworkAdapterCategory.Wireless]),
            "같은 인터페이스의 안전 지문과 범주를 하나로 요약해야 합니다.");
        Ensure(result.EvaluatedAt == EvaluatedAt,
            "주입한 평가 시각을 유지해야 합니다.");
    }

    private static void DifferentExactGuidIsDiverged()
    {
        ProxyEndpointRouteAnalysisResult analysis = CreateAnalysis(
        [
            CreateEndpoint(
                1,
                AlternateInterfaceId,
                NetworkAdapterCategory.Tunnel,
                isVirtual: true,
                isVpn: true)
        ]);
        InternalProxyRouteComparisonResult result =
            InternalProxyRouteComparisonEvaluator.Evaluate(
                CreateInternalRoute(InternalInterfaceId),
                Execute(
                    CreateSelectedProxy(
                        "PROXY tunnel.example.invalid:8080"),
                    analysis),
                EvaluatedAt);

        Ensure(result.Status
               == InternalProxyRouteComparisonStatus.Diverged,
            "서로 다른 정확 GUID는 Diverged여야 합니다.");
        Ensure(result.Relation
               == InternalProxyRouteRelation.DifferentInterface
               && result.Code
                   == InternalProxyRouteComparisonCode
                       .DifferentLocalInterface,
            "다른 GUID는 DifferentInterface와 고정 코드를 가져야 합니다.");
        Ensure(result.ExactIdentityComparisonPerformed
               && result.HasCompleteComparableEvidence,
            "Diverged도 충분한 정확 비교 근거를 가져야 합니다.");
        Ensure(result.ProxyInterfaceCategories
                .SequenceEqual([NetworkAdapterCategory.Tunnel]),
            "프록시 터널 인터페이스 범주를 안전하게 유지해야 합니다.");
        Ensure(result.Interpretation.Contains(
                "분할 라우팅",
                StringComparison.Ordinal),
            "경로 분리가 의도된 정책일 수 있다는 설명이 필요합니다.");
    }

    private static void
        MultipleExactProxyGuidsAreAmbiguousEvenWithSpoofedFingerprint()
    {
        const string spoofedFingerprint = "0123456789";
        ProxyEndpointRouteEvidenceItem first = CreateEndpoint(
            1,
            AlternateInterfaceId,
            NetworkAdapterCategory.Ethernet) with
        {
            SelectedInterfaceFingerprint = spoofedFingerprint
        };
        ProxyEndpointRouteEvidenceItem second = CreateEndpoint(
            2,
            ThirdInterfaceId,
            NetworkAdapterCategory.Tunnel) with
        {
            SelectedInterfaceFingerprint = spoofedFingerprint
        };
        ProxyEndpointRouteAnalysisResult analysis = CreateAnalysis(
            [first, second],
            distinctInterfaceCount: 1);

        InternalProxyRouteComparisonResult result =
            InternalProxyRouteComparisonEvaluator.Evaluate(
                CreateInternalRoute(InternalInterfaceId),
                Execute(
                    CreateSelectedProxy(
                        "PROXY one.example.invalid:8080; PROXY two.example.invalid:8080"),
                    analysis),
                EvaluatedAt);

        Ensure(result.Status
               == InternalProxyRouteComparisonStatus.Ambiguous,
            "축약 지문이 같아도 전체 GUID가 둘이면 Ambiguous여야 합니다.");
        Ensure(result.Relation
               == InternalProxyRouteRelation.MultipleInterfaces
               && result.Code
                   == InternalProxyRouteComparisonCode
                       .ProxyRouteAmbiguous,
            "복수 정확 NIC를 구조화된 모호성으로 표시해야 합니다.");
        Ensure(!result.ExactIdentityComparisonPerformed
               && !result.HasCompleteComparableEvidence,
            "복수 NIC 중 하나를 내부 경로와 임의 비교하면 안 됩니다.");
    }

    private static void StructuredMultipleInterfaceStatesAreAmbiguous()
    {
        ProxyEndpointRouteEvidenceItem endpoint = CreateEndpoint(
            1,
            AlternateInterfaceId,
            NetworkAdapterCategory.Ethernet) with
        {
            RouteStatus = DestinationRouteEvidenceStatus.MultipleInterfaces
        };
        ProxyEndpointRouteAnalysisResult analysis = CreateAnalysis(
            [endpoint],
            status: ProxyEndpointRouteAnalysisStatus.MultipleInterfaces,
            successfulCount: 1,
            distinctInterfaceCount: 2);

        InternalProxyRouteComparisonResult result =
            InternalProxyRouteComparisonEvaluator.Evaluate(
                CreateInternalRoute(InternalInterfaceId),
                Execute(
                    CreateSelectedProxy(
                        "PROXY multi.example.invalid:8080"),
                    analysis),
                EvaluatedAt);

        Ensure(result.Status
               == InternalProxyRouteComparisonStatus.Ambiguous
               && result.Code
                   == InternalProxyRouteComparisonCode
                       .ProxyRouteAmbiguous,
            "기존 경로 분석기의 MultipleInterfaces를 그대로 모호성으로 유지해야 합니다.");
    }

    private static void
        DirectBlockedUnavailableAndCanceledAreIncomplete()
    {
        ProxyDirectiveRouteAnalysisExecutionResult<
            ProxyEndpointRouteAnalysisResult> direct =
            ProxyDirectiveRouteAnalysisExecutor.ExecuteAsync<
                    ProxyEndpointRouteAnalysisResult>(
                    ProxyDirectiveSourceSelectionPolicy.Select(
                        targetDecisionWasEvaluated: true,
                        targetDecisionIsDirect: true,
                        targetSpecificDirective: null,
                        manualProxyConfigured: true,
                        manualProxyDirective:
                            "PROXY ignored.example.invalid:8080"),
                    (_, _) => Task.FromResult(CreateAnalysis([])))
                .GetAwaiter()
                .GetResult();
        ProxyDirectiveRouteAnalysisExecutionResult<
            ProxyEndpointRouteAnalysisResult> blocked =
            ProxyDirectiveRouteAnalysisExecutor.ExecuteAsync<
                    ProxyEndpointRouteAnalysisResult>(
                    ProxyDirectiveSourceSelectionPolicy.Select(
                        targetDecisionWasEvaluated: true,
                        targetDecisionIsDirect: false,
                        targetSpecificDirective: "DIRECT",
                        manualProxyConfigured: true,
                        manualProxyDirective:
                            "PROXY ignored.example.invalid:8080"),
                    (_, _) => Task.FromResult(CreateAnalysis([])))
                .GetAwaiter()
                .GetResult();
        ProxyDirectiveRouteAnalysisExecutionResult<
            ProxyEndpointRouteAnalysisResult> unavailable =
            ProxyDirectiveRouteAnalysisExecutor.ExecuteAsync<
                    ProxyEndpointRouteAnalysisResult>(
                    ProxyDirectiveSourceSelectionPolicy.Select(
                        targetDecisionWasEvaluated: false,
                        targetDecisionIsDirect: false,
                        targetSpecificDirective: null,
                        manualProxyConfigured: false,
                        manualProxyDirective: null),
                    (_, _) => Task.FromResult(CreateAnalysis([])))
                .GetAwaiter()
                .GetResult();
        using CancellationTokenSource source = new();
        source.Cancel();
        ProxyDirectiveRouteAnalysisExecutionResult<
            ProxyEndpointRouteAnalysisResult> canceled =
            ProxyDirectiveRouteAnalysisExecutor.ExecuteAsync(
                    CreateSelectedProxy(
                        "PROXY canceled.example.invalid:8080"),
                    (_, _) => Task.FromResult(CreateAnalysis([])),
                    source.Token)
                .GetAwaiter()
                .GetResult();

        (ProxyDirectiveRouteAnalysisExecutionResult<
                ProxyEndpointRouteAnalysisResult> Execution,
            InternalProxyRouteComparisonCode Code)[] cases =
        [
            (direct, InternalProxyRouteComparisonCode.ProxyDirectOnly),
            (blocked, InternalProxyRouteComparisonCode.ProxySourceBlocked),
            (unavailable,
                InternalProxyRouteComparisonCode.ProxySourceUnavailable),
            (canceled,
                InternalProxyRouteComparisonCode.ProxyExecutionCanceled)
        ];

        foreach ((ProxyDirectiveRouteAnalysisExecutionResult<
                     ProxyEndpointRouteAnalysisResult> execution,
                  InternalProxyRouteComparisonCode code) in cases)
        {
            InternalProxyRouteComparisonResult result =
                InternalProxyRouteComparisonEvaluator.Evaluate(
                    CreateInternalRoute(InternalInterfaceId),
                    execution,
                    EvaluatedAt);
            Ensure(result.Status
                   == InternalProxyRouteComparisonStatus.Incomplete,
                $"비실행·취소 상태는 Incomplete여야 합니다: {execution.Status}");
            Ensure(result.Code == code,
                $"실행 종료 원인 코드가 잘못됐습니다: {execution.Status}");
            Ensure(!result.ExactIdentityComparisonPerformed,
                "불완전 실행에서 전체 GUID 비교를 수행하면 안 됩니다.");
        }
    }

    private static void PartialParseOrRouteEvidenceIsIncomplete()
    {
        ProxyEndpointRouteAnalysisResult successAnalysis = CreateAnalysis(
        [
            CreateEndpoint(
                1,
                InternalInterfaceId,
                NetworkAdapterCategory.Wireless)
        ]);
        ProxyDirectiveSourceSelectionResult partialSelection =
            ProxyDirectiveSourceSelectionPolicy.Select(
                targetDecisionWasEvaluated: true,
                targetDecisionIsDirect: false,
                targetSpecificDirective:
                    "PROXY valid.example.invalid:8080; UNKNOWN invalid; DIRECT",
                manualProxyConfigured: false,
                manualProxyDirective: null);
        InternalProxyRouteComparisonResult parseRisk =
            InternalProxyRouteComparisonEvaluator.Evaluate(
                CreateInternalRoute(InternalInterfaceId),
                Execute(partialSelection, successAnalysis),
                EvaluatedAt);
        Ensure(parseRisk.Status
               == InternalProxyRouteComparisonStatus.Incomplete
               && parseRisk.Code
                   == InternalProxyRouteComparisonCode
                       .ProxyAnalysisIncomplete
               && parseRisk.ProxyParseErrorsPresent,
            "제외된 fallback 구간이 있으면 성공 후보만으로 완전 비교하면 안 됩니다.");

        ProxyEndpointRouteEvidenceItem failed = CreateEndpoint(
            2,
            exactInterfaceId: null,
            NetworkAdapterCategory.Other) with
        {
            RouteStatus = DestinationRouteEvidenceStatus.RouteNotFound,
            SelectedInterfaceFingerprint = null,
            SelectedInterfaceCategory = null
        };
        ProxyEndpointRouteAnalysisResult partialAnalysis = CreateAnalysis(
        [
            CreateEndpoint(
                1,
                InternalInterfaceId,
                NetworkAdapterCategory.Wireless),
            failed
        ],
        status: ProxyEndpointRouteAnalysisStatus.PartialSuccess,
        successfulCount: 1,
        distinctInterfaceCount: 1);
        InternalProxyRouteComparisonResult routeRisk =
            InternalProxyRouteComparisonEvaluator.Evaluate(
                CreateInternalRoute(InternalInterfaceId),
                Execute(
                    CreateSelectedProxy(
                        "PROXY success.example.invalid:8080; PROXY failed.example.invalid:8080"),
                    partialAnalysis),
                EvaluatedAt);
        Ensure(routeRisk.Status
               == InternalProxyRouteComparisonStatus.Incomplete
               && routeRisk.Code
                   == InternalProxyRouteComparisonCode
                       .ProxyAnalysisIncomplete,
            "일부 프록시 후보 실패는 Incomplete여야 합니다.");
    }

    private static void DisplayFingerprintWithoutExactIdentityIsIncomplete()
    {
        DestinationRouteEvidence internalRoute =
            CreateInternalRoute(InternalInterfaceId);
        string displayedFingerprint =
            internalRoute.SelectedInterface!.IdentityFingerprint;
        ProxyEndpointRouteEvidenceItem endpoint = CreateEndpoint(
            1,
            exactInterfaceId: null,
            NetworkAdapterCategory.Wireless) with
        {
            SelectedInterfaceFingerprint = displayedFingerprint,
            SelectedInterfaceIdentity = null
        };
        ProxyEndpointRouteAnalysisResult analysis = CreateAnalysis(
            [endpoint],
            distinctInterfaceCount: 1);

        InternalProxyRouteComparisonResult result =
            InternalProxyRouteComparisonEvaluator.Evaluate(
                internalRoute,
                Execute(
                    CreateSelectedProxy(
                        "PROXY fingerprint-only.example.invalid:8080"),
                    analysis),
                EvaluatedAt);

        Ensure(result.Status
               == InternalProxyRouteComparisonStatus.Incomplete
               && result.Code
                   == InternalProxyRouteComparisonCode
                       .ProxyExactIdentityUnavailable,
            "표시 지문만 같은 결과는 Ready가 될 수 없습니다.");
        Ensure(!result.ExactIdentityComparisonPerformed,
            "축약 지문 비교를 정확 GUID 비교로 표시하면 안 됩니다.");
        Ensure(result.ProxyInterfaceFingerprints
                .SequenceEqual([displayedFingerprint]),
            "안전한 표시 지문은 진단 근거로 유지할 수 있습니다.");
    }

    private static void WrongInternalPurposeAndIdentityAreRejected()
    {
        DestinationRouteEvidence wrongPurpose = CreateInternalRoute(
            InternalInterfaceId,
            RouteProbePurpose.ManualDestination);
        DestinationRouteEvidence nonGuid = CreateInternalRoute(
            "local-interface-1");
        ProxyDirectiveRouteAnalysisExecutionResult<
            ProxyEndpointRouteAnalysisResult> execution = Execute(
                CreateSelectedProxy(
                    "PROXY valid.example.invalid:8080"),
                CreateAnalysis(
                [
                    CreateEndpoint(
                        1,
                        InternalInterfaceId,
                        NetworkAdapterCategory.Wireless)
                ]));

        InternalProxyRouteComparisonResult purposeResult =
            InternalProxyRouteComparisonEvaluator.Evaluate(
                wrongPurpose,
                execution,
                EvaluatedAt);
        InternalProxyRouteComparisonResult identityResult =
            InternalProxyRouteComparisonEvaluator.Evaluate(
                nonGuid,
                execution,
                EvaluatedAt);

        Ensure(purposeResult.Code
               == InternalProxyRouteComparisonCode
                   .InternalPurposeMismatch,
            "일반 목적지 경로를 내부 DIRECT 기준으로 재사용하면 안 됩니다.");
        Ensure(identityResult.Code
               == InternalProxyRouteComparisonCode
                   .InternalExactIdentityUnavailable,
            "GUID가 아닌 내부 identity로 정확 비교하면 안 됩니다.");
    }

    private static void
        ExactIdentityIsMemoryOnlyAndComparisonResultIsRedacted()
    {
        const string secretHost =
            "proxy-secret.example.invalid";
        const string secretInternalTarget =
            "https://internal-secret.example.invalid/private.bin";
        const string secretDescription =
            "Corporate Secret Tunnel Adapter";
        ProxyEndpointRouteEvidenceItem endpoint = CreateEndpoint(
            1,
            AlternateInterfaceId,
            NetworkAdapterCategory.Tunnel,
            isVirtual: true,
            isVpn: true) with
        {
            EndpointLabel = secretHost,
            Message = secretInternalTarget,
            Warnings = [secretHost, secretDescription]
        };
        ProxyEndpointRouteAnalysisResult analysis = CreateAnalysis(
            [endpoint]);
        string analysisJson = JsonSerializer.Serialize(analysis);
        Ensure(!analysisJson.Contains(
                AlternateInterfaceId,
                StringComparison.OrdinalIgnoreCase),
            "메모리 전용 프록시 전체 GUID가 분석 JSON에 남으면 안 됩니다.");

        DestinationRouteEvidence internalRoute = CreateInternalRoute(
            InternalInterfaceId,
            targetLabel: secretInternalTarget,
            description: secretDescription);
        InternalProxyRouteComparisonResult result =
            InternalProxyRouteComparisonEvaluator.Evaluate(
                internalRoute,
                Execute(
                    CreateSelectedProxy(
                        $"PROXY {secretHost}:8080"),
                    analysis),
                EvaluatedAt);
        string comparisonJson = JsonSerializer.Serialize(result);

        foreach (string secret in new[]
                 {
                     secretHost,
                     secretInternalTarget,
                     "internal-secret.example.invalid",
                     InternalInterfaceId,
                     AlternateInterfaceId,
                     secretDescription
                 })
        {
            Ensure(!comparisonJson.Contains(
                    secret,
                    StringComparison.OrdinalIgnoreCase),
                $"비교 결과 JSON에 원문 경로·호스트·GUID·설명이 남았습니다: {secret}");
        }

        Ensure(comparisonJson.Contains(
                result.InternalInterfaceFingerprint!,
                StringComparison.Ordinal)
               && comparisonJson.Contains(
                   result.ProxyInterfaceFingerprints.Single(),
                   StringComparison.Ordinal),
            "비교 결과에는 안전한 인터페이스 지문을 유지해야 합니다.");
    }

    private static ProxyDirectiveSourceSelectionResult
        CreateSelectedProxy(string text) =>
        ProxyDirectiveSourceSelectionPolicy.Select(
            targetDecisionWasEvaluated: true,
            targetDecisionIsDirect: false,
            targetSpecificDirective: text,
            manualProxyConfigured: false,
            manualProxyDirective: null);

    private static ProxyDirectiveRouteAnalysisExecutionResult<
        ProxyEndpointRouteAnalysisResult> Execute(
            ProxyDirectiveSourceSelectionResult selection,
            ProxyEndpointRouteAnalysisResult analysis) =>
        ProxyDirectiveRouteAnalysisExecutor.ExecuteAsync(
                selection,
                (_, _) => Task.FromResult(analysis))
            .GetAwaiter()
            .GetResult();

    private static DestinationRouteEvidence CreateInternalRoute(
        string? interfaceId,
        RouteProbePurpose purpose =
            RouteProbePurpose.InternalDirectTarget,
        string targetLabel = "내부 DIRECT 대상",
        string description = "Synthetic Internal Adapter")
    {
        RouteInterfaceDescriptor? selected = interfaceId is null
            ? null
            : new RouteInterfaceDescriptor(
                InterfaceIdentity: interfaceId,
                DisplayName: description,
                Description: description,
                NativeInterfaceType: "Wireless80211",
                Category: NetworkAdapterCategory.Wireless,
                OperationalState: NetworkAdapterOperationalState.Up,
                HasDefaultGateway: true,
                IsVirtual: false,
                IsVpn: false);
        return new DestinationRouteEvidence(
            CapturedAt: EvaluatedAt,
            TargetLabel: targetLabel,
            Purpose: purpose,
            DnsWasUsed: true,
            ResolvedAddressCount: selected is null ? 0 : 1,
            Status: selected is null
                ? DestinationRouteEvidenceStatus.RouteNotFound
                : DestinationRouteEvidenceStatus.Success,
            SelectedInterface: selected,
            AddressEvidence: selected is null
                ? Array.Empty<RouteAddressEvidence>()
                :
                [
                    new RouteAddressEvidence(
                        RouteAddressFamilyKind.IPv4,
                        RouteAddressEvidenceStatus.Success,
                        selected,
                        NativeErrorCode: null,
                        Message: "합성 내부 경로")
                ],
            Warnings: Array.Empty<string>(),
            Message: "합성 내부 경로");
    }

    private static ProxyEndpointRouteAnalysisResult CreateAnalysis(
        IReadOnlyList<ProxyEndpointRouteEvidenceItem> endpoints,
        ProxyEndpointRouteAnalysisStatus status =
            ProxyEndpointRouteAnalysisStatus.Success,
        bool directFallback = false,
        int? directSequence = null,
        int? successfulCount = null,
        int? distinctInterfaceCount = null) =>
        new(
            CapturedAt: EvaluatedAt,
            Status: status,
            SourceKind: ProxyEndpointSourceKind.AutoProxyResult,
            ProxyDecision: directFallback
                ? ProxyEndpointDecision.ProxyWithDirectFallback
                : ProxyEndpointDecision.Proxy,
            TargetScheme: "https",
            DirectPresent: directFallback,
            DirectIsPrimary: false,
            DirectFallback: directFallback,
            DirectSequence: directSequence,
            ParsedEndpointCount: endpoints.Count,
            ApplicableEndpointCount: endpoints.Count,
            AnalyzedEndpointCount: endpoints.Count,
            SkippedAfterDirectCount: 0,
            SuccessfulEndpointCount: successfulCount
                ?? endpoints.Count(endpoint => endpoint.IsRouteSuccess),
            DistinctInterfaceCount: distinctInterfaceCount
                ?? endpoints
                    .Where(endpoint => endpoint.IsRouteSuccess)
                    .Select(endpoint =>
                        endpoint.SelectedInterfaceFingerprint)
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Count(),
            Endpoints: endpoints,
            Warnings: directFallback
                ? ["프록시 후보 뒤 DIRECT fallback"]
                : Array.Empty<string>(),
            Message: "합성 프록시 경로 분석",
            Limitation: "합성 프록시 경로 한계");

    private static ProxyEndpointRouteEvidenceItem CreateEndpoint(
        int sequence,
        string? exactInterfaceId,
        NetworkAdapterCategory category,
        bool isVirtual = false,
        bool isVpn = false)
    {
        string? fingerprint = exactInterfaceId is null
            ? null
            : RouteInterfaceFingerprint.Create(exactInterfaceId);
        return new ProxyEndpointRouteEvidenceItem(
            Sequence: sequence,
            EndpointLabel:
                $"프록시 후보 {sequence} · host#0123456789 · port 8080",
            HostFingerprint: "0123456789",
            AppliesToScheme: "https",
            Transport: ProxyEndpointTransport.Http,
            Port: 8080,
            RouteStatus: DestinationRouteEvidenceStatus.Success,
            WlanCorrelationStatus:
                exactInterfaceId is not null
                && RouteInterfaceFingerprint.Normalize(exactInterfaceId)
                    .Equals(
                        RouteInterfaceFingerprint.Normalize(
                            InternalInterfaceId),
                        StringComparison.OrdinalIgnoreCase)
                    ? RouteWlanCorrelationStatus.Matched
                    : RouteWlanCorrelationStatus.DifferentInterface,
            SelectedInterfaceFingerprint: fingerprint,
            SelectedInterfaceCategory: category,
            SelectedInterfaceIsVirtual: isVirtual,
            SelectedInterfaceIsVpn: isVpn,
            SelectedInterfaceIsUp: true,
            SelectedInterfaceHasDefaultGateway: true,
            ResolvedAddressCount: 1,
            SuccessfulAddressCount: 1,
            FailedAddressCount: 0,
            Message: "합성 프록시 경로",
            Warnings: Array.Empty<string>())
        {
            SelectedInterfaceIdentity = exactInterfaceId
        };
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
