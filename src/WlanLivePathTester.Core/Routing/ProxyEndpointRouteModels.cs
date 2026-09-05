using System.Text.Json.Serialization;
using WlanLivePathTester.Core.NetworkEnvironment;
using WlanLivePathTester.Core.Proxy;

namespace WlanLivePathTester.Core.Routing;

public enum ProxyEndpointRouteAnalysisStatus
{
    InvalidInput,
    DirectPathSelected,
    NoApplicableEndpoint,
    Success,
    PartialSuccess,
    MultipleInterfaces,
    Canceled,
    Failed
}

public sealed record ProxyEndpointRouteEvidenceItem(
    int Sequence,
    string EndpointLabel,
    string HostFingerprint,
    string? AppliesToScheme,
    ProxyEndpointTransport Transport,
    int? Port,
    DestinationRouteEvidenceStatus RouteStatus,
    RouteWlanCorrelationStatus WlanCorrelationStatus,
    string? SelectedInterfaceFingerprint,
    NetworkAdapterCategory? SelectedInterfaceCategory,
    bool? SelectedInterfaceIsVirtual,
    bool? SelectedInterfaceIsVpn,
    bool? SelectedInterfaceIsUp,
    bool? SelectedInterfaceHasDefaultGateway,
    int ResolvedAddressCount,
    int SuccessfulAddressCount,
    int FailedAddressCount,
    string Message,
    IReadOnlyList<string> Warnings)
{
    [JsonIgnore]
    public string? SelectedInterfaceIdentity { get; init; }

    public bool IsRouteSuccess =>
        RouteStatus is DestinationRouteEvidenceStatus.Success
            or DestinationRouteEvidenceStatus.PartialSuccess;
}

public sealed record ProxyEndpointRouteAnalysisResult(
    DateTimeOffset CapturedAt,
    ProxyEndpointRouteAnalysisStatus Status,
    ProxyEndpointSourceKind SourceKind,
    ProxyEndpointDecision ProxyDecision,
    string? TargetScheme,
    bool DirectPresent,
    bool DirectIsPrimary,
    bool DirectFallback,
    int? DirectSequence,
    int ParsedEndpointCount,
    int ApplicableEndpointCount,
    int AnalyzedEndpointCount,
    int SkippedAfterDirectCount,
    int SuccessfulEndpointCount,
    int DistinctInterfaceCount,
    IReadOnlyList<ProxyEndpointRouteEvidenceItem> Endpoints,
    IReadOnlyList<string> Warnings,
    string Message,
    string Limitation)
{
    public bool IsSuccess =>
        Status is ProxyEndpointRouteAnalysisStatus.DirectPathSelected
            or ProxyEndpointRouteAnalysisStatus.Success
            or ProxyEndpointRouteAnalysisStatus.PartialSuccess
            or ProxyEndpointRouteAnalysisStatus.MultipleInterfaces;
}
