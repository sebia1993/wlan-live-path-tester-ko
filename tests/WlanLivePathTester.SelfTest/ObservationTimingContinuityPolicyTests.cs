using System.Runtime.CompilerServices;
using WlanLivePathTester.Core.Observation;

namespace WlanLivePathTester.SelfTest;

internal static class ObservationTimingContinuityPolicyTests
{
#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Run()
    {
        AcceptsExpectedInterval();
        AcceptsModerateSchedulerDelay();
        AcceptsExactBoundary();
        RejectsGapBeyondBoundary();
        ScalesMaximumForTwoSecondSampling();
        RejectsNonPositiveIntervals();
        RejectsInvalidExpectedInterval();
        Console.WriteLine(
            "PASS observation sample timing continuity policy tests");
    }

    private static void AcceptsExpectedInterval()
    {
        DateTimeOffset previous = DateTimeOffset.UnixEpoch;
        ObservationTimingContinuityDecision decision =
            ObservationTimingContinuityPolicy.Evaluate(
                previous,
                previous.AddSeconds(1),
                expectedSampleIntervalMilliseconds: 1000);

        Ensure(decision.ShouldContinue,
            "예상 1초 간격은 유효해야 합니다.");
        Ensure(decision.Status
               == ObservationTimingContinuityStatus.Valid,
            "정상 간격 상태가 필요합니다.");
        Ensure(decision.MaximumAllowedInterval
               == TimeSpan.FromSeconds(5),
            "1초 샘플은 절대 최소 상한 5초를 사용해야 합니다.");
    }

    private static void AcceptsModerateSchedulerDelay()
    {
        DateTimeOffset previous = DateTimeOffset.UnixEpoch;
        ObservationTimingContinuityDecision decision =
            ObservationTimingContinuityPolicy.Evaluate(
                previous,
                previous.AddSeconds(4.9),
                expectedSampleIntervalMilliseconds: 1000);

        Ensure(decision.ShouldContinue,
            "5초 미만의 일시적 스케줄러 지연은 관찰을 유지해야 합니다.");
    }

    private static void AcceptsExactBoundary()
    {
        DateTimeOffset previous = DateTimeOffset.UnixEpoch;
        ObservationTimingContinuityDecision decision =
            ObservationTimingContinuityPolicy.Evaluate(
                previous,
                previous.AddSeconds(5),
                expectedSampleIntervalMilliseconds: 1000);

        Ensure(decision.ShouldContinue,
            "허용 상한과 정확히 같은 간격은 유효해야 합니다.");
        Ensure(decision.ActualInterval
               == decision.MaximumAllowedInterval,
            "경계값 테스트가 실제 상한과 같아야 합니다.");
    }

    private static void RejectsGapBeyondBoundary()
    {
        DateTimeOffset previous = DateTimeOffset.UnixEpoch;
        ObservationTimingContinuityDecision decision =
            ObservationTimingContinuityPolicy.Evaluate(
                previous,
                previous.AddMilliseconds(5001),
                expectedSampleIntervalMilliseconds: 1000);

        Ensure(!decision.ShouldContinue,
            "허용 상한을 넘는 간격은 중단해야 합니다.");
        Ensure(decision.Status
               == ObservationTimingContinuityStatus.ExcessiveInterval,
            "장시간 간격은 ExcessiveInterval이어야 합니다.");
        Ensure(decision.Message.Contains(
                "결합하지 않습니다",
                StringComparison.Ordinal),
            "서로 다른 시간 구간을 합치지 않는다는 설명이 필요합니다.");
    }

    private static void ScalesMaximumForTwoSecondSampling()
    {
        DateTimeOffset previous = DateTimeOffset.UnixEpoch;
        ObservationTimingContinuityDecision atBoundary =
            ObservationTimingContinuityPolicy.Evaluate(
                previous,
                previous.AddSeconds(8),
                expectedSampleIntervalMilliseconds: 2000);
        ObservationTimingContinuityDecision overBoundary =
            ObservationTimingContinuityPolicy.Evaluate(
                previous,
                previous.AddMilliseconds(8001),
                expectedSampleIntervalMilliseconds: 2000);

        Ensure(atBoundary.MaximumAllowedInterval
               == TimeSpan.FromSeconds(8),
            "2초 샘플은 예상 간격의 4배인 8초 상한을 사용해야 합니다.");
        Ensure(atBoundary.ShouldContinue,
            "8초 경계값은 허용해야 합니다.");
        Ensure(!overBoundary.ShouldContinue,
            "8초를 넘는 간격은 중단해야 합니다.");
    }

    private static void RejectsNonPositiveIntervals()
    {
        DateTimeOffset timestamp = DateTimeOffset.UnixEpoch;
        ObservationTimingContinuityDecision equal =
            ObservationTimingContinuityPolicy.Evaluate(
                timestamp,
                timestamp,
                expectedSampleIntervalMilliseconds: 1000);
        ObservationTimingContinuityDecision reversed =
            ObservationTimingContinuityPolicy.Evaluate(
                timestamp,
                timestamp.AddMilliseconds(-1),
                expectedSampleIntervalMilliseconds: 1000);

        Ensure(!equal.ShouldContinue && !reversed.ShouldContinue,
            "0 또는 음수 간격은 모두 거부해야 합니다.");
        Ensure(equal.Status
               == ObservationTimingContinuityStatus.NonPositiveInterval,
            "0 간격 상태가 필요합니다.");
        Ensure(reversed.Status
               == ObservationTimingContinuityStatus.NonPositiveInterval,
            "음수 간격 상태가 필요합니다.");
    }

    private static void RejectsInvalidExpectedInterval()
    {
        EnsureThrows<ArgumentOutOfRangeException>(() =>
            ObservationTimingContinuityPolicy.Evaluate(
                DateTimeOffset.UnixEpoch,
                DateTimeOffset.UnixEpoch.AddSeconds(1),
                499));
        EnsureThrows<ArgumentOutOfRangeException>(() =>
            ObservationTimingContinuityPolicy.Evaluate(
                DateTimeOffset.UnixEpoch,
                DateTimeOffset.UnixEpoch.AddSeconds(1),
                2001));
    }

    private static void EnsureThrows<TException>(Action action)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }

        throw new InvalidOperationException(
            $"예상 예외 {typeof(TException).Name}가 발생하지 않았습니다.");
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
