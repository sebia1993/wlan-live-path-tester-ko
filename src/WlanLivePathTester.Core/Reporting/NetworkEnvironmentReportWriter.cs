using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WlanLivePathTester.Core.NetworkEnvironment;

namespace WlanLivePathTester.Core.Reporting;

public sealed record NetworkEnvironmentReportDocument(
    string SchemaVersion,
    DateTimeOffset GeneratedAt,
    string ApplicationName,
    string ApplicationVersion,
    bool SensitiveValuesIncluded,
    string DataHandlingStatement,
    NetworkEnvironmentReportSummary Summary,
    IReadOnlyList<NetworkEnvironmentReportAdapter> Adapters,
    IReadOnlyList<NetworkEnvironmentReportFinding> Findings,
    IReadOnlyList<string> Limitations);

public sealed record NetworkEnvironmentReportSummary(
    int TotalAdapterCount,
    int ActiveAdapterCount,
    int ActiveWirelessCount,
    int ActiveEthernetCount,
    int ActiveVpnCount,
    int ActiveVirtualCount,
    int ActiveDefaultGatewayCount,
    bool RouteSelectionMayBeAmbiguous,
    bool HasSinglePhysicalWirelessCandidate);

public sealed record NetworkEnvironmentReportAdapter(
    int Index,
    string Category,
    string NativeInterfaceType,
    string OperationalState,
    long? SpeedBitsPerSecond,
    bool HasDefaultGateway,
    int GatewayCount,
    bool HasIpv4,
    bool HasIpv6,
    int UnicastAddressCount,
    bool SupportsMulticast,
    bool IsVirtual,
    bool IsVpn,
    bool PropertyReadWasPartial);

public sealed record NetworkEnvironmentReportFinding(
    string Code,
    string Severity,
    string Title,
    string Evidence,
    string Interpretation,
    string NextStep);

public sealed record NetworkEnvironmentReportExportResult(
    string OutputDirectory,
    string JsonPath,
    string CsvPath,
    string HtmlPath,
    string Sha256Path,
    IReadOnlyDictionary<string, string> Sha256);

public static class NetworkEnvironmentReportWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static NetworkEnvironmentReportDocument CreateDocument(
        LocalNetworkEnvironmentSnapshot snapshot,
        string applicationVersion,
        DateTimeOffset? generatedAt = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationVersion);

        NetworkEnvironmentAssessment assessment = snapshot.Assessment;
        LocalNetworkAdapterSnapshot[] orderedAdapters = snapshot.Adapters
            .OrderByDescending(adapter => adapter.IsUp)
            .ThenByDescending(adapter => adapter.HasDefaultGateway)
            .ThenBy(adapter => adapter.Category)
            .ThenBy(adapter => adapter.NativeInterfaceType, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new NetworkEnvironmentReportDocument(
            SchemaVersion: "1.0",
            GeneratedAt: generatedAt ?? DateTimeOffset.UtcNow,
            ApplicationName: "WLAN Live Path Tester KO",
            ApplicationVersion: applicationVersion,
            SensitiveValuesIncluded: false,
            DataHandlingStatement: "인터페이스 환경 보고서는 현재 PC에서 생성되며 자동 업로드, 텔레메트리 또는 온라인 분석을 수행하지 않습니다.",
            Summary: new NetworkEnvironmentReportSummary(
                TotalAdapterCount: assessment.TotalAdapterCount,
                ActiveAdapterCount: assessment.ActiveAdapterCount,
                ActiveWirelessCount: assessment.ActiveWirelessCount,
                ActiveEthernetCount: assessment.ActiveEthernetCount,
                ActiveVpnCount: assessment.ActiveVpnCount,
                ActiveVirtualCount: assessment.ActiveVirtualCount,
                ActiveDefaultGatewayCount: assessment.ActiveDefaultGatewayCount,
                RouteSelectionMayBeAmbiguous: assessment.RouteSelectionMayBeAmbiguous,
                HasSinglePhysicalWirelessCandidate:
                    assessment.PreferredWirelessDisplayName is not null),
            Adapters: orderedAdapters
                .Select((adapter, index) =>
                    new NetworkEnvironmentReportAdapter(
                        Index: index + 1,
                        Category: adapter.Category.ToString(),
                        NativeInterfaceType: adapter.NativeInterfaceType,
                        OperationalState: adapter.OperationalState.ToString(),
                        SpeedBitsPerSecond: adapter.SpeedBitsPerSecond,
                        HasDefaultGateway: adapter.HasDefaultGateway,
                        GatewayCount: adapter.GatewayCount,
                        HasIpv4: adapter.HasIpv4,
                        HasIpv6: adapter.HasIpv6,
                        UnicastAddressCount: adapter.UnicastAddressCount,
                        SupportsMulticast: adapter.SupportsMulticast,
                        IsVirtual: adapter.IsVirtual,
                        IsVpn: adapter.IsVpn,
                        PropertyReadWasPartial:
                            !string.IsNullOrWhiteSpace(adapter.ReadError)))
                .ToArray(),
            Findings: assessment.Findings
                .Select(finding => new NetworkEnvironmentReportFinding(
                    Code: finding.Code,
                    Severity: finding.Severity.ToString(),
                    Title: finding.Title,
                    Evidence: finding.Evidence,
                    Interpretation: finding.Interpretation,
                    NextStep: finding.NextStep))
                .ToArray(),
            Limitations:
            [
                "인터페이스 이름, 설명, GUID, IP, 게이트웨이, DNS와 MAC 주소 원문은 보고서에 포함하지 않습니다.",
                "기본 게이트웨이 개수만으로 특정 목적지의 실제 Windows 라우팅 경로를 확정할 수 없습니다.",
                "VPN·가상 어댑터 분류는 Windows 인터페이스 유형과 제한된 이름 휴리스틱을 사용하므로 오탐·누락 가능성이 있습니다.",
                "프록시 사용 시 로컬 PC의 직접 연결 목적지는 외부 사이트가 아니라 프록시 서버일 수 있습니다.",
                "실제 경로 확인에는 Get-NetRoute, Get-NetIPInterface, 프록시 판정과 다운로드 결과를 함께 사용해야 합니다."
            ]);
    }

    public static NetworkEnvironmentReportExportResult WriteAll(
        NetworkEnvironmentReportDocument report,
        string outputDirectory,
        string filePrefix = "WlanNetworkEnvironment")
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);

        string fullDirectory = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(fullDirectory);
        string safePrefix = SensitiveDataRedactor.SafeFileComponent(
            filePrefix,
            "WlanNetworkEnvironment");
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

        return new NetworkEnvironmentReportExportResult(
            OutputDirectory: fullDirectory,
            JsonPath: jsonPath,
            CsvPath: csvPath,
            HtmlPath: htmlPath,
            Sha256Path: sha256Path,
            Sha256: hashes);
    }

    public static string RenderJson(
        NetworkEnvironmentReportDocument report)
    {
        ArgumentNullException.ThrowIfNull(report);
        return JsonSerializer.Serialize(report, JsonOptions)
            + Environment.NewLine;
    }

    public static string RenderCsv(
        NetworkEnvironmentReportDocument report)
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
            FormatInvariant(report.SensitiveValuesIncluded));
        AddCsv(builder, "metadata", "dataHandling", report.DataHandlingStatement);

        AddSummaryCsv(builder, report.Summary);
        foreach (NetworkEnvironmentReportAdapter adapter in report.Adapters)
        {
            string section = $"adapter.{adapter.Index}";
            AddCsv(builder, section, "category", adapter.Category);
            AddCsv(builder, section, "nativeInterfaceType", adapter.NativeInterfaceType);
            AddCsv(builder, section, "operationalState", adapter.OperationalState);
            AddCsv(builder, section, "speedBitsPerSecond", FormatInvariant(adapter.SpeedBitsPerSecond));
            AddCsv(builder, section, "hasDefaultGateway", FormatInvariant(adapter.HasDefaultGateway));
            AddCsv(builder, section, "gatewayCount", FormatInvariant(adapter.GatewayCount));
            AddCsv(builder, section, "hasIpv4", FormatInvariant(adapter.HasIpv4));
            AddCsv(builder, section, "hasIpv6", FormatInvariant(adapter.HasIpv6));
            AddCsv(builder, section, "unicastAddressCount", FormatInvariant(adapter.UnicastAddressCount));
            AddCsv(builder, section, "supportsMulticast", FormatInvariant(adapter.SupportsMulticast));
            AddCsv(builder, section, "isVirtual", FormatInvariant(adapter.IsVirtual));
            AddCsv(builder, section, "isVpn", FormatInvariant(adapter.IsVpn));
            AddCsv(builder, section, "propertyReadWasPartial", FormatInvariant(adapter.PropertyReadWasPartial));
        }

        for (int index = 0; index < report.Findings.Count; index++)
        {
            NetworkEnvironmentReportFinding finding = report.Findings[index];
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
        NetworkEnvironmentReportDocument report)
    {
        ArgumentNullException.ThrowIfNull(report);

        StringBuilder builder = new(capacity: 24 * 1024);
        builder.Append("<!doctype html><html lang=\"ko\"><head><meta charset=\"utf-8\">");
        builder.Append("<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">");
        builder.Append("<meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'none'; style-src 'unsafe-inline'; base-uri 'none'; form-action 'none'; frame-ancestors 'none'\">");
        builder.Append("<title>로컬 인터페이스 환경 보고서</title><style>");
        builder.Append("body{margin:0;background:#f4f6f8;color:#17202a;font:14px/1.55 system-ui,-apple-system,'Segoe UI',sans-serif}main{max-width:1180px;margin:auto;padding:28px}h1{font-size:28px;margin:0}h2{font-size:19px;margin:0 0 12px}.sub,.small{color:#566573}.card{background:#fff;border:1px solid #d8dde3;border-radius:12px;padding:18px;margin-top:16px;box-shadow:0 1px 2px rgba(0,0,0,.03)}.grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(190px,1fr));gap:10px}.metric{background:#f8fafb;border-radius:8px;padding:12px}.metric strong{display:block;font-size:19px}.badge{display:inline-block;border-radius:999px;padding:3px 9px;font-size:12px;background:#eaf2f8;color:#1b4f72}.warning{background:#fff3cd;color:#7d6608}.information{background:#e8f6f3;color:#0e6251}table{width:100%;border-collapse:collapse}th,td{padding:8px;border-bottom:1px solid #e8ebed;text-align:left;vertical-align:top}.scroll{overflow:auto}.privacy{background:#fff8e7;border-color:#e8ce8a}.finding{border-left:4px solid #5dade2;padding-left:12px;margin-top:14px}.finding.warning{border-color:#f5b041}@media(max-width:640px){main{padding:16px}.grid{display:block}.metric{margin-top:8px}}@media print{body{background:#fff}.card{box-shadow:none;break-inside:avoid}main{max-width:none;padding:0}}</style></head><body><main>");
        builder.Append("<header><h1>로컬 인터페이스 환경 보고서</h1><div class=\"sub\">");
        Html(
            builder,
            report.GeneratedAt.ToLocalTime()
                .ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture));
        builder.Append(" · 스키마 ");
        Html(builder, report.SchemaVersion);
        builder.Append("</div></header><section class=\"card privacy\"><h2>데이터 처리</h2><p>");
        Html(builder, report.DataHandlingStatement);
        builder.Append("</p><p class=\"small\">인터페이스 이름·설명·GUID·IP·게이트웨이·DNS·MAC 주소 원문은 포함하지 않습니다. 이 HTML은 외부 리소스와 스크립트를 사용하지 않습니다.</p></section>");

        AppendSummaryHtml(builder, report.Summary);
        AppendAdaptersHtml(builder, report.Adapters);
        AppendFindingsHtml(builder, report.Findings);

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

    private static void AddSummaryCsv(
        StringBuilder builder,
        NetworkEnvironmentReportSummary summary)
    {
        AddCsv(builder, "summary", "totalAdapterCount", FormatInvariant(summary.TotalAdapterCount));
        AddCsv(builder, "summary", "activeAdapterCount", FormatInvariant(summary.ActiveAdapterCount));
        AddCsv(builder, "summary", "activeWirelessCount", FormatInvariant(summary.ActiveWirelessCount));
        AddCsv(builder, "summary", "activeEthernetCount", FormatInvariant(summary.ActiveEthernetCount));
        AddCsv(builder, "summary", "activeVpnCount", FormatInvariant(summary.ActiveVpnCount));
        AddCsv(builder, "summary", "activeVirtualCount", FormatInvariant(summary.ActiveVirtualCount));
        AddCsv(builder, "summary", "activeDefaultGatewayCount", FormatInvariant(summary.ActiveDefaultGatewayCount));
        AddCsv(builder, "summary", "routeSelectionMayBeAmbiguous", FormatInvariant(summary.RouteSelectionMayBeAmbiguous));
        AddCsv(builder, "summary", "hasSinglePhysicalWirelessCandidate", FormatInvariant(summary.HasSinglePhysicalWirelessCandidate));
    }

    private static void AppendSummaryHtml(
        StringBuilder builder,
        NetworkEnvironmentReportSummary summary)
    {
        builder.Append("<section class=\"card\"><h2>요약</h2><div class=\"grid\">");
        Metric(builder, "전체 / 활성", $"{summary.TotalAdapterCount} / {summary.ActiveAdapterCount}");
        Metric(builder, "활성 Wi-Fi", summary.ActiveWirelessCount.ToString(CultureInfo.InvariantCulture));
        Metric(builder, "활성 유선", summary.ActiveEthernetCount.ToString(CultureInfo.InvariantCulture));
        Metric(builder, "활성 VPN·터널", summary.ActiveVpnCount.ToString(CultureInfo.InvariantCulture));
        Metric(builder, "활성 가상 NIC", summary.ActiveVirtualCount.ToString(CultureInfo.InvariantCulture));
        Metric(builder, "기본 게이트웨이 NIC", summary.ActiveDefaultGatewayCount.ToString(CultureInfo.InvariantCulture));
        Metric(builder, "경로 혼재 가능성", summary.RouteSelectionMayBeAmbiguous ? "있음" : "낮음");
        Metric(builder, "단일 물리 Wi-Fi 후보", summary.HasSinglePhysicalWirelessCandidate ? "있음" : "확정 안 함");
        builder.Append("</div></section>");
    }

    private static void AppendAdaptersHtml(
        StringBuilder builder,
        IReadOnlyList<NetworkEnvironmentReportAdapter> adapters)
    {
        builder.Append("<section class=\"card\"><h2>익명화된 인터페이스 목록</h2><div class=\"scroll\"><table><thead><tr><th>#</th><th>범주</th><th>Native 유형</th><th>상태</th><th>링크 속도</th><th>게이트웨이</th><th>주소 계열</th><th>가상</th><th>VPN</th></tr></thead><tbody>");
        foreach (NetworkEnvironmentReportAdapter adapter in adapters)
        {
            builder.Append("<tr><td>");
            Html(builder, adapter.Index.ToString(CultureInfo.InvariantCulture));
            builder.Append("</td><td>");
            Html(builder, adapter.Category);
            builder.Append("</td><td>");
            Html(builder, adapter.NativeInterfaceType);
            builder.Append("</td><td>");
            Html(builder, adapter.OperationalState);
            builder.Append("</td><td>");
            Html(builder, FormatSpeed(adapter.SpeedBitsPerSecond));
            builder.Append("</td><td>");
            Html(builder, adapter.HasDefaultGateway ? $"있음({adapter.GatewayCount})" : "없음");
            builder.Append("</td><td>");
            Html(builder, $"IPv4 {(adapter.HasIpv4 ? "Y" : "N")} / IPv6 {(adapter.HasIpv6 ? "Y" : "N")} / {adapter.UnicastAddressCount}개");
            builder.Append("</td><td>");
            Html(builder, adapter.IsVirtual ? "Y" : "N");
            builder.Append("</td><td>");
            Html(builder, adapter.IsVpn ? "Y" : "N");
            builder.Append("</td></tr>");
        }
        builder.Append("</tbody></table></div></section>");
    }

    private static void AppendFindingsHtml(
        StringBuilder builder,
        IReadOnlyList<NetworkEnvironmentReportFinding> findings)
    {
        builder.Append("<section class=\"card\"><h2>판정</h2>");
        foreach (NetworkEnvironmentReportFinding finding in findings)
        {
            string severityClass = finding.Severity.Equals(
                "Warning",
                StringComparison.OrdinalIgnoreCase)
                ? "warning"
                : "information";
            builder.Append("<article class=\"finding ");
            Html(builder, severityClass);
            builder.Append("\"><span class=\"badge ");
            Html(builder, severityClass);
            builder.Append("\">");
            Html(builder, finding.Severity);
            builder.Append("</span><h3>");
            Html(builder, finding.Title);
            builder.Append("</h3><p><strong>근거:</strong> ");
            Html(builder, finding.Evidence);
            builder.Append("</p><p><strong>해석:</strong> ");
            Html(builder, finding.Interpretation);
            builder.Append("</p><p><strong>다음 확인:</strong> ");
            Html(builder, finding.NextStep);
            builder.Append("</p></article>");
        }
        builder.Append("</section>");
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

    private static string FormatInvariant(object? value) =>
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

    private static void Html(StringBuilder builder, string? value) =>
        builder.Append(WebUtility.HtmlEncode(value ?? string.Empty));

    private static string FormatSpeed(long? bitsPerSecond)
    {
        if (!bitsPerSecond.HasValue || bitsPerSecond.Value <= 0)
        {
            return "확인 불가";
        }

        double mbps = bitsPerSecond.Value / 1_000_000d;
        return mbps >= 1000
            ? $"{mbps / 1000:F1} Gbps"
            : $"{mbps:F0} Mbps";
    }

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

        throw new IOException("사용 가능한 인터페이스 환경 보고서 파일 이름을 만들지 못했습니다.");
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
