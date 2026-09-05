using System.Globalization;
using System.Text;
using WlanLivePathTester.Core.NetworkEnvironment;
using WlanLivePathTester.Core.Proxy;
using WlanLivePathTester.Core.Routing;

namespace WlanLivePathTester.Core.Reporting;

public static class InternalProxyRouteComparisonRunTextRenderer
{
    public static string Render(
        InternalProxyRouteComparisonRunResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        ReportFinding finding =
            InternalProxyRouteComparisonRunFindingMapper.FromResult(
                result);
        StringBuilder builder = new();
        builder.AppendLine(
            "내부 DIRECT ↔ 프록시 Windows 로컬 경로 비교");
        builder.AppendLine(
            $"완료 시각: {result.CompletedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture)}");
        builder.AppendLine(
            $"실행 상태: {SafeEnum(result.Status)}");
        builder.AppendLine(
            $"프록시 출처 / 선택: {SafeEnum(result.ProxySourceKind)} / {SafeEnum(result.ProxySelectionStatus)}");
        builder.AppendLine(
            $"실행 계획: {SafeEnum(result.ProxyPlanStatus)} / {SafeEnum(result.ProxyPlanCode)}");
        builder.AppendLine(
            $"프록시 실행 / 경로: {SafeNullableEnum(result.ProxyExecutionStatus)} / {SafeNullableEnum(result.ProxyRouteStatus)}");
        builder.AppendLine(
            $"endpoint 형식 / 결정: {SafeEnum(result.ProxyEndpointSourceKind)} / {SafeEnum(result.ProxyDecision)}");
        builder.AppendLine(
            $"외부 대상 스킴: {SafeScheme(result.TargetScheme)}");
        builder.AppendLine(
            $"내부 경로 상태: {SafeNullableEnum(result.InternalRouteStatus)}");
        builder.AppendLine(
            $"내부 / 프록시 단계: {Performed(result.InternalRouteReadPerformed)} / {Performed(result.ProxyRouteAnalysisPerformed)}");
        builder.AppendLine(
            $"후보(파싱 / 적용 / 분석 / 성공): {Count(result.ParsedProxyEndpointCount)} / {Count(result.ApplicableProxyEndpointCount)} / {Count(result.AnalyzedProxyEndpointCount)} / {Count(result.SuccessfulProxyEndpointCount)}");
        builder.AppendLine(
            $"서로 다른 프록시 인터페이스: {Count(result.DistinctProxyInterfaceCount)}개");
        builder.AppendLine(
            $"DIRECT(존재 / 첫 경로 / fallback): {YesNo(result.DirectPresent)} / {YesNo(result.DirectIsPrimary)} / {YesNo(result.DirectFallback)}");
        builder.AppendLine(
            $"프록시 파싱 오류 / 현재 WLAN 전체 ID: {Present(result.ProxyParseErrorsPresent)} / {(result.ExpectedWlanIdentityAvailable ? "확인" : "미확인")}");

        AppendComparison(builder, result.Comparison);
        AppendProxyEntries(builder, result.ProxyExecution?.Analysis);

        builder.AppendLine();
        builder.AppendLine("[판정]");
        builder.AppendLine(
            $"{SafeFindingSeverity(finding.Severity)} · {SafeCode(finding.Code)}");
        builder.AppendLine(SafeFixedNarrative(finding.Title));
        builder.AppendLine(
            $"근거: {SafeFixedNarrative(finding.Evidence)}");
        builder.AppendLine(
            $"해석: {SafeFixedNarrative(finding.Interpretation)}");
        builder.AppendLine(
            $"한계: {SafeFixedNarrative(finding.Limitation)}");
        builder.AppendLine(
            $"다음 확인: {SafeFixedNarrative(finding.NextStep)}");
        builder.AppendLine();
        builder.AppendLine("[데이터 처리]");
        builder.AppendLine(
            "입력한 내부·외부 URL과 프록시 지시문, 실제 프록시 호스트, 전체 인터페이스 GUID·이름·설명은 결과 영역에 표시하지 않습니다.");
        builder.AppendLine(
            "경로 비교는 사용자가 시작한 경우에만 운영체제 DNS와 Windows 최적 인터페이스 판정을 수행하며 HTTP 다운로드·프록시 로그인·외부 업로드는 수행하지 않습니다.");
        return builder.ToString().TrimEnd();
    }

    private static void AppendComparison(
        StringBuilder builder,
        InternalProxyRouteComparisonResult? comparison)
    {
        builder.AppendLine();
        builder.AppendLine("[정확 인터페이스 비교]");
        if (comparison is null)
        {
            builder.AppendLine("구조화 비교 결과 없음");
            return;
        }

        builder.AppendLine(
            $"상태 / 관계 / 원인: {SafeEnum(comparison.Status)} / {SafeEnum(comparison.Relation)} / {SafeEnum(comparison.Code)}");
        builder.AppendLine(
            $"전체 인터페이스 ID 정확 비교: {Performed(comparison.ExactIdentityComparisonPerformed)}");
        builder.AppendLine(
            $"내부 인터페이스: {SafeCategory(comparison.InternalInterfaceCategory)} / 지문 {SafeFingerprint(comparison.InternalInterfaceFingerprint)}");
        builder.AppendLine(
            $"프록시 인터페이스 범주: {JoinCategories(comparison.ProxyInterfaceCategories)}");
        builder.AppendLine(
            $"프록시 인터페이스 지문: {JoinFingerprints(comparison.ProxyInterfaceFingerprints)}");
        builder.AppendLine(
            $"비교 후보(적용 / 분석 / 성공 / distinct / DIRECT 이후 제외): {Count(comparison.ProxyApplicableEndpointCount)} / {Count(comparison.ProxyAnalyzedEndpointCount)} / {Count(comparison.ProxySuccessfulEndpointCount)} / {Count(comparison.ProxyDistinctInterfaceCount)} / {Count(comparison.ProxySkippedAfterDirectCount)}");
        builder.AppendLine(
            $"비교 DIRECT(존재 / 첫 경로 / fallback): {YesNo(comparison.ProxyDirectPresent)} / {YesNo(comparison.ProxyDirectIsPrimary)} / {YesNo(comparison.ProxyDirectFallbackPresent)}");
    }

    private static void AppendProxyEntries(
        StringBuilder builder,
        ProxyEndpointRouteAnalysisResult? analysis)
    {
        builder.AppendLine();
        builder.AppendLine("[프록시 후보 로컬 경로]");
        if (analysis is null || analysis.Endpoints.Count == 0)
        {
            builder.AppendLine("분석된 프록시 후보 없음");
            return;
        }

        foreach (ProxyEndpointRouteEvidenceItem endpoint in
                 analysis.Endpoints
                     .OrderBy(item => Math.Max(0, item.Sequence)))
        {
            string port = endpoint.Port is >= 1 and <= 65535
                ? endpoint.Port.Value.ToString(
                    CultureInfo.InvariantCulture)
                : "-";
            builder.AppendLine(string.Join(
                " · ",
                $"#{Math.Max(0, endpoint.Sequence)}",
                SafeEnum(endpoint.Transport),
                $"범위 {SafeSchemeScope(endpoint.AppliesToScheme)}",
                $"포트 {port}",
                $"호스트 지문 {SafeFingerprint(endpoint.HostFingerprint)}",
                $"경로 {SafeEnum(endpoint.RouteStatus)}",
                $"인터페이스 {SafeCategory(endpoint.SelectedInterfaceCategory)} / {SafeFingerprint(endpoint.SelectedInterfaceFingerprint)}",
                $"WLAN {SafeEnum(endpoint.WlanCorrelationStatus)}",
                $"주소 {Count(endpoint.SuccessfulAddressCount)}/{Count(endpoint.ResolvedAddressCount)} 성공"));
        }
    }

    private static string JoinFingerprints(
        IReadOnlyList<string> values)
    {
        string[] safe = values
            .Select(SafeFingerprint)
            .Where(value => value != "없음")
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        return safe.Length == 0
            ? "없음"
            : string.Join(", ", safe);
    }

    private static string JoinCategories(
        IReadOnlyList<NetworkAdapterCategory> values)
    {
        string[] safe = values
            .Select(value => SafeCategory(value))
            .Where(value => value != "확인 불가")
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        return safe.Length == 0
            ? "없음"
            : string.Join(", ", safe);
    }

    private static string SafeScheme(string? value) =>
        (value ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "http" => "http",
            "https" => "https",
            _ => "확인 불가"
        };

    private static string SafeSchemeScope(string? value) =>
        (value ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "all" => "all",
            "http" => "http",
            "https" => "https",
            _ => "all"
        };

    private static string SafeFingerprint(string? value)
    {
        string candidate = (value ?? string.Empty)
            .Trim()
            .ToLowerInvariant();
        return candidate.Length == 10
               && candidate.All(character =>
                   character is >= '0' and <= '9'
                       or >= 'a' and <= 'f')
            ? candidate
            : "없음";
    }

    private static string SafeCategory(
        NetworkAdapterCategory? value) =>
        value.HasValue && Enum.IsDefined(value.Value)
            ? value.Value.ToString()
            : "확인 불가";

    private static string SafeEnum<TEnum>(TEnum value)
        where TEnum : struct, Enum =>
        Enum.IsDefined(value)
            ? value.ToString()
            : "Unknown";

    private static string SafeNullableEnum<TEnum>(TEnum? value)
        where TEnum : struct, Enum =>
        value.HasValue && Enum.IsDefined(value.Value)
            ? value.Value.ToString()
            : "없음";

    private static string SafeCode(string? value)
    {
        string candidate = (value ?? string.Empty)
            .Trim()
            .ToUpperInvariant();
        return candidate.Length is >= 1 and <= 96
               && candidate.All(character =>
                   character is >= 'A' and <= 'Z'
                       or >= '0' and <= '9'
                       or '_')
            ? candidate
            : "INVALID_CODE";
    }

    private static string SafeFindingSeverity(string? value) =>
        (value ?? string.Empty).Trim() switch
        {
            "Information" => "Information",
            "Warning" => "Warning",
            "Error" => "Error",
            _ => "Warning"
        };

    private static string SafeFixedNarrative(string? value)
    {
        string candidate = (value ?? string.Empty)
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Replace('\t', ' ')
            .Trim();
        if (candidate.Length == 0)
        {
            return "설명 없음";
        }

        return candidate.Length <= 4096
            ? candidate
            : candidate[..4093] + "...";
    }

    private static int Count(int value) => Math.Max(0, value);

    private static string Performed(bool value) =>
        value ? "수행" : "미수행";

    private static string Present(bool value) =>
        value ? "있음" : "없음";

    private static string YesNo(bool value) =>
        value ? "예" : "아니오";
}
