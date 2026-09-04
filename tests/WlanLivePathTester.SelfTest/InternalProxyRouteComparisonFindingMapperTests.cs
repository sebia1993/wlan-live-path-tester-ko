using System.Runtime.CompilerServices;
using System.Text.Json;
using WlanLivePathTester.Core.Reporting;
using WlanLivePathTester.Core.Routing;

namespace WlanLivePathTester.SelfTest;

internal static class InternalProxyRouteComparisonFindingMapperTests
{
#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Run()
    {
        VerifyFindingContract(
            InternalProxyRouteComparisonStatus.Ready,
            "INTERNAL_PROXY_ROUTE_SAME_INTERFACE",
            "Information");
        VerifyFindingContract(
            InternalProxyRouteComparisonStatus.Diverged,
            "INTERNAL_PROXY_ROUTE_DIVERGED",
            "Information");
        VerifyFindingContract(
            InternalProxyRouteComparisonStatus.Ambiguous,
            "INTERNAL_PROXY_ROUTE_AMBIGUOUS",
            "Warning");
        VerifyFindingContract(
            InternalProxyRouteComparisonStatus.Incomplete,
            "INTERNAL_PROXY_ROUTE_INCOMPLETE",
            "Warning");
        DoesNotExposeRawIdentityInFinding();
        Console.WriteLine(
            "PASS internal and proxy route comparison finding tests");
    }

    private static void VerifyFindingContract(
        InternalProxyRouteComparisonStatus status,
        string expectedCode,
        string expectedSeverity)
    {
        InternalProxyRouteComparisonResult result = CreateResult(status);
        ReportFinding finding =
            InternalProxyRouteComparisonFindingMapper.FromResult(result);

        Ensure(finding.Code == expectedCode,
            $"비교 상태 {status}의 Finding 코드가 잘못됐습니다.");
        Ensure(finding.Severity == expectedSeverity,
            $"비교 상태 {status}의 Finding 심각도가 잘못됐습니다.");
        Ensure(finding.Evidence.Contains(
                $"비교 상태 {status}",
                StringComparison.Ordinal),
            "Finding 근거에 구조화 비교 상태가 필요합니다.");
        Ensure(finding.Evidence.Contains(
                "프록시 후보 2개",
                StringComparison.Ordinal),
            "Finding 근거에 후보 수가 필요합니다.");
        Ensure(finding.Interpretation == result.Interpretation
               && finding.Limitation == result.Limitation
               && finding.NextStep == result.NextStep,
            "비교 결과의 해석·한계·조치를 Finding에서 유지해야 합니다.");
    }

    private static void DoesNotExposeRawIdentityInFinding()
    {
        const string secretGuid =
            "C2B2C3D4-E5F6-47A8-9123-1234567890AB";
        const string secretHost =
            "proxy-secret.example.invalid";
        const string secretUrl =
            "https://internal-secret.example.invalid/private.bin";
        InternalProxyRouteComparisonResult result = CreateResult(
            InternalProxyRouteComparisonStatus.Diverged) with
        {
            InternalInterfaceFingerprint = "0123456789",
            ProxyInterfaceFingerprints = ["abcdef0123"],
            Message =
                "내부 DIRECT 대상과 프록시 엔드포인트가 서로 다른 Windows 로컬 인터페이스를 선택했습니다.",
            Interpretation =
                "현재 PC에서 내부 경로와 프록시 경로의 첫 로컬 송출 NIC가 분리돼 있습니다.",
            Limitation =
                "인터페이스 차이만으로 장애를 확정할 수 없습니다.",
            NextStep =
                "인터페이스 범주와 VPN 정책을 확인하십시오."
        };
        ReportFinding finding =
            InternalProxyRouteComparisonFindingMapper.FromResult(result);
        string json = JsonSerializer.Serialize(finding);

        string[] forbidden =
        [
            secretGuid,
            secretHost,
            secretUrl,
            "internal-secret.example.invalid"
        ];
        foreach (string value in forbidden)
        {
            Ensure(!json.Contains(
                    value,
                    StringComparison.OrdinalIgnoreCase),
                $"Finding에 원문 인터페이스·호스트·URL이 남았습니다: {value}");
        }

        Ensure(!json.Contains(
                result.InternalInterfaceFingerprint!,
                StringComparison.Ordinal)
               && !json.Contains(
                   result.ProxyInterfaceFingerprints.Single(),
                   StringComparison.Ordinal),
            "일반 Finding은 축약 인터페이스 지문도 Evidence에 자동 복사하지 않아야 합니다.");
    }

    private static InternalProxyRouteComparisonResult CreateResult(
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
                InternalProxyRouteComparisonCode.DifferentLocalInterface,
            InternalProxyRouteComparisonStatus.Ambiguous =>
                InternalProxyRouteComparisonCode.ProxyRouteAmbiguous,
            _ => InternalProxyRouteComparisonCode.ProxyAnalysisIncomplete
        };

        return new InternalProxyRouteComparisonResult(
            Status: status,
            Relation: relation,
            Code: code,
            InternalRouteStatus: "Success",
            ProxyAnalysisStatus: status
                == InternalProxyRouteComparisonStatus.Incomplete
                    ? "PartialSuccess"
                    : "Success",
            InternalInterfaceFingerprint: "0123456789",
            InternalInterfaceCategory: "Wireless",
            ProxyInterfaceFingerprints: ["abcdef0123"],
            ProxyInterfaceCategories: ["Tunnel"],
            ProxyEndpointCount: 2,
            SuccessfulProxyRouteCount: status
                == InternalProxyRouteComparisonStatus.Incomplete
                    ? 1
                    : 2,
            DirectDirectiveCount: 1,
            ProxyAnalysisWasTruncated: status
                == InternalProxyRouteComparisonStatus.Incomplete,
            ExactIdentityComparisonPerformed: status is
                InternalProxyRouteComparisonStatus.Ready
                or InternalProxyRouteComparisonStatus.Diverged,
            Message: "합성 비교 메시지",
            Interpretation: "합성 비교 해석",
            Limitation: "합성 비교 한계",
            NextStep: "합성 다음 단계");
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
