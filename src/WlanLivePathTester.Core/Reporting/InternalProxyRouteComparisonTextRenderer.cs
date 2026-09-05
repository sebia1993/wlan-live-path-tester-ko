using System.Globalization;
using System.Text;
using WlanLivePathTester.Core.NetworkEnvironment;
using WlanLivePathTester.Core.Proxy;
using WlanLivePathTester.Core.Routing;

namespace WlanLivePathTester.Core.Reporting;

public static class InternalProxyRouteComparisonTextRenderer
{
    public static string Render(
        InternalProxyRouteComparisonResult comparison,
        ProxyDirectiveRouteAnalysisExecutionResult<
            ProxyEndpointRouteAnalysisResult> proxyExecution)
    {
        ArgumentNullException.ThrowIfNull(comparison);
        ArgumentNullException.ThrowIfNull(proxyExecution);

        StringBuilder builder = new();
        builder.AppendLine("내부 DIRECT ↔ 프록시 로컬 경로 비교");
        builder.AppendLine($"상태: {SafeEnum(comparison.Status, InternalProxyRouteComparisonStatus.Incomplete)}");
        builder.AppendLine($"관계: {SafeEnum(comparison.Relation, InternalProxyRouteRelation.Unknown)}");
        builder.AppendLine($"판정 코드: {SafeEnum(comparison.Code, InternalProxyRouteComparisonCode.ProxyAnalysisIncomplete)}");
        builder.AppendLine(
            $"전체 인터페이스 ID 정확 비교: {(comparison.ExactIdentityComparisonPerformed ? "수행" : "미수행")}");
        builder.AppendLine();

        builder.AppendLine("[내부 DIRECT]");
        builder.AppendLine(
            $"경로 상태: {FormatNullableEnum(comparison.InternalRouteStatus)}");
        builder.AppendLine(
            $"인터페이스: {FormatInterface(comparison.InternalInterfaceCategory, comparison.InternalInterfaceFingerprint)}");
        builder.AppendLine();

        builder.AppendLine("[프록시 출처와 실행]");
        builder.AppendLine(
            $"출처: {FormatNullableEnum(comparison.ProxySourceKind)}");
        builder.AppendLine(
            $"계획: {FormatNullableEnum(comparison.ProxyPlanCode)}");
        builder.AppendLine(
            $"실행 상태: {FormatNullableEnum(comparison.ProxyExecutionStatus)}");
        builder.AppendLine(
            $"분석 상태: {FormatNullableEnum(comparison.ProxyAnalysisStatus)}");
        builder.AppendLine(
            $"적용/분석/성공 후보: {Math.Max(0, comparison.ProxyApplicableEndpointCount)} / {Math.Max(0, comparison.ProxyAnalyzedEndpointCount)} / {Math.Max(0, comparison.ProxySuccessfulEndpointCount)}");
        builder.AppendLine(
            $"서로 다른 인터페이스: {Math.Max(0, comparison.ProxyDistinctInterfaceCount)}개");
        builder.AppendLine(
            $"DIRECT: {(comparison.ProxyDirectPresent ? "있음" : "없음")} · 첫 경로: {(comparison.ProxyDirectIsPrimary ? "예" : "아니오")} · fallback: {(comparison.ProxyDirectFallbackPresent ? "있음" : "없음")}");
        builder.AppendLine(
            $"파싱 오류: {(comparison.ProxyParseErrorsPresent ? "있음" : "없음")}");

        ProxyEndpointRouteAnalysisResult? analysis = proxyExecution.Analysis;
        if (analysis is not null && analysis.Endpoints.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("[프록시 후보별 로컬 경로]");
            foreach (ProxyEndpointRouteEvidenceItem endpoint in
                     analysis.Endpoints.OrderBy(item => item.Sequence))
            {
                builder.AppendLine(FormatEndpoint(endpoint));
            }
        }

        builder.AppendLine();
        builder.AppendLine("[판정]");
        builder.AppendLine(SanitizeNarrative(comparison.Message));
        builder.AppendLine(
            $"해석: {SanitizeNarrative(comparison.Interpretation)}");
        builder.AppendLine(
            $"한계: {SanitizeNarrative(comparison.Limitation)}");
        builder.AppendLine(
            $"다음 확인: {SanitizeNarrative(comparison.NextStep)}");
        return builder.ToString().TrimEnd();
    }

    private static string FormatEndpoint(
        ProxyEndpointRouteEvidenceItem endpoint)
    {
        string transport = Enum.IsDefined(endpoint.Transport)
            ? endpoint.Transport.ToString()
            : ProxyEndpointTransport.Unspecified.ToString();
        string routeStatus = Enum.IsDefined(endpoint.RouteStatus)
            ? endpoint.RouteStatus.ToString()
            : DestinationRouteEvidenceStatus.Failed.ToString();
        string correlation = Enum.IsDefined(endpoint.WlanCorrelationStatus)
            ? endpoint.WlanCorrelationStatus.ToString()
            : RouteWlanCorrelationStatus.NotEvaluated.ToString();
        string category = endpoint.SelectedInterfaceCategory.HasValue
            && Enum.IsDefined(endpoint.SelectedInterfaceCategory.Value)
                ? endpoint.SelectedInterfaceCategory.Value.ToString()
                : "확인 불가";
        string port = endpoint.Port is >= 1 and <= 65535
            ? endpoint.Port.Value.ToString(CultureInfo.InvariantCulture)
            : "-";
        string hostFingerprint = NormalizeFingerprint(
            endpoint.HostFingerprint,
            allowNone: false);
        string interfaceFingerprint = NormalizeFingerprint(
            endpoint.SelectedInterfaceFingerprint,
            allowNone: true);
        string scope = NormalizeScope(endpoint.AppliesToScheme);

        return string.Join(
            " · ",
            $"#{Math.Max(0, endpoint.Sequence)}",
            transport,
            $"범위 {scope}",
            $"포트 {port}",
            $"호스트 지문 {hostFingerprint}",
            $"경로 {routeStatus}",
            $"인터페이스 {category}/{interfaceFingerprint}",
            $"WLAN 상관 {correlation}",
            $"VPN {FormatFlag(endpoint.SelectedInterfaceIsVpn)}",
            $"가상 {FormatFlag(endpoint.SelectedInterfaceIsVirtual)}");
    }

    private static string FormatInterface(
        NetworkAdapterCategory? category,
        string? fingerprint)
    {
        string safeCategory = category.HasValue
            && Enum.IsDefined(category.Value)
                ? category.Value.ToString()
                : "확인 불가";
        return $"{safeCategory} / 지문 {NormalizeFingerprint(fingerprint, allowNone: true)}";
    }

    private static string NormalizeScope(string? value)
    {
        string candidate = (value ?? string.Empty)
            .Trim()
            .ToLowerInvariant();
        return candidate switch
        {
            "http" or "https" or "ftp" or "all"
                or "socks" or "socks4" or "socks5" => candidate,
            "" => "all",
            _ => "unknown"
        };
    }

    private static string NormalizeFingerprint(
        string? value,
        bool allowNone)
    {
        string candidate = (value ?? string.Empty)
            .Trim()
            .ToLowerInvariant();
        if (allowNone
            && (candidate.Length == 0
                || candidate.Equals("없음", StringComparison.Ordinal)))
        {
            return "없음";
        }

        return candidate.Length == RouteInterfaceFingerprint.DisplayLength
               && candidate.All(character =>
                   character is >= '0' and <= '9'
                       or >= 'a' and <= 'f')
            ? candidate
            : "확인 불가";
    }

    private static string FormatFlag(bool? value) =>
        value switch
        {
            true => "예",
            false => "아니오",
            null => "확인 불가"
        };

    private static string FormatNullableEnum<TEnum>(TEnum? value)
        where TEnum : struct, Enum =>
        value.HasValue && Enum.IsDefined(value.Value)
            ? value.Value.ToString()
            : "없음";

    private static string SafeEnum<TEnum>(
        TEnum value,
        TEnum fallback)
        where TEnum : struct, Enum =>
        Enum.IsDefined(value)
            ? value.ToString()
            : fallback.ToString();

    private static string SanitizeNarrative(string? value) =>
        SensitiveDataRedactor.RedactText(value)
        ?? string.Empty;
}
