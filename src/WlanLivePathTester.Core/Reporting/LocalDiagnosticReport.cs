using WlanLivePathTester.Core.Measurements;
using WlanLivePathTester.Core.Observation;

namespace WlanLivePathTester.Core.Reporting;

public sealed record LocalDiagnosticReport(
    string SchemaVersion,
    ReportMetadata Metadata,
    ReportWlanSection Wlan,
    ReportProxySection Proxy,
    IReadOnlyList<ReportTextSection> Measurements,
    ReportObservationSection? BrowserObservation,
    IReadOnlyList<ReportFinding> Findings,
    IReadOnlyList<string> Limitations,
    IReadOnlyList<ReportMeasurementSection>? StructuredMeasurements = null);

public sealed record ReportMetadata(
    DateTimeOffset GeneratedAt,
    string ApplicationName,
    string ApplicationVersion,
    string OperatingSystem,
    string RuntimeVersion,
    string Culture,
    bool SensitiveValuesIncluded,
    string DataHandlingStatement);

public sealed record ReportWlanSection(
    DateTimeOffset CapturedAt,
    bool IsConnected,
    string InterfaceDescription,
    string InterfaceState,
    string Ssid,
    string Bssid,
    int? RssiDbm,
    int? SignalQualityPercent,
    uint? Channel,
    uint? CenterFrequencyMhz,
    string Band,
    string PhyType,
    double? ReceiveLinkMbps,
    double? TransmitLinkMbps,
    string Authentication,
    string Cipher,
    string? ReadError);

public sealed record ReportProxySection(
    bool ReadSucceeded,
    string Mode,
    bool AutoDetectEnabled,
    bool PacConfigured,
    bool ManualProxyConfigured,
    bool BypassConfigured,
    int? Win32Error,
    string Statement);

public sealed record ReportTextSection(
    string SectionId,
    string Title,
    string Content,
    DateTimeOffset CapturedAt);

public sealed record ReportMeasurementSection(
    string TargetName,
    string PathKind,
    string Status,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    double? DurationSeconds,
    long BytesReceived,
    double? AverageMbps,
    double? PeakMbps,
    double? TimeToFirstByteMilliseconds,
    int? HttpStatusCode,
    bool? ProxyWasUsed,
    int StreamsRequested,
    int StreamsCompleted,
    int RedirectCount,
    string FinalUrl,
    string CacheClassification,
    string Confidence,
    IReadOnlyList<string> ConfidenceReasons,
    string? ErrorCode,
    string Message,
    IReadOnlyDictionary<string, string> ResponseMetadata,
    IReadOnlyList<ReportThroughputSample> Samples);

public sealed record ReportThroughputSample(
    int StreamIndex,
    double OffsetSeconds,
    long IntervalBytes,
    double Mbps);

public sealed record ReportObservationSection(
    string Status,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    double? ObservedSeconds,
    double? BaselineReceiveMbps,
    double? AverageAdjustedReceiveMbps,
    double? PeakAdjustedReceiveMbps,
    long? TotalReceiveBytes,
    int? ActiveSampleCount,
    int? PauseCount,
    int? SuddenDropCount,
    int? BssidChangeCount,
    int? AdapterChangeCount,
    int? CounterResetCount,
    int? WlanDisconnectedSampleCount,
    string Confidence,
    string Message,
    string Limitation,
    IReadOnlyList<ReportObservationSample> Samples)
{
    public string? TerminationReason
    {
        get;
        init;
    }
}

public sealed record ReportObservationSample(
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
    bool BssidChanged,
    bool AdapterChanged,
    bool CounterReset,
    bool WlanDisconnected,
    bool PauseDetected,
    bool SuddenDropDetected,
    string? Note);

public sealed record ReportFinding(
    string Code,
    string Severity,
    string Title,
    string Evidence,
    string Interpretation,
    string Limitation,
    string NextStep);

public sealed record LocalReportExportResult(
    string OutputDirectory,
    string JsonPath,
    string CsvPath,
    string HtmlPath,
    string Sha256Path,
    IReadOnlyDictionary<string, string> Sha256);

public static class ReportMeasurementMapper
{
    private static readonly string[] IncludedHeaderNames =
    [
        "Age",
        "Cache-Status",
        "X-Cache",
        "Content-Length",
        "Content-Range",
        "Via"
    ];

    public static ReportMeasurementSection FromResult(
        DownloadMeasurementResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        MeasurementQualityAssessment quality =
            MeasurementQualityEvaluator.Evaluate(result);
        Dictionary<string, string> metadata =
            new(StringComparer.OrdinalIgnoreCase);

        foreach (string name in IncludedHeaderNames)
        {
            if (!result.ResponseHeaders.TryGetValue(name, out string? value)
                || string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            metadata[name] = name.Equals("Via", StringComparison.OrdinalIgnoreCase)
                ? "[설정됨]"
                : SensitiveDataRedactor.RedactText(value.Trim()) ?? string.Empty;
        }

        return new ReportMeasurementSection(
            TargetName: SensitiveDataRedactor.RedactText(result.TargetName)
                ?? "측정 대상",
            PathKind: result.PathKind.ToString(),
            Status: result.Status.ToString(),
            StartedAt: result.StartedAt,
            CompletedAt: result.CompletedAt,
            DurationSeconds: Math.Max(0, result.Duration.TotalSeconds),
            BytesReceived: result.BytesReceived,
            AverageMbps: result.AverageMbps,
            PeakMbps: result.PeakMbps,
            TimeToFirstByteMilliseconds: result.TimeToFirstByte?.TotalMilliseconds,
            HttpStatusCode: result.HttpStatusCode,
            ProxyWasUsed: result.ProxyWasUsed,
            StreamsRequested: result.StreamsRequested,
            StreamsCompleted: result.StreamsCompleted,
            RedirectCount: result.RedirectCount,
            FinalUrl: SensitiveDataRedactor.RedactUrl(result.FinalUrl),
            CacheClassification: quality.CacheClassification.ToString(),
            Confidence: quality.Confidence.ToString(),
            ConfidenceReasons: quality.Reasons
                .Select(reason => SensitiveDataRedactor.RedactText(reason)
                    ?? string.Empty)
                .ToArray(),
            ErrorCode: SensitiveDataRedactor.RedactText(result.ErrorCode),
            Message: SensitiveDataRedactor.RedactText(result.Message)
                ?? string.Empty,
            ResponseMetadata: metadata,
            Samples: result.Samples
                .Select(sample => new ReportThroughputSample(
                    StreamIndex: sample.StreamIndex,
                    OffsetSeconds: sample.Offset.TotalSeconds,
                    IntervalBytes: sample.IntervalBytes,
                    Mbps: sample.Mbps))
                .ToArray());
    }
}

public static class ReportObservationMapper
{
    public static ReportObservationSection? FromResult(
        BrowserObservationResult? result)
    {
        if (result is null)
        {
            return null;
        }

        BrowserObservationSummary? summary = result.Summary;
        IReadOnlyList<ReportObservationSample> samples = summary?.Samples
            .Select(sample => new ReportObservationSample(
                Timestamp: sample.Timestamp,
                IntervalSeconds: sample.Interval.TotalSeconds,
                IsBaseline: sample.IsBaseline,
                ReceiveBytesDelta: sample.ReceiveBytesDelta,
                TransmitBytesDelta: sample.TransmitBytesDelta,
                RawReceiveMbps: sample.RawReceiveMbps,
                RawTransmitMbps: sample.RawTransmitMbps,
                AdjustedReceiveMbps: sample.AdjustedReceiveMbps,
                RssiDbm: sample.RssiDbm,
                ReceiveLinkMbps: ToMbps(sample.ReceiveLinkSpeedBps),
                TransmitLinkMbps: ToMbps(sample.TransmitLinkSpeedBps),
                BssidChanged: sample.BssidChanged,
                AdapterChanged: sample.AdapterChanged,
                CounterReset: sample.CounterReset,
                WlanDisconnected: sample.WlanDisconnected,
                PauseDetected: sample.PauseDetected,
                SuddenDropDetected: sample.SuddenDropDetected,
                Note: SensitiveDataRedactor.RedactText(sample.Note)))
            .ToArray()
            ?? Array.Empty<ReportObservationSample>();

        BrowserObservationTerminationReason terminationReason =
            result.EffectiveTerminationReason;
        string? terminationReasonValue = terminationReason
            == BrowserObservationTerminationReason.None
                ? null
                : terminationReason.ToString();
        string redactedMessage =
            SensitiveDataRedactor.RedactText(result.Message)
            ?? string.Empty;
        string reportMessage = AppendTerminationDisplay(
            redactedMessage,
            terminationReason);

        return new ReportObservationSection(
            Status: result.Status.ToString(),
            StartedAt: summary?.StartedAt,
            CompletedAt: summary?.CompletedAt,
            ObservedSeconds: summary?.ObservedDuration.TotalSeconds,
            BaselineReceiveMbps: summary?.BaselineReceiveMbps,
            AverageAdjustedReceiveMbps: summary?.AverageAdjustedReceiveMbps,
            PeakAdjustedReceiveMbps: summary?.PeakAdjustedReceiveMbps,
            TotalReceiveBytes: summary?.TotalReceiveBytes,
            ActiveSampleCount: summary?.ActiveSampleCount,
            PauseCount: summary?.PauseCount,
            SuddenDropCount: summary?.SuddenDropCount,
            BssidChangeCount: summary?.BssidChangeCount,
            AdapterChangeCount: summary?.AdapterChangeCount,
            CounterResetCount: summary?.CounterResetCount,
            WlanDisconnectedSampleCount: summary?.WlanDisconnectedSampleCount,
            Confidence: summary?.Confidence.ToString() ?? "Unknown",
            Message: reportMessage,
            Limitation: SensitiveDataRedactor.RedactText(summary?.Limitation)
                ?? "Wi-Fi 인터페이스 전체 트래픽이므로 다른 프로그램의 통신이 포함될 수 있습니다.",
            Samples: samples)
        {
            TerminationReason = terminationReasonValue
        };
    }

    private static string AppendTerminationDisplay(
        string message,
        BrowserObservationTerminationReason reason)
    {
        if (reason == BrowserObservationTerminationReason.None)
        {
            return message;
        }

        string termination =
            $"종료 원인: {BrowserObservationTerminationPolicy.ToDisplayText(reason)} ({reason})";
        if (message.Contains(termination, StringComparison.Ordinal))
        {
            return message;
        }

        return string.IsNullOrWhiteSpace(message)
            ? termination
            : message.TrimEnd() + " " + termination;
    }

    private static double? ToMbps(ulong? bitsPerSecond) =>
        bitsPerSecond.HasValue ? bitsPerSecond.Value / 1_000_000d : null;
}
