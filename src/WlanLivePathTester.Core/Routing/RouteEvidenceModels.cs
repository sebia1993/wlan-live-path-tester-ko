using System.Security.Cryptography;
using System.Text;
using WlanLivePathTester.Core.NetworkEnvironment;

namespace WlanLivePathTester.Core.Routing;

public enum RouteProbePurpose
{
    InternalDirectTarget,
    ProxyEndpoint,
    ExternalTargetReference,
    ManualDestination
}

public enum RouteAddressFamilyKind
{
    IPv4,
    IPv6
}

public enum RouteAddressEvidenceStatus
{
    Success,
    RouteNotFound,
    InterfaceNotFound,
    InterfaceAmbiguous,
    Failed
}

public enum DestinationRouteEvidenceStatus
{
    Success,
    PartialSuccess,
    MultipleInterfaces,
    InvalidTarget,
    ResolutionFailed,
    RouteNotFound,
    Canceled,
    Failed
}

public sealed record RouteInterfaceDescriptor(
    string InterfaceIdentity,
    string DisplayName,
    string Description,
    string NativeInterfaceType,
    NetworkAdapterCategory Category,
    NetworkAdapterOperationalState OperationalState,
    bool HasDefaultGateway,
    bool IsVirtual,
    bool IsVpn)
{
    public bool IsUp =>
        OperationalState == NetworkAdapterOperationalState.Up;

    public string IdentityFingerprint =>
        RouteInterfaceFingerprint.Create(InterfaceIdentity);
}

public sealed record RouteAddressEvidence(
    RouteAddressFamilyKind AddressFamily,
    RouteAddressEvidenceStatus Status,
    RouteInterfaceDescriptor? Interface,
    uint? NativeErrorCode,
    string Message);

public sealed record DestinationRouteEvidence(
    DateTimeOffset CapturedAt,
    string TargetLabel,
    RouteProbePurpose Purpose,
    bool DnsWasUsed,
    int ResolvedAddressCount,
    DestinationRouteEvidenceStatus Status,
    RouteInterfaceDescriptor? SelectedInterface,
    IReadOnlyList<RouteAddressEvidence> AddressEvidence,
    IReadOnlyList<string> Warnings,
    string Message)
{
    public bool IsSuccess =>
        Status is DestinationRouteEvidenceStatus.Success
            or DestinationRouteEvidenceStatus.PartialSuccess;
}

public static class RouteInterfaceFingerprint
{
    public const int DisplayLength = 10;

    public static string Create(string? interfaceIdentity)
    {
        string normalized = Normalize(interfaceIdentity);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return "없음";
        }

        byte[] digest = SHA256.HashData(
            Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(digest)
            [..DisplayLength]
            .ToLowerInvariant();
    }

    public static string Normalize(string? interfaceIdentity)
    {
        string trimmed = (interfaceIdentity ?? string.Empty)
            .Trim()
            .Trim('{', '}');
        return Guid.TryParse(trimmed, out Guid parsed)
            ? parsed.ToString("D")
            : trimmed.ToLowerInvariant();
    }
}
