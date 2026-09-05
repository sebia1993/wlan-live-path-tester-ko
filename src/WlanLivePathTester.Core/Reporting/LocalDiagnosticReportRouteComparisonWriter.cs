using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using WlanLivePathTester.Core.Routing;

namespace WlanLivePathTester.Core.Reporting;

public sealed record LocalDiagnosticReportRouteComparisonExportResult(
    string OutputDirectory,
    string JsonPath,
    string CsvPath,
    string HtmlPath,
    string Sha256Path,
    IReadOnlyDictionary<string, string> Sha256);

public static class LocalDiagnosticReportRouteComparisonWriter
{
    private const string JsonPropertyName =
        "internalProxyRouteComparison";
    private const string DefaultFilePrefix =
        "WlanUnifiedDiagnostic";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static string RenderJson(
        LocalDiagnosticReport report,
        InternalProxyRouteComparisonRunResult? routeComparison)
    {
        ArgumentNullException.ThrowIfNull(report);
        string baseJson = LocalReportWriter.RenderJson(report);
        if (routeComparison is null)
        {
            return baseJson;
        }

        JsonObject root = JsonNode.Parse(baseJson) as JsonObject
            ?? throw new InvalidOperationException(
                "기존 통합 보고서 JSON의 최상위 객체를 확인할 수 없습니다.");
        InternalProxyRouteComparisonRunSnapshot snapshot =
            InternalProxyRouteComparisonRunSnapshotMapper.FromResult(
                routeComparison);
        root[JsonPropertyName] = JsonSerializer.SerializeToNode(
            snapshot,
            JsonOptions);
        AppendFindingToJson(root, snapshot.Finding);
        return root.ToJsonString(JsonOptions) + Environment.NewLine;
    }

    public static string RenderCsv(
        LocalDiagnosticReport report,
        InternalProxyRouteComparisonRunResult? routeComparison)
    {
        ArgumentNullException.ThrowIfNull(report);
        string baseCsv = LocalReportWriter.RenderCsv(report);
        if (routeComparison is null)
        {
            return baseCsv;
        }

        InternalProxyRouteComparisonRunSnapshot snapshot =
            InternalProxyRouteComparisonRunSnapshotMapper.FromResult(
                routeComparison);
        StringBuilder builder = new(baseCsv);
        if (builder.Length > 0
            && builder[^1] is not '\r' and not '\n')
        {
            builder.AppendLine();
        }

        AddSnapshotCsv(builder, snapshot);
        bool findingAlreadyPresent = report.Findings.Any(finding =>
            finding.Code.Equals(
                snapshot.Finding.Code,
                StringComparison.Ordinal));
        if (!findingAlreadyPresent)
        {
            AddFindingCsv(builder, snapshot.Finding);
        }

        return builder.ToString();
    }

    public static string RenderHtml(
        LocalDiagnosticReport report,
        InternalProxyRouteComparisonRunResult? routeComparison)
    {
        ArgumentNullException.ThrowIfNull(report);
        string baseHtml = LocalReportWriter.RenderHtml(report);
        if (routeComparison is null)
        {
            return baseHtml;
        }

        InternalProxyRouteComparisonRunSnapshot snapshot =
            InternalProxyRouteComparisonRunSnapshotMapper.FromResult(
                routeComparison);
        bool findingAlreadyPresent = report.Findings.Any(finding =>
            finding.Code.Equals(
                snapshot.Finding.Code,
                StringComparison.Ordinal));
        string section = RenderRouteComparisonHtmlSection(
            snapshot,
            includeFinding: !findingAlreadyPresent);
        int insertionIndex = FindHtmlInsertionIndex(baseHtml);
        return baseHtml.Insert(insertionIndex, section);
    }

    public static LocalDiagnosticReportRouteComparisonExportResult
        WriteAll(
            LocalDiagnosticReport report,
            InternalProxyRouteComparisonRunResult? routeComparison,
            string outputDirectory,
            string filePrefix = DefaultFilePrefix,
            DateTimeOffset? generatedAt = null)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);

        string fullDirectory = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(fullDirectory);
        string safePrefix = SensitiveDataRedactor.SafeFileComponent(
            filePrefix,
            DefaultFilePrefix);
        DateTimeOffset timestampValue = generatedAt
            ?? report.Metadata.GeneratedAt;
        string timestamp = timestampValue.ToLocalTime().ToString(
            "yyyyMMdd_HHmmss",
            CultureInfo.InvariantCulture);
        string baseName = GetAvailableBaseName(
            fullDirectory,
            $"{safePrefix}_{timestamp}");

        string jsonPath = Path.Combine(
            fullDirectory,
            baseName + ".json");
        string csvPath = Path.Combine(
            fullDirectory,
            baseName + ".csv");
        string htmlPath = Path.Combine(
            fullDirectory,
            baseName + ".html");
        string sha256Path = Path.Combine(
            fullDirectory,
            baseName + "_SHA256SUMS.txt");

        WriteAtomic(
            jsonPath,
            RenderJson(report, routeComparison),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        WriteAtomic(
            csvPath,
            RenderCsv(report, routeComparison),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        WriteAtomic(
            htmlPath,
            RenderHtml(report, routeComparison),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        Dictionary<string, string> hashes = new(
            StringComparer.OrdinalIgnoreCase)
        {
            [Path.GetFileName(jsonPath)] = ComputeSha256(jsonPath),
            [Path.GetFileName(csvPath)] = ComputeSha256(csvPath),
            [Path.GetFileName(htmlPath)] = ComputeSha256(htmlPath)
        };
        string checksumText = string.Join(
            Environment.NewLine,
            hashes.OrderBy(
                    pair => pair.Key,
                    StringComparer.OrdinalIgnoreCase)
                .Select(pair => $"{pair.Value}  {pair.Key}"))
            + Environment.NewLine;
        WriteAtomic(
            sha256Path,
            checksumText,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        return new LocalDiagnosticReportRouteComparisonExportResult(
            OutputDirectory: fullDirectory,
            JsonPath: jsonPath,
            CsvPath: csvPath,
            HtmlPath: htmlPath,
            Sha256Path: sha256Path,
            Sha256: hashes);
    }

    private static void AppendFindingToJson(
        JsonObject root,
        ReportFinding finding)
    {
        JsonArray findings;
        if (root["findings"] is JsonArray existing)
        {
            findings = existing;
        }
        else
        {
            findings = [];
            root["findings"] = findings;
        }

        bool duplicate = findings.Any(node =>
            node is JsonObject findingNode
            && findingNode["code"]?.GetValue<string>()
                .Equals(
                    finding.Code,
                    StringComparison.Ordinal) == true);
        if (!duplicate)
        {
            findings.Add(JsonSerializer.SerializeToNode(
                finding,
                JsonOptions));
        }
    }

    private static void AddSnapshotCsv(
        StringBuilder builder,
        InternalProxyRouteComparisonRunSnapshot snapshot)
    {
        const string section = "internalProxyRouteComparison";
        AddCsv(builder, section, "schemaVersion", snapshot.SchemaVersion);
        AddCsv(builder, section, "completedAt", Iso(snapshot.CompletedAt));
        AddCsv(builder, section, "runStatus", snapshot.RunStatus);
        AddCsv(
            builder,
            section,
            "proxySourceKind",
            snapshot.ProxySourceKind);
        AddCsv(
            builder,
            section,
            "proxyDecision",
            snapshot.ProxyDecision);
        AddCsv(
            builder,
            section,
            "targetScheme",
            snapshot.TargetScheme ?? string.Empty);
        AddCsv(
            builder,
            section,
            "internalRouteStatus",
            snapshot.InternalRouteStatus ?? string.Empty);
        AddCsv(
            builder,
            section,
            "proxyRouteStatus",
            snapshot.ProxyRouteStatus ?? string.Empty);
        AddCsv(
            builder,
            section,
            "comparisonStatus",
            snapshot.ComparisonStatus ?? string.Empty);
        AddCsv(
            builder,
            section,
            "sameLocalInterface",
            Invariant(snapshot.SameLocalInterface));
        AddCsv(
            builder,
            section,
            "parsedProxyEndpointCount",
            Invariant(snapshot.ParsedProxyEndpointCount));
        AddCsv(
            builder,
            section,
            "analyzedProxyEndpointCount",
            Invariant(snapshot.AnalyzedProxyEndpointCount));
        AddCsv(
            builder,
            section,
            "successfulProxyEndpointCount",
            Invariant(snapshot.SuccessfulProxyEndpointCount));
        AddCsv(
            builder,
            section,
            "proxyDistinctInterfaceCount",
            Invariant(snapshot.ProxyDistinctInterfaceCount));
        AddCsv(
            builder,
            section,
            "directPresent",
            Invariant(snapshot.DirectPresent));
        AddCsv(
            builder,
            section,
            "directFallback",
            Invariant(snapshot.DirectFallback));
        AddCsv(
            builder,
            section,
            "expectedWlanIdentityAvailable",
            Invariant(snapshot.ExpectedWlanIdentityAvailable));
        AddCsv(
            builder,
            section,
            "internalRouteReadPerformed",
            Invariant(snapshot.InternalRouteReadPerformed));
        AddCsv(
            builder,
            section,
            "proxyRouteAnalysisPerformed",
            Invariant(snapshot.ProxyRouteAnalysisPerformed));
        AddCsv(
            builder,
            section,
            "internalEvidencePartial",
            Invariant(snapshot.InternalEvidencePartial));
        AddCsv(
            builder,
            section,
            "proxyEvidencePartial",
            Invariant(snapshot.ProxyEvidencePartial));
        AddCsv(
            builder,
            section,
            "anyVirtualInterface",
            Invariant(snapshot.AnyVirtualInterface));
        AddCsv(
            builder,
            section,
            "anyVpnOrTunnelInterface",
            Invariant(snapshot.AnyVpnOrTunnelInterface));
        AddCsv(
            builder,
            section,
            "dataHandling",
            snapshot.DataHandlingStatement);

        AddInterfaceCsv(
            builder,
            "internalProxyRouteComparison.internalInterface",
            snapshot.InternalInterface);
        AddInterfaceCsv(
            builder,
            "internalProxyRouteComparison.proxyInterface",
            snapshot.ProxyInterface);
    }

    private static void AddInterfaceCsv(
        StringBuilder builder,
        string section,
        SafeLocalRouteInterfaceSnapshot? routeInterface)
    {
        AddCsv(
            builder,
            section,
            "available",
            Invariant(routeInterface is not null));
        if (routeInterface is null)
        {
            return;
        }

        AddCsv(
            builder,
            section,
            "interfaceFingerprint",
            routeInterface.InterfaceFingerprint);
        AddCsv(
            builder,
            section,
            "category",
            routeInterface.Category);
        AddCsv(
            builder,
            section,
            "isVirtual",
            Invariant(routeInterface.IsVirtual));
        AddCsv(
            builder,
            section,
            "isVpn",
            Invariant(routeInterface.IsVpn));
        AddCsv(
            builder,
            section,
            "isUp",
            Invariant(routeInterface.IsUp));
        AddCsv(
            builder,
            section,
            "hasDefaultGateway",
            Invariant(routeInterface.HasDefaultGateway));
        AddCsv(
            builder,
            section,
            "matchesExpectedWlan",
            Invariant(routeInterface.MatchesExpectedWlan));
    }

    private static void AddFindingCsv(
        StringBuilder builder,
        ReportFinding finding)
    {
        const string section =
            "internalProxyRouteComparison.finding";
        AddCsv(builder, section, "code", finding.Code);
        AddCsv(builder, section, "severity", finding.Severity);
        AddCsv(builder, section, "title", finding.Title);
        AddCsv(builder, section, "evidence", finding.Evidence);
        AddCsv(
            builder,
            section,
            "interpretation",
            finding.Interpretation);
        AddCsv(
            builder,
            section,
            "limitation",
            finding.Limitation);
        AddCsv(builder, section, "nextStep", finding.NextStep);
    }

    private static string RenderRouteComparisonHtmlSection(
        InternalProxyRouteComparisonRunSnapshot snapshot,
        bool includeFinding)
    {
        StringBuilder builder = new(capacity: 12 * 1024);
        builder.Append(
            "<section class=\"card\" id=\"internal-proxy-route-comparison\"><h2>내부 DIRECT ↔ 프록시 로컬 경로 비교</h2><p><strong>실행 상태:</strong> ");
        Html(builder, snapshot.RunStatus);
        builder.Append(" · <strong>비교 상태:</strong> ");
        Html(builder, snapshot.ComparisonStatus ?? "없음");
        builder.Append("</p><div class=\"grid\">");
        Metric(builder, "프록시 출처", snapshot.ProxySourceKind);
        Metric(builder, "프록시 결정", snapshot.ProxyDecision);
        Metric(
            builder,
            "대상 스킴",
            snapshot.TargetScheme ?? "확인 안 됨");
        Metric(
            builder,
            "내부 / 프록시 상태",
            $"{snapshot.InternalRouteStatus ?? "-"} / {snapshot.ProxyRouteStatus ?? "-"}");
        Metric(
            builder,
            "파싱 / 분석 / 성공 후보",
            $"{snapshot.ParsedProxyEndpointCount} / {snapshot.AnalyzedProxyEndpointCount} / {snapshot.SuccessfulProxyEndpointCount}");
        Metric(
            builder,
            "같은 인터페이스",
            snapshot.SameLocalInterface.HasValue
                ? snapshot.SameLocalInterface.Value.ToString()
                : "판정 안 함");
        Metric(
            builder,
            "DIRECT / fallback",
            $"{(snapshot.DirectPresent ? "있음" : "없음")} / {(snapshot.DirectFallback ? "있음" : "없음")}");
        Metric(
            builder,
            "VPN·터널 / 가상 NIC",
            $"{(snapshot.AnyVpnOrTunnelInterface ? "있음" : "확인 안 됨")} / {(snapshot.AnyVirtualInterface ? "있음" : "확인 안 됨")}");
        builder.Append("</div>");
        AppendInterfaceHtml(
            builder,
            "내부 DIRECT 인터페이스",
            snapshot.InternalInterface);
        AppendInterfaceHtml(
            builder,
            "프록시 인터페이스",
            snapshot.ProxyInterface);

        if (includeFinding)
        {
            ReportFinding finding = snapshot.Finding;
            builder.Append("<article><h3>");
            Html(builder, finding.Title);
            builder.Append(" <span class=\"small\">[");
            Html(builder, finding.Severity);
            builder.Append(" · ");
            Html(builder, finding.Code);
            builder.Append("]</span></h3><p><strong>근거:</strong> ");
            Html(builder, finding.Evidence);
            builder.Append("</p><p><strong>해석:</strong> ");
            Html(builder, finding.Interpretation);
            builder.Append("</p><p><strong>다음 확인:</strong> ");
            Html(builder, finding.NextStep);
            builder.Append("</p><p class=\"small\"><strong>한계:</strong> ");
            Html(builder, finding.Limitation);
            builder.Append("</p></article>");
        }
        else
        {
            builder.Append(
                "<p class=\"small\">동일한 경로 비교 Finding이 기존 통합 Finding 목록에 이미 포함돼 있어 이 섹션에서는 중복 표시하지 않았습니다.</p>");
        }

        builder.Append("<p class=\"small\">");
        Html(builder, snapshot.DataHandlingStatement);
        builder.Append("</p></section>");
        return builder.ToString();
    }

    private static void AppendInterfaceHtml(
        StringBuilder builder,
        string title,
        SafeLocalRouteInterfaceSnapshot? routeInterface)
    {
        builder.Append("<h3>");
        Html(builder, title);
        builder.Append("</h3>");
        if (routeInterface is null)
        {
            builder.Append(
                "<p>단일 안전 인터페이스 근거를 확인하지 못했습니다.</p>");
            return;
        }

        builder.Append("<table><tbody>");
        TableRow(
            builder,
            "인터페이스 지문",
            routeInterface.InterfaceFingerprint);
        TableRow(builder, "범주", routeInterface.Category);
        TableRow(
            builder,
            "가상 / VPN",
            $"{Invariant(routeInterface.IsVirtual)} / {Invariant(routeInterface.IsVpn)}");
        TableRow(
            builder,
            "Up / 기본 게이트웨이",
            $"{Invariant(routeInterface.IsUp)} / {Invariant(routeInterface.HasDefaultGateway)}");
        TableRow(
            builder,
            "현재 WLAN 일치",
            Invariant(routeInterface.MatchesExpectedWlan));
        builder.Append("</tbody></table>");
    }

    private static void Metric(
        StringBuilder builder,
        string label,
        string value)
    {
        builder.Append(
            "<div class=\"metric\"><span class=\"small\">");
        Html(builder, label);
        builder.Append("</span><strong>");
        Html(builder, value);
        builder.Append("</strong></div>");
    }

    private static void TableRow(
        StringBuilder builder,
        string key,
        string value)
    {
        builder.Append("<tr><th>");
        Html(builder, key);
        builder.Append("</th><td>");
        Html(
            builder,
            string.IsNullOrWhiteSpace(value)
                ? "확인 안 됨"
                : value);
        builder.Append("</td></tr>");
    }

    private static int FindHtmlInsertionIndex(string html)
    {
        int main = html.LastIndexOf(
            "</main>",
            StringComparison.OrdinalIgnoreCase);
        if (main >= 0)
        {
            return main;
        }

        int body = html.LastIndexOf(
            "</body>",
            StringComparison.OrdinalIgnoreCase);
        if (body >= 0)
        {
            return body;
        }

        throw new InvalidOperationException(
            "기존 통합 HTML 보고서의 삽입 위치를 확인할 수 없습니다.");
    }

    private static void AddCsv(
        StringBuilder builder,
        string section,
        string key,
        string value)
    {
        builder.Append(Csv(section));
        builder.Append(',');
        builder.Append(Csv(key));
        builder.Append(',');
        builder.AppendLine(Csv(value));
    }

    private static string Csv(string value)
    {
        string safe = SensitiveDataRedactor.ProtectCsvFormula(value);
        return '"' + safe.Replace("\"", "\"\"") + '"';
    }

    private static string Invariant(object? value) =>
        value switch
        {
            null => string.Empty,
            IFormattable formattable => formattable.ToString(
                null,
                CultureInfo.InvariantCulture),
            _ => Convert.ToString(
                    value,
                    CultureInfo.InvariantCulture)
                ?? string.Empty
        };

    private static string Iso(DateTimeOffset value) =>
        value.ToString("O", CultureInfo.InvariantCulture);

    private static void Html(
        StringBuilder builder,
        string? value) =>
        builder.Append(WebUtility.HtmlEncode(value ?? string.Empty));

    private static string GetAvailableBaseName(
        string directory,
        string desired)
    {
        for (int suffix = 0; suffix <= 9999; suffix++)
        {
            string candidate = suffix == 0
                ? desired
                : $"{desired}_{suffix}";
            if (!File.Exists(
                    Path.Combine(directory, candidate + ".json"))
                && !File.Exists(
                    Path.Combine(directory, candidate + ".csv"))
                && !File.Exists(
                    Path.Combine(directory, candidate + ".html"))
                && !File.Exists(Path.Combine(
                    directory,
                    candidate + "_SHA256SUMS.txt")))
            {
                return candidate;
            }
        }

        throw new IOException(
            "사용 가능한 통합 로컬 진단 보고서 파일 이름을 만들지 못했습니다.");
    }

    private static void WriteAtomic(
        string destination,
        string content,
        Encoding encoding)
    {
        string directory = Path.GetDirectoryName(destination)
            ?? throw new InvalidOperationException(
                "통합 보고서 출력 디렉터리를 확인할 수 없습니다.");
        string temporary = Path.Combine(
            directory,
            $".{Path.GetFileName(destination)}.{Guid.NewGuid():N}.tmp");

        try
        {
            File.WriteAllText(temporary, content, encoding);
            File.Move(temporary, destination, overwrite: false);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private static string ComputeSha256(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream))
            .ToLowerInvariant();
    }
}
