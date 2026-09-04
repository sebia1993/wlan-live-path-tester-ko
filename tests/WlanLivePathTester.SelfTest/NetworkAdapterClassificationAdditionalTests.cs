using System.Net.NetworkInformation;
using System.Runtime.CompilerServices;
using WlanLivePathTester.Core.Adapters;

namespace WlanLivePathTester.SelfTest;

internal static class NetworkAdapterClassificationAdditionalTests
{
#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Run()
    {
        VerifyBluetoothPanClassification();
        VerifyEnterpriseVpnMarkers();
        VerifyUsbWirelessRemainsPhysical();
        Console.WriteLine("PASS Bluetooth enterprise VPN and USB Wi-Fi classification tests");
    }

    private static void VerifyBluetoothPanClassification()
    {
        ClassifiedNetworkAdapter classified = NetworkAdapterSelector.Classify(
            Candidate(
                name: "Bluetooth Network Connection",
                description: "Bluetooth Device (Personal Area Network)",
                type: NetworkInterfaceType.Ethernet));

        Ensure(classified.Role == NetworkAdapterRole.Bluetooth,
            "Bluetooth PAN을 실제 유선 어댑터로 분류하면 안 됩니다.");
    }

    private static void VerifyEnterpriseVpnMarkers()
    {
        (string Name, string Description)[] examples =
        [
            ("PANGP", "PANGP Virtual Ethernet Adapter"),
            ("Zscaler", "Zscaler Network Adapter"),
            ("Netskope", "Netskope Client Tunnel"),
            ("WARP", "Cloudflare WARP Interface")
        ];

        foreach ((string name, string description) in examples)
        {
            ClassifiedNetworkAdapter classified =
                NetworkAdapterSelector.Classify(
                    Candidate(
                        name,
                        description,
                        NetworkInterfaceType.Ethernet));
            Ensure(classified.Role == NetworkAdapterRole.VpnOrTunnel,
                $"기업 VPN·터널 어댑터를 별도 분류해야 합니다: {name}");
        }
    }

    private static void VerifyUsbWirelessRemainsPhysical()
    {
        ClassifiedNetworkAdapter classified = NetworkAdapterSelector.Classify(
            Candidate(
                name: "Wi-Fi 2",
                description: "Realtek USB 802.11ax Wireless LAN Adapter",
                type: NetworkInterfaceType.Wireless80211));

        Ensure(classified.Role == NetworkAdapterRole.PhysicalWireless,
            "USB 연결 방식만으로 실제 Wi-Fi를 가상 어댑터로 제외하면 안 됩니다.");
    }

    private static NetworkAdapterCandidate Candidate(
        string name,
        string description,
        NetworkInterfaceType type) =>
        new(
            Id: Guid.NewGuid().ToString("D"),
            Name: name,
            Description: description,
            InterfaceType: type,
            OperationalStatus: OperationalStatus.Up,
            SpeedBitsPerSecond: 1_000_000_000,
            HasUnicastAddress: true,
            HasDefaultGateway: true,
            IsNativeWlanConnected: false);

    private static void Ensure(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
