using System.Runtime.CompilerServices;
using WlanLivePathTester.Core.Observation;

namespace WlanLivePathTester.SelfTest;

internal static class InterfaceCounterSelectionPolicyTests
{
#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Run()
    {
        SelectsUniqueGuidMatch();
        SelectsUniqueDescriptionFallback();
        RejectsDuplicateGuidMatches();
        RejectsDuplicateDescriptionMatches();
        DoesNotFallbackWhenPreferredInterfaceIsMissing();
        SelectsOnlySingleActiveWirelessWithoutPreference();
        RejectsMultipleActiveWirelessWithoutPreference();
        RejectsInactivePreferredInterface();
        IgnoresNonWirelessCandidates();
        Console.WriteLine("PASS fail-closed Wi-Fi counter selection tests");
    }

    private static void SelectsUniqueGuidMatch()
    {
        const string id =
            "A1B2C3D4-E5F6-47A8-9123-1234567890AB";
        InterfaceCounterSelectionDecision decision =
            InterfaceCounterSelectionPolicy.Select(
            [
                Candidate(
                    index: 0,
                    id: "B1B2C3D4-E5F6-47A8-9123-1234567890AB",
                    description: "Other Wi-Fi",
                    wireless: true,
                    up: true),
                Candidate(
                    index: 1,
                    id: id.ToLowerInvariant(),
                    description: "Target Wi-Fi",
                    wireless: true,
                    up: true)
            ],
            preferredInterfaceId: "{" + id + "}",
            preferredInterfaceDescription: "Wrong description");

        Ensure(decision.IsSelected,
            "유일한 GUID 일치는 선택되어야 합니다.");
        Ensure(decision.Status
               == InterfaceCounterSelectionStatus.SelectedByInterfaceId,
            "GUID가 설명보다 우선해야 합니다.");
        Ensure(decision.SelectedCandidateIndex == 1,
            "정확한 GUID 후보의 인덱스를 반환해야 합니다.");
    }

    private static void SelectsUniqueDescriptionFallback()
    {
        InterfaceCounterSelectionDecision decision =
            InterfaceCounterSelectionPolicy.Select(
            [
                Candidate(
                    0,
                    "A1B2C3D4-E5F6-47A8-9123-1234567890AB",
                    " Intel(R)   Wi-Fi 6E AX211 ",
                    wireless: true,
                    up: true),
                Candidate(
                    1,
                    "B1B2C3D4-E5F6-47A8-9123-1234567890AB",
                    "USB Wi-Fi",
                    wireless: true,
                    up: true)
            ],
            preferredInterfaceId:
                "C1B2C3D4-E5F6-47A8-9123-1234567890AB",
            preferredInterfaceDescription:
                "Intel(R) Wi-Fi 6E AX211");

        Ensure(decision.IsSelected,
            "GUID를 찾지 못해도 유일한 설명 완전 일치는 선택해야 합니다.");
        Ensure(decision.Status
               == InterfaceCounterSelectionStatus.SelectedByDescription,
            "설명 보조 일치 상태가 필요합니다.");
        Ensure(decision.SelectedCandidateIndex == 0,
            "설명 완전 일치 후보를 선택해야 합니다.");
    }

    private static void RejectsDuplicateGuidMatches()
    {
        const string id =
            "D1B2C3D4-E5F6-47A8-9123-1234567890AB";
        InterfaceCounterSelectionDecision decision =
            InterfaceCounterSelectionPolicy.Select(
            [
                Candidate(0, id, "Wi-Fi 1", true, true),
                Candidate(1, id.ToLowerInvariant(), "Wi-Fi 2", true, true)
            ],
            preferredInterfaceId: id,
            preferredInterfaceDescription: null);

        Ensure(!decision.IsSelected,
            "중복 GUID 후보를 임의 선택하면 안 됩니다.");
        Ensure(decision.Status
               == InterfaceCounterSelectionStatus.AmbiguousWirelessInterfaces,
            "중복 GUID는 모호한 인터페이스 상태여야 합니다.");
    }

    private static void RejectsDuplicateDescriptionMatches()
    {
        InterfaceCounterSelectionDecision decision =
            InterfaceCounterSelectionPolicy.Select(
            [
                Candidate(
                    0,
                    "E1B2C3D4-E5F6-47A8-9123-1234567890AB",
                    "USB Wi-Fi Adapter",
                    true,
                    true),
                Candidate(
                    1,
                    "F1B2C3D4-E5F6-47A8-9123-1234567890AB",
                    " USB   Wi-Fi Adapter ",
                    true,
                    true)
            ],
            preferredInterfaceId: null,
            preferredInterfaceDescription: "USB Wi-Fi Adapter");

        Ensure(!decision.IsSelected,
            "중복 설명 후보를 임의 선택하면 안 됩니다.");
        Ensure(decision.Status
               == InterfaceCounterSelectionStatus.AmbiguousWirelessInterfaces,
            "중복 설명은 모호한 인터페이스 상태여야 합니다.");
    }

    private static void DoesNotFallbackWhenPreferredInterfaceIsMissing()
    {
        InterfaceCounterSelectionDecision decision =
            InterfaceCounterSelectionPolicy.Select(
            [
                Candidate(
                    0,
                    "A2B2C3D4-E5F6-47A8-9123-1234567890AB",
                    "Only Active Wi-Fi",
                    true,
                    true)
            ],
            preferredInterfaceId:
                "B2B2C3D4-E5F6-47A8-9123-1234567890AB",
            preferredInterfaceDescription:
                "Missing Wi-Fi");

        Ensure(!decision.IsSelected,
            "명시된 WLAN NIC가 없을 때 유일한 활성 Wi-Fi로 우회하면 안 됩니다.");
        Ensure(decision.Status
               == InterfaceCounterSelectionStatus.PreferredInterfaceNotFound,
            "명시된 인터페이스 미발견 상태를 반환해야 합니다.");
    }

    private static void SelectsOnlySingleActiveWirelessWithoutPreference()
    {
        InterfaceCounterSelectionDecision decision =
            InterfaceCounterSelectionPolicy.Select(
            [
                Candidate(0, null, "Wi-Fi Down", true, false),
                Candidate(1, null, "Wi-Fi Up", true, true),
                Candidate(2, null, "Ethernet", false, true)
            ],
            preferredInterfaceId: null,
            preferredInterfaceDescription: null);

        Ensure(decision.IsSelected,
            "식별정보가 없어도 활성 Wi-Fi가 정확히 하나면 선택할 수 있어야 합니다.");
        Ensure(decision.Status
               == InterfaceCounterSelectionStatus.SelectedSingleActiveWireless,
            "단일 활성 Wi-Fi 선택 상태가 필요합니다.");
        Ensure(decision.SelectedCandidateIndex == 1,
            "유일한 활성 Wi-Fi 후보를 선택해야 합니다.");
    }

    private static void RejectsMultipleActiveWirelessWithoutPreference()
    {
        InterfaceCounterSelectionDecision decision =
            InterfaceCounterSelectionPolicy.Select(
            [
                Candidate(0, null, "Wi-Fi 1", true, true),
                Candidate(1, null, "Wi-Fi 2", true, true)
            ],
            preferredInterfaceId: null,
            preferredInterfaceDescription: null);

        Ensure(!decision.IsSelected,
            "식별정보 없이 활성 Wi-Fi가 여러 개면 임의 선택하면 안 됩니다.");
        Ensure(decision.Status
               == InterfaceCounterSelectionStatus.AmbiguousWirelessInterfaces,
            "다중 활성 Wi-Fi는 모호한 상태여야 합니다.");
    }

    private static void RejectsInactivePreferredInterface()
    {
        const string id =
            "C2B2C3D4-E5F6-47A8-9123-1234567890AB";
        InterfaceCounterSelectionDecision decision =
            InterfaceCounterSelectionPolicy.Select(
            [
                Candidate(0, id, "Connected Wi-Fi", true, false),
                Candidate(
                    1,
                    "D2B2C3D4-E5F6-47A8-9123-1234567890AB",
                    "Other Wi-Fi",
                    true,
                    true)
            ],
            preferredInterfaceId: id,
            preferredInterfaceDescription: "Connected Wi-Fi");

        Ensure(!decision.IsSelected,
            "대응된 Wi-Fi가 Up이 아니면 다른 활성 Wi-Fi로 우회하면 안 됩니다.");
        Ensure(decision.Status
               == InterfaceCounterSelectionStatus.PreferredInterfaceNotOperational,
            "대응 NIC 비활성 상태가 필요합니다.");
    }

    private static void IgnoresNonWirelessCandidates()
    {
        InterfaceCounterSelectionDecision decision =
            InterfaceCounterSelectionPolicy.Select(
            [
                Candidate(
                    0,
                    "E2B2C3D4-E5F6-47A8-9123-1234567890AB",
                    "Ethernet",
                    wireless: false,
                    up: true)
            ],
            preferredInterfaceId:
                "E2B2C3D4-E5F6-47A8-9123-1234567890AB",
            preferredInterfaceDescription: "Ethernet");

        Ensure(!decision.IsSelected,
            "같은 ID라도 비무선 인터페이스를 관찰 대상으로 선택하면 안 됩니다.");
        Ensure(decision.Status
               == InterfaceCounterSelectionStatus.NoWirelessInterface,
            "무선 후보 없음 상태가 필요합니다.");
    }

    private static InterfaceCounterCandidate Candidate(
        int index,
        string? id,
        string description,
        bool wireless,
        bool up) =>
        new(
            CandidateIndex: index,
            InterfaceId: id,
            Description: description,
            IsWireless: wireless,
            IsOperational: up);

    private static void Ensure(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
