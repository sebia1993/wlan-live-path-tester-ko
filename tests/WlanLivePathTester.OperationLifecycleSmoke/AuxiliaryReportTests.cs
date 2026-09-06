using System.Windows;
using System.Windows.Controls;
using WlanLivePathTester.App;
using WlanLivePathTester.Core.Operations;
using static WlanLivePathTester.OperationLifecycleSmoke.SmokeContext;

namespace WlanLivePathTester.OperationLifecycleSmoke;

internal static class AuxiliaryReportTests
{
    internal static readonly (string Name, Func<Task> Run)[] Cases =
    [
        ("five report types save through the shared lease and actual button", AllReportTypesAsync),
        ("report save rejects reentry readers and file opening", SaveExclusionAsync),
        ("all report failures retain previous export and support retry", FailureAndRetryAsync),
        ("five report types wait for real write before closing", CloseSuccessAsync),
        ("five close-time report failures retain the window for review", CloseFailureAsync),
        ("missing report data preserves the previous successful report", NoDataAsync),
        ("late report tabs restore state and registration is idempotent", LateTabsAsync),
        ("report callbacks detach on close and reject worker starts", CloseAndThreadAsync),
        ("source wiring keeps reads and writers behind common entry points", SourceWiringAsync)
    ];

    private static async Task AllReportTypesAsync()
    {
        MainWindow window = Prepare();
        try
        {
            Ensure(window.AuxiliaryReportSections.Count == 5, "Exactly the five existing auxiliary report types must be registered.");
            foreach (AuxiliaryReportSection section in window.AuxiliaryReportSections.Values)
            {
                Tabs(window).SelectedItem = section.Tab;
                int calls = 0;
                section.CaptureAndSave = () =>
                {
                    calls++;
                    Ensure(window.CurrentApplicationOperation.Kind == ApplicationOperationKind.DiagnosticReportSave
                        && !window.CurrentApplicationOperation.SupportsCancellation,
                        "Legacy writers must acquire the shared noncancelable save lease before capture.");
                    Ensure(!section.GenerateButton.IsEnabled && !section.FolderButton.IsEnabled && !section.HtmlButton.IsEnabled,
                        "Save controls must be disabled before capture starts.");
                    return Task.FromResult<AuxiliaryReportExport?>(Export(section.Kind.ToString()));
                };
                section.GenerateButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await UntilAsync(() => !window.CurrentApplicationOperation.IsBusy);
                Ensure(calls == 1 && section.LatestExport is not null && section.GenerateButton.IsEnabled
                    && section.FolderButton.IsEnabled && section.HtmlButton.IsEnabled,
                    "Actual click must save once and enable only committed report paths.");
                Ensure(!section.ResultText.Text.Contains(section.LatestExport!.OutputDirectory, StringComparison.Ordinal),
                    "Successful output must not display the full user directory.");
            }
        }
        finally { window.Close(); }
    }

    private static async Task SaveExclusionAsync()
    {
        MainWindow window = Prepare();
        AuxiliaryReportSection section = window.AuxiliaryReportSections[AuxiliaryReportKind.RouteEvidence];
        Tabs(window).SelectedItem = section.Tab;
        TaskCompletionSource<AuxiliaryReportExport?> done = Pending<AuxiliaryReportExport?>();
        int calls = 0;
        try
        {
            section.LatestExport = Export("previous");
            section.CaptureAndSave = () => { calls++; return done.Task; };
            Task<bool> save = section.SaveAsync();
            string status = section.ResultText.Text;
            section.FolderButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            section.HtmlButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Ensure(section.ResultText.Text == status, "Open handlers must not inspect or open files during save.");
            Ensure(!await section.SaveAsync() && calls == 1, "Repeated save must not enter the writer twice.");
            foreach (ApplicationOperationKind kind in DiagnosticKinds)
                Ensure(!await Run(window, kind), "Diagnostic collection must wait for report writing.");
            foreach (AuxiliaryReportSection other in window.AuxiliaryReportSections.Values.Where(s => !ReferenceEquals(s, section)))
                Ensure(!await other.SaveAsync(), "A second report type must share the same exclusion.");
            Ensure(window.CurrentApplicationOperation.IsBusy && !save.IsCompleted, "Rejection must not release the original save.");
            done.SetResult(Export("new"));
            Ensure(await save && section.LatestExport?.JsonPath == "new.json", "Only successful export may replace the old paths.");
        }
        finally { done.TrySetResult(Export("cleanup")); window.Close(); }
    }

    private static async Task FailureAndRetryAsync()
    {
        MainWindow window = Prepare();
        const string secret = @"C:\Users\private-name\secret-report.json";
        try
        {
            foreach (AuxiliaryReportSection section in window.AuxiliaryReportSections.Values)
            {
                Tabs(window).SelectedItem = section.Tab;
                AuxiliaryReportExport previous = Export("previous");
                section.LatestExport = previous;
                section.CaptureAndSave = () => throw new IOException(secret);
                Ensure(!await section.SaveAsync(), "Faulting capture/save must fail.");
                Ensure(ReferenceEquals(section.LatestExport, previous) && section.FolderButton.IsEnabled
                    && section.HtmlButton.IsEnabled && section.GenerateButton.IsEnabled
                    && !section.IsRunning && !window.CurrentApplicationOperation.IsBusy,
                    "Failure must preserve the previous report and restore controls.");
                Ensure(section.ResultText.Text.Contains(nameof(IOException), StringComparison.Ordinal)
                    && !section.ResultText.Text.Contains(secret, StringComparison.Ordinal), "Raw exception path must never be displayed.");
                section.CaptureAndSave = () => Task.FromResult<AuxiliaryReportExport?>(Export("retry"));
                Ensure(await section.SaveAsync() && section.LatestExport?.JsonPath == "retry.json",
                    "A failed attempt must not leave the section permanently busy.");
            }
        }
        finally { window.Close(); }
    }

    private static async Task CloseSuccessAsync()
    {
        foreach (AuxiliaryReportKind kind in Enum.GetValues<AuxiliaryReportKind>())
        {
            MainWindow window = Prepare();
            AuxiliaryReportSection section = window.AuxiliaryReportSections[kind];
            Tabs(window).SelectedItem = section.Tab;
            TaskCompletionSource<AuxiliaryReportExport?> done = Pending<AuxiliaryReportExport?>();
            TaskCompletionSource<bool> closed = Pending<bool>();
            window.Closed += (_, _) => closed.TrySetResult(true);
            section.CaptureAndSave = () => done.Task;
            Task<bool> save = section.SaveAsync();
            try
            {
                window.Close();
                Ensure(!closed.Task.IsCompleted && !save.IsCompleted && window.CurrentApplicationOperation.IsBusy,
                    "Window must remain open until the actual write returns.");
                Ensure(window.CurrentApplicationOperation.ShutdownRequested
                    && !window.CurrentApplicationOperation.SupportsCancellation,
                    "Shutdown must not falsely label a legacy file write as cancelable.");
                done.SetResult(Export());
                Ensure(await save, "Successful close-time write must stay successful.");
                await closed.Task.WaitAsync(TimeSpan.FromSeconds(3));
                Ensure(section.IsDisposed && !window.CurrentApplicationOperation.IsBusy,
                    "Closed must follow writer completion and UI lease cleanup.");
            }
            finally { done.TrySetResult(Export("cleanup")); if (!closed.Task.IsCompleted) window.Close(); }
        }
    }

    private static async Task CloseFailureAsync()
    {
        foreach (AuxiliaryReportKind kind in Enum.GetValues<AuxiliaryReportKind>())
        {
            MainWindow window = Prepare();
            AuxiliaryReportSection section = window.AuxiliaryReportSections[kind];
            Tabs(window).SelectedItem = section.Tab;
            TaskCompletionSource<AuxiliaryReportExport?> done = Pending<AuxiliaryReportExport?>();
            bool closed = false;
            window.Closed += (_, _) => closed = true;
            section.CaptureAndSave = () => done.Task;
            Task<bool> save = section.SaveAsync();
            try
            {
                window.Close();
                done.SetException(new IOException("secret-close-failure"));
                Ensure(!await save, "Failed write must not be reported as saved.");
                await UntilAsync(() => !window.CurrentApplicationOperation.ShutdownRequested);
                Ensure(!closed && !window.CurrentApplicationOperation.IsBusy
                    && section.ResultText.Text.Contains("창을 유지", StringComparison.Ordinal)
                    && !section.ResultText.Text.Contains("secret-close-failure", StringComparison.Ordinal),
                    "Close-time error must remain visible and reopen the operation gate.");
                window.Close();
                Ensure(closed, "After review, a subsequent user close must not be vetoed forever.");
            }
            finally { done.TrySetResult(null); if (!closed) window.Close(); }
        }
    }

    private static async Task NoDataAsync()
    {
        MainWindow window = Prepare();
        try
        {
            foreach (AuxiliaryReportSection section in window.AuxiliaryReportSections.Values)
            {
                AuxiliaryReportExport previous = Export("previous");
                section.LatestExport = previous;
                Tabs(window).SelectedItem = section.Tab;
                section.CaptureAndSave = () => Task.FromResult<AuxiliaryReportExport?>(null);
                Ensure(!await section.SaveAsync() && ReferenceEquals(section.LatestExport, previous)
                    && section.ResultText.Text == section.NoDataMessage && !window.CurrentApplicationOperation.IsBusy,
                    "No-data attempt must not erase the last successful export or claim a new save.");
            }
        }
        finally { window.Close(); }
    }

    private static async Task LateTabsAsync()
    {
        MainWindow window = Prepare();
        TaskCompletionSource<LocalDiagnosticText> done = Pending<LocalDiagnosticText>();
        window.CollectRouteEvidence = (_, _, _) => done.Task;
        try
        {
            Task<bool> read = window.RunRouteEvidenceAsync();
            TabItem late = new() { Header = "late" };
            Tabs(window).Items.Add(late);
            Ensure(!late.IsEnabled, "Late-added tabs must be locked by the same UI lease.");
            int count = Tabs(window).Items.Count;
            window.EnsureRouteEvidenceTab();
            window.EnsureNetworkEnvironmentTab();
            window.EnsureNetworkAdapterDiagnosticsTab();
            window.EnsureRouteEvidenceReportTab();
            window.EnsureRepeatedMeasurementReportTab();
            window.EnsureBrowserObservationSessionReportTab();
            window.EnsureNetworkAdapterReportTab();
            window.EnsureNetworkEnvironmentReportTab();
            Ensure(Tabs(window).Items.Count == count, "Repeated registration must add no tabs or handlers.");
            done.SetResult(new LocalDiagnosticText("complete"));
            await read;
            Ensure(late.IsEnabled && ReferenceEquals(late.ReadLocalValue(UIElement.IsEnabledProperty), DependencyProperty.UnsetValue),
                "Late tab cleanup must restore inherited/default values, not a forced local Boolean.");
        }
        finally { done.TrySetResult(new LocalDiagnosticText("cleanup")); window.Close(); }
    }

    private static async Task CloseAndThreadAsync()
    {
        MainWindow window = Prepare();
        AuxiliaryReportSection section = window.AuxiliaryReportSections[AuxiliaryReportKind.RepeatedMeasurement];
        int calls = 0;
        section.CaptureAndSave = () => { calls++; return Task.FromResult<AuxiliaryReportExport?>(Export()); };
        bool rejected = await Task.Run(async () =>
        {
            try { await section.SaveAsync(); }
            catch (InvalidOperationException) { return true; }
            return false;
        });
        Ensure(rejected && calls == 0 && !window.CurrentApplicationOperation.IsBusy,
            "A worker must not acquire a report UI lease.");
        window.Close();
        section.GenerateButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Ensure(!await section.SaveAsync() && calls == 0 && section.IsDisposed,
            "Closed sections must detach their button callbacks and reject direct reentry.");
    }

    private static Task SourceWiringAsync()
    {
        DirectoryInfo? root = new(AppContext.BaseDirectory);
        while (root is not null && !Directory.Exists(Path.Combine(root.FullName, "src", "WlanLivePathTester.App"))) root = root.Parent;
        Ensure(root is not null, "Repository root is required for the source wiring contract.");
        string app = Path.Combine(root!.FullName, "src", "WlanLivePathTester.App");
        foreach (string name in new[]
        {
            "MainWindow.RouteEvidenceReport.cs", "MainWindow.RepeatedMeasurementReport.cs",
            "MainWindow.BrowserObservationSessionReport.cs", "MainWindow.NetworkAdapterReport.cs",
            "MainWindow.NetworkEnvironmentReport.cs"
        })
        {
            string text = File.ReadAllText(Path.Combine(app, name));
            Ensure(text.Contains("EnsureAuxiliaryReportTab(", StringComparison.Ordinal)
                && text.Contains("return await Task.Run(", StringComparison.Ordinal)
                && !text.Contains("Process.Start", StringComparison.Ordinal)
                && !text.Contains(".Click +=", StringComparison.Ordinal),
                "Each report must delegate UI lifetime/opening to the shared section and keep its existing writer off-dispatcher.");
        }
        foreach (string name in new[] { "MainWindow.RouteEvidence.cs", "MainWindow.NetworkEnvironment.cs", "MainWindow.NetworkAdapterDiagnostics.cs" })
        {
            string text = File.ReadAllText(Path.Combine(app, name));
            Ensure(text.Contains("RunLocalDiagnosticAsync(", StringComparison.Ordinal)
                && !text.Contains("exception.Message", StringComparison.Ordinal)
                && !text.Contains("_routeEvidenceCancellation", StringComparison.Ordinal),
                "Diagnostic entry points must use the common runner and not dispose an independent reader token on Closed.");
        }
        string monitor = File.ReadAllText(Path.Combine(app, "MainWindow.NetworkAdapterChangeMonitor.cs"));
        Ensure(monitor.Contains("_applicationOperations.StateChanged +=", StringComparison.Ordinal)
            && monitor.Contains("_applicationOperations.StateChanged -=", StringComparison.Ordinal)
            && !monitor.Contains("IsEnabledChanged", StringComparison.Ordinal)
            && !monitor.Contains("NativeWlanReader", StringComparison.Ordinal),
            "Automatic refresh must use actual global state, not unrelated button availability or direct native reads.");
        return Task.CompletedTask;
    }
}
