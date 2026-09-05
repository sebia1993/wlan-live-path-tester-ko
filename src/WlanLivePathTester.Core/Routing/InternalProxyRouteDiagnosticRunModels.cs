using System.Diagnostics;
using System.Text.Json.Serialization;
using WlanLivePathTester.Core.Proxy;

namespace WlanLivePathTester.Core.Routing;

public enum InternalProxyRouteDiagnosticRunStatus
{
    Completed,
    DirectOnly,
    Blocked,
    Unavailable,
    Canceled,
    Failed
}

[DebuggerDisplay("{RedactedDisplay,nq}")]
public sealed record InternalProxyRouteDiagnosticRunResult(
    [property: JsonPropertyName("status")]
    InternalProxyRouteDiagnosticRunStatus Status,
    [property: JsonPropertyName("selectionStatus")]
    ProxyDirectiveSourceSelectionStatus SelectionStatus,
    [property: JsonPropertyName("sourceKind")]
    ProxyDirectiveSourceKind SourceKind,
    [property: JsonPropertyName("planCode")]
    ProxyDirectiveRouteAnalysisPlanCode PlanCode,
    [property: JsonPropertyName("internalRouteStatus")]
    string InternalRouteStatus,
    [property: JsonPropertyName("proxyRouteStatus")]
    string ProxyRouteStatus,
    [property: JsonPropertyName("comparisonStatus")]
    string ComparisonStatus,
    [property: JsonPropertyName("sameLocalInterface")]
    bool? SameLocalInterface,
    [property: JsonPropertyName("proxyEndpointCount")]
    int ProxyEndpointCount,
    [property: JsonPropertyName("successfulProxyRouteCount")]
    int SuccessfulProxyRouteCount,
    [property: JsonPropertyName("directDirectiveCount")]
    int DirectDirectiveCount,
    [property: JsonPropertyName("proxyAnalysisWasTruncated")]
    bool ProxyAnalysisWasTruncated,
    [property: JsonPropertyName("message")]
    string Message,
    [property: JsonIgnore]
    DestinationRouteEvidence? InternalRouteEvidence = null,
    [property: JsonIgnore]
    ProxyEndpointRouteAnalysisResult? ProxyRouteAnalysis = null,
    [property: JsonIgnore]
    InternalProxyRouteComparisonResult? Comparison = null)
{
    [JsonPropertyName("hasCompleteComparison")]
    public bool HasCompleteComparison =>
        Comparison is not null
        && Comparison.Status is
            InternalProxyRouteComparisonStatus.Ready
            or InternalProxyRouteComparisonStatus.Diverged;

    [JsonPropertyName("redactedDisplay")]
    public string RedactedDisplay =>
        $"{Status} · {SourceKind} · {PlanCode} · 내부 {InternalRouteStatus} · 프록시 {ProxyRouteStatus} · 비교 {ComparisonStatus} · 후보 {Math.Max(0, ProxyEndpointCount)}개";

    public override string ToString() => RedactedDisplay;
}
