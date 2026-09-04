namespace WlanLivePathTester.Core.NetworkEnvironment;

public static class NetworkAdapterClassifier
{
    private static readonly string[] VpnMarkers =
    [
        "vpn",
        "wireguard",
        "wintun",
        "tap-windows",
        "tap adapter",
        "anyconnect",
        "globalprotect",
        "forticlient",
        "fortinet",
        "pulse secure",
        "juniper",
        "zscaler",
        "netskope",
        "check point",
        "checkpoint",
        "sonicwall",
        "tailscale",
        "zerotier"
    ];

    private static readonly string[] VirtualMarkers =
    [
        "hyper-v",
        "vethernet",
        "virtual ethernet",
        "virtual adapter",
        "wi-fi direct virtual",
        "wifi direct virtual",
        "vmware",
        "virtualbox",
        "vbox",
        "wsl",
        "docker",
        "container",
        "npcap loopback"
    ];

    public static NetworkAdapterClassification Classify(
        string? nativeInterfaceType,
        string? displayName,
        string? description)
    {
        string searchable = string.Join(
                " ",
                displayName ?? string.Empty,
                description ?? string.Empty)
            .ToLowerInvariant();
        string nativeType = (nativeInterfaceType ?? string.Empty).Trim();

        bool isVpn = nativeType.Equals(
                "Tunnel",
                StringComparison.OrdinalIgnoreCase)
            || nativeType.Equals(
                "Ppp",
                StringComparison.OrdinalIgnoreCase)
            || ContainsAny(searchable, VpnMarkers);
        bool isVirtual = ContainsAny(searchable, VirtualMarkers)
            || isVpn;

        NetworkAdapterCategory category = nativeType switch
        {
            string value when value.Equals(
                "Wireless80211",
                StringComparison.OrdinalIgnoreCase)
                => NetworkAdapterCategory.Wireless,
            string value when value.Equals(
                    "Ethernet",
                    StringComparison.OrdinalIgnoreCase)
                || value.Equals(
                    "FastEthernetFx",
                    StringComparison.OrdinalIgnoreCase)
                || value.Equals(
                    "FastEthernetT",
                    StringComparison.OrdinalIgnoreCase)
                || value.Equals(
                    "GigabitEthernet",
                    StringComparison.OrdinalIgnoreCase)
                => NetworkAdapterCategory.Ethernet,
            string value when value.Equals(
                    "Tunnel",
                    StringComparison.OrdinalIgnoreCase)
                || value.Equals(
                    "Ppp",
                    StringComparison.OrdinalIgnoreCase)
                => NetworkAdapterCategory.Tunnel,
            string value when value.Equals(
                "Loopback",
                StringComparison.OrdinalIgnoreCase)
                => NetworkAdapterCategory.Loopback,
            _ when isVpn => NetworkAdapterCategory.Tunnel,
            _ => NetworkAdapterCategory.Other
        };

        return new NetworkAdapterClassification(
            Category: category,
            IsVirtual: isVirtual,
            IsVpn: isVpn);
    }

    private static bool ContainsAny(
        string source,
        IEnumerable<string> markers) =>
        markers.Any(marker => source.Contains(
            marker,
            StringComparison.OrdinalIgnoreCase));
}
