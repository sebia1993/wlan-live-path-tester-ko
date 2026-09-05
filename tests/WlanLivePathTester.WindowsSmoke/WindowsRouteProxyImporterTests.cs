using System.Text.Json;
using WlanLivePathTester.Core.Models;
using WlanLivePathTester.Core.Proxy;
using WlanLivePathTester.Core.Routing;
using WlanLivePathTester.Windows.Proxy;

namespace WlanLivePathTester.WindowsSmoke;

internal static class WindowsRouteProxyImporterTests
{
    private static readonly Uri Target = new("https://target-private.example.invalid/file.bin?token=secret-value");
    private const string ProxyHost = "proxy-private.example.invalid";
    private static readonly CurrentUserProxyConfiguration Manual =
        new(true, null, false, null, $"http=other.example.invalid:8080;https={ProxyHost}:8443", null);
    private static readonly CurrentUserProxyConfiguration Automatic =
        Manual with { AutoConfigUrl = "https://pac-private.example.invalid/proxy.pac" };

    internal static async Task RunAsync()
    {
        await InvalidTargetsAndTimeoutsReadNothing();
        await PreCanceledReadNothing();
        await ManualSettingsStayLocalAndFilterByScheme();
        await ManualBypassIsBoundToTheTarget();
        await MissingConfigurationIsNotDirect();
        await ConfigurationFailureDoesNotResolve();
        await AutomaticLookupRequiresConsentEvenWithManualSettings();
        await AutomaticSourcesPreserveProvenanceAndOrder();
        await DirectAutomaticDecisionIsPreserved();
        await FailedOrFallbackAutomaticDecisionIsRejected();
        await PartialAndDirectFirstAutomaticResultsAreRejected();
        await MalformedOrInapplicableManualInputIsRejected();
        await TargetAndMonotonicAgeAreRequired();
        await PublicResultDoesNotExposeRawData();
        await CancellationWaitsForNativeReturnAndBlocksReentry();
        await ReaderFailureDoesNotLeakOrPoisonNextRun();
        Console.WriteLine("PASS Windows proxy import: 16 scenario groups, injected readers only");
    }

    private static async Task InvalidTargetsAndTimeoutsReadNothing()
    {
        int reads = 0;
        WindowsRouteProxyImporter importer = new(
            () => { reads++; return Manual; }, (_, _) => throw new InvalidOperationException());
        Uri?[] invalid =
        [
            null, new Uri("relative", UriKind.Relative), new Uri("ftp://example.invalid/file"),
            new Uri("https://user:pass@example.invalid/file"),
            new Uri("https://example.invalid/file#secret"),
            new Uri("https://example.invalid/" + new string('a', 2049)),
            new Uri("https://example.invalid/file\n")
        ];
        foreach (Uri? target in invalid)
        {
            Ensure((await importer.ImportAsync(target, true)).Status
                == WindowsRouteProxyImportStatus.InvalidInput, "Unsafe target accepted.");
        }
        foreach (int timeout in new[] { 999, 30001 })
        {
            Ensure((await importer.ImportAsync(Target, true, timeout)).Status
                == WindowsRouteProxyImportStatus.InvalidInput, "Unsafe timeout accepted.");
        }
        Ensure(reads == 0, "Invalid input must not invoke configuration reader.");
    }

    private static async Task PreCanceledReadNothing()
    {
        int reads = 0;
        WindowsRouteProxyImporter importer = new(
            () => { reads++; return Manual; }, (_, _) => throw new InvalidOperationException());
        using CancellationTokenSource cts = new();
        cts.Cancel();
        WindowsRouteProxyImportResult result = await importer.ImportAsync(
            Target, true, cancellationToken: cts.Token);
        Ensure(result.Status == WindowsRouteProxyImportStatus.Canceled && reads == 0,
            "Pre-cancel must read nothing.");
    }

    private static async Task ManualSettingsStayLocalAndFilterByScheme()
    {
        int resolves = 0;
        WindowsRouteProxyImporter importer = new(() => Manual,
            (_, _) => { resolves++; throw new InvalidOperationException(); });
        WindowsRouteProxyImportResult result = await importer.ImportAsync(Target);
        Ensure(result.Status == WindowsRouteProxyImportStatus.Ready
            && result.Source == ProxyConfigurationSource.Manual && resolves == 0
            && !result.AutomaticLookupAttempted, "Manual import must remain local.");
        Ensure(result.TryGetSelection(Target, out ProxyDirectiveSourceSelectionResult? selection),
            "Target-bound selection missing.");
        ProxyEndpointParseResult parsed = ProxyEndpointParser.Parse(selection!.SelectedDirectiveText, Target);
        Ensure(parsed.Endpoints.Count == 1, "Unrelated HTTP mapping must not be applied to HTTPS.");
    }

    private static async Task ManualBypassIsBoundToTheTarget()
    {
        WindowsRouteProxyImporter importer = new(
            () => Manual with { BypassList = Target.Host },
            (_, _) => throw new InvalidOperationException());
        WindowsRouteProxyImportResult result = await importer.ImportAsync(Target);
        Ensure(result.Status == WindowsRouteProxyImportStatus.Direct && result.WasBypassed,
            "Explicit local bypass must remain DIRECT.");
        Ensure(result.TryGetSelection(Target, out _) && !result.TryGetSelection(
            new Uri("https://different.example.invalid/file"), out _),
            "Bypass decision must not apply to a different URL.");
    }

    private static async Task MissingConfigurationIsNotDirect()
    {
        WindowsRouteProxyImporter importer = new(
            () => Manual with { ManualProxy = null }, (_, _) => throw new InvalidOperationException());
        WindowsRouteProxyImportResult result = await importer.ImportAsync(Target);
        Ensure(result.Status == WindowsRouteProxyImportStatus.NoConfiguredProxy
            && !result.HasSelection, "Missing configuration is not evidence of DIRECT.");
    }

    private static async Task ConfigurationFailureDoesNotResolve()
    {
        WindowsRouteProxyImporter importer = new(
            () => Automatic with { ReadSucceeded = false, Win32Error = 5 },
            (_, _) => throw new InvalidOperationException("resolver must not run"));
        WindowsRouteProxyImportResult result = await importer.ImportAsync(Target, true);
        Ensure(result.Status == WindowsRouteProxyImportStatus.ConfigurationReadFailed
            && !result.AutomaticLookupAttempted && !result.HasSelection,
            "Configuration failure must not run automatic lookup.");
    }

    private static async Task AutomaticLookupRequiresConsentEvenWithManualSettings()
    {
        foreach (CurrentUserProxyConfiguration configuration in new[]
        {
            Automatic,
            Manual with { AutoDetectEnabled = true },
            Automatic with { AutoDetectEnabled = true }
        })
        {
            int calls = 0;
            WindowsRouteProxyImporter importer = new(() => configuration,
                (_, _) => { calls++; throw new InvalidOperationException(); });
            WindowsRouteProxyImportResult result = await importer.ImportAsync(Target);
            Ensure(result.Status == WindowsRouteProxyImportStatus.NeedsAutomaticLookupConsent
                && calls == 0 && !result.HasSelection && !result.AutomaticLookupAttempted,
                "Automatic settings require opt-in; do not choose available manual settings.");
        }
    }

    private static async Task AutomaticSourcesPreserveProvenanceAndOrder()
    {
        foreach (ProxyConfigurationSource source in new[]
        { ProxyConfigurationSource.Pac, ProxyConfigurationSource.Wpad, ProxyConfigurationSource.WpadThenPac })
        {
            int calls = 0;
            WindowsRouteProxyImporter importer = new(() => Automatic, (target, timeout) =>
            {
                Ensure(target == Target && timeout == 2345, "Target or timeout was changed.");
                calls++;
                return Resolved(source, $"PROXY {ProxyHost}:8080; PROXY second.example.invalid:3128; DIRECT");
            });
            WindowsRouteProxyImportResult result = await importer.ImportAsync(Target, true, 2345);
            Ensure(result.Status == WindowsRouteProxyImportStatus.Ready && result.Source == source
                && result.AutomaticLookupAttempted && calls == 1,
                "Automatic source provenance or call count changed.");
            Ensure(result.TryGetSelection(Target, out ProxyDirectiveSourceSelectionResult? selection),
                "Imported automatic selection missing.");
            Ensure(selection!.SourceKind == ProxyDirectiveSourceKind.TargetSpecificAutoProxy
                && selection.ProxyEndpointCount == 2 && selection.HasDirectFallback,
                "Automatic selection lost provenance, candidates or DIRECT fallback.");
            string directive = selection.SelectedDirectiveText!;
            Ensure(directive.IndexOf(ProxyHost, StringComparison.Ordinal)
                < directive.IndexOf("second.example.invalid", StringComparison.Ordinal)
                && directive.EndsWith("DIRECT", StringComparison.Ordinal), "Fallback order changed.");
        }
    }

    private static async Task DirectAutomaticDecisionIsPreserved()
    {
        WindowsRouteProxyImporter importer = new(() => Automatic, (_, _) =>
        {
            ResolvedProxyRoute raw = Resolved(ProxyConfigurationSource.Pac, "DIRECT");
            return raw with { Summary = raw.Summary with { AutoLogonRetried = true } };
        });
        WindowsRouteProxyImportResult result = await importer.ImportAsync(Target, true);
        Ensure(result.Status == WindowsRouteProxyImportStatus.Direct && result.AutoLogonRetried
            && result.ProxyEndpointCount == 0 && result.DirectDirectiveCount == 1,
            "DIRECT or PAC authentication-retry metadata lost.");
    }

    private static async Task FailedOrFallbackAutomaticDecisionIsRejected()
    {
        foreach (ProxyConfigurationSource source in new[]
        { ProxyConfigurationSource.ManualFallback, ProxyConfigurationSource.Manual,
          ProxyConfigurationSource.None, (ProxyConfigurationSource)999 })
        {
            WindowsRouteProxyImporter importer = new(() => Automatic,
                (_, _) => Resolved(source, $"PROXY {ProxyHost}:8080"));
            WindowsRouteProxyImportResult result = await importer.ImportAsync(Target, true);
            Ensure(result.Status == WindowsRouteProxyImportStatus.AutomaticResolutionFailed
                && !result.HasSelection, "A fallback or changed source became a PAC result.");
        }
        WindowsRouteProxyImporter failed = new(() => Automatic, (_, _) =>
        {
            ResolvedProxyRoute raw = Resolved(ProxyConfigurationSource.Pac, "DIRECT");
            return raw with { Summary = raw.Summary with { Status = ProxyResolutionStatus.TimedOut } };
        });
        Ensure((await failed.ImportAsync(Target, true)).Status
            == WindowsRouteProxyImportStatus.AutomaticResolutionFailed,
            "A failed automatic result became DIRECT.");
    }

    private static async Task PartialAndDirectFirstAutomaticResultsAreRejected()
    {
        ResolvedProxyRoute normal = Resolved(ProxyConfigurationSource.Pac, $"PROXY {ProxyHost}:8080");
        ResolvedProxyRoute[] invalid =
        [
            Resolved(ProxyConfigurationSource.Pac, $"DIRECT; PROXY {ProxyHost}:8080"),
            Resolved(ProxyConfigurationSource.Pac, $"PROXY {ProxyHost}:8080; SOCKS5 other.example.invalid:1080"),
            normal with { Summary = normal.Summary with { InvalidDirectiveCount = 1 } },
            normal with { Summary = normal.Summary with { ProxyCandidateCount = 2 } },
            normal with { Summary = normal.Summary with { RouteKind = ProxyRouteKind.Direct } }
        ];
        foreach (ResolvedProxyRoute raw in invalid)
        {
            WindowsRouteProxyImporter importer = new(() => Automatic, (_, _) => raw);
            WindowsRouteProxyImportResult result = await importer.ImportAsync(Target, true);
            Ensure(result.Status == WindowsRouteProxyImportStatus.UnsafeOrUnsupportedResult
                && !result.HasSelection, "Ambiguous/partial result was silently reduced.");
        }
    }

    private static async Task MalformedOrInapplicableManualInputIsRejected()
    {
        foreach (string text in new[]
        { "http=other.example.invalid:8080", $"PROXY {ProxyHost}:0", $"PROXY {ProxyHost}:8080; UNKNOWN invalid" })
        {
            WindowsRouteProxyImporter importer = new(
                () => Manual with { ManualProxy = text }, (_, _) => throw new InvalidOperationException());
            Ensure((await importer.ImportAsync(Target)).Status
                == WindowsRouteProxyImportStatus.UnsafeOrUnsupportedResult,
                "Malformed or inapplicable manual input was accepted.");
        }
    }

    private static async Task TargetAndMonotonicAgeAreRequired()
    {
        TestClock clock = new();
        WindowsRouteProxyImporter importer = new(() => Manual,
            (_, _) => throw new InvalidOperationException(), clock);
        WindowsRouteProxyImportResult result = await importer.ImportAsync(Target);
        Ensure(result.TryGetSelection(Target, out _), "Fresh selection rejected.");
        Ensure(!result.TryGetSelection(new Uri(Target.AbsoluteUri.Replace("secret-value", "SECRET-VALUE")), out _),
            "Query changes must invalidate the imported decision.");
        Ensure(!result.TryGetSelection(new Uri("https://" + Target.Host + "/other"), out _),
            "Path changes must invalidate the imported decision.");
        clock.Advance(TimeSpan.FromMinutes(5));
        Ensure(!result.TryGetSelection(Target, out _), "Expired decision accepted.");
    }

    private static async Task PublicResultDoesNotExposeRawData()
    {
        WindowsRouteProxyImporter importer = new(() => Automatic, (_, _) =>
        {
            ResolvedProxyRoute raw = Resolved(ProxyConfigurationSource.Pac, $"PROXY {ProxyHost}:8080; DIRECT");
            return raw with { Summary = raw.Summary with { Message = Target.AbsoluteUri } };
        });
        WindowsRouteProxyImportResult result = await importer.ImportAsync(Target, true);
        string json = JsonSerializer.Serialize(result);
        foreach (string secret in new[]
        { Target.Host, "secret-value", ProxyHost, "pac-private", "other.example.invalid" })
        {
            Ensure(!json.Contains(secret, StringComparison.OrdinalIgnoreCase)
                && !result.ToString().Contains(secret, StringComparison.OrdinalIgnoreCase)
                && !result.Message.Contains(secret, StringComparison.OrdinalIgnoreCase),
                "Public result reflected raw data.");
        }
    }

    private static async Task CancellationWaitsForNativeReturnAndBlocksReentry()
    {
        TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using ManualResetEventSlim release = new(false);
        using CancellationTokenSource cts = new();
        int calls = 0;
        WindowsRouteProxyImporter importer = new(() => Automatic, (_, _) =>
        {
            Interlocked.Increment(ref calls);
            entered.TrySetResult();
            if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("Test latch expired.");
            return Resolved(ProxyConfigurationSource.Pac, $"PROXY {ProxyHost}:8080");
        });
        Task<WindowsRouteProxyImportResult> first = importer.ImportAsync(Target, true, cancellationToken: cts.Token);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            cts.Cancel();
            Ensure(!first.IsCompleted, "Cancellation abandoned the native callback.");
            Ensure((await importer.ImportAsync(Target, true)).Status == WindowsRouteProxyImportStatus.Busy
                && calls == 1, "Native callback reentered while cancellation was pending.");
        }
        finally
        {
            release.Set();
        }
        Ensure((await first.WaitAsync(TimeSpan.FromSeconds(5))).Status == WindowsRouteProxyImportStatus.Canceled,
            "Canceled native result was retained.");
        Ensure((await importer.ImportAsync(Target, true)).Status == WindowsRouteProxyImportStatus.Ready
            && calls == 2, "Importer did not recover after cancellation.");
    }

    private static async Task ReaderFailureDoesNotLeakOrPoisonNextRun()
    {
        int count = 0;
        WindowsRouteProxyImporter importer = new(
            () => ++count == 1 ? throw new InvalidOperationException("secret-value " + Target.Host) : Manual,
            (_, _) => throw new InvalidOperationException());
        WindowsRouteProxyImportResult failure = await importer.ImportAsync(Target);
        Ensure(failure.Status == WindowsRouteProxyImportStatus.Failed
            && !failure.Message.Contains("secret-value", StringComparison.Ordinal), "Exception leaked.");
        Ensure((await importer.ImportAsync(Target)).Status == WindowsRouteProxyImportStatus.Ready,
            "Reader failure poisoned the busy gate.");
    }

    private static ResolvedProxyRoute Resolved(ProxyConfigurationSource source, string directives)
    {
        ProxySelection selection = ProxyDirectiveParser.SelectAutoProxyList(Target, directives);
        return new ResolvedProxyRoute(new ProxyRouteResolution(
            ProxyResolutionStatus.Success, selection.RouteKind, source,
            ProxyRouteExpectationEvaluator.Evaluate(NetworkPathKind.External, selection.RouteKind),
            selection.ProxyCandidateCount, selection.HasDirectFallback, selection.WasBypassed,
            false, true, selection.InvalidDirectiveCount, null, "Synthetic private message"), selection);
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class TestClock : TimeProvider
    {
        private long _ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _ticks;
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch.AddTicks(_ticks);
        public void Advance(TimeSpan time) => _ticks += time.Ticks;
    }
}
