namespace WlanLivePathTester.Core.Proxy;

public static class ProxyDirectiveSourceExecutionPipeline
{
    public static Task<ProxyDirectiveRouteAnalysisExecutionResult<TAnalysis>>
        ExecuteAsync<TAnalysis>(
            ProxyDirectiveSourceSnapshot snapshot,
            Func<string, CancellationToken, Task<TAnalysis>> analyzer,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(analyzer);

        ProxyDirectiveSourceSelectionResult selection =
            ProxyDirectiveSourceSnapshotSelectionPolicy.Select(snapshot);
        return ProxyDirectiveRouteAnalysisExecutor.ExecuteAsync(
            selection,
            analyzer,
            cancellationToken);
    }
}
