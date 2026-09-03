namespace WlanLivePathTester.Windows.Proxy;

internal sealed record CurrentUserProxyConfiguration(
    bool ReadSucceeded,
    int? Win32Error,
    bool AutoDetectEnabled,
    string? AutoConfigUrl,
    string? ManualProxy,
    string? BypassList);
