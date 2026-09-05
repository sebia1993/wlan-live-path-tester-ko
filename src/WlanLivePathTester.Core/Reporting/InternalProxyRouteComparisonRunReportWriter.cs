using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WlanLivePathTester.Core.Routing;

namespace WlanLivePathTester.Core.Reporting;

public sealed record InternalProxyRouteComparisonRunReportDocument(
    string SchemaVersion,
    DateTimeOffset GeneratedAt,
    string ApplicationName,
    string ApplicationVersion,
    bool SensitiveValuesIncluded,
    string DataHandlingStatement,
    InternalProxyRouteComparisonRunSnapshot RouteComparison,
    IReadOnlyList<string> Limitations);

public sealed record InternalProxyRouteComparisonRunReportExportResult(
    string OutputDirectory,
    string JsonPath,
    string CsvPath,
    string HtmlPath,
    string Sha256Path,
    IReadOnlyDictionary<string, string> Sha256);

public static class InternalProxyRouteComparisonRunReportWriter
{
    private const string DefaultFilePrefix =
        "WlanInternalProxyRouteComparison";
    private const int MaximumApplicationVersionLength = 128;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static InternalProxyRouteComparisonRunReportDocument
        CreateDocument(
            InternalProxyRouteComparisonRunResult result,
            string applicationVersion,
            DateTimeOffset? generatedAt = null)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationVersion);

        return new InternalProxyRouteComparisonRunReportDocument(
            SchemaVersion: "1.0",
            GeneratedAt: generatedAt ?? DateTimeOffset.UtcNow,
            ApplicationName: "WLAN Live Path Tester KO",
            ApplicationVersion: SanitizeApplicationVersion(
                applicationVersion),
            SensitiveValuesIncluded: false,
            DataHandlingStatement:
                "이 보고서는 내부 DIRECT–프록시 로컬 경로 비교의 검증된 상태, 개수, Boolean과 짧은 비가역 인터페이스 지문만 현재 PC에 저장합니다. 자동 업로드, 텔레메트리 또는 온라인 분석을 수행하지 않습니다.",
            RouteComparison:
                InternalProxyRouteComparisonRunSnapshotMapper
                    .FromResult(result),
            Limitations:
            [
                "이 결과는 현재 PC에서 내부 DIRECT 대상과 프록시 엔드포인트까지 선택되는 Windows 첫 로컬 인터페이스만 비교합니다.",
                "Ready는 첫 로컬 NIC가 같다는 뜻이며 내부 서비스, 프록시, 인터넷 회선 또는 대상 서버의 성능이 정상이라는 뜻이 아닙니다.",
                "Diverged는 VPN·터널·정적 경로·인터페이스 메트릭 또는 의도된 유선·무선 분할 정책일 수 있으며 단독 장애 증거가 아닙니다.",
                "호스트·인터페이스 지문은 SHA-256 앞 10자의 표시값이며 정확한 NIC 판정은 같은 실행 세션의 원본 Windows 인터페이스 ID로만 수행합니다.",
                "프록시 서버의 CPU·세션·큐·인증·정책·캐시·클러스터 상태와 프록시 이후 인터넷 경로는 확인하지 않습니다.",
                "마스킹은 보조 수단이므로 회사 밖으로 공유하기 전에 JSON·CSV·HTML 내용을 사용자가 다시 검토해야 합니다."
            ]);
    }

    public static InternalProxyRouteComparisonRunReportExportResult
        WriteAll(
            InternalProxyRouteComparisonRunReportDocument report,
            string outputDirectory,
            string filePrefix = DefaultFilePrefix)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);

        string fullDirectory = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(fullDirectory);
        string safePrefix = SensitiveDataRedactor.SafeFileComponent(
            filePrefix,
            DefaultFilePrefix);
        string timestamp = report.GeneratedAt.ToLocalTime()
            .ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
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
            RenderJson(report),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        WriteAtomic(
            csvPath,
            RenderCsv(report),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        WriteAtomic(
            htmlPath,
            RenderHtml(report),
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

        return new InternalProxyRouteComparisonRunReportExportResult(
            OutputDirectory: fullDirectory,
            JsonPath: jsonPath,
            CsvPath: csvPath,
            HtmlPath: htmlPath,
            Sha256Path: sha256Path,
            Sha256: hashes);
    }

    public static string RenderJson(
        InternalProxyRouteComparisonRunReportDocument report)
    {
        ArgumentNullException.ThrowIfNull(report);
        return JsonSerializer.Serialize(report, JsonOptions)
            + Environment.NewLine;
    }

    public static string RenderCsv(
        InternalProxyRouteComparisonRunReportDocument report)
    {
        ArgumentNullException.ThrowIfNull(report);

        InternalProxyRouteComparisonRunSnapshot snapshot =
            report.RouteComparison;
        StringBuilder builder = new();
        builder.AppendLine("section,key,value");
        AddCsv(builder, "metadata", "schemaVersion", report.SchemaVersion);
        AddCsv(
            builder,
            "metadata",
            "generatedAt",
            Iso(report.GeneratedAt));
        AddCsv(
            builder,
            "metadata",
            "applicationName",
            report.ApplicationName);
        AddCsv(
            builder,
            "metadata",
            "applicationVersion",
            report.ApplicationVersion);
        AddCsv(
            builder,
            "metadata",
            "sensitiveValuesIncluded",
            Invariant(report.SensitiveValuesIncluded));
        AddCsv(
            builder,
            "metadata",
            "dataHandling",
            report.DataHandlingStatement);

        AddRunCsv(builder, snapshot);
        AddInterfaceCsv(
            builder,
            "internalInterface",
            snapshot.InternalInterface);
        AddInterfaceCsv(
            builder,
            "proxyInterface",
            snapshot.ProxyInterface);
        AddFindingCsv(builder, snapshot.Finding);

        for (int index = 0; index < report.Limitations.Count; index++)
        {
            AddCsv(
                builder,
                "limitation",
                (index + 1).ToString(
                    CultureInfo.InvariantCulture),
                report.Limitations[index]);
        }

        return builder.ToString();
    }

    public static string RenderHtml(
        InternalProxyRouteComparisonRunReportDocument report)
    {
        ArgumentNullException.ThrowIfNull(report);

        InternalProxyRouteComparisonRunSnapshot snapshot =
            report.RouteComparison;
        StringBuilder builder = new(capacity: 30 * 1024);
        builder.Append(
            "<!doctype html><html lang=\"ko\"><head><meta charset=\"utf-8\">");
        builder.Append(
            "<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">");
        builder.Append(
            "<meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'none'; style-src 'unsafe-inline'; base-uri 'none'; form-action 'none'; frame-ancestors 'none'\">");
        builder.Append(
            "<title>내부 DIRECT–프록시 로컬 경로 비교</title><style>");
        builder.Append(
            "body{margin:0;background:#f4f6f8;color:#17202a;font:14px/1.55 system-ui,-apple-system,'Segoe UI',sans-serif}main{max-width:1120px;margin:auto;padding:28px}h1{font-size:27px;margin:0}h2{font-size:19px;margin:0 0 12px}h3{font-size:16px}.sub,.small{color:#566573}.card{background:#fff;border:1px solid #d8dde3;border-radius:12px;padding:18px;margin-top:16px;box-shadow:0 1px 2px rgba(0,0,0,.03)}.privacy{background:#fff8e7;border-color:#e8ce8a}.grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(180px,1fr));gap:10px}.metric{background:#f8fafb;border-radius:8px;padding:12px}.metric strong{display:block;font-size:16px;overflow-wrap:anywhere}.badge{display:inline-block;border-radius:999px;padding:4px 10px;font-size:12px;background:#eaf2f8;color:#1b4f72}.ready{background:#e8f6f3;color:#0e6251}.warn{background:#fff3cd;color:#7d6608}.bad{background:#fdecea;color:#922b21}.scroll{overflow:auto}table{width:100%;border-collapse:collapse}th,td{padding:8px;border-bottom:1px solid #e8ebed;text-align:left;vertical-align:top}.finding{border-left:5px solid #d6a900}@media(max-width:640px){main{padding:16px}.grid{display:block}.metric{margin-top:8px}}@media print{body{background:#fff}.card{box-shadow:none;break-inside:avoid}main{max-width:none;padding:0}}</style></head><body><main>");
        builder.Append(
            "<header><h1>내부 DIRECT ↔ 프록시 로컬 경로 비교</h1><div class=\"sub\">");
        Html(
            builder,
            report.GeneratedAt.ToLocalTime().ToString(
                "yyyy-MM-dd HH:mm:ss zzz",
                CultureInfo.InvariantCulture));
        builder.Append(" · 애플리케이션 ");
        Html(builder, report.ApplicationVersion);
        builder.Append(" · 스키마 ");
        Html(builder, report.SchemaVersion);
        builder.Append("</div></header>");

        builder.Append(
            "<section class=\"card privacy\"><h2>데이터 처리</h2><p>");
        Html(builder, report.DataHandlingStatement);
        builder.Append(
            "</p><p class=\"small\">내부 URL·프록시 호스트·전체 인터페이스 ID·이름·설명·IP·MAC·게이트웨이·DNS·SSID·BSSID와 원본 경로 객체를 포함하지 않습니다.</p></section>");

        AppendRunSummaryHtml(builder, snapshot);
        AppendInterfaceHtml(
            builder,
            "내부 DIRECT 인터페이스",
            snapshot.InternalInterface);
        AppendInterfaceHtml(
            builder,
            "프록시 인터페이스",
            snapshot.ProxyInterface);
        AppendFindingHtml(builder, snapshot.Finding);

        builder.Append(
            "<section class=\"card\"><h2>판단 한계</h2><ul>");
        foreach (string limitation in report.Limitations)
        {
            builder.Append("<li>");
            Html(builder, limitation);
            builder.Append("</li>");
        }

        builder.Append(
            "</ul></section><footer class=\"small\">현재 PC에서 생성한 로컬 보고서입니다.</footer></main></body></html>");
        return builder.ToString();
    }

    private static void AddRunCsv(
        StringBuilder builder,
        InternalProxyRouteComparisonRunSnapshot snapshot)
    {
        AddCsv(builder, "run", "status", snapshot.RunStatus);
        AddCsv(
            builder,
            "run",
            "proxySourceKind",
            snapshot.ProxySourceKind);
        AddCsv(
            builder,
            "run",
            "proxyDecision",
            snapshot.ProxyDecision);
        AddCsv(
            builder,
            "run",
            "targetScheme",
            snapshot.TargetScheme ?? string.Empty);
        AddCsv(
            builder,
            "run",
            "internalRouteStatus",
            snapshot.InternalRouteStatus ?? string.Empty);
        AddCsv(
            builder,
            "run",
            "proxyRouteStatus",
            snapshot.ProxyRouteStatus ?? string.Empty);
        AddCsv(
            builder,
            "run",
            "comparisonStatus",
            snapshot.ComparisonStatus ?? string.Empty);
        AddCsv(
            builder,
            "run",
            "sameLocalInterface",
            Invariant(snapshot.SameLocalInterface));
        AddCsv(
            builder,
            "run",
            "parsedProxyEndpointCount",
            Invariant(snapshot.ParsedProxyEndpointCount));
        AddCsv(
            builder,
            "run",
            "analyzedProxyEndpointCount",
            Invariant(snapshot.AnalyzedProxyEndpointCount));
        AddCsv(
            builder,
            "run",
            "successfulProxyEndpointCount",
            Invariant(snapshot.SuccessfulProxyEndpointCount));
        AddCsv(
            builder,
            "run",
            "proxyDistinctInterfaceCount",
            Invariant(snapshot.ProxyDistinctInterfaceCount));
        AddCsv(
            builder,
            "run",
            "directPresent",
            Invariant(snapshot.DirectPresent));
        AddCsv(
            builder,
            "run",
            "directFallback",
            Invariant(snapshot.DirectFallback));
        AddCsv(
            builder,
            "run",
            "expectedWlanIdentityAvailable",
            Invariant(snapshot.ExpectedWlanIdentityAvailable));
        AddCsv(
            builder,
            "run",
            "internalRouteReadPerformed",
            Invariant(snapshot.InternalRouteReadPerformed));
        AddCsv(
            builder,
            "run",
            "proxyRouteAnalysisPerformed",
            Invariant(snapshot.ProxyRouteAnalysisPerformed));
        AddCsv(
            builder,
            "run",
            "internalEvidencePartial",
            Invariant(snapshot.InternalEvidencePartial));
        AddCsv(
            builder,
            "run",
            "proxyEvidencePartial",
            Invariant(snapshot.ProxyEvidencePartial));
        AddCsv(
            builder,
            "run",
            "anyVirtualInterface",
            Invariant(snapshot.AnyVirtualInterface));
        AddCsv(
            builder,
            "run",
            "anyVpnOrTunnelInterface",
            Invariant(snapshot.AnyVpnOrTunnelInterface));
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
        AddCsv(builder, "finding", "code", finding.Code);
        AddCsv(builder, "finding", "severity", finding.Severity);
        AddCsv(builder, "finding", "title", finding.Title);
        AddCsv(builder, "finding", "evidence", finding.Evidence);
        AddCsv(
            builder,
            "finding",
            "interpretation",
            finding.Interpretation);
        AddCsv(
            builder,
            "finding",
            "limitation",
            finding.Limitation);
        AddCsv(builder, "finding", "nextStep", finding.NextStep);
    }

    private static void AppendRunSummaryHtml(
        StringBuilder builder,
        InternalProxyRouteComparisonRunSnapshot snapshot)
    {
        builder.Append(
            "<section class=\"card\"><h2>비교 실행 결과</h2><p><span class=\"badge ");
        Html(builder, StatusCss(snapshot.RunStatus));
        builder.Append("\">");
        Html(builder, snapshot.RunStatus);
        builder.Append("</span>");
        if (!string.IsNullOrWhiteSpace(snapshot.ComparisonStatus))
        {
            builder.Append(" <span class=\"badge ");
            Html(builder, StatusCss(snapshot.ComparisonStatus));
            builder.Append("\">");
            Html(builder, snapshot.ComparisonStatus);
            builder.Append("</span>");
        }

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
            "서로 다른 인터페이스",
            snapshot.ProxyDistinctInterfaceCount.ToString(
                CultureInfo.InvariantCulture));
        Metric(
            builder,
            "같은 로컬 인터페이스",
            snapshot.SameLocalInterface.HasValue
                ? snapshot.SameLocalInterface.Value.ToString()
                : "판정 안 함");
        Metric(
            builder,
            "DIRECT / fallback",
            $"{(snapshot.DirectPresent ? "있음" : "없음")} / {(snapshot.DirectFallback ? "있음" : "없음")}");
        Metric(
            builder,
            "내부 / 프록시 단계",
            $"{(snapshot.InternalRouteReadPerformed ? "수행" : "미수행")} / {(snapshot.ProxyRouteAnalysisPerformed ? "수행" : "미수행")}");
        Metric(
            builder,
            "VPN·터널 / 가상 NIC",
            $"{(snapshot.AnyVpnOrTunnelInterface ? "있음" : "확인 안 됨")} / {(snapshot.AnyVirtualInterface ? "있음" : "확인 안 됨")}");
        builder.Append("</div></section>");
    }

    private static void AppendInterfaceHtml(
        StringBuilder builder,
        string title,
        SafeLocalRouteInterfaceSnapshot? routeInterface)
    {
        builder.Append("<section class=\"card\"><h2>");
        Html(builder, title);
        builder.Append("</h2>");
        if (routeInterface is null)
        {
            builder.Append(
                "<p>단일 안전 인터페이스 근거를 확인하지 못했습니다.</p></section>");
            return;
        }

        builder.Append("<div class=\"scroll\"><table><tbody>");
        TableRow(
            builder,
            "인터페이스 지문",
            routeInterface.InterfaceFingerprint);
        TableRow(builder, "범주", routeInterface.Category);
        TableRow(
            builder,
            "가상 인터페이스",
            Invariant(routeInterface.IsVirtual));
        TableRow(
            builder,
            "VPN",
            Invariant(routeInterface.IsVpn));
        TableRow(builder, "Up", Invariant(routeInterface.IsUp));
        TableRow(
            builder,
            "기본 게이트웨이",
            Invariant(routeInterface.HasDefaultGateway));
        TableRow(
            builder,
            "현재 WLAN 일치",
            Invariant(routeInterface.MatchesExpectedWlan));
        builder.Append("</tbody></table></div></section>");
    }

    private static void AppendFindingHtml(
        StringBuilder builder,
        ReportFinding finding)
    {
        builder.Append(
            "<section class=\"card finding\"><h2>판정</h2><p><span class=\"badge ");
        Html(builder, SeverityCss(finding.Severity));
        builder.Append("\">");
        Html(builder, finding.Severity);
        builder.Append("</span> <span class=\"badge\">");
        Html(builder, finding.Code);
        builder.Append("</span></p><h3>");
        Html(builder, finding.Title);
        builder.Append("</h3><p><strong>근거:</strong> ");
        Html(builder, finding.Evidence);
        builder.Append("</p><p><strong>해석:</strong> ");
        Html(builder, finding.Interpretation);
        builder.Append("</p><p><strong>다음 확인:</strong> ");
        Html(builder, finding.NextStep);
        builder.Append("</p><p class=\"small\"><strong>한계:</strong> ");
        Html(builder, finding.Limitation);
        builder.Append("</p></section>");
    }

    private static string StatusCss(string? value) =>
        value switch
        {
            "Completed" or "Ready" => "ready",
            "DirectPathSelected" or "Incomplete" or "Canceled" =>
                "warn",
            "Diverged" or "Ambiguous" => "warn",
            _ => "bad"
        };

    private static string SeverityCss(string? value) =>
        value switch
        {
            "Information" => "ready",
            "Warning" => "warn",
            _ => "bad"
        };

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

    private static string SanitizeApplicationVersion(string value)
    {
        string sanitized = SensitiveDataRedactor.RedactText(value)
            ?? string.Empty;
        sanitized = new string(sanitized
            .Where(character => !char.IsControl(character))
            .ToArray())
            .Trim();
        if (sanitized.Length == 0)
        {
            return "확인 불가";
        }

        return sanitized.Length <= MaximumApplicationVersionLength
            ? sanitized
            : sanitized[..MaximumApplicationVersionLength];
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
            _ => Convert.ToString(value, CultureInfo.InvariantCulture)
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
            "사용 가능한 내부 DIRECT–프록시 경로 비교 보고서 파일 이름을 만들지 못했습니다.");
    }

    private static void WriteAtomic(
        string destination,
        string content,
        Encoding encoding)
    {
        string directory = Path.GetDirectoryName(destination)
            ?? throw new InvalidOperationException(
                "보고서 출력 디렉터리를 확인할 수 없습니다.");
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
