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

        string evidence = string.Join(
            " ",
            $"비교 상태는 {result.Status}, 관계는 {result.Relation}, 원인 코드는 {result.Code}입니다.",
            $"내부 경로 상태는 {Format(result.InternalRouteStatus)}, 프록시 실행 상태는 {Format(result.ProxyExecutionStatus)}, 프록시 분석 상태는 {Format(result.ProxyAnalysisStatus)}입니다.",
            $"적용 후보 {result.ProxyApplicableEndpointCount}개, 분석 후보 {result.ProxyAnalyzedEndpointCount}개, 성공 후보 {result.ProxySuccessfulEndpointCount}개, 서로 다른 인터페이스 {result.ProxyDistinctInterfaceCount}개입니다.",
            $"전체 인터페이스 ID 정확 비교는 {(result.ExactIdentityComparisonPerformed ? "수행했습니다" : "수행하지 않았습니다")}.");

        return new ReportFinding(
            Code: code,
            Severity: severity,
            Title: title,
            Evidence: evidence,
            Interpretation: result.Interpretation,
            Limitation: result.Limitation,
            NextStep: result.NextStep);
    }

    private static string Format<TEnum>(TEnum? value)
        where TEnum : struct, Enum =>
        value?.ToString() ?? "없음";
}
