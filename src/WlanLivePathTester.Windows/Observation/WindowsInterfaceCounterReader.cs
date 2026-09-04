using System.Net.NetworkInformation;
using System.Runtime.Versioning;
using WlanLivePathTester.Core.Observation;

namespace WlanLivePathTester.Windows.Observation;

[SupportedOSPlatform("windows")]
public static class WindowsInterfaceCounterReader
{
    public static InterfaceCounterReadResult ReadCurrent(
        string? preferredInterfaceId = null,
        string? preferredInterfaceDescription = null)
    {
        if (!OperatingSystem.IsWindows())
        {
            return new InterfaceCounterReadResult(
                InterfaceCounterReadStatus.UnsupportedPlatform,
                null,
                "Windows에서만 Wi-Fi 인터페이스 카운터를 읽을 수 있습니다.");
        }

        try
        {
            NetworkInterface[] interfaces = NetworkInterface.GetAllNetworkInterfaces();
            NetworkInterface? selected = SelectInterface(
                interfaces,
                preferredInterfaceId,
                preferredInterfaceDescription);

            if (selected is null)
            {
                return new InterfaceCounterReadResult(
                    InterfaceCounterReadStatus.InterfaceNotFound,
                    null,
                    "관찰할 Wi-Fi 인터페이스를 찾지 못했습니다. 무선 연결 상태를 확인하십시오.");
            }

            IPInterfaceStatistics statistics = selected.GetIPStatistics();
            return new InterfaceCounterReadResult(
                InterfaceCounterReadStatus.Success,
                new InterfaceCounterSnapshot(
                    Timestamp: DateTimeOffset.UtcNow,
                    InterfaceId: NormalizeInterfaceId(selected.Id),
                    InterfaceName: selected.Name,
                    InterfaceDescription: selected.Description,
                    BytesReceived: statistics.BytesReceived,
                    BytesSent: statistics.BytesSent,
                    IsOperational: selected.OperationalStatus == OperationalStatus.Up),
                selected.OperationalStatus == OperationalStatus.Up
                    ? "Wi-Fi 인터페이스 누적 바이트를 읽었습니다."
                    : "Wi-Fi 인터페이스 카운터를 읽었지만 현재 동작 상태는 Up이 아닙니다.");
        }
        catch (NetworkInformationException exception)
        {
            return new InterfaceCounterReadResult(
                InterfaceCounterReadStatus.StatisticsUnavailable,
                null,
                $"Wi-Fi 인터페이스 통계를 읽지 못했습니다: {exception.ErrorCode}");
        }
        catch (PlatformNotSupportedException)
        {
            return new InterfaceCounterReadResult(
                InterfaceCounterReadStatus.UnsupportedPlatform,
                null,
                "현재 운영체제에서 네트워크 인터페이스 통계를 지원하지 않습니다.");
        }
        catch (Exception exception)
        {
            return new InterfaceCounterReadResult(
                InterfaceCounterReadStatus.Failed,
                null,
                $"Wi-Fi 인터페이스 카운터 확인 중 오류가 발생했습니다: {exception.Message}");
        }
    }

    private static NetworkInterface? SelectInterface(
        IEnumerable<NetworkInterface> interfaces,
        string? preferredInterfaceId,
        string? preferredInterfaceDescription)
    {
        NetworkInterface[] wireless = interfaces
            .Where(item => item.NetworkInterfaceType == NetworkInterfaceType.Wireless80211)
            .ToArray();

        if (!string.IsNullOrWhiteSpace(preferredInterfaceId))
        {
            string normalizedPreferredId = NormalizeInterfaceId(preferredInterfaceId);
            NetworkInterface? byId = wireless.FirstOrDefault(item =>
                string.Equals(
                    NormalizeInterfaceId(item.Id),
                    normalizedPreferredId,
                    StringComparison.OrdinalIgnoreCase));
            if (byId is not null)
            {
                return byId;
            }
        }

        if (!string.IsNullOrWhiteSpace(preferredInterfaceDescription))
        {
            NetworkInterface? byDescription = wireless.FirstOrDefault(item =>
                string.Equals(
                    item.Description.Trim(),
                    preferredInterfaceDescription.Trim(),
                    StringComparison.OrdinalIgnoreCase));
            if (byDescription is not null)
            {
                return byDescription;
            }
        }

        return wireless.FirstOrDefault(item => item.OperationalStatus == OperationalStatus.Up)
            ?? wireless.FirstOrDefault();
    }

    private static string NormalizeInterfaceId(string value) =>
        value.Trim().Trim('{', '}');
}
