using WlanLivePathTester.Core.Routing;

namespace WlanLivePathTester.Core.Reporting;

public static class InternalProxyRouteComparisonRunFindingMapper
{
    public static ReportFinding FromResult(
        InternalProxyRouteComparisonRunResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (result.Status
            == InternalProxyRouteComparisonRunStatus.Completed)
        {
            return FromCompletedResult(result);
        }

        return result.Status switch
        {
            InternalProxyRouteComparisonRunStatus.InvalidInput =>
                Create(
                    "INTERNAL_PROXY_ROUTE_COMPARISON_INVALID_INPUT",
                    "Warning",
                    "내부 DIRECT–프록시 경로 비교 입력 확인 필요",
                    BuildRunEvidence(result),
                    "현재 외부 대상에 적용되는 안전한 프록시 경로 또는 내부 기준 입력을 확정하지 못해 DNS·라우팅 조회를 시작하지 않았습니다.",
                    "입력 오류만으로 실제 내부망·프록시·인터넷 장애를 판단할 수 없습니다.",
                    "승인된 내부 DIRECT 대상, 절대 HTTP(S) 외부 URL과 현재 대상에 적용되는 프록시 지시문을 다시 확인하십시오."),
            InternalProxyRouteComparisonRunStatus.DirectPathSelected =>
                Create(
                    "INTERNAL_PROXY_ROUTE_COMPARISON_DIRECT_PRIMARY",
                    "Information",
                    "외부 대상의 첫 경로는 DIRECT",
                    BuildRunEvidence(result),
                    "DIRECT가 프록시 후보보다 먼저 적용되므로 비교할 프록시 엔드포인트가 없고 내부·프록시 DNS 조회를 생략했습니다.",
                    "이 DIRECT 판정은 현재 입력한 외부 대상과 지시문 순서에만 적용되며 다른 URL의 정책을 의미하지 않습니다.",
                    "프록시가 적용되는 다른 승인 외부 대상과 실제 대상별 PAC·WPAD 판정을 사용해 다시 비교하십시오."),
            InternalProxyRouteComparisonRunStatus
                .InternalRouteUnavailable =>
                Create(
                    "INTERNAL_PROXY_ROUTE_COMPARISON_INTERNAL_UNAVAILABLE",
                    "Warning",
                    "내부 DIRECT 기준 경로 확인 실패",
                    BuildRunEvidence(result),
                    "내부 기준 대상의 비교 가능한 Windows 로컬 인터페이스 근거가 없어 프록시 후보 조회를 추가로 수행하지 않았습니다.",
                    "내부 DNS, IPv4·IPv6 라우팅 또는 대상 입력 중 어느 단계가 원인인지는 주소별 근거 없이는 확정할 수 없습니다.",
                    "내부 대상의 DNS 해석과 Windows 최적 경로를 확인한 뒤 비교를 다시 실행하십시오."),
            InternalProxyRouteComparisonRunStatus.Canceled =>
                Create(
                    "INTERNAL_PROXY_ROUTE_COMPARISON_CANCELED",
                    "Information",
                    "내부 DIRECT–프록시 경로 비교 사용자 중지",
                    BuildRunEvidence(result),
                    "사용자 요청으로 이후 DNS·라우팅 단계를 시작하지 않거나 진행 중인 분석을 중단했습니다.",
                    "완료되지 않은 후보는 결과에 없으므로 부분 근거만으로 두 경로가 같거나 다르다고 판단할 수 없습니다.",
                    "필요한 경우 네트워크 상태를 고정한 뒤 비교를 처음부터 다시 실행하십시오."),
            InternalProxyRouteComparisonRunStatus.Failed =>
                Create(
                    "INTERNAL_PROXY_ROUTE_COMPARISON_FAILED",
                    "Warning",
                    "내부 DIRECT–프록시 경로 비교 실행 오류",
                    BuildRunEvidence(result),
                    "내부 경로 reader 또는 프록시 경로 분석 서비스가 안전하게 완료되지 않았습니다.",
                    "오류 원문을 보고서에 반사하지 않으므로 이 Finding만으로 예외의 세부 원인을 알 수 없습니다.",
                    "Windows DNS·라우팅 상태와 애플리케이션 로컬 로그를 회사 정책 범위에서 확인한 뒤 다시 실행하십시오."),
            _ => Create(
                "INTERNAL_PROXY_ROUTE_COMPARISON_UNKNOWN",
                "Warning",
                "알 수 없는 내부 DIRECT–프록시 비교 상태",
                BuildRunEvidence(result),
                "지원하지 않는 실행 상태가 전달돼 경로 비교 결론을 사용하지 않았습니다.",
                "향후 스키마 또는 코드 변경으로 알려지지 않은 enum 값이 발생할 수 있습니다.",
                "애플리케이션 버전과 보고서 스키마를 확인하고 최신 검증 빌드에서 다시 실행하십시오.")
        };
    }

    private static ReportFinding FromCompletedResult(
        InternalProxyRouteComparisonRunResult result)
    {
        InternalProxyRouteComparisonResult? comparison =
            result.Comparison;
        if (comparison is null)
        {
            return Create(
                "INTERNAL_PROXY_ROUTE_COMPARISON_RESULT_MISSING",
                "Warning",
                "완료된 경로 비교 결과 누락",
                BuildRunEvidence(result),
                "실행 상태는 Completed이지만 구조화 비교 결과가 없어 동일·분기·모호·불완전 판정을 사용하지 않았습니다.",
                "실행 상태와 결과 객체의 내부 계약 불일치이며 네트워크 장애를 의미하지 않습니다.",
                "애플리케이션 검증 로그를 확인하고 경로 비교를 다시 실행하십시오.");
        }

        string evidence = BuildCompletedEvidence(
            result,
            comparison);
        return comparison.Status switch
        {
            InternalProxyRouteComparisonStatus.Ready => Create(
                "INTERNAL_PROXY_ROUTE_COMPARISON_READY",
                "Information",
                "내부 DIRECT와 프록시의 로컬 인터페이스 일치",
                evidence,
                "내부 DIRECT 대상과 분석된 모든 프록시 후보가 같은 Windows 로컬 인터페이스 지문을 사용합니다.",
                "같은 첫 로컬 인터페이스는 이후 사내 라우팅, 프록시, 인터넷 회선 또는 대상 서버의 성능이 같다는 뜻이 아닙니다.",
                "내부·외부 처리량, HTTP 상태, 프록시 인증과 WLAN RSSI·PHY·로밍 근거를 같은 시점 기준으로 비교하십시오."),
            InternalProxyRouteComparisonStatus.Diverged => Create(
                "INTERNAL_PROXY_ROUTE_COMPARISON_DIVERGED",
                "Warning",
                "내부 DIRECT와 프록시의 로컬 인터페이스 분기",
                evidence,
                "내부 DIRECT 대상과 프록시 엔드포인트가 서로 다른 Windows 로컬 인터페이스 지문을 사용합니다.",
                "경로 분기는 VPN·터널, 인터페이스 메트릭, 정적 경로 또는 의도된 유선·무선 분할 정책일 수 있으며 단독 장애 증거가 아닙니다.",
                "양쪽 인터페이스 범주와 WLAN 일치 여부를 확인하고 VPN·정적 경로·인터페이스 메트릭을 비교하십시오."),
            InternalProxyRouteComparisonStatus.Ambiguous => Create(
                "INTERNAL_PROXY_ROUTE_COMPARISON_AMBIGUOUS",
                "Warning",
                "내부 DIRECT–프록시 로컬 경로 근거 모호",
                evidence,
                "내부 주소군 또는 프록시 후보가 여러 인터페이스로 나뉘거나 인터페이스 메타데이터가 충돌해 단일 경로 결론을 내리지 않았습니다.",
                "IPv4·IPv6, DNS 응답, VPN 상태와 Windows 라우팅 변화가 같은 결과를 만들 수 있습니다.",
                "주소 계열과 각 프록시 후보별 경로를 확인하고 유선·무선·VPN 상태를 고정한 뒤 다시 실행하십시오."),
            InternalProxyRouteComparisonStatus.Incomplete => Create(
                "INTERNAL_PROXY_ROUTE_COMPARISON_INCOMPLETE",
                "Information",
                "내부 DIRECT–프록시 로컬 경로 비교 근거 불완전",
                evidence,
                "내부 또는 프록시 경로와 fallback 후보의 근거가 부족해 같은 경로인지 다른 경로인지 결론 내리지 않았습니다.",
                "일부 성공 후보만으로 실제 요청의 전체 fallback 경로를 확정할 수 없습니다.",
                "실패한 DNS·라우팅 후보와 DIRECT 순서를 확인한 뒤 모든 필수 근거를 다시 수집하십시오."),
            _ => Create(
                "INTERNAL_PROXY_ROUTE_COMPARISON_RESULT_UNKNOWN",
                "Warning",
                "알 수 없는 내부 DIRECT–프록시 비교 결과",
                evidence,
                "지원하지 않는 비교 상태가 전달돼 결과를 사용하지 않았습니다.",
                "향후 스키마 변경 또는 손상된 객체일 수 있으며 네트워크 장애를 직접 의미하지 않습니다.",
                "애플리케이션과 보고서 스키마 버전을 확인한 뒤 다시 실행하십시오.")
        };
    }

    private static string BuildRunEvidence(
        InternalProxyRouteComparisonRunResult result) =>
        string.Join(
            " ",
            $"실행 상태는 {result.Status}입니다.",
            $"프록시 출처는 {result.ProxySourceKind}, 결정은 {result.ProxyDecision}입니다.",
            $"파싱 후보 {Math.Max(0, result.ParsedProxyEndpointCount)}개, 분석 후보 {Math.Max(0, result.AnalyzedProxyEndpointCount)}개, 성공 후보 {Math.Max(0, result.SuccessfulProxyEndpointCount)}개입니다.",
            $"내부 경로 조회는 {(result.InternalRouteReadPerformed ? "수행" : "미수행")}, 프록시 경로 분석은 {(result.ProxyRouteAnalysisPerformed ? "수행" : "미수행")}했습니다.",
            $"DIRECT는 {(result.DirectPresent ? "있음" : "없음")}, fallback은 {(result.DirectFallback ? "있음" : "없음")}입니다.");

    private static string BuildCompletedEvidence(
        InternalProxyRouteComparisonRunResult run,
        InternalProxyRouteComparisonResult comparison) =>
        string.Join(
            " ",
            BuildRunEvidence(run),
            $"비교 상태는 {comparison.Status}입니다.",
            comparison.SameLocalInterface.HasValue
                ? $"같은 로컬 인터페이스 여부는 {comparison.SameLocalInterface.Value}입니다."
                : "같은 로컬 인터페이스 여부는 판정하지 않았습니다.",
            $"프록시 경로의 서로 다른 인터페이스 수는 {Math.Max(0, comparison.ProxyDistinctInterfaceCount)}개입니다.",
            $"VPN·터널 포함은 {(comparison.AnyVpnOrTunnelInterface ? "있음" : "확인 안 됨")}, 가상 인터페이스 포함은 {(comparison.AnyVirtualInterface ? "있음" : "확인 안 됨")}입니다.");

    private static ReportFinding Create(
        string code,
        string severity,
        string title,
        string evidence,
        string interpretation,
        string limitation,
        string nextStep) =>
        new(
            Code: code,
            Severity: severity,
            Title: title,
            Evidence: evidence,
            Interpretation: interpretation,
            Limitation: limitation,
            NextStep: nextStep);
}
