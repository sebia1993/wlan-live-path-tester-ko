using WlanLivePathTester.Core.Routing;

namespace WlanLivePathTester.Core.Proxy;

public enum ProxyEndpointRouteAnalysisStatus
{
    Success,
    PartialSuccess,
    DirectOnly,
    Empty,
    InvalidInput,
    Canceled,
    Failed
}

public enum ProxyEndpointRouteEntryStatus
{
    Direct,
    Success,
    PartialSuccess,
    MultipleInterfaces,
    ResolutionFailed,
    RouteNotFound,
    Canceled,
    Failed
}

public sealed record ProxyEndpointRouteEntry(
    int Sequence,
    ProxyRouteDirectiveKind Kind,
    ProxyDirectiveSourceSyntax SourceSyntax,
    string Scope,
    int? Port,
    string HostFingerprint,
    string RedactedDisplay,
    ProxyEndpointRouteEntryStatus Status,
    DestinationRouteEvidence? RouteEvidence,
    string Message)
{
    public bool IsDirect => Kind == ProxyRouteDirectiveKind.Direct;

    public bool HasUsableRoute => Status is
        ProxyEndpointRouteEntryStatus.Success
        or ProxyEndpointRouteEntryStatus.PartialSuccess;
}

public sealed record ProxyEndpointRouteAnalysisResult(
    ProxyEndpointRouteAnalysisStatus Status,
    ProxyDirectiveParseStatus ParseStatus,
    IReadOnlyList<ProxyEndpointRouteEntry> Entries,
    IReadOnlyList<ProxyDirectiveIssue> ParseIssues,
    int EndpointLimit,
    bool WasTruncated,
    string Message)
{
    public int ProxyEndpointCount => Entries.Count(entry =>
        !entry.IsDirect);

    public int DirectDirectiveCount => Entries.Count(entry =>
        entry.IsDirect);

    public int SuccessfulRouteCount => Entries.Count(entry =>
        entry.HasUsableRoute);
}
