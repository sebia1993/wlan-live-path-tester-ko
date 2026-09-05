using System.Text.Json;
using System.Text.RegularExpressions;
using WlanLivePathTester.Core.Proxy;

namespace WlanLivePathTester.ProxyBoundarySmoke;

internal static class Program
{
    private const string Proxy = "PROXY boundary.example.invalid:8080; DIRECT";
    private const string Payload = "synthetic-private-analysis-payload";
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(5);

    private static async Task<int> Main()
    {
        (string Name, Func<Task> Run)[] tests =
        [
            ("raw control-character matrix", () => Sync(ControlCharacterMatrix)),
            ("raw length boundary and ordinary spaces", () => Sync(LengthAndSpaceBoundary)),
            ("authoritative source validation", () => Sync(SourceValidation)),
            ("unused source isolation", () => Sync(UnusedSourceIsolation)),
            ("DIRECT empty-input distinction", () => Sync(DirectEmptyInputDistinction)),
            ("blocked DIRECT and missing sources call no analyzer", NoAnalyzerForNonProxyPlans),
            ("pre-canceled execution calls no analyzer", PreCanceledExecution),
            ("late result awaits callback and stays canceled", LateResultAfterCancellation),
            ("value-type and null late results stay canceled", ValueAndNullLateResults),
            ("completed result is not retroactively canceled", CompletedResultIsStable),
            ("exception and result privacy", ExceptionAndResultPrivacy),
            ("WPF raw-input source contract (not interaction test)", () => Sync(UiSourceContract))
        ];
        int failures = 0;
        foreach ((string name, Func<Task> run) in tests)
        {
            try
            {
                await run().WaitAsync(TestTimeout);
                Console.WriteLine($"PASS {name}");
            }
            catch (Exception exception)
            {
                failures++;
                Console.Error.WriteLine($"FAIL {name}: {exception}");
            }
        }
        Console.WriteLine($"Proxy boundary smoke: {tests.Length} groups, {failures} failures; no network requests.");
        return failures == 0 ? 0 : 1;
    }

    private static Task Sync(Action test)
    {
        test();
        return Task.CompletedTask;
    }

    private static void ControlCharacterMatrix()
    {
        char[] controls = Enumerable.Range(char.MinValue, char.MaxValue + 1)
            .Select(value => (char)value).Where(char.IsControl).ToArray();
        Ensure(controls.Length > 0, "Control-character fixture must not be empty.");
        foreach (char control in controls)
        {
            foreach (string input in new[]
                     { control + Proxy, Proxy + control, control.ToString(), "DIRECT" + control })
            {
                AssertInvalidRaw(input, "CONTROL_CHARACTER");
            }
        }
        Console.WriteLine($"Checked {controls.Length * 4} raw control-character inputs.");
    }

    private static void LengthAndSpaceBoundary()
    {
        int limit = ProxyRouteDirectiveParser.MaximumInputLength;
        string exact = Proxy.PadLeft(limit);
        Ensure(ProxyRouteDirectiveParser.Parse(exact).HasProxyEndpoint,
            "A valid raw input exactly at the length limit must remain accepted.");
        foreach (string input in new[] { exact + " ", " " + exact, new string(' ', limit + 1) })
            AssertInvalidRaw(input, "INPUT_TOO_LONG");
        Ensure(ProxyRouteDirectiveParser.Parse("  " + Proxy + "  ").Status
            == ProxyDirectiveParseStatus.Success, "Ordinary edge spaces must remain supported.");
        foreach (string? input in new string?[] { null, string.Empty, "   " })
            Ensure(ProxyRouteDirectiveParser.Parse(input).Status == ProxyDirectiveParseStatus.Empty,
                "Genuinely empty or ordinary-space input must remain Empty.");
    }

    private static void SourceValidation()
    {
        string[] invalid =
        [
            Proxy + "\t", "\n" + Proxy, "\r\n", "DIRECT\u0085",
            Proxy.PadLeft(ProxyRouteDirectiveParser.MaximumInputLength + 1),
            new string(' ', ProxyRouteDirectiveParser.MaximumInputLength + 1)
        ];
        foreach (string raw in invalid)
        {
            foreach (bool direct in new[] { false, true })
            {
                ProxyDirectiveSourceSelectionResult target = ProxyDirectiveSourceSelectionPolicy.Select(
                    true, direct, raw, true, Proxy);
                AssertInvalidSelection(target, ProxyDirectiveSourceKind.TargetSpecificAutoProxy);
                Ensure(target.Code == ProxyDirectiveSourceSelectionCode.TargetDecisionInvalid,
                    "An invalid target decision must not fall back to the valid manual proxy.");
            }
            AssertInvalidSelection(Manual(raw), ProxyDirectiveSourceKind.ManualProxyConfiguration);
            ProxyDirectiveSourceSnapshot snapshot = new(
                DateTimeOffset.UnixEpoch,
                ProxyDirectiveSourceReadStatus.Success, false, raw,
                ProxyDirectiveSourceReadStatus.Success, true, Proxy, true, true);
            AssertInvalidSelection(ProxyDirectiveSourceSnapshotSelectionPolicy.Select(snapshot),
                ProxyDirectiveSourceKind.TargetSpecificAutoProxy);
        }
    }

    private static void UnusedSourceIsolation()
    {
        string invalid = Proxy + "\r\n";
        var target = ProxyDirectiveSourceSelectionPolicy.Select(true, false, Proxy, true, invalid);
        Ensure(target.Status == ProxyDirectiveSourceSelectionStatus.Selected
            && target.SourceKind == ProxyDirectiveSourceKind.TargetSpecificAutoProxy,
            "An unused invalid manual setting must not reject a valid authoritative target decision.");
        var manual = ProxyDirectiveSourceSelectionPolicy.Select(false, false, invalid, true, Proxy);
        Ensure(manual.Status == ProxyDirectiveSourceSelectionStatus.Selected
            && manual.SourceKind == ProxyDirectiveSourceKind.ManualProxyConfiguration,
            "A target value never evaluated must not override the selected manual source.");
        var absent = ProxyDirectiveSourceSelectionPolicy.Select(false, false, invalid, false, invalid);
        Ensure(absent.Status == ProxyDirectiveSourceSelectionStatus.Unavailable,
            "Unavailable sources must not become an inferred DIRECT decision.");
    }

    private static void DirectEmptyInputDistinction()
    {
        foreach (string? raw in new string?[] { null, string.Empty, "   " })
        {
            var direct = ProxyDirectiveSourceSelectionPolicy.Select(true, true, raw, true, Proxy);
            Ensure(direct.Status == ProxyDirectiveSourceSelectionStatus.Direct
                && direct.SelectedDirectiveText == "DIRECT", "An explicit DIRECT with no text is valid.");
        }
        foreach (string raw in new[] { "\t", "\r\n", "DIRECT\n" })
            AssertInvalidSelection(ProxyDirectiveSourceSelectionPolicy.Select(true, true, raw, true, Proxy),
                ProxyDirectiveSourceKind.TargetSpecificAutoProxy);
        var scoped = Manual("  ftp=DIRECT  ");
        Ensure(scoped.Status == ProxyDirectiveSourceSelectionStatus.Direct
            && scoped.SelectedDirectiveText == "ftp=DIRECT"
            && scoped.ParseResult!.Directives.Single().Scope == "ftp",
            "Scoped DIRECT must not expand into a global DIRECT policy.");
    }

    private static async Task NoAnalyzerForNonProxyPlans()
    {
        var cases = new[]
        {
            (Manual(Proxy + "\n"), ProxyDirectiveRouteAnalysisExecutionStatus.Blocked),
            (ProxyDirectiveSourceSelectionPolicy.Select(true, true, null, true, Proxy),
                ProxyDirectiveRouteAnalysisExecutionStatus.DirectOnly),
            (ProxyDirectiveSourceSelectionPolicy.Select(false, false, null, false, null),
                ProxyDirectiveRouteAnalysisExecutionStatus.Unavailable)
        };
        int calls = 0;
        foreach (var (selection, expected) in cases)
        {
            var result = await ProxyDirectiveRouteAnalysisExecutor.ExecuteAsync(selection, (_, _) =>
            {
                calls++;
                return Task.FromResult(Payload);
            });
            Ensure(result.Status == expected && !result.HasCompletedAnalysis && result.Analysis is null,
                "A non-proxy plan must retain its safe status with no analysis payload.");
        }
        Ensure(calls == 0, "Blocked, DIRECT and unavailable plans must not call the analyzer.");
    }

    private static async Task PreCanceledExecution()
    {
        using CancellationTokenSource source = new();
        source.Cancel();
        int calls = 0;
        var result = await ProxyDirectiveRouteAnalysisExecutor.ExecuteAsync(Manual(Proxy), (_, _) =>
        {
            calls++;
            return Task.FromResult(Payload);
        }, source.Token);
        AssertCanceled(result);
        Ensure(calls == 0, "Pre-canceled execution must not call the analyzer.");
    }

    private static async Task LateResultAfterCancellation()
    {
        using CancellationTokenSource source = new();
        TaskCompletionSource<string> gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int calls = 0;
        var pending = ProxyDirectiveRouteAnalysisExecutor.ExecuteAsync(Manual(Proxy), (text, token) =>
        {
            calls++;
            Ensure(text == Proxy && token == source.Token, "The original selected input/token must be forwarded.");
            return gate.Task;
        }, source.Token);
        try
        {
            Ensure(calls == 1, "The allowed analyzer must execute exactly once.");
            source.Cancel();
            Ensure(!pending.IsCompleted,
                "Cancel must not pretend that an outstanding native/adapter callback already completed.");
        }
        finally { gate.TrySetResult(Payload); }
        var result = await pending.WaitAsync(TestTimeout);
        AssertCanceled(result);
        Ensure(result.Analysis is null, "A canceled late result must not publish its payload.");
        Ensure(!JsonSerializer.Serialize(result).Contains(Payload, StringComparison.Ordinal),
            "Canceled result JSON must not expose the late payload.");
    }

    private static async Task ValueAndNullLateResults()
    {
        using CancellationTokenSource valueSource = new();
        var value = await ProxyDirectiveRouteAnalysisExecutor.ExecuteAsync(Manual(Proxy), (_, _) =>
        {
            valueSource.Cancel();
            return Task.FromResult(42);
        }, valueSource.Token);
        AssertCanceled(value);
        Ensure(value.Analysis == default, "A canceled value-type result must not publish 42.");
        using CancellationTokenSource nullSource = new();
        var empty = await ProxyDirectiveRouteAnalysisExecutor.ExecuteAsync<string>(Manual(Proxy), (_, _) =>
        {
            nullSource.Cancel();
            return Task.FromResult<string>(null!);
        }, nullSource.Token);
        AssertCanceled(empty);
    }

    private static async Task CompletedResultIsStable()
    {
        using CancellationTokenSource source = new();
        var result = await ProxyDirectiveRouteAnalysisExecutor.ExecuteAsync(Manual(Proxy),
            (_, _) => Task.FromResult(42), source.Token);
        Ensure(result.Status == ProxyDirectiveRouteAnalysisExecutionStatus.Completed
            && result.HasCompletedAnalysis && result.Analysis == 42, "Uncanceled success must remain successful.");
        source.Cancel();
        Ensure(result.Status == ProxyDirectiveRouteAnalysisExecutionStatus.Completed,
            "Cancellation after publication must not mutate an immutable prior result.");
    }

    private static async Task ExceptionAndResultPrivacy()
    {
        var result = await ProxyDirectiveRouteAnalysisExecutor.ExecuteAsync<string>(Manual(Proxy),
            (_, _) => throw new InvalidOperationException(Payload));
        Ensure(result.Status == ProxyDirectiveRouteAnalysisExecutionStatus.Failed,
            "A genuine callback fault must remain Failed.");
        string safe = JsonSerializer.Serialize(result) + result;
        Ensure(!safe.Contains(Payload, StringComparison.Ordinal)
            && !safe.Contains("boundary.example.invalid", StringComparison.Ordinal),
            "Exception details and raw host must not appear in safe output.");
        var invalid = Manual("PROXY user:synthetic-secret@private.example.invalid:8080\n");
        string invalidSafe = JsonSerializer.Serialize(invalid) + invalid;
        Ensure(!invalidSafe.Contains("synthetic-secret", StringComparison.Ordinal)
            && !invalidSafe.Contains("private.example.invalid", StringComparison.Ordinal),
            "Rejected raw input must not be reflected in serialized diagnostics.");
    }

    private static void UiSourceContract()
    {
        DirectoryInfo? root = new(Directory.GetCurrentDirectory());
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "WlanLivePathTester.sln")))
            root = root.Parent;
        Ensure(root is not null, "Run this repository source check from the repository or a child directory.");
        string source = File.ReadAllText(Path.Combine(root!.FullName,
            "src", "WlanLivePathTester.App", "MainWindow.RouteComparisonV3.cs"));
        const string pattern = @"string\s+proxyDirective\s*=\s*_routeComparisonProxyDirectiveV3\?\.Text\s*\?\?\s*string\.Empty\s*;";
        Ensure(Regex.IsMatch(source, pattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)),
            "The WPF entry must pass raw proxy text without Trim or other sanitization.");
    }

    private static ProxyDirectiveSourceSelectionResult Manual(string? raw) =>
        ProxyDirectiveSourceSelectionPolicy.Select(false, false, null, true, raw);

    private static void AssertInvalidRaw(string raw, string code)
    {
        var parsed = ProxyRouteDirectiveParser.Parse(raw);
        Ensure(parsed.Status == ProxyDirectiveParseStatus.InvalidInput
            && parsed.Directives.Count == 0
            && parsed.Issues.Any(issue => issue.Code == code),
            "Raw input must be rejected with the expected fixed issue code and no directives.");
    }

    private static void AssertInvalidSelection(ProxyDirectiveSourceSelectionResult result,
        ProxyDirectiveSourceKind kind) =>
        Ensure(result.Status == ProxyDirectiveSourceSelectionStatus.Invalid
            && result.SourceKind == kind && result.SelectedDirectiveText is null
            && !result.HasUsableSelection, "Invalid source must fail closed without changing provenance.");

    private static void AssertCanceled<T>(ProxyDirectiveRouteAnalysisExecutionResult<T> result) =>
        Ensure(result.Status == ProxyDirectiveRouteAnalysisExecutionStatus.Canceled
            && !result.HasCompletedAnalysis, "Cancellation must win over a late successful return.");

    private static void Ensure(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
