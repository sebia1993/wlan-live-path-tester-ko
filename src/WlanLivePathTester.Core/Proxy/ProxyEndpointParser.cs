using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace WlanLivePathTester.Core.Proxy;

public enum EffectiveProxyDecisionKind
{
    Direct,
    Proxy,
    ProxyWithDirectFallback,
    Unresolved
}

public enum ProxyEndpointTransport
{
    Http,
    Https,
    Socks,
    Unknown
}

public sealed record ProxyEndpointCandidate(
    int Sequence,
    ProxyEndpointTransport Transport,
    string Host,
    int Port)
{
    public string HostFingerprint =>
        ProxyEndpointFingerprint.Create(Host);
}

public sealed record EffectiveProxyParseResult(
    EffectiveProxyDecisionKind Decision,
    IReadOnlyList<ProxyEndpointCandidate> Endpoints,
    bool HasDirectFallback,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors)
{
    public bool IsValid => Errors.Count == 0;
}

public static class ProxyEndpointParser
{
    public const int MaximumProxyStringLength = 8192;
    public const int MaximumEndpointCount = 8;

    public static EffectiveProxyParseResult Parse(
        string? proxyValue,
        string targetScheme)
    {
        string normalizedScheme = NormalizeTargetScheme(targetScheme);
        if (normalizedScheme.Length == 0)
        {
            return Invalid("대상 URL 스킴은 http 또는 https여야 합니다.");
        }

        string value = (proxyValue ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            return new EffectiveProxyParseResult(
                Decision: EffectiveProxyDecisionKind.Unresolved,
                Endpoints: Array.Empty<ProxyEndpointCandidate>(),
                HasDirectFallback: false,
                Warnings: Array.Empty<string>(),
                Errors:
                [
                    "프록시 결정 문자열이 비어 있습니다."
                ]);
        }

        if (value.Length > MaximumProxyStringLength
            || value.Contains('\r')
            || value.Contains('\n')
            || value.Contains('\0'))
        {
            return Invalid(
                "프록시 결정 문자열이 너무 길거나 허용되지 않는 제어 문자를 포함합니다.");
        }

        string[] rawSegments = value.Split(
            ';',
            StringSplitOptions.RemoveEmptyEntries
            | StringSplitOptions.TrimEntries);
        List<string> selectedSegments = [];
        bool hasSchemeMapping = rawSegments.Any(IsSchemeMapping);

        if (hasSchemeMapping)
        {
            foreach (string segment in rawSegments)
            {
                if (!TrySplitSchemeMapping(
                        segment,
                        out string mappingScheme,
                        out string mappingValue))
                {
                    continue;
                }

                if (mappingScheme.Equals(
                        normalizedScheme,
                        StringComparison.OrdinalIgnoreCase)
                    || mappingScheme.Equals(
                        "proxy",
                        StringComparison.OrdinalIgnoreCase)
                    || (normalizedScheme == "https"
                        && mappingScheme.Equals(
                            "http",
                            StringComparison.OrdinalIgnoreCase)
                        && !rawSegments.Any(item =>
                            TrySplitSchemeMapping(
                                item,
                                out string candidateScheme,
                                out _)
                            && candidateScheme.Equals(
                                "https",
                                StringComparison.OrdinalIgnoreCase))))
                {
                    selectedSegments.Add(mappingValue);
                }
            }

            if (selectedSegments.Count == 0)
            {
                return Invalid(
                    $"대상 스킴 '{normalizedScheme}'에 적용할 프록시 항목이 없습니다.");
            }
        }
        else
        {
            selectedSegments.AddRange(rawSegments);
        }

        List<string> warnings = [];
        List<string> errors = [];
        List<ProxyEndpointCandidate> endpoints = [];
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        bool hasDirect = false;

        foreach (string selected in selectedSegments)
        {
            foreach (string token in SplitCandidateList(selected))
            {
                string trimmed = token.Trim();
                if (string.IsNullOrWhiteSpace(trimmed))
                {
                    continue;
                }

                if (IsDirectToken(trimmed))
                {
                    hasDirect = true;
                    continue;
                }

                if (endpoints.Count >= MaximumEndpointCount)
                {
                    errors.Add(
                        $"프록시 후보는 최대 {MaximumEndpointCount}개까지 허용합니다.");
                    break;
                }

                if (!TryParseEndpoint(
                        trimmed,
                        normalizedScheme,
                        out ParsedEndpoint parsed,
                        out string error))
                {
                    errors.Add(error);
                    continue;
                }

                string key = string.Create(
                    CultureInfo.InvariantCulture,
                    $"{parsed.Transport}|{parsed.Host}|{parsed.Port}");
                if (!seen.Add(key))
                {
                    warnings.Add(
                        $"중복 프록시 후보를 한 번만 사용합니다: {ProxyEndpointFingerprint.Create(parsed.Host)}:{parsed.Port}");
                    continue;
                }

                endpoints.Add(new ProxyEndpointCandidate(
                    Sequence: endpoints.Count + 1,
                    Transport: parsed.Transport,
                    Host: parsed.Host,
                    Port: parsed.Port));
            }
        }

        EffectiveProxyDecisionKind decision = endpoints.Count switch
        {
            0 when hasDirect => EffectiveProxyDecisionKind.Direct,
            > 0 when hasDirect =>
                EffectiveProxyDecisionKind.ProxyWithDirectFallback,
            > 0 => EffectiveProxyDecisionKind.Proxy,
            _ => EffectiveProxyDecisionKind.Unresolved
        };

        if (decision == EffectiveProxyDecisionKind.Unresolved
            && errors.Count == 0)
        {
            errors.Add("프록시 또는 DIRECT 후보를 확인하지 못했습니다.");
        }

        return new EffectiveProxyParseResult(
            Decision: decision,
            Endpoints: endpoints,
            HasDirectFallback: hasDirect,
            Warnings: warnings,
            Errors: errors.Distinct(StringComparer.Ordinal).ToArray());
    }

    private static bool TryParseEndpoint(
        string token,
        string targetScheme,
        out ParsedEndpoint endpoint,
        out string error)
    {
        endpoint = default;
        error = string.Empty;
        string value = token.Trim();
        ProxyEndpointTransport transport =
            ProxyEndpointTransport.Unknown;

        int firstSpace = value.IndexOf(' ');
        if (firstSpace > 0)
        {
            string prefix = value[..firstSpace].Trim();
            ProxyEndpointTransport prefixedTransport =
                ParseTransport(prefix);
            if (prefixedTransport != ProxyEndpointTransport.Unknown
                || prefix.Equals(
                    "PROXY",
                    StringComparison.OrdinalIgnoreCase))
            {
                transport = prefix.Equals(
                    "PROXY",
                    StringComparison.OrdinalIgnoreCase)
                    ? ProxyEndpointTransport.Http
                    : prefixedTransport;
                value = value[(firstSpace + 1)..].Trim();
            }
        }

        if (IsDirectToken(value))
        {
            error = "DIRECT는 프록시 엔드포인트가 아닙니다.";
            return false;
        }

        if (value.Contains("//", StringComparison.Ordinal))
        {
            if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? proxyUri))
            {
                error = "유효하지 않은 프록시 URL입니다.";
                return false;
            }

            ProxyEndpointTransport uriTransport =
                ParseTransport(proxyUri.Scheme);
            if (uriTransport == ProxyEndpointTransport.Unknown)
            {
                error = "프록시 URL은 http, https 또는 socks 스킴만 허용합니다.";
                return false;
            }

            if (!string.IsNullOrEmpty(proxyUri.UserInfo)
                || !string.IsNullOrEmpty(proxyUri.Query)
                || !string.IsNullOrEmpty(proxyUri.Fragment)
                || !proxyUri.AbsolutePath.Equals(
                    "/",
                    StringComparison.Ordinal))
            {
                error = "프록시 엔드포인트에 사용자 정보·경로·쿼리·fragment를 포함할 수 없습니다.";
                return false;
            }

            transport = uriTransport;
            value = proxyUri.IsDefaultPort
                ? proxyUri.IdnHost
                : FormatHostPort(proxyUri.IdnHost, proxyUri.Port);
        }

        if (!TryParseHostPort(
                value,
                DefaultPort(transport, targetScheme),
                out string host,
                out int port,
                out error))
        {
            return false;
        }

        if (transport == ProxyEndpointTransport.Unknown)
        {
            transport = targetScheme == "https"
                ? ProxyEndpointTransport.Http
                : ProxyEndpointTransport.Http;
        }

        endpoint = new ParsedEndpoint(
            Transport: transport,
            Host: host,
            Port: port);
        return true;
    }

    private static bool TryParseHostPort(
        string value,
        int defaultPort,
        out string host,
        out int port,
        out string error)
    {
        host = string.Empty;
        port = 0;
        error = string.Empty;
        string trimmed = value.Trim();

        if (string.IsNullOrWhiteSpace(trimmed)
            || trimmed.Contains('@')
            || trimmed.Contains('/')
            || trimmed.Contains('?')
            || trimmed.Contains('#'))
        {
            error = "프록시 후보에는 호스트와 선택적 포트만 사용할 수 있습니다.";
            return false;
        }

        string hostPart;
        string? portPart = null;
        if (trimmed.StartsWith('[', StringComparison.Ordinal))
        {
            int closing = trimmed.IndexOf(']');
            if (closing <= 1)
            {
                error = "IPv6 프록시 호스트의 대괄호 형식이 잘못됐습니다.";
                return false;
            }

            hostPart = trimmed[1..closing];
            string remainder = trimmed[(closing + 1)..];
            if (!string.IsNullOrEmpty(remainder))
            {
                if (!remainder.StartsWith(':')
                    || remainder.Length == 1)
                {
                    error = "IPv6 프록시 호스트 뒤에는 :port만 사용할 수 있습니다.";
                    return false;
                }

                portPart = remainder[1..];
            }
        }
        else
        {
            int colonCount = trimmed.Count(character => character == ':');
            if (colonCount == 1)
            {
                int separator = trimmed.LastIndexOf(':');
                hostPart = trimmed[..separator];
                portPart = trimmed[(separator + 1)..];
            }
            else if (colonCount > 1
                     && IPAddress.TryParse(
                         trimmed,
                         out IPAddress? ipv6Address))
            {
                hostPart = ipv6Address.ToString();
            }
            else if (colonCount > 1)
            {
                error = "포트가 있는 IPv6 프록시 호스트는 [address]:port 형식을 사용해야 합니다.";
                return false;
            }
            else
            {
                hostPart = trimmed;
            }
        }

        if (!TryNormalizeHost(hostPart, out host))
        {
            error = "유효한 프록시 DNS 호스트 또는 IP 주소가 아닙니다.";
            return false;
        }

        port = defaultPort;
        if (portPart is not null)
        {
            if (!int.TryParse(
                    portPart,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out port)
                || port is < 1 or > 65535)
            {
                error = "프록시 포트는 1~65535 범위의 정수여야 합니다.";
                return false;
            }
        }

        return true;
    }

    private static bool TryNormalizeHost(
        string value,
        out string normalized)
    {
        normalized = string.Empty;
        string trimmed = value.Trim().TrimEnd('.');
        if (string.IsNullOrWhiteSpace(trimmed)
            || trimmed.Length > 253)
        {
            return false;
        }

        if (IPAddress.TryParse(trimmed, out IPAddress? address))
        {
            normalized = address.ToString();
            return true;
        }

        if (Uri.CheckHostName(trimmed) != UriHostNameType.Dns)
        {
            return false;
        }

        try
        {
            normalized = new IdnMapping()
                .GetAscii(trimmed)
                .ToLowerInvariant();
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static IEnumerable<string> SplitCandidateList(
        string value)
    {
        foreach (string commaToken in value.Split(
                     ',',
                     StringSplitOptions.RemoveEmptyEntries
                     | StringSplitOptions.TrimEntries))
        {
            yield return commaToken;
        }
    }

    private static bool IsSchemeMapping(string value) =>
        TrySplitSchemeMapping(
            value,
            out string scheme,
            out _)
        && scheme is "http" or "https" or "proxy" or "socks";

    private static bool TrySplitSchemeMapping(
        string value,
        out string scheme,
        out string mappedValue)
    {
        scheme = string.Empty;
        mappedValue = string.Empty;
        int separator = value.IndexOf('=');
        if (separator <= 0 || separator == value.Length - 1)
        {
            return false;
        }

        scheme = value[..separator].Trim().ToLowerInvariant();
        mappedValue = value[(separator + 1)..].Trim();
        return scheme.All(character =>
            char.IsAsciiLetter(character));
    }

    private static bool IsDirectToken(string value) =>
        value.Equals("DIRECT", StringComparison.OrdinalIgnoreCase)
        || value.Equals("NONE", StringComparison.OrdinalIgnoreCase);

    private static ProxyEndpointTransport ParseTransport(
        string value) =>
        value.Trim().ToLowerInvariant() switch
        {
            "http" => ProxyEndpointTransport.Http,
            "https" => ProxyEndpointTransport.Https,
            "socks" or "socks4" or "socks5" =>
                ProxyEndpointTransport.Socks,
            _ => ProxyEndpointTransport.Unknown
        };

    private static int DefaultPort(
        ProxyEndpointTransport transport,
        string targetScheme) =>
        transport switch
        {
            ProxyEndpointTransport.Https => 443,
            ProxyEndpointTransport.Socks => 1080,
            _ when targetScheme == "https" => 80,
            _ => 80
        };

    private static string NormalizeTargetScheme(string? value) =>
        (value ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "http" => "http",
            "https" => "https",
            _ => string.Empty
        };

    private static string FormatHostPort(string host, int port) =>
        IPAddress.TryParse(host, out IPAddress? address)
        && address.AddressFamily
            == System.Net.Sockets.AddressFamily.InterNetworkV6
            ? $"[{host}]:{port}"
            : $"{host}:{port}";

    private static EffectiveProxyParseResult Invalid(string error) =>
        new(
            Decision: EffectiveProxyDecisionKind.Unresolved,
            Endpoints: Array.Empty<ProxyEndpointCandidate>(),
            HasDirectFallback: false,
            Warnings: Array.Empty<string>(),
            Errors: [error]);

    private readonly record struct ParsedEndpoint(
        ProxyEndpointTransport Transport,
        string Host,
        int Port);
}

public static class ProxyEndpointFingerprint
{
    public const int DisplayLength = 10;

    public static string Create(string? host)
    {
        string normalized = (host ?? string.Empty)
            .Trim()
            .TrimEnd('.')
            .ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return "없음";
        }

        byte[] digest = SHA256.HashData(
            Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(digest)
            [..DisplayLength]
            .ToLowerInvariant();
    }
}
