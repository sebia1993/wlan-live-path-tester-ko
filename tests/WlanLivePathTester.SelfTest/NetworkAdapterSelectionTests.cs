using System.Net.NetworkInformation;
using System.Runtime.CompilerServices;
using WlanLivePathTester.Core.Adapters;

namespace WlanLivePathTester.SelfTest;

internal static class NetworkAdapterSelectionTests
{
#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Run()
    {
        SelectsNativeConnectedPhysicalWireless();
        ExcludesWiFiDirectAndVpnAdapters();
        ReportsAmbiguousEqualWirelessCandidates();
        ReportsNoConnectedPhysicalWireless();
        UsesDeterministicInventoryOrder();
        Console.WriteLine("PASS multi-NIC VPN and wireless adapter selection tests");
    }

    private static void SelectsNativeConnectedPhysicalWireless()
    {
        NetworkAdapterCandidate physical = Candidate(
            id: "11111111-1111-1111-1111-111111111111",
            name: "Wi-Fi",
            description: "Intel Wi-Fi 6E Adapter",
            type: NetworkInterfaceType.Wireless80211,
            status: OperationalStatus.Up,
            gateway: true,
            nativeConnected: true);
        NetworkAdapterCandidate usbWireless = Candidate(
            id: "22222222-2222-2222-2222-222222222222",
            name: "Wi-Fi 2",
            description: "USB 802.11ax Adapter",
            type: NetworkInterfaceType.Wireless80211,
            status: OperationalStatus.Up,
            gateway: true,
            nativeConnected: false);

        WirelessAdapterSelectionResult result = NetworkAdapterSelector.Select(
            [usbWireless, physical]);

        Ensure(result.Status == WirelessAdapterSelectionStatus.Selected,
            "Native WLAN 현재 연결과 일치하는 물리 Wi-Fi를 선택해야 합니다.");
        Ensure(result.Selected?.Candidate.Id == physical.Id,
            "현재 연결된 Native WLAN GUID와 일치하는 후보가 우선이어야 합니다.");
    }

    private static void ExcludesWiFiDirectAndVpnAdapters()
    {
        NetworkAdapterCandidate physical = Candidate(
            id: "33333333-3333-3333-3333-333333333333",
            name: "Wi-Fi",
            description: "Realtek USB WiFi Adapter",
            type: NetworkInterfaceType.Wireless80211,
            status: OperationalStatus.Up,
            gateway: true,
            nativeConnected: true);
        NetworkAdapterCandidate direct = Candidate(
            id: "44444444-4444-4444-4444-444444444444",
            name: "Local Area Connection* 12",
            description: "Microsoft Wi-Fi Direct Virtual Adapter",
            type: NetworkInterfaceType.Wireless80211,
            status: OperationalStatus.Up,
            gateway: false,
            nativeConnected: false);
        NetworkAdapterCandidate vpn = Candidate(
            id: "55555555-5555-5555-5555-555555555555",
            name: "Tailscale",
            description: "Tailscale Tunnel",
            type: NetworkInterfaceType.Tunnel,
            status: OperationalStatus.Up,
            gateway: true,
            nativeConnected: false);

        WirelessAdapterSelectionResult result = NetworkAdapterSelector.Select(
            [direct, vpn, physical]);

        Ensure(result.Selected?.Candidate.Id == physical.Id,
            "Wi-Fi Direct와 VPN보다 실제 물리 Wi-Fi를 선택해야 합니다.");
        Ensure(result.Inventory.Single(item => item.Candidate.Id == direct.Id).Role
               == NetworkAdapterRole.WiFiDirectOrHosted,
            "Wi-Fi Direct 가상 어댑터를 물리 Wi-Fi로 분류하면 안 됩니다.");
        Ensure(result.Inventory.Single(item => item.Candidate.Id == vpn.Id).Role
               == NetworkAdapterRole.VpnOrTunnel,
            "VPN 터널을 별도 역할로 분류해야 합니다.");
        Ensure(result.Warnings.Any(warning => warning.Contains(
                "VPN",
                StringComparison.OrdinalIgnoreCase)),
            "활성 VPN 경고가 필요합니다.");
    }

    private static void ReportsAmbiguousEqualWirelessCandidates()
    {
        NetworkAdapterCandidate first = Candidate(
            id: "66666666-6666-6666-6666-666666666666",
            name: "Wi-Fi A",
            description: "Wireless Adapter A",
            type: NetworkInterfaceType.Wireless80211,
            status: OperationalStatus.Up,
            gateway: true,
            nativeConnected: false);
        NetworkAdapterCandidate second = Candidate(
            id: "77777777-7777-7777-7777-777777777777",
            name: "Wi-Fi B",
            description: "Wireless Adapter B",
            type: NetworkInterfaceType.Wireless80211,
            status: OperationalStatus.Up,
            gateway: true,
            nativeConnected: false);

        WirelessAdapterSelectionResult result = NetworkAdapterSelector.Select(
            [second, first]);

        Ensure(result.Status == WirelessAdapterSelectionStatus.Ambiguous,
            "동일 우선순위의 활성 물리 Wi-Fi가 여러 개면 모호함이어야 합니다.");
        Ensure(result.Selected is null,
            "모호한 환경에서 임의의 첫 번째 Wi-Fi를 선택하면 안 됩니다.");
        Ensure(result.Candidates.Count == 2,
            "모호한 최상위 후보를 모두 반환해야 합니다.");
    }

    private static void ReportsNoConnectedPhysicalWireless()
    {
        NetworkAdapterCandidate down = Candidate(
            id: "88888888-8888-8888-8888-888888888888",
            name: "Wi-Fi",
            description: "Intel Wireless Adapter",
            type: NetworkInterfaceType.Wireless80211,
            status: OperationalStatus.Down,
            gateway: false,
            nativeConnected: false);

        WirelessAdapterSelectionResult result =
            NetworkAdapterSelector.Select([down]);

        Ensure(result.Status
               == WirelessAdapterSelectionStatus.NoConnectedPhysicalWireless,
            "물리 Wi-Fi가 Down이면 연결된 후보 없음으로 분류해야 합니다.");
        Ensure(result.Selected is null,
            "Down 상태의 Wi-Fi를 자동 선택하면 안 됩니다.");
    }

    private static void UsesDeterministicInventoryOrder()
    {
        NetworkAdapterCandidate zeta = Candidate(
            id: "99999999-9999-9999-9999-999999999999",
            name: "Zeta Wi-Fi",
            description: "Wireless Z",
            type: NetworkInterfaceType.Wireless80211,
            status: OperationalStatus.Down,
            gateway: false,
            nativeConnected: false);
        NetworkAdapterCandidate alpha = Candidate(
            id: "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
            name: "Alpha Wi-Fi",
            description: "Wireless A",
            type: NetworkInterfaceType.Wireless80211,
            status: OperationalStatus.Down,
            gateway: false,
            nativeConnected: false);

        WirelessAdapterSelectionResult first =
            NetworkAdapterSelector.Select([zeta, alpha]);
        WirelessAdapterSelectionResult second =
            NetworkAdapterSelector.Select([alpha, zeta]);

        string firstOrder = string.Join(
            "|",
            first.Inventory.Select(item => item.Candidate.Id));
        string secondOrder = string.Join(
            "|",
            second.Inventory.Select(item => item.Candidate.Id));
        Ensure(firstOrder == secondOrder,
            "입력 열거 순서와 무관하게 인벤토리 순서가 결정론적이어야 합니다.");
        Ensure(first.Inventory[0].Candidate.Name == "Alpha Wi-Fi",
            "동일 역할·점수에서는 표시 이름 순으로 정렬해야 합니다.");
    }

    private static NetworkAdapterCandidate Candidate(
        string id,
        string name,
        string description,
        NetworkInterfaceType type,
        OperationalStatus status,
        bool gateway,
        bool nativeConnected) =>
        new(
            Id: id,
            Name: name,
            Description: description,
            InterfaceType: type,
            OperationalStatus: status,
            SpeedBitsPerSecond: 1_000_000_000,
            HasUnicastAddress: status == OperationalStatus.Up,
            HasDefaultGateway: gateway,
            IsNativeWlanConnected: nativeConnected,
            IPv4InterfaceIndex: 10,
            IPv6InterfaceIndex: 10);

    private static void Ensure(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
