namespace WlanLivePathTester.Core.Observation;

public sealed class BrowserObservationCancellationContext
{
    private int _requestedReason =
        (int)BrowserObservationTerminationReason.None;

    public BrowserObservationTerminationReason RequestedReason =>
        (BrowserObservationTerminationReason)Volatile.Read(
            ref _requestedReason);

    public void Reset() =>
        Interlocked.Exchange(
            ref _requestedReason,
            (int)BrowserObservationTerminationReason.None);

    public bool RequestUserCancellation() =>
        Request(BrowserObservationTerminationReason.CanceledByUser);

    public bool RequestSystemSuspend() =>
        Request(BrowserObservationTerminationReason.SystemSuspend);

    public BrowserObservationTerminationReason
        ResolveCancellationReason()
    {
        BrowserObservationTerminationReason reason = RequestedReason;
        return reason == BrowserObservationTerminationReason.None
            ? BrowserObservationTerminationReason.CanceledByUser
            : reason;
    }

    private bool Request(
        BrowserObservationTerminationReason reason)
    {
        int requestedPriority = GetPriority(reason);
        if (requestedPriority == 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(reason),
                reason,
                "관찰 취소 컨텍스트에는 사용자 중지 또는 시스템 절전만 요청할 수 있습니다.");
        }

        while (true)
        {
            int currentValue = Volatile.Read(ref _requestedReason);
            BrowserObservationTerminationReason currentReason =
                (BrowserObservationTerminationReason)currentValue;
            if (GetPriority(currentReason) >= requestedPriority)
            {
                return false;
            }

            int original = Interlocked.CompareExchange(
                ref _requestedReason,
                (int)reason,
                currentValue);
            if (original == currentValue)
            {
                return true;
            }
        }
    }

    private static int GetPriority(
        BrowserObservationTerminationReason reason) =>
        reason switch
        {
            BrowserObservationTerminationReason.CanceledByUser => 1,
            BrowserObservationTerminationReason.SystemSuspend => 2,
            _ => 0
        };
}
