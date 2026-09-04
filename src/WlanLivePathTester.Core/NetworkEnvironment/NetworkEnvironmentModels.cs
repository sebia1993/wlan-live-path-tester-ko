namespace WlanLivePathTester.Core.NetworkEnvironment;

public enum NetworkAdapterCategory
{
    Wireless,
    Ethernet,
    Tunnel,
    Loopback,
    Other
}

public enum NetworkAdapterOperationalState
{
    Up,
    Down,
    Dormant,
    LowerLayerDown,
    Testing,
    Unknown
}

public enum NetworkEnvironmentSeverity
{
    Information,
    Warning
}

public sealed record NetworkAdapterClassification(
    NetworkAdapterCategory Category,
    bool IsVirtual,
    bool IsVpn);

public sealed record LocalNetworkAdapterSnapshot(
    string DisplayName,
    string Description,
    string NativeInterfaceType,
    NetworkAdapterCategory Category,
    NetworkAdapterOperationalState OperationalState,
    long? SpeedBitsPerSecond,
    bool HasDefaultGateway,
    int GatewayCount,
    bool HasIpv4,
    bool HasIpv6,
    int UnicastAddressCount,
    bool SupportsMulticast,
    bool IsVirtual,
    bool IsVpn,
    string? ReadError,
    string? InterfaceId = null)
{
    public bool IsUp => OperationalState == NetworkAdapterOperationalState.Up;

    public bool IsPhysicalPathCandidate =>
        IsUp
        && Category is NetworkAdapterCategory.Wireless
            or NetworkAdapterCategory.Ethernet
        && !IsVirtual
        && !IsVpn;
}

public sealed record NetworkEnvironmentFinding(
    string Code,
    NetworkEnvironmentSeverity Severity,
    string Title,
    string Evidence,
    string Interpretation,
    string NextStep);

public sealed record NetworkEnvironmentAssessment(
    int TotalAdapterCount,
    int ActiveAdapterCount,
    int ActiveWirelessCount,
    int ActiveEthernetCount,
    int ActiveVpnCount,
    int ActiveVirtualCount,
    int ActiveDefaultGatewayCount,
    bool RouteSelectionMayBeAmbiguous,
    string? PreferredWirelessDisplayName,
    IReadOnlyList<NetworkEnvironmentFinding> Findings);

public sealed record LocalNetworkEnvironmentSnapshot(
    DateTimeOffset CapturedAt,
    IReadOnlyList<LocalNetworkAdapterSnapshot> Adapters,
    NetworkEnvironmentAssessment Assessment,
    string Message);
