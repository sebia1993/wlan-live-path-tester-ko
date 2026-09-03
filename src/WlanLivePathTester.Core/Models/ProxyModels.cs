namespace WlanLivePathTester.Core.Models;

public enum ProxyRouteKind
{
    Direct,
    Proxy,
    Unknown
}

public enum ProxyPathExpectation
{
    Match,
    Mismatch,
    Unknown
}

public static class ProxyRouteExpectationEvaluator
{
    public static ProxyPathExpectation Evaluate(
        NetworkPathKind pathKind,
        ProxyRouteKind routeKind)
    {
        return routeKind switch
        {
            ProxyRouteKind.Unknown => ProxyPathExpectation.Unknown,
            ProxyRouteKind.Direct when pathKind == NetworkPathKind.Internal =>
                ProxyPathExpectation.Match,
            ProxyRouteKind.Proxy when pathKind == NetworkPathKind.External =>
                ProxyPathExpectation.Match,
            _ => ProxyPathExpectation.Mismatch
        };
    }
}
