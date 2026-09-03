using WlanLivePathTester.Core.Models;

namespace WlanLivePathTester.Windows.Wlan;

public enum WlanReadStatus
{
    Success,
    UnsupportedPlatform,
    NoWirelessInterfaces,
    NotConnected,
    AccessDenied,
    NativeError
}

public sealed record WlanReadResult(
    WlanReadStatus Status,
    IReadOnlyList<WlanSnapshot> Interfaces,
    uint? NativeErrorCode,
    string Message)
{
    public WlanSnapshot? FirstConnectedInterface =>
        Interfaces.FirstOrDefault(item => item.IsConnected && item.Ssid is not null);
}
