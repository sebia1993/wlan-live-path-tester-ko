using System.Runtime.CompilerServices;
using WlanLivePathTester.Core.Models;
using WlanLivePathTester.Core.Observation;

namespace WlanLivePathTester.SelfTest;

internal static class ObservationWlanIdentityContinuityTrackerTests
{
    private const string PrimaryId =
        "21B2C3D4-E5F6-47A8-9123-1234567890AB";
    private const string SecondaryId =
        "31B2C3D4-E5F6-47A8-9123-1234567890AB";

#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Run()
    {
        StableIdentityKeepsTrackerClear();
        AllowsTwoUnavailableSamplesAndStopsOnThird();
        SameIdentityRecoveryResetsUnavailableCount();
        DifferentIdentityStopsImmediately();
        DifferentIdentityAfterTransientGapStopsImmediately();
        TerminalStateRemainsTerminal();
        RejectsInvalidThresholds();
        Console.WriteLine(
            "PASS bounded WLAN identity continuity tracker tests");
    }

    private static void StableIdentityKeepsTrackerClear()
    {
        ObservationWlanIdentityContinuityTracker tracker =
            CreateTracker();

        ObservationWlanIdentityContinuityObservation observation =
            tracker.Observe(Connected(PrimaryId));

        Ensure(observation.ShouldContinue,
            "같은 WLAN ID는 관찰을 계속해야 합니다.");
        Ensure(observation.Status
               == ObservationWlanIdentityContinuityStatus.Stable,
            "같은 ID는 Stable이어야 합니다.");
        Ensure(observation.ConsecutiveUnavailableCount == 0
               && tracker.ConsecutiveUnavailableCount == 0,
            "정상 ID에서는 미확인 횟수가 없어야 합니다.");
        Ensure(observation.CurrentIdentityAvailable,
            "정상 연결 ID는 사용 가능 상태여야 합니다.");
    }

    private static void AllowsTwoUnavailableSamplesAndStopsOnThird()
    {
        ObservationWlanIdentityContinuityTracker tracker =
            CreateTracker(unavailableThreshold: 3);

        ObservationWlanIdentityContinuityObservation first =
            tracker.Observe(currentWlan: null);
        ObservationWlanIdentityContinuityObservation second =
            tracker.Observe(Connected(interfaceId: null));
        ObservationWlanIdentityContinuityObservation third =
            tracker.Observe(Disconnected(PrimaryId));

        Ensure(first.ShouldContinue && second.ShouldContinue,
            "첫 두 번의 WLAN ID 미확인은 고정 카운터로 재확인해야 합니다.");
        Ensure(first.Status
               == ObservationWlanIdentityContinuityStatus
                   .TransientlyUnavailable
               && second.Status
                   == ObservationWlanIdentityContinuityStatus
                       .TransientlyUnavailable,
            "임계값 전 미확인은 TransientlyUnavailable이어야 합니다.");
        Ensure(first.ConsecutiveUnavailableCount == 1
               && second.ConsecutiveUnavailableCount == 2,
            "연속 미확인 횟수를 정확히 누적해야 합니다.");
        Ensure(!third.ShouldContinue,
            "세 번째 연속 미확인은 관찰을 중단해야 합니다.");
        Ensure(third.Status
               == ObservationWlanIdentityContinuityStatus
                   .UnavailableThresholdExceeded,
            "임계값 도달 상태가 필요합니다.");
        Ensure(third.ConsecutiveUnavailableCount == 3
               && tracker.IsTerminal,
            "세 번째 미확인에서 종료 상태를 고정해야 합니다.");
        Ensure(third.Message.Contains(
                "연속 3회",
                StringComparison.Ordinal),
            "종료 메시지에 실제 연속 횟수가 필요합니다.");
    }

    private static void SameIdentityRecoveryResetsUnavailableCount()
    {
        ObservationWlanIdentityContinuityTracker tracker =
            CreateTracker();
        _ = tracker.Observe(currentWlan: null);
        _ = tracker.Observe(Connected(interfaceId: null));

        ObservationWlanIdentityContinuityObservation recovered =
            tracker.Observe(Connected(PrimaryId));
        ObservationWlanIdentityContinuityObservation unavailableAgain =
            tracker.Observe(currentWlan: null);

        Ensure(recovered.ShouldContinue
               && recovered.Status
                   == ObservationWlanIdentityContinuityStatus.Stable,
            "동일 WLAN ID 복구는 관찰을 계속해야 합니다.");
        Ensure(recovered.ConsecutiveUnavailableCount == 0
               && tracker.ConsecutiveUnavailableCount == 1,
            "복구 시 누적값을 지우고 다음 미확인은 다시 1부터 시작해야 합니다.");
        Ensure(unavailableAgain.ConsecutiveUnavailableCount == 1,
            "복구 뒤 새 미확인 연속 횟수가 1이어야 합니다.");
    }

    private static void DifferentIdentityStopsImmediately()
    {
        ObservationWlanIdentityContinuityTracker tracker =
            CreateTracker(unavailableThreshold: 10);

        ObservationWlanIdentityContinuityObservation changed =
            tracker.Observe(Connected(SecondaryId));

        Ensure(!changed.ShouldContinue,
            "실제 다른 WLAN GUID는 임계값과 무관하게 즉시 중단해야 합니다.");
        Ensure(changed.Status
               == ObservationWlanIdentityContinuityStatus.Changed,
            "다른 GUID는 Changed여야 합니다.");
        Ensure(changed.ConsecutiveUnavailableCount == 0,
            "실제 ID 변경을 미확인 횟수로 처리하면 안 됩니다.");
        Ensure(changed.CurrentIdentityAvailable,
            "변경된 실제 ID도 사용 가능한 identity입니다.");
    }

    private static void DifferentIdentityAfterTransientGapStopsImmediately()
    {
        ObservationWlanIdentityContinuityTracker tracker =
            CreateTracker(unavailableThreshold: 3);
        _ = tracker.Observe(currentWlan: null);
        _ = tracker.Observe(Connected(interfaceId: null));

        ObservationWlanIdentityContinuityObservation changed =
            tracker.Observe(Connected(SecondaryId));

        Ensure(!changed.ShouldContinue
               && changed.Status
                   == ObservationWlanIdentityContinuityStatus.Changed,
            "미확인 뒤 다른 실제 ID가 나타나면 즉시 Changed여야 합니다.");
        Ensure(changed.ConsecutiveUnavailableCount == 2,
            "변경 전 두 번의 미확인 근거는 진단 메시지용으로 유지해야 합니다.");
    }

    private static void TerminalStateRemainsTerminal()
    {
        ObservationWlanIdentityContinuityTracker unavailableTracker =
            CreateTracker(unavailableThreshold: 1);
        ObservationWlanIdentityContinuityObservation terminal =
            unavailableTracker.Observe(currentWlan: null);
        ObservationWlanIdentityContinuityObservation laterStable =
            unavailableTracker.Observe(Connected(PrimaryId));

        Ensure(!terminal.ShouldContinue
               && !laterStable.ShouldContinue,
            "임계값 초과 뒤 같은 tracker를 다시 정상 상태로 되돌리면 안 됩니다.");
        Ensure(laterStable.Status
               == ObservationWlanIdentityContinuityStatus
                   .UnavailableThresholdExceeded,
            "종료 상태는 세션 끝까지 유지해야 합니다.");

        ObservationWlanIdentityContinuityTracker changedTracker =
            CreateTracker();
        _ = changedTracker.Observe(Connected(SecondaryId));
        ObservationWlanIdentityContinuityObservation laterMissing =
            changedTracker.Observe(currentWlan: null);
        Ensure(laterMissing.Status
               == ObservationWlanIdentityContinuityStatus.Changed
               && !laterMissing.ShouldContinue,
            "ID 변경 종료 상태도 세션 끝까지 유지해야 합니다.");
    }

    private static void RejectsInvalidThresholds()
    {
        EnsureThrows<ArgumentOutOfRangeException>(() =>
            CreateTracker(unavailableThreshold: 0));
        EnsureThrows<ArgumentOutOfRangeException>(() =>
            CreateTracker(unavailableThreshold: 21));
    }

    private static ObservationWlanIdentityContinuityTracker CreateTracker(
        int unavailableThreshold = 3) =>
        new(
            new PinnedObservationInterface(
                WlanInterfaceId: PrimaryId,
                CounterInterfaceId: PrimaryId,
                InterfaceDescription: "Synthetic Wi-Fi"),
            unavailableThreshold);

    private static WlanSnapshot Connected(string? interfaceId) =>
        new(
            Timestamp: DateTimeOffset.UnixEpoch,
            IsConnected: true,
            Ssid: "Synthetic-SSID",
            Bssid: "AA:BB:CC:DD:EE:60",
            RssiDbm: -55,
            Channel: 36,
            PhyType: "802.11ax",
            ReceiveLinkSpeedBps: 1_200_000_000,
            TransmitLinkSpeedBps: 1_200_000_000,
            InterfaceDescription: "Synthetic Wi-Fi",
            InterfaceState: "Connected",
            SignalQualityPercent: 90,
            CenterFrequencyMhz: 5180,
            Authentication: "WPA2-Enterprise",
            Cipher: "CCMP",
            InterfaceId: interfaceId);

    private static WlanSnapshot Disconnected(string? interfaceId) =>
        Connected(interfaceId) with
        {
            IsConnected = false,
            InterfaceState = "Disconnected"
        };

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
