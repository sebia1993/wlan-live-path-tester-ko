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
    ProxyWithDirectFallback
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
            string applicability = string.IsNullOrWhiteSpace(
                AppliesToScheme)
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
            string port = Port?.ToString(
                CultureInfo.InvariantCulture)
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
    bool DirectFallback,
    int ParsedEndpointCount,
    int IgnoredEndpointCount,
    int DuplicateEndpointCount,
    int RejectedTokenCount,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors)
{
    public bool IsUsable =>
        Errors.Count == 0
        && Decision is ProxyEndpointDecision.Direct
            or ProxyEndpointDecision.Proxy
            or ProxyEndpointDecision.ProxyWithDirectFallback;
}

public static class ProxyEndpointParser
{
    private const int MaximumInputLength = 16 * 1024;
    private const int MaximumTokenCount = 64;
    private const int MaximumEndpointCount = 32;
    private const int FingerprintLength = 10;

    public static ProxyEndpointParseResult Parse(
        string? value,
        Uri? targetUri = null)
    {
        string input = (value ?? string.Empty).Trim();
        List<string> warnings = [];
        List<string> errors = [];

        string? targetScheme = ResolveTargetScheme(
            targetUri,
            errors);
        if (errors.Count > 0)
        {
            return EmptyResult(
                inputPresent: input.Length > 0,
                ProxyEndpointSourceKind.Unknown,
                targetScheme,
                warnings,
                errors);
        }

        if (input.Length == 0)
        {
            warnings.Add(
                "프록시 경로 문자열이 비어 있어 프록시 엔드포인트를 확인하지 않았습니다.");
            return EmptyResult(
                inputPresent: false,
                ProxyEndpointSourceKind.Unknown,
                targetScheme,
                warnings,
                errors);
        }

        if (input.Length > MaximumInputLength)
        {
            errors.Add(
                $"프록시 경로 문자열은 {MaximumInputLength}자를 초과할 수 없습니다.");
            return EmptyResult(
                inputPresent: true,
                ProxyEndpointSourceKind.Unknown,
                targetScheme,
                warnings,
                errors);
        }

        IReadOnlyList<RawProxyToken> tokens = Tokenize(
            input,
            warnings);
        ProxyEndpointSourceKind sourceKind = InferSourceKind(
            tokens);
        if (sourceKind == ProxyEndpointSourceKind.Mixed)
        {
            warnings.Add(
                "자동 프록시 지시문과 수동 스킴 매핑이 함께 있어 입력 순서대로 안전하게 해석했습니다.");
        }

        bool directFallback = false;
        int parsedEndpointCount = 0;
        int ignoredEndpointCount = 0;
        int duplicateEndpointCount = 0;
        int rejectedTokenCount = 0;
        List<ProxyEndpointCandidate> selected = [];
        HashSet<string> endpointKeys = new(
            StringComparer.OrdinalIgnoreCase);

        foreach (RawProxyToken token in tokens)
        {
            if (token.Value.Equals(
                    "DIRECT",
                    StringComparison.OrdinalIgnoreCase))
            {
                directFallback = true;
                continue;
            }

            string? appliesToScheme = null;
            ProxyEndpointTransport directiveTransport =
                ProxyEndpointTransport.Unspecified;
            string endpointValue;

            if (TrySplitDirective(
                    token.Value,
                    out ProxyEndpointTransport parsedTransport,
                    out endpointValue))
            {
                directiveTransport = parsedTransport;
            }
            else if (TrySplitManualMapping(
                         token.Value,
                         out string? parsedTargetScheme,
                         out endpointValue))
            {
                appliesToScheme = parsedTargetScheme;
            }
            else
            {
                endpointValue = token.Value;
            }

            if (!TryParseEndpoint(
                    endpointValue,
                    directiveTransport,
                    out ParsedEndpoint? parsed,
                    out string failureReason))
            {
                rejectedTokenCount++;
                warnings.Add(
                    $"프록시 항목 {token.Sequence}을(를) 사용하지 않았습니다: {failureReason}");
                continue;
            }

            parsedEndpointCount++;
            if (!AppliesToTarget(
                    appliesToScheme,
                    targetScheme))
            {
                ignoredEndpointCount++;
                continue;
            }

            if (selected.Count >= MaximumEndpointCount)
            {
                ignoredEndpointCount++;
                warnings.Add(
                    $"프록시 후보는 최대 {MaximumEndpointCount}개까지만 사용합니다.");
                continue;
            }

            string key = BuildEndpointKey(
                targetScheme is null ? appliesToScheme : null,
                parsed.Transport,
                parsed.Host,
                parsed.Port);
            if (!endpointKeys.Add(key))
            {
                duplicateEndpointCount++;
                continue;
            }

            selected.Add(new ProxyEndpointCandidate(
                Sequence: token.Sequence,
                AppliesToScheme: appliesToScheme,
                Transport: parsed.Transport,
                Host: parsed.Host,
                Port: parsed.Port,
                HostFingerprint: CreateHostFingerprint(
                    parsed.Host)));
        }

        if (ignoredEndpointCount > 0)
        {
            warnings.Add(
                $"현재 대상 스킴 또는 후보 제한에 맞지 않는 프록시 항목 {ignoredEndpointCount}개를 선택에서 제외했습니다.");
        }

        if (duplicateEndpointCount > 0)
        {
            warnings.Add(
                $"중복 프록시 엔드포인트 {duplicateEndpointCount}개를 첫 번째 후보로 통합했습니다.");
        }

        ProxyEndpointDecision decision = selected.Count switch
        {
            > 0 when directFallback =>
                ProxyEndpointDecision.ProxyWithDirectFallback,
            > 0 => ProxyEndpointDecision.Proxy,
            _ when directFallback => ProxyEndpointDecision.Direct,
            _ => ProxyEndpointDecision.Unknown
        };

        if (decision == ProxyEndpointDecision.Unknown)
        {
            warnings.Add(parsedEndpointCount > 0
                ? "현재 대상 URL에 적용되는 프록시 엔드포인트 또는 DIRECT 지시문이 없습니다."
                : "사용 가능한 프록시 엔드포인트 또는 DIRECT 지시문을 찾지 못했습니다.");
        }

        return new ProxyEndpointParseResult(
            InputPresent: true,
            SourceKind: sourceKind,
            Decision: decision,
            TargetScheme: targetScheme,
            Endpoints: selected.ToArray(),
            DirectFallback: directFallback,
            ParsedEndpointCount: parsedEndpointCount,
            IgnoredEndpointCount: ignoredEndpointCount,
            DuplicateEndpointCount: duplicateEndpointCount,
            RejectedTokenCount: rejectedTokenCount,
            Warnings: warnings.ToArray(),
            Errors: errors.ToArray());
    }

    public static string CreateHostFingerprint(string host)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        string normalized = host.Trim().TrimEnd('.').ToLowerInvariant();
        byte[] digest = SHA256.HashData(
            Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(digest)
            [..FingerprintLength]
            .ToLowerInvariant();
    }

    private static ProxyEndpointParseResult EmptyResult(
        bool inputPresent,
        ProxyEndpointSourceKind sourceKind,
        string? targetScheme,
        IReadOnlyList<string> warnings,
        IReadOnlyList<string> errors) =>
        new(
            InputPresent: inputPresent,
            SourceKind: sourceKind,
            Decision: ProxyEndpointDecision.Unknown,
            TargetScheme: targetScheme,
            Endpoints: Array.Empty<ProxyEndpointCandidate>(),
            DirectFallback: false,
            ParsedEndpointCount: 0,
            IgnoredEndpointCount: 0,
            DuplicateEndpointCount: 0,
            RejectedTokenCount: 0,
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

    private static IReadOnlyList<RawProxyToken> Tokenize(
        string input,
        ICollection<string> warnings)
    {
        List<RawProxyToken> tokens = [];
        int nextSequence = 1;
        bool limitReported = false;

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
                if (tokens.Count >= MaximumTokenCount)
                {
                    if (!limitReported)
                    {
                        warnings.Add(
                            $"프록시 항목은 최대 {MaximumTokenCount}개까지만 해석합니다.");
                        limitReported = true;
                    }

                    continue;
                }

                string word = words[index];
                string tokenValue = word;
                if (IsProxyDirectiveWord(word))
                {
                    if (index + 1 < words.Length
                        && !IsProxyDirectiveWord(words[index + 1])
                        && !words[index + 1].Equals(
                            "DIRECT",
                            StringComparison.OrdinalIgnoreCase)
                        && !LooksLikeManualMapping(words[index + 1]))
                    {
                        tokenValue = word + " " + words[++index];
                    }
                }
                else if (word.EndsWith(
                             '=',
                             StringComparison.Ordinal)
                         && index + 1 < words.Length)
                {
                    tokenValue = word + words[++index];
                }

                tokens.Add(new RawProxyToken(
                    Sequence: nextSequence++,
                    Value: tokenValue));
            }
        }

        return tokens;
    }

    private static ProxyEndpointSourceKind InferSourceKind(
        IReadOnlyList<RawProxyToken> tokens)
    {
        bool hasAutoDirective = tokens.Any(token =>
            token.Value.Equals(
                "DIRECT",
                StringComparison.OrdinalIgnoreCase)
            || StartsWithProxyDirective(token.Value));
        bool hasManualMapping = tokens.Any(token =>
            LooksLikeManualMapping(token.Value));

        return (hasAutoDirective, hasManualMapping) switch
        {
            (true, true) => ProxyEndpointSourceKind.Mixed,
            (true, false) => ProxyEndpointSourceKind.AutoProxyResult,
            (false, true) => ProxyEndpointSourceKind.ManualServerList,
            _ => ProxyEndpointSourceKind.Unknown
        };
    }

    private static bool TrySplitDirective(
        string token,
        out ProxyEndpointTransport transport,
        out string endpoint)
    {
        transport = ProxyEndpointTransport.Unspecified;
        endpoint = string.Empty;
        int separator = token.IndexOfAny([' ', '\t']);
        string directive = separator < 0
            ? token
            : token[..separator];
        if (!TryMapDirectiveTransport(
                directive,
                out transport))
        {
            return false;
        }

        endpoint = separator < 0
            ? string.Empty
            : token[(separator + 1)..].Trim();
        return true;
    }

    private static bool TrySplitManualMapping(
        string token,
        out string? appliesToScheme,
        out string endpoint)
    {
        appliesToScheme = null;
        endpoint = string.Empty;
        int separator = token.IndexOf('=');
        if (separator <= 0)
        {
            return false;
        }

        string key = token[..separator].Trim();
        endpoint = token[(separator + 1)..].Trim();
        if (key.Equals("all", StringComparison.OrdinalIgnoreCase)
            || key.Equals("*", StringComparison.Ordinal))
        {
            appliesToScheme = "all";
            return true;
        }

        if (!Uri.CheckSchemeName(key))
        {
            return false;
        }

        appliesToScheme = key.ToLowerInvariant();
        return true;
    }

    private static bool TryParseEndpoint(
        string value,
        ProxyEndpointTransport directiveTransport,
        out ParsedEndpoint? endpoint,
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
                    out ProxyEndpointTransport uriTransport,
                    out host,
                    out port,
                    out failureReason))
            {
                return false;
            }

            transport = uriTransport;
        }
        else if (!TryParseAuthority(
                     candidate,
                     out host,
                     out port,
                     out failureReason))
        {
            return false;
        }

        if (!TryNormalizeHost(
                host,
                out string normalizedHost))
        {
            failureReason = "호스트 형식이 유효하지 않습니다.";
            return false;
        }

        endpoint = new ParsedEndpoint(
            Transport: transport,
            Host: normalizedHost,
            Port: port);
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

        if (!Uri.TryCreate(
                value,
                UriKind.Absolute,
                out Uri? uri)
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
                && !uri.AbsolutePath.Equals(
                    "/",
                    StringComparison.Ordinal)))
        {
            failureReason = "프록시 URI에는 경로, query 또는 fragment를 사용할 수 없습니다.";
            return false;
        }

        host = uri.IdnHost;
        port = uri.Port > 0
            ? uri.Port
            : GetUriDefaultPort(transport);
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
            failureReason = "사용자 정보가 포함된 프록시 엔드포인트는 허용하지 않습니다.";
            return false;
        }

        if (value.StartsWith('[', StringComparison.Ordinal))
        {
            int closingBracket = value.IndexOf(']');
            if (closingBracket <= 1)
            {
                failureReason = "IPv6 대괄호 형식이 올바르지 않습니다.";
                return false;
            }

            host = value[1..closingBracket];
            string suffix = value[(closingBracket + 1)..];
            if (suffix.Length == 0)
            {
                return true;
            }

            if (!suffix.StartsWith(':', StringComparison.Ordinal)
                || !TryParsePort(suffix[1..], out int parsedPort))
            {
                failureReason = "IPv6 프록시 포트가 유효하지 않습니다.";
                return false;
            }

            port = parsedPort;
            return true;
        }

        if (IPAddress.TryParse(value, out IPAddress? literalAddress))
        {
            host = literalAddress.ToString();
            return true;
        }

        int firstColon = value.IndexOf(':');
        int lastColon = value.LastIndexOf(':');
        if (firstColon >= 0)
        {
            if (firstColon != lastColon)
            {
                failureReason = "포트가 있는 IPv6 주소는 대괄호로 감싸야 합니다.";
                return false;
            }

            host = value[..firstColon];
            if (!TryParsePort(
                    value[(firstColon + 1)..],
                    out int parsedPort))
            {
                failureReason = "프록시 포트는 1~65535 범위여야 합니다.";
                return false;
            }

            port = parsedPort;
            return true;
        }

        host = value;
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

        if (IPAddress.TryParse(
                candidate,
                out IPAddress? literalAddress))
        {
            normalizedHost = literalAddress.ToString();
            return true;
        }

        string ascii;
        try
        {
            ascii = new IdnMapping()
                .GetAscii(candidate)
                .ToLowerInvariant();
        }
        catch (ArgumentException)
        {
            return false;
        }

        if (ascii.Length == 0 || ascii.Length > 253)
        {
            return false;
        }

        string[] labels = ascii.Split('.');
        foreach (string label in labels)
        {
            if (label.Length is < 1 or > 63
                || label.StartsWith('-', StringComparison.Ordinal)
                || label.EndsWith('-', StringComparison.Ordinal)
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

    private static bool TryParsePort(
        string value,
        out int port) =>
        int.TryParse(
            value,
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out port)
        && port is >= 1 and <= 65535;

    private static bool AppliesToTarget(
        string? appliesToScheme,
        string? targetScheme) =>
        targetScheme is null
        || string.IsNullOrWhiteSpace(appliesToScheme)
        || appliesToScheme.Equals(
            "all",
            StringComparison.OrdinalIgnoreCase)
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
            port?.ToString(CultureInfo.InvariantCulture)
                ?? string.Empty);

    private static bool LooksLikeManualMapping(string value)
    {
        int separator = value.IndexOf('=');
        if (separator <= 0)
        {
            return false;
        }

        string key = value[..separator].Trim();
        return key.Equals("all", StringComparison.OrdinalIgnoreCase)
            || key.Equals("*", StringComparison.Ordinal)
            || Uri.CheckSchemeName(key);
    }

    private static bool StartsWithProxyDirective(string value)
    {
        int separator = value.IndexOfAny([' ', '\t']);
        string firstWord = separator < 0
            ? value
            : value[..separator];
        return IsProxyDirectiveWord(firstWord);
    }

    private static bool IsProxyDirectiveWord(string value) =>
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

    private sealed record RawProxyToken(
        int Sequence,
        string Value);

    private sealed record ParsedEndpoint(
        ProxyEndpointTransport Transport,
        string Host,
        int? Port);
}
