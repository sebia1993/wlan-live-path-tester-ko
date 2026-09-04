using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace WlanLivePathTester.Core.Reporting;

public static class LocalReportWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static LocalReportExportResult WriteAll(
        LocalDiagnosticReport report,
        string outputDirectory,
        string filePrefix = "WlanLivePathTester")
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);

        string fullOutputDirectory = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(fullOutputDirectory);

        string safePrefix = SensitiveDataRedactor.SafeFileComponent(
            filePrefix,
            "WlanLivePathTester");
        string timestamp = report.Metadata.GeneratedAt.ToLocalTime()
            .ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        string baseName = GetAvailableBaseName(
            fullOutputDirectory,
            $"{safePrefix}_{timestamp}");

        string jsonPath = Path.Combine(fullOutputDirectory, baseName + ".json");
        string csvPath = Path.Combine(fullOutputDirectory, baseName + ".csv");
        string htmlPath = Path.Combine(fullOutputDirectory, baseName + ".html");
        string sha256Path = Path.Combine(fullOutputDirectory, baseName + "_SHA256SUMS.txt");

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

        return new LocalReportExportResult(
            OutputDirectory: fullOutputDirectory,
            JsonPath: jsonPath,
            CsvPath: csvPath,
            HtmlPath: htmlPath,
            Sha256Path: sha256Path,
            Sha256: hashes);
    }

    public static string RenderJson(LocalDiagnosticReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        return JsonSerializer.Serialize(report, JsonOptions) + Environment.NewLine;
    }

    public static string RenderCsv(LocalDiagnosticReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        List<(string Section, string Key, string Value)> rows = [];
        Add(rows, "metadata", "schemaVersion", report.SchemaVersion);
        Add(rows, "metadata", "generatedAt", report.Metadata.GeneratedAt.ToString("O", CultureInfo.InvariantCulture));
        Add(rows, "metadata", "applicationName", report.Metadata.ApplicationName);
        Add(rows, "metadata", "applicationVersion", report.Metadata.ApplicationVersion);
        Add(rows, "metadata", "operatingSystem", report.Metadata.OperatingSystem);
        Add(rows, "metadata", "runtimeVersion", report.Metadata.RuntimeVersion);
        Add(rows, "metadata", "culture", report.Metadata.Culture);
        Add(rows, "metadata", "sensitiveValuesIncluded", report.Metadata.SensitiveValuesIncluded.ToString(CultureInfo.InvariantCulture));
        Add(rows, "metadata", "dataHandling", report.Metadata.DataHandlingStatement);

        Add(rows, "wlan", "capturedAt", report.Wlan.CapturedAt.ToString("O", CultureInfo.InvariantCulture));
        Add(rows, "wlan", "isConnected", report.Wlan.IsConnected.ToString(CultureInfo.InvariantCulture));
        Add(rows, "wlan", "interfaceDescription", report.Wlan.InterfaceDescription);
        Add(rows, "wlan", "interfaceState", report.Wlan.InterfaceState);
        Add(rows, "wlan", "ssid", report.Wlan.Ssid);
        Add(rows, "wlan", "bssid", report.Wlan.Bssid);
        Add(rows, "wlan", "rssiDbm", Format(report.Wlan.RssiDbm));
        Add(rows, "wlan", "signalQualityPercent", Format(report.Wlan.SignalQualityPercent));
        Add(rows, "wlan", "channel", Format(report.Wlan.Channel));
        Add(rows, "wlan", "centerFrequencyMhz", Format(report.Wlan.CenterFrequencyMhz));
        Add(rows, "wlan", "band", report.Wlan.Band);
        Add(rows, "wlan", "phyType", report.Wlan.PhyType);
        Add(rows, "wlan", "receiveLinkMbps", Format(report.Wlan.ReceiveLinkMbps));
        Add(rows, "wlan", "transmitLinkMbps", Format(report.Wlan.TransmitLinkMbps));
        Add(rows, "wlan", "authentication", report.Wlan.Authentication);
        Add(rows, "wlan", "cipher", report.Wlan.Cipher);
        Add(rows, "wlan", "readError", report.Wlan.ReadError ?? string.Empty);

        Add(rows, "proxy", "readSucceeded", report.Proxy.ReadSucceeded.ToString(CultureInfo.InvariantCulture));
        Add(rows, "proxy", "mode", report.Proxy.Mode);
        Add(rows, "proxy", "autoDetectEnabled", report.Proxy.AutoDetectEnabled.ToString(CultureInfo.InvariantCulture));
        Add(rows, "proxy", "pacConfigured", report.Proxy.PacConfigured.ToString(CultureInfo.InvariantCulture));
        Add(rows, "proxy", "manualProxyConfigured", report.Proxy.ManualProxyConfigured.ToString(CultureInfo.InvariantCulture));
        Add(rows, "proxy", "bypassConfigured", report.Proxy.BypassConfigured.ToString(CultureInfo.InvariantCulture));
        Add(rows, "proxy", "win32Error", Format(report.Proxy.Win32Error));
        Add(rows, "proxy", "statement", report.Proxy.Statement);

        foreach (ReportTextSection section in report.Measurements)
        {
            string prefix = $"measurement.{section.SectionId}";
            Add(rows, prefix, "title", section.Title);
            Add(rows, prefix, "capturedAt", section.CapturedAt.ToString("O", CultureInfo.InvariantCulture));
            Add(rows, prefix, "content", section.Content);
        }

        if (report.BrowserObservation is ReportObservationSection observation)
        {
            Add(rows, "browserObservation", "status", observation.Status);
            Add(rows, "browserObservation", "startedAt", Format(observation.StartedAt));
            Add(rows, "browserObservation", "completedAt", Format(observation.CompletedAt));
            Add(rows, "browserObservation", "observedSeconds", Format(observation.ObservedSeconds));
            Add(rows, "browserObservation", "baselineReceiveMbps", Format(observation.BaselineReceiveMbps));
            Add(rows, "browserObservation", "averageAdjustedReceiveMbps", Format(observation.AverageAdjustedReceiveMbps));
            Add(rows, "browserObservation", "peakAdjustedReceiveMbps", Format(observation.PeakAdjustedReceiveMbps));
            Add(rows, "browserObservation", "totalReceiveBytes", Format(observation.TotalReceiveBytes));
            Add(rows, "browserObservation", "activeSampleCount", Format(observation.ActiveSampleCount));
            Add(rows, "browserObservation", "pauseCount", Format(observation.PauseCount));
            Add(rows, "browserObservation", "suddenDropCount", Format(observation.SuddenDropCount));
            Add(rows, "browserObservation", "bssidChangeCount", Format(observation.BssidChangeCount));
            Add(rows, "browserObservation", "adapterChangeCount", Format(observation.AdapterChangeCount));
            Add(rows, "browserObservation", "counterResetCount", Format(observation.CounterResetCount));
            Add(rows, "browserObservation", "wlanDisconnectedSampleCount", Format(observation.WlanDisconnectedSampleCount));
            Add(rows, "browserObservation", "confidence", observation.Confidence);
            Add(rows, "browserObservation", "message", observation.Message);
            Add(rows, "browserObservation", "limitation", observation.Limitation);

            for (int index = 0; index < observation.Samples.Count; index++)
            {
                ReportObservationSample sample = observation.Samples[index];
                string prefix = $"browserObservation.sample.{index + 1}";
                Add(rows, prefix, "timestamp", sample.Timestamp.ToString("O", CultureInfo.InvariantCulture));
                Add(rows, prefix, "intervalSeconds", Format(sample.IntervalSeconds));
                Add(rows, prefix, "isBaseline", sample.IsBaseline.ToString(CultureInfo.InvariantCulture));
                Add(rows, prefix, "receiveBytesDelta", Format(sample.ReceiveBytesDelta));
                Add(rows, prefix, "transmitBytesDelta", Format(sample.TransmitBytesDelta));
                Add(rows, prefix, "rawReceiveMbps", Format(sample.RawReceiveMbps));
                Add(rows, prefix, "rawTransmitMbps", Format(sample.RawTransmitMbps));
                Add(rows, prefix, "adjustedReceiveMbps", Format(sample.AdjustedReceiveMbps));
                Add(rows, prefix, "rssiDbm", Format(sample.RssiDbm));
                Add(rows, prefix, "receiveLinkMbps", Format(sample.ReceiveLinkMbps));
                Add(rows, prefix, "transmitLinkMbps", Format(sample.TransmitLinkMbps));
                Add(rows, prefix, "bssidChanged", sample.BssidChanged.ToString(CultureInfo.InvariantCulture));
                Add(rows, prefix, "adapterChanged", sample.AdapterChanged.ToString(CultureInfo.InvariantCulture));
                Add(rows, prefix, "counterReset", sample.CounterReset.ToString(CultureInfo.InvariantCulture));
                Add(rows, prefix, "wlanDisconnected", sample.WlanDisconnected.ToString(CultureInfo.InvariantCulture));
                Add(rows, prefix, "pauseDetected", sample.PauseDetected.ToString(CultureInfo.InvariantCulture));
                Add(rows, prefix, "suddenDropDetected", sample.SuddenDropDetected.ToString(CultureInfo.InvariantCulture));
                Add(rows, prefix, "note", sample.Note ?? string.Empty);
            }
        }

        for (int index = 0; index < report.Findings.Count; index++)
        {
            ReportFinding finding = report.Findings[index];
            string prefix = $"finding.{index + 1}";
            Add(rows, prefix, "code", finding.Code);
            Add(rows, prefix, "severity", finding.Severity);
            Add(rows, prefix, "title", finding.Title);
            Add(rows, prefix, "evidence", finding.Evidence);
            Add(rows, prefix, "interpretation", finding.Interpretation);
            Add(rows, prefix, "limitation", finding.Limitation);
            Add(rows, prefix, "nextStep", finding.NextStep);
        }

        for (int index = 0; index < report.Limitations.Count; index++)
        {
            Add(rows, "limitation", (index + 1).ToString(CultureInfo.InvariantCulture), report.Limitations[index]);
        }

        StringBuilder builder = new();
        builder.AppendLine("section,key,value");
        foreach ((string section, string key, string value) in rows)
        {
            builder.Append(Csv(section));
            builder.Append(',');
            builder.Append(Csv(key));
            builder.Append(',');
            builder.AppendLine(Csv(value));
        }

        return builder.ToString();
    }

    public static string RenderHtml(LocalDiagnosticReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        StringBuilder builder = new(capacity: 32 * 1024);
        builder.Append("<!doctype html><html lang=\"ko\"><head><meta charset=\"utf-8\">");
        builder.Append("<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">");
        builder.Append("<meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'none'; style-src 'unsafe-inline'; img-src data:; base-uri 'none'; form-action 'none'; frame-ancestors 'none'\">");
        builder.Append("<title>WLAN Live Path Tester KO 보고서</title>");
        builder.Append("<style>");
        builder.Append("body{margin:0;background:#f4f6f8;color:#17202a;font:14px/1.55 system-ui,-apple-system,'Segoe UI',sans-serif}main{max-width:1180px;margin:auto;padding:28px}h1{font-size:28px;margin:0}h2{font-size:19px;margin:0 0 12px}h3{font-size:15px;margin:18px 0 8px}.sub{color:#566573;margin-top:6px}.grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(260px,1fr));gap:14px}.card{background:#fff;border:1px solid #d8dde3;border-radius:12px;padding:18px;margin-top:16px;box-shadow:0 1px 2px rgba(0,0,0,.03)}table{width:100%;border-collapse:collapse}th,td{padding:9px 10px;border-bottom:1px solid #e8ebed;text-align:left;vertical-align:top}th{width:32%;color:#566573;font-weight:600}.badge{display:inline-block;border-radius:999px;padding:3px 9px;font-size:12px;background:#eaf2f8;color:#1b4f72}.warn{background:#fff3cd;color:#7d6608}.critical{background:#fdecea;color:#922b21}.info{background:#e8f6f3;color:#0e6251}pre{white-space:pre-wrap;overflow-wrap:anywhere;background:#f7f9fa;border-radius:8px;padding:12px;margin:0}.finding{border-left:4px solid #5dade2;padding-left:12px;margin-top:14px}.finding.warning{border-color:#f5b041}.finding.critical{border-color:#e74c3c}.samples{max-height:520px;overflow:auto}.small{font-size:12px;color:#707b7c}.privacy{background:#fff8e7;border-color:#e8ce8a}@media(max-width:640px){main{padding:16px}th{width:40%}}@media print{body{background:#fff}.card{box-shadow:none;break-inside:avoid}main{max-width:none;padding:0}.samples{max-height:none;overflow:visible}}");
        builder.Append("</style></head><body><main>");
        builder.Append("<header><h1>WLAN Live Path Tester KO</h1><div class=\"sub\">로컬 네트워크 진단 보고서 · ");
        Html(builder, report.Metadata.GeneratedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture));
        builder.Append("</div></header>");

        builder.Append("<section class=\"card privacy\"><h2>데이터 처리</h2><p>");
        Html(builder, report.Metadata.DataHandlingStatement);
        builder.Append("</p><p class=\"small\">민감정보 포함: ");
        Html(builder, report.Metadata.SensitiveValuesIncluded ? "예" : "아니요");
        builder.Append(" · 보고서는 외부 리소스와 스크립트를 포함하지 않습니다.</p></section>");

        builder.Append("<div class=\"grid\">");
        builder.Append("<section class=\"card\"><h2>실행 정보</h2><table>");
        Row(builder, "앱 버전", report.Metadata.ApplicationVersion);
        Row(builder, "운영체제", report.Metadata.OperatingSystem);
        Row(builder, "런타임", report.Metadata.RuntimeVersion);
        Row(builder, "문화권", report.Metadata.Culture);
        Row(builder, "스키마", report.SchemaVersion);
        builder.Append("</table></section>");

        builder.Append("<section class=\"card\"><h2>WLAN</h2><table>");
        Row(builder, "연결", report.Wlan.IsConnected ? "연결됨" : "연결 안 됨");
        Row(builder, "인터페이스", report.Wlan.InterfaceDescription);
        Row(builder, "SSID", report.Wlan.Ssid);
        Row(builder, "BSSID", report.Wlan.Bssid);
        Row(builder, "RSSI", Unit(report.Wlan.RssiDbm, "dBm"));
        Row(builder, "신호 품질", Unit(report.Wlan.SignalQualityPercent, "%"));
        Row(builder, "밴드 / 채널", $"{report.Wlan.Band} / {Format(report.Wlan.Channel)}");
        Row(builder, "PHY", report.Wlan.PhyType);
        Row(builder, "Rx / Tx 링크", $"{Unit(report.Wlan.ReceiveLinkMbps, "Mbps")} / {Unit(report.Wlan.TransmitLinkMbps, "Mbps")}");
        Row(builder, "인증 / 암호화", $"{report.Wlan.Authentication} / {report.Wlan.Cipher}");
        builder.Append("</table></section>");

        builder.Append("<section class=\"card\"><h2>프록시 설정</h2><table>");
        Row(builder, "읽기", report.Proxy.ReadSucceeded ? "성공" : "실패");
        Row(builder, "방식", report.Proxy.Mode);
        Row(builder, "자동 감지", report.Proxy.AutoDetectEnabled ? "사용" : "미사용");
        Row(builder, "PAC", report.Proxy.PacConfigured ? "설정됨" : "없음");
        Row(builder, "수동 프록시", report.Proxy.ManualProxyConfigured ? "설정됨" : "없음");
        Row(builder, "바이패스", report.Proxy.BypassConfigured ? "설정됨" : "없음");
        builder.Append("</table><p class=\"small\">");
        Html(builder, report.Proxy.Statement);
        builder.Append("</p></section></div>");

        builder.Append("<section class=\"card\"><h2>측정 결과</h2>");
        if (report.Measurements.Count == 0)
        {
            builder.Append("<p>저장된 측정 화면 결과가 없습니다.</p>");
        }
        else
        {
            foreach (ReportTextSection section in report.Measurements)
            {
                builder.Append("<h3>");
                Html(builder, section.Title);
                builder.Append("</h3><pre>");
                Html(builder, section.Content);
                builder.Append("</pre>");
            }
        }
        builder.Append("</section>");

        if (report.BrowserObservation is ReportObservationSection observation)
        {
            builder.Append("<section class=\"card\"><h2>브라우저 다운로드 관찰</h2><table>");
            Row(builder, "상태", observation.Status);
            Row(builder, "관찰 시간", Unit(observation.ObservedSeconds, "초"));
            Row(builder, "백그라운드 기준", Unit(observation.BaselineReceiveMbps, "Mbps"));
            Row(builder, "평균 / 최고", $"{Unit(observation.AverageAdjustedReceiveMbps, "Mbps")} / {Unit(observation.PeakAdjustedReceiveMbps, "Mbps")}");
            Row(builder, "수신량", FormatBytes(observation.TotalReceiveBytes));
            Row(builder, "일시 정지 / 급락", $"{Format(observation.PauseCount)} / {Format(observation.SuddenDropCount)}");
            Row(builder, "BSSID 변경", Format(observation.BssidChangeCount));
            Row(builder, "신뢰도", observation.Confidence);
            builder.Append("</table><p>");
            Html(builder, observation.Message);
            builder.Append("</p><p class=\"small\">");
            Html(builder, observation.Limitation);
            builder.Append("</p>");

            if (observation.Samples.Count > 0)
            {
                builder.Append("<h3>시간축 샘플</h3><div class=\"samples\"><table><thead><tr><th>시각</th><th>구간</th><th>수신 Mbps</th><th>RSSI</th><th>이벤트</th></tr></thead><tbody>");
                foreach (ReportObservationSample sample in observation.Samples)
                {
                    builder.Append("<tr><td>");
                    Html(builder, sample.Timestamp.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture));
                    builder.Append("</td><td>");
                    Html(builder, sample.IsBaseline ? "기준" : "관찰");
                    builder.Append("</td><td>");
                    Html(builder, Unit(sample.IsBaseline ? sample.RawReceiveMbps : sample.AdjustedReceiveMbps, "Mbps"));
                    builder.Append("</td><td>");
                    Html(builder, Unit(sample.RssiDbm, "dBm"));
                    builder.Append("</td><td>");
                    Html(builder, SampleEvents(sample));
                    builder.Append("</td></tr>");
                }
                builder.Append("</tbody></table></div>");
            }
            builder.Append("</section>");
        }

        builder.Append("<section class=\"card\"><h2>판정</h2>");
        if (report.Findings.Count == 0)
        {
            builder.Append("<p>추가 판정 항목이 없습니다.</p>");
        }
        else
        {
            foreach (ReportFinding finding in report.Findings)
            {
                string severityClass = finding.Severity.Equals("Critical", StringComparison.OrdinalIgnoreCase)
                    ? "critical"
                    : finding.Severity.Equals("Warning", StringComparison.OrdinalIgnoreCase)
                        ? "warning"
                        : "information";
                string badgeClass = severityClass == "critical"
                    ? "critical"
                    : severityClass == "warning" ? "warn" : "info";
                builder.Append("<article class=\"finding ");
                Html(builder, severityClass);
                builder.Append("\"><span class=\"badge ");
                Html(builder, badgeClass);
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
                builder.Append("</p><p class=\"small\"><strong>한계:</strong> ");
                Html(builder, finding.Limitation);
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
        builder.Append("</ul></section>");
        builder.Append("<footer class=\"small\" style=\"margin-top:18px\">이 파일은 WLAN Live Path Tester KO가 로컬에서 생성했습니다.</footer>");
        builder.Append("</main></body></html>");
        return builder.ToString();
    }

    private static string GetAvailableBaseName(string directory, string desired)
    {
        string candidate = desired;
        for (int suffix = 1; suffix <= 9999; suffix++)
        {
            bool exists = Directory.EnumerateFiles(directory, candidate + ".*", SearchOption.TopDirectoryOnly).Any();
            if (!exists)
            {
                return candidate;
            }

            candidate = $"{desired}_{suffix}";
        }

        throw new IOException("사용 가능한 보고서 파일 이름을 만들지 못했습니다.");
    }

    private static void WriteAtomic(string destination, string content, Encoding encoding)
    {
        string directory = Path.GetDirectoryName(destination)
            ?? throw new InvalidOperationException("보고서 출력 디렉터리를 확인할 수 없습니다.");
        string temporary = Path.Combine(directory, $".{Path.GetFileName(destination)}.{Guid.NewGuid():N}.tmp");
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
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static void Add(
        ICollection<(string Section, string Key, string Value)> rows,
        string section,
        string key,
        string? value) =>
        rows.Add((section, key, value ?? string.Empty));

    private static string Csv(string value)
    {
        string safe = SensitiveDataRedactor.ProtectCsvFormula(value);
        return '"' + safe.Replace("\"", "\"\"") + '"';
    }

    private static void Row(StringBuilder builder, string key, string? value)
    {
        builder.Append("<tr><th>");
        Html(builder, key);
        builder.Append("</th><td>");
        Html(builder, value ?? string.Empty);
        builder.Append("</td></tr>");
    }

    private static void Html(StringBuilder builder, string? value) =>
        builder.Append(WebUtility.HtmlEncode(value ?? string.Empty));

    private static string Unit<T>(T? value, string unit)
        where T : struct, IFormattable =>
        value.HasValue
            ? $"{value.Value.ToString(null, CultureInfo.InvariantCulture)} {unit}"
            : "확인 불가";

    private static string Format<T>(T? value)
        where T : struct, IFormattable =>
        value.HasValue
            ? value.Value.ToString(null, CultureInfo.InvariantCulture)
            : string.Empty;

    private static string Format(DateTimeOffset? value) =>
        value?.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty;

    private static string FormatBytes(long? bytes)
    {
        if (!bytes.HasValue)
        {
            return "확인 불가";
        }

        return bytes.Value >= 1024L * 1024 * 1024
            ? $"{bytes.Value / 1024d / 1024 / 1024:F2} GiB"
            : $"{bytes.Value / 1024d / 1024:F2} MiB";
    }

    private static string SampleEvents(ReportObservationSample sample)
    {
        List<string> events = [];
        if (sample.BssidChanged) events.Add("BSSID 변경");
        if (sample.AdapterChanged) events.Add("인터페이스 변경");
        if (sample.CounterReset) events.Add("카운터 재설정");
        if (sample.WlanDisconnected) events.Add("WLAN 미연결");
        if (sample.PauseDetected) events.Add("일시 정지");
        if (sample.SuddenDropDetected) events.Add("급락");
        if (!string.IsNullOrWhiteSpace(sample.Note)) events.Add(sample.Note);
        return events.Count == 0 ? "-" : string.Join(", ", events);
    }
}
