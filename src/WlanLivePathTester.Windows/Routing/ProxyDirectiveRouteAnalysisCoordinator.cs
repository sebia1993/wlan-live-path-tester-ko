using System.Runtime.Versioning;
using WlanLivePathTester.Core.Proxy;
using WlanLivePathTester.Core.Routing;

namespace WlanLivePathTester.Windows.Routing;

[SupportedOSPlatform("windows")]
public sealed class ProxyDirectiveRouteAnalysisCoordinator
{
    private readonly ProxyEndpointRouteAnalyzer _routeAnalyzer;

    public ProxyDirectiveRouteAnalysisCoordinator()
        : this(new ProxyEndpointRouteAnalyzer())
    {
    }

    public ProxyDirectiveRouteAnalysisCoordinator(
        ProxyEndpointRouteAnalyzer routeAnalyzer)
    {
        _routeAnalyzer = routeAnalyzer
            ?? throw new ArgumentNullException(nameof(routeAnalyzer));
    }

    public Task<ProxyDirectiveRouteAnalysisExecutionResult<
        ProxyEndpointRouteAnalysisResult>> ExecuteAsync(
            ProxyDirectiveSourceSnapshot snapshot,
            Uri targetUri,
            string? expectedWlanInterfaceId,
            int dnsTimeoutSeconds = 5,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ProxyDirectiveSourceSelectionResult selection =
            ProxyDirectiveSourceSnapshotSelectionPolicy.Select(snapshot);
        return ExecuteAsync(
            selection,
            targetUri,
            expectedWlanInterfaceId,
            dnsTimeoutSeconds,
            cancellationToken);
    }

    public Task<ProxyDirectiveRouteAnalysisExecutionResult<
        ProxyEndpointRouteAnalysisResult>> ExecuteAsync(
            ProxyDirectiveSourceSelectionResult selection,
            Uri targetUri,
            string? expectedWlanInterfaceId,
            int dnsTimeoutSeconds = 5,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(selection);
        ValidateTargetUri(targetUri);
        if (dnsTimeoutSeconds is < 1 or > 30)
        {
            throw new ArgumentOutOfRangeException(
                nameof(dnsTimeoutSeconds),
                "DNS 제한 시간은 1~30초 범위여야 합니다.");
        }

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

    private static void ValidateTargetUri(Uri targetUri)
    {
        ArgumentNullException.ThrowIfNull(targetUri);
        if (!targetUri.IsAbsoluteUri
            || (!targetUri.Scheme.Equals(
                    Uri.UriSchemeHttp,
                    StringComparison.OrdinalIgnoreCase)
                && !targetUri.Scheme.Equals(
                    Uri.UriSchemeHttps,
                    StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException(
                "프록시 경로 분석 대상은 절대 HTTP 또는 HTTPS URL이어야 합니다.",
                nameof(targetUri));
        }
    }
}
