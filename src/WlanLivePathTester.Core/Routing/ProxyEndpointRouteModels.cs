using System.Diagnostics;
using System.Globalization;
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

[DebuggerDisplay("{SafeDiagnosticDisplay,nq}")]
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
    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    public string? SelectedInterfaceIdentity { get; init; }

    public bool IsRouteSuccess =>
        RouteStatus is DestinationRouteEvidenceStatus.Success
            or DestinationRouteEvidenceStatus.PartialSuccess;

    public override string ToString() => SafeDiagnosticDisplay;

    private string SafeDiagnosticDisplay
    {
        get
        {
            string transport = SafeEnum(
                Transport,
                ProxyEndpointTransport.Unspecified);
            string routeStatus = SafeEnum(
                RouteStatus,
                DestinationRouteEvidenceStatus.Failed);
            string correlation = SafeEnum(
                WlanCorrelationStatus,
                RouteWlanCorrelationStatus.NotEvaluated);
            string category = SelectedInterfaceCategory.HasValue
                && Enum.IsDefined(SelectedInterfaceCategory.Value)
                    ? SelectedInterfaceCategory.Value.ToString()
                    : "확인 불가";
            string port = Port is >= 1 and <= 65535
                ? Port.Value.ToString(CultureInfo.InvariantCulture)
                : "없음";

            return string.Join(
                " · ",
                $"프록시 후보 {Math.Max(0, Sequence)}",
                transport,
                $"포트 {port}",
                $"경로 {routeStatus}",
                $"WLAN 상관 {correlation}",
                $"호스트 지문 {NormalizeFingerprint(HostFingerprint)}",
                $"인터페이스 {category}/{NormalizeFingerprint(SelectedInterfaceFingerprint)}");
        }
    }

    private static string SafeEnum<TEnum>(
        TEnum value,
        TEnum fallback)
        where TEnum : struct, Enum =>
        Enum.IsDefined(value)
            ? value.ToString()
            : fallback.ToString();

    private static string NormalizeFingerprint(string? value)
    {
        string candidate = (value ?? string.Empty)
            .Trim()
            .ToLowerInvariant();
        return candidate.Length == RouteInterfaceFingerprint.DisplayLength
               && candidate.All(character =>
                   character is >= '0' and <= '9'
                       or >= 'a' and <= 'f')
            ? candidate
            : "확인 불가";
    }
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
