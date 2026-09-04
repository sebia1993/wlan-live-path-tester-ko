using System.Runtime.CompilerServices;
using System.Text.Json;
using WlanLivePathTester.Core.NetworkEnvironment;
using WlanLivePathTester.Core.Proxy;
using WlanLivePathTester.Core.Routing;

namespace WlanLivePathTester.SelfTest;

internal static class InternalProxyRouteComparisonEvaluatorTests
{
    private const string InternalInterfaceId =
        "91B2C3D4-E5F6-47A8-9123-1234567890AB";
    private const string AlternateInterfaceId =
        "A2B2C3D4-E5F6-47A8-9123-1234567890AB";
    private const string ThirdInterfaceId =
        "B2B2C3D4-E5F6-47A8-9123-1234567890AB";

#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Run()
    {
        ReportsReadyForTheSameExactInterface();
        ReportsDivergedForDifferentExactInterfaces();
        ReportsAmbiguousForMultipleProxyInterfaces();
        ReportsAmbiguousForInternalMultipleInterfaces();
        ReportsIncompleteForDirectOnlyAndMissingInputs();
        ReportsIncompleteForPartialOrTruncatedProxyEvidence();
        DoesNotCompareDisplayFingerprintsWithoutExactIdentity();
        RejectsWrongInternalPurposeAndNonGuidIdentity();
        DoesNotLeakRawLabelsOrInterfaceIdentity();
        Console.WriteLine(
            "PASS internal DIRECT and proxy route comparison tests");
    }

    private static void ReportsReadyForTheSameExactInterface()
    {
        DestinationRouteEvidence internalRoute = CreateRoute(
            RouteProbePurpose.InternalDirectTarget,
            "{" + InternalInterfaceId.ToLowerInvariant() + "}",
            NetworkAdapterCategory.Wireless,
            DestinationRouteEvidenceStatus.Success);
        ProxyEndpointRouteAnalysisResult proxy = CreateAnalysis(
            entries:
            [
                CreateProxyEntry(
                    1,
                    InternalInterfaceId,
                    NetworkAdapterCategory.Wireless),
                CreateProxyEntry(
                    2,
                    "{" + InternalInterfaceId.ToLowerInvariant() + "}",
                    NetworkAdapterCategory.Wireless),
                CreateDirectEntry(3)
            ]);

        InternalProxyRouteComparisonResult result =
            InternalProxyRouteComparisonEvaluator.Evaluate(
                internalRoute,
                proxy);

        Ensure(result.Status
               == InternalProxyRouteComparisonStatus.Ready,
            $"같은 정확 ID의 경로는 Ready여야 합니다: {result.Status}");
        Ensure(result.Relation
               == InternalProxyRouteRelation.SameInterface,
            "같은 정확 ID는 SameInterface 관계여야 합니다.");
        Ensure(result.Code
               == InternalProxyRouteComparisonCode.SameLocalInterface,
            "같은 로컬 인터페이스 코드가 필요합니다.");
        Ensure(result.ExactIdentityComparisonPerformed,
            "Ready 판정은 메모리 내 전체 GUID 정확 비교를 수행해야 합니다.");
        Ensure(result.HasCompleteComparableEvidence,
            "Ready 판정은 완전 비교 증거를 가져야 합니다.");
        Ensure(result.ProxyEndpointCount == 2
               && result.SuccessfulProxyRouteCount == 2
               && result.DirectDirectiveCount == 1,
            "프록시 후보·성공·DIRECT 수를 보존해야 합니다.");
        Ensure(result.ProxyInterfaceFingerprints.Count == 1,
            "동일 NIC를 사용하는 두 프록시 후보는 지문 한 개로 요약해야 합니다.");
    }

    private static void ReportsDivergedForDifferentExactInterfaces()
    {
        DestinationRouteEvidence internalRoute = CreateRoute(
            RouteProbePurpose.InternalDirectTarget,
            InternalInterfaceId,
            NetworkAdapterCategory.Wireless,
            DestinationRouteEvidenceStatus.Success);
        ProxyEndpointRouteAnalysisResult proxy = CreateAnalysis(
            entries:
            [
                CreateProxyEntry(
                    1,
                    AlternateInterfaceId,
                    NetworkAdapterCategory.Tunnel)
            ]);

        InternalProxyRouteComparisonResult result =
            InternalProxyRouteComparisonEvaluator.Evaluate(
                internalRoute,
                proxy);

        Ensure(result.Status
               == InternalProxyRouteComparisonStatus.Diverged,
            "서로 다른 정확 GUID는 Diverged여야 합니다.");
        Ensure(result.Relation
               == InternalProxyRouteRelation.DifferentInterface,
            "서로 다른 경로는 DifferentInterface 관계여야 합니다.");
        Ensure(result.Code
               == InternalProxyRouteComparisonCode.DifferentLocalInterface,
            "분리된 로컬 인터페이스 코드가 필요합니다.");
        Ensure(result.ExactIdentityComparisonPerformed,
            "Diverged도 전체 GUID 정확 비교 결과여야 합니다.");
        Ensure(result.Interpretation.Contains(
                "분할 라우팅",
                StringComparison.Ordinal),
            "분리 경로가 의도된 정책일 수 있다는 설명이 필요합니다.");
        Ensure(result.ProxyInterfaceCategories
                .SequenceEqual(["Tunnel"]),
            "프록시 경로의 안전한 인터페이스 범주를 유지해야 합니다.");
    }

    private static void ReportsAmbiguousForMultipleProxyInterfaces()
    {
        ProxyEndpointRouteAnalysisResult proxy = CreateAnalysis(
            entries:
            [
                CreateProxyEntry(
                    1,
                    AlternateInterfaceId,
                    NetworkAdapterCategory.Ethernet),
                CreateProxyEntry(
                    2,
                    ThirdInterfaceId,
                    NetworkAdapterCategory.Tunnel)
            ]);

        InternalProxyRouteComparisonResult result =
            InternalProxyRouteComparisonEvaluator.Evaluate(
                CreateInternalSuccess(),
                proxy);

        Ensure(result.Status
               == InternalProxyRouteComparisonStatus.Ambiguous,
            "프록시 후보가 서로 다른 정확 NIC를 선택하면 Ambiguous여야 합니다.");
        Ensure(result.Relation
               == InternalProxyRouteRelation.MultipleInterfaces,
            "복수 프록시 NIC는 MultipleInterfaces 관계여야 합니다.");
        Ensure(result.Code
               == InternalProxyRouteComparisonCode.ProxyRouteAmbiguous,
            "프록시 경로 모호성 코드가 필요합니다.");
        Ensure(!result.ExactIdentityComparisonPerformed,
            "여러 프록시 NIC 중 하나를 내부 경로와 임의 비교하면 안 됩니다.");
        Ensure(result.ProxyInterfaceFingerprints.Count == 2
               && result.ProxyInterfaceCategories.Count == 2,
            "복수 프록시 경로의 안전한 지문과 범주를 유지해야 합니다.");
    }

    private static void ReportsAmbiguousForInternalMultipleInterfaces()
    {
        DestinationRouteEvidence internalRoute = CreateRoute(
            RouteProbePurpose.InternalDirectTarget,
            interfaceId: null,
            NetworkAdapterCategory.Unknown,
            DestinationRouteEvidenceStatus.MultipleInterfaces);

        InternalProxyRouteComparisonResult result =
            InternalProxyRouteComparisonEvaluator.Evaluate(
                internalRoute,
                CreateAnalysis(
                    [
                        CreateProxyEntry(
                            1,
                            InternalInterfaceId,
                            NetworkAdapterCategory.Wireless)
                    ]));

        Ensure(result.Status
               == InternalProxyRouteComparisonStatus.Ambiguous,
            "내부 주소 계열이 여러 NIC를 선택하면 Ambiguous여야 합니다.");
        Ensure(result.Code
               == InternalProxyRouteComparisonCode.InternalRouteAmbiguous,
            "내부 경로 모호성 코드가 필요합니다.");
    }

    private static void ReportsIncompleteForDirectOnlyAndMissingInputs()
    {
        ProxyEndpointRouteAnalysisResult directOnly = CreateAnalysis(
            [CreateDirectEntry(1)],
            status: ProxyEndpointRouteAnalysisStatus.DirectOnly);
        InternalProxyRouteComparisonResult directResult =
            InternalProxyRouteComparisonEvaluator.Evaluate(
                CreateInternalSuccess(),
                directOnly);
        InternalProxyRouteComparisonResult missingInternal =
            InternalProxyRouteComparisonEvaluator.Evaluate(
                internalDirectRoute: null,
                directOnly);
        InternalProxyRouteComparisonResult missingProxy =
            InternalProxyRouteComparisonEvaluator.Evaluate(
                CreateInternalSuccess(),
                proxyAnalysis: null);

        Ensure(directResult.Status
               == InternalProxyRouteComparisonStatus.Incomplete
               && directResult.Code
                   == InternalProxyRouteComparisonCode.ProxyDirectOnly,
            "DIRECT-only는 비교할 프록시 엔드포인트가 없어 Incomplete여야 합니다.");
        Ensure(missingInternal.Code
               == InternalProxyRouteComparisonCode.InternalRouteMissing,
            "내부 경로 누락 코드를 유지해야 합니다.");
        Ensure(missingProxy.Code
               == InternalProxyRouteComparisonCode.ProxyAnalysisMissing,
            "프록시 분석 누락 코드를 유지해야 합니다.");
        Ensure(!directResult.ExactIdentityComparisonPerformed
               && !missingInternal.ExactIdentityComparisonPerformed
               && !missingProxy.ExactIdentityComparisonPerformed,
            "불완전 입력에서 정확 비교를 수행한 것으로 표시하면 안 됩니다.");
    }

    private static void
        ReportsIncompleteForPartialOrTruncatedProxyEvidence()
    {
        ProxyEndpointRouteEntry successful = CreateProxyEntry(
            1,
            InternalInterfaceId,
            NetworkAdapterCategory.Wireless);
        ProxyEndpointRouteEntry failed = CreateFailedProxyEntry(2);
        ProxyEndpointRouteAnalysisResult partial = CreateAnalysis(
            [successful, failed, CreateDirectEntry(3)],
            status: ProxyEndpointRouteAnalysisStatus.PartialSuccess,
            parseStatus: ProxyDirectiveParseStatus.PartialSuccess);
        ProxyEndpointRouteAnalysisResult truncated = CreateAnalysis(
            [successful, CreateDirectEntry(3)],
            status: ProxyEndpointRouteAnalysisStatus.PartialSuccess,
            wasTruncated: true);

        InternalProxyRouteComparisonResult partialResult =
            InternalProxyRouteComparisonEvaluator.Evaluate(
                CreateInternalSuccess(),
                partial);
        InternalProxyRouteComparisonResult truncatedResult =
            InternalProxyRouteComparisonEvaluator.Evaluate(
                CreateInternalSuccess(),
                truncated);

        Ensure(partialResult.Status
               == InternalProxyRouteComparisonStatus.Incomplete
               && partialResult.Code
                   == InternalProxyRouteComparisonCode.ProxyAnalysisIncomplete,
            "실패 후보나 부분 파싱이 있으면 전체 fallback 비교는 Incomplete여야 합니다.");
        Ensure(truncatedResult.Status
               == InternalProxyRouteComparisonStatus.Incomplete
               && truncatedResult.ProxyAnalysisWasTruncated,
            "후보 상한으로 잘린 분석은 Incomplete여야 합니다.");
    }

    private static void
        DoesNotCompareDisplayFingerprintsWithoutExactIdentity()
    {
        DestinationRouteEvidence internalRoute = CreateInternalSuccess();
        string sameDisplayedFingerprint =
            internalRoute.SelectedInterface!.IdentityFingerprint;
        ProxyEndpointRouteEntry fingerprintOnly = new(
            Sequence: 1,
            Kind: ProxyRouteDirectiveKind.HttpProxy,
            SourceSyntax: ProxyDirectiveSourceSyntax.PacKeyword,
            Scope: "all",
            Port: 8080,
            HostFingerprint: "0123456789",
            RedactedDisplay:
                "HttpProxy · 범위 all · 호스트 지문 0123456789 · 포트 8080",
            Status: ProxyEndpointRouteEntryStatus.Success,
            SelectedInterfaceFingerprint:
                sameDisplayedFingerprint,
            SelectedInterfaceCategory: "Wireless",
            SelectedInterfaceOperationalState: "Up",
            WlanCorrelationStatus:
                RouteWlanCorrelationStatus.Matched.ToString(),
            RouteEvidence: null,
            Message: "합성 직렬화 복원 결과");

        InternalProxyRouteComparisonResult result =
            InternalProxyRouteComparisonEvaluator.Evaluate(
                internalRoute,
                CreateAnalysis([fingerprintOnly]));

        Ensure(result.Status
               == InternalProxyRouteComparisonStatus.Incomplete,
            "표시 지문만 같은 결과는 Ready가 될 수 없습니다.");
        Ensure(result.Code
               == InternalProxyRouteComparisonCode.ExactIdentityUnavailable,
            "전체 GUID 미확인 코드를 사용해야 합니다.");
        Ensure(!result.ExactIdentityComparisonPerformed,
            "축약 지문 비교를 정확 ID 비교로 표시하면 안 됩니다.");
        Ensure(result.ProxyInterfaceFingerprints
                .SequenceEqual([sameDisplayedFingerprint]),
            "표시용 안전 지문은 진단 근거로 유지할 수 있습니다.");
    }

    private static void RejectsWrongInternalPurposeAndNonGuidIdentity()
    {
        DestinationRouteEvidence wrongPurpose = CreateRoute(
            RouteProbePurpose.ManualDestination,
            InternalInterfaceId,
            NetworkAdapterCategory.Wireless,
            DestinationRouteEvidenceStatus.Success);
        DestinationRouteEvidence nonGuid = CreateRoute(
            RouteProbePurpose.InternalDirectTarget,
            "local-interface-1",
            NetworkAdapterCategory.Wireless,
            DestinationRouteEvidenceStatus.Success);
        ProxyEndpointRouteAnalysisResult proxy = CreateAnalysis(
            [
                CreateProxyEntry(
                    1,
                    InternalInterfaceId,
                    NetworkAdapterCategory.Wireless)
            ]);

        InternalProxyRouteComparisonResult purposeResult =
            InternalProxyRouteComparisonEvaluator.Evaluate(
                wrongPurpose,
                proxy);
        InternalProxyRouteComparisonResult identityResult =
            InternalProxyRouteComparisonEvaluator.Evaluate(
                nonGuid,
                proxy);

        Ensure(purposeResult.Code
               == InternalProxyRouteComparisonCode.InternalPurposeMismatch,
            "일반 목적지 경로를 내부 DIRECT 기준으로 재사용하면 안 됩니다.");
        Ensure(identityResult.Code
               == InternalProxyRouteComparisonCode.ExactIdentityUnavailable,
            "GUID가 아닌 identity로 정확 NIC 비교를 수행하면 안 됩니다.");
    }

    private static void DoesNotLeakRawLabelsOrInterfaceIdentity()
    {
        const string secretUrl =
            "https://internal-secret.example.invalid/private.bin";
        const string secretProxyHost =
            "proxy-secret.example.invalid";
        const string secretDescription =
            "Corporate Secret Tunnel Adapter";
        DestinationRouteEvidence internalRoute = CreateRoute(
            RouteProbePurpose.InternalDirectTarget,
            InternalInterfaceId,
            NetworkAdapterCategory.Wireless,
            DestinationRouteEvidenceStatus.Success,
            secretUrl,
            secretDescription);
        ProxyEndpointRouteEntry proxyEntry = CreateProxyEntry(
            1,
            AlternateInterfaceId,
            NetworkAdapterCategory.Tunnel,
            secretProxyHost,
            secretDescription);

        InternalProxyRouteComparisonResult result =
            InternalProxyRouteComparisonEvaluator.Evaluate(
                internalRoute,
                CreateAnalysis([proxyEntry]));
        string json = JsonSerializer.Serialize(result);

        string[] forbidden =
        [
            secretUrl,
            "internal-secret.example.invalid",
            secretProxyHost,
            InternalInterfaceId,
            AlternateInterfaceId,
            secretDescription
        ];
        foreach (string value in forbidden)
        {
            Ensure(!json.Contains(
                    value,
                    StringComparison.OrdinalIgnoreCase),
                $"비교 결과 JSON에 원문 경로·호스트·인터페이스 정보가 남았습니다: {value}");
        }

        Ensure(json.Contains(
                result.InternalInterfaceFingerprint!,
                StringComparison.Ordinal)
               && json.Contains(
                   result.ProxyInterfaceFingerprints.Single(),
                   StringComparison.Ordinal),
            "비교 결과에는 비가역 인터페이스 지문을 유지해야 합니다.");
    }

    private static DestinationRouteEvidence CreateInternalSuccess() =>
        CreateRoute(
            RouteProbePurpose.InternalDirectTarget,
            InternalInterfaceId,
            NetworkAdapterCategory.Wireless,
            DestinationRouteEvidenceStatus.Success);

    private static DestinationRouteEvidence CreateRoute(
        RouteProbePurpose purpose,
        string? interfaceId,
        NetworkAdapterCategory category,
        DestinationRouteEvidenceStatus status,
        string targetLabel = "합성 대상",
        string description = "Synthetic Adapter")
    {
        RouteInterfaceDescriptor? selected = interfaceId is null
            ? null
            : new RouteInterfaceDescriptor(
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
            CapturedAt: DateTimeOffset.UnixEpoch,
            TargetLabel: targetLabel,
            Purpose: purpose,
            DnsWasUsed: true,
            ResolvedAddressCount: selected is null ? 2 : 1,
            Status: status,
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
                        Message: "합성 Windows 최적 경로")
                ],
            Warnings: Array.Empty<string>(),
            Message: "합성 경로 결과");
    }

    private static ProxyEndpointRouteEntry CreateProxyEntry(
        int sequence,
        string interfaceId,
        NetworkAdapterCategory category,
        string host = "proxy.example.invalid",
        string description = "Synthetic Proxy Adapter")
    {
        DestinationRouteEvidence route = CreateRoute(
            RouteProbePurpose.ProxyEndpoint,
            interfaceId,
            category,
            DestinationRouteEvidenceStatus.Success,
            targetLabel: $"프록시 후보 {sequence}",
            description);
        RouteInterfaceDescriptor selected = route.SelectedInterface!;
        return new ProxyEndpointRouteEntry(
            Sequence: sequence,
            Kind: ProxyRouteDirectiveKind.HttpProxy,
            SourceSyntax: ProxyDirectiveSourceSyntax.PacKeyword,
            Scope: "all",
            Port: 8080,
            HostFingerprint: ProxyHostFingerprint.Create(host),
            RedactedDisplay:
                $"HttpProxy · 범위 all · 호스트 지문 {ProxyHostFingerprint.Create(host)} · 포트 8080",
            Status: ProxyEndpointRouteEntryStatus.Success,
            SelectedInterfaceFingerprint:
                selected.IdentityFingerprint,
            SelectedInterfaceCategory: category.ToString(),
            SelectedInterfaceOperationalState:
                selected.OperationalState.ToString(),
            WlanCorrelationStatus:
                RouteWlanCorrelationStatus.NotEvaluated.ToString(),
            RouteEvidence: route,
            Message: "합성 프록시 경로 성공");
    }

    private static ProxyEndpointRouteEntry CreateFailedProxyEntry(
        int sequence) =>
        new(
            Sequence: sequence,
            Kind: ProxyRouteDirectiveKind.HttpProxy,
            SourceSyntax: ProxyDirectiveSourceSyntax.PacKeyword,
            Scope: "all",
            Port: 8080,
            HostFingerprint: "abcdef0123",
            RedactedDisplay:
                "HttpProxy · 범위 all · 호스트 지문 abcdef0123 · 포트 8080",
            Status: ProxyEndpointRouteEntryStatus.ResolutionFailed,
            SelectedInterfaceFingerprint: null,
            SelectedInterfaceCategory: null,
            SelectedInterfaceOperationalState: null,
            WlanCorrelationStatus:
                RouteWlanCorrelationStatus.RouteInterfaceUnavailable.ToString(),
            RouteEvidence: CreateRoute(
                RouteProbePurpose.ProxyEndpoint,
                interfaceId: null,
                NetworkAdapterCategory.Unknown,
                DestinationRouteEvidenceStatus.ResolutionFailed),
            Message: "합성 DNS 실패");

    private static ProxyEndpointRouteEntry CreateDirectEntry(
        int sequence) =>
        new(
            Sequence: sequence,
            Kind: ProxyRouteDirectiveKind.Direct,
            SourceSyntax: ProxyDirectiveSourceSyntax.PacKeyword,
            Scope: "all",
            Port: null,
            HostFingerprint: "없음",
            RedactedDisplay: "DIRECT",
            Status: ProxyEndpointRouteEntryStatus.Direct,
            SelectedInterfaceFingerprint: null,
            SelectedInterfaceCategory: null,
            SelectedInterfaceOperationalState: null,
            WlanCorrelationStatus:
                RouteWlanCorrelationStatus.NotEvaluated.ToString(),
            RouteEvidence: null,
            Message: "DIRECT는 조회하지 않음");

    private static ProxyEndpointRouteAnalysisResult CreateAnalysis(
        IReadOnlyList<ProxyEndpointRouteEntry> entries,
        ProxyEndpointRouteAnalysisStatus status =
            ProxyEndpointRouteAnalysisStatus.Success,
        ProxyDirectiveParseStatus parseStatus =
            ProxyDirectiveParseStatus.Success,
        bool wasTruncated = false) =>
        new(
            Status: status,
            ParseStatus: parseStatus,
            Entries: entries,
            ParseIssues: Array.Empty<ProxyDirectiveIssue>(),
            EndpointLimit: 8,
            WasTruncated: wasTruncated,
            Message: "합성 프록시 경로 분석");

    private static void Ensure(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
