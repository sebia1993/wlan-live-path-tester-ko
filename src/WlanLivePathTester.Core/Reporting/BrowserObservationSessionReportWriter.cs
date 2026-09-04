using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using WlanLivePathTester.Core.Models;
using WlanLivePathTester.Core.Observation;

namespace WlanLivePathTester.Core.Reporting;

public sealed record BrowserObservationSessionReportDocument(
    string SchemaVersion,
    DateTimeOffset GeneratedAt,
    string ApplicationName,
    string ApplicationVersion,
    bool SensitiveValuesIncluded,
    string DataHandlingStatement,
    string Status,
    string TerminationReason,
    string TerminationDisplay,
    string Message,
    BrowserObservationSessionReportSummary? Summary,
    IReadOnlyList<string> Limitations);

public sealed record BrowserObservationSessionReportSummary(
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    double ObservedSeconds,
    double BaselineReceiveMbps,
    double? AverageAdjustedReceiveMbps,
    double? PeakAdjustedReceiveMbps,
    long TotalReceiveBytes,
    int ActiveSampleCount,
    int PauseCount,
    int SuddenDropCount,
    int BssidChangeCount,
    int AdapterChangeCount,
    int CounterResetCount,
    int WlanDisconnectedSampleCount,
    string Confidence,
    string SummaryMessage,
    string Limitation,
    IReadOnlyList<BrowserObservationSessionReportSample> Samples);

public sealed record BrowserObservationSessionReportSample(
    int Sequence,
    DateTimeOffset Timestamp,
    double IntervalSeconds,
    bool IsBaseline,
    long ReceiveBytesDelta,
    long TransmitBytesDelta,
    double? RawReceiveMbps,
    double? RawTransmitMbps,
    double? AdjustedReceiveMbps,
    int? RssiDbm,
    double? ReceiveLinkMbps,
    double? TransmitLinkMbps,
    bool InvalidInterval,
    bool AdapterChanged,
    bool CounterReset,
    bool WlanDisconnected,
    bool BssidChanged,
    bool PauseDetected,
    bool SuddenDropDetected,
    string? Note);

public sealed record BrowserObservationSessionReportExportResult(
    string OutputDirectory,
    string JsonPath,
    string CsvPath,
    string HtmlPath,
    string Sha256Path,
    IReadOnlyDictionary<string, string> Sha256);

public static class BrowserObservationSessionReportWriter
{
    private const string DefaultFilePrefix = "WlanBrowserObservation";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static readonly Regex GuidRegex = new(
        @"(?i)(?<![0-9a-f])\{?[0-9a-f]{8}-(?:[0-9a-f]{4}-){3}[0-9a-f]{12}\}?(?![0-9a-f])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static BrowserObservationSessionReportDocument CreateDocument(
        BrowserObservationResult result,
        string applicationVersion,
        DateTimeOffset? generatedAt = null)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationVersion);

        BrowserObservationTerminationReason terminationReason =
            result.EffectiveTerminationReason;
        WlanSnapshot? initialWlan = result.InitialWlan;

        return new BrowserObservationSessionReportDocument(
            SchemaVersion: "1.1",
            GeneratedAt: generatedAt ?? DateTimeOffset.UtcNow,
            ApplicationName: "WLAN Live Path Tester KO",
            ApplicationVersion: RedactObservationText(
                applicationVersion,
                initialWlan),
            SensitiveValuesIncluded: false,
            DataHandlingStatement:
                "브라우저 관찰 보고서는 현재 PC에서 생성되며 자동 업로드, 텔레메트리 또는 온라인 분석을 수행하지 않습니다.",
            Status: result.Status.ToString(),
            TerminationReason: terminationReason.ToString(),
            TerminationDisplay:
                BrowserObservationTerminationPolicy.ToDisplayText(
                    terminationReason),
            Message: RedactObservationText(result.Message, initialWlan),
            Summary: result.Summary is null
                ? null
                : MapSummary(result.Summary, initialWlan),
            Limitations:
            [
                "관찰값은 브라우저 프로세스 한 개가 아니라 시작 시 고정한 물리 Wi-Fi 인터페이스 전체 수신·송신 카운터입니다.",
                "다른 프로그램의 통신이 포함될 수 있으므로 관찰 신뢰도는 최대 Medium입니다.",
                "같은 물리 Wi-Fi에서 BSSID가 바뀌는 로밍은 인터페이스 변경과 구분합니다.",
                "SSID, BSSID, 인터페이스 ID, 인터페이스 이름·설명, IP, MAC, 게이트웨이, DNS와 다운로드 URL은 보고서에 포함하지 않습니다.",
                "외부 다운로드 결과는 회사 프록시, 보안 정책, 인터넷 회선과 외부 사이트 또는 CDN의 영향을 받을 수 있습니다.",
                "Completed는 관찰 절차 완료를 뜻하며 WLAN 또는 서비스 품질 정상 판정을 뜻하지 않습니다."
            ]);
    }

    public static BrowserObservationSessionReportExportResult WriteAll(
        BrowserObservationSessionReportDocument report,
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

        string jsonPath = Path.Combine(fullDirectory, baseName + ".json");
        string csvPath = Path.Combine(fullDirectory, baseName + ".csv");
        string htmlPath = Path.Combine(fullDirectory, baseName + ".html");
        string sha256Path = Path.Combine(
            fullDirectory,
            baseName + "_SHA256SUMS.txt");

        WriteAtomic(jsonPath, RenderJson(report), new UTF8Encoding(false));
        WriteAtomic(csvPath, RenderCsv(report), new UTF8Encoding(true));
        WriteAtomic(htmlPath, RenderHtml(report), new UTF8Encoding(false));

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
        WriteAtomic(sha256Path, checksumText, new UTF8Encoding(false));

        return new BrowserObservationSessionReportExportResult(
            OutputDirectory: fullDirectory,
            JsonPath: jsonPath,
            CsvPath: csvPath,
            HtmlPath: htmlPath,
            Sha256Path: sha256Path,
            Sha256: hashes);
    }

    public static string RenderJson(
        BrowserObservationSessionReportDocument report)
    {
        ArgumentNullException.ThrowIfNull(report);
        return JsonSerializer.Serialize(report, JsonOptions)
            + Environment.NewLine;
    }

    public static string RenderCsv(
        BrowserObservationSessionReportDocument report)
    {
        ArgumentNullException.ThrowIfNull(report);

        StringBuilder builder = new();
        builder.AppendLine("section,key,value");
        AddCsv(builder, "metadata", "schemaVersion", report.SchemaVersion);
        AddCsv(builder, "metadata", "generatedAt", Iso(report.GeneratedAt));
        AddCsv(builder, "metadata", "applicationName", report.ApplicationName);
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
        AddCsv(builder, "observation", "status", report.Status);
        AddCsv(
            builder,
            "observation",
            "terminationReason",
            report.TerminationReason);
        AddCsv(
            builder,
            "observation",
            "terminationDisplay",
            report.TerminationDisplay);
        AddCsv(builder, "observation", "message", report.Message);

        if (report.Summary is BrowserObservationSessionReportSummary summary)
        {
            AddSummaryCsv(builder, summary);
            foreach (BrowserObservationSessionReportSample sample
                     in summary.Samples)
            {
                AddSampleCsv(builder, sample);
            }
        }
        else
        {
            AddCsv(builder, "summary", "available", "false");
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
        BrowserObservationSessionReportDocument report)
    {
        ArgumentNullException.ThrowIfNull(report);

        StringBuilder builder = new(capacity: 36 * 1024);
        builder.Append(
            "<!doctype html><html lang=\"ko\"><head><meta charset=\"utf-8\">");
        builder.Append(
            "<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">");
        builder.Append(
            "<meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'none'; style-src 'unsafe-inline'; base-uri 'none'; form-action 'none'; frame-ancestors 'none'\">");
        builder.Append(
            "<title>브라우저 다운로드 관찰 보고서</title><style>");
        builder.Append(
            "body{margin:0;background:#f4f6f8;color:#17202a;font:14px/1.55 system-ui,-apple-system,'Segoe UI',sans-serif}main{max-width:1180px;margin:auto;padding:28px}h1{font-size:28px;margin:0}h2{font-size:19px;margin:0 0 12px}.sub,.small{color:#566573}.card{background:#fff;border:1px solid #d8dde3;border-radius:12px;padding:18px;margin-top:16px;box-shadow:0 1px 2px rgba(0,0,0,.03)}.grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(190px,1fr));gap:10px}.metric{background:#f8fafb;border-radius:8px;padding:12px}.metric strong{display:block;font-size:18px}.badge{display:inline-block;border-radius:999px;padding:3px 9px;font-size:12px;background:#eaf2f8;color:#1b4f72}.ok{background:#e8f6f3;color:#0e6251}.warn{background:#fff3cd;color:#7d6608}.bad{background:#fdecea;color:#922b21}.privacy{background:#fff8e7;border-color:#e8ce8a}table{width:100%;border-collapse:collapse}th,td{padding:8px;border-bottom:1px solid #e8ebed;text-align:left;vertical-align:top}.scroll{overflow:auto}.nowrap{white-space:nowrap}@media(max-width:640px){main{padding:16px}.grid{display:block}.metric{margin-top:8px}}@media print{body{background:#fff}.card{box-shadow:none;break-inside:avoid}main{max-width:none;padding:0}}</style></head><body><main>");
        builder.Append(
            "<header><h1>브라우저 다운로드 관찰 보고서</h1><div class=\"sub\">");
        Html(
            builder,
            report.GeneratedAt.ToLocalTime()
                .ToString(
                    "yyyy-MM-dd HH:mm:ss zzz",
                    CultureInfo.InvariantCulture));
        builder.Append(" · 스키마 ");
        Html(builder, report.SchemaVersion);
        builder.Append(
            "</div></header><section class=\"card privacy\"><h2>데이터 처리</h2><p>");
        Html(builder, report.DataHandlingStatement);
        builder.Append(
            "</p><p class=\"small\">SSID·BSSID·인터페이스 ID·이름·설명·IP·MAC·게이트웨이·DNS·URL은 포함하지 않습니다. 이 HTML은 외부 리소스와 스크립트를 사용하지 않습니다.</p></section>");

        builder.Append(
            "<section class=\"card\"><h2>종료 상태</h2><p><span class=\"badge ");
        Html(builder, TerminationCss(report.TerminationReason));
        builder.Append("\">");
        Html(builder, report.TerminationDisplay);
        builder.Append(" (");
        Html(builder, report.TerminationReason);
        builder.Append("</span> <span class=\"badge\">");
        Html(builder, report.Status);
        builder.Append("</span></p><p>");
        Html(builder, report.Message);
        builder.Append("</p></section>");

        if (report.Summary is BrowserObservationSessionReportSummary summary)
        {
            AppendSummaryHtml(builder, summary);
            AppendSamplesHtml(builder, summary.Samples);
        }
        else
        {
            builder.Append(
                "<section class=\"card\"><h2>관찰 요약</h2><p>저장된 관찰 샘플이 없습니다.</p></section>");
        }

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

    private static BrowserObservationSessionReportSummary MapSummary(
        BrowserObservationSummary summary,
        WlanSnapshot? initialWlan) =>
        new(
            StartedAt: summary.StartedAt,
            CompletedAt: summary.CompletedAt,
            ObservedSeconds: FiniteOrZero(
                summary.ObservedDuration.TotalSeconds),
            BaselineReceiveMbps: FiniteOrZero(
                summary.BaselineReceiveMbps),
            AverageAdjustedReceiveMbps: FiniteOrNull(
                summary.AverageAdjustedReceiveMbps),
            PeakAdjustedReceiveMbps: FiniteOrNull(
                summary.PeakAdjustedReceiveMbps),
            TotalReceiveBytes: Math.Max(0, summary.TotalReceiveBytes),
            ActiveSampleCount: Math.Max(0, summary.ActiveSampleCount),
            PauseCount: Math.Max(0, summary.PauseCount),
            SuddenDropCount: Math.Max(0, summary.SuddenDropCount),
            BssidChangeCount: Math.Max(0, summary.BssidChangeCount),
            AdapterChangeCount: Math.Max(0, summary.AdapterChangeCount),
            CounterResetCount: Math.Max(0, summary.CounterResetCount),
            WlanDisconnectedSampleCount: Math.Max(
                0,
                summary.WlanDisconnectedSampleCount),
            Confidence: summary.Confidence.ToString(),
            SummaryMessage: RedactObservationText(
                summary.Message,
                initialWlan),
            Limitation: RedactObservationText(
                summary.Limitation,
                initialWlan),
            Samples: summary.Samples
                .Select((sample, index) =>
                    MapSample(sample, index + 1, initialWlan))
                .ToArray());

    private static BrowserObservationSessionReportSample MapSample(
        BrowserObservationSample sample,
        int sequence,
        WlanSnapshot? initialWlan) =>
        new(
            Sequence: sequence,
            Timestamp: sample.Timestamp,
            IntervalSeconds: FiniteOrZero(
                Math.Max(0, sample.Interval.TotalSeconds)),
            IsBaseline: sample.IsBaseline,
            ReceiveBytesDelta: Math.Max(0, sample.ReceiveBytesDelta),
            TransmitBytesDelta: Math.Max(0, sample.TransmitBytesDelta),
            RawReceiveMbps: FiniteOrNull(sample.RawReceiveMbps),
            RawTransmitMbps: FiniteOrNull(sample.RawTransmitMbps),
            AdjustedReceiveMbps: FiniteOrNull(
                sample.AdjustedReceiveMbps),
            RssiDbm: sample.RssiDbm,
            ReceiveLinkMbps: ToMbps(sample.ReceiveLinkSpeedBps),
            TransmitLinkMbps: ToMbps(sample.TransmitLinkSpeedBps),
            InvalidInterval: sample.InvalidInterval,
            AdapterChanged: sample.AdapterChanged,
            CounterReset: sample.CounterReset,
            WlanDisconnected: sample.WlanDisconnected,
            BssidChanged: sample.BssidChanged,
            PauseDetected: sample.PauseDetected,
            SuddenDropDetected: sample.SuddenDropDetected,
            Note: RedactObservationTextOrNull(
                sample.Note,
                initialWlan));

    private static void AddSummaryCsv(
        StringBuilder builder,
        BrowserObservationSessionReportSummary summary)
    {
        AddCsv(builder, "summary", "available", "true");
        AddCsv(builder, "summary", "startedAt", Iso(summary.StartedAt));
        AddCsv(builder, "summary", "completedAt", Iso(summary.CompletedAt));
        AddCsv(
            builder,
            "summary",
            "observedSeconds",
            Invariant(summary.ObservedSeconds));
        AddCsv(
            builder,
            "summary",
            "baselineReceiveMbps",
            Invariant(summary.BaselineReceiveMbps));
        AddCsv(
            builder,
            "summary",
            "averageAdjustedReceiveMbps",
            Invariant(summary.AverageAdjustedReceiveMbps));
        AddCsv(
            builder,
            "summary",
            "peakAdjustedReceiveMbps",
            Invariant(summary.PeakAdjustedReceiveMbps));
        AddCsv(
            builder,
            "summary",
            "totalReceiveBytes",
            Invariant(summary.TotalReceiveBytes));
        AddCsv(
            builder,
            "summary",
            "activeSampleCount",
            Invariant(summary.ActiveSampleCount));
        AddCsv(
            builder,
            "summary",
            "pauseCount",
            Invariant(summary.PauseCount));
        AddCsv(
            builder,
            "summary",
            "suddenDropCount",
            Invariant(summary.SuddenDropCount));
        AddCsv(
            builder,
            "summary",
            "bssidChangeCount",
            Invariant(summary.BssidChangeCount));
        AddCsv(
            builder,
            "summary",
            "adapterChangeCount",
            Invariant(summary.AdapterChangeCount));
        AddCsv(
            builder,
            "summary",
            "counterResetCount",
            Invariant(summary.CounterResetCount));
        AddCsv(
            builder,
            "summary",
            "wlanDisconnectedSampleCount",
            Invariant(summary.WlanDisconnectedSampleCount));
        AddCsv(builder, "summary", "confidence", summary.Confidence);
        AddCsv(builder, "summary", "message", summary.SummaryMessage);
        AddCsv(builder, "summary", "limitation", summary.Limitation);
    }

    private static void AddSampleCsv(
        StringBuilder builder,
        BrowserObservationSessionReportSample sample)
    {
        string section = $"sample.{sample.Sequence}";
        AddCsv(builder, section, "timestamp", Iso(sample.Timestamp));
        AddCsv(
            builder,
            section,
            "intervalSeconds",
            Invariant(sample.IntervalSeconds));
        AddCsv(builder, section, "isBaseline", Invariant(sample.IsBaseline));
        AddCsv(
            builder,
            section,
            "receiveBytesDelta",
            Invariant(sample.ReceiveBytesDelta));
        AddCsv(
            builder,
            section,
            "transmitBytesDelta",
            Invariant(sample.TransmitBytesDelta));
        AddCsv(
            builder,
            section,
            "rawReceiveMbps",
            Invariant(sample.RawReceiveMbps));
        AddCsv(
            builder,
            section,
            "rawTransmitMbps",
            Invariant(sample.RawTransmitMbps));
        AddCsv(
            builder,
            section,
            "adjustedReceiveMbps",
            Invariant(sample.AdjustedReceiveMbps));
        AddCsv(builder, section, "rssiDbm", Invariant(sample.RssiDbm));
        AddCsv(
            builder,
            section,
            "receiveLinkMbps",
            Invariant(sample.ReceiveLinkMbps));
        AddCsv(
            builder,
            section,
            "transmitLinkMbps",
            Invariant(sample.TransmitLinkMbps));
        AddCsv(
            builder,
            section,
            "invalidInterval",
            Invariant(sample.InvalidInterval));
        AddCsv(
            builder,
            section,
            "adapterChanged",
            Invariant(sample.AdapterChanged));
        AddCsv(
            builder,
            section,
            "counterReset",
            Invariant(sample.CounterReset));
        AddCsv(
            builder,
            section,
            "wlanDisconnected",
            Invariant(sample.WlanDisconnected));
        AddCsv(
            builder,
            section,
            "bssidChanged",
            Invariant(sample.BssidChanged));
        AddCsv(
            builder,
            section,
            "pauseDetected",
            Invariant(sample.PauseDetected));
        AddCsv(
            builder,
            section,
            "suddenDropDetected",
            Invariant(sample.SuddenDropDetected));
        AddCsv(builder, section, "note", sample.Note ?? string.Empty);
    }

    private static void AppendSummaryHtml(
        StringBuilder builder,
        BrowserObservationSessionReportSummary summary)
    {
        builder.Append(
            "<section class=\"card\"><h2>관찰 요약</h2><div class=\"grid\">");
        Metric(builder, "관찰 시간", $"{summary.ObservedSeconds:F1}초");
        Metric(
            builder,
            "기준 수신",
            Mbps(summary.BaselineReceiveMbps));
        Metric(
            builder,
            "조정 평균",
            Mbps(summary.AverageAdjustedReceiveMbps));
        Metric(
            builder,
            "조정 최고",
            Mbps(summary.PeakAdjustedReceiveMbps));
        Metric(
            builder,
            "총 수신량",
            Bytes(summary.TotalReceiveBytes));
        Metric(
            builder,
            "활성 샘플",
            summary.ActiveSampleCount.ToString(
                CultureInfo.InvariantCulture));
        Metric(
            builder,
            "BSSID 변경",
            summary.BssidChangeCount.ToString(
                CultureInfo.InvariantCulture));
        Metric(
            builder,
            "NIC 변경",
            summary.AdapterChangeCount.ToString(
                CultureInfo.InvariantCulture));
        Metric(
            builder,
            "카운터 재설정",
            summary.CounterResetCount.ToString(
                CultureInfo.InvariantCulture));
        Metric(
            builder,
            "WLAN 미확인",
            summary.WlanDisconnectedSampleCount.ToString(
                CultureInfo.InvariantCulture));
        Metric(
            builder,
            "정지 / 급락",
            $"{summary.PauseCount} / {summary.SuddenDropCount}");
        Metric(builder, "신뢰도", summary.Confidence);
        builder.Append("</div><p class=\"small\">");
        Html(builder, summary.SummaryMessage);
        builder.Append("</p><p class=\"small\"><strong>한계:</strong> ");
        Html(builder, summary.Limitation);
        builder.Append("</p></section>");
    }

    private static void AppendSamplesHtml(
        StringBuilder builder,
        IReadOnlyList<BrowserObservationSessionReportSample> samples)
    {
        builder.Append(
            "<section class=\"card\"><h2>시간축 샘플</h2><div class=\"scroll\"><table><thead><tr><th>#</th><th>시각</th><th>구분</th><th>조정 Rx</th><th>원시 Rx</th><th>Tx</th><th>RSSI</th><th>PHY Rx/Tx</th><th>상태</th></tr></thead><tbody>");
        foreach (BrowserObservationSessionReportSample sample in samples)
        {
            builder.Append("<tr><td>");
            Html(
                builder,
                sample.Sequence.ToString(CultureInfo.InvariantCulture));
            builder.Append("</td><td class=\"nowrap\">");
            Html(
                builder,
                sample.Timestamp.ToLocalTime()
                    .ToString(
                        "HH:mm:ss.fff",
                        CultureInfo.InvariantCulture));
            builder.Append("</td><td>");
            Html(builder, sample.IsBaseline ? "기준" : "관찰");
            builder.Append("</td><td>");
            Html(builder, Mbps(sample.AdjustedReceiveMbps));
            builder.Append("</td><td>");
            Html(builder, Mbps(sample.RawReceiveMbps));
            builder.Append("</td><td>");
            Html(builder, Mbps(sample.RawTransmitMbps));
            builder.Append("</td><td>");
            Html(
                builder,
                sample.RssiDbm?.ToString(CultureInfo.InvariantCulture)
                ?? "-");
            builder.Append("</td><td class=\"nowrap\">");
            Html(
                builder,
                $"{Mbps(sample.ReceiveLinkMbps)} / {Mbps(sample.TransmitLinkMbps)}");
            builder.Append("</td><td>");
            Html(builder, FormatFlags(sample));
            builder.Append("</td></tr>");
        }

        builder.Append("</tbody></table></div></section>");
    }

    private static string FormatFlags(
        BrowserObservationSessionReportSample sample)
    {
        List<string> flags = [];
        if (sample.InvalidInterval)
        {
            flags.Add("간격 오류");
        }

        if (sample.AdapterChanged)
        {
            flags.Add("NIC 변경");
        }

        if (sample.CounterReset)
        {
            flags.Add("카운터 재설정");
        }

        if (sample.WlanDisconnected)
        {
            flags.Add("WLAN 미확인");
        }

        if (sample.BssidChanged)
        {
            flags.Add("BSSID 변경");
        }

        if (sample.PauseDetected)
        {
            flags.Add("정지");
        }

        if (sample.SuddenDropDetected)
        {
            flags.Add("급락");
        }

        if (!string.IsNullOrWhiteSpace(sample.Note))
        {
            flags.Add(sample.Note);
        }

        return flags.Count == 0
            ? "정상"
            : string.Join(" · ", flags);
    }

    private static string TerminationCss(string reason) =>
        reason.Equals(
            BrowserObservationTerminationReason.Completed.ToString(),
            StringComparison.OrdinalIgnoreCase)
            ? "ok"
            : reason.Equals(
                    BrowserObservationTerminationReason.CanceledByUser
                        .ToString(),
                    StringComparison.OrdinalIgnoreCase)
                ? "warn"
                : "bad";

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

    private static string RedactObservationText(
        string? value,
        WlanSnapshot? initialWlan)
    {
        string redacted = value ?? string.Empty;
        redacted = ReplaceSensitiveValue(
            redacted,
            initialWlan?.InterfaceId,
            "[인터페이스 ID 마스킹됨]");
        redacted = ReplaceSensitiveValue(
            redacted,
            initialWlan?.InterfaceDescription,
            "[인터페이스 설명 마스킹됨]");
        redacted = ReplaceSensitiveValue(
            redacted,
            initialWlan?.Ssid,
            "[SSID 마스킹됨]");
        redacted = ReplaceSensitiveValue(
            redacted,
            initialWlan?.Bssid,
            "[BSSID 마스킹됨]");
        redacted = GuidRegex.Replace(
            redacted,
            "[인터페이스 ID 마스킹됨]");
        return SensitiveDataRedactor.RedactText(redacted)
            ?? string.Empty;
    }

    private static string? RedactObservationTextOrNull(
        string? value,
        WlanSnapshot? initialWlan)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string redacted = RedactObservationText(value, initialWlan);
        return string.IsNullOrWhiteSpace(redacted)
            ? null
            : redacted;
    }

    private static string ReplaceSensitiveValue(
        string source,
        string? value,
        string replacement)
    {
        string candidate = (value ?? string.Empty).Trim();
        if (candidate.Length < 4)
        {
            return source;
        }

        return source.Replace(
            candidate,
            replacement,
            StringComparison.OrdinalIgnoreCase);
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
                formattable.ToString(
                    null,
                    CultureInfo.InvariantCulture),
            _ => Convert.ToString(
                    value,
                    CultureInfo.InvariantCulture)
                ?? string.Empty
        };

    private static string Iso(DateTimeOffset value) =>
        value.ToString("O", CultureInfo.InvariantCulture);

    private static double FiniteOrZero(double value) =>
        double.IsFinite(value) ? Math.Max(0, value) : 0;

    private static double? FiniteOrNull(double? value) =>
        value.HasValue && double.IsFinite(value.Value)
            ? Math.Max(0, value.Value)
            : null;

    private static double? ToMbps(ulong? bitsPerSecond) =>
        bitsPerSecond.HasValue
            ? FiniteOrNull(bitsPerSecond.Value / 1_000_000d)
            : null;

    private static string Mbps(double value) =>
        $"{value:F1} Mbps";

    private static string Mbps(double? value) =>
        value.HasValue
            ? Mbps(value.Value)
            : "계산 안 함";

    private static string Bytes(long bytes) =>
        bytes >= 1024L * 1024 * 1024
            ? $"{bytes / 1024d / 1024 / 1024:F2} GiB"
            : $"{bytes / 1024d / 1024:F2} MiB";

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
            "사용 가능한 브라우저 관찰 보고서 파일 이름을 만들지 못했습니다.");
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
