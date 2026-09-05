using System.Runtime.Versioning;
using WlanLivePathTester.Core.Proxy;
using WlanLivePathTester.Windows.Routing;

namespace WlanLivePathTester.Windows.Proxy;

[SupportedOSPlatform("windows")]
public static class WindowsProxyDirectiveRouteAnalysisExtensions
{
    public static Task<
        WindowsProxyDirectiveSourceExecutionResult<
            ProxyEndpointRouteAnalysisResult>>
        ReadAndAnalyzeRoutesAsync(
            this WindowsProxyDirectiveSourceExecutionCoordinator
                coordinator,
            Uri targetUri,
            ProxyEndpointRouteAnalyzer routeAnalyzer,
            string? expectedWlanInterfaceId,
            int dnsTimeoutSeconds = 5,
            int endpointLimit =
                ProxyEndpointRouteAnalyzer.DefaultEndpointLimit,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(coordinator);
        ArgumentNullException.ThrowIfNull(routeAnalyzer);

        return coordinator.ReadAndExecuteAsync(
            targetUri,
            (directiveText, token) =>
                routeAnalyzer.AnalyzeAsync(
                    directiveText,
                    expectedWlanInterfaceId,
                    dnsTimeoutSeconds,
                    endpointLimit,
                    token),
            cancellationToken);
    }
}
