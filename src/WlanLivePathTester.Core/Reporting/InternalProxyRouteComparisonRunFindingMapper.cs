using WlanLivePathTester.Core.Proxy;
using WlanLivePathTester.Core.Routing;

namespace WlanLivePathTester.Core.Reporting;

public static class InternalProxyRouteComparisonRunFindingMapper
{
    public static ReportFinding FromResult(
        InternalProxyRouteComparisonRunResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (!Enum.IsDefined(result.Status))
        {
            return Create(
                code: "INTERNAL_PROXY_ROUTE_RUN_UNKNOWN",
                severity: "Warning",
                title: "알 수 없는 내부 DIRECT–프록시 경로 실행 상태",
                evidence: BuildRunEvidence(result),
                interpretation:
                    "지원하지 않는 실행 상태가 전달돼 경로 비교 결과를 사용하지 않았습니다.",
                limitation:
                    "향후 스키마 변경 또는 손상된 객체일 수 있으며 네트워크 장애를 직접 의미하지 않습니다.",
                nextStep:
                    "애플리케이션과 보고서 스키마 버전을 확인한 뒤 검증된 최신 빌드에서 다시 실행하십시오.");
        }

        return result.Status switch
        {
            InternalProxyRouteComparisonRunStatus.Completed =>
                FromCompletedRun(result),
            InternalProxyRouteComparisonRunStatus.InvalidInput =>
                Create(
                    "INTERNAL_PROXY_ROUTE_RUN_INVALID_INPUT",
                    "Warning",
                    "내부 DIRECT–프록시 경로 비교 입력 확인 필요",
                    BuildRunEvidence(result),
                    "내부 기준 대상, 외부 HTTP(S) 대상 또는 적용 프록시 지시문이 비교 조건을 충족하지 않아 라우팅 조회를 시작하지 않았습니다.",
                    "입력 오류만으로 내부망·프록시·인터넷 장애를 판단할 수 없습니다.",
                    "승인된 내부 DIRECT 대상, 절대 HTTP(S) 외부 URL과 해당 대상에 적용되는 프록시 지시문을 확인하십시오."),
            InternalProxyRouteComparisonRunStatus.ProxySourceBlocked =>
                Create(
                    "INTERNAL_PROXY_ROUTE_RUN_SOURCE_BLOCKED",
                    "Warning",
                    "프록시 출처 판정 불일치로 경로 비교 차단",
                    BuildRunEvidence(result),
                    "대상별 판정, 수동 설정 또는 실행 계획의 내부 계약이 모순돼 DNS와 라우팅 조회를 시작하지 않았습니다.",
                    "차단은 잘못된 경로 추정을 막기 위한 fail-closed 결과이며 프록시 장애를 확정하지 않습니다.",
                    "대상별 PAC·WPAD 판정과 수동 프록시 설정의 상태·DIRECT 여부·후보 순서를 다시 수집하십시오."),
            InternalProxyRouteComparisonRunStatus.ProxySourceUnavailable =>
                Create(
                    "INTERNAL_PROXY_ROUTE_RUN_SOURCE_UNAVAILABLE",
                    "Information",
                    "비교 가능한 프록시 출처 없음",
                    BuildRunEvidence(result),
                    "대상별 또는 수동 프록시 지시문을 확인하지 못해 프록시 엔드포인트 경로 비교를 수행하지 않았습니다.",
                    "프록시 출처가 없다는 사실을 DIRECT 또는 네트워크 정상으로 해석할 수 없습니다.",
                    "현재 외부 대상의 Windows 프록시 판정 또는 승인된 수동 프록시 설정을 확인하십시오."),
            InternalProxyRouteComparisonRunStatus.DirectPathSelected =>
                Create(
                    "INTERNAL_PROXY_ROUTE_RUN_DIRECT_PRIMARY",
                    "Information",
                    "외부 대상의 첫 적용 경로는 DIRECT",
                    BuildRunEvidence(result),
                    "DIRECT가 첫 적용 경로이므로 비교할 프록시 엔드포인트가 없고 내부·프록시 라우팅 조회를 생략했습니다.",
                    "이 결과는 현재 외부 대상과 현재 지시문 순서에만 적용되며 다른 URL의 프록시 정책을 의미하지 않습니다.",
                    "프록시가 적용되는 승인 외부 대상의 대상별 PAC·WPAD 판정으로 다시 비교하십시오."),
            InternalProxyRouteComparisonRunStatus
                .InternalRouteUnavailable =>
                Create(
                    "INTERNAL_PROXY_ROUTE_RUN_INTERNAL_UNAVAILABLE",
                    "Warning",
                    "내부 DIRECT 기준 경로 확인 불가",
                    BuildRunEvidence(result),
                    "내부 기준 대상에서 정확하고 단일한 Windows 로컬 인터페이스 근거를 얻지 못해 프록시 후보 조회를 추가로 수행하지 않았습니다.",
                    "내부 DNS, IPv4·IPv6 라우팅, 인터페이스 상태 또는 전체 GUID 수집 중 어느 단계가 원인인지는 주소별 근거를 함께 확인해야 합니다.",
                    "내부 대상의 DNS 해석과 Windows 최적 경로를 확인한 뒤 다시 실행하십시오."),
            InternalProxyRouteComparisonRunStatus.Canceled =>
                Create(
                    "INTERNAL_PROXY_ROUTE_RUN_CANCELED",
                    "Information",
                    "내부 DIRECT–프록시 경로 비교 사용자 중지",
                    BuildRunEvidence(result),
                    "사용자 요청으로 이후 DNS·라우팅 단계를 시작하지 않거나 진행 중인 프록시 분석을 중단했습니다.",
                    "완료되지 않은 후보는 전체 fallback 근거가 아니므로 두 경로가 같거나 다르다고 판단할 수 없습니다.",
                    "네트워크 상태를 고정한 뒤 필요한 경우 비교를 처음부터 다시 실행하십시오."),
            InternalProxyRouteComparisonRunStatus.Failed =>
                Create(
                    "INTERNAL_PROXY_ROUTE_RUN_FAILED",
                    "Warning",
                    "내부 DIRECT–프록시 경로 비교 실행 오류",
                    BuildRunEvidence(result),
                    "내부 경로 reader 또는 프록시 경로 분석 서비스가 안전하게 완료되지 않았습니다.",
                    "오류 원문을 Finding에 반사하지 않으므로 이 결과만으로 예외의 세부 원인을 알 수 없습니다.",
                    "Windows DNS·라우팅 상태와 로컬 검증 로그를 회사 정책 범위에서 확인한 뒤 다시 실행하십시오."),
            _ => Create(
                "INTERNAL_PROXY_ROUTE_RUN_UNKNOWN",
                "Warning",
                "알 수 없는 내부 DIRECT–프록시 경로 실행 상태",
                BuildRunEvidence(result),
                "지원하지 않는 실행 상태가 전달돼 경로 비교 결과를 사용하지 않았습니다.",
                "향후 스키마 변경 또는 손상된 객체일 수 있으며 네트워크 장애를 직접 의미하지 않습니다.",
                "애플리케이션과 보고서 스키마 버전을 확인한 뒤 검증된 최신 빌드에서 다시 실행하십시오.")
        };
    }

    private static ReportFinding FromCompletedRun(
        InternalProxyRouteComparisonRunResult run)
    {
        InternalProxyRouteComparisonResult? comparison =
            run.Comparison;
        if (comparison is null)
        {
            return Create(
                "INTERNAL_PROXY_ROUTE_RUN_RESULT_MISSING",
                "Warning",
                "완료된 경로 비교의 구조화 결과 누락",
                BuildRunEvidence(run),
                "실행 상태는 Completed이지만 구조화 비교 결과가 없어 동일·분기·모호·불완전 판정을 사용하지 않았습니다.",
                "실행 상태와 결과 객체의 내부 계약 불일치이며 네트워크 장애를 의미하지 않습니다.",
                "검증 로그를 확인하고 경로 비교를 다시 실행하십시오.");
        }

        if (!Enum.IsDefined(comparison.Status))
        {
            return Create(
                "INTERNAL_PROXY_ROUTE_RUN_RESULT_UNKNOWN",
                "Warning",
                "알 수 없는 내부 DIRECT–프록시 비교 결과",
                BuildCompletedEvidence(run, comparison),
                "지원하지 않는 비교 상태가 전달돼 결과를 사용하지 않았습니다.",
                "향후 스키마 변경 또는 손상된 객체일 수 있으며 네트워크 장애를 직접 의미하지 않습니다.",
                "애플리케이션과 보고서 스키마 버전을 확인한 뒤 다시 실행하십시오.");
        }

        string evidence = BuildCompletedEvidence(run, comparison);
        return comparison.Status switch
        {
            InternalProxyRouteComparisonStatus.Ready => Create(
                "INTERNAL_PROXY_ROUTE_SAME_INTERFACE",
                "Information",
                "내부 DIRECT와 프록시 경로의 로컬 인터페이스 일치",
                evidence,
                "내부 DIRECT 대상과 분석된 모든 프록시 후보가 같은 정확한 Windows 로컬 인터페이스를 사용합니다.",
                "같은 첫 로컬 인터페이스는 이후 사내 라우팅, 프록시, 인터넷 회선 또는 대상 서버의 성능이 같다는 뜻이 아닙니다.",
                "내부·외부 처리량, HTTP 상태, 프록시 인증과 WLAN RSSI·PHY·로밍 근거를 같은 시점 기준으로 비교하십시오."),
            InternalProxyRouteComparisonStatus.Diverged => Create(
                "INTERNAL_PROXY_ROUTE_DIVERGED",
                "Information",
                "내부 DIRECT와 프록시 경로의 로컬 인터페이스 분리",
                evidence,
                "내부 DIRECT 대상과 프록시 엔드포인트가 서로 다른 정확한 Windows 로컬 인터페이스를 사용합니다.",
                "경로 분리는 VPN·터널, 인터페이스 메트릭, 정적 경로 또는 의도된 유선·무선 분할 정책일 수 있으며 단독 장애 증거가 아닙니다.",
                "양쪽 인터페이스 범주와 WLAN 일치 여부를 확인하고 VPN·정적 경로·인터페이스 메트릭을 비교하십시오."),
            InternalProxyRouteComparisonStatus.Ambiguous => Create(
                "INTERNAL_PROXY_ROUTE_AMBIGUOUS",
                "Warning",
                "내부 DIRECT·프록시 로컬 경로가 여러 인터페이스로 모호함",
                evidence,
                "내부 주소군 또는 프록시 후보가 여러 인터페이스로 나뉘어 단일 경로 결론을 내리지 않았습니다.",
                "IPv4·IPv6, DNS 응답, VPN 상태와 Windows 라우팅 변화가 같은 결과를 만들 수 있습니다.",
                "주소 계열과 각 프록시 후보별 경로를 확인하고 유선·무선·VPN 상태를 고정한 뒤 다시 실행하십시오."),
            InternalProxyRouteComparisonStatus.Incomplete => Create(
                "INTERNAL_PROXY_ROUTE_INCOMPLETE",
                "Warning",
                "내부 DIRECT·프록시 로컬 경로 비교 근거 불완전",
                evidence,
                "내부 또는 프록시 경로와 fallback 후보의 근거가 부족해 같은 경로인지 다른 경로인지 결론 내리지 않았습니다.",
                "일부 성공 후보만으로 실제 요청의 전체 fallback 경로를 확정할 수 없습니다.",
                "실패한 DNS·라우팅 후보와 DIRECT 순서를 확인한 뒤 모든 필수 근거를 다시 수집하십시오."),
            _ => Create(
                "INTERNAL_PROXY_ROUTE_RUN_RESULT_UNKNOWN",
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
            $"실행 상태는 {SafeEnum(result.Status, "Unknown")}입니다.",
            $"프록시 출처는 {SafeEnum(result.ProxySourceKind, "Unknown")}, 선택 상태는 {SafeEnum(result.ProxySelectionStatus, "Unknown")}, 실행 계획은 {SafeEnum(result.ProxyPlanStatus, "Unknown")}·{SafeEnum(result.ProxyPlanCode, "Unknown")}입니다.",
            $"프록시 실행 상태는 {SafeNullableEnum(result.ProxyExecutionStatus)}, 엔드포인트 형식은 {SafeEnum(result.ProxyEndpointSourceKind, "Unknown")}, 결정은 {SafeEnum(result.ProxyDecision, "Unknown")}입니다.",
            $"대상 스킴은 {SafeScheme(result.TargetScheme)}, 내부 경로 상태는 {SafeNullableEnum(result.InternalRouteStatus)}, 프록시 경로 상태는 {SafeNullableEnum(result.ProxyRouteStatus)}입니다.",
            $"파싱 후보 {Count(result.ParsedProxyEndpointCount)}개, 적용 후보 {Count(result.ApplicableProxyEndpointCount)}개, 분석 후보 {Count(result.AnalyzedProxyEndpointCount)}개, 성공 후보 {Count(result.SuccessfulProxyEndpointCount)}개, 서로 다른 프록시 인터페이스 {Count(result.DistinctProxyInterfaceCount)}개입니다.",
            $"DIRECT는 {(result.DirectPresent ? "있음" : "없음")}, 첫 경로 DIRECT는 {(result.DirectIsPrimary ? "예" : "아니오")}, DIRECT fallback은 {(result.DirectFallback ? "있음" : "없음")}입니다.",
            $"프록시 파싱 오류는 {(result.ProxyParseErrorsPresent ? "있음" : "없음")}, 현재 WLAN 전체 ID는 {(result.ExpectedWlanIdentityAvailable ? "확인" : "미확인")}했습니다.",
            $"내부 경로 조회는 {(result.InternalRouteReadPerformed ? "수행" : "미수행")}, 프록시 경로 분석은 {(result.ProxyRouteAnalysisPerformed ? "수행" : "미수행")}했습니다.");

    private static string BuildCompletedEvidence(
        InternalProxyRouteComparisonRunResult run,
        InternalProxyRouteComparisonResult comparison) =>
        string.Join(
            " ",
            BuildRunEvidence(run),
            $"비교 상태는 {SafeEnum(comparison.Status, "Unknown")}, 관계는 {SafeEnum(comparison.Relation, "Unknown")}, 원인 코드는 {SafeEnum(comparison.Code, "Unknown")}입니다.",
            $"전체 인터페이스 ID 정확 비교는 {(comparison.ExactIdentityComparisonPerformed ? "수행" : "미수행")}했습니다.",
            $"비교 모델 기준 적용 후보 {Count(comparison.ProxyApplicableEndpointCount)}개, 분석 후보 {Count(comparison.ProxyAnalyzedEndpointCount)}개, 성공 후보 {Count(comparison.ProxySuccessfulEndpointCount)}개, 서로 다른 인터페이스 {Count(comparison.ProxyDistinctInterfaceCount)}개입니다.");

    private static string SafeScheme(string? value) =>
        (value ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "http" => "http",
            "https" => "https",
            _ => "없음"
        };

    private static string SafeEnum<TEnum>(
        TEnum value,
        string fallback)
        where TEnum : struct, Enum =>
        Enum.IsDefined(value)
            ? value.ToString()
            : fallback;

    private static string SafeNullableEnum<TEnum>(TEnum? value)
        where TEnum : struct, Enum =>
        value.HasValue && Enum.IsDefined(value.Value)
            ? value.Value.ToString()
            : "없음";

    private static int Count(int value) => Math.Max(0, value);

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
