using System.Runtime.CompilerServices;
using System.Text.Json;
using WlanLivePathTester.Core.NetworkEnvironment;
using WlanLivePathTester.Core.Proxy;
using WlanLivePathTester.Core.Reporting;
using WlanLivePathTester.Core.Routing;

namespace WlanLivePathTester.SelfTest;

internal static class InternalProxyExactRouteFindingV3Tests
{
#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Run()
    {
        VerifyFinding(
            InternalProxyRouteComparisonStatus.Ready,
            InternalProxyRouteRelation.SameInterface,
            InternalProxyRouteComparisonCode.SameLocalInterface,
            "INTERNAL_PROXY_ROUTE_SAME_INTERFACE",
            "Information");
        VerifyFinding(
            InternalProxyRouteComparisonStatus.Diverged,
            InternalProxyRouteRelation.DifferentInterface,
            InternalProxyRouteComparisonCode.DifferentLocalInterface,
            "INTERNAL_PROXY_ROUTE_DIVERGED",
            "Information");
        VerifyFinding(
            InternalProxyRouteComparisonStatus.Ambiguous,
            InternalProxyRouteRelation.MultipleInterfaces,
            InternalProxyRouteComparisonCode.ProxyRouteAmbiguous,
            "INTERNAL_PROXY_ROUTE_AMBIGUOUS",
            "Warning");
        VerifyFinding(
            InternalProxyRouteComparisonStatus.Incomplete,
            InternalProxyRouteRelation.Unknown,
            InternalProxyRouteComparisonCode.ProxyAnalysisIncomplete,
            "INTERNAL_PROXY_ROUTE_INCOMPLETE",
            "Warning");
        FindingDoesNotCopyFingerprintsOrRawIdentity();
        Console.WriteLine(
            "PASS exact route comparison finding v3 tests");
    }

    private static void VerifyFinding(
        InternalProxyRouteComparisonStatus status,
        InternalProxyRouteRelation relation,
        InternalProxyRouteComparisonCode comparisonCode,
        string expectedFindingCode,
        string expectedSeverity)
    {
        InternalProxyRouteComparisonResult result = CreateResult(
            status,
            relation,
            comparisonCode);
        ReportFinding finding =
            InternalProxyRouteComparisonFindingMapper.FromResult(result);

        Ensure(finding.Code == expectedFindingCode,
            $"비교 상태 {status}의 Finding 코드가 잘못됐습니다.");
        Ensure(finding.Severity == expectedSeverity,
            $"비교 상태 {status}의 Finding 심각도가 잘못됐습니다.");
        Ensure(finding.Evidence.Contains(
                $"비교 상태는 {status}",
                StringComparison.Ordinal)
               && finding.Evidence.Contains(
                   "적용 후보 2개",
                   StringComparison.Ordinal)
               && finding.Evidence.Contains(
                   "정확 비교",
                   StringComparison.Ordinal),
            "Finding 근거에 구조화 상태·개수·정확 비교 여부가 필요합니다.");
        Ensure(finding.Interpretation == result.Interpretation
               && finding.Limitation == result.Limitation
               && finding.NextStep == result.NextStep,
            "비교 결과의 해석·한계·조치를 Finding에서 유지해야 합니다.");
    }

    private static void FindingDoesNotCopyFingerprintsOrRawIdentity()
    {
        const string internalFingerprint = "0123456789";
        const string proxyFingerprint = "abcdef0123";
        const string secretGuid =
            "C2B2C3D4-E5F6-47A8-9123-1234567890AB";
        const string secretHost =
            "proxy-secret.example.invalid";
        InternalProxyRouteComparisonResult result = CreateResult(
            InternalProxyRouteComparisonStatus.Diverged,
            InternalProxyRouteRelation.DifferentInterface,
            InternalProxyRouteComparisonCode.DifferentLocalInterface) with
        {
            InternalInterfaceFingerprint = internalFingerprint,
            ProxyInterfaceFingerprints = [proxyFingerprint]
        };

        ReportFinding finding =
            InternalProxyRouteComparisonFindingMapper.FromResult(result);
        string json = JsonSerializer.Serialize(finding);
        foreach (string forbidden in new[]
                 {
                     internalFingerprint,
                     proxyFingerprint,
                     secretGuid,
                     secretHost
                 })
        {
            Ensure(!json.Contains(
                    forbidden,
                    StringComparison.OrdinalIgnoreCase),
                $"일반 Finding에 인터페이스·호스트 식별값이 남았습니다: {forbidden}");
        }
    }

    private static InternalProxyRouteComparisonResult CreateResult(
        InternalProxyRouteComparisonStatus status,
        InternalProxyRouteRelation relation,
        InternalProxyRouteComparisonCode code) =>
        new(
            EvaluatedAt: DateTimeOffset.UnixEpoch,
            Status: status,
            Relation: relation,
            Code: code,
            InternalRouteStatus: DestinationRouteEvidenceStatus.Success,
            ProxyExecutionStatus:
                ProxyDirectiveRouteAnalysisExecutionStatus.Completed,
            ProxyAnalysisStatus: status
                == InternalProxyRouteComparisonStatus.Incomplete
                    ? ProxyEndpointRouteAnalysisStatus.PartialSuccess
                    : ProxyEndpointRouteAnalysisStatus.Success,
            ProxySourceKind:
                ProxyDirectiveSourceKind.TargetSpecificAutoProxy,
            ProxyPlanCode:
                ProxyDirectiveRouteAnalysisPlanCode
                    .TargetSpecificProxySelected,
            InternalInterfaceFingerprint: "0123456789",
            InternalInterfaceCategory:
                NetworkAdapterCategory.Wireless,
            ProxyInterfaceFingerprints: ["abcdef0123"],
            ProxyInterfaceCategories:
                [NetworkAdapterCategory.Tunnel],
            ProxyApplicableEndpointCount: 2,
            ProxyAnalyzedEndpointCount: 2,
            ProxySuccessfulEndpointCount: status
                == InternalProxyRouteComparisonStatus.Incomplete
                    ? 1
                    : 2,
            ProxyDistinctInterfaceCount: status
                == InternalProxyRouteComparisonStatus.Ambiguous
                    ? 2
                    : 1,
            ProxySkippedAfterDirectCount: 0,
            ProxyDirectPresent: true,
            ProxyDirectIsPrimary: false,
            ProxyDirectFallbackPresent: true,
            ProxyParseErrorsPresent: status
                == InternalProxyRouteComparisonStatus.Incomplete,
            ExactIdentityComparisonPerformed: status is
                InternalProxyRouteComparisonStatus.Ready
                    or InternalProxyRouteComparisonStatus.Diverged,
            Message: "합성 비교 메시지",
            Interpretation: "합성 비교 해석",
            Limitation: "합성 비교 한계",
            NextStep: "합성 다음 단계");

    private static void Ensure(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
