namespace WlanLivePathTester.Core.Observation;

public enum BrowserObservationTerminationReason
{
    None,
    Completed,
    CanceledByUser,
    AdapterChanged,
    AdapterUnavailable,
    CounterProviderMismatch,
    SystemSuspend,
    TimingDiscontinuity,
    InvalidOptions,
    UnsupportedPlatform,
    NoWirelessConnection,
    Failed
}

public static class BrowserObservationTerminationPolicy
{
    public static BrowserObservationTerminationReason FromStatus(
        BrowserObservationStatus status) =>
        status switch
        {
            BrowserObservationStatus.Success =>
                BrowserObservationTerminationReason.Completed,
            BrowserObservationStatus.Canceled =>
                BrowserObservationTerminationReason.CanceledByUser,
            BrowserObservationStatus.AdapterChanged =>
                BrowserObservationTerminationReason.AdapterChanged,
            BrowserObservationStatus.AdapterUnavailable =>
                BrowserObservationTerminationReason.AdapterUnavailable,
            BrowserObservationStatus.CounterProviderMismatch =>
                BrowserObservationTerminationReason.CounterProviderMismatch,
            BrowserObservationStatus.UnsupportedPlatform =>
                BrowserObservationTerminationReason.UnsupportedPlatform,
            BrowserObservationStatus.NoWirelessConnection =>
                BrowserObservationTerminationReason.NoWirelessConnection,
            BrowserObservationStatus.InterfaceUnavailable =>
                BrowserObservationTerminationReason.AdapterUnavailable,
            BrowserObservationStatus.InvalidOptions =>
                BrowserObservationTerminationReason.InvalidOptions,
            BrowserObservationStatus.PartialSuccess
                or BrowserObservationStatus.Failed =>
                BrowserObservationTerminationReason.Failed,
            _ => BrowserObservationTerminationReason.None
        };

    public static BrowserObservationTerminationReason Resolve(
        BrowserObservationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return result.TerminationReason
            == BrowserObservationTerminationReason.None
                ? FromStatus(result.Status)
                : result.TerminationReason;
    }

    public static string ToDisplayText(
        BrowserObservationTerminationReason reason) =>
        reason switch
        {
            BrowserObservationTerminationReason.Completed => "정상 완료",
            BrowserObservationTerminationReason.CanceledByUser => "사용자 중지",
            BrowserObservationTerminationReason.AdapterChanged =>
                "관찰 Wi-Fi 인터페이스 변경",
            BrowserObservationTerminationReason.AdapterUnavailable =>
                "고정 Wi-Fi 사용 불가",
            BrowserObservationTerminationReason.CounterProviderMismatch =>
                "고정 ID와 카운터 공급자 불일치",
            BrowserObservationTerminationReason.SystemSuspend =>
                "시스템 절전 전환",
            BrowserObservationTerminationReason.TimingDiscontinuity =>
                "샘플 시간 연속성 중단",
            BrowserObservationTerminationReason.InvalidOptions =>
                "관찰 설정 오류",
            BrowserObservationTerminationReason.UnsupportedPlatform =>
                "지원하지 않는 실행 환경",
            BrowserObservationTerminationReason.NoWirelessConnection =>
                "연결된 WLAN 없음",
            BrowserObservationTerminationReason.Failed =>
                "분류되지 않은 실행 오류",
            _ => "기록되지 않음"
        };
}
