using WlanLivePathTester.Core.Measurements;
using WlanLivePathTester.Core.Models;
using WlanLivePathTester.Windows.Proxy;

namespace WlanLivePathTester.Windows.Http;

public enum WinHttpRequestMethod
{
    Head,
    Get
}

public enum WinHttpRequestStatus
{
    Success,
    InvalidRequest,
    UnsupportedPlatform,
    ProxyResolutionFailed,
    PathMismatch,
    ProxyAuthenticationUnsupported,
    ProxyAuthenticationFailed,
    ServerAuthenticationRequired,
    RedirectResponse,
    RedirectDenied,
    RedirectLimitExceeded,
    HttpErrorResponse,
    ResponseLimitReached,
    TimedOut,
    Canceled,
    NetworkError
}

public enum ProxyAuthenticationMethod
{
    None,
    Negotiate,
    Ntlm
}

public sealed record WinHttpRequestOptions(
    string Url,
    NetworkPathKind ExpectedPath,
    WinHttpRequestMethod Method = WinHttpRequestMethod.Head,
    int TimeoutMilliseconds = 15000,
    long MaxResponseBytes = 1024 * 1024,
    bool RequireExpectedPath = true,
    CancellationToken CancellationToken = default);

public sealed record WinHttpRequestResult(
    WinHttpRequestStatus Status,
    int? HttpStatusCode,
    bool ProxyWasUsed,
    ProxyAuthenticationMethod AuthenticationMethod,
    int AuthenticationAttempts,
    long BytesReceived,
    TimeSpan Duration,
    bool ResponseWasTruncated,
    int? Win32ErrorCode,
    ProxyRouteResolution? Route,
    string Message)
{
    public bool IsSuccess =>
        Status is WinHttpRequestStatus.Success
            or WinHttpRequestStatus.ResponseLimitReached;

    public TimeSpan? TimeToFirstByte { get; init; }

    public string FinalUrl { get; init; } = string.Empty;

    public string? RedirectLocation { get; init; }

    public string? ErrorCode { get; init; }

    public IReadOnlyDictionary<string, string> ResponseHeaders { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<ThroughputSample> ThroughputSamples { get; init; } =
        Array.Empty<ThroughputSample>();
}
