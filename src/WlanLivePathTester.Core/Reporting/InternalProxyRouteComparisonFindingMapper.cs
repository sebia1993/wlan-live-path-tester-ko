using WlanLivePathTester.Core.Routing;

namespace WlanLivePathTester.Core.Reporting;

public static class InternalProxyRouteComparisonFindingMapper
{
    public static ReportFinding FromResult(
        InternalProxyRouteComparisonResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        (string code, string severity, string title) = result.Status switch
        {
            InternalProxyRouteComparisonStatus.Ready =>
                (
                    "INTERNAL_PROXY_ROUTE_SAME_INTERFACE",
                    "Information",
                    "내부 DIRECT와 프록시 경로의 로컬 인터페이스 일치"),
            InternalProxyRouteComparisonStatus.Diverged =>
                (
                    "INTERNAL_PROXY_ROUTE_DIVERGED",
                    "Information",
                    "내부 DIRECT와 프록시 경로의 로컬 인터페이스 분리"),
            InternalProxyRouteComparisonStatus.Ambiguous =>
                (
                    "INTERNAL_PROXY_ROUTE_AMBIGUOUS",
                    "Warning",
                    "내부 DIRECT·프록시 로컬 경로가 여러 인터페이스로 모호함"),
            _ =>
                (
                    "INTERNAL_PROXY_ROUTE_INCOMPLETE",
                    "Warning",
                    "내부 DIRECT·프록시 로컬 경로 비교 근거 불완전")
        };

        string evidence =
            $"비교 상태 {result.Status}, 관계 {result.Relation}, 원인 코드 {result.Code}, 내부 경로 상태 {result.InternalRouteStatus}, 프록시 분석 상태 {result.ProxyAnalysisStatus}, 프록시 후보 {result.ProxyEndpointCount}개, 성공 경로 {result.SuccessfulProxyRouteCount}개, DIRECT {result.DirectDirectiveCount}개, 후보 잘림 {(result.ProxyAnalysisWasTruncated ? "있음" : "없음")}, 전체 인터페이스 ID 정확 비교 {(result.ExactIdentityComparisonPerformed ? "수행" : "미수행")}입니다.";

        return new ReportFinding(
            Code: code,
            Severity: severity,
            Title: title,
            Evidence: evidence,
            Interpretation: result.Interpretation,
            Limitation: result.Limitation,
            NextStep: result.NextStep);
    }
}
