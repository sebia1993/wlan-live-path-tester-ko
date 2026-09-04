using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WlanLivePathTester.Core.Routing;

namespace WlanLivePathTester.Core.Reporting;

public sealed record RouteEvidenceReportDocument(
    string SchemaVersion,
    DateTimeOffset GeneratedAt,
    string ApplicationName,
    string ApplicationVersion,
    bool SensitiveValuesIncluded,
    string DataHandlingStatement,
    IReadOnlyList<RouteEvidenceReportEntry> Results,
    IReadOnlyList<string> Limitations);

public sealed record RouteEvidenceReportEntry(
    DateTimeOffset CapturedAt,
    string TargetLabel,
    string Purpose,
    bool DnsWasUsed,
    int ResolvedAddressCount,
    string Status,
    RouteEvidenceReportInterface? SelectedInterface,
    IReadOnlyList<RouteEvidenceReportAddress> AddressEvidence,
    IReadOnlyList<string> Warnings,
    string Message);

public sealed record RouteEvidenceReportInterface(
    string IdFingerprint,
    string Category,
    string NativeInterfaceType,
    string OperationalState,
    bool HasDefaultGateway,
    bool IsVirtual,
    bool IsVpn);

public sealed record RouteEvidenceReportAddress(
    string AddressFamily,
    string Status,
    RouteEvidenceReportInterface? Interface,
    uint? NativeErrorCode,
    string Message);

public sealed record RouteEvidenceReportExportResult(
    string OutputDirectory,
    string JsonPath,
    string CsvPath,
    string HtmlPath,
    string Sha256Path,
    IReadOnlyDictionary<string, string> Sha256);

public static class RouteEvidenceReportWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static RouteEvidenceReportDocument CreateDocument(
        IReadOnlyList<DestinationRouteEvidence> results,
        string applicationVersion,
        DateTimeOffset? generatedAt = null)
    {
        ArgumentNullException.ThrowIfNull(results);
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationVersion);

        return new RouteEvidenceReportDocument(
            SchemaVersion: "1.0",
            GeneratedAt: generatedAt ?? DateTimeOffset.UtcNow,
            ApplicationName: "WLAN Live Path Tester KO",
            ApplicationVersion: applicationVersion,
            SensitiveValuesIncluded: false,
            DataHandlingStatement: "라우팅 근거 보고서는 현재 PC에서 생성되며 자동 업로드, 텔레메트리 또는 온라인 분석을 수행하지 않습니다.",
            Results: results
                .Select(Map)
                .ToArray(),
            Limitations:
            [
                "DNS로 확인한 목적지 주소, 게이트웨이 주소, DNS 서버 주소, MAC 주소와 전체 인터페이스 GUID는 보고서에 포함하지 않습니다.",
                "GetBestInterfaceEx 결과는 현재 Windows 라우팅 테이블 기반의 최적 인터페이스 근거이며 실제 TCP 연결 성공을 보장하지 않습니다.",
                "회사 프록시 사용 시 외부 사이트 주소의 직접 라우팅 근거는 실제 HTTP 연결 경로가 아닐 수 있습니다.",
                "IPv4와 IPv6 또는 복수 주소가 서로 다른 인터페이스를 선택하면 실제 연결 주소가 정해지기 전에는 단일 경로로 확정할 수 없습니다.",
                "Windows Filtering Platform, VPN split tunnel, 투명 프록시와 보안 에이전트 정책은 이 보고서만으로 완전히 판정할 수 없습니다."
            ]);
    }

    public static RouteEvidenceReportExportResult WriteAll(
        RouteEvidenceReportDocument report,
        string outputDirectory,
        string filePrefix = "WlanRouteEvidence")
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);

        string fullDirectory = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(fullDirectory);
        string safePrefix = SensitiveDataRedactor.SafeFileComponent(
            filePrefix,
            "WlanRouteEvidence");
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

        return new RouteEvidenceReportExportResult(
            OutputDirectory: fullDirectory,
            JsonPath: jsonPath,
            CsvPath: csvPath,
            HtmlPath: htmlPath,
            Sha256Path: sha256Path,
            Sha256: hashes);
    }

    public static string RenderJson(RouteEvidenceReportDocument report)
    {
        ArgumentNullException.ThrowIfNull(report);
        return JsonSerializer.Serialize(report, JsonOptions)
            + Environment.NewLine;
    }

    public static string RenderCsv(RouteEvidenceReportDocument report)
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

        for (int resultIndex = 0;
             resultIndex < report.Results.Count;
             resultIndex++)
        {
            RouteEvidenceReportEntry result = report.Results[resultIndex];
            string section = $"route.{resultIndex + 1}";
            AddCsv(builder, section, "capturedAt", Iso(result.CapturedAt));
            AddCsv(builder, section, "targetLabel", result.TargetLabel);
            AddCsv(builder, section, "purpose", result.Purpose);
            AddCsv(builder, section, "dnsWasUsed", Invariant(result.DnsWasUsed));
            AddCsv(
                builder,
                section,
                "resolvedAddressCount",
                Invariant(result.ResolvedAddressCount));
            AddCsv(builder, section, "status", result.Status);
            AddCsv(builder, section, "message", result.Message);

            if (result.SelectedInterface is RouteEvidenceReportInterface selected)
            {
                AddInterfaceCsv(
                    builder,
                    section + ".selectedInterface",
                    selected);
            }

            for (int addressIndex = 0;
                 addressIndex < result.AddressEvidence.Count;
                 addressIndex++)
            {
                RouteEvidenceReportAddress address =
                    result.AddressEvidence[addressIndex];
                string addressSection =
                    $"{section}.address.{addressIndex + 1}";
                AddCsv(
                    builder,
                    addressSection,
                    "addressFamily",
                    address.AddressFamily);
                AddCsv(builder, addressSection, "status", address.Status);
                AddCsv(
                    builder,
                    addressSection,
                    "nativeErrorCode",
                    Invariant(address.NativeErrorCode));
                AddCsv(builder, addressSection, "message", address.Message);
                if (address.Interface is RouteEvidenceReportInterface routeInterface)
                {
                    AddInterfaceCsv(
                        builder,
                        addressSection + ".interface",
                        routeInterface);
                }
            }

            for (int warningIndex = 0;
                 warningIndex < result.Warnings.Count;
                 warningIndex++)
            {
                AddCsv(
                    builder,
                    section + ".warning",
                    (warningIndex + 1).ToString(
                        CultureInfo.InvariantCulture),
                    result.Warnings[warningIndex]);
            }
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

    public static string RenderHtml(RouteEvidenceReportDocument report)
    {
        ArgumentNullException.ThrowIfNull(report);

        StringBuilder builder = new(capacity: 28 * 1024);
        builder.Append("<!doctype html><html lang=\"ko\"><head><meta charset=\"utf-8\">");
        builder.Append("<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">");
        builder.Append("<meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'none'; style-src 'unsafe-inline'; base-uri 'none'; form-action 'none'; frame-ancestors 'none'\">");
        builder.Append("<title>목적지별 Windows 라우팅 근거 보고서</title><style>");
        builder.Append("body{margin:0;background:#f4f6f8;color:#17202a;font:14px/1.55 system-ui,-apple-system,'Segoe UI',sans-serif}main{max-width:1180px;margin:auto;padding:28px}h1{font-size:28px;margin:0}h2{font-size:19px;margin:0 0 12px}h3{font-size:16px;margin:18px 0 8px}.sub,.small{color:#566573}.card{background:#fff;border:1px solid #d8dde3;border-radius:12px;padding:18px;margin-top:16px;box-shadow:0 1px 2px rgba(0,0,0,.03)}.grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(190px,1fr));gap:10px}.metric{background:#f8fafb;border-radius:8px;padding:12px}.metric strong{display:block;font-size:18px}.badge{display:inline-block;border-radius:999px;padding:3px 9px;font-size:12px;background:#eaf2f8;color:#1b4f72}.success{background:#e8f6f3;color:#0e6251}.warning{background:#fff3cd;color:#7d6608}.failure{background:#fdecea;color:#922b21}table{width:100%;border-collapse:collapse}th,td{padding:8px;border-bottom:1px solid #e8ebed;text-align:left;vertical-align:top}.scroll{overflow:auto}.privacy{background:#fff8e7;border-color:#e8ce8a}@media(max-width:640px){main{padding:16px}.grid{display:block}.metric{margin-top:8px}}@media print{body{background:#fff}.card{box-shadow:none;break-inside:avoid}main{max-width:none;padding:0}}</style></head><body><main>");
        builder.Append("<header><h1>목적지별 Windows 라우팅 근거 보고서</h1><div class=\"sub\">");
        Html(
            builder,
            report.GeneratedAt.ToLocalTime()
                .ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture));
        builder.Append(" · 스키마 ");
        Html(builder, report.SchemaVersion);
        builder.Append("</div></header><section class=\"card privacy\"><h2>데이터 처리</h2><p>");
        Html(builder, report.DataHandlingStatement);
        builder.Append("</p><p class=\"small\">해석된 IP·게이트웨이·DNS 주소, MAC 주소, 인터페이스 이름·설명과 전체 GUID는 포함하지 않습니다. 이 HTML은 외부 리소스와 스크립트를 사용하지 않습니다.</p></section>");

        if (report.Results.Count == 0)
        {
            builder.Append("<section class=\"card\"><h2>결과</h2><p>저장된 라우팅 근거가 없습니다.</p></section>");
        }

        for (int index = 0; index < report.Results.Count; index++)
        {
            AppendResultHtml(builder, report.Results[index], index + 1);
        }

        builder.Append("<section class=\"card\"><h2>판단 한계</h2><ul>");
        foreach (string limitation in report.Limitations)
        {
            builder.Append("<li>");
            Html(builder, limitation);
            builder.Append("</li>");
        }
        builder.Append("</ul></section><footer class=\"small\">현재 PC에서 생성한 로컬 보고서입니다.</footer></main></body></html>");
        return builder.ToString();
    }

    private static RouteEvidenceReportEntry Map(
        DestinationRouteEvidence result) =>
        new(
            CapturedAt: result.CapturedAt,
            TargetLabel: Redact(result.TargetLabel, "라우팅 확인 대상"),
            Purpose: result.Purpose.ToString(),
            DnsWasUsed: result.DnsWasUsed,
            ResolvedAddressCount: result.ResolvedAddressCount,
            Status: result.Status.ToString(),
            SelectedInterface: MapInterface(result.SelectedInterface),
            AddressEvidence: result.AddressEvidence
                .Select(item => new RouteEvidenceReportAddress(
                    AddressFamily: item.AddressFamily.ToString(),
                    Status: item.Status.ToString(),
                    Interface: MapInterface(item.Interface),
                    NativeErrorCode: item.NativeErrorCode,
                    Message: Redact(item.Message, string.Empty)))
                .ToArray(),
            Warnings: result.Warnings
                .Select(warning => Redact(warning, string.Empty))
                .Where(warning => !string.IsNullOrWhiteSpace(warning))
                .ToArray(),
            Message: Redact(result.Message, string.Empty));

    private static RouteEvidenceReportInterface? MapInterface(
        RouteInterfaceDescriptor? descriptor)
    {
        if (descriptor is null)
        {
            return null;
        }

        return new RouteEvidenceReportInterface(
            IdFingerprint: descriptor.IdentityFingerprint,
            Category: descriptor.Category.ToString(),
            NativeInterfaceType: Redact(
                descriptor.NativeInterfaceType,
                "Unknown"),
            OperationalState: descriptor.OperationalState.ToString(),
            HasDefaultGateway: descriptor.HasDefaultGateway,
            IsVirtual: descriptor.IsVirtual,
            IsVpn: descriptor.IsVpn);
    }

    private static string Redact(string? value, string fallback) =>
        SensitiveDataRedactor.RedactText(value) is string redacted
        && !string.IsNullOrWhiteSpace(redacted)
            ? redacted
            : fallback;

    private static void AddInterfaceCsv(
        StringBuilder builder,
        string section,
        RouteEvidenceReportInterface routeInterface)
    {
        AddCsv(builder, section, "idFingerprint", routeInterface.IdFingerprint);
        AddCsv(builder, section, "category", routeInterface.Category);
        AddCsv(
            builder,
            section,
            "nativeInterfaceType",
            routeInterface.NativeInterfaceType);
        AddCsv(
            builder,
            section,
            "operationalState",
            routeInterface.OperationalState);
        AddCsv(
            builder,
            section,
            "hasDefaultGateway",
            Invariant(routeInterface.HasDefaultGateway));
        AddCsv(builder, section, "isVirtual", Invariant(routeInterface.IsVirtual));
        AddCsv(builder, section, "isVpn", Invariant(routeInterface.IsVpn));
    }

    private static void AppendResultHtml(
        StringBuilder builder,
        RouteEvidenceReportEntry result,
        int index)
    {
        builder.Append("<section class=\"card\"><h2>");
        Html(builder, $"결과 {index}: {result.TargetLabel}");
        builder.Append("</h2><p><span class=\"badge ");
        Html(builder, StatusCss(result.Status));
        builder.Append("\">");
        Html(builder, result.Status);
        builder.Append("</span></p><div class=\"grid\">");
        Metric(builder, "목적", result.Purpose);
        Metric(builder, "DNS 사용", result.DnsWasUsed ? "예" : "아니요");
        Metric(
            builder,
            "확인 주소 수",
            result.ResolvedAddressCount.ToString(CultureInfo.InvariantCulture));
        Metric(
            builder,
            "선택 인터페이스",
            result.SelectedInterface?.Category ?? "확정하지 않음");
        Metric(
            builder,
            "ID 지문",
            result.SelectedInterface?.IdFingerprint ?? "없음");
        Metric(
            builder,
            "VPN·가상",
            result.SelectedInterface is null
                ? "확인 불가"
                : result.SelectedInterface.IsVpn
                    ? "VPN/터널"
                    : result.SelectedInterface.IsVirtual
                        ? "가상"
                        : "아니요");
        builder.Append("</div><p>");
        Html(builder, result.Message);
        builder.Append("</p>");

        if (result.SelectedInterface is RouteEvidenceReportInterface selected)
        {
            builder.Append("<p class=\"small\">선택 인터페이스: ");
            Html(
                builder,
                $"{selected.Category} / {selected.NativeInterfaceType} / {selected.OperationalState} / 기본 게이트웨이 {(selected.HasDefaultGateway ? "있음" : "확인되지 않음")}");
            builder.Append("</p>");
        }

        if (result.AddressEvidence.Count > 0)
        {
            builder.Append("<h3>주소 계열별 근거</h3><div class=\"scroll\"><table><thead><tr><th>주소 계열</th><th>상태</th><th>인터페이스 범주</th><th>ID 지문</th><th>Native 오류</th><th>설명</th></tr></thead><tbody>");
            foreach (RouteEvidenceReportAddress address in result.AddressEvidence)
            {
                builder.Append("<tr><td>");
                Html(builder, address.AddressFamily);
                builder.Append("</td><td>");
                Html(builder, address.Status);
                builder.Append("</td><td>");
                Html(builder, address.Interface?.Category ?? "확인 안 됨");
                builder.Append("</td><td>");
                Html(builder, address.Interface?.IdFingerprint ?? "없음");
                builder.Append("</td><td>");
                Html(
                    builder,
                    address.NativeErrorCode?.ToString(
                        CultureInfo.InvariantCulture) ?? "없음");
                builder.Append("</td><td>");
                Html(builder, address.Message);
                builder.Append("</td></tr>");
            }
            builder.Append("</tbody></table></div>");
        }

        if (result.Warnings.Count > 0)
        {
            builder.Append("<h3>주의</h3><ul>");
            foreach (string warning in result.Warnings)
            {
                builder.Append("<li>");
                Html(builder, warning);
                builder.Append("</li>");
            }
            builder.Append("</ul>");
        }

        builder.Append("</section>");
    }

    private static string StatusCss(string status) =>
        status.Equals("Success", StringComparison.OrdinalIgnoreCase)
            ? "success"
            : status.Equals("PartialSuccess", StringComparison.OrdinalIgnoreCase)
                || status.Equals(
                    "MultipleInterfaces",
                    StringComparison.OrdinalIgnoreCase)
                ? "warning"
                : "failure";

    private static void Metric(
        StringBuilder builder,
        string label,
        string value)
    {
        builder.Append("<div class=\"metric\"><span class=\"small\">");
        Html(builder, label);
        builder.Append("</span><strong>");
        Html(builder, value);
        builder.Append("</strong></div>");
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
            "사용 가능한 라우팅 근거 보고서 파일 이름을 만들지 못했습니다.");
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
