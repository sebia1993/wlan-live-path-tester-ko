using WlanLivePathTester.Core.Models;

namespace WlanLivePathTester.Core.Proxy;

internal static class ProxyDirectiveParser
{
    private static readonly char[] TokenSeparators = [' ', '\t', '\r', '\n', ','];

    internal static ProxySelection SelectManual(
        Uri destination,
        string? proxyConfiguration,
        string? bypassList)
    {
        ArgumentNullException.ThrowIfNull(destination);

        if (ProxyBypassMatcher.IsBypassed(destination, bypassList))
        {
            return ProxySelection.Direct(wasBypassed: true);
        }

        if (string.IsNullOrWhiteSpace(proxyConfiguration))
        {
            return ProxySelection.Direct();
        }

        return ParseConfiguration(destination, proxyConfiguration);
    }

    internal static ProxySelection SelectAutoProxyList(
        Uri destination,
        string? proxyList)
    {
        ArgumentNullException.ThrowIfNull(destination);

        if (string.IsNullOrWhiteSpace(proxyList))
        {
            return ProxySelection.Unknown("WinHTTP가 프록시 목록을 반환하지 않았습니다.");
        }

        return ParseConfiguration(destination, proxyList);
    }

    private static ProxySelection ParseConfiguration(Uri destination, string configuration)
    {
        List<ProxyRouteHop> hops = [];
        bool sawSchemeSpecificDirective = false;
        bool sawApplicableDirective = false;
        int invalidDirectiveCount = 0;

        foreach (string rawSegment in configuration.Split(';'))
        {
            string segment = rawSegment.Trim();
            if (segment.Length == 0)
            {
                continue;
            }

            if (segment.Equals("DIRECT", StringComparison.OrdinalIgnoreCase))
            {
                sawApplicableDirective = true;
                hops.Add(new ProxyRouteHop(ProxyRouteKind.Direct, null));
                continue;
            }

            if (TrySplitSchemeDirective(segment, out string scheme, out string value))
            {
                sawSchemeSpecificDirective = true;
                if (!scheme.Equals(destination.Scheme, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                sawApplicableDirective = true;
                invalidDirectiveCount += ParseDirectiveValue(value, hops);
                continue;
            }

            sawApplicableDirective = true;
            invalidDirectiveCount += ParseDirectiveValue(segment, hops);
        }

        if (hops.Count > 0)
        {
            return ProxySelection.FromHops(hops, invalidDirectiveCount);
        }

        if (sawSchemeSpecificDirective && !sawApplicableDirective)
        {
            return ProxySelection.Direct();
        }

        if (invalidDirectiveCount > 0)
        {
            return ProxySelection.Unknown(
                "적용 가능한 프록시 지시문을 해석하지 못했습니다.",
                invalidDirectiveCount);
        }

        return ProxySelection.Unknown("적용 가능한 프록시 지시문이 없습니다.");
    }

    private static bool TrySplitSchemeDirective(
        string segment,
        out string scheme,
        out string value)
    {
        scheme = string.Empty;
        value = string.Empty;

        int separator = segment.IndexOf('=');
        if (separator <= 0)
        {
            return false;
        }

        string candidate = segment[..separator].Trim();
        if (candidate.Length == 0
            || candidate.Any(character =>
                !char.IsAsciiLetterOrDigit(character)
                && character is not '+' and not '-' and not '.'))
        {
            return false;
        }

        scheme = candidate;
        value = segment[(separator + 1)..].Trim();
        return true;
    }

    private static int ParseDirectiveValue(
        string rawValue,
        ICollection<ProxyRouteHop> destination)
    {
        string value = rawValue.Trim();
        if (value.Length == 0)
        {
            return 1;
        }

        if (value.Equals("DIRECT", StringComparison.OrdinalIgnoreCase))
        {
            destination.Add(new ProxyRouteHop(ProxyRouteKind.Direct, null));
            return 0;
        }

        if (StartsWithUnsupportedSocksDirective(value))
        {
            return 1;
        }

        value = RemoveSupportedDirectivePrefix(value);
        string[] tokens = value.Split(
            TokenSeparators,
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        int invalidCount = 0;
        for (int index = 0; index < tokens.Length; index++)
        {
            string token = tokens[index];

            if (token.Equals("DIRECT", StringComparison.OrdinalIgnoreCase))
            {
                destination.Add(new ProxyRouteHop(ProxyRouteKind.Direct, null));
                continue;
            }

            if (IsSupportedDirectivePrefix(token))
            {
                if (index + 1 >= tokens.Length)
                {
                    invalidCount++;
                    continue;
                }

                token = tokens[++index];
            }

            if (StartsWithUnsupportedSocksDirective(token))
            {
                invalidCount++;
                continue;
            }

            if (TryNormalizeProxyEndpoint(token, out string? normalized))
            {
                destination.Add(new ProxyRouteHop(ProxyRouteKind.Proxy, normalized));
            }
            else
            {
                invalidCount++;
            }
        }

        return tokens.Length == 0 ? 1 : invalidCount;
    }

    private static bool TryNormalizeProxyEndpoint(
        string rawEndpoint,
        out string? normalized)
    {
        normalized = null;
        string endpoint = rawEndpoint.Trim().Trim('"', '\'');
        if (endpoint.Length == 0 || endpoint.Equals("DIRECT", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string candidate = endpoint.StartsWith("//", StringComparison.Ordinal)
            ? $"http:{endpoint}"
            : endpoint.Contains("://", StringComparison.Ordinal)
                ? endpoint
                : $"http://{endpoint}";

        if (!Uri.TryCreate(candidate, UriKind.Absolute, out Uri? uri)
            || string.IsNullOrWhiteSpace(uri.Host)
            || !string.IsNullOrEmpty(uri.UserInfo)
            || (!uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                && !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            || (uri.AbsolutePath.Length > 1 && uri.AbsolutePath != "/")
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment))
        {
            return false;
        }

        string host = uri.HostNameType == UriHostNameType.IPv6
            ? $"[{uri.IdnHost}]"
            : uri.IdnHost;

        normalized = $"{uri.Scheme.ToLowerInvariant()}://{host}:{uri.Port}";
        return true;
    }

    private static string RemoveSupportedDirectivePrefix(string value)
    {
        foreach (string prefix in new[] { "PROXY ", "HTTP ", "HTTPS " })
        {
            if (value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return value[prefix.Length..].Trim();
            }
        }

        return value;
    }

    private static bool IsSupportedDirectivePrefix(string value) =>
        value.Equals("PROXY", StringComparison.OrdinalIgnoreCase)
        || value.Equals("HTTP", StringComparison.OrdinalIgnoreCase)
        || value.Equals("HTTPS", StringComparison.OrdinalIgnoreCase);

    private static bool StartsWithUnsupportedSocksDirective(string value) =>
        value.Equals("SOCKS", StringComparison.OrdinalIgnoreCase)
        || value.Equals("SOCKS4", StringComparison.OrdinalIgnoreCase)
        || value.Equals("SOCKS5", StringComparison.OrdinalIgnoreCase)
        || value.StartsWith("SOCKS ", StringComparison.OrdinalIgnoreCase)
        || value.StartsWith("SOCKS4 ", StringComparison.OrdinalIgnoreCase)
        || value.StartsWith("SOCKS5 ", StringComparison.OrdinalIgnoreCase);
}
