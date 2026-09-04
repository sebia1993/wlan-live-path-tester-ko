using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

namespace WlanLivePathTester.Core.Proxy;

public enum ProxyRouteDirectiveKind
{
    HttpProxy,
    HttpsProxy,
    SocksProxy,
    Socks4Proxy,
    Socks5Proxy,
    Direct
}

public enum ProxyDirectiveSourceSyntax
{
    PacKeyword,
    SchemeMapping,
    AbsoluteUri,
    BareEndpoint
}

public enum ProxyDirectiveParseStatus
{
    Success,
    PartialSuccess,
    Empty,
    InvalidInput
}

public enum ProxyDirectiveIssueSeverity
{
    Warning,
    Error
}

public sealed record ProxyDirectiveIssue(
    int SegmentIndex,
    ProxyDirectiveIssueSeverity Severity,
    string Code,
    string Message);

[DebuggerDisplay("{RedactedDisplay,nq}")]
public sealed class ProxyRouteDirective
{
    internal ProxyRouteDirective(
        int sequence,
        ProxyRouteDirectiveKind kind,
        ProxyDirectiveSourceSyntax sourceSyntax,
        string scope,
        string? host,
        int? port)
    {
        Sequence = sequence;
        Kind = kind;
        SourceSyntax = sourceSyntax;
        Scope = string.IsNullOrWhiteSpace(scope)
            ? "all"
            : scope.Trim().ToLowerInvariant();
        Host = host;
        Port = port;
        HostFingerprint = string.IsNullOrWhiteSpace(host)
            ? "없음"
            : ProxyHostFingerprint.Create(host);
        RedactedDisplay = CreateRedactedDisplay();
    }

    public int Sequence { get; }

    public ProxyRouteDirectiveKind Kind { get; }

    public ProxyDirectiveSourceSyntax SourceSyntax { get; }

    public string Scope { get; }

    [JsonIgnore]
    public string? Host { get; }

    public int? Port { get; }

    public bool IsDirect => Kind == ProxyRouteDirectiveKind.Direct;

    public string HostFingerprint { get; }

    public string RedactedDisplay { get; }

    [JsonIgnore]
    internal string DeduplicationKey => IsDirect
        ? $"direct|{Scope}"
        : $"{Kind}|{Scope}|{Host?.ToLowerInvariant()}|{Port}";

    public override string ToString() => RedactedDisplay;

    private string CreateRedactedDisplay()
    {
        if (IsDirect)
        {
            return Scope.Equals("all", StringComparison.OrdinalIgnoreCase)
                ? "DIRECT"
                : $"{Scope.ToUpperInvariant()} DIRECT";
        }

        string portText = Port.HasValue
            ? Port.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : "없음";
        return $"{Kind} · 범위 {Scope} · 호스트 지문 {HostFingerprint} · 포트 {portText}";
    }
}

public sealed record ProxyDirectiveParseResult(
    ProxyDirectiveParseStatus Status,
    IReadOnlyList<ProxyRouteDirective> Directives,
    IReadOnlyList<ProxyDirectiveIssue> Issues,
    string Message)
{
    public bool HasUsableDirective => Directives.Count > 0;

    public bool HasProxyEndpoint => Directives.Any(
        directive => !directive.IsDirect);

    public bool HasDirectFallback => Directives.Any(
        directive => directive.IsDirect);
}

public static class ProxyHostFingerprint
{
    public const int DisplayLength = 10;

    public static string Create(string normalizedHost)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(normalizedHost);
        byte[] digest = SHA256.HashData(
            Encoding.UTF8.GetBytes(
                normalizedHost.Trim().ToLowerInvariant()));
        return Convert.ToHexString(digest)
            [..DisplayLength]
            .ToLowerInvariant();
    }
}
