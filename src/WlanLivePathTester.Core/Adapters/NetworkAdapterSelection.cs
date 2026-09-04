using System.Net.NetworkInformation;

namespace WlanLivePathTester.Core.Adapters;

public enum NetworkAdapterRole
{
    PhysicalWireless,
    PhysicalEthernet,
    WiFiDirectOrHosted,
    VpnOrTunnel,
    VirtualSwitch,
    Bluetooth,
    Loopback,
    OtherVirtual,
    Unknown
}

public enum WirelessAdapterSelectionStatus
{
    Selected,
    Ambiguous,
    NoConnectedPhysicalWireless,
    NoPhysicalWireless
}

public sealed record NetworkAdapterCandidate(
    string Id,
    string Name,
    string Description,
    NetworkInterfaceType InterfaceType,
    OperationalStatus OperationalStatus,
    long SpeedBitsPerSecond,
    bool HasUnicastAddress,
    bool HasDefaultGateway,
    bool IsNativeWlanConnected,
    int? IPv4InterfaceIndex = null,
    int? IPv6InterfaceIndex = null);

public sealed record ClassifiedNetworkAdapter(
    NetworkAdapterCandidate Candidate,
    NetworkAdapterRole Role,
    int WirelessSelectionScore,
    IReadOnlyList<string> ClassificationReasons)
{
    public bool IsEligiblePhysicalWireless =>
        Role == NetworkAdapterRole.PhysicalWireless;
}

public sealed record WirelessAdapterSelectionResult(
    WirelessAdapterSelectionStatus Status,
    ClassifiedNetworkAdapter? Selected,
    IReadOnlyList<ClassifiedNetworkAdapter> Candidates,
    IReadOnlyList<ClassifiedNetworkAdapter> Inventory,
    IReadOnlyList<string> Warnings,
    string Message);

public static class NetworkAdapterSelector
{
    private static readonly string[] WiFiDirectMarkers =
    [
        "wi-fi direct",
        "wifi direct",
        "hosted network",
        "mobile hotspot",
        "softap"
    ];

    private static readonly string[] BluetoothMarkers =
    [
        "bluetooth",
        "personal area network"
    ];

    private static readonly string[] VpnMarkers =
    [
        "vpn",
        "tunnel",
        "wintun",
        "wireguard",
        "openvpn",
        "tap-windows",
        "tap adapter",
        "tailscale",
        "zerotier",
        "hamachi",
        "anyconnect",
        "secure client",
        "globalprotect",
        "pangp",
        "pulse secure",
        "juniper",
        "fortinet ssl",
        "forticlient",
        "checkpoint",
        "check point",
        "zscaler",
        "netskope",
        "cloudflare warp"
    ];

    private static readonly string[] VirtualSwitchMarkers =
    [
        "vethernet",
        "hyper-v",
        "vmware",
        "virtualbox",
        "container",
        "docker",
        "wsl"
    ];

    private static readonly string[] OtherVirtualMarkers =
    [
        "virtual adapter",
        "virtual ethernet",
        "virtual network",
        "loopback adapter",
        "npcap loopback"
    ];

    public static WirelessAdapterSelectionResult Select(
        IEnumerable<NetworkAdapterCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        ClassifiedNetworkAdapter[] inventory = candidates
            .Select(Classify)
            .OrderBy(item => RoleSortOrder(item.Role))
            .ThenByDescending(item => item.WirelessSelectionScore)
            .ThenBy(
                item => DisplayName(item.Candidate),
                StringComparer.OrdinalIgnoreCase)
            .ThenBy(
                item => item.Candidate.Id,
                StringComparer.OrdinalIgnoreCase)
            .ToArray();

        ClassifiedNetworkAdapter[] physicalWireless = inventory
            .Where(item => item.IsEligiblePhysicalWireless)
            .ToArray();
        ClassifiedNetworkAdapter[] connected = physicalWireless
            .Where(item =>
                item.Candidate.OperationalStatus == OperationalStatus.Up)
            .ToArray();

        List<string> warnings = BuildWarnings(inventory, connected);

        if (physicalWireless.Length == 0)
        {
            return new WirelessAdapterSelectionResult(
                WirelessAdapterSelectionStatus.NoPhysicalWireless,
                Selected: null,
                Candidates: Array.Empty<ClassifiedNetworkAdapter>(),
                Inventory: inventory,
                Warnings: warnings,
                Message: "물리 Wi-Fi 후보를 확인하지 못했습니다. WLAN AutoConfig, 무선 어댑터 상태와 드라이버를 확인하십시오.");
        }

        if (connected.Length == 0)
        {
            return new WirelessAdapterSelectionResult(
                WirelessAdapterSelectionStatus.NoConnectedPhysicalWireless,
                Selected: null,
                Candidates: physicalWireless,
                Inventory: inventory,
                Warnings: warnings,
                Message: "물리 Wi-Fi 후보는 있지만 현재 Up 상태인 어댑터가 없습니다.");
        }

        int highestScore = connected.Max(
            item => item.WirelessSelectionScore);
        ClassifiedNetworkAdapter[] highest = connected
            .Where(item => item.WirelessSelectionScore == highestScore)
            .ToArray();

        if (highest.Length > 1)
        {
            warnings.Add(
                $"동일 우선순위의 활성 Wi-Fi 후보가 {highest.Length}개입니다. 자동으로 한 어댑터를 단정하지 않습니다.");
            return new WirelessAdapterSelectionResult(
                WirelessAdapterSelectionStatus.Ambiguous,
                Selected: null,
                Candidates: highest,
                Inventory: inventory,
                Warnings: warnings,
                Message: "활성 Wi-Fi 어댑터가 여러 개라 선택이 모호합니다. 사용 중인 어댑터를 확인한 뒤 다른 Wi-Fi 어댑터를 비활성화하거나 명시적으로 선택하십시오.");
        }

        ClassifiedNetworkAdapter selected = highest[0];
        return new WirelessAdapterSelectionResult(
            WirelessAdapterSelectionStatus.Selected,
            Selected: selected,
            Candidates: connected,
            Inventory: inventory,
            Warnings: warnings,
            Message: $"권장 Wi-Fi 어댑터를 선택했습니다: {DisplayName(selected.Candidate)}");
    }

    public static ClassifiedNetworkAdapter Classify(
        NetworkAdapterCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        string searchable = $"{candidate.Name} {candidate.Description}";
        List<string> reasons = [];
        NetworkAdapterRole role;

        if (candidate.InterfaceType == NetworkInterfaceType.Loopback)
        {
            role = NetworkAdapterRole.Loopback;
            reasons.Add("NetworkInterfaceType.Loopback");
        }
        else if (ContainsAny(searchable, BluetoothMarkers))
        {
            role = NetworkAdapterRole.Bluetooth;
            reasons.Add("Bluetooth 또는 Personal Area Network 식별 문자열");
        }
        else if (candidate.InterfaceType == NetworkInterfaceType.Ppp
                 || candidate.InterfaceType == NetworkInterfaceType.Tunnel
                 || ContainsAny(searchable, VpnMarkers))
        {
            role = NetworkAdapterRole.VpnOrTunnel;
            reasons.Add(candidate.InterfaceType switch
            {
                NetworkInterfaceType.Ppp => "NetworkInterfaceType.Ppp",
                NetworkInterfaceType.Tunnel => "NetworkInterfaceType.Tunnel",
                _ => "VPN 또는 터널 식별 문자열"
            });
        }
        else if (ContainsAny(searchable, WiFiDirectMarkers))
        {
            role = NetworkAdapterRole.WiFiDirectOrHosted;
            reasons.Add(
                "Wi-Fi Direct, Hosted Network 또는 SoftAP 식별 문자열");
        }
        else if (ContainsAny(searchable, VirtualSwitchMarkers))
        {
            role = NetworkAdapterRole.VirtualSwitch;
            reasons.Add(
                "Hyper-V, VMware, VirtualBox, WSL 또는 컨테이너 식별 문자열");
        }
        else if (candidate.InterfaceType ==
                 NetworkInterfaceType.Wireless80211)
        {
            role = NetworkAdapterRole.PhysicalWireless;
            reasons.Add("NetworkInterfaceType.Wireless80211");
        }
        else if (candidate.InterfaceType == NetworkInterfaceType.Ethernet
                 || candidate.InterfaceType ==
                    NetworkInterfaceType.GigabitEthernet
                 || candidate.InterfaceType ==
                    NetworkInterfaceType.FastEthernetFx
                 || candidate.InterfaceType ==
                    NetworkInterfaceType.FastEthernetT)
        {
            role = ContainsAny(searchable, OtherVirtualMarkers)
                ? NetworkAdapterRole.OtherVirtual
                : NetworkAdapterRole.PhysicalEthernet;
            reasons.Add(role == NetworkAdapterRole.OtherVirtual
                ? "일반 가상 어댑터 식별 문자열"
                : "Ethernet 계열 인터페이스 유형");
        }
        else if (candidate.InterfaceType ==
                 NetworkInterfaceType.Ethernet3Megabit
                 && ContainsAny(searchable, OtherVirtualMarkers))
        {
            role = NetworkAdapterRole.OtherVirtual;
            reasons.Add("일반 가상 어댑터 식별 문자열");
        }
        else if (candidate.InterfaceType == NetworkInterfaceType.Unknown
                 && ContainsAny(searchable, OtherVirtualMarkers))
        {
            role = NetworkAdapterRole.OtherVirtual;
            reasons.Add("Unknown 유형과 가상 어댑터 식별 문자열");
        }
        else
        {
            role = NetworkAdapterRole.Unknown;
            reasons.Add(
                $"분류되지 않은 인터페이스 유형: {candidate.InterfaceType}");
        }

        int score = role == NetworkAdapterRole.PhysicalWireless
            ? ScorePhysicalWireless(candidate, reasons)
            : int.MinValue;

        return new ClassifiedNetworkAdapter(
            candidate,
            role,
            score,
            reasons);
    }

    private static int ScorePhysicalWireless(
        NetworkAdapterCandidate candidate,
        ICollection<string> reasons)
    {
        int score = 100;

        if (candidate.IsNativeWlanConnected)
        {
            score += 100;
            reasons.Add("Native WLAN 현재 연결 인터페이스와 일치");
        }

        if (candidate.OperationalStatus == OperationalStatus.Up)
        {
            score += 40;
            reasons.Add("OperationalStatus.Up");
        }

        if (candidate.HasDefaultGateway)
        {
            score += 20;
            reasons.Add("기본 게이트웨이 존재");
        }

        if (candidate.HasUnicastAddress)
        {
            score += 10;
            reasons.Add("유니캐스트 주소 존재");
        }

        if (candidate.SpeedBitsPerSecond > 0)
        {
            score += 5;
            reasons.Add("링크 속도 값 존재");
        }

        return score;
    }

    private static List<string> BuildWarnings(
        IReadOnlyList<ClassifiedNetworkAdapter> inventory,
        IReadOnlyList<ClassifiedNetworkAdapter> connectedWireless)
    {
        List<string> warnings = [];

        int activeVpn = inventory.Count(item =>
            item.Role == NetworkAdapterRole.VpnOrTunnel
            && item.Candidate.OperationalStatus == OperationalStatus.Up);
        if (activeVpn > 0)
        {
            warnings.Add(
                $"활성 VPN·터널 어댑터가 {activeVpn}개입니다. 외부 경로와 기본 라우팅이 VPN 정책의 영향을 받을 수 있습니다.");
        }

        int activeVirtual = inventory.Count(item =>
            (item.Role is NetworkAdapterRole.VirtualSwitch
                or NetworkAdapterRole.WiFiDirectOrHosted
                or NetworkAdapterRole.OtherVirtual)
            && item.Candidate.OperationalStatus == OperationalStatus.Up);
        if (activeVirtual > 0)
        {
            warnings.Add(
                $"활성 가상·Wi-Fi Direct 어댑터가 {activeVirtual}개입니다. 인터페이스 전체 카운터 비교 시 실제 Wi-Fi GUID를 사용해야 합니다.");
        }

        if (connectedWireless.Count > 1)
        {
            warnings.Add(
                $"Up 상태의 물리 Wi-Fi 후보가 {connectedWireless.Count}개입니다. 다중 무선 어댑터 환경입니다.");
        }

        return warnings;
    }

    private static bool ContainsAny(
        string value,
        IEnumerable<string> markers) =>
        markers.Any(marker => value.Contains(
            marker,
            StringComparison.OrdinalIgnoreCase));

    private static string DisplayName(
        NetworkAdapterCandidate candidate) =>
        string.IsNullOrWhiteSpace(candidate.Name)
            ? candidate.Description
            : candidate.Name;

    private static int RoleSortOrder(NetworkAdapterRole role) =>
        role switch
        {
            NetworkAdapterRole.PhysicalWireless => 0,
            NetworkAdapterRole.PhysicalEthernet => 1,
            NetworkAdapterRole.VpnOrTunnel => 2,
            NetworkAdapterRole.VirtualSwitch => 3,
            NetworkAdapterRole.WiFiDirectOrHosted => 4,
            NetworkAdapterRole.OtherVirtual => 5,
            NetworkAdapterRole.Bluetooth => 6,
            NetworkAdapterRole.Loopback => 7,
            _ => 8
        };
}
