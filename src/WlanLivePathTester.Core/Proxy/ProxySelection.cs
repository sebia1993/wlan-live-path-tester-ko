using WlanLivePathTester.Core.Models;

namespace WlanLivePathTester.Core.Proxy;

internal sealed record ProxyRouteHop(ProxyRouteKind Kind, string? ProxyUri);

internal sealed class ProxySelection
{
    private ProxySelection(
        IReadOnlyList<ProxyRouteHop> hops,
        bool wasBypassed,
        int invalidDirectiveCount,
        string? error)
    {
        Hops = hops;
        WasBypassed = wasBypassed;
        InvalidDirectiveCount = invalidDirectiveCount;
        Error = error;
    }

    internal IReadOnlyList<ProxyRouteHop> Hops { get; }

    internal bool WasBypassed { get; }

    internal int InvalidDirectiveCount { get; }

    internal string? Error { get; }

    internal ProxyRouteKind RouteKind =>
        Hops.Count == 0 ? ProxyRouteKind.Unknown : Hops[0].Kind;

    internal int ProxyCandidateCount =>
        Hops.Count(item => item.Kind == ProxyRouteKind.Proxy);

    internal bool HasDirectFallback =>
        RouteKind == ProxyRouteKind.Proxy
        && Hops.Skip(1).Any(item => item.Kind == ProxyRouteKind.Direct);

    internal IReadOnlyList<string> ProxyUris =>
        Hops
            .Where(item => item.Kind == ProxyRouteKind.Proxy && item.ProxyUri is not null)
            .Select(item => item.ProxyUri!)
            .ToArray();

    internal static ProxySelection Direct(bool wasBypassed = false) =>
        new(
            [new ProxyRouteHop(ProxyRouteKind.Direct, null)],
            wasBypassed,
            invalidDirectiveCount: 0,
            error: null);

    internal static ProxySelection FromHops(
        IReadOnlyList<ProxyRouteHop> hops,
        int invalidDirectiveCount) =>
        new(hops, wasBypassed: false, invalidDirectiveCount, error: null);

    internal static ProxySelection Unknown(
        string error,
        int invalidDirectiveCount = 0) =>
        new([], wasBypassed: false, invalidDirectiveCount, error);
}
