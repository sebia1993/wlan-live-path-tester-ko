using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WlanLivePathTester.Core.Routing;

namespace WlanLivePathTester.Core.Reporting;

public sealed record RoutePathComparisonReportDocument(
    string SchemaVersion,
    DateTimeOffset GeneratedAt,
    string ApplicationName,
    string ApplicationVersion,
    bool SensitiveValuesIncluded,
    string DataHandlingStatement,
    string Status,
    string Message,
    RoutePathComparisonReportPoint? InternalDirect,
    RoutePathComparisonReportPoint? ProxyEndpoint,
    RoutePathComparisonReportPoint? ExternalReference,
    IReadOnlyList<RoutePathComparisonReportFinding> Findings,
    IReadOnlyList<string> Limitations);

public sealed record RoutePathComparisonReportPoint(
    string Purpose,
    DateTimeOffset CapturedAt,
    string RouteStatus,
    string WlanCorrelationStatus,
    string? InterfaceFingerprint,
    string? InterfaceCategory,
    bool? IsVpn,
    bool? IsVirtual,
    int WarningCount);

public sealed record RoutePathComparisonReportFinding(
    string Code,
    string Severity,
    string Title,
    string Evidence,
    string Interpretation,
    string NextStep);

public sealed record RoutePathComparisonReportExportResult(
    string OutputDirectory,
    string JsonPath,
    string CsvPath,
    string HtmlPath,
    string Sha256Path,
    IReadOnlyDictionary<string, string> Sha256);

public static class RoutePathComparisonReportWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static RoutePathComparisonReportDocument CreateDocument(
        RoutePathComparisonResult comparison,
        string applicationVersion,
        DateTimeOffset? generatedAt = null)
    {
        ArgumentNullException.ThrowIfNull(comparison);
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationVersion);

        return new RoutePathComparisonReportDocument(
            SchemaVersion: "1.0",
            GeneratedAt: generatedAt ?? DateTimeOffset.UtcNow,
            ApplicationName: "WLAN Live Path Tester KO",
            ApplicationVersion: applicationVersion,
            SensitiveValuesIncluded: false,
            DataHandlingStatement: "내부·프록시 경로 비교 보고서는 현재 PC에서 생성되며 자동 업로드, 텔레메트리 또는 온라인 분석을 수행하지 않습니다.",
            Status: comparison.Status.ToString(),
            Message: Redact(comparison.Message),
            InternalDirect: MapPoint(comparison.InternalDirect),
            ProxyEndpoint: MapPoint(comparison.ProxyEndpoint),
            ExternalReference: MapPoint(comparison.ExternalReference),
            Findings: comparison.Findings
                .Select(MapFinding)
                .ToArray(),
            Limitations:
            [
                "비교는 현재 앱 메모리 이력에서 목적별 가장 최근 라우팅 결과를 사용합니다.",
                "목적지 IP, 게이트웨이·DNS 서버·MAC 주소, 인터페이스 이름·설명과 전체 GUID는 보고서에 포함하지 않습니다.",
                "인터페이스 ID는 SHA-256 앞 10자리 지문으로만 기록합니다.",
                "회사 프록시 환경에서 외부 사이트 직접 경로는 실제 외부 HTTP 연결 경로를 대신하지 못합니다.",
                "Windows 최적 인터페이스 근거는 실제 TCP 연결 성공, VPN split tunnel 또는 프록시 이후 구간을 증명하지 않습니다."
            ]);
    }

    public static RoutePathComparisonReportExportResult WriteAll(
        RoutePathComparisonReportDocument report,
        string outputDirectory,
        string filePrefix = "WlanRouteComparison")
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);

        string fullDirectory = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(fullDirectory);
        string safePrefix = SensitiveDataRedactor.SafeFileComponent(
            filePrefix,
            "WlanRouteComparison");
        string timestamp = report.GeneratedAt.ToLocalTime()
            .ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        string baseName = GetAvailableBaseName(
            fullDirectory,
            $"{safePrefix}_{timestamp}");

        string jsonPath = Path.Combine(fullDirectory, baseName + ".json");
        string csvPath = Path.Combine(fullDirectory, baseName + ".csv");
        string htmlPath = Path.Combine(fullDirectory, baseName + ".html");
        string sha256Path = Path.Combine(
            fullDirectory,
            baseName + "_SHA256SUMS.txt");

        WriteAtomic(jsonPath, RenderJson(report), new UTF8Encoding(false));
        WriteAtomic(csvPath, RenderCsv(report), new UTF8Encoding(true));
        WriteAtomic(htmlPath, RenderHtml(report), new UTF8Encoding(false));

        Dictionary<string, string> hashes = new(StringComparer.OrdinalIgnoreCase)
        {
            [Path.GetFileName(jsonPath)] = ComputeSha256(jsonPath),
            [Path.GetFileName(csvPath)] = ComputeSha256(csvPath),
            [Path.GetFileName(htmlPath)] = ComputeSha256(htmlPath)
        };
        string checksumText = string.Join(
            Environment.NewLine,
            hashes.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .Select(pair => $"{pair.Value}  {pair.Key}"))
            + Environment.NewLine;
        WriteAtomic(sha256Path, checksumText, new UTF8Encoding(false));

        return new RoutePathComparisonReportExportResult(
            OutputDirectory: fullDirectory,
            JsonPath: jsonPath,
            CsvPath: csvPath,
            HtmlPath: htmlPath,
            Sha256Path: sha256Path,
            Sha256: hashes);
    }

    public static string RenderJson(
        RoutePathComparisonReportDocument report)
    {
        ArgumentNullException.ThrowIfNull(report);
        return JsonSerializer.Serialize(report, JsonOptions)
            + Environment.NewLine;
    }

    public static string RenderCsv(
        RoutePathComparisonReportDocument report)
    {
        ArgumentNullException.ThrowIfNull(report);

        StringBuilder builder = new();
        builder.AppendLine("section,key,value");
        AddCsv(builder, "metadata", "schemaVersion", report.SchemaVersion);
        AddCsv(builder, "metadata", "generatedAt", Iso(report.GeneratedAt));
        AddCsv(builder, "metadata", "applicationName", report.ApplicationName);
        AddCsv(builder, "metadata", "applicationVersion", report.ApplicationVersion);
        AddCsv(
            builder,
            "metadata",
            "sensitiveValuesIncluded",
            Invariant(report.SensitiveValuesIncluded));
        AddCsv(builder, "metadata", "dataHandling", report.DataHandlingStatement);
        AddCsv(builder, "comparison", "status", report.Status);
        AddCsv(builder, "comparison", "message", report.Message);

        AddPointCsv(builder, "comparison.internalDirect", report.InternalDirect);
        AddPointCsv(builder, "comparison.proxyEndpoint", report.ProxyEndpoint);
        AddPointCsv(builder, "comparison.externalReference", report.ExternalReference);

        for (int index = 0; index < report.Findings.Count; index++)
        {
            RoutePathComparisonReportFinding finding = report.Findings[index];
            string section = $"finding.{index + 1}";
            AddCsv(builder, section, "code", finding.Code);
            AddCsv(builder, section, "severity", finding.Severity);
            AddCsv(builder, section, "title", finding.Title);
            AddCsv(builder, section, "evidence", finding.Evidence);
            AddCsv(builder, section, "interpretation", finding.Interpretation);
            AddCsv(builder, section, "nextStep", finding.NextStep);
        }

        for (int index = 0; index < report.Limitations.Count; index++)
        {
            AddCsv(
                builder,
                "limitation",
                (index + 1).ToString(CultureInfo.InvariantCulture),
                report.Limitations[index]);
        }

        return builder.ToString();
    }

    public static string RenderHtml(
        RoutePathComparisonReportDocument report)
    {
        ArgumentNullException.ThrowIfNull(report);

        StringBuilder builder = new(capacity: 24 * 1024);
        builder.Append("<!doctype html><html lang=\"ko\"><head><meta charset=\"utf-8\">");
        builder.Append("<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">");
        builder.Append("<meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'none'; style-src 'unsafe-inline'; base-uri 'none'; form-action 'none'; frame-ancestors 'none'\">");
        builder.Append("<title>내부·프록시 로컬 경로 비교 보고서</title><style>");
        builder.Append("body{margin:0;background:#f4f6f8;color:#17202a;font:14px/1.55 system-ui,-apple-system,'Segoe UI',sans-serif}main{max-width:1180px;margin:auto;padding:28px}h1{font-size:28px;margin:0}h2{font-size:19px;margin:0 0 12px}h3{font-size:16px;margin:0 0 8px}.sub,.small{color:#566573}.card{background:#fff;border:1px solid #d8dde3;border-radius:12px;padding:18px;margin-top:16px;box-shadow:0 1px 2px rgba(0,0,0,.03)}.grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(220px,1fr));gap:10px}.point{background:#f8fafb;border-radius:8px;padding:12px}.point strong{display:block;font-size:16px}.badge{display:inline-block;border-radius:999px;padding:3px 9px;font-size:12px;background:#eaf2f8;color:#1b4f72}.ready{background:#e8f6f3;color:#0e6251}.incomplete,.ambiguous{background:#fff3cd;color:#7d6608}.diverged{background:#fdecea;color:#922b21}.finding{border-left:4px solid #5dade2;padding:12px;margin-top:12px;background:#fafcfd}.finding.warning{border-color:#f5b041}.privacy{background:#fff8e7;border-color:#e8ce8a}@media(max-width:640px){main{padding:16px}.grid{display:block}.point{margin-top:8px}}@media print{body{background:#fff}.card{box-shadow:none;break-inside:avoid}main{max-width:none;padding:0}}</style></head><body><main>");
        builder.Append("<header><h1>내부·프록시 로컬 경로 비교 보고서</h1><div class=\"sub\">");
        Html(
            builder,
            report.GeneratedAt.ToLocalTime()
                .ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture));
        builder.Append(" · 스키마 ");
        Html(builder, report.SchemaVersion);
        builder.Append("</div></header><section class=\"card privacy\"><h2>데이터 처리</h2><p>");
        Html(builder, report.DataHandlingStatement);
        builder.Append("</p><p class=\"small\">목적지 IP, 인터페이스 이름·설명, 게이트웨이·DNS·MAC 주소와 전체 GUID는 포함하지 않습니다. 이 HTML은 외부 리소스와 스크립트를 사용하지 않습니다.</p></section>");

        builder.Append("<section class=\"card\"><h2>비교 결과</h2><p><span class=\"badge ");
        Html(builder, StatusCss(report.Status));
        builder.Append("\">");
        Html(builder, report.Status);
        builder.Append("</span></p><p>");
        Html(builder, report.Message);
        builder.Append("</p><div class=\"grid\">");
        AppendPointHtml(builder, "내부 DIRECT", report.InternalDirect);
        AppendPointHtml(builder, "프록시 엔드포인트", report.ProxyEndpoint);
        AppendPointHtml(builder, "외부 사이트 참고", report.ExternalReference);
        builder.Append("</div></section>");

        builder.Append("<section class=\"card\"><h2>판정</h2>");
        if (report.Findings.Count == 0)
        {
            builder.Append("<p>별도 판정이 없습니다.</p>");
        }
        else
        {
            foreach (RoutePathComparisonReportFinding finding in report.Findings)
            {
                string severityClass = finding.Severity.Equals(
                    "Warning",
                    StringComparison.OrdinalIgnoreCase)
                    ? "warning"
                    : "information";
                builder.Append("<article class=\"finding ");
                Html(builder, severityClass);
                builder.Append("\"><h3>");
                Html(builder, finding.Title);
                builder.Append("</h3><p class=\"small\">");
                Html(builder, finding.Code);
                builder.Append(" · ");
                Html(builder, finding.Severity);
                builder.Append("</p><p><strong>근거:</strong> ");
                Html(builder, finding.Evidence);
                builder.Append("</p><p><strong>해석:</strong> ");
                Html(builder, finding.Interpretation);
                builder.Append("</p><p><strong>다음 확인:</strong> ");
                Html(builder, finding.NextStep);
                builder.Append("</p></article>");
            }
        }
        builder.Append("</section>");

        builder.Append("<section class=\"card\"><h2>판단 한계</h2><ul>");
        foreach (string limitation in report.Limitations)
        {
            builder.Append("<li>");
            Html(builder, limitation);
            builder.Append("</li>");
        }
        builder.Append("</ul></section><footer class=\"small\">현재 PC의 메모리 이력으로 생성한 로컬 보고서입니다.</footer></main></body></html>");
        return builder.ToString();
    }

    private static RoutePathComparisonReportPoint? MapPoint(
        RoutePathComparisonPoint? point)
    {
        if (point is null)
        {
            return null;
        }

        return new RoutePathComparisonReportPoint(
            Purpose: point.Purpose.ToString(),
            CapturedAt: point.CapturedAt,
            RouteStatus: point.RouteStatus.ToString(),
            WlanCorrelationStatus: point.WlanCorrelationStatus.ToString(),
            InterfaceFingerprint: NormalizeFingerprint(
                point.InterfaceFingerprint),
            InterfaceCategory: RedactNullable(point.InterfaceCategory),
            IsVpn: point.IsVpn,
            IsVirtual: point.IsVirtual,
            WarningCount: Math.Max(0, point.WarningCount));
    }

    private static RoutePathComparisonReportFinding MapFinding(
        RoutePathComparisonFinding finding) =>
        new(
            Code: Redact(finding.Code),
            Severity: finding.Severity.ToString(),
            Title: Redact(finding.Title),
            Evidence: Redact(finding.Evidence),
            Interpretation: Redact(finding.Interpretation),
            NextStep: Redact(finding.NextStep));

    private static string? NormalizeFingerprint(string? value)
    {
        string normalized = (value ?? string.Empty).Trim();
        if (normalized.Length != RouteInterfaceFingerprint.DisplayLength
            || normalized.Any(character => !Uri.IsHexDigit(character)))
        {
            return null;
        }

        return normalized.ToLowerInvariant();
    }

    private static string Redact(string? value) =>
        SensitiveDataRedactor.RedactText(value) ?? string.Empty;

    private static string? RedactNullable(string? value) =>
        SensitiveDataRedactor.RedactText(value) is string redacted
        && !string.IsNullOrWhiteSpace(redacted)
            ? redacted
            : null;

    private static void AddPointCsv(
        StringBuilder builder,
        string section,
        RoutePathComparisonReportPoint? point)
    {
        if (point is null)
        {
            AddCsv(builder, section, "available", "false");
            return;
        }

        AddCsv(builder, section, "available", "true");
        AddCsv(builder, section, "purpose", point.Purpose);
        AddCsv(builder, section, "capturedAt", Iso(point.CapturedAt));
        AddCsv(builder, section, "routeStatus", point.RouteStatus);
        AddCsv(
            builder,
            section,
            "wlanCorrelationStatus",
            point.WlanCorrelationStatus);
        AddCsv(
            builder,
            section,
            "interfaceFingerprint",
            point.InterfaceFingerprint ?? string.Empty);
        AddCsv(
            builder,
            section,
            "interfaceCategory",
            point.InterfaceCategory ?? string.Empty);
        AddCsv(builder, section, "isVpn", Invariant(point.IsVpn));
        AddCsv(builder, section, "isVirtual", Invariant(point.IsVirtual));
        AddCsv(builder, section, "warningCount", Invariant(point.WarningCount));
    }

    private static void AppendPointHtml(
        StringBuilder builder,
        string label,
        RoutePathComparisonReportPoint? point)
    {
        builder.Append("<div class=\"point\"><strong>");
        Html(builder, label);
        builder.Append("</strong>");
        if (point is null)
        {
            builder.Append("<p>근거 없음</p></div>");
            return;
        }

        builder.Append("<p>");
        Html(
            builder,
            $"Route {point.RouteStatus} · WLAN {point.WlanCorrelationStatus}");
        builder.Append("</p><p class=\"small\">Category ");
        Html(builder, point.InterfaceCategory ?? "없음");
        builder.Append(" · ID ");
        Html(builder, point.InterfaceFingerprint ?? "없음");
        builder.Append(" · VPN ");
        Html(builder, Flag(point.IsVpn));
        builder.Append(" · Virtual ");
        Html(builder, Flag(point.IsVirtual));
        builder.Append(" · Warnings ");
        Html(builder, point.WarningCount.ToString(CultureInfo.InvariantCulture));
        builder.Append("</p></div>");
    }

    private static string Flag(bool? value) =>
        value switch
        {
            true => "Y",
            false => "N",
            null => "?"
        };

    private static string StatusCss(string status) =>
        status.ToLowerInvariant() switch
        {
            "ready" => "ready",
            "incomplete" => "incomplete",
            "ambiguous" => "ambiguous",
            "diverged" => "diverged",
            _ => "incomplete"
        };

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
            IFormattable formattable =>
                formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture)
                ?? string.Empty
        };

    private static string Iso(DateTimeOffset value) =>
        value.ToString("O", CultureInfo.InvariantCulture);

    private static void Html(StringBuilder builder, string? value) =>
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
            if (!File.Exists(Path.Combine(directory, candidate + ".json"))
                && !File.Exists(Path.Combine(directory, candidate + ".csv"))
                && !File.Exists(Path.Combine(directory, candidate + ".html"))
                && !File.Exists(Path.Combine(
                    directory,
                    candidate + "_SHA256SUMS.txt")))
            {
                return candidate;
            }
        }

        throw new IOException(
            "사용 가능한 경로 비교 보고서 파일 이름을 만들지 못했습니다.");
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
