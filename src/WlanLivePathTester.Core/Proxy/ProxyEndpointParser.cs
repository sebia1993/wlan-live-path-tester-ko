using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace WlanLivePathTester.Core.Proxy;

public enum ProxyEndpointTransport
{
    Unspecified,
    Http,
    Https,
    Socks,
    Socks4,
    Socks5
}

public enum ProxyEndpointSourceKind
{
    Unknown,
    ManualServerList,
    AutoProxyResult,
    Mixed
}

public enum ProxyEndpointDecision
{
    Unknown,
    Direct,
    Proxy,
    ProxyWithDirectFallback,
    DirectWithProxyAlternatives
}

public sealed record ProxyEndpointCandidate(
    int Sequence,
    string? AppliesToScheme,
    ProxyEndpointTransport Transport,
    string Host,
    int? Port,
    string HostFingerprint)
{
    public string SafeLabel
    {
        get
        {
            string applicability = string.IsNullOrWhiteSpace(AppliesToScheme)
                ? "모든 HTTP(S) 대상"
                : $"{AppliesToScheme} 대상";
            string transport = Transport switch
            {
                ProxyEndpointTransport.Http => "HTTP proxy",
                ProxyEndpointTransport.Https => "HTTPS proxy",
                ProxyEndpointTransport.Socks => "SOCKS proxy",
                ProxyEndpointTransport.Socks4 => "SOCKS4 proxy",
                ProxyEndpointTransport.Socks5 => "SOCKS5 proxy",
                _ => "proxy transport 미지정"
            };
            string port = Port?.ToString(CultureInfo.InvariantCulture)
                ?? "미지정";
            return $"프록시 후보 {Sequence} · {applicability} · {transport} · host#{HostFingerprint} · port {port}";
        }
    }
}

public sealed record ProxyEndpointParseResult(
    bool InputPresent,
    ProxyEndpointSourceKind SourceKind,
    ProxyEndpointDecision Decision,
    string? TargetScheme,
    IReadOnlyList<ProxyEndpointCandidate> Endpoints,
    IReadOnlyList<int> DirectSequences,
    bool DirectFallback,
    int ParsedEndpointCount,
    int IgnoredEndpointCount,
    int DuplicateEndpointCount,
    int DuplicateDirectCount,
    int RejectedTokenCount,
    int TruncatedTokenCount,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors)
{
    public bool DirectPresent => DirectSequences.Count > 0;

    public bool IsUsable =>
        Errors.Count == 0
        && Decision is ProxyEndpointDecision.Direct
            or ProxyEndpointDecision.Proxy
            or ProxyEndpointDecision.ProxyWithDirectFallback
            or ProxyEndpointDecision.DirectWithProxyAlternatives;
}

public static class ProxyEndpointParser
{
    public const int MaximumInputLength = 16 * 1024;
    public const int MaximumTokenCount = 64;
    public const int MaximumEndpointCount = 32;
    public const int FingerprintLength = 10;

    public static ProxyEndpointParseResult Parse(
        string? value,
        Uri? targetUri = null)
    {
        string input = (value ?? string.Empty).Trim();
        List<string> warnings = [];
        List<string> errors = [];
        string? targetScheme = ResolveTargetScheme(targetUri, errors);

        if (errors.Count > 0)
        {
            return EmptyResult(input.Length > 0, targetScheme, warnings, errors);
        }

        if (input.Length == 0)
        {
            warnings.Add(
                "프록시 경로 문자열이 비어 있어 프록시 엔드포인트를 확인하지 않았습니다.");
            return EmptyResult(false, targetScheme, warnings, errors);
        }

        if (input.Length > MaximumInputLength)
        {
            errors.Add(
                $"프록시 경로 문자열은 {MaximumInputLength}자를 초과할 수 없습니다.");
            return EmptyResult(true, targetScheme, warnings, errors);
        }

        TokenizationResult tokenization = Tokenize(input);
        if (tokenization.TruncatedTokenCount > 0)
        {
            warnings.Add(
                $"프록시 항목은 최대 {MaximumTokenCount}개까지만 해석하며 나머지 {tokenization.TruncatedTokenCount}개는 사용하지 않았습니다.");
        }

        ProxyEndpointSourceKind sourceKind = InferSourceKind(tokenization.Tokens);
        if (sourceKind == ProxyEndpointSourceKind.Mixed)
        {
            warnings.Add(
                "자동 프록시 지시문과 수동 스킴 매핑이 함께 있어 입력 순서대로 안전하게 해석했습니다.");
        }

        List<ProxyEndpointCandidate> selected = [];
        List<int> directSequences = [];
        HashSet<string> endpointKeys = new(StringComparer.OrdinalIgnoreCase);
        int parsedCount = 0;
        int ignoredCount = 0;
        int duplicateCount = 0;
        int duplicateDirectCount = 0;
        int rejectedCount = 0;
        bool endpointLimitReported = false;

        foreach (RawProxyToken token in tokenization.Tokens)
        {
            if (token.Value.Equals("DIRECT", StringComparison.OrdinalIgnoreCase))
            {
                if (directSequences.Count == 0)
                {
                    directSequences.Add(token.Sequence);
                }
                else
                {
                    duplicateDirectCount++;
                }

                continue;
            }

            ParseTokenShape(
                token.Value,
                out string? appliesToScheme,
                out ProxyEndpointTransport directiveTransport,
                out string endpointValue);

            if (!TryParseEndpoint(
                    endpointValue,
                    directiveTransport,
                    out ParsedEndpoint? parsed,
                    out string failureReason))
            {
                rejectedCount++;
                warnings.Add(
                    $"프록시 항목 {token.Sequence}을(를) 사용하지 않았습니다: {failureReason}");
                continue;
            }

            parsedCount++;
            if (!AppliesToTarget(appliesToScheme, targetScheme))
            {
                ignoredCount++;
                continue;
            }

            if (selected.Count >= MaximumEndpointCount)
            {
                ignoredCount++;
                if (!endpointLimitReported)
                {
                    warnings.Add(
                        $"현재 대상에 적용되는 프록시 후보는 최대 {MaximumEndpointCount}개까지만 사용합니다.");
                    endpointLimitReported = true;
                }

                continue;
            }

            string key = BuildEndpointKey(
                targetScheme is null ? appliesToScheme : null,
                parsed.Transport,
                parsed.Host,
                parsed.Port);
            if (!endpointKeys.Add(key))
            {
                duplicateCount++;
                continue;
            }

            selected.Add(new ProxyEndpointCandidate(
                token.Sequence,
                appliesToScheme,
                parsed.Transport,
                parsed.Host,
                parsed.Port,
                CreateHostFingerprint(parsed.Host)));
        }

        AddCountWarnings(
            warnings,
            ignoredCount,
            duplicateCount,
            duplicateDirectCount);

        ProxyEndpointDecision decision = ResolveDecision(
            selected,
            directSequences);
        bool directFallback = decision
            == ProxyEndpointDecision.ProxyWithDirectFallback;

        if (decision == ProxyEndpointDecision.Unknown)
        {
            warnings.Add(parsedCount > 0
                ? "현재 대상 URL에 적용되는 프록시 엔드포인트 또는 DIRECT 지시문이 없습니다."
                : "사용 가능한 프록시 엔드포인트 또는 DIRECT 지시문을 찾지 못했습니다.");
        }
        else if (decision == ProxyEndpointDecision.DirectWithProxyAlternatives)
        {
            warnings.Add(
                "DIRECT가 적용 가능한 프록시 후보보다 먼저 나타납니다. 순서를 보존했으며 프록시를 기본 경로로 추정하지 않습니다.");
        }

        return new ProxyEndpointParseResult(
            InputPresent: true,
            SourceKind: sourceKind,
            Decision: decision,
            TargetScheme: targetScheme,
            Endpoints: selected.ToArray(),
            DirectSequences: directSequences.ToArray(),
            DirectFallback: directFallback,
            ParsedEndpointCount: parsedCount,
            IgnoredEndpointCount: ignoredCount,
            DuplicateEndpointCount: duplicateCount,
            DuplicateDirectCount: duplicateDirectCount,
            RejectedTokenCount: rejectedCount,
            TruncatedTokenCount: tokenization.TruncatedTokenCount,
            Warnings: warnings.ToArray(),
            Errors: errors.ToArray());
    }

    public static string CreateHostFingerprint(string host)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        string normalized = host.Trim().TrimEnd('.').ToLowerInvariant();
        byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(digest)
            [..FingerprintLength]
            .ToLowerInvariant();
    }

    private static ProxyEndpointParseResult EmptyResult(
        bool inputPresent,
        string? targetScheme,
        IReadOnlyList<string> warnings,
        IReadOnlyList<string> errors) =>
        new(
            InputPresent: inputPresent,
            SourceKind: ProxyEndpointSourceKind.Unknown,
            Decision: ProxyEndpointDecision.Unknown,
            TargetScheme: targetScheme,
            Endpoints: Array.Empty<ProxyEndpointCandidate>(),
            DirectSequences: Array.Empty<int>(),
            DirectFallback: false,
            ParsedEndpointCount: 0,
            IgnoredEndpointCount: 0,
            DuplicateEndpointCount: 0,
            DuplicateDirectCount: 0,
            RejectedTokenCount: 0,
            TruncatedTokenCount: 0,
            Warnings: warnings.ToArray(),
            Errors: errors.ToArray());

    private static string? ResolveTargetScheme(
        Uri? targetUri,
        ICollection<string> errors)
    {
        if (targetUri is null)
        {
            return null;
        }

        if (!targetUri.IsAbsoluteUri
            || (!targetUri.Scheme.Equals(
                    Uri.UriSchemeHttp,
                    StringComparison.OrdinalIgnoreCase)
                && !targetUri.Scheme.Equals(
                    Uri.UriSchemeHttps,
                    StringComparison.OrdinalIgnoreCase)))
        {
            errors.Add(
                "프록시 엔드포인트 선택 대상은 절대 HTTP 또는 HTTPS URL이어야 합니다.");
            return null;
        }

        return targetUri.Scheme.ToLowerInvariant();
    }

    private static TokenizationResult Tokenize(string input)
    {
        List<RawProxyToken> tokens = [];
        int nextSequence = 1;
        int truncatedCount = 0;

        foreach (string rawSegment in input.Split(';'))
        {
            string segment = rawSegment.Trim();
            if (segment.Length == 0)
            {
                continue;
            }

            string[] words = segment.Split(
                (char[]?)null,
                StringSplitOptions.RemoveEmptyEntries
                    | StringSplitOptions.TrimEntries);

            for (int index = 0; index < words.Length; index++)
            {
                string word = words[index];
                string token = word;

                if (IsDirectiveWord(word)
                    && index + 1 < words.Length
                    && !IsDirectiveWord(words[index + 1])
                    && !words[index + 1].Equals(
                        "DIRECT",
                        StringComparison.OrdinalIgnoreCase)
                    && !LooksLikeManualMapping(words[index + 1]))
                {
                    token = word + " " + words[++index];
                }
                else if (word.EndsWith("=", StringComparison.Ordinal)
                         && index + 1 < words.Length)
                {
                    token = word + words[++index];
                }

                if (tokens.Count >= MaximumTokenCount)
                {
                    truncatedCount++;
                    continue;
                }

                tokens.Add(new RawProxyToken(nextSequence++, token));
            }
        }

        return new TokenizationResult(tokens.ToArray(), truncatedCount);
    }

    private static ProxyEndpointSourceKind InferSourceKind(
        IReadOnlyList<RawProxyToken> tokens)
    {
        bool automatic = tokens.Any(token =>
            token.Value.Equals("DIRECT", StringComparison.OrdinalIgnoreCase)
            || StartsWithDirective(token.Value));
        bool manual = tokens.Any(token => LooksLikeManualMapping(token.Value));

        return (automatic, manual) switch
        {
            (true, true) => ProxyEndpointSourceKind.Mixed,
            (true, false) => ProxyEndpointSourceKind.AutoProxyResult,
            (false, true) => ProxyEndpointSourceKind.ManualServerList,
            _ => ProxyEndpointSourceKind.Unknown
        };
    }

    private static void ParseTokenShape(
        string token,
        out string? appliesToScheme,
        out ProxyEndpointTransport directiveTransport,
        out string endpointValue)
    {
        appliesToScheme = null;
        directiveTransport = ProxyEndpointTransport.Unspecified;
        endpointValue = token;

        int whitespace = token.IndexOfAny([' ', '\t']);
        string first = whitespace < 0 ? token : token[..whitespace];
        if (TryMapDirectiveTransport(
                first,
                out ProxyEndpointTransport transport))
        {
            directiveTransport = transport;
            endpointValue = whitespace < 0
                ? string.Empty
                : token[(whitespace + 1)..].Trim();
            return;
        }

        int equals = token.IndexOf('=');
        if (equals <= 0)
        {
            return;
        }

        string key = token[..equals].Trim();
        if (key.Equals("all", StringComparison.OrdinalIgnoreCase)
            || key.Equals("*", StringComparison.Ordinal))
        {
            appliesToScheme = "all";
            endpointValue = token[(equals + 1)..].Trim();
            return;
        }

        if (Uri.CheckSchemeName(key))
        {
            appliesToScheme = key.ToLowerInvariant();
            endpointValue = token[(equals + 1)..].Trim();
        }
    }

    private static bool TryParseEndpoint(
        string value,
        ProxyEndpointTransport directiveTransport,
        [NotNullWhen(true)] out ParsedEndpoint? endpoint,
        out string failureReason)
    {
        endpoint = null;
        failureReason = string.Empty;
        string candidate = value.Trim();
        if (candidate.Length == 0)
        {
            failureReason = "엔드포인트 값이 없습니다.";
            return false;
        }

        if (candidate.Any(char.IsControl)
            || candidate.Any(char.IsWhiteSpace))
        {
            failureReason = "엔드포인트에 공백 또는 제어 문자가 있습니다.";
            return false;
        }

        ProxyEndpointTransport transport = directiveTransport;
        string host;
        int? port;

        if (candidate.Contains("://", StringComparison.Ordinal))
        {
            if (!TryParseEndpointUri(
                    candidate,
                    out transport,
                    out host,
                    out port,
                    out failureReason))
            {
                return false;
            }
        }
        else if (!TryParseAuthority(
                     candidate,
                     out host,
                     out port,
                     out failureReason))
        {
            return false;
        }

        if (!TryNormalizeHost(host, out string normalizedHost))
        {
            failureReason = "호스트 형식이 유효하지 않습니다.";
            return false;
        }

        endpoint = new ParsedEndpoint(transport, normalizedHost, port);
        return true;
    }

    private static bool TryParseEndpointUri(
        string value,
        out ProxyEndpointTransport transport,
        out string host,
        out int? port,
        out string failureReason)
    {
        transport = ProxyEndpointTransport.Unspecified;
        host = string.Empty;
        port = null;
        failureReason = string.Empty;

        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri)
            || !TryMapUriTransport(uri.Scheme, out transport))
        {
            failureReason = "지원하지 않는 프록시 URI 스킴입니다.";
            return false;
        }

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            failureReason = "사용자 정보가 포함된 프록시 URI는 허용하지 않습니다.";
            return false;
        }

        if (!string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment)
            || (!string.IsNullOrEmpty(uri.AbsolutePath)
                && !uri.AbsolutePath.Equals("/", StringComparison.Ordinal)))
        {
            failureReason =
                "프록시 URI에는 경로, query 또는 fragment를 사용할 수 없습니다.";
            return false;
        }

        host = uri.IdnHost;
        port = uri.Port > 0 ? uri.Port : GetUriDefaultPort(transport);
        return true;
    }

    private static bool TryParseAuthority(
        string value,
        out string host,
        out int? port,
        out string failureReason)
    {
        host = string.Empty;
        port = null;
        failureReason = string.Empty;

        if (value.Contains('@'))
        {
            failureReason =
                "사용자 정보가 포함된 프록시 엔드포인트는 허용하지 않습니다.";
            return false;
        }

        if (value.StartsWith("[", StringComparison.Ordinal))
        {
            int closing = value.IndexOf(']');
            if (closing <= 1)
            {
                failureReason = "IPv6 대괄호 형식이 올바르지 않습니다.";
                return false;
            }

            host = value[1..closing];
            string suffix = value[(closing + 1)..];
            if (suffix.Length == 0)
            {
                return true;
            }

            if (!suffix.StartsWith(":", StringComparison.Ordinal)
                || !TryParsePort(suffix[1..], out int bracketPort))
            {
                failureReason = "IPv6 프록시 포트가 유효하지 않습니다.";
                return false;
            }

            port = bracketPort;
            return true;
        }

        if (IPAddress.TryParse(value, out IPAddress? address))
        {
            host = address.ToString();
            return true;
        }

        int firstColon = value.IndexOf(':');
        int lastColon = value.LastIndexOf(':');
        if (firstColon < 0)
        {
            host = value;
            return true;
        }

        if (firstColon != lastColon)
        {
            failureReason =
                "포트가 있는 IPv6 주소는 대괄호로 감싸야 합니다.";
            return false;
        }

        host = value[..firstColon];
        if (!TryParsePort(value[(firstColon + 1)..], out int parsedPort))
        {
            failureReason = "프록시 포트는 1~65535 범위여야 합니다.";
            return false;
        }

        port = parsedPort;
        return true;
    }

    private static bool TryNormalizeHost(
        string value,
        out string normalizedHost)
    {
        normalizedHost = string.Empty;
        string candidate = value.Trim().TrimEnd('.');
        if (candidate.Length == 0
            || candidate.Length > 253
            || candidate.Contains('/')
            || candidate.Contains('\\')
            || candidate.Contains('*')
            || candidate.Any(char.IsControl)
            || candidate.Any(char.IsWhiteSpace))
        {
            return false;
        }

        if (IPAddress.TryParse(candidate, out IPAddress? address))
        {
            normalizedHost = address.ToString();
            return true;
        }

        string ascii;
        try
        {
            ascii = new IdnMapping().GetAscii(candidate).ToLowerInvariant();
        }
        catch (ArgumentException)
        {
            return false;
        }

        if (ascii.Length == 0 || ascii.Length > 253)
        {
            return false;
        }

        foreach (string label in ascii.Split('.'))
        {
            if (label.Length is < 1 or > 63
                || label.StartsWith("-", StringComparison.Ordinal)
                || label.EndsWith("-", StringComparison.Ordinal)
                || label.Any(character =>
                    !char.IsAsciiLetterOrDigit(character)
                    && character is not '-' and not '_'))
            {
                return false;
            }
        }

        normalizedHost = ascii;
        return true;
    }

    private static bool TryParsePort(string value, out int port) =>
        int.TryParse(
            value,
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out port)
        && port is >= 1 and <= 65535;

    private static void AddCountWarnings(
        ICollection<string> warnings,
        int ignored,
        int duplicates,
        int duplicateDirect)
    {
        if (ignored > 0)
        {
            warnings.Add(
                $"현재 대상 스킴 또는 후보 제한에 맞지 않는 프록시 항목 {ignored}개를 선택에서 제외했습니다.");
        }

        if (duplicates > 0)
        {
            warnings.Add(
                $"중복 프록시 엔드포인트 {duplicates}개를 첫 번째 후보로 통합했습니다.");
        }

        if (duplicateDirect > 0)
        {
            warnings.Add(
                $"중복 DIRECT 지시문 {duplicateDirect}개를 첫 번째 지시문으로 통합했습니다.");
        }
    }

    private static ProxyEndpointDecision ResolveDecision(
        IReadOnlyList<ProxyEndpointCandidate> endpoints,
        IReadOnlyList<int> directSequences)
    {
        if (endpoints.Count == 0)
        {
            return directSequences.Count > 0
                ? ProxyEndpointDecision.Direct
                : ProxyEndpointDecision.Unknown;
        }

        if (directSequences.Count == 0)
        {
            return ProxyEndpointDecision.Proxy;
        }

        return endpoints.Min(item => item.Sequence) < directSequences.Min()
            ? ProxyEndpointDecision.ProxyWithDirectFallback
            : ProxyEndpointDecision.DirectWithProxyAlternatives;
    }

    private static bool AppliesToTarget(
        string? appliesToScheme,
        string? targetScheme) =>
        targetScheme is null
        || string.IsNullOrWhiteSpace(appliesToScheme)
        || appliesToScheme.Equals("all", StringComparison.OrdinalIgnoreCase)
        || appliesToScheme.Equals(
            targetScheme,
            StringComparison.OrdinalIgnoreCase);

    private static string BuildEndpointKey(
        string? appliesToScheme,
        ProxyEndpointTransport transport,
        string host,
        int? port) =>
        string.Join(
            '|',
            appliesToScheme ?? string.Empty,
            transport.ToString(),
            host,
            port?.ToString(CultureInfo.InvariantCulture) ?? string.Empty);

    private static bool LooksLikeManualMapping(string value)
    {
        int equals = value.IndexOf('=');
        if (equals <= 0)
        {
            return false;
        }

        string key = value[..equals].Trim();
        return key.Equals("all", StringComparison.OrdinalIgnoreCase)
            || key.Equals("*", StringComparison.Ordinal)
            || Uri.CheckSchemeName(key);
    }

    private static bool StartsWithDirective(string value)
    {
        int separator = value.IndexOfAny([' ', '\t']);
        return IsDirectiveWord(separator < 0 ? value : value[..separator]);
    }

    private static bool IsDirectiveWord(string value) =>
        TryMapDirectiveTransport(value, out _);

    private static bool TryMapDirectiveTransport(
        string value,
        out ProxyEndpointTransport transport)
    {
        transport = value.Trim().ToUpperInvariant() switch
        {
            "PROXY" or "HTTP" => ProxyEndpointTransport.Http,
            "HTTPS" => ProxyEndpointTransport.Https,
            "SOCKS" => ProxyEndpointTransport.Socks,
            "SOCKS4" => ProxyEndpointTransport.Socks4,
            "SOCKS5" => ProxyEndpointTransport.Socks5,
            _ => ProxyEndpointTransport.Unspecified
        };
        return transport != ProxyEndpointTransport.Unspecified;
    }

    private static bool TryMapUriTransport(
        string value,
        out ProxyEndpointTransport transport)
    {
        transport = value.Trim().ToLowerInvariant() switch
        {
            "proxy" or "http" => ProxyEndpointTransport.Http,
            "https" => ProxyEndpointTransport.Https,
            "socks" => ProxyEndpointTransport.Socks,
            "socks4" => ProxyEndpointTransport.Socks4,
            "socks5" => ProxyEndpointTransport.Socks5,
            _ => ProxyEndpointTransport.Unspecified
        };
        return transport != ProxyEndpointTransport.Unspecified;
    }

    private static int? GetUriDefaultPort(
        ProxyEndpointTransport transport) =>
        transport switch
        {
            ProxyEndpointTransport.Http => 80,
            ProxyEndpointTransport.Https => 443,
            ProxyEndpointTransport.Socks
                or ProxyEndpointTransport.Socks4
                or ProxyEndpointTransport.Socks5 => 1080,
            _ => null
        };

    private sealed record RawProxyToken(int Sequence, string Value);

    private sealed record TokenizationResult(
        IReadOnlyList<RawProxyToken> Tokens,
        int TruncatedTokenCount);

    private sealed record ParsedEndpoint(
        ProxyEndpointTransport Transport,
        string Host,
        int? Port);
}
