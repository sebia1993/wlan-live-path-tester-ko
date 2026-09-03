using WlanLivePathTester.Core.Models;

namespace WlanLivePathTester.Windows.Proxy;

public enum ProxyResolutionStatus
{
    Success,
    InvalidUrl,
    UnsupportedPlatform,
    ConfigurationReadFailed,
    ConfigurationInvalid,
    AutoProxyAuthenticationFailed,
    AutoProxyFailed,
    TimedOut,
    NativeError
}

public enum ProxyConfigurationSource
{
    Unknown,
    None,
    Manual,
    Wpad,
    Pac,
    WpadThenPac,
    ManualFallback
}

public sealed record ProxyRouteResolution(
    ProxyResolutionStatus Status,
    ProxyRouteKind RouteKind,
    ProxyConfigurationSource Source,
    ProxyPathExpectation Expectation,
    int ProxyCandidateCount,
    bool HasDirectFallback,
    bool WasBypassed,
    bool AutoLogonRetried,
    bool NetworkLookupPerformed,
    int InvalidDirectiveCount,
    int? Win32ErrorCode,
    string Message)
{
    public bool IsSuccess => Status == ProxyResolutionStatus.Success;

    public string SafeRouteSummary => RouteKind switch
    {
        ProxyRouteKind.Direct => "DIRECT",
        ProxyRouteKind.Proxy when ProxyCandidateCount <= 1 =>
            HasDirectFallback
                ? "PROXY 1개(주소 숨김), 실패 시 DIRECT"
                : "PROXY 1개(주소 숨김)",
        ProxyRouteKind.Proxy =>
            HasDirectFallback
                ? $"PROXY 후보 {ProxyCandidateCount}개(주소 숨김), 실패 시 DIRECT"
                : $"PROXY 후보 {ProxyCandidateCount}개(주소 숨김)",
        _ => "판단 불가"
    };
}
