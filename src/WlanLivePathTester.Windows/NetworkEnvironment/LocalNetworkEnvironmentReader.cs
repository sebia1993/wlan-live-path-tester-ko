using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using WlanLivePathTester.Core.NetworkEnvironment;

namespace WlanLivePathTester.Windows.NetworkEnvironment;

public static class LocalNetworkEnvironmentReader
{
    public static LocalNetworkEnvironmentSnapshot ReadCurrent()
    {
        DateTimeOffset capturedAt = DateTimeOffset.UtcNow;
        List<LocalNetworkAdapterSnapshot> adapters = [];

        try
        {
            foreach (NetworkInterface networkInterface in
                     NetworkInterface.GetAllNetworkInterfaces()
                         .OrderBy(
                             item => item.Name,
                             StringComparer.OrdinalIgnoreCase))
            {
                adapters.Add(ReadAdapter(networkInterface));
            }

            NetworkEnvironmentAssessment assessment =
                NetworkEnvironmentEvaluator.Evaluate(adapters);
            return new LocalNetworkEnvironmentSnapshot(
                CapturedAt: capturedAt,
                Adapters: adapters,
                Assessment: assessment,
                Message: $"로컬 네트워크 인터페이스 {adapters.Count}개를 확인했습니다. DNS·HTTP·프록시 요청은 수행하지 않았습니다.");
        }
        catch (Exception exception) when (
            exception is NetworkInformationException
                or PlatformNotSupportedException
                or InvalidOperationException)
        {
            NetworkEnvironmentAssessment assessment =
                NetworkEnvironmentEvaluator.Evaluate(adapters);
            return new LocalNetworkEnvironmentSnapshot(
                CapturedAt: capturedAt,
                Adapters: adapters,
                Assessment: assessment,
                Message: $"로컬 네트워크 인터페이스 목록을 완전히 읽지 못했습니다: {exception.GetType().Name}");
        }
    }

    private static LocalNetworkAdapterSnapshot ReadAdapter(
        NetworkInterface networkInterface)
    {
        string nativeType = networkInterface.NetworkInterfaceType.ToString();
        NetworkAdapterClassification classification =
            NetworkAdapterClassifier.Classify(
                nativeType,
                networkInterface.Name,
                networkInterface.Description);

        long? speed = null;
        bool hasDefaultGateway = false;
        int gatewayCount = 0;
        bool hasIpv4 = false;
        bool hasIpv6 = false;
        int unicastAddressCount = 0;
        string? readError = null;

        try
        {
            speed = networkInterface.Speed > 0
                ? networkInterface.Speed
                : null;
        }
        catch (NetworkInformationException)
        {
            readError = "링크 속도를 읽지 못했습니다.";
        }

        try
        {
            IPInterfaceProperties properties =
                networkInterface.GetIPProperties();
            IPAddress[] gateways = properties.GatewayAddresses
                .Select(item => item.Address)
                .Where(IsUsableGateway)
                .ToArray();
            gatewayCount = gateways.Length;
            hasDefaultGateway = gatewayCount > 0;

            IPAddress[] unicastAddresses = properties.UnicastAddresses
                .Select(item => item.Address)
                .Where(address => !IPAddress.IsLoopback(address))
                .ToArray();
            unicastAddressCount = unicastAddresses.Length;
            hasIpv4 = unicastAddresses.Any(address =>
                address.AddressFamily == AddressFamily.InterNetwork);
            hasIpv6 = unicastAddresses.Any(address =>
                address.AddressFamily == AddressFamily.InterNetworkV6);
        }
        catch (Exception exception) when (
            exception is NetworkInformationException
                or InvalidOperationException
                or ObjectDisposedException)
        {
            readError = string.IsNullOrWhiteSpace(readError)
                ? "IP 속성을 읽지 못했습니다."
                : readError + " IP 속성을 읽지 못했습니다.";
        }

        return new LocalNetworkAdapterSnapshot(
            DisplayName: SafeDisplayText(
                networkInterface.Name,
                "이름 없는 인터페이스"),
            Description: SafeDisplayText(
                networkInterface.Description,
                "설명 없음"),
            NativeInterfaceType: nativeType,
            Category: classification.Category,
            OperationalState: MapOperationalState(
                networkInterface.OperationalStatus),
            SpeedBitsPerSecond: speed,
            HasDefaultGateway: hasDefaultGateway,
            GatewayCount: gatewayCount,
            HasIpv4: hasIpv4,
            HasIpv6: hasIpv6,
            UnicastAddressCount: unicastAddressCount,
            SupportsMulticast: networkInterface.SupportsMulticast,
            IsVirtual: classification.IsVirtual,
            IsVpn: classification.IsVpn,
            ReadError: readError);
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

        byte[] bytes = address.GetAddressBytes();
        return bytes.Any(value => value != 0);
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

    private static string SafeDisplayText(
        string? value,
        string fallback)
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
}
