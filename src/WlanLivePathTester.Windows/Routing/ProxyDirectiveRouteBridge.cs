using System.Runtime.Versioning;
using WlanLivePathTester.Core.Proxy;
using WlanLivePathTester.Core.Routing;

namespace WlanLivePathTester.Windows.Routing;

[SupportedOSPlatform("windows")]
public sealed class ProxyDirectiveRouteBridge
{
    private readonly ProxyEndpointRouteAnalyzer _routeAnalyzer;

    public ProxyDirectiveRouteBridge()
        : this(new ProxyEndpointRouteAnalyzer())
    {
    }

    public ProxyDirectiveRouteBridge(
        ProxyEndpointRouteAnalyzer routeAnalyzer)
    {
        _routeAnalyzer = routeAnalyzer
            ?? throw new ArgumentNullException(nameof(routeAnalyzer));
    }

    public Task<
        ProxyDirectiveRouteAnalysisExecutionResult<
            ProxyEndpointRouteAnalysisResult>> ExecuteAsync(
        ProxyDirectiveSourceSelectionResult selection,
        Uri targetUri,
        string? expectedWlanInterfaceId,
        int dnsTimeoutSeconds = 5,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(targetUri);

        return ProxyDirectiveRouteAnalysisExecutor.ExecuteAsync(
            selection,
            async (directiveText, token) =>
            {
                ProxyEndpointParseResult parsed =
                    ProxyEndpointParser.Parse(
                        directiveText,
                        targetUri);
                return await _routeAnalyzer.AnalyzeAsync(
                        parsed,
                        expectedWlanInterfaceId,
                        dnsTimeoutSeconds,
                        token)
                    .ConfigureAwait(false);
            },
            cancellationToken);
    }
}
