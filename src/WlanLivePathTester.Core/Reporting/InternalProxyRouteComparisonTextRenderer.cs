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
        ProxyEndpointRouteAnalysisResult proxyAnalysis)
    {
        ArgumentNullException.ThrowIfNull(comparison);
        ArgumentNullException.ThrowIfNull(proxyAnalysis);

        StringBuilder builder = new();
        builder.AppendLine("내부 DIRECT ↔ 프록시 로컬 경로 비교");
        builder.AppendLine($"상태: {comparison.Status}");
        builder.AppendLine($"관계: {comparison.Relation}");
        builder.AppendLine($"판정 코드: {comparison.Code}");
        builder.AppendLine(
            $"정확한 전체 인터페이스 ID 비교: {(comparison.ExactIdentityComparisonPerformed ? "수행" : "미수행")}");
        builder.AppendLine();

        builder.AppendLine("[내부 DIRECT]");
        builder.AppendLine(
            $"경로 상태: {comparison.InternalRouteStatus}");
        builder.AppendLine(
            $"인터페이스: {FormatInterface(comparison.InternalInterfaceCategory, comparison.InternalInterfaceFingerprint)}");
        builder.AppendLine();

        builder.AppendLine("[프록시 분석]");
        builder.AppendLine(
            $"분석 상태: {comparison.ProxyAnalysisStatus}");
        builder.AppendLine(
            $"후보: {comparison.ProxyEndpointCount}개 · 경로 확인: {comparison.SuccessfulProxyRouteCount}개 · DIRECT: {comparison.DirectDirectiveCount}개");
        builder.AppendLine(
            $"후보 상한으로 잘림: {(comparison.ProxyAnalysisWasTruncated ? "있음" : "없음")}");

        foreach (ProxyEndpointRouteEntry entry in
                 proxyAnalysis.Entries.OrderBy(entry => entry.Sequence))
        {
            builder.AppendLine(FormatProxyEntry(entry));
        }

        ProxyDirectiveIssue[] issues = proxyAnalysis.ParseIssues
            .OrderBy(issue => issue.SegmentIndex)
            .ThenBy(issue => issue.Code, StringComparer.Ordinal)
            .ToArray();
        if (issues.Length > 0)
        {
            builder.AppendLine();
            builder.AppendLine("[프록시 문자열 경고]");
            foreach (ProxyDirectiveIssue issue in issues)
            {
                builder.AppendLine(
                    $"구간 {Math.Max(0, issue.SegmentIndex)} · {NormalizeIssueSeverity(issue.Severity)} · {NormalizeIssueCode(issue.Code)}");
            }
        }

        builder.AppendLine();
        builder.AppendLine("[판정]");
        builder.AppendLine(comparison.Message);
        builder.AppendLine($"해석: {comparison.Interpretation}");
        builder.AppendLine($"한계: {comparison.Limitation}");
        builder.AppendLine($"다음 확인: {comparison.NextStep}");
        return builder.ToString().TrimEnd();
    }

    private static string FormatProxyEntry(
        ProxyEndpointRouteEntry entry)
    {
        string kind = Enum.IsDefined(entry.Kind)
            ? entry.Kind.ToString()
            : "Unknown";
        string syntax = Enum.IsDefined(entry.SourceSyntax)
            ? entry.SourceSyntax.ToString()
            : "Unknown";
        string status = Enum.IsDefined(entry.Status)
            ? entry.Status.ToString()
            : "Unknown";
        string scope = NormalizeScope(entry.Scope);
        string port = entry.Port is >= 1 and <= 65535
            ? entry.Port.Value.ToString(CultureInfo.InvariantCulture)
            : "-";
        string hostFingerprint = NormalizeFingerprint(
            entry.HostFingerprint,
            allowNone: entry.IsDirect);
        string selected = FormatInterface(
            NormalizeAdapterCategory(entry.SelectedInterfaceCategory),
            NormalizeFingerprint(
                entry.SelectedInterfaceFingerprint,
                allowNone: true));
        string correlation = NormalizeCorrelation(
            entry.WlanCorrelationStatus);

        return entry.IsDirect
            ? $"#{Math.Max(0, entry.Sequence)} DIRECT · 범위 {scope} · 구문 {syntax} · 상태 {status} · 네트워크 조회 없음"
            : $"#{Math.Max(0, entry.Sequence)} {kind} · 범위 {scope} · 포트 {port} · 호스트 지문 {hostFingerprint} · 상태 {status} · 인터페이스 {selected} · WLAN 상관 {correlation}";
    }

    private static string FormatInterface(
        string? category,
        string? fingerprint)
    {
        string safeCategory = NormalizeAdapterCategory(category) ?? "확인 불가";
        string safeFingerprint = NormalizeFingerprint(
            fingerprint,
            allowNone: true);
        return $"{safeCategory} / 지문 {safeFingerprint}";
    }

    private static string NormalizeScope(string? value)
    {
        string candidate = (value ?? string.Empty)
            .Trim()
            .ToLowerInvariant();
        return candidate switch
        {
            "all" or "http" or "https" or "ftp"
                or "socks" or "socks4" or "socks5" => candidate,
            _ => "unknown"
        };
    }

    private static string? NormalizeAdapterCategory(string? value) =>
        Enum.TryParse(
            value,
            ignoreCase: true,
            out NetworkAdapterCategory parsed)
            ? parsed.ToString()
            : null;

    private static string NormalizeCorrelation(string? value) =>
        Enum.TryParse(
            value,
            ignoreCase: true,
            out RouteWlanCorrelationStatus parsed)
            ? parsed.ToString()
            : RouteWlanCorrelationStatus.NotEvaluated.ToString();

    private static string NormalizeIssueSeverity(
        ProxyDirectiveIssueSeverity severity) =>
        Enum.IsDefined(severity)
            ? severity.ToString()
            : ProxyDirectiveIssueSeverity.Error.ToString();

    private static string NormalizeIssueCode(string? value)
    {
        string candidate = (value ?? string.Empty)
            .Trim()
            .ToUpperInvariant();
        if (candidate.Length is < 1 or > 64
            || candidate.Any(character =>
                !(character is >= 'A' and <= 'Z'
                    or >= '0' and <= '9'
                    or '_')))
        {
            return "INVALID_ISSUE_CODE";
        }

        return candidate;
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
                || candidate.Equals(
                    "없음",
                    StringComparison.Ordinal)))
        {
            return "없음";
        }

        return candidate.Length == 10
               && candidate.All(character =>
                   character is >= '0' and <= '9'
                       or >= 'a' and <= 'f')
            ? candidate
            : "확인 불가";
    }
}
