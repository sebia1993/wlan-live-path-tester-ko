using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net;

namespace WlanLivePathTester.Core.Security;

/// <summary>Pure, bounded input checks. Never performs DNS, HTTP or configuration reads.</summary>
public static class NetworkInputBoundary
{
    public const int MaximumUrlLength = 2048;
    public const int MaximumHostLength = 253;
    public const int MaximumNameLength = 128;
    public const int MaximumUrlCount = 4;
    public const int MaximumUrlListLength = MaximumUrlCount * (MaximumUrlLength + 2);

    public static bool TrySingleLine(string? raw, int maximumLength,
        out string normalized, out string error, bool allowEmpty = false)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumLength);
        normalized = string.Empty;
        error = string.Empty;
        string value = raw ?? string.Empty;
        if (value.Length > maximumLength)
        {
            error = $"원문 입력은 {maximumLength}자 이하여야 합니다.";
            return false;
        }
        for (int index = 0; index < value.Length; index++)
        {
            char character = value[index];
            if (char.IsHighSurrogate(character))
            {
                if (index + 1 >= value.Length || !char.IsLowSurrogate(value[index + 1]))
                {
                    error = "입력에 올바르지 않은 유니코드 문자가 있습니다.";
                    return false;
                }
                UnicodeCategory pairCategory = CharUnicodeInfo.GetUnicodeCategory(value, index);
                if (pairCategory == UnicodeCategory.Format)
                {
                    error = "입력에 허용되지 않는 숨김 문자가 있습니다.";
                    return false;
                }
                index++;
                continue;
            }
            if (char.IsLowSurrogate(character) || char.IsControl(character)
                || (character != ' ' && char.IsWhiteSpace(character))
                || char.GetUnicodeCategory(character) == UnicodeCategory.Format)
            {
                error = "입력에 제어 문자, 줄 구분자 또는 숨김 문자가 있습니다.";
                return false;
            }
        }
        // Only ordinary edge spaces are presentation whitespace. Never erase
        // tabs, control-only values or excess raw length before validation.
        normalized = value.Trim(' ');
        if (!allowEmpty && normalized.Length == 0)
        {
            error = "입력이 비어 있습니다.";
            return false;
        }
        return true;
    }

    public static bool TryHttpUri(string? raw, [NotNullWhen(true)] out Uri? uri, out string error)
    {
        uri = null;
        if (!TryUriReference(raw, out string value, out error)) return false;
        if (!(value.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || value.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            || !Uri.TryCreate(value, UriKind.Absolute, out Uri? parsed)
            || parsed.Scheme is not ("http" or "https")
            || string.IsNullOrEmpty(parsed.Host) || parsed.Port is < 1 or > 65535)
        {
            error = "호스트와 유효한 포트를 포함한 절대 HTTP 또는 HTTPS URL이 필요합니다.";
            return false;
        }
        int authorityStart = value.IndexOf("://", StringComparison.Ordinal) + 3;
        int authorityEnd = value.IndexOfAny(['/', '?', '#'], authorityStart);
        ReadOnlySpan<char> authority = value.AsSpan(authorityStart,
            (authorityEnd < 0 ? value.Length : authorityEnd) - authorityStart);
        if (!string.IsNullOrEmpty(parsed.UserInfo) || authority.Contains('@'))
        {
            error = "URL에 사용자 이름이나 비밀번호를 포함할 수 없습니다.";
            return false;
        }
        if (value.Contains('#', StringComparison.Ordinal))
        {
            error = "URL fragment는 사용할 수 없습니다.";
            return false;
        }
        // Escaped hosts and IPv6 scope identifiers are not accepted as HTTP
        // destinations. Route-only literal scope handling is separate below.
        if (authority.Contains('%') || parsed.IdnHost.Length > MaximumHostLength)
        {
            error = "URL 호스트 형식 또는 길이가 허용 범위를 벗어납니다.";
            return false;
        }
        uri = parsed;
        return true;
    }

    public static bool IsHttpUri(Uri? uri, out string error)
    {
        if (uri is null || !uri.IsAbsoluteUri)
        {
            error = "절대 HTTP 또는 HTTPS URL이 필요합니다.";
            return false;
        }
        return TryHttpUri(uri.OriginalString, out _, out error);
    }

    public static bool TryUriReference(string? raw, out string value, out string error)
    {
        if (!TrySingleLine(raw, MaximumUrlLength, out value, out error)) return false;
        if (value.Any(char.IsWhiteSpace) || value.Contains('\\', StringComparison.Ordinal))
        {
            error = "URL에는 원문 공백 또는 역슬래시를 사용할 수 없습니다. 경로 공백은 %20으로 표기하십시오.";
            return false;
        }
        for (int index = 0; index < value.Length; index++)
        {
            if (value[index] != '%') continue;
            if (index + 2 >= value.Length || !TryHex(value[index + 1], out int high)
                || !TryHex(value[index + 2], out int low))
            {
                error = "URL의 퍼센트 인코딩이 올바르지 않습니다.";
                return false;
            }
            int code = high * 16 + low;
            if (code < 32 || code == 127)
            {
                error = "URL에 인코딩된 제어 문자를 사용할 수 없습니다.";
                return false;
            }
            index += 2;
        }
        return true;
    }

    public static bool TryAllowedHost(string? raw, out string host, out string error)
    {
        host = string.Empty;
        if (!TrySingleLine(raw, MaximumHostLength, out string value, out error)) return false;
        if (value.Any(char.IsWhiteSpace) || value.IndexOfAny(['/', '\\', '?', '#', '@', ':', '%', '*', '[', ']']) >= 0)
        {
            error = "승인 호스트에는 스킴·포트·경로 없는 정확한 DNS 이름 또는 IPv4 주소만 사용할 수 있습니다.";
            return false;
        }
        value = value.TrimEnd('.');
        try
        {
            host = IPAddress.TryParse(value, out IPAddress? address)
                ? address.ToString()
                : new IdnMapping().GetAscii(value).ToLowerInvariant();
        }
        catch (ArgumentException)
        {
            error = "승인 호스트의 유니코드 형식을 해석할 수 없습니다.";
            return false;
        }
        if (host.Length is 0 or > MaximumHostLength || Uri.CheckHostName(host) == UriHostNameType.Unknown)
        {
            host = string.Empty;
            error = "승인 호스트 이름이 올바르지 않습니다.";
            return false;
        }
        return true;
    }

    public static bool IsLocalDnsName(string host)
    {
        string value = host.TrimEnd('.');
        return value.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || value.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)
            || value.EndsWith(".local", StringComparison.OrdinalIgnoreCase)
            || !value.Contains('.', StringComparison.Ordinal);
    }

    public static bool TryRouteHost(string? raw, out string host, out string error)
    {
        host = string.Empty;
        if (!TrySingleLine(raw, MaximumUrlLength, out string value, out error)) return false;
        string literal = value.StartsWith('[') && value.EndsWith(']')
            ? value[1..^1] : value;
        if (IPAddress.TryParse(literal, out IPAddress? address))
        {
            host = address.ToString();
            return true;
        }
        if (value.Contains("://", StringComparison.Ordinal))
        {
            if (!TryHttpUri(value, out Uri? uri, out error)) return false;
            host = uri.IdnHost.Trim('[', ']');
            return true;
        }
        if (value.IndexOfAny(['/', '\\', '?', '#', '@', '%']) >= 0
            || !TryHttpUri("http://" + value, out Uri? authority, out error)
            || authority.AbsolutePath != "/" || authority.Query.Length != 0)
        {
            error = "유효한 호스트 이름, IP 주소 또는 host:port 형식이 필요합니다.";
            return false;
        }
        host = authority.IdnHost.Trim('[', ']');
        return true;
    }

    public static bool TryHttpUrlLines(string? raw, out string[] urls, out string error)
    {
        urls = [];
        error = string.Empty;
        if (raw is null || raw.Length > MaximumUrlListLength)
        {
            error = $"URL 목록의 원문은 {MaximumUrlListLength}자 이하여야 합니다.";
            return false;
        }
        List<string> accepted = [];
        HashSet<string> seen = new(StringComparer.Ordinal);
        string[] lines = raw.Split(["\r\n", "\r", "\n"], StringSplitOptions.None);
        for (int index = 0; index < lines.Length; index++)
        {
            if (lines[index].All(character => character == ' ')) continue;
            if (!TryHttpUri(lines[index], out Uri? uri, out string lineError))
            {
                error = $"URL 목록 {index + 1}번째 줄: {lineError}";
                return false;
            }
            if (seen.Add(CreateHttpKey(uri))) accepted.Add(uri.OriginalString);
            if (accepted.Count > MaximumUrlCount)
            {
                error = $"URL은 최대 {MaximumUrlCount}개까지 사용할 수 있습니다.";
                return false;
            }
        }
        if (accepted.Count == 0)
        {
            error = "측정할 URL을 한 줄에 하나씩 입력하십시오.";
            return false;
        }
        urls = accepted.ToArray();
        return true;
    }

    public static string CreateHttpKey(Uri uri) =>
        uri.GetComponents(UriComponents.SchemeAndServer, UriFormat.UriEscaped).ToLowerInvariant()
        + uri.GetComponents(UriComponents.PathAndQuery, UriFormat.UriEscaped);

    private static bool TryHex(char value, out int digit)
    {
        digit = value is >= '0' and <= '9' ? value - '0'
            : value is >= 'a' and <= 'f' ? value - 'a' + 10
            : value is >= 'A' and <= 'F' ? value - 'A' + 10 : -1;
        return digit >= 0;
    }
}
