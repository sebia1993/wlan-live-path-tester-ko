using WlanLivePathTester.Core.Models;
using WlanLivePathTester.Core.Security;

namespace WlanLivePathTester.Core.Proxy;

internal static class ProxyDirectiveParser
{
    private static readonly char[] TokenSeparators = [' ', ','];
    private const int MaximumListLength = 16 * 1024;
    private const int MaximumHopCount = 64;

    internal static ProxySelection SelectManual(Uri destination, string? proxyConfiguration, string? bypassList)
    {
        ArgumentNullException.ThrowIfNull(destination);
        if (!NetworkInputBoundary.IsHttpUri(destination, out _)
            || !NetworkInputBoundary.TrySingleLine(proxyConfiguration, MaximumListLength,
                out string configuration, out _, allowEmpty: true)
            || !ProxyBypassMatcher.IsValidList(bypassList))
            return ProxySelection.Unknown("프록시 대상·설정·바이패스 원문이 올바르지 않습니다.", 1);
        if (ProxyBypassMatcher.IsBypassed(destination, bypassList)) return ProxySelection.Direct(wasBypassed: true);
        if (configuration.Length == 0) return ProxySelection.Direct();
        return ParseConfiguration(destination, configuration);
    }

    internal static ProxySelection SelectAutoProxyList(Uri destination, string? proxyList)
    {
        ArgumentNullException.ThrowIfNull(destination);
        if (!NetworkInputBoundary.IsHttpUri(destination, out _)
            || !NetworkInputBoundary.TrySingleLine(proxyList, MaximumListLength, out string configuration, out _))
            return ProxySelection.Unknown("WinHTTP 프록시 목록 또는 대상 원문이 올바르지 않습니다.", 1);
        return ParseConfiguration(destination, configuration);
    }

    private static ProxySelection ParseConfiguration(Uri destination, string configuration)
    {
        if (configuration.Split([';', ' ', ','], StringSplitOptions.RemoveEmptyEntries).Length > MaximumHopCount * 2)
            return ProxySelection.Unknown("프록시 목록의 항목 수 제한을 초과했습니다.", 1);
        List<ProxyRouteHop> hops = [];
        bool sawSchemeSpecificDirective = false, sawApplicableDirective = false;
        int invalidDirectiveCount = 0;
        foreach (string rawSegment in configuration.Split(';'))
        {
            string segment = rawSegment.Trim();
            if (segment.Length == 0) continue;
            if (segment.Equals("DIRECT", StringComparison.OrdinalIgnoreCase))
            {
                sawApplicableDirective = true;
                hops.Add(new ProxyRouteHop(ProxyRouteKind.Direct, null));
                continue;
            }
            if (TrySplitSchemeDirective(segment, out string scheme, out string value))
            {
                sawSchemeSpecificDirective = true;
                if (!scheme.Equals(destination.Scheme, StringComparison.OrdinalIgnoreCase)) continue;
                sawApplicableDirective = true;
                invalidDirectiveCount += ParseDirectiveValue(value, hops);
                continue;
            }
            sawApplicableDirective = true;
            invalidDirectiveCount += ParseDirectiveValue(segment, hops);
        }
        if (hops.Count > MaximumHopCount)
            return ProxySelection.Unknown("프록시 후보 수 제한을 초과했습니다.", 1);
        if (hops.Count > 0) return ProxySelection.FromHops(hops, invalidDirectiveCount);
        if (sawSchemeSpecificDirective && !sawApplicableDirective) return ProxySelection.Direct();
        if (invalidDirectiveCount > 0)
            return ProxySelection.Unknown("적용 가능한 프록시 지시문을 해석하지 못했습니다.", invalidDirectiveCount);
        return ProxySelection.Unknown("적용 가능한 프록시 지시문이 없습니다.");
    }

    private static bool TrySplitSchemeDirective(string segment, out string scheme, out string value)
    {
        scheme = string.Empty;
        value = string.Empty;
        int separator = segment.IndexOf('=');
        if (separator <= 0) return false;
        string candidate = segment[..separator].Trim();
        if (candidate.Length == 0 || candidate.Any(character => !char.IsAsciiLetterOrDigit(character)
                && character is not '+' and not '-' and not '.')) return false;
        scheme = candidate;
        value = segment[(separator + 1)..].Trim();
        return true;
    }

    private static int ParseDirectiveValue(string rawValue, ICollection<ProxyRouteHop> destination)
    {
        string value = rawValue.Trim();
        if (value.Length == 0) return 1;
        if (value.Equals("DIRECT", StringComparison.OrdinalIgnoreCase))
        {
            destination.Add(new ProxyRouteHop(ProxyRouteKind.Direct, null));
            return 0;
        }
        if (StartsWithUnsupportedSocksDirective(value)) return 1;
        value = RemoveSupportedDirectivePrefix(value);
        string[] tokens = value.Split(TokenSeparators,
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
                if (index + 1 >= tokens.Length) { invalidCount++; continue; }
                token = tokens[++index];
            }
            if (StartsWithUnsupportedSocksDirective(token)) { invalidCount++; continue; }
            if (TryNormalizeProxyEndpoint(token, out string? normalized))
                destination.Add(new ProxyRouteHop(ProxyRouteKind.Proxy, normalized));
            else invalidCount++;
        }
        return tokens.Length == 0 ? 1 : invalidCount;
    }

    private static bool TryNormalizeProxyEndpoint(string rawEndpoint, out string? normalized)
    {
        normalized = null;
        string endpoint = rawEndpoint.Trim(' ');
        if (endpoint.Length >= 2 && endpoint[0] is '\'' or '"' && endpoint[^1] == endpoint[0])
            endpoint = endpoint[1..^1];
        if (endpoint.Length == 0 || endpoint.IndexOfAny(['\'', '"', '?', '#', '@', '%', '\\']) >= 0
            || endpoint.Equals("DIRECT", StringComparison.OrdinalIgnoreCase)) return false;
        string candidate = endpoint.StartsWith("//", StringComparison.Ordinal) ? $"http:{endpoint}"
            : endpoint.Contains("://", StringComparison.Ordinal) ? endpoint : $"http://{endpoint}";
        if (!NetworkInputBoundary.TryHttpUri(candidate, out Uri? uri, out _)
            || uri.AbsolutePath != "/" || uri.Query.Length != 0) return false;
        string host = uri.HostNameType == UriHostNameType.IPv6
            ? $"[{uri.IdnHost.Trim('[', ']')}]" : uri.IdnHost;
        normalized = $"{uri.Scheme.ToLowerInvariant()}://{host}:{uri.Port}";
        return true;
    }

    private static string RemoveSupportedDirectivePrefix(string value)
    {
        foreach (string prefix in new[] { "PROXY ", "HTTP ", "HTTPS " })
            if (value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return value[prefix.Length..].Trim();
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
