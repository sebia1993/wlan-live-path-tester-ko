namespace WlanLivePathTester.Core.Security;

public static class UrlDisplayFormatter
{
    public static string WithoutSensitiveQuery(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
        {
            return "[유효하지 않은 URL]";
        }

        UriBuilder builder = new(uri)
        {
            UserName = string.Empty,
            Password = string.Empty,
            Fragment = string.Empty,
            Query = string.IsNullOrEmpty(uri.Query) ? string.Empty : "[마스킹됨]"
        };

        return builder.Uri.AbsoluteUri;
    }
}
