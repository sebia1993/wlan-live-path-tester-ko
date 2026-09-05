using System.Text.Json.Serialization;
using WlanLivePathTester.Core.Proxy;

namespace WlanLivePathTester.Core.Routing;

public enum InternalProxyRouteComparisonRunStatus
{
    InvalidInput,
    ProxySourceBlocked,
    ProxySourceUnavailable,
    DirectPathSelected,
    InternalRouteUnavailable,
    Completed,
    Canceled,
    Failed
}

public sealed record InternalProxyRouteComparisonRunResult(
    DateTimeOffset CompletedAt,
    InternalProxyRouteComparisonRunStatus Status,
    ProxyDirectiveSourceKind ProxySourceKind,
    ProxyDirectiveSourceSelectionStatus ProxySelectionStatus,
    ProxyDirectiveRouteAnalysisPlanStatus ProxyPlanStatus,
    ProxyDirectiveRouteAnalysisPlanCode ProxyPlanCode,
    ProxyDirectiveRouteAnalysisExecutionStatus? ProxyExecutionStatus,
    ProxyEndpointSourceKind ProxyEndpointSourceKind,
    ProxyEndpointDecision ProxyDecision,
    string? TargetScheme,
    DestinationRouteEvidenceStatus? InternalRouteStatus,
    ProxyEndpointRouteAnalysisStatus? ProxyRouteStatus,
    InternalProxyRouteComparisonResult? Comparison,
    int ParsedProxyEndpointCount,
    int ApplicableProxyEndpointCount,
    int AnalyzedProxyEndpointCount,
    int SuccessfulProxyEndpointCount,
    int DistinctProxyInterfaceCount,
    bool DirectPresent,
    bool DirectIsPrimary,
    bool DirectFallback,
    bool ProxyParseErrorsPresent,
    bool ExpectedWlanIdentityAvailable,
    bool InternalRouteReadPerformed,
    bool ProxyRouteAnalysisPerformed,
    string Message,
    string Limitation,
    [property: JsonIgnore]
    DestinationRouteEvidence? InternalRouteEvidence,
    [property: JsonIgnore]
    ProxyDirectiveRouteAnalysisExecutionResult<
        ProxyEndpointRouteAnalysisResult>? ProxyExecution)
{
    public bool OperationCompleted => Status is
        InternalProxyRouteComparisonRunStatus.DirectPathSelected
            or InternalProxyRouteComparisonRunStatus.Completed;

    public bool HasComparableResult => Comparison is not null;
}
