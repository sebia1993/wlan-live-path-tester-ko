using System.Net;
using System.Text.RegularExpressions;

namespace WlanLivePathTester.Core.Proxy;

internal static class ProxyBypassMatcher
{
    private const int MaximumPatternLength = 1024;

    internal static bool IsBypassed(Uri destination, string? bypassList)
    {
        ArgumentNullException.ThrowIfNull(destination);

        if (string.IsNullOrWhiteSpace(bypassList))
        {
            return false;
        }

        string host = destination.IdnHost.TrimEnd('.');
        foreach (string rawToken in Regex.Split(bypassList, @"[;,\s]+"))
        {
            string token = rawToken.Trim();
            if (token.Length == 0 || token.Length > MaximumPatternLength)
            {
                continue;
            }

            if (token.Equals("<local>", StringComparison.OrdinalIgnoreCase))
            {
                if (IsLocalHostName(host))
                {
                    return true;
                }

                continue;
            }

            if (token.Equals("<-loopback>", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (token == "*")
            {
                return true;
            }

            if (!TrySplitHostAndPort(token, out string hostPattern, out int? port))
            {
                continue;
            }

            if (port is int requiredPort && destination.Port != requiredPort)
            {
                continue;
            }

            if (HostMatches(host, hostPattern))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsLocalHostName(string host)
    {
        return !host.Contains('.')
            && !host.Contains(':')
            && !IPAddress.TryParse(host, out _);
    }

    private static bool TrySplitHostAndPort(
        string rawToken,
        out string hostPattern,
        out int? port)
    {
        hostPattern = string.Empty;
        port = null;

        string token = rawToken.Trim();
        int schemeSeparator = token.IndexOf("://", StringComparison.Ordinal);
        if (schemeSeparator >= 0)
        {
            token = token[(schemeSeparator + 3)..];
        }

        int pathStart = token.IndexOfAny(['/', '?', '#']);
        if (pathStart >= 0)
        {
            token = token[..pathStart];
        }

        if (token.StartsWith("[", StringComparison.Ordinal))
        {
            int closingBracket = token.IndexOf(']');
            if (closingBracket <= 1)
            {
                return false;
            }

            hostPattern = token[1..closingBracket];
            string remainder = token[(closingBracket + 1)..];
            if (remainder.Length == 0)
            {
                return true;
            }

            return remainder.StartsWith(":", StringComparison.Ordinal)
                && int.TryParse(remainder[1..], out int parsedPort)
                && parsedPort is >= 1 and <= 65535
                && AssignPort(parsedPort, out port);
        }

        int colonCount = token.Count(character => character == ':');
        if (colonCount == 1)
        {
            int separator = token.LastIndexOf(':');
            string portText = token[(separator + 1)..];
            if (int.TryParse(portText, out int parsedPort))
            {
                if (parsedPort is < 1 or > 65535)
                {
                    return false;
                }

                hostPattern = token[..separator].TrimEnd('.');
                port = parsedPort;
                return hostPattern.Length > 0;
            }
        }

        hostPattern = token.Trim('[', ']').TrimEnd('.');
        return hostPattern.Length > 0;
    }

    private static bool AssignPort(int parsedPort, out int? port)
    {
        port = parsedPort;
        return true;
    }

    private static bool HostMatches(string host, string rawPattern)
    {
        string pattern = rawPattern.Trim().TrimEnd('.');
        if (pattern.Length == 0)
        {
            return false;
        }

        if (pattern.StartsWith(".", StringComparison.Ordinal))
        {
            string root = pattern[1..];
            return host.Equals(root, StringComparison.OrdinalIgnoreCase)
                || host.EndsWith(pattern, StringComparison.OrdinalIgnoreCase);
        }

        if (!pattern.Contains('*') && !pattern.Contains('?'))
        {
            return host.Equals(pattern, StringComparison.OrdinalIgnoreCase);
        }

        string regexPattern = "^"
            + Regex.Escape(pattern)
                .Replace("\\*", ".*", StringComparison.Ordinal)
                .Replace("\\?", ".", StringComparison.Ordinal)
            + "$";

        return Regex.IsMatch(
            host,
            regexPattern,
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
            TimeSpan.FromMilliseconds(100));
    }
}
