using System.Net.NetworkInformation;
using System.Net.Sockets;
using WlanLivePathTester.Core.Adapters;

namespace WlanLivePathTester.Windows.Adapters;

public sealed record NetworkAdapterInventoryReadResult(
    IReadOnlyList<NetworkAdapterCandidate> Adapters,
    IReadOnlyList<string> Warnings);

public static class NetworkAdapterInventoryReader
{
    public static NetworkAdapterInventoryReadResult Read(
        string? connectedNativeWlanInterfaceId = null)
    {
        string normalizedConnectedId = NormalizeInterfaceId(
            connectedNativeWlanInterfaceId);
        List<NetworkAdapterCandidate> adapters = [];
        List<string> warnings = [];

        NetworkInterface[] interfaces;
        try
        {
            interfaces = NetworkInterface.GetAllNetworkInterfaces();
        }
        catch (NetworkInformationException exception)
        {
            warnings.Add(
                $"Windows 네트워크 인터페이스 목록을 읽지 못했습니다: {exception.ErrorCode}");
            return new NetworkAdapterInventoryReadResult(adapters, warnings);
        }
        catch (Exception exception) when (
            exception is PlatformNotSupportedException
                or InvalidOperationException)
        {
            warnings.Add(
                $"네트워크 인터페이스 목록을 읽지 못했습니다: {exception.GetType().Name}");
            return new NetworkAdapterInventoryReadResult(adapters, warnings);
        }

        foreach (NetworkInterface networkInterface in interfaces)
        {
            try
            {
                adapters.Add(ReadOne(
                    networkInterface,
                    normalizedConnectedId));
            }
            catch (Exception exception) when (
                exception is NetworkInformationException
                    or InvalidOperationException
                    or NotSupportedException
                    or ObjectDisposedException)
            {
                string safeName = string.IsNullOrWhiteSpace(networkInterface.Name)
                    ? "이름 없는 인터페이스"
                    : networkInterface.Name;
                warnings.Add(
                    $"'{safeName}' 인터페이스의 일부 속성을 읽지 못해 목록에서 제외했습니다: {exception.GetType().Name}");
            }
        }

        return new NetworkAdapterInventoryReadResult(
            adapters
                .OrderBy(adapter => adapter.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(adapter => adapter.Id, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            warnings);
    }

    private static NetworkAdapterCandidate ReadOne(
        NetworkInterface networkInterface,
        string normalizedConnectedId)
    {
        IPInterfaceProperties ipProperties =
            networkInterface.GetIPProperties();
        IPv4InterfaceProperties? ipv4Properties = null;
        IPv6InterfaceProperties? ipv6Properties = null;

        try
        {
            ipv4Properties = ipProperties.GetIPv4Properties();
        }
        catch (NetworkInformationException)
        {
            // IPv4 may be disabled for this interface.
        }

        try
        {
            ipv6Properties = ipProperties.GetIPv6Properties();
        }
        catch (NetworkInformationException)
        {
            // IPv6 may be disabled for this interface.
        }

        bool hasUnicastAddress = ipProperties.UnicastAddresses.Any(address =>
            address.Address.AddressFamily is AddressFamily.InterNetwork
                or AddressFamily.InterNetworkV6
            && !address.Address.IsIPv6LinkLocal);
        bool hasDefaultGateway = ipProperties.GatewayAddresses.Any(gateway =>
            !gateway.Address.Equals(System.Net.IPAddress.Any)
            && !gateway.Address.Equals(System.Net.IPAddress.IPv6Any));
        string normalizedId = NormalizeInterfaceId(networkInterface.Id);

        return new NetworkAdapterCandidate(
            Id: string.IsNullOrWhiteSpace(networkInterface.Id)
                ? "[인터페이스 ID 없음]"
                : networkInterface.Id,
            Name: networkInterface.Name ?? string.Empty,
            Description: networkInterface.Description ?? string.Empty,
            InterfaceType: networkInterface.NetworkInterfaceType,
            OperationalStatus: networkInterface.OperationalStatus,
            SpeedBitsPerSecond: Math.Max(0, networkInterface.Speed),
            HasUnicastAddress: hasUnicastAddress,
            HasDefaultGateway: hasDefaultGateway,
            IsNativeWlanConnected: !string.IsNullOrWhiteSpace(
                    normalizedConnectedId)
                && normalizedId.Equals(
                    normalizedConnectedId,
                    StringComparison.OrdinalIgnoreCase),
            IPv4InterfaceIndex: ipv4Properties?.Index,
            IPv6InterfaceIndex: ipv6Properties?.Index);
    }

    public static string NormalizeInterfaceId(string? value)
    {
        string trimmed = (value ?? string.Empty).Trim();
        if (Guid.TryParse(trimmed, out Guid guid))
        {
            return guid.ToString("D");
        }

        return trimmed.Trim('{', '}').ToLowerInvariant();
    }
}
