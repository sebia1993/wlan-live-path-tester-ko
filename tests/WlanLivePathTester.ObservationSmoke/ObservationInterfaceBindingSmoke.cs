using System.Runtime.CompilerServices;
using WlanLivePathTester.Core.Models;
using WlanLivePathTester.Core.Observation;

namespace WlanLivePathTester.ObservationSmoke;

internal static class ObservationInterfaceBindingSmoke
{
#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Run()
    {
        StrictSelectionDoesNotFallbackToDescription();
        StrictSelectionUsesExactInterfaceId();
        PinRejectsInitialProviderMismatch();
        DetectsNativeWlanInterfaceChange();
        AllowsTemporaryWlanIdentityLoss();
        DetectsCounterProviderMismatch();
        Console.WriteLine("PASS  고정 Wi-Fi ID 선택·연속성·공급자 경계");
    }

    private static void StrictSelectionDoesNotFallbackToDescription()
    {
        InterfaceCounterSelectionDecision decision =
            InterfaceCounterSelectionPolicy.Select(
            [
                new InterfaceCounterCandidate(
                    CandidateIndex: 0,
                    InterfaceId: "wifi-b",
                    Description: "같은 무선 어댑터 설명",
                    IsWireless: true,
                    IsOperational: true)
            ],
            preferredInterfaceId: "wifi-a",
            preferredInterfaceDescription: "같은 무선 어댑터 설명",
            mode: InterfaceCounterSelectionMode.RequireExactInterfaceId);

        Ensure(!decision.IsSelected,
            "정확 ID 강제 모드에서 설명이 같다는 이유로 다른 Wi-Fi를 선택하면 안 됩니다.");
        Ensure(decision.Status
               == InterfaceCounterSelectionStatus.PreferredInterfaceNotFound,
            "고정 ID 미발견 상태를 반환해야 합니다.");
    }

    private static void StrictSelectionUsesExactInterfaceId()
    {
        InterfaceCounterSelectionDecision decision =
            InterfaceCounterSelectionPolicy.Select(
            [
                new InterfaceCounterCandidate(
                    CandidateIndex: 0,
                    InterfaceId: "{A1B2C3D4-E5F6-47A8-9123-1234567890AB}",
                    Description: "Wi-Fi A",
                    IsWireless: true,
                    IsOperational: true),
                new InterfaceCounterCandidate(
                    CandidateIndex: 1,
                    InterfaceId: "B1B2C3D4-E5F6-47A8-9123-1234567890AB",
                    Description: "Wi-Fi B",
                    IsWireless: true,
                    IsOperational: true)
            ],
            preferredInterfaceId:
                "a1b2c3d4-e5f6-47a8-9123-1234567890ab",
            preferredInterfaceDescription: null,
            mode: InterfaceCounterSelectionMode.RequireExactInterfaceId);

        Ensure(decision.IsSelected,
            "중괄호·대소문자가 다른 같은 GUID는 정확 일치로 선택해야 합니다.");
        Ensure(decision.SelectedCandidateIndex == 0,
            "정확한 고정 ID 후보를 선택해야 합니다.");
        Ensure(decision.Status
               == InterfaceCounterSelectionStatus.SelectedByInterfaceId,
            "정확 ID 선택 상태가 필요합니다.");
    }

    private static void PinRejectsInitialProviderMismatch()
    {
        try
        {
            _ = ObservationInterfaceBindingPolicy.Pin(
                ConnectedWlan("wifi-a"),
                Counter("wifi-b"));
            throw new InvalidOperationException(
                "초기 Native WLAN과 카운터 ID 불일치를 거부해야 합니다.");
        }
        catch (InvalidDataException)
        {
            // Expected.
        }
    }

    private static void DetectsNativeWlanInterfaceChange()
    {
        PinnedObservationInterface binding =
            ObservationInterfaceBindingPolicy.Pin(
                ConnectedWlan("wifi-a"),
                Counter("wifi-a"));
        ObservationInterfaceContinuityResult result =
            ObservationInterfaceBindingPolicy.EvaluateWlan(
                binding,
                ConnectedWlan("wifi-b"));

        Ensure(!result.ShouldContinue,
            "관찰 중 Native WLAN ID가 바뀌면 계속하면 안 됩니다.");
        Ensure(result.Status
               == ObservationInterfaceContinuityStatus.WlanInterfaceChanged,
            "Native WLAN 인터페이스 변경 상태가 필요합니다.");
    }

    private static void AllowsTemporaryWlanIdentityLoss()
    {
        PinnedObservationInterface binding =
            ObservationInterfaceBindingPolicy.Pin(
                ConnectedWlan("wifi-a"),
                Counter("wifi-a"));
        WlanSnapshot unavailableIdentity = ConnectedWlan(
            interfaceId: null);
        ObservationInterfaceContinuityResult result =
            ObservationInterfaceBindingPolicy.EvaluateWlan(
                binding,
                unavailableIdentity);

        Ensure(result.ShouldContinue,
            "현재 WLAN ID를 한 번 읽지 못했다고 고정 카운터를 즉시 버리면 안 됩니다.");
        Ensure(result.Status
               == ObservationInterfaceContinuityStatus.WlanTemporarilyUnavailable,
            "일시적인 WLAN identity 미확인 상태가 필요합니다.");
    }

    private static void DetectsCounterProviderMismatch()
    {
        PinnedObservationInterface binding =
            ObservationInterfaceBindingPolicy.Pin(
                ConnectedWlan("wifi-a"),
                Counter("wifi-a"));
        ObservationInterfaceContinuityResult result =
            ObservationInterfaceBindingPolicy.VerifyCounter(
                binding,
                Counter("wifi-b"));

        Ensure(!result.ShouldContinue,
            "카운터 공급자가 다른 인터페이스를 반환하면 해당 샘플을 사용하면 안 됩니다.");
        Ensure(result.Status
               == ObservationInterfaceContinuityStatus.CounterProviderMismatch,
            "카운터 공급자 불일치 상태가 필요합니다.");
    }

    private static WlanSnapshot ConnectedWlan(string? interfaceId) =>
        new(
            Timestamp: DateTimeOffset.UnixEpoch,
            IsConnected: true,
            Ssid: "SYNTHETIC-SSID",
            Bssid: "00:11:22:33:44:55",
            RssiDbm: -55,
            Channel: 36,
            PhyType: "802.11ax",
            ReceiveLinkSpeedBps: 1_200_000_000,
            TransmitLinkSpeedBps: 1_200_000_000,
            InterfaceDescription: "Synthetic wireless adapter",
            InterfaceId: interfaceId);

    private static InterfaceCounterSnapshot Counter(string interfaceId) =>
        new(
            Timestamp: DateTimeOffset.UnixEpoch,
            InterfaceId: interfaceId,
            InterfaceName: "Synthetic Wi-Fi",
            InterfaceDescription: "Synthetic wireless adapter",
            BytesReceived: 1_000,
            BytesSent: 500,
            IsOperational: true);

    private static void Ensure(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
