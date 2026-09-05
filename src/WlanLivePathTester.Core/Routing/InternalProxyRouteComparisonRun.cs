using System.Text.Json.Serialization;
using WlanLivePathTester.Core.Proxy;

namespace WlanLivePathTester.Core.Routing;

public enum InternalProxyRouteComparisonRunStatus
{
    InvalidInput,
    DirectPathSelected,
    InternalRouteUnavailable,
    Completed,
    Canceled,
    Failed
}

public sealed record InternalProxyRouteComparisonRunResult(
    DateTimeOffset CompletedAt,
    InternalProxyRouteComparisonRunStatus Status,
    ProxyEndpointSourceKind ProxySourceKind,
    ProxyEndpointDecision ProxyDecision,
    string? TargetScheme,
    DestinationRouteEvidenceStatus? InternalRouteStatus,
    ProxyEndpointRouteAnalysisStatus? ProxyRouteStatus,
    InternalProxyRouteComparisonResult? Comparison,
    int ParsedProxyEndpointCount,
    int AnalyzedProxyEndpointCount,
    int SuccessfulProxyEndpointCount,
    bool DirectPresent,
    bool DirectFallback,
    bool ExpectedWlanIdentityAvailable,
    bool InternalRouteReadPerformed,
    bool ProxyRouteAnalysisPerformed,
    string Message,
    string Limitation,
    [property: JsonIgnore]
    DestinationRouteEvidence? InternalRouteEvidence,
    [property: JsonIgnore]
    ProxyEndpointRouteAnalysisResult? ProxyRouteAnalysis)
{
    public bool OperationCompleted => Status is
        InternalProxyRouteComparisonRunStatus.DirectPathSelected
        or InternalProxyRouteComparisonRunStatus.Completed;

    public bool HasComparableResult => Comparison is not null;
}
