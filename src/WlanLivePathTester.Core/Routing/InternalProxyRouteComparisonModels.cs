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
    ProxyAnalysisMissing,
    ProxyDirectiveMissing,
    ProxyDirectOnly,
    ProxyAnalysisIncomplete,
    ProxyRouteMissing,
    ProxyRouteIncomplete,
    ProxyRouteAmbiguous,
    ExactIdentityUnavailable
}

public sealed record InternalProxyRouteComparisonResult(
    InternalProxyRouteComparisonStatus Status,
    InternalProxyRouteRelation Relation,
    InternalProxyRouteComparisonCode Code,
    string InternalRouteStatus,
    string ProxyAnalysisStatus,
    string? InternalInterfaceFingerprint,
    string? InternalInterfaceCategory,
    IReadOnlyList<string> ProxyInterfaceFingerprints,
    IReadOnlyList<string> ProxyInterfaceCategories,
    int ProxyEndpointCount,
    int SuccessfulProxyRouteCount,
    int DirectDirectiveCount,
    bool ProxyAnalysisWasTruncated,
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
