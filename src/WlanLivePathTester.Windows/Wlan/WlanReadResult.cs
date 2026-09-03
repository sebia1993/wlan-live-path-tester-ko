using WlanLivePathTester.Core.Models;

namespace WlanLivePathTester.Windows.Wlan;

public enum WlanReadStatus
{
    Success,
    UnsupportedPlatform,
    NoWirelessInterfaces,
    NotConnected,
    AccessDenied,
    ServiceNotRunning,
    NativeError
}

public sealed record WlanReadResult
{
    private const uint ErrorServiceNotActive = 1062;

    public WlanReadResult(
        WlanReadStatus status,
        IReadOnlyList<WlanSnapshot> interfaces,
        uint? nativeErrorCode,
        string message)
    {
        ArgumentNullException.ThrowIfNull(interfaces);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        Interfaces = interfaces;
        NativeErrorCode = nativeErrorCode;

        if (status == WlanReadStatus.NativeError
            && nativeErrorCode == ErrorServiceNotActive)
        {
            Status = WlanReadStatus.ServiceNotRunning;
            Message = "Windows WLAN AutoConfig 서비스가 실행 중이 아닙니다. "
                + "WLAN AutoConfig(WlanSvc) 서비스 상태를 확인한 뒤 다시 시도하십시오.";
            return;
        }

        Status = status;
        Message = message;
    }

    public WlanReadStatus Status { get; }

    public IReadOnlyList<WlanSnapshot> Interfaces { get; }

    public uint? NativeErrorCode { get; }

    public string Message { get; }

    public WlanSnapshot? FirstConnectedInterface =>
        Interfaces.FirstOrDefault(item => item.IsConnected && item.Ssid is not null);
}
