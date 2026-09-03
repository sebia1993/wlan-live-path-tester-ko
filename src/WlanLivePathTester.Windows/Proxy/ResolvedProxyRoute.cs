using WlanLivePathTester.Core.Proxy;

namespace WlanLivePathTester.Windows.Proxy;

internal sealed record ResolvedProxyRoute(
    ProxyRouteResolution Summary,
    ProxySelection Selection);
