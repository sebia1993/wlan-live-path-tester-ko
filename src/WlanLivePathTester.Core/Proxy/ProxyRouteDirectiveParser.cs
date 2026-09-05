using System.Globalization;
using System.Net;

namespace WlanLivePathTester.Core.Proxy;

public static class ProxyRouteDirectiveParser
{
    public const int MaximumInputLength = 4096;
    public const int MaximumSegments = 32;

    private static readonly IdnMapping Idn = new();

    public static ProxyDirectiveParseResult Parse(string? input)
    {
        // Validate the original text: Trim can erase trailing control characters
        // and must not turn an overlong input into an accepted directive.
        string value = input ?? string.Empty;
        if (value.Length > MaximumInputLength)
        {
            return InvalidGlobal(
                "INPUT_TOO_LONG",
                $"프록시 지시문은 {MaximumInputLength}자를 초과할 수 없습니다.");
        }

        if (value.Any(char.IsControl))
        {
            return InvalidGlobal(
                "CONTROL_CHARACTER",
                "프록시 지시문에 줄바꿈·탭·NUL과 같은 제어 문자를 사용할 수 없습니다.");
        }

        value = value.Trim();
        if (value.Length == 0)
        {
            return new ProxyDirectiveParseResult(
                ProxyDirectiveParseStatus.Empty,
                Array.Empty<ProxyRouteDirective>(),
                Array.Empty<ProxyDirectiveIssue>(),
                "프록시 지시문이 비어 있습니다.");
        }

        string[] segments = value.Split(';');
        if (segments.Length > MaximumSegments)
        {
            return InvalidGlobal(
                "TOO_MANY_SEGMENTS",
                $"프록시 지시문은 최대 {MaximumSegments}개 구간만 처리합니다.");
        }

        List<ProxyRouteDirective> directives = [];
        List<ProxyDirectiveIssue> issues = [];
        HashSet<string> deduplicationKeys = new(
            StringComparer.OrdinalIgnoreCase);

        for (int index = 0; index < segments.Length; index++)
        {
            int segmentIndex = index + 1;
            string segment = segments[index].Trim();
            if (segment.Length == 0)
            {
                issues.Add(new ProxyDirectiveIssue(
                    segmentIndex,
                    ProxyDirectiveIssueSeverity.Warning,
                    "EMPTY_SEGMENT",
                    "빈 프록시 구간을 건너뛰었습니다."));
                continue;
            }

            SegmentParseResult parsed = ParseSegment(
                segment,
                segmentIndex);
            if (parsed.Directive is null)
            {
                issues.Add(parsed.Issue
                    ?? new ProxyDirectiveIssue(
                        segmentIndex,
                        ProxyDirectiveIssueSeverity.Error,
                        "INVALID_SEGMENT",
                        "프록시 구간을 안전하게 해석하지 못했습니다."));
                continue;
            }

            if (!deduplicationKeys.Add(
                    parsed.Directive.DeduplicationKey))
            {
                issues.Add(new ProxyDirectiveIssue(
                    segmentIndex,
                    ProxyDirectiveIssueSeverity.Warning,
                    "DUPLICATE_DIRECTIVE",
                    "동일한 종류·범위·호스트 지문·포트의 중복 지시문을 한 번만 유지했습니다."));
                continue;
            }

            directives.Add(parsed.Directive);
        }

        bool hasErrors = issues.Any(issue =>
            issue.Severity == ProxyDirectiveIssueSeverity.Error);
        ProxyDirectiveParseStatus status = directives.Count switch
        {
            0 => ProxyDirectiveParseStatus.InvalidInput,
            _ when hasErrors => ProxyDirectiveParseStatus.PartialSuccess,
            _ => ProxyDirectiveParseStatus.Success
        };
        string message = status switch
        {
            ProxyDirectiveParseStatus.Success =>
                $"프록시 지시문 {directives.Count}개를 로컬에서 해석했습니다.",
            ProxyDirectiveParseStatus.PartialSuccess =>
                $"유효한 프록시 지시문 {directives.Count}개를 유지하고 해석할 수 없는 구간 {issues.Count(issue => issue.Severity == ProxyDirectiveIssueSeverity.Error)}개를 제외했습니다.",
            _ => "사용할 수 있는 프록시 지시문을 확인하지 못했습니다."
        };

        return new ProxyDirectiveParseResult(
            status,
            directives,
            issues,
            message);
    }

    private static SegmentParseResult ParseSegment(
        string segment,
        int segmentIndex)
    {
        if (segment.Equals(
                "DIRECT",
                StringComparison.OrdinalIgnoreCase))
        {
            return Success(new ProxyRouteDirective(
                segmentIndex,
                ProxyRouteDirectiveKind.Direct,
                ProxyDirectiveSourceSyntax.PacKeyword,
                "all",
                host: null,
                port: null));
        }

        int firstWhitespace = FindFirstWhitespace(segment);
        if (firstWhitespace > 0)
        {
            string keyword = segment[..firstWhitespace].Trim();
            string endpoint = segment[firstWhitespace..].Trim();
            if (TryMapPacKeyword(
                    keyword,
                    out ProxyRouteDirectiveKind kind))
            {
                return ParseEndpointDirective(
                    endpoint,
                    segmentIndex,
                    kind,
                    ProxyDirectiveSourceSyntax.PacKeyword,
                    scope: "all");
            }
        }

        int equalsIndex = segment.IndexOf('=');
        if (equalsIndex > 0)
        {
            string scope = segment[..equalsIndex].Trim();
            string target = segment[(equalsIndex + 1)..].Trim();
            if (!TryMapSchemeScope(
                    scope,
                    out string normalizedScope,
                    out ProxyRouteDirectiveKind kind))
            {
                return Failure(
                    segmentIndex,
                    "UNSUPPORTED_SCOPE",
                    "지원하지 않는 프록시 범위 이름입니다. http, https, ftp, proxy, all, socks, socks4 또는 socks5만 허용합니다.");
            }

            if (target.Equals(
                    "DIRECT",
                    StringComparison.OrdinalIgnoreCase))
            {
                return Success(new ProxyRouteDirective(
                    segmentIndex,
                    ProxyRouteDirectiveKind.Direct,
                    ProxyDirectiveSourceSyntax.SchemeMapping,
                    normalizedScope,
                    host: null,
                    port: null));
            }

            return ParseEndpointDirective(
                target,
                segmentIndex,
                kind,
                ProxyDirectiveSourceSyntax.SchemeMapping,
                normalizedScope);
        }

        if (segment.Contains("://", StringComparison.Ordinal))
        {
            return ParseAbsoluteUri(segment, segmentIndex);
        }

        return ParseEndpointDirective(
            segment,
            segmentIndex,
            ProxyRouteDirectiveKind.HttpProxy,
            ProxyDirectiveSourceSyntax.BareEndpoint,
            scope: "all");
    }

    private static SegmentParseResult ParseAbsoluteUri(
        string value,
        int segmentIndex)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri))
        {
            return Failure(
                segmentIndex,
                "INVALID_PROXY_URI",
                "유효한 절대 프록시 URI가 아닙니다.");
        }

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            return Failure(
                segmentIndex,
                "USER_INFO_NOT_ALLOWED",
                "프록시 URI에 사용자 이름이나 암호를 포함할 수 없습니다.");
        }

        if (!string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment)
            || !uri.AbsolutePath.Equals(
                "/",
                StringComparison.Ordinal))
        {
            return Failure(
                segmentIndex,
                "PATH_NOT_ALLOWED",
                "프록시 URI에는 경로, query 또는 fragment를 포함할 수 없습니다.");
        }

        ProxyRouteDirectiveKind kind;
        int defaultPort;
        string scope;
        switch (uri.Scheme.ToLowerInvariant())
        {
            case "http":
                kind = ProxyRouteDirectiveKind.HttpProxy;
                defaultPort = 80;
                scope = "all";
                break;
            case "https":
                kind = ProxyRouteDirectiveKind.HttpsProxy;
                defaultPort = 443;
                scope = "all";
                break;
            case "socks":
                kind = ProxyRouteDirectiveKind.SocksProxy;
                defaultPort = 1080;
                scope = "all";
                break;
            case "socks4":
                kind = ProxyRouteDirectiveKind.Socks4Proxy;
                defaultPort = 1080;
                scope = "all";
                break;
            case "socks5":
                kind = ProxyRouteDirectiveKind.Socks5Proxy;
                defaultPort = 1080;
                scope = "all";
                break;
            default:
                return Failure(
                    segmentIndex,
                    "UNSUPPORTED_PROXY_SCHEME",
                    "프록시 URI 스킴은 http, https, socks, socks4 또는 socks5만 지원합니다.");
        }

        string normalizedHost;
        if (!TryNormalizeHost(
                uri.IdnHost,
                out normalizedHost,
                out string hostError))
        {
            return Failure(
                segmentIndex,
                "INVALID_HOST",
                hostError);
        }

        int port = uri.IsDefaultPort ? defaultPort : uri.Port;
        if (!IsValidPort(port))
        {
            return Failure(
                segmentIndex,
                "INVALID_PORT",
                "프록시 포트는 1~65535 범위여야 합니다.");
        }

        return Success(new ProxyRouteDirective(
            segmentIndex,
            kind,
            ProxyDirectiveSourceSyntax.AbsoluteUri,
            scope,
            normalizedHost,
            port));
    }

    private static SegmentParseResult ParseEndpointDirective(
        string endpoint,
        int segmentIndex,
        ProxyRouteDirectiveKind kind,
        ProxyDirectiveSourceSyntax sourceSyntax,
        string scope)
    {
        if (!TryParseHostAndPort(
                endpoint,
                out string normalizedHost,
                out int port,
                out string issueCode,
                out string error))
        {
            return Failure(
                segmentIndex,
                issueCode,
                error);
        }

        return Success(new ProxyRouteDirective(
            segmentIndex,
            kind,
            sourceSyntax,
            scope,
            normalizedHost,
            port));
    }

    private static bool TryParseHostAndPort(
        string endpoint,
        out string normalizedHost,
        out int port,
        out string issueCode,
        out string error)
    {
        normalizedHost = string.Empty;
        port = 0;
        issueCode = "INVALID_ENDPOINT";
        error = string.Empty;
        string value = endpoint.Trim();

        if (value.Length == 0)
        {
            error = "프록시 호스트와 포트가 비어 있습니다.";
            return false;
        }

        if (value.Any(char.IsWhiteSpace)
            || value.IndexOfAny(['/', '\\', '?', '#', '@']) >= 0)
        {
            error = "프록시 엔드포인트는 경로·자격 증명 없이 host:port 또는 [IPv6]:port 형식이어야 합니다.";
            return false;
        }

        string hostPart;
        string portPart;
        if (value.StartsWith('[', StringComparison.Ordinal))
        {
            int closingBracket = value.IndexOf(']');
            if (closingBracket <= 1
                || closingBracket + 1 >= value.Length
                || value[closingBracket + 1] != ':')
            {
                error = "IPv6 프록시는 [IPv6]:port 형식이어야 합니다.";
                return false;
            }

            hostPart = value[1..closingBracket];
            portPart = value[(closingBracket + 2)..];
            if (!IPAddress.TryParse(
                    hostPart,
                    out IPAddress? ipv6)
                || ipv6.AddressFamily
                    != System.Net.Sockets.AddressFamily.InterNetworkV6)
            {
                issueCode = "INVALID_HOST";
                error = "대괄호 안의 값이 유효한 IPv6 주소가 아닙니다.";
                return false;
            }
        }
        else
        {
            int firstColon = value.IndexOf(':');
            int lastColon = value.LastIndexOf(':');
            if (firstColon <= 0 || firstColon != lastColon)
            {
                error = firstColon != lastColon
                    ? "포트를 포함한 IPv6 주소는 [IPv6]:port 형식으로 입력해야 합니다."
                    : "프록시 엔드포인트는 host:port 형식이어야 합니다.";
                return false;
            }

            hostPart = value[..firstColon];
            portPart = value[(firstColon + 1)..];
        }

        if (!int.TryParse(
                portPart,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out port)
            || !IsValidPort(port))
        {
            issueCode = "INVALID_PORT";
            error = "프록시 포트는 1~65535 범위의 정수여야 합니다.";
            return false;
        }

        if (!TryNormalizeHost(
                hostPart,
                out normalizedHost,
                out error))
        {
            issueCode = "INVALID_HOST";
            return false;
        }

        return true;
    }

    private static bool TryNormalizeHost(
        string host,
        out string normalizedHost,
        out string error)
    {
        normalizedHost = string.Empty;
        error = string.Empty;
        string candidate = host.Trim().TrimEnd('.');
        if (candidate.Length == 0)
        {
            error = "프록시 호스트가 비어 있습니다.";
            return false;
        }

        if (IPAddress.TryParse(
                candidate,
                out IPAddress? address))
        {
            normalizedHost = address.ToString().ToLowerInvariant();
            return true;
        }

        string ascii;
        try
        {
            ascii = Idn.GetAscii(candidate).ToLowerInvariant();
        }
        catch (ArgumentException)
        {
            error = "프록시 DNS 호스트 이름의 국제화 문자 구성이 유효하지 않습니다.";
            return false;
        }

        if (ascii.Length > 253
            || Uri.CheckHostName(ascii) != UriHostNameType.Dns
            || ascii.Split('.').Any(label =>
                label.Length is < 1 or > 63))
        {
            error = "유효한 DNS 호스트 이름 또는 IP 주소가 아닙니다.";
            return false;
        }

        normalizedHost = ascii;
        return true;
    }

    private static bool TryMapPacKeyword(
        string keyword,
        out ProxyRouteDirectiveKind kind)
    {
        switch (keyword.ToUpperInvariant())
        {
            case "PROXY":
            case "HTTP":
                kind = ProxyRouteDirectiveKind.HttpProxy;
                return true;
            case "HTTPS":
                kind = ProxyRouteDirectiveKind.HttpsProxy;
                return true;
            case "SOCKS":
                kind = ProxyRouteDirectiveKind.SocksProxy;
                return true;
            case "SOCKS4":
                kind = ProxyRouteDirectiveKind.Socks4Proxy;
                return true;
            case "SOCKS5":
                kind = ProxyRouteDirectiveKind.Socks5Proxy;
                return true;
            default:
                kind = default;
                return false;
        }
    }

    private static bool TryMapSchemeScope(
        string scope,
        out string normalizedScope,
        out ProxyRouteDirectiveKind kind)
    {
        normalizedScope = scope.Trim().ToLowerInvariant();
        switch (normalizedScope)
        {
            case "http":
            case "https":
            case "ftp":
                kind = ProxyRouteDirectiveKind.HttpProxy;
                return true;
            case "proxy":
            case "all":
                normalizedScope = "all";
                kind = ProxyRouteDirectiveKind.HttpProxy;
                return true;
            case "socks":
                kind = ProxyRouteDirectiveKind.SocksProxy;
                return true;
            case "socks4":
                kind = ProxyRouteDirectiveKind.Socks4Proxy;
                return true;
            case "socks5":
                kind = ProxyRouteDirectiveKind.Socks5Proxy;
                return true;
            default:
                kind = default;
                return false;
        }
    }

    private static bool IsValidPort(int port) =>
        port is >= 1 and <= 65535;

    private static int FindFirstWhitespace(string value)
    {
        for (int index = 0; index < value.Length; index++)
        {
            if (char.IsWhiteSpace(value[index]))
            {
                return index;
            }
        }

        return -1;
    }

    private static SegmentParseResult Success(
        ProxyRouteDirective directive) =>
        new(directive, Issue: null);

    private static SegmentParseResult Failure(
        int segmentIndex,
        string code,
        string message) =>
        new(
            Directive: null,
            new ProxyDirectiveIssue(
                segmentIndex,
                ProxyDirectiveIssueSeverity.Error,
                code,
                message));

    private static ProxyDirectiveParseResult InvalidGlobal(
        string code,
        string message) =>
        new(
            ProxyDirectiveParseStatus.InvalidInput,
            Array.Empty<ProxyRouteDirective>(),
            [
                new ProxyDirectiveIssue(
                    0,
                    ProxyDirectiveIssueSeverity.Error,
                    code,
                    message)
            ],
            message);

    private sealed record SegmentParseResult(
        ProxyRouteDirective? Directive,
        ProxyDirectiveIssue? Issue);
}
