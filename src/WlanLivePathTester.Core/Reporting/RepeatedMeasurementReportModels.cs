namespace WlanLivePathTester.Core.Reporting;

public sealed record RepeatedMeasurementReportDocument(
    string SchemaVersion,
    DateTimeOffset GeneratedAt,
    string ApplicationName,
    string ApplicationVersion,
    bool SensitiveValuesIncluded,
    string DataHandlingStatement,
    IReadOnlyList<RepeatedMeasurementReportEntry> Measurements,
    IReadOnlyList<string> Limitations);

public sealed record RepeatedMeasurementReportEntry(
    string TargetName,
    string PathKind,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    int RepeatCount,
    bool IncludeWarmup,
    int DelayMilliseconds,
    int PlannedMeasurementCount,
    int CompletedMeasurementCount,
    int SuccessfulMeasurementCount,
    int FailedMeasurementCount,
    int NotCompletedMeasurementCount,
    long TotalBytesReceived,
    double? MedianMbps,
    double? MinimumMbps,
    double? MaximumMbps,
    double? MeanMbps,
    double? StandardDeviationMbps,
    double? CoefficientOfVariation,
    int? RepresentativeSequence,
    bool CacheHitPossible,
    string Confidence,
    IReadOnlyList<string> ConfidenceReasons,
    IReadOnlyList<RepeatedMeasurementReportRun> Runs);

public sealed record RepeatedMeasurementReportRun(
    int Sequence,
    bool IsWarmup,
    string Status,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    double DurationSeconds,
    long BytesReceived,
    double? AverageMbps,
    double? PeakMbps,
    double? TimeToFirstByteMilliseconds,
    int? HttpStatusCode,
    bool? ProxyWasUsed,
    int StreamsRequested,
    int StreamsCompleted,
    int RedirectCount,
    string CacheClassification,
    string Confidence,
    string? ErrorCode);

public sealed record RepeatedMeasurementReportExportResult(
    string OutputDirectory,
    string JsonPath,
    string CsvPath,
    string HtmlPath,
    string Sha256Path,
    IReadOnlyDictionary<string, string> Sha256);
