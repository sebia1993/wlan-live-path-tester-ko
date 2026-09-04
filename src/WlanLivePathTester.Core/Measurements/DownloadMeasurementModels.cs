using WlanLivePathTester.Core.Models;

namespace WlanLivePathTester.Core.Measurements;

public sealed record ThroughputSample(
    int StreamIndex,
    TimeSpan Offset,
    long IntervalBytes,
    double Mbps);

public sealed record DownloadMeasurementResult(
    string TargetName,
    NetworkPathKind PathKind,
    MeasurementStatus Status,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    long BytesReceived,
    double? AverageMbps,
    TimeSpan? TimeToFirstByte,
    int? HttpStatusCode,
    bool? ProxyWasUsed,
    int StreamsRequested,
    int StreamsCompleted,
    int RedirectCount,
    string FinalUrl,
    IReadOnlyList<ThroughputSample> Samples,
    IReadOnlyDictionary<string, string> ResponseHeaders,
    string? ErrorCode,
    string Message)
{
    public TimeSpan Duration => CompletedAt - StartedAt;

    public bool IsSuccess =>
        Status is MeasurementStatus.Success or MeasurementStatus.PartialSuccess;

    public double? PeakMbps => Samples.Count == 0
        ? null
        : Samples.Max(sample => sample.Mbps);

    public DownloadMeasurement ToDiagnosisInput() => new(
        TargetName,
        PathKind,
        Status,
        BytesReceived,
        Duration,
        AverageMbps,
        HttpStatusCode,
        ProxyWasUsed,
        ErrorCode);
}
