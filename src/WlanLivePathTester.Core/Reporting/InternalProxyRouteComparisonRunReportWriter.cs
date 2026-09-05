using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using WlanLivePathTester.Core.Routing;

namespace WlanLivePathTester.Core.Reporting;

public static class InternalProxyRouteComparisonRunReportWriter
{
    private const string DefaultFilePrefix = "WlanRouteComparison";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static InternalProxyRouteComparisonRunReportDocument CreateDocument(
        InternalProxyRouteComparisonRunResult result, string applicationVersion,
        DateTimeOffset? generatedAt = null)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationVersion);
        string version = (SensitiveDataRedactor.RedactText(applicationVersion) ?? string.Empty)
            .Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ').Trim();
        version = version.Length == 0 ? "unknown" : version[..Math.Min(version.Length, 128)];
        return new InternalProxyRouteComparisonRunReportDocument(
            SchemaVersion: "1.0", GeneratedAt: generatedAt ?? DateTimeOffset.UtcNow,
            ApplicationName: "WLAN Live Path Tester KO", ApplicationVersion: version,
            SensitiveValuesIncluded: false,
            DataHandlingStatement:
                "이 보고서는 현재 PC에만 저장합니다. 자동 업로드, 텔레메트리 또는 온라인 분석을 수행하지 않습니다.",
            RouteComparison: InternalProxyRouteComparisonRunReportSnapshotMapper.FromResult(result),
            Limitations:
            [
                "내부 DIRECT 대상과 적용 프록시 엔드포인트까지 Windows가 선택하는 첫 로컬 인터페이스만 비교합니다.",
                "Ready는 첫 로컬 NIC가 같다는 뜻이며 서비스·프록시·인터넷·대상 서버가 정상이라는 판정이 아닙니다.",
                "Diverged는 VPN·터널·정적 경로·인터페이스 메트릭 또는 의도된 분할 정책일 수 있습니다.",
                "10자리 호스트·인터페이스 지문은 표시용 축약값입니다. 정확 비교에는 실행 중 전체 Windows GUID만 사용합니다.",
                "프록시 서버 CPU·세션·큐·인증·정책·캐시 및 프록시 이후 인터넷 경로는 확인하지 않습니다.",
                "회사 밖으로 공유하기 전에 보고서 내용을 직접 검토하십시오. 지문도 익명성이나 기밀성을 보장하지 않습니다."
            ]);
    }

    public static InternalProxyRouteComparisonRunReportExportResult WriteAll(
        InternalProxyRouteComparisonRunReportDocument report, string outputDirectory,
        string filePrefix = DefaultFilePrefix) =>
        WriteAll(report, outputDirectory, filePrefix, CancellationToken.None);

    public static InternalProxyRouteComparisonRunReportExportResult WriteAll(
        InternalProxyRouteComparisonRunReportDocument report, string outputDirectory,
        string filePrefix, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        cancellationToken.ThrowIfCancellationRequested();
        string prefix = SensitiveDataRedactor.SafeFileComponent(filePrefix, DefaultFilePrefix);
        string name = prefix + "_" + report.GeneratedAt.ToLocalTime()
            .ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        string json = RenderJson(report);
        cancellationToken.ThrowIfCancellationRequested();
        string csv = RenderCsv(report);
        cancellationToken.ThrowIfCancellationRequested();
        string html = RenderHtml(report);
        cancellationToken.ThrowIfCancellationRequested();
        return LocalReportFileSetWriter.Write(outputDirectory, name, json, csv, html, cancellationToken);
    }

    public static string RenderJson(InternalProxyRouteComparisonRunReportDocument report)
    {
        ArgumentNullException.ThrowIfNull(report);
        return JsonSerializer.Serialize(report, JsonOptions) + Environment.NewLine;
    }

    public static string RenderCsv(InternalProxyRouteComparisonRunReportDocument report)
    {
        ArgumentNullException.ThrowIfNull(report);
        StringBuilder builder = new("section,key,value" + Environment.NewLine);
        foreach (ReportRow row in Rows(report))
        {
            builder.Append(Csv(row.Section)).Append(',').Append(Csv(row.Key))
                .Append(',').AppendLine(Csv(row.Value));
        }
        return builder.ToString();
    }

    public static string RenderHtml(InternalProxyRouteComparisonRunReportDocument report)
    {
        ArgumentNullException.ThrowIfNull(report);
        InternalProxyRouteComparisonRunReportSnapshot run = report.RouteComparison;
        StringBuilder builder = new(24 * 1024);
        builder.Append("<!doctype html><html lang=\"ko\"><head><meta charset=\"utf-8\">")
            .Append("<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">")
            .Append("<meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'none'; style-src 'unsafe-inline'; base-uri 'none'; form-action 'none'\">")
            .Append("<title>내부 DIRECT–프록시 로컬 경로 비교</title><style>")
            .Append("body{margin:0;background:#f4f6f8;color:#17202a;font:14px/1.6 system-ui,sans-serif}main{max-width:1120px;margin:auto;padding:24px}section{background:white;border:1px solid #d8dde3;border-radius:10px;margin:16px 0;padding:18px}h1{font-size:27px}h2{font-size:19px}table{border-collapse:collapse;width:100%;table-layout:fixed}th,td{text-align:left;vertical-align:top;border-bottom:1px solid #e8ebed;padding:8px;overflow-wrap:anywhere}th{width:36%}.sub{color:#566573}.badge{display:inline-block;padding:4px 10px;margin-right:8px;border-radius:16px;background:#eaf2f8}.warning{background:#fff8e7}@media(max-width:640px){main{padding:12px}}@media print{body{background:white}section{break-inside:avoid}}")
            .Append("</style></head><body><main><h1>내부 DIRECT ↔ 프록시 로컬 경로 비교</h1><p class=\"sub\">")
            .Append(H(report.GeneratedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture)))
            .Append(" · ").Append(H(report.ApplicationVersion)).Append(" · 스키마 ").Append(H(report.SchemaVersion))
            .Append("</p><section class=\"warning\"><h2>데이터 처리</h2><p>").Append(H(report.DataHandlingStatement))
            .Append("</p><p>원문 URL·프록시 호스트·전체 GUID·인터페이스 이름·설명과 원본 경로 객체는 포함하지 않습니다.</p></section>")
            .Append("<section><h2>실행 및 비교 판정</h2><span class=\"badge\">").Append(H(run.RunStatus))
            .Append("</span><span class=\"badge\">").Append(H(run.Comparison?.Status ?? "비교 결과 없음"))
            .Append("</span><p>").Append(H(run.Comparison?.Relation ?? "Unknown")).Append("</p></section>");

        foreach (IGrouping<string, ReportRow> section in Rows(report).GroupBy(row => row.Section))
        {
            builder.Append("<section><h2>").Append(H(SectionTitle(section.Key))).Append("</h2><table><tbody>");
            foreach (ReportRow row in section)
            {
                builder.Append("<tr><th>").Append(H(row.Key)).Append("</th><td>")
                    .Append(H(row.Value)).Append("</td></tr>");
            }
            builder.Append("</tbody></table></section>");
        }
        return builder.Append("<footer class=\"sub\">로컬 보고서 · SHA256SUMS와 세 파일의 해시를 함께 확인하십시오.</footer></main></body></html>").ToString();
    }

    // Only the dedicated, already-redacted DTO is enumerated. Never enumerate raw route evidence.
    // Reuse the same field projection for CSV and HTML to prevent one format dropping diagnostics.
    private static IEnumerable<ReportRow> Rows(InternalProxyRouteComparisonRunReportDocument report)
    {
        yield return new("metadata", "schemaVersion", report.SchemaVersion);
        yield return new("metadata", "generatedAt", Iso(report.GeneratedAt));
        yield return new("metadata", "applicationName", report.ApplicationName);
        yield return new("metadata", "applicationVersion", report.ApplicationVersion);
        yield return new("metadata", "sensitiveValuesIncluded", report.SensitiveValuesIncluded.ToString());
        yield return new("metadata", "dataHandling", report.DataHandlingStatement);
        yield return new("run", "completedAt", Iso(report.RouteComparison.CompletedAt));
        foreach (ReportRow row in ScalarRows("run", report.RouteComparison,
                     ["completedAt", "comparison", "proxyEntries", "finding"]))
        {
            yield return row.Key == "runStatus" ? row with { Key = "status" } : row;
        }
        var comparison = report.RouteComparison.Comparison;
        yield return new("comparison", "available", (comparison is not null).ToString());
        if (comparison is not null)
        {
            yield return new("comparison", "evaluatedAt", Iso(comparison.EvaluatedAt));
            foreach (ReportRow row in ScalarRows("comparison", comparison, ["evaluatedAt"])) yield return row;
        }
        for (int index = 0; index < report.RouteComparison.ProxyEntries.Count; index++)
        {
            string section = "proxyEntry." + (index + 1).ToString(CultureInfo.InvariantCulture);
            foreach (ReportRow row in ScalarRows(section, report.RouteComparison.ProxyEntries[index], [])) yield return row;
        }
        foreach (ReportRow row in ScalarRows("finding", report.RouteComparison.Finding, [])) yield return row;
        for (int index = 0; index < report.Limitations.Count; index++)
            yield return new("limitation", (index + 1).ToString(CultureInfo.InvariantCulture), report.Limitations[index]);
    }

    private static IEnumerable<ReportRow> ScalarRows<T>(string section, T value, string[] excluded)
    {
        JsonElement element = JsonSerializer.SerializeToElement(value, JsonOptions);
        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (excluded.Contains(property.Name, StringComparer.Ordinal)) continue;
            yield return new(section, property.Name, Scalar(property.Value));
        }
    }

    private static string Scalar(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Null => string.Empty,
        JsonValueKind.String => value.GetString() ?? string.Empty,
        JsonValueKind.True => bool.TrueString,
        JsonValueKind.False => bool.FalseString,
        JsonValueKind.Number => value.GetRawText(),
        JsonValueKind.Array => string.Join(" | ", value.EnumerateArray().Select(Scalar)),
        _ => throw new InvalidOperationException("REPORT_UNEXPECTED_NESTED_FIELD")
    };

    private static string SectionTitle(string section) => section switch
    {
        "metadata" => "보고서 정보",
        "run" => "실행 요약",
        "comparison" => "정확 인터페이스 비교",
        "finding" => "보고서 판정 · 근거 · 해석 · 다음 확인",
        "limitation" => "판단 한계",
        _ => "프록시 후보 로컬 경로 · " + section
    };

    private static string Csv(string value) =>
        '"' + SensitiveDataRedactor.ProtectCsvFormula(value).Replace("\"", "\"\"") + '"';
    private static string H(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
    private static string Iso(DateTimeOffset value) => value.ToString("O", CultureInfo.InvariantCulture);
    private sealed record ReportRow(string Section, string Key, string Value);
}
