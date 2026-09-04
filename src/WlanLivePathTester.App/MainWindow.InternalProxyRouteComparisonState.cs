namespace WlanLivePathTester.App;

public partial class MainWindow
{
    internal bool HasLatestInternalProxyRouteComparison =>
        _lastInternalDirectRouteEvidence is not null
        && _lastProxyEndpointRouteAnalysis is not null
        && _lastInternalProxyRouteComparison is not null
        && _lastInternalProxyRouteComparisonFinding is not null;
}
