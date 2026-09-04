namespace WlanLivePathTester.Core.Observation;

public enum ObservationTimingContinuityStatus
{
    Valid,
    NonPositiveInterval,
    ExcessiveInterval
}

public sealed record ObservationTimingContinuityDecision(
    ObservationTimingContinuityStatus Status,
    bool ShouldContinue,
    TimeSpan ActualInterval,
    TimeSpan MaximumAllowedInterval,
    string Message);

public static class ObservationTimingContinuityPolicy
{
    public const int MinimumExpectedSampleIntervalMilliseconds = 500;
    public const int MaximumExpectedSampleIntervalMilliseconds = 2000;
    public const int MaximumIntervalMultiplier = 4;

    public static readonly TimeSpan AbsoluteMinimumMaximumGap =
        TimeSpan.FromSeconds(5);

    public static ObservationTimingContinuityDecision Evaluate(
        DateTimeOffset previousTimestamp,
        DateTimeOffset currentTimestamp,
        int expectedSampleIntervalMilliseconds)
    {
        if (expectedSampleIntervalMilliseconds
            is < MinimumExpectedSampleIntervalMilliseconds
                or > MaximumExpectedSampleIntervalMilliseconds)
        {
            throw new ArgumentOutOfRangeException(
                nameof(expectedSampleIntervalMilliseconds),
                $"예상 샘플 간격은 {MinimumExpectedSampleIntervalMilliseconds}~{MaximumExpectedSampleIntervalMilliseconds}ms여야 합니다.");
        }

        TimeSpan actualInterval = currentTimestamp - previousTimestamp;
        TimeSpan multipliedLimit = TimeSpan.FromMilliseconds(
            checked(expectedSampleIntervalMilliseconds
                * MaximumIntervalMultiplier));
        TimeSpan maximumAllowed = multipliedLimit
            > AbsoluteMinimumMaximumGap
                ? multipliedLimit
                : AbsoluteMinimumMaximumGap;

        if (actualInterval <= TimeSpan.Zero)
        {
            return new ObservationTimingContinuityDecision(
                ObservationTimingContinuityStatus.NonPositiveInterval,
                ShouldContinue: false,
                ActualInterval: actualInterval,
                MaximumAllowedInterval: maximumAllowed,
                Message:
                    "Wi-Fi 카운터 시각이 이전 샘플보다 같거나 과거여서 처리량 계산을 중단합니다. 해당 카운터 구간은 결과 통계에 포함하지 않습니다.");
        }

        if (actualInterval > maximumAllowed)
        {
            return new ObservationTimingContinuityDecision(
                ObservationTimingContinuityStatus.ExcessiveInterval,
                ShouldContinue: false,
                ActualInterval: actualInterval,
                MaximumAllowedInterval: maximumAllowed,
                Message:
                    $"Wi-Fi 카운터 샘플 간격이 {actualInterval.TotalSeconds:F2}초로 허용 상한 {maximumAllowed.TotalSeconds:F2}초를 초과했습니다. 절전·드라이버 정지·스케줄러 지연 가능성이 있어 서로 다른 시간 구간을 결합하지 않습니다.");
        }

        return new ObservationTimingContinuityDecision(
            ObservationTimingContinuityStatus.Valid,
            ShouldContinue: true,
            ActualInterval: actualInterval,
            MaximumAllowedInterval: maximumAllowed,
            Message: "Wi-Fi 카운터 샘플 간격이 허용 범위입니다.");
    }
}
