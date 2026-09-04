using System.Buffers.Binary;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using WlanLivePathTester.Core.NetworkEnvironment;
using WlanLivePathTester.Core.Routing;

namespace WlanLivePathTester.Windows.Routing;

public sealed record WindowsBestInterfaceResult(
    RouteAddressEvidenceStatus Status,
    RouteInterfaceDescriptor? Interface,
    uint? NativeErrorCode,
    string Message);

[SupportedOSPlatform("windows")]
public static class WindowsBestInterfaceResolver
{
    private const ushort AddressFamilyInterNetwork = 2;
    private const ushort AddressFamilyInterNetworkV6 = 23;
    private const uint ErrorSuccess = 0;

    public static WindowsBestInterfaceResult Resolve(IPAddress destination)
    {
        ArgumentNullException.ThrowIfNull(destination);

        if (!OperatingSystem.IsWindows())
        {
            return new WindowsBestInterfaceResult(
                RouteAddressEvidenceStatus.Failed,
                Interface: null,
                NativeErrorCode: null,
                Message: "Windows에서만 최적 라우팅 인터페이스를 확인할 수 있습니다.");
        }

        if (destination.AddressFamily is not AddressFamily.InterNetwork
            and not AddressFamily.InterNetworkV6)
        {
            return new WindowsBestInterfaceResult(
                RouteAddressEvidenceStatus.Failed,
                Interface: null,
                NativeErrorCode: null,
                Message: "IPv4 또는 IPv6 대상만 지원합니다.");
        }

        byte[] socketAddress = BuildSocketAddress(destination);
        nint addressPointer = Marshal.AllocHGlobal(socketAddress.Length);
        try
        {
            Marshal.Copy(
                socketAddress,
                startIndex: 0,
                addressPointer,
                socketAddress.Length);
            uint nativeResult = GetBestInterfaceEx(
                addressPointer,
                out uint interfaceIndex);
            if (nativeResult != ErrorSuccess)
            {
                return new WindowsBestInterfaceResult(
                    RouteAddressEvidenceStatus.RouteNotFound,
                    Interface: null,
                    NativeErrorCode: nativeResult,
                    Message: $"Windows GetBestInterfaceEx가 오류 {nativeResult}를 반환했습니다.");
            }

            return MapInterface(
                interfaceIndex,
                destination.AddressFamily);
        }
        catch (Exception exception) when (
            exception is DllNotFoundException
                or EntryPointNotFoundException
                or BadImageFormatException
                or NetworkInformationException
                or InvalidOperationException
                or OverflowException)
        {
            return new WindowsBestInterfaceResult(
                RouteAddressEvidenceStatus.Failed,
                Interface: null,
                NativeErrorCode: null,
                Message: $"Windows 최적 인터페이스 확인 중 오류가 발생했습니다: {exception.GetType().Name}");
        }
        finally
        {
            Marshal.FreeHGlobal(addressPointer);
        }
    }

    private static WindowsBestInterfaceResult MapInterface(
        uint interfaceIndex,
        AddressFamily addressFamily)
    {
        List<NetworkInterface> matches = [];
        foreach (NetworkInterface networkInterface in
                 NetworkInterface.GetAllNetworkInterfaces())
        {
            try
            {
                IPInterfaceProperties properties =
                    networkInterface.GetIPProperties();
                int? candidateIndex = addressFamily
                    == AddressFamily.InterNetwork
                    ? TryGetIPv4Index(properties)
                    : TryGetIPv6Index(properties);
                if (candidateIndex.HasValue
                    && candidateIndex.Value == interfaceIndex)
                {
                    matches.Add(networkInterface);
                }
            }
            catch (Exception exception) when (
                exception is NetworkInformationException
                    or PlatformNotSupportedException
                    or InvalidOperationException
                    or ObjectDisposedException)
            {
                // Another interface may still match the native index.
            }
        }

        if (matches.Count == 0)
        {
            return new WindowsBestInterfaceResult(
                RouteAddressEvidenceStatus.InterfaceNotFound,
                Interface: null,
                NativeErrorCode: null,
                Message: "Windows가 반환한 최적 인터페이스 인덱스를 로컬 NetworkInterface 목록에서 찾지 못했습니다.");
        }

        if (matches.Count > 1)
        {
            return new WindowsBestInterfaceResult(
                RouteAddressEvidenceStatus.InterfaceAmbiguous,
                Interface: null,
                NativeErrorCode: null,
                Message: $"같은 Windows 인터페이스 인덱스를 가진 로컬 후보가 {matches.Count}개라 하나를 선택하지 않았습니다.");
        }

        NetworkInterface selected = matches[0];
        NetworkAdapterClassification classification =
            NetworkAdapterClassifier.Classify(
                selected.NetworkInterfaceType.ToString(),
                selected.Name,
                selected.Description);
        bool hasDefaultGateway = false;
        try
        {
            hasDefaultGateway = selected.GetIPProperties()
                .GatewayAddresses
                .Select(item => item.Address)
                .Any(IsUsableGateway);
        }
        catch (Exception exception) when (
            exception is NetworkInformationException
                or PlatformNotSupportedException
                or InvalidOperationException
                or ObjectDisposedException)
        {
            // Route selection remains valid even if gateway metadata is unavailable.
        }

        RouteInterfaceDescriptor descriptor = new(
            InterfaceIdentity: NormalizeInterfaceIdentity(selected.Id),
            DisplayName: SafeText(selected.Name, "이름 없는 인터페이스"),
            Description: SafeText(selected.Description, "설명 없음"),
            NativeInterfaceType: selected.NetworkInterfaceType.ToString(),
            Category: classification.Category,
            OperationalState: MapOperationalState(
                selected.OperationalStatus),
            HasDefaultGateway: hasDefaultGateway,
            IsVirtual: classification.IsVirtual,
            IsVpn: classification.IsVpn);

        return new WindowsBestInterfaceResult(
            RouteAddressEvidenceStatus.Success,
            descriptor,
            NativeErrorCode: null,
            Message: "Windows GetBestInterfaceEx가 선택한 로컬 인터페이스를 확인했습니다.");
    }

    private static byte[] BuildSocketAddress(IPAddress destination)
    {
        if (destination.AddressFamily == AddressFamily.InterNetwork)
        {
            byte[] result = new byte[16];
            BinaryPrimitives.WriteUInt16LittleEndian(
                result.AsSpan(0, 2),
                AddressFamilyInterNetwork);
            destination.GetAddressBytes().CopyTo(result, 4);
            return result;
        }

        byte[] ipv6Result = new byte[28];
        BinaryPrimitives.WriteUInt16LittleEndian(
            ipv6Result.AsSpan(0, 2),
            AddressFamilyInterNetworkV6);
        destination.GetAddressBytes().CopyTo(ipv6Result, 8);
        BinaryPrimitives.WriteUInt32LittleEndian(
            ipv6Result.AsSpan(24, 4),
            checked((uint)Math.Max(0, destination.ScopeId)));
        return ipv6Result;
    }

    private static int? TryGetIPv4Index(
        IPInterfaceProperties properties)
    {
        try
        {
            return properties.GetIPv4Properties()?.Index;
        }
        catch (Exception exception) when (
            exception is NetworkInformationException
                or PlatformNotSupportedException)
        {
            return null;
        }
    }

    private static int? TryGetIPv6Index(
        IPInterfaceProperties properties)
    {
        try
        {
            return properties.GetIPv6Properties()?.Index;
        }
        catch (Exception exception) when (
            exception is NetworkInformationException
                or PlatformNotSupportedException)
        {
            return null;
        }
    }

    private static bool IsUsableGateway(IPAddress address)
    {
        if (IPAddress.IsLoopback(address)
            || address.Equals(IPAddress.Any)
            || address.Equals(IPAddress.IPv6Any)
            || address.Equals(IPAddress.None)
            || address.Equals(IPAddress.IPv6None))
        {
            return false;
        }

        return address.GetAddressBytes().Any(value => value != 0);
    }

    private static string NormalizeInterfaceIdentity(string? value)
    {
        string normalized = RouteInterfaceFingerprint.Normalize(value);
        return string.IsNullOrWhiteSpace(normalized)
            ? "interface-id-unavailable"
            : normalized;
    }

    private static NetworkAdapterOperationalState MapOperationalState(
        OperationalStatus status) =>
        status switch
        {
            OperationalStatus.Up => NetworkAdapterOperationalState.Up,
            OperationalStatus.Down => NetworkAdapterOperationalState.Down,
            OperationalStatus.Dormant => NetworkAdapterOperationalState.Dormant,
            OperationalStatus.LowerLayerDown =>
                NetworkAdapterOperationalState.LowerLayerDown,
            OperationalStatus.Testing => NetworkAdapterOperationalState.Testing,
            _ => NetworkAdapterOperationalState.Unknown
        };

    private static string SafeText(string? value, string fallback)
    {
        string normalized = (value ?? string.Empty)
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Replace('\t', ' ')
            .Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return fallback;
        }

        return normalized.Length <= 160
            ? normalized
            : normalized[..157] + "...";
    }

    [DllImport("iphlpapi.dll", ExactSpelling = true)]
    private static extern uint GetBestInterfaceEx(
        nint pDestAddr,
        out uint pdwBestIfIndex);
}
