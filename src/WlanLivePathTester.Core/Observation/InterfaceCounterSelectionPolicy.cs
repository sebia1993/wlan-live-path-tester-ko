namespace WlanLivePathTester.Core.Observation;

public enum InterfaceCounterSelectionMode
{
    PreferExactIdentityThenDescription,
    RequireExactInterfaceId
}

public enum InterfaceCounterSelectionStatus
{
    SelectedByInterfaceId,
    SelectedByDescription,
    SelectedSingleActiveWireless,
    NoWirelessInterface,
    NoActiveWirelessInterface,
    PreferredInterfaceNotFound,
    PreferredInterfaceNotOperational,
    AmbiguousWirelessInterfaces
}

public sealed record InterfaceCounterCandidate(
    int CandidateIndex,
    string? InterfaceId,
    string? Description,
    bool IsWireless,
    bool IsOperational);

public sealed record InterfaceCounterSelectionDecision(
    InterfaceCounterSelectionStatus Status,
    int? SelectedCandidateIndex,
    string Message)
{
    public bool IsSelected =>
        (Status is InterfaceCounterSelectionStatus.SelectedByInterfaceId
            or InterfaceCounterSelectionStatus.SelectedByDescription
            or InterfaceCounterSelectionStatus.SelectedSingleActiveWireless)
        && SelectedCandidateIndex.HasValue;
}

public static class InterfaceCounterSelectionPolicy
{
    public static InterfaceCounterSelectionDecision Select(
        IReadOnlyList<InterfaceCounterCandidate> candidates,
        string? preferredInterfaceId,
        string? preferredInterfaceDescription,
        InterfaceCounterSelectionMode mode =
            InterfaceCounterSelectionMode.PreferExactIdentityThenDescription)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        InterfaceCounterCandidate[] wireless = candidates
            .Where(candidate => candidate.IsWireless)
            .ToArray();
        if (wireless.Length == 0)
        {
            return Failure(
                InterfaceCounterSelectionStatus.NoWirelessInterface,
                "로컬 인터페이스 목록에 물리 Wi-Fi 후보가 없습니다.");
        }

        string normalizedPreferredId =
            ObservationInterfaceBindingPolicy.NormalizeInterfaceId(
                preferredInterfaceId);
        bool idWasSupplied = !string.IsNullOrWhiteSpace(
            normalizedPreferredId);

        if (idWasSupplied)
        {
            InterfaceCounterCandidate[] idMatches = wireless
                .Where(candidate =>
                    ObservationInterfaceBindingPolicy.NormalizeInterfaceId(
                            candidate.InterfaceId)
                        .Equals(
                            normalizedPreferredId,
                            StringComparison.OrdinalIgnoreCase))
                .ToArray();

            if (idMatches.Length > 1)
            {
                return Failure(
                    InterfaceCounterSelectionStatus.AmbiguousWirelessInterfaces,
                    "같은 인터페이스 ID를 가진 Wi-Fi 후보가 여러 개여서 관찰 대상을 선택하지 않았습니다.");
            }

            if (idMatches.Length == 1)
            {
                return SelectMatched(
                    idMatches[0],
                    InterfaceCounterSelectionStatus.SelectedByInterfaceId,
                    mode == InterfaceCounterSelectionMode.RequireExactInterfaceId
                        ? "시작 시 고정한 인터페이스 ID와 정확히 일치하는 Wi-Fi 카운터를 선택했습니다."
                        : "Native WLAN 인터페이스 ID와 정확히 일치하는 Wi-Fi 카운터를 선택했습니다.");
            }
        }

        if (mode == InterfaceCounterSelectionMode.RequireExactInterfaceId)
        {
            return Failure(
                InterfaceCounterSelectionStatus.PreferredInterfaceNotFound,
                idWasSupplied
                    ? "시작 시 고정한 Wi-Fi 인터페이스 ID를 현재 로컬 목록에서 찾지 못했습니다."
                    : "정확히 고정할 Wi-Fi 인터페이스 ID가 없어 카운터를 선택하지 않았습니다.");
        }

        bool descriptionWasSupplied = !string.IsNullOrWhiteSpace(
            preferredInterfaceDescription);
        string normalizedDescription = NormalizeDescription(
            preferredInterfaceDescription);
        if (!string.IsNullOrWhiteSpace(normalizedDescription))
        {
            InterfaceCounterCandidate[] descriptionMatches = wireless
                .Where(candidate => NormalizeDescription(
                        candidate.Description)
                    .Equals(
                        normalizedDescription,
                        StringComparison.OrdinalIgnoreCase))
                .ToArray();

            if (descriptionMatches.Length > 1)
            {
                return Failure(
                    InterfaceCounterSelectionStatus.AmbiguousWirelessInterfaces,
                    "같은 설명을 가진 Wi-Fi 후보가 여러 개여서 관찰 대상을 선택하지 않았습니다.");
            }

            if (descriptionMatches.Length == 1)
            {
                return SelectMatched(
                    descriptionMatches[0],
                    InterfaceCounterSelectionStatus.SelectedByDescription,
                    "Native WLAN 설명과 정확히 일치하는 Wi-Fi 카운터를 선택했습니다.");
            }
        }

        if (idWasSupplied || descriptionWasSupplied)
        {
            return Failure(
                InterfaceCounterSelectionStatus.PreferredInterfaceNotFound,
                "Native WLAN 인터페이스와 정확히 대응되는 Wi-Fi 카운터를 찾지 못해 임의 선택하지 않았습니다.");
        }

        InterfaceCounterCandidate[] activeWireless = wireless
            .Where(candidate => candidate.IsOperational)
            .ToArray();
        if (activeWireless.Length == 0)
        {
            return Failure(
                InterfaceCounterSelectionStatus.NoActiveWirelessInterface,
                "Up 상태인 물리 Wi-Fi 인터페이스가 없어 관찰 대상을 선택하지 않았습니다.");
        }

        if (activeWireless.Length > 1)
        {
            return Failure(
                InterfaceCounterSelectionStatus.AmbiguousWirelessInterfaces,
                "활성 물리 Wi-Fi 인터페이스가 여러 개이고 Native WLAN 식별정보가 없어 임의 선택하지 않았습니다.");
        }

        return new InterfaceCounterSelectionDecision(
            Status:
                InterfaceCounterSelectionStatus.SelectedSingleActiveWireless,
            SelectedCandidateIndex: activeWireless[0].CandidateIndex,
            Message: "활성 물리 Wi-Fi 인터페이스가 정확히 한 개여서 해당 카운터를 선택했습니다.");
    }

    private static InterfaceCounterSelectionDecision SelectMatched(
        InterfaceCounterCandidate candidate,
        InterfaceCounterSelectionStatus successStatus,
        string successMessage)
    {
        if (!candidate.IsOperational)
        {
            return Failure(
                InterfaceCounterSelectionStatus.PreferredInterfaceNotOperational,
                "Native WLAN과 대응된 Wi-Fi 인터페이스가 Up 상태가 아니어서 관찰을 시작하지 않았습니다.");
        }

        return new InterfaceCounterSelectionDecision(
            Status: successStatus,
            SelectedCandidateIndex: candidate.CandidateIndex,
            Message: successMessage);
    }

    private static InterfaceCounterSelectionDecision Failure(
        InterfaceCounterSelectionStatus status,
        string message) =>
        new(
            Status: status,
            SelectedCandidateIndex: null,
            Message: message);

    private static string NormalizeDescription(string? value) =>
        string.Join(
            ' ',
            (value ?? string.Empty)
                .Split(
                    [' ', '\t', '\r', '\n'],
                    StringSplitOptions.RemoveEmptyEntries
                    | StringSplitOptions.TrimEntries));
}
