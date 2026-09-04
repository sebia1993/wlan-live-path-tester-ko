namespace WlanLivePathTester.Core.Models;

public enum NetworkPathKind
{
    Internal,
    External
}

public enum MeasurementStatus
{
    NotRun,
    Success,
    PartialSuccess,
    Failed,
    TimedOut,
    Canceled,
    Blocked,
    ProxyAuthenticationRequired,
    PathMismatch
}

public enum FindingSeverity
{
    Information,
    Warning,
    Critical
}

public sealed record MeasurementTargetDefinition(
    string Name,
    string Url,
    NetworkPathKind PathKind,
    bool RequireProxy,
    bool RequireDirect,
    long MaxBytes,
    int TimeoutSeconds,
    int Streams,
    int MaxRedirects);

public sealed record WlanSnapshot(
    DateTimeOffset Timestamp,
    bool IsConnected,
    string? Ssid,
    string? Bssid,
    int? RssiDbm,
    uint? Channel,
    string? PhyType,
    ulong? ReceiveLinkSpeedBps,
    ulong? TransmitLinkSpeedBps,
    string? InterfaceDescription = null,
    string? InterfaceState = null,
    int? SignalQualityPercent = null,
    uint? CenterFrequencyMhz = null,
    string? Authentication = null,
    string? Cipher = null,
    uint? NativeErrorCode = null,
    string? ReadError = null,
    string? InterfaceId = null);

public sealed record DownloadMeasurement(
    string TargetName,
    NetworkPathKind PathKind,
    MeasurementStatus Status,
    long BytesReceived,
    TimeSpan Duration,
    double? AverageMbps,
    int? HttpStatusCode,
    bool? ProxyWasUsed,
    string? ErrorCode);

public sealed record DiagnosisFinding(
    string Code,
    FindingSeverity Severity,
    string Title,
    string Explanation,
    string NextStep);

public sealed record DiagnosisThresholds(
    int WeakRssiDbm,
    double MinimumInternalMbps,
    double MinimumExternalMbps,
    double CommonExternalPathRatio,
    double SiteVariationRatio)
{
    public static DiagnosisThresholds Default { get; } = new(
        WeakRssiDbm: -75,
        MinimumInternalMbps: 50,
        MinimumExternalMbps: 20,
        CommonExternalPathRatio: 0.35,
        SiteVariationRatio: 2.0);
}
