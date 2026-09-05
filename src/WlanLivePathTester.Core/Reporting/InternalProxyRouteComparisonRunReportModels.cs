namespace WlanLivePathTester.Core.Reporting;

public sealed record InternalProxyRouteComparisonRunReportDocument(
    string SchemaVersion,
    DateTimeOffset GeneratedAt,
    string ApplicationName,
    string ApplicationVersion,
    bool SensitiveValuesIncluded,
    string DataHandlingStatement,
    InternalProxyRouteComparisonRunReportSnapshot RouteComparison,
    IReadOnlyList<string> Limitations);

public sealed record InternalProxyRouteComparisonRunReportSnapshot(
    DateTimeOffset CompletedAt,
    string RunStatus,
    string ProxySourceKind,
    string ProxySelectionStatus,
    string ProxyPlanStatus,
    string ProxyPlanCode,
    string ProxyExecutionStatus,
    string ProxyEndpointSourceKind,
    string ProxyDecision,
    string TargetScheme,
    string InternalRouteStatus,
    string ProxyRouteStatus,
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
    bool OperationCompleted,
    bool HasComparableResult,
    InternalProxyRouteComparisonReportComparison? Comparison,
    IReadOnlyList<InternalProxyRouteComparisonReportProxyEntry> ProxyEntries,
    InternalProxyRouteComparisonReportFinding Finding);

public sealed record InternalProxyRouteComparisonReportComparison(
    DateTimeOffset EvaluatedAt,
    string Status,
    string Relation,
    string Code,
    string InternalRouteStatus,
    string ProxyExecutionStatus,
    string ProxyAnalysisStatus,
    string ProxySourceKind,
    string ProxyPlanCode,
    string? InternalInterfaceFingerprint,
    string? InternalInterfaceCategory,
    IReadOnlyList<string> ProxyInterfaceFingerprints,
    IReadOnlyList<string> ProxyInterfaceCategories,
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
    bool HasCompleteComparableEvidence);

public sealed record InternalProxyRouteComparisonReportProxyEntry(
    int Sequence,
    string AppliesToScheme,
    string Transport,
    int? Port,
    string? HostFingerprint,
    string RouteStatus,
    string WlanCorrelationStatus,
    string? SelectedInterfaceFingerprint,
    string? SelectedInterfaceCategory,
    bool? SelectedInterfaceIsVirtual,
    bool? SelectedInterfaceIsVpn,
    bool? SelectedInterfaceIsUp,
    bool? SelectedInterfaceHasDefaultGateway,
    int ResolvedAddressCount,
    int SuccessfulAddressCount,
    int FailedAddressCount);

public sealed record InternalProxyRouteComparisonReportFinding(
    string Code,
    string Severity,
    string Title,
    string Evidence,
    string Interpretation,
    string Limitation,
    string NextStep);

public sealed record InternalProxyRouteComparisonRunReportExportResult(
    string OutputDirectory,
    string JsonPath,
    string CsvPath,
    string HtmlPath,
    string Sha256Path,
    IReadOnlyDictionary<string, string> Sha256)
{
    public bool CleanupIncomplete { get; init; }
}
