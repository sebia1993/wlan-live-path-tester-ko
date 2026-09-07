using System.Text.Json;
using WlanLivePathTester.Core.Models;
using WlanLivePathTester.Core.Proxy;
using WlanLivePathTester.Windows.Proxy;

namespace WlanLivePathTester.WindowsSmoke;

internal static class ProxyImportRawInputTests
{
    private static readonly Uri Target = new("https://target.example.invalid/File?key=AbC");
    private static readonly CurrentUserProxyConfiguration Manual =
        new(true, null, false, null, "https=proxy.example.invalid:8080", null);

    internal static async Task RunAsync()
    {
        (string Name, Func<Task> Run)[] groups =
        [
            ("invalid original URI reads nothing", InvalidTargetsAsync),
            ("invalid PAC configuration never invokes automatic resolver", InvalidPacAsync),
            ("invalid bypass is not reselected as a manual proxy", InvalidBypassAsync),
            ("control-only manual settings are not treated as absent", InvalidManualAsync),
            ("valid automatic decision ignores unselected manual input", ValidAutomaticAsync),
            ("configuration recovery and raw selection identity", RecoveryAndIdentityAsync)
        ];
        foreach ((string name, Func<Task> run) in groups)
        {
            await run().WaitAsync(TimeSpan.FromSeconds(5));
            Console.WriteLine($"PASS proxy import raw input: {name}");
        }
        Console.WriteLine("PASS proxy import raw input: 6 groups; injected configuration and resolver only");
    }

    private static async Task InvalidTargetsAsync()
    {
        int reads = 0;
        int resolves = 0;
        WindowsRouteProxyImporter importer = new(
            () => { reads++; return Manual; },
            (_, _) => { resolves++; throw new InvalidOperationException("must not resolve"); });
        foreach (string raw in new[]
        {
            Target.OriginalString + "\t", Target.OriginalString + "\u200B",
            Target.OriginalString + "%0d", "https://user:secret@target.example.invalid/",
            "https://target.example.invalid/#", " https://target.example.invalid/file "
        })
        {
            var result = await importer.ImportAsync(new Uri(raw, UriKind.Absolute), true);
            Check(result.Status == WindowsRouteProxyImportStatus.InvalidInput && !result.HasSelection,
                "Invalid raw target must fail before configuration reads.");
        }
        Check(reads == 0 && resolves == 0, "Invalid targets must invoke neither callback.");
    }

    private static async Task InvalidPacAsync()
    {
        foreach (bool autoDetect in new[] { false, true })
        foreach (string raw in new[]
        {
            "https://pac.example.invalid/script.pac\t", "\t", " ",
            "file:///local.pac", "https://user:secret@pac.example.invalid/script.pac",
            "https://pac.example.invalid/script.pac#fragment",
            "https://pac.example.invalid/" + new string('a', 2049)
        })
        {
            int resolves = 0;
            WindowsRouteProxyImporter importer = new(
                () => Manual with { AutoDetectEnabled = autoDetect, AutoConfigUrl = raw },
                (_, _) => { resolves++; throw new InvalidOperationException("must not resolve"); });
            var result = await importer.ImportAsync(Target, true);
            Check(result.Status == WindowsRouteProxyImportStatus.UnsafeOrUnsupportedResult
                && !result.AutomaticLookupAttempted && !result.HasSelection && resolves == 0,
                "An invalid configured PAC must not disappear into WPAD/manual fallback.");
        }
    }

    private static async Task InvalidBypassAsync()
    {
        foreach (string raw in new[] { "*\t", "*.example.invalid\r\n", new string('*', 1025), "\u200B" })
        {
            WindowsRouteProxyImporter importer = new(
                () => Manual with { BypassList = raw },
                (_, _) => throw new InvalidOperationException("manual import must not resolve"));
            var result = await importer.ImportAsync(Target);
            Check(result.Status == WindowsRouteProxyImportStatus.UnsafeOrUnsupportedResult
                && !result.HasSelection && !result.WasBypassed,
                "A rejected bypass must not be discarded before rebuilding a manual selection.");
            Check(!JsonSerializer.Serialize(result).Contains("proxy.example.invalid", StringComparison.Ordinal),
                "Rejected output must not reveal the raw proxy host.");
        }
    }

    private static async Task InvalidManualAsync()
    {
        foreach (string raw in new[] { "\t", "https=proxy.example.invalid:8080\n", "\u200B", new string(' ', 16385) })
        {
            WindowsRouteProxyImporter importer = new(
                () => Manual with { ManualProxy = raw },
                (_, _) => throw new InvalidOperationException("manual import must not resolve"));
            var result = await importer.ImportAsync(Target);
            Check(result.Status == WindowsRouteProxyImportStatus.UnsafeOrUnsupportedResult && !result.HasSelection,
                "Malformed raw manual input must not become NoConfiguredProxy or DIRECT.");
        }
    }

    private static async Task ValidAutomaticAsync()
    {
        int resolves = 0;
        WindowsRouteProxyImporter importer = new(
            () => Manual with
            {
                AutoConfigUrl = "https://pac.example.invalid/script.pac",
                ManualProxy = "invalid\t", BypassList = "*\t"
            },
            (_, _) => { resolves++; return AutomaticResult(); });
        var consent = await importer.ImportAsync(Target);
        Check(consent.Status == WindowsRouteProxyImportStatus.NeedsAutomaticLookupConsent && resolves == 0,
            "Consent remains mandatory and unselected manual input is not used instead.");
        var result = await importer.ImportAsync(Target, true);
        Check(result.Status == WindowsRouteProxyImportStatus.Ready
            && result.Source == ProxyConfigurationSource.Pac && resolves == 1,
            "A valid target-specific decision must not be rejected because of unused manual settings.");
    }

    private static async Task RecoveryAndIdentityAsync()
    {
        int reads = 0;
        WindowsRouteProxyImporter importer = new(
            () => ++reads == 1 ? Manual with { BypassList = "*\t" } : Manual,
            (_, _) => throw new InvalidOperationException("manual import must not resolve"));
        Check((await importer.ImportAsync(Target)).Status == WindowsRouteProxyImportStatus.UnsafeOrUnsupportedResult,
            "First malformed snapshot must fail.");
        var result = await importer.ImportAsync(Target);
        Check(result.Status == WindowsRouteProxyImportStatus.Ready && result.TryGetSelection(Target, out _),
            "A failed import must release busy state and allow a valid retry.");
        foreach (string raw in new[]
        {
            Target.OriginalString + "\t", Target.OriginalString + "\u200B",
            Target.OriginalString.Replace("File", "file", StringComparison.Ordinal),
            Target.OriginalString.Replace("AbC", "abc", StringComparison.Ordinal)
        })
            Check(!result.TryGetSelection(new Uri(raw), out _),
                "Normalized malformed input or a different resource cannot reuse the selection.");
    }

    private static ResolvedProxyRoute AutomaticResult()
    {
        ProxySelection selection = ProxyDirectiveParser.SelectAutoProxyList(
            Target, "PROXY auto.example.invalid:8080; DIRECT");
        ProxyRouteResolution summary = new(
            ProxyResolutionStatus.Success, selection.RouteKind, ProxyConfigurationSource.Pac,
            ProxyRouteExpectationEvaluator.Evaluate(NetworkPathKind.External, selection.RouteKind),
            selection.ProxyCandidateCount, selection.HasDirectFallback, false,
            false, true, selection.InvalidDirectiveCount, null, "synthetic");
        return new(summary, selection);
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
