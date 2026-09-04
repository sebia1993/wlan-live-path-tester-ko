namespace WlanLivePathTester.Core.Observation;

public enum BrowserObservationTerminationReason
{
    None,
    Completed,
    CanceledByUser,
    AdapterChanged,
    AdapterUnavailable,
    CounterProviderMismatch,
    InvalidOptions,
    UnsupportedPlatform,
    NoWirelessConnection,
    Failed
}
