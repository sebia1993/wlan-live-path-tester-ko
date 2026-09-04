using System.Text.RegularExpressions;

namespace WlanLivePathTester.Core.Reporting;

public static partial class SensitiveDataRedactor
{
    public static string MaskSsid(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "확인 불가" : "[SSID 마스킹됨]";

    public static string MaskBssid(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "확인 불가" : "[BSSID 마스킹됨]";

    public static string? RedactText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        string redacted = value;
        redacted = WindowsUserPathRegex().Replace(redacted, @"C:\Users\[사용자]");
        redacted = UrlRegex().Replace(redacted, match => RedactUrl(match.Value));
        redacted = MacAddressRegex().Replace(redacted, "[MAC 마스킹됨]");
        redacted = Ipv4Regex().Replace(redacted, "[IP 마스킹됨]");
        redacted = Ipv6Regex().Replace(redacted, "[IPv6 마스킹됨]");
        redacted = EmailRegex().Replace(redacted, "[이메일 마스킹됨]");
        return redacted;
    }

    public static string RedactUrl(string value)
    {
        string trimmed = value.TrimEnd('.', ',', ';', ')', ']');
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out Uri? uri)
            || uri.Scheme is not ("http" or "https"))
        {
            return "[URL 마스킹됨]";
        }

        string fileName = Path.GetFileName(uri.AbsolutePath);
        string pathHint = string.IsNullOrWhiteSpace(fileName)
            ? string.Empty
            : $"/{SanitizePathSegment(fileName)}";
        return $"{uri.Scheme}://[호스트 마스킹됨]{pathHint}";
    }

    public static string ProtectCsvFormula(string? value)
    {
        string sanitized = RedactText(value) ?? string.Empty;
        string candidate = sanitized.TrimStart(' ');
        if (candidate.Length > 0
            && candidate[0] is '=' or '+' or '-' or '@' or '\t' or '\r')
        {
            return "'" + sanitized;
        }

        return sanitized;
    }

    public static string SafeFileComponent(string? value, string fallback = "report")
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        HashSet<char> invalid = Path.GetInvalidFileNameChars().ToHashSet();
        string cleaned = new(value
            .Trim()
            .Select(character => invalid.Contains(character) || char.IsControl(character)
                ? '_'
                : character)
            .ToArray());
        cleaned = WhitespaceRegex().Replace(cleaned, "-").Trim('-', '_', '.');
        return string.IsNullOrWhiteSpace(cleaned) ? fallback : cleaned;
    }

    private static string SanitizePathSegment(string value)
    {
        string cleaned = Regex.Replace(value, @"[^A-Za-z0-9._-]", "_");
        return cleaned.Length <= 80 ? cleaned : cleaned[..80];
    }

    [GeneratedRegex(@"(?i)C:\\Users\\[^\\\s]+")]
    private static partial Regex WindowsUserPathRegex();

    [GeneratedRegex("""(?i)https?://[^\s"'<>]+""")]
    private static partial Regex UrlRegex();

    [GeneratedRegex(@"(?i)(?<![0-9A-F])(?:[0-9A-F]{2}[:-]){5}[0-9A-F]{2}(?![0-9A-F])")]
    private static partial Regex MacAddressRegex();

    [GeneratedRegex(@"(?<!\d)(?:(?:25[0-5]|2[0-4]\d|1?\d?\d)\.){3}(?:25[0-5]|2[0-4]\d|1?\d?\d)(?!\d)")]
    private static partial Regex Ipv4Regex();

    [GeneratedRegex(@"(?i)(?<![0-9A-F:])(?:[0-9A-F]{0,4}:){2,7}[0-9A-F]{0,4}(?![0-9A-F:])")]
    private static partial Regex Ipv6Regex();

    [GeneratedRegex(@"(?i)\b[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}\b")]
    private static partial Regex EmailRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
