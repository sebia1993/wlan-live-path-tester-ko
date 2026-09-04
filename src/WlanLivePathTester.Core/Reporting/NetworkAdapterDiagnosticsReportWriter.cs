using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WlanLivePathTester.Core.Adapters;

namespace WlanLivePathTester.Core.Reporting;

public sealed record NetworkAdapterDiagnosticsReportDocument(
    string SchemaVersion,
    DateTimeOffset GeneratedAt,
    string ApplicationName,
    string ApplicationVersion,
    bool SensitiveValuesIncluded,
    string DataHandlingStatement,
    string SelectionStatus,
    string SelectionMessage,
    string? SelectedAdapterFingerprint,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<NetworkAdapterDiagnosticsReportEntry> Adapters,
    IReadOnlyList<string> Limitations);

public sealed record NetworkAdapterDiagnosticsReportEntry(
    int Order,
    string IdFingerprint,
    string DisplayName,
    string Role,
    string OperationalStatus,
    string InterfaceType,
    double? LinkSpeedMbps,
    bool HasUnicastAddress,
    bool HasDefaultGateway,
    bool IsNativeWlanConnected,
    int? IPv4InterfaceIndex,
    int? IPv6InterfaceIndex,
    int? WirelessSelectionScore,
    IReadOnlyList<string> ClassificationReasons);

public sealed record NetworkAdapterDiagnosticsReportExportResult(
    string OutputDirectory,
    string JsonPath,
    string CsvPath,
    string HtmlPath,
    string Sha256Path,
    IReadOnlyDictionary<string, string> Sha256);

public static class NetworkAdapterDiagnosticsReportWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static NetworkAdapterDiagnosticsReportDocument CreateDocument(
        WirelessAdapterSelectionResult selection,
        string applicationVersion,
        DateTimeOffset? generatedAt = null)
    {
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationVersion);

        return new NetworkAdapterDiagnosticsReportDocument(
            SchemaVersion: "1.0",
            GeneratedAt: generatedAt ?? DateTimeOffset.UtcNow,
            ApplicationName: "WLAN Live Path Tester KO",
            ApplicationVersion: applicationVersion,
            SensitiveValuesIncluded: false,
            DataHandlingStatement: "어댑터 진단 보고서는 현재 PC에서 생성되며 자동 업로드, 텔레메트리 또는 온라인 분석을 수행하지 않습니다.",
            SelectionStatus: selection.Status.ToString(),
            SelectionMessage: SensitiveDataRedactor.RedactText(
                    selection.Message)
                ?? string.Empty,
            SelectedAdapterFingerprint: selection.Selected is null
                ? null
                : Fingerprint(selection.Selected.Candidate.Id),
            Warnings: selection.Warnings
                .Select(warning => SensitiveDataRedactor.RedactText(warning)
                    ?? string.Empty)
                .ToArray(),
            Adapters: selection.Inventory
                .Select((adapter, index) => Map(adapter, index + 1))
                .ToArray(),
            Limitations:
            [
                "어댑터 역할은 Windows 인터페이스 유형과 제품명·설명에 대한 고정 규칙으로 분류하며 모든 드라이버를 완전히 식별하지는 못합니다.",
                "활성 VPN·가상 어댑터가 있다는 사실만으로 장애 원인을 확정하지 않습니다.",
                "기본 게이트웨이와 유니캐스트 주소는 존재 여부만 기록하며 실제 주소는 저장하지 않습니다.",
                "인터페이스 ID는 SHA-256 앞 10자리 지문으로만 기록하며 원본 GUID를 저장하지 않습니다.",
                "보고서 생성 시점 이후 절전·로밍·VPN 연결·어댑터 활성 상태가 변경될 수 있습니다."
            ]);
    }

    public static NetworkAdapterDiagnosticsReportExportResult WriteAll(
        NetworkAdapterDiagnosticsReportDocument report,
        string outputDirectory,
        string filePrefix = "WlanNetworkAdapters")
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);

        string fullDirectory = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(fullDirectory);
        string safePrefix = SensitiveDataRedactor.SafeFileComponent(
            filePrefix,
            "WlanNetworkAdapters");
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

        return new NetworkAdapterDiagnosticsReportExportResult(
            OutputDirectory: fullDirectory,
            JsonPath: jsonPath,
            CsvPath: csvPath,
            HtmlPath: htmlPath,
            Sha256Path: sha256Path,
            Sha256: hashes);
    }

    public static string RenderJson(
        NetworkAdapterDiagnosticsReportDocument report)
    {
        ArgumentNullException.ThrowIfNull(report);
        return JsonSerializer.Serialize(report, JsonOptions)
            + Environment.NewLine;
    }

    public static string RenderCsv(
        NetworkAdapterDiagnosticsReportDocument report)
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
        AddCsv(builder, "selection", "status", report.SelectionStatus);
        AddCsv(builder, "selection", "message", report.SelectionMessage);
        AddCsv(
            builder,
            "selection",
            "selectedAdapterFingerprint",
            report.SelectedAdapterFingerprint ?? string.Empty);

        for (int index = 0; index < report.Warnings.Count; index++)
        {
            AddCsv(
                builder,
                "warning",
                (index + 1).ToString(CultureInfo.InvariantCulture),
                report.Warnings[index]);
        }

        foreach (NetworkAdapterDiagnosticsReportEntry adapter in report.Adapters)
        {
            string section = $"adapter.{adapter.Order}";
            AddCsv(builder, section, "idFingerprint", adapter.IdFingerprint);
            AddCsv(builder, section, "displayName", adapter.DisplayName);
            AddCsv(builder, section, "role", adapter.Role);
            AddCsv(builder, section, "operationalStatus", adapter.OperationalStatus);
            AddCsv(builder, section, "interfaceType", adapter.InterfaceType);
            AddCsv(builder, section, "linkSpeedMbps", Invariant(adapter.LinkSpeedMbps));
            AddCsv(builder, section, "hasUnicastAddress", Invariant(adapter.HasUnicastAddress));
            AddCsv(builder, section, "hasDefaultGateway", Invariant(adapter.HasDefaultGateway));
            AddCsv(builder, section, "isNativeWlanConnected", Invariant(adapter.IsNativeWlanConnected));
            AddCsv(builder, section, "ipv4InterfaceIndex", Invariant(adapter.IPv4InterfaceIndex));
            AddCsv(builder, section, "ipv6InterfaceIndex", Invariant(adapter.IPv6InterfaceIndex));
            AddCsv(builder, section, "wirelessSelectionScore", Invariant(adapter.WirelessSelectionScore));
            AddCsv(
                builder,
                section,
                "classificationReasons",
                string.Join(" | ", adapter.ClassificationReasons));
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
        NetworkAdapterDiagnosticsReportDocument report)
    {
        ArgumentNullException.ThrowIfNull(report);

        StringBuilder builder = new(capacity: 24 * 1024);
        builder.Append("<!doctype html><html lang=\"ko\"><head><meta charset=\"utf-8\">");
        builder.Append("<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">");
        builder.Append("<meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'none'; style-src 'unsafe-inline'; base-uri 'none'; form-action 'none'; frame-ancestors 'none'\">");
        builder.Append("<title>네트워크 어댑터 진단 보고서</title><style>");
        builder.Append("body{margin:0;background:#f4f6f8;color:#17202a;font:14px/1.55 system-ui,-apple-system,'Segoe UI',sans-serif}main{max-width:1180px;margin:auto;padding:28px}h1{font-size:28px;margin:0}h2{font-size:19px;margin:0 0 12px}.sub,.small{color:#566573}.card{background:#fff;border:1px solid #d8dde3;border-radius:12px;padding:18px;margin-top:16px;box-shadow:0 1px 2px rgba(0,0,0,.03)}.privacy{background:#fff8e7;border-color:#e8ce8a}.badge{display:inline-block;border-radius:999px;padding:3px 9px;font-size:12px;background:#eaf2f8;color:#1b4f72}.ok{background:#e8f6f3;color:#0e6251}.warn{background:#fff3cd;color:#7d6608}.bad{background:#fdecea;color:#922b21}table{width:100%;border-collapse:collapse}th,td{padding:8px;border-bottom:1px solid #e8ebed;text-align:left;vertical-align:top}.scroll{overflow:auto}.nowrap{white-space:nowrap}@media(max-width:640px){main{padding:16px}}@media print{body{background:#fff}.card{box-shadow:none;break-inside:avoid}main{max-width:none;padding:0}}</style></head><body><main>");
        builder.Append("<header><h1>네트워크 어댑터 진단 보고서</h1><div class=\"sub\">");
        Html(
            builder,
            report.GeneratedAt.ToLocalTime()
                .ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture));
        builder.Append(" · 스키마 ");
        Html(builder, report.SchemaVersion);
        builder.Append("</div></header>");

        builder.Append("<section class=\"card privacy\"><h2>데이터 처리</h2><p>");
        Html(builder, report.DataHandlingStatement);
        builder.Append("</p><p class=\"small\">IP·MAC·게이트웨이 주소와 전체 인터페이스 GUID를 포함하지 않습니다. 이 HTML은 외부 리소스와 스크립트를 사용하지 않습니다.</p></section>");

        builder.Append("<section class=\"card\"><h2>Wi-Fi 선택 결과</h2><p><span class=\"badge ");
        Html(builder, StatusCss(report.SelectionStatus));
        builder.Append("\">");
        Html(builder, report.SelectionStatus);
        builder.Append("</span></p><p>");
        Html(builder, report.SelectionMessage);
        builder.Append("</p><p class=\"small\">선택 ID 지문: ");
        Html(builder, report.SelectedAdapterFingerprint ?? "없음");
        builder.Append("</p>");
        if (report.Warnings.Count > 0)
        {
            builder.Append("<h2>경고</h2><ul>");
            foreach (string warning in report.Warnings)
            {
                builder.Append("<li>");
                Html(builder, warning);
                builder.Append("</li>");
            }
            builder.Append("</ul>");
        }
        builder.Append("</section>");

        builder.Append("<section class=\"card\"><h2>어댑터 인벤토리</h2><div class=\"scroll\"><table><thead><tr><th>No</th><th>역할</th><th>상태</th><th>유형</th><th>속도</th><th>GW</th><th>IP</th><th>WLAN</th><th>점수</th><th>ID 지문</th><th>이름</th></tr></thead><tbody>");
        foreach (NetworkAdapterDiagnosticsReportEntry adapter in report.Adapters)
        {
            builder.Append("<tr><td>");
            Html(builder, adapter.Order.ToString(CultureInfo.InvariantCulture));
            builder.Append("</td><td class=\"nowrap\">");
            Html(builder, adapter.Role);
            builder.Append("</td><td>");
            Html(builder, adapter.OperationalStatus);
            builder.Append("</td><td>");
            Html(builder, adapter.InterfaceType);
            builder.Append("</td><td class=\"nowrap\">");
            Html(
                builder,
                adapter.LinkSpeedMbps.HasValue
                    ? $"{adapter.LinkSpeedMbps.Value:F1} Mbps"
                    : "확인 불가");
            builder.Append("</td><td>");
            Html(builder, adapter.HasDefaultGateway ? "Y" : "-");
            builder.Append("</td><td>");
            Html(builder, adapter.HasUnicastAddress ? "Y" : "-");
            builder.Append("</td><td>");
            Html(builder, adapter.IsNativeWlanConnected ? "Y" : "-");
            builder.Append("</td><td>");
            Html(
                builder,
                adapter.WirelessSelectionScore?.ToString(
                    CultureInfo.InvariantCulture) ?? "-");
            builder.Append("</td><td class=\"nowrap\">");
            Html(builder, adapter.IdFingerprint);
            builder.Append("</td><td>");
            Html(builder, adapter.DisplayName);
            builder.Append("</td></tr>");
        }
        builder.Append("</tbody></table></div></section>");

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

    private static NetworkAdapterDiagnosticsReportEntry Map(
        ClassifiedNetworkAdapter classified,
        int order)
    {
        NetworkAdapterCandidate adapter = classified.Candidate;
        return new NetworkAdapterDiagnosticsReportEntry(
            Order: order,
            IdFingerprint: Fingerprint(adapter.Id),
            DisplayName: SensitiveDataRedactor.RedactText(
                    SafeDisplayName(adapter))
                ?? "이름 없는 어댑터",
            Role: classified.Role.ToString(),
            OperationalStatus: adapter.OperationalStatus.ToString(),
            InterfaceType: adapter.InterfaceType.ToString(),
            LinkSpeedMbps: adapter.SpeedBitsPerSecond > 0
                ? adapter.SpeedBitsPerSecond / 1_000_000d
                : null,
            HasUnicastAddress: adapter.HasUnicastAddress,
            HasDefaultGateway: adapter.HasDefaultGateway,
            IsNativeWlanConnected: adapter.IsNativeWlanConnected,
            IPv4InterfaceIndex: adapter.IPv4InterfaceIndex,
            IPv6InterfaceIndex: adapter.IPv6InterfaceIndex,
            WirelessSelectionScore: classified.IsEligiblePhysicalWireless
                ? classified.WirelessSelectionScore
                : null,
            ClassificationReasons: classified.ClassificationReasons
                .Select(reason => SensitiveDataRedactor.RedactText(reason)
                    ?? string.Empty)
                .ToArray());
    }

    private static string SafeDisplayName(NetworkAdapterCandidate adapter)
    {
        string value = string.IsNullOrWhiteSpace(adapter.Name)
            ? adapter.Description
            : adapter.Name;
        return string.IsNullOrWhiteSpace(value)
            ? "이름 없는 어댑터"
            : value.Replace('\r', ' ').Replace('\n', ' ').Trim();
    }

    private static string Fingerprint(string value)
    {
        byte[] hash = SHA256.HashData(
            Encoding.UTF8.GetBytes(value.Trim()));
        return Convert.ToHexString(hash)[..10].ToLowerInvariant();
    }

    private static string StatusCss(string status) =>
        status.Equals("Selected", StringComparison.OrdinalIgnoreCase)
            ? "ok"
            : status.Equals("Ambiguous", StringComparison.OrdinalIgnoreCase)
                ? "warn"
                : "bad";

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
            "사용 가능한 어댑터 진단 보고서 파일 이름을 만들지 못했습니다.");
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
