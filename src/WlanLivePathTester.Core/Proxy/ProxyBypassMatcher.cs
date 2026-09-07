using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using WlanLivePathTester.Core.Security;

namespace WlanLivePathTester.Core.Proxy;

internal static class ProxyBypassMatcher
{
    private const int MaximumPatternLength = 1024;
    private const int MaximumPatternCount = 64;
    private const int MaximumListLength = 16 * 1024;

    internal static bool IsValidList(string? raw)
    {
        if (!NetworkInputBoundary.TrySingleLine(raw, MaximumListLength,
                out string list, out _, allowEmpty: true)) return false;
        string[] tokens = Split(list);
        return tokens.Length <= MaximumPatternCount && tokens.All(token => token.Length <= MaximumPatternLength);
    }

    internal static bool IsBypassed(Uri destination, string? bypassList)
    {
        ArgumentNullException.ThrowIfNull(destination);
        if (!NetworkInputBoundary.IsHttpUri(destination, out _) || !IsValidList(bypassList)
            || string.IsNullOrEmpty(bypassList)) return false;
        string host = destination.IdnHost.Trim('[', ']').TrimEnd('.');
        foreach (string token in Split(bypassList))
        {
            if (token.Equals("<local>", StringComparison.OrdinalIgnoreCase))
            {
                if (IsLocalHostName(host)) return true;
                continue;
            }
            if (token.Equals("<-loopback>", StringComparison.OrdinalIgnoreCase)) continue;
            if (token == "*") return true;
            if (!TrySplitHostAndPort(token, out string hostPattern, out int? port)) continue;
            if (port is int requiredPort && destination.Port != requiredPort) continue;
            if (HostMatches(host, hostPattern)) return true;
        }
        return false;
    }

    private static string[] Split(string list) =>
        list.Split([';', ',', ' '], StringSplitOptions.RemoveEmptyEntries);
    private static bool IsLocalHostName(string host) =>
        !host.Contains('.') && !host.Contains(':') && !IPAddress.TryParse(host, out _);

    private static bool TrySplitHostAndPort(string rawToken, out string hostPattern, out int? port)
    {
        hostPattern = string.Empty;
        port = null;
        string token = rawToken;
        int schemeSeparator = token.IndexOf("://", StringComparison.Ordinal);
        if (schemeSeparator >= 0) token = token[(schemeSeparator + 3)..];
        int pathStart = token.IndexOfAny(['/', '?', '#']);
        if (pathStart >= 0) token = token[..pathStart];
        if (token.Contains('@') || token.Contains('\\') || token.Contains('%')) return false;
        if (token.StartsWith("[", StringComparison.Ordinal))
        {
            int closingBracket = token.IndexOf(']');
            if (closingBracket <= 1 || !IPAddress.TryParse(token[1..closingBracket], out IPAddress? address)
                || address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetworkV6) return false;
            hostPattern = address.ToString();
            string remainder = token[(closingBracket + 1)..];
            if (remainder.Length == 0) return true;
            if (!remainder.StartsWith(":", StringComparison.Ordinal)
                || !TryPort(remainder[1..], out int parsedPort)) return false;
            port = parsedPort;
            return true;
        }
        int colonCount = token.Count(character => character == ':');
        if (colonCount == 1)
        {
            int separator = token.LastIndexOf(':');
            if (!TryPort(token[(separator + 1)..], out int parsedPort)) return false;
            hostPattern = token[..separator].TrimEnd('.');
            port = parsedPort;
            return hostPattern.Length > 0;
        }
        hostPattern = token.TrimEnd('.');
        return hostPattern.Length > 0;
    }

    private static bool TryPort(string value, out int port) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out port) && port is >= 1 and <= 65535;

    private static bool HostMatches(string host, string rawPattern)
    {
        string pattern = rawPattern.TrimEnd('.');
        if (pattern.Length == 0) return false;
        if (pattern.StartsWith(".", StringComparison.Ordinal))
            return host.Equals(pattern[1..], StringComparison.OrdinalIgnoreCase)
                || host.EndsWith(pattern, StringComparison.OrdinalIgnoreCase);
        if (!pattern.Contains('*') && !pattern.Contains('?'))
            return host.Equals(pattern, StringComparison.OrdinalIgnoreCase);
        string regexPattern = "^" + Regex.Escape(pattern)
            .Replace("\\*", ".*", StringComparison.Ordinal)
            .Replace("\\?", ".", StringComparison.Ordinal) + "$";
        return Regex.IsMatch(host, regexPattern,
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking,
            TimeSpan.FromMilliseconds(100));
    }
}
