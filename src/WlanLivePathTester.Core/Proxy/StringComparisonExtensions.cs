namespace WlanLivePathTester.Core.Proxy;

internal static class StringComparisonExtensions
{
    public static bool StartsWith(
        this string value,
        char candidate,
        StringComparison comparisonType)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length == 0)
        {
            return false;
        }

        return comparisonType switch
        {
            StringComparison.Ordinal => value[0] == candidate,
            StringComparison.OrdinalIgnoreCase =>
                char.ToUpperInvariant(value[0])
                == char.ToUpperInvariant(candidate),
            _ => value.StartsWith(
                candidate.ToString(),
                comparisonType)
        };
    }
}
