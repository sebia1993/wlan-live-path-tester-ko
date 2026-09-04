using System.Runtime.CompilerServices;
using WlanLivePathTester.Core.Models;
using WlanLivePathTester.Core.NetworkEnvironment;

namespace WlanLivePathTester.SelfTest;

internal static class WlanInterfaceCorrelationTests
{
#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Run()
    {
        MatchesNormalizedInterfaceGuid();
        FallsBackToExactDescription();
        RejectsDuplicateDescriptionCandidates();
        IgnoresNonWirelessIdMatch();
        WarnsWhenMatchedWifiHasNoGateway();
        ReportsNoMatchWithoutGuessing();
        Console.WriteLine("PASS Native WLAN to local NIC correlation tests");
    }

    private static void MatchesNormalizedInterfaceGuid()
    {
        const string id = "A1B2C3D4-E5F6-47A8-9123-1234567890AB";
        WlanSnapshot wlan = ConnectedWlan(
            interfaceId: "{" + id + "}",
            description: "Intel Wi-Fi");
        LocalNetworkAdapterSnapshot adapter = Adapter(
            name: "Wi-Fi",
            description: "Intel Wi-Fi 6E AX211",
            id: id.ToLowerInvariant(),
            category: NetworkAdapterCategory.Wireless,
            gateway: true);

        WlanInterfaceCorrelationResult result =
            WlanInterfaceCorrelator.Correlate(wlan, [adapter]);

        Ensure(result.Status
               == WlanInterfaceCorrelationStatus.MatchedByInterfaceId,
            "대소문자·중괄호가 다른 같은 GUID를 정확 일치시켜야 합니다.");
        Ensure(result.MatchedDisplayName == "Wi-Fi",
            "대응된 로컬 NIC 이름을 로컬 UI용으로 반환해야 합니다.");
        Ensure(result.Warnings.Count == 0,
            "정상 Up·게이트웨이 보유 물리 Wi-Fi에는 경고가 없어야 합니다.");
    }

    private static void FallsBackToExactDescription()
    {
        WlanSnapshot wlan = ConnectedWlan(
            interfaceId: null,
            description: "  Intel(R)   Wi-Fi 6E AX211  ");
        LocalNetworkAdapterSnapshot adapter = Adapter(
            name: "Wi-Fi",
            description: "Intel(R) Wi-Fi 6E AX211",
            id: null,
            category: NetworkAdapterCategory.Wireless,
            gateway: true);

        WlanInterfaceCorrelationResult result =
            WlanInterfaceCorrelator.Correlate(wlan, [adapter]);

        Ensure(result.Status
               == WlanInterfaceCorrelationStatus.MatchedByDescription,
            "GUID가 없을 때 공백 정규화 후 설명 완전 일치를 사용할 수 있어야 합니다.");
    }

    private static void RejectsDuplicateDescriptionCandidates()
    {
        WlanSnapshot wlan = ConnectedWlan(
            interfaceId: null,
            description: "USB Wi-Fi Adapter");
        LocalNetworkAdapterSnapshot first = Adapter(
            "Wi-Fi 1",
            "USB Wi-Fi Adapter",
            null,
            NetworkAdapterCategory.Wireless,
            true);
        LocalNetworkAdapterSnapshot second = Adapter(
            "Wi-Fi 2",
            "USB Wi-Fi Adapter",
            null,
            NetworkAdapterCategory.Wireless,
            false);

        WlanInterfaceCorrelationResult result =
            WlanInterfaceCorrelator.Correlate(wlan, [first, second]);

        Ensure(result.Status
               == WlanInterfaceCorrelationStatus.MultipleMatches,
            "같은 설명 후보가 여러 개면 임의 선택하면 안 됩니다.");
        Ensure(!result.IsMatched && result.MatchedDisplayName is null,
            "중복 후보에서는 대응된 NIC를 반환하면 안 됩니다.");
    }

    private static void IgnoresNonWirelessIdMatch()
    {
        const string id = "B1B2C3D4-E5F6-47A8-9123-1234567890AB";
        WlanSnapshot wlan = ConnectedWlan(
            interfaceId: id,
            description: "Intel Wi-Fi");
        LocalNetworkAdapterSnapshot ethernet = Adapter(
            "Ethernet",
            "Intel Ethernet",
            id,
            NetworkAdapterCategory.Ethernet,
            true);

        WlanInterfaceCorrelationResult result =
            WlanInterfaceCorrelator.Correlate(wlan, [ethernet]);

        Ensure(result.Status == WlanInterfaceCorrelationStatus.NoMatch,
            "같은 ID라도 Ethernet 항목을 Native WLAN NIC로 대응시키면 안 됩니다.");
    }

    private static void WarnsWhenMatchedWifiHasNoGateway()
    {
        const string id = "C1B2C3D4-E5F6-47A8-9123-1234567890AB";
        WlanSnapshot wlan = ConnectedWlan(id, "Intel Wi-Fi");
        LocalNetworkAdapterSnapshot adapter = Adapter(
            "Wi-Fi",
            "Intel Wi-Fi",
            id,
            NetworkAdapterCategory.Wireless,
            gateway: false);

        WlanInterfaceCorrelationResult result =
            WlanInterfaceCorrelator.Correlate(wlan, [adapter]);

        Ensure(result.IsMatched,
            "GUID가 같으면 게이트웨이 유무와 관계없이 NIC 대응은 성공해야 합니다.");
        Ensure(result.Warnings.Any(warning => warning.Contains(
                "기본 게이트웨이",
                StringComparison.Ordinal)),
            "대응된 Wi-Fi에 기본 게이트웨이가 없으면 경고해야 합니다.");
    }

    private static void ReportsNoMatchWithoutGuessing()
    {
        WlanSnapshot wlan = ConnectedWlan(
            interfaceId: "D1B2C3D4-E5F6-47A8-9123-1234567890AB",
            description: "Unknown Wi-Fi");
        LocalNetworkAdapterSnapshot adapter = Adapter(
            "Wi-Fi",
            "Different Wi-Fi",
            "E1B2C3D4-E5F6-47A8-9123-1234567890AB",
            NetworkAdapterCategory.Wireless,
            true);

        WlanInterfaceCorrelationResult result =
            WlanInterfaceCorrelator.Correlate(wlan, [adapter]);

        Ensure(result.Status == WlanInterfaceCorrelationStatus.NoMatch,
            "GUID와 설명이 모두 다르면 대응 실패여야 합니다.");
        Ensure(result.Warnings.Count > 0,
            "대응 실패 시 같은 NIC로 단정하지 말라는 경고가 필요합니다.");
    }

    private static WlanSnapshot ConnectedWlan(
        string? interfaceId,
        string description) =>
        new(
            Timestamp: DateTimeOffset.UnixEpoch,
            IsConnected: true,
            Ssid: "SYNTHETIC",
            Bssid: "00:00:00:00:00:00",
            RssiDbm: -55,
            Channel: 36,
            PhyType: "802.11ax",
            ReceiveLinkSpeedBps: 1_200_000_000,
            TransmitLinkSpeedBps: 1_200_000_000,
            InterfaceDescription: description,
            InterfaceId: interfaceId);

    private static LocalNetworkAdapterSnapshot Adapter(
        string name,
        string description,
        string? id,
        NetworkAdapterCategory category,
        bool gateway) =>
        new(
            DisplayName: name,
            Description: description,
            NativeInterfaceType: category == NetworkAdapterCategory.Wireless
                ? "Wireless80211"
                : "Ethernet",
            Category: category,
            OperationalState: NetworkAdapterOperationalState.Up,
            SpeedBitsPerSecond: 1_000_000_000,
            HasDefaultGateway: gateway,
            GatewayCount: gateway ? 1 : 0,
            HasIpv4: true,
            HasIpv6: true,
            UnicastAddressCount: 2,
            SupportsMulticast: true,
            IsVirtual: false,
            IsVpn: false,
            ReadError: null,
            InterfaceId: id);

    private static void Ensure(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
