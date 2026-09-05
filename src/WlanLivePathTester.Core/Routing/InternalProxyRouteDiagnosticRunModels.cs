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
public sealed class InternalProxyRouteDiagnosticRunResult
{
    public InternalProxyRouteDiagnosticRunResult(
        InternalProxyRouteDiagnosticRunStatus status,
        ProxyDirectiveSourceSelectionStatus selectionStatus,
        ProxyDirectiveSourceKind sourceKind,
        ProxyDirectiveRouteAnalysisPlanCode planCode,
        string internalRouteStatus,
        string proxyRouteStatus,
        string comparisonStatus,
        bool? sameLocalInterface,
        int proxyEndpointCount,
        int successfulProxyRouteCount,
        int directDirectiveCount,
        bool proxyAnalysisWasTruncated,
        string message,
        DestinationRouteEvidence? internalRouteEvidence = null,
        ProxyEndpointRouteAnalysisResult? proxyRouteAnalysis = null,
        InternalProxyRouteComparisonResult? comparison = null)
    {
        Status = status;
        SelectionStatus = selectionStatus;
        SourceKind = sourceKind;
        PlanCode = planCode;
        InternalRouteStatus = internalRouteStatus;
        ProxyRouteStatus = proxyRouteStatus;
        ComparisonStatus = comparisonStatus;
        SameLocalInterface = sameLocalInterface;
        ProxyEndpointCount = Math.Max(0, proxyEndpointCount);
        SuccessfulProxyRouteCount = Math.Max(
            0,
            successfulProxyRouteCount);
        DirectDirectiveCount = Math.Max(0, directDirectiveCount);
        ProxyAnalysisWasTruncated = proxyAnalysisWasTruncated;
        Message = message;
        InternalRouteEvidence = internalRouteEvidence;
        ProxyRouteAnalysis = proxyRouteAnalysis;
        Comparison = comparison;
        RedactedDisplay =
            $"{Status} · {SourceKind} · {PlanCode} · 내부 {InternalRouteStatus} · 프록시 {ProxyRouteStatus} · 비교 {ComparisonStatus} · 후보 {ProxyEndpointCount}개";
    }

    [JsonPropertyName("status")]
    public InternalProxyRouteDiagnosticRunStatus Status { get; }

    [JsonPropertyName("selectionStatus")]
    public ProxyDirectiveSourceSelectionStatus SelectionStatus { get; }

    [JsonPropertyName("sourceKind")]
    public ProxyDirectiveSourceKind SourceKind { get; }

    [JsonPropertyName("planCode")]
    public ProxyDirectiveRouteAnalysisPlanCode PlanCode { get; }

    [JsonPropertyName("internalRouteStatus")]
    public string InternalRouteStatus { get; }

    [JsonPropertyName("proxyRouteStatus")]
    public string ProxyRouteStatus { get; }

    [JsonPropertyName("comparisonStatus")]
    public string ComparisonStatus { get; }

    [JsonPropertyName("sameLocalInterface")]
    public bool? SameLocalInterface { get; }

    [JsonPropertyName("proxyEndpointCount")]
    public int ProxyEndpointCount { get; }

    [JsonPropertyName("successfulProxyRouteCount")]
    public int SuccessfulProxyRouteCount { get; }

    [JsonPropertyName("directDirectiveCount")]
    public int DirectDirectiveCount { get; }

    [JsonPropertyName("proxyAnalysisWasTruncated")]
    public bool ProxyAnalysisWasTruncated { get; }

    [JsonPropertyName("message")]
    public string Message { get; }

    [JsonPropertyName("redactedDisplay")]
    public string RedactedDisplay { get; }

    [JsonPropertyName("hasCompleteComparison")]
    public bool HasCompleteComparison =>
        Comparison is not null
        && Comparison.Status is
            InternalProxyRouteComparisonStatus.Ready
            or InternalProxyRouteComparisonStatus.Diverged;

    [JsonIgnore]
    public DestinationRouteEvidence? InternalRouteEvidence { get; }

    [JsonIgnore]
    public ProxyEndpointRouteAnalysisResult? ProxyRouteAnalysis { get; }

    [JsonIgnore]
    public InternalProxyRouteComparisonResult? Comparison { get; }

    public override string ToString() => RedactedDisplay;
}
