using WlanLivePathTester.Core.Models;

namespace WlanLivePathTester.Core.Observation;

public enum InterfaceCounterReadStatus
{
    Success,
    UnsupportedPlatform,
    InterfaceNotFound,
    PreferredInterfaceNotFound,
    InterfaceNotOperational,
    InterfaceAmbiguous,
    StatisticsUnavailable,
    CounterProviderMismatch,
    Failed
}

public sealed record InterfaceCounterSnapshot(
    DateTimeOffset Timestamp,
    string InterfaceId,
    string InterfaceName,
    string InterfaceDescription,
    long BytesReceived,
    long BytesSent,
    bool IsOperational);

public sealed record InterfaceCounterReadResult(
    InterfaceCounterReadStatus Status,
    InterfaceCounterSnapshot? Snapshot,
    string Message)
{
    public bool IsSuccess =>
        Status == InterfaceCounterReadStatus.Success
        && Snapshot is not null;
}

public enum BrowserObservationStatus
{
    Success,
    PartialSuccess,
    Canceled,
    AdapterChanged,
    AdapterUnavailable,
    CounterProviderMismatch,
    UnsupportedPlatform,
    NoWirelessConnection,
    InterfaceUnavailable,
    InvalidOptions,
    Failed
}

public enum ObservationConfidence
{
    Medium,
    Low
}

public sealed record BrowserObservationOptions(
    int BaselineSeconds = 3,
    int ObservationSeconds = 30,
    int SampleIntervalMilliseconds = 1000)
{
    public IReadOnlyList<string> Validate()
    {
        List<string> errors = [];

        if (BaselineSeconds is < 2 or > 15)
        {
            errors.Add("백그라운드 기준 수집 시간은 2~15초 범위여야 합니다.");
        }

        if (ObservationSeconds is < 5 or > 600)
        {
            errors.Add("브라우저 관찰 시간은 5~600초 범위여야 합니다.");
        }

        if (SampleIntervalMilliseconds is < 500 or > 2000)
        {
            errors.Add("샘플 간격은 500~2000ms 범위여야 합니다.");
        }

        return errors;
    }
}

public sealed record BrowserObservationSample(
    DateTimeOffset Timestamp,
    TimeSpan Interval,
    bool IsBaseline,
    string? InterfaceId,
    long ReceiveBytesDelta,
    long TransmitBytesDelta,
    double? RawReceiveMbps,
    double? RawTransmitMbps,
    double? AdjustedReceiveMbps,
    int? RssiDbm,
    string? Bssid,
    ulong? ReceiveLinkSpeedBps,
    ulong? TransmitLinkSpeedBps,
    bool InvalidInterval,
    bool AdapterChanged,
    bool CounterReset,
    bool WlanDisconnected,
    bool BssidChanged,
    bool PauseDetected,
    bool SuddenDropDetected,
    string? Note);

public sealed record BrowserObservationSummary(
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    TimeSpan ObservedDuration,
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
    ObservationConfidence Confidence,
    IReadOnlyList<BrowserObservationSample> Samples,
    string Message,
    string Limitation);

public sealed record BrowserObservationResult(
    BrowserObservationStatus Status,
    BrowserObservationSummary? Summary,
    WlanSnapshot? InitialWlan,
    string Message)
{
    public BrowserObservationResult(
        BrowserObservationStatus status,
        BrowserObservationSummary? summary,
        WlanSnapshot? initialWlan,
        string message,
        BrowserObservationTerminationReason terminationReason)
        : this(status, summary, initialWlan, message)
    {
        TerminationReason = terminationReason;
    }

    public BrowserObservationTerminationReason TerminationReason
    {
        get;
        init;
    } = BrowserObservationTerminationReason.None;

    public BrowserObservationTerminationReason EffectiveTerminationReason =>
        BrowserObservationTerminationPolicy.Resolve(this);

    public bool IsSuccess => Status is BrowserObservationStatus.Success
        or BrowserObservationStatus.PartialSuccess;
}

public enum BrowserObservationPhase
{
    Preparing,
    Baseline,
    Observing,
    Completed,
    Canceled,
    Failed
}

public sealed record BrowserObservationProgress(
    BrowserObservationPhase Phase,
    TimeSpan Elapsed,
    TimeSpan Remaining,
    BrowserObservationSample? LatestSample,
    string Message);
