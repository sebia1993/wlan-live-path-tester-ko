using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using WlanLivePathTester.Core.Routing;

namespace WlanLivePathTester.Core.Reporting;

/// <summary>Composes the existing safe route DTO; never retains the raw run.</summary>
public static class LocalReportRouteComparison
{
    private const string RouteFindingPrefix = "INTERNAL_PROXY_ROUTE_";
    private const string NoPatternCode = "NO_CLEAR_FAILURE_PATTERN";
    public const string CorrelationLimitation =
        "경로 비교는 별도 실행에서 확보한 최근 결과입니다. 완료 시각을 확인하십시오. 이 보고서의 다운로드·WLAN 수집과 동시에 측정한 경로라고 단정할 수 없습니다.";
    public const string PathLimitation =
        "경로 비교는 내부 대상과 프록시까지의 첫 로컬 인터페이스만 확인합니다. 같은 NIC나 다른 NIC라는 사실로 프록시 내부 상태·인터넷 경로·속도 저하 원인을 확정하지 않습니다.";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static LocalDiagnosticReport Attach(
        LocalDiagnosticReport report,
        InternalProxyRouteComparisonRunResult? run)
    {
        ArgumentNullException.ThrowIfNull(report);
        // No route input must preserve the legacy report contract. This API
        // does not erase an already-attached snapshot when called with null.
        if (run is null) return report;
        return Normalize(report with
        {
            InternalProxyRouteComparison =
                InternalProxyRouteComparisonRunReportSnapshotMapper.FromResult(run)
        });
    }

    internal static LocalDiagnosticReport Normalize(LocalDiagnosticReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        InternalProxyRouteComparisonRunReportSnapshot? snapshot = report.InternalProxyRouteComparison;
        if (snapshot is null) return report;

        InternalProxyRouteComparisonReportFinding route = snapshot.Finding;
        ReportFinding finding = new(route.Code, route.Severity, route.Title,
            route.Evidence, route.Interpretation, route.Limitation, route.NextStep);
        // Only replace this feature's findings. Existing WLAN, observation,
        // authentication and per-target measurement findings remain untouched.
        List<ReportFinding> findings = report.Findings.Where(item =>
            !item.Code.StartsWith(RouteFindingPrefix, StringComparison.OrdinalIgnoreCase)
            && !item.Code.Equals(NoPatternCode, StringComparison.OrdinalIgnoreCase)).ToList();
        findings.Add(finding);
        return report with
        {
            SchemaVersion = report.SchemaVersion is "1.0" or "1.1" ? "1.2" : report.SchemaVersion,
            Findings = findings.ToArray(),
            Limitations = report.Limitations.Concat([CorrelationLimitation, PathLimitation])
                .Distinct(StringComparer.Ordinal).ToArray()
        };
    }

    // CSV and HTML deliberately share one projection, including every scalar
    // field of the dedicated safe DTO. Raw RouteEvidence is never enumerated.
    internal static IEnumerable<LocalRouteReportRow> Rows(
        InternalProxyRouteComparisonRunReportSnapshot? snapshot)
    {
        if (snapshot is null) yield break;
        const string section = "internalProxyRouteComparison";
        foreach (LocalRouteReportRow row in ScalarRows(section, snapshot,
                     ["comparison", "proxyEntries", "finding"])) yield return row;
        yield return new(section, "findingCode", snapshot.Finding.Code);
        yield return new(section + ".comparison", "available", (snapshot.Comparison is not null).ToString());
        if (snapshot.Comparison is not null)
        {
            foreach (LocalRouteReportRow row in ScalarRows(section + ".comparison", snapshot.Comparison, []))
                yield return row;
        }
        for (int index = 0; index < snapshot.ProxyEntries.Count; index++)
        {
            string entry = section + ".proxyEntry." + (index + 1).ToString(CultureInfo.InvariantCulture);
            foreach (LocalRouteReportRow row in ScalarRows(entry, snapshot.ProxyEntries[index], []))
                yield return row;
        }
    }

    internal static void AppendHtml(
        StringBuilder builder, InternalProxyRouteComparisonRunReportSnapshot? snapshot)
    {
        if (snapshot is null) return;
        builder.Append("<section class=\"card\" id=\"internal-proxy-route-comparison\"><h2>내부 DIRECT–프록시 경로 비교</h2>");
        builder.Append("<p class=\"small\">").Append(H(CorrelationLimitation)).Append("</p><p><span class=\"badge\">")
            .Append(H(snapshot.RunStatus)).Append("</span> <span class=\"badge\">")
            .Append(H(snapshot.Comparison?.Status ?? "비교 결과 없음"))
            .Append("</span></p><p>판정 근거·해석·다음 확인은 아래 판정 항목에 표시합니다.</p>");
        foreach (IGrouping<string, LocalRouteReportRow> group in Rows(snapshot).GroupBy(row => row.Section))
        {
            builder.Append("<h3>").Append(H(SectionTitle(group.Key))).Append("</h3><table class=\"kv\"><tbody>");
            foreach (LocalRouteReportRow row in group)
            {
                builder.Append("<tr><th>").Append(H(row.Key)).Append("</th><td>")
                    .Append(H(row.Value)).Append("</td></tr>");
            }
            builder.Append("</tbody></table>");
        }
        builder.Append("<p class=\"small\">").Append(H(PathLimitation)).Append("</p></section>");
    }

    private static IEnumerable<LocalRouteReportRow> ScalarRows<T>(string section, T value, string[] excluded)
    {
        JsonElement element = JsonSerializer.SerializeToElement(value, JsonOptions);
        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (excluded.Contains(property.Name, StringComparer.Ordinal)) continue;
            yield return new(section, property.Name, Scalar(property.Value));
        }
    }

    private static string Scalar(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Null => string.Empty,
        JsonValueKind.String => element.GetString() ?? string.Empty,
        JsonValueKind.True => bool.TrueString,
        JsonValueKind.False => bool.FalseString,
        JsonValueKind.Number => element.GetRawText(),
        JsonValueKind.Array => string.Join(" | ", element.EnumerateArray().Select(Scalar)),
        _ => throw new InvalidOperationException("UNIFIED_ROUTE_UNEXPECTED_NESTED_FIELD")
    };

    private static string SectionTitle(string section) => section switch
    {
        "internalProxyRouteComparison" => "실행 출처·완료 시각·조회 범위",
        "internalProxyRouteComparison.comparison" => "정확 인터페이스 비교",
        _ => "프록시 후보 · " + section[(section.LastIndexOf('.') + 1)..]
    };
    private static string H(string? text) => WebUtility.HtmlEncode(text ?? string.Empty);
}

internal sealed record LocalRouteReportRow(string Section, string Key, string Value);
