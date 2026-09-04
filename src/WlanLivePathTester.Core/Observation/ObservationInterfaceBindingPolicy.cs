using WlanLivePathTester.Core.Models;

namespace WlanLivePathTester.Core.Observation;

public enum ObservationInterfaceContinuityStatus
{
    Stable,
    WlanTemporarilyUnavailable,
    WlanInterfaceChanged,
    CounterProviderMismatch
}

public sealed record PinnedObservationInterface(
    string CounterInterfaceId,
    string? WlanInterfaceId,
    string WlanInterfaceDescription);

public sealed record ObservationInterfaceContinuityResult(
    ObservationInterfaceContinuityStatus Status,
    bool ShouldContinue,
    string Message);

public static class ObservationInterfaceBindingPolicy
{
    public static PinnedObservationInterface Pin(
        WlanSnapshot initialWlan,
        InterfaceCounterSnapshot initialCounter)
    {
        ArgumentNullException.ThrowIfNull(initialWlan);
        ArgumentNullException.ThrowIfNull(initialCounter);

        string counterInterfaceId = NormalizeInterfaceId(
            initialCounter.InterfaceId);
        if (string.IsNullOrWhiteSpace(counterInterfaceId))
        {
            throw new InvalidDataException(
                "관찰 시작 시 Wi-Fi 카운터 인터페이스 ID를 고정하지 못했습니다.");
        }

        string wlanInterfaceId = NormalizeInterfaceId(
            initialWlan.InterfaceId);
        if (!string.IsNullOrWhiteSpace(wlanInterfaceId)
            && !wlanInterfaceId.Equals(
                counterInterfaceId,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "Native WLAN 인터페이스와 카운터 공급자가 서로 다른 Wi-Fi ID를 반환했습니다.");
        }

        return new PinnedObservationInterface(
            CounterInterfaceId: counterInterfaceId,
            WlanInterfaceId: string.IsNullOrWhiteSpace(wlanInterfaceId)
                ? null
                : wlanInterfaceId,
            WlanInterfaceDescription: NormalizeDescription(
                initialWlan.InterfaceDescription));
    }

    public static ObservationInterfaceContinuityResult EvaluateWlan(
        PinnedObservationInterface binding,
        WlanSnapshot? currentWlan)
    {
        ArgumentNullException.ThrowIfNull(binding);

        if (currentWlan is null || !currentWlan.IsConnected)
        {
            return new ObservationInterfaceContinuityResult(
                ObservationInterfaceContinuityStatus.WlanTemporarilyUnavailable,
                ShouldContinue: true,
                Message: "현재 WLAN 연결 정보를 일시적으로 확인하지 못했지만 시작 시 고정한 Wi-Fi 카운터를 유지합니다.");
        }

        string currentInterfaceId = NormalizeInterfaceId(
            currentWlan.InterfaceId);
        if (string.IsNullOrWhiteSpace(currentInterfaceId))
        {
            return new ObservationInterfaceContinuityResult(
                ObservationInterfaceContinuityStatus.WlanTemporarilyUnavailable,
                ShouldContinue: true,
                Message: "현재 WLAN 인터페이스 ID를 일시적으로 확인하지 못했지만 시작 시 고정한 Wi-Fi 카운터를 유지합니다.");
        }

        string expectedInterfaceId = binding.WlanInterfaceId
            ?? binding.CounterInterfaceId;
        if (!currentInterfaceId.Equals(
                expectedInterfaceId,
                StringComparison.OrdinalIgnoreCase))
        {
            return new ObservationInterfaceContinuityResult(
                ObservationInterfaceContinuityStatus.WlanInterfaceChanged,
                ShouldContinue: false,
                Message: "관찰 중 Native WLAN 연결이 시작 시 고정한 물리 Wi-Fi와 다른 인터페이스로 변경됐습니다.");
        }

        return new ObservationInterfaceContinuityResult(
            ObservationInterfaceContinuityStatus.Stable,
            ShouldContinue: true,
            Message: "현재 Native WLAN 연결이 시작 시 고정한 물리 Wi-Fi와 일치합니다.");
    }

    public static ObservationInterfaceContinuityResult VerifyCounter(
        PinnedObservationInterface binding,
        InterfaceCounterSnapshot currentCounter)
    {
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(currentCounter);

        string currentInterfaceId = NormalizeInterfaceId(
            currentCounter.InterfaceId);
        if (string.IsNullOrWhiteSpace(currentInterfaceId)
            || !currentInterfaceId.Equals(
                binding.CounterInterfaceId,
                StringComparison.OrdinalIgnoreCase))
        {
            return new ObservationInterfaceContinuityResult(
                ObservationInterfaceContinuityStatus.CounterProviderMismatch,
                ShouldContinue: false,
                Message: "카운터 공급자가 시작 시 고정한 Wi-Fi와 다른 인터페이스 결과를 반환했습니다.");
        }

        return new ObservationInterfaceContinuityResult(
            ObservationInterfaceContinuityStatus.Stable,
            ShouldContinue: true,
            Message: "카운터 공급자가 시작 시 고정한 Wi-Fi 결과를 반환했습니다.");
    }

    public static string NormalizeInterfaceId(string? value)
    {
        string trimmed = (value ?? string.Empty)
            .Trim()
            .Trim('{', '}');
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return string.Empty;
        }

        return Guid.TryParse(trimmed, out Guid parsed)
            ? parsed.ToString("D")
            : trimmed.ToLowerInvariant();
    }

    private static string NormalizeDescription(string? value) =>
        string.Join(
            ' ',
            (value ?? string.Empty)
                .Split(
                    [' ', '\t', '\r', '\n'],
                    StringSplitOptions.RemoveEmptyEntries
                    | StringSplitOptions.TrimEntries));
}
