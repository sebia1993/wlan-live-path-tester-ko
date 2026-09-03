namespace WlanLivePathTester.Windows.Proxy;

public sealed record CurrentUserProxySettings(
    bool ReadSucceeded,
    int? Win32Error,
    bool AutoDetectEnabled,
    string? AutoConfigUrl,
    string? ManualProxy,
    string? BypassList)
{
    public string Mode
    {
        get
        {
            if (!ReadSucceeded)
            {
                return "읽기 실패";
            }

            if (AutoDetectEnabled && AutoConfigUrl is not null)
            {
                return "자동 감지 + PAC";
            }

            if (AutoDetectEnabled)
            {
                return "WPAD 자동 감지";
            }

            if (AutoConfigUrl is not null)
            {
                return "PAC";
            }

            if (ManualProxy is not null)
            {
                return "수동 프록시";
            }

            return "DIRECT 또는 별도 네트워크 정책";
        }
    }
}
