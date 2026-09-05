using WlanLivePathTester.Core.NetworkEnvironment;
using WlanLivePathTester.Core.Proxy;

namespace WlanLivePathTester.Core.Routing;

public enum InternalProxyRouteComparisonStatus
{
    Ready,
    Incomplete,
    Ambiguous,
    Diverged
}

public enum InternalProxyRouteRelation
{
    SameInterface,
    DifferentInterface,
    MultipleInterfaces,
    Unknown
}

public enum InternalProxyRouteComparisonCode
{
    SameLocalInterface,
    DifferentLocalInterface,
    InternalRouteMissing,
    InternalPurposeMismatch,
    InternalRouteIncomplete,
    InternalRouteAmbiguous,
    InternalExactIdentityUnavailable,
    ProxyExecutionMissing,
    ProxySourceBlocked,
    ProxySourceUnavailable,
    ProxyDirectOnly,
    ProxyExecutionCanceled,
    ProxyExecutionFailed,
    ProxyAnalysisMissing,
    ProxyDirectPathSelected,
    ProxyEndpointMissing,
    ProxyAnalysisIncomplete,
    ProxyRouteAmbiguous,
    ProxyExactIdentityUnavailable
}

public sealed record InternalProxyRouteComparisonResult(
    DateTimeOffset EvaluatedAt,
    InternalProxyRouteComparisonStatus Status,
    InternalProxyRouteRelation Relation,
    InternalProxyRouteComparisonCode Code,
    DestinationRouteEvidenceStatus? InternalRouteStatus,
    ProxyDirectiveRouteAnalysisExecutionStatus? ProxyExecutionStatus,
    ProxyEndpointRouteAnalysisStatus? ProxyAnalysisStatus,
    ProxyDirectiveSourceKind? ProxySourceKind,
    ProxyDirectiveRouteAnalysisPlanCode? ProxyPlanCode,
    string? InternalInterfaceFingerprint,
    NetworkAdapterCategory? InternalInterfaceCategory,
    IReadOnlyList<string> ProxyInterfaceFingerprints,
    IReadOnlyList<NetworkAdapterCategory> ProxyInterfaceCategories,
    int ProxyApplicableEndpointCount,
    int ProxyAnalyzedEndpointCount,
    int ProxySuccessfulEndpointCount,
    int ProxyDistinctInterfaceCount,
    int ProxySkippedAfterDirectCount,
    bool ProxyDirectPresent,
    bool ProxyDirectIsPrimary,
    bool ProxyDirectFallbackPresent,
    bool ProxyParseErrorsPresent,
    bool ExactIdentityComparisonPerformed,
    string Message,
    string Interpretation,
    string Limitation,
    string NextStep)
{
    public bool HasCompleteComparableEvidence =>
        Status is InternalProxyRouteComparisonStatus.Ready
            or InternalProxyRouteComparisonStatus.Diverged;
}
