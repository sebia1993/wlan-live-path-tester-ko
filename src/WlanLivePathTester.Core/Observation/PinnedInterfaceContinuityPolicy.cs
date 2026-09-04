namespace WlanLivePathTester.Core.Observation;

public enum PinnedInterfaceContinuityStatus
{
    StableByInterfaceId,
    StableByDescription,
    CurrentIdentityUnavailable,
    PinnedIdentityUnavailable,
    InterfaceChanged
}

public sealed record PinnedInterfaceContinuityDecision(
    PinnedInterfaceContinuityStatus Status,
    bool ShouldContinue,
    string Message);

public static class PinnedInterfaceContinuityPolicy
{
    public static PinnedInterfaceContinuityDecision Evaluate(
        string? pinnedInterfaceId,
        string? pinnedInterfaceDescription,
        string? currentInterfaceId,
        string? currentInterfaceDescription)
    {
        string? pinnedId = NormalizeGuid(pinnedInterfaceId);
        string? currentId = NormalizeGuid(currentInterfaceId);
        string pinnedDescription = NormalizeDescription(
            pinnedInterfaceDescription);
        string currentDescription = NormalizeDescription(
            currentInterfaceDescription);

        if (pinnedId is not null && currentId is not null)
        {
            return pinnedId.Equals(
                    currentId,
                    StringComparison.OrdinalIgnoreCase)
                ? new PinnedInterfaceContinuityDecision(
                    PinnedInterfaceContinuityStatus.StableByInterfaceId,
                    ShouldContinue: true,
                    Message: "현재 Native WLAN 인터페이스가 관찰 시작 시 고정한 Wi-Fi GUID와 일치합니다.")
                : Changed(
                    "Native WLAN 연결이 관찰 시작 시 고정한 Wi-Fi와 다른 인터페이스 GUID로 변경됐습니다.");
        }

        if (!string.IsNullOrWhiteSpace(pinnedDescription)
            && !string.IsNullOrWhiteSpace(currentDescription))
        {
            return pinnedDescription.Equals(
                    currentDescription,
                    StringComparison.OrdinalIgnoreCase)
                ? new PinnedInterfaceContinuityDecision(
                    PinnedInterfaceContinuityStatus.StableByDescription,
                    ShouldContinue: true,
                    Message: "현재 Native WLAN 설명이 관찰 시작 시 고정한 Wi-Fi 설명과 일치합니다.")
                : Changed(
                    "Native WLAN 연결이 관찰 시작 시 고정한 Wi-Fi와 다른 인터페이스 설명으로 변경됐습니다.");
        }

        if (pinnedId is null
            && string.IsNullOrWhiteSpace(pinnedDescription))
        {
            return new PinnedInterfaceContinuityDecision(
                PinnedInterfaceContinuityStatus.PinnedIdentityUnavailable,
                ShouldContinue: false,
                Message: "관찰 시작 시 Wi-Fi 인터페이스 식별정보를 고정하지 못해 연속성을 보장할 수 없습니다.");
        }

        return new PinnedInterfaceContinuityDecision(
            PinnedInterfaceContinuityStatus.CurrentIdentityUnavailable,
            ShouldContinue: true,
            Message: "현재 WLAN 식별정보를 완전히 읽지 못했지만 시작 시 고정한 Wi-Fi 카운터를 유지합니다.");
    }

    private static PinnedInterfaceContinuityDecision Changed(
        string message) =>
        new(
            PinnedInterfaceContinuityStatus.InterfaceChanged,
            ShouldContinue: false,
            Message: message
                + " 서로 다른 NIC의 누적 카운터를 결합하지 않기 위해 관찰을 중단합니다.");

    private static string? NormalizeGuid(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string trimmed = value.Trim().Trim('{', '}');
        return Guid.TryParse(trimmed, out Guid parsed)
            ? parsed.ToString("D")
            : null;
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
