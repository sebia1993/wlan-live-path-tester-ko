using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using WlanLivePathTester.App;
using WlanLivePathTester.Core.Operations;
using WlanLivePathTester.Core.Reporting;
using WlanLivePathTester.Core.Routing;
using static WlanLivePathTester.UnifiedReportSmoke.Fixtures;

namespace WlanLivePathTester.UnifiedReportSmoke;

internal static class UnifiedReportUiTests
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    internal static IEnumerable<(string Name, Func<Task> Test)> Cases =>
    [
        ("real report path captures one safe route snapshot", SnapshotAndSuccessAsync),
        ("report exclusion and peer controls", ExclusionAsync),
        ("failed save retains previous report and can retry", FailureAndRetryAsync),
        ("successful save drains before window close", DeferredCloseAsync),
        ("failed close-time save keeps window for review", FailedCloseReviewAsync),
        ("capture failure releases lease without writing", CaptureFailureAsync),
        ("one tab registration and closed-window rejection", ClosedAndRegistrationAsync),
        ("worker-thread report start rejected", DispatcherOwnershipAsync)
    ];

    private static async Task SnapshotAndSuccessAsync()
    {
        MainWindow window = Prepare();
        try
        {
            var run = Run();
            SetField(window, "_latestRouteComparisonRunV3", run);
            LocalDiagnosticReport? received = null;
            bool result = await window.RunLocalReportSaveAsync(() =>
            {
                Ensure(window.CurrentApplicationOperation.Kind == ApplicationOperationKind.DiagnosticReportSave
                    && !window.CurrentApplicationOperation.SupportsCancellation,
                    "The real report path must acquire its fixed noncancelable save lease before capture.");
                Ensure(!Field<Button>(window, "_generateReportButton").IsEnabled
                    && !Field<Button>(window, "_openReportFolderButton").IsEnabled,
                    "Report controls must be disabled before capture/write.");
                SetField(window, "_latestRouteComparisonRunV3", run with
                {
                    CompletedAt = CapturedAt.AddHours(5), Status = InternalProxyRouteComparisonRunStatus.Failed
                });
                return Report();
            }, report =>
            {
                received = report;
                return Task.FromResult(Export());
            });
            Ensure(result && received?.InternalProxyRouteComparison?.CompletedAt == CapturedAt
                && received.InternalProxyRouteComparison.RunStatus == "Completed",
                "The save must attach the route run captured before invoking its report capture delegate.");
            Ensure(received!.SchemaVersion == "1.2" && !LocalReportWriter.RenderJson(received).Contains(SecretHost, StringComparison.Ordinal),
                "The production UI must pass a safe schema 1.2 document to the existing writer.");
            Ensure(Field<string>(window, "_lastReportHtmlPath") == "synthetic-report.html"
                && Field<Button>(window, "_openLatestReportButton").IsEnabled,
                "Only successful saves should update the latest report path.");
            AssertIdle(window);
        }
        finally { window.Close(); }
    }

    private static async Task ExclusionAsync()
    {
        MainWindow window = Prepare();
        var done = Pending();
        int captures = 0;
        int writes = 0;
        try
        {
            ApplicationOperationCoordinator coordinator = Field<ApplicationOperationCoordinator>(window, "_applicationOperations");
            using (ApplicationOperationLease competing = coordinator.TryBegin(ApplicationOperationKind.RouteComparison).Lease!)
            {
                bool blocked = await window.RunLocalReportSaveAsync(
                    () => { captures++; return Report(); },
                    _ => { writes++; return Task.FromResult(Export()); });
                Ensure(!blocked && captures == 0 && writes == 0,
                    "A competing route operation must block capture and writer before either is called.");
            }
            Task<bool> first = window.RunLocalReportSaveAsync(
                () => { captures++; return Report(); }, _ => { writes++; return done.Task; });
            TabControl tabs = Tabs(window);
            Ensure(tabs.Items.OfType<TabItem>().Where(tab => !ReferenceEquals(tab, tabs.SelectedItem)).All(tab => !tab.IsEnabled),
                "Peer tabs must stay locked for the entire save.");
            bool duplicate = await window.RunLocalReportSaveAsync(
                () => { captures++; return Report(); }, _ => { writes++; return Task.FromResult(Export()); });
            int measurementCalls = 0;
            Task measurement = (Task)typeof(MainWindow).GetMethod("RunMeasurementOperationAsync", PrivateInstance)!
                .Invoke(window, new object[]
                {
                    new Func<CancellationToken, Task>(_ => { measurementCalls++; return Task.CompletedTask; }), "synthetic measurement"
                })!;
            await measurement;
            Ensure(!duplicate && captures == 1 && writes == 1 && measurementCalls == 0,
                "Neither duplicate save nor a single measurement may bypass the shared lease.");
            string before = Text(window);
            Field<Button>(window, "_openLatestReportButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Ensure(Text(window) == before, "Programmatic open events must not execute while a report is being saved.");
            done.SetResult(Export());
            Ensure(await first, "The original save should complete normally.");
            AssertIdle(window);
        }
        finally { done.TrySetResult(Export()); window.Close(); }
    }

    private static async Task FailureAndRetryAsync()
    {
        MainWindow window = Prepare();
        try
        {
            Ensure(await window.RunLocalReportSaveAsync(Report, _ => Task.FromResult(Export("previous"))), "Seed save should succeed.");
            bool failed = await window.RunLocalReportSaveAsync(Report,
                _ => throw new IOException(SecretInternal));
            Ensure(!failed && Field<string>(window, "_lastReportHtmlPath") == "previous.html"
                && Field<Button>(window, "_openLatestReportButton").IsEnabled,
                "Failed writes must not replace the previous successful report paths.");
            Ensure(!Text(window).Contains(SecretInternal, StringComparison.Ordinal)
                && Text(window).Contains(nameof(IOException), StringComparison.Ordinal),
                "Write failure display must contain the error type, not its sensitive message.");
            AssertIdle(window);
            Ensure(await window.RunLocalReportSaveAsync(Report, _ => Task.FromResult(Export("retry")))
                && Field<string>(window, "_lastReportHtmlPath") == "retry.html", "A failed save must allow a subsequent retry.");
        }
        finally { window.Close(); }
    }

    private static async Task DeferredCloseAsync()
    {
        MainWindow window = Prepare();
        var done = Pending();
        TaskCompletionSource closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        window.Closed += (_, _) => closed.TrySetResult();
        Task<bool> save = window.RunLocalReportSaveAsync(Report, _ => done.Task);
        try
        {
            window.Close();
            window.Close();
            Ensure(!closed.Task.IsCompleted && !save.IsCompleted
                && window.CurrentApplicationOperation.IsBusy
                && window.CurrentApplicationOperation.ShutdownRequested
                && !window.CurrentApplicationOperation.SupportsCancellation,
                "Close must wait for the actual legacy writer; it cannot pretend to cancel a live write.");
            done.SetResult(Export());
            Ensure(await save, "Close-time successful output must still be recorded as success.");
            await closed.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Ensure(!window.CurrentApplicationOperation.IsBusy, "Final Close must follow report UI cleanup and lease release.");
        }
        finally { done.TrySetResult(Export()); if (!closed.Task.IsCompleted) window.Close(); }
    }

    private static async Task FailedCloseReviewAsync()
    {
        MainWindow window = Prepare();
        var done = Pending();
        bool closed = false;
        window.Closed += (_, _) => closed = true;
        Task<bool> save = window.RunLocalReportSaveAsync(Report, _ => done.Task);
        try
        {
            window.Close();
            Ensure(!closed && window.CurrentApplicationOperation.ShutdownRequested, "The first close must be deferred.");
            done.SetException(new IOException(SecretInternal));
            Ensure(!await save, "The failed writer must not be reported as a successful save.");
            await UntilAsync(() => !window.CurrentApplicationOperation.ShutdownRequested);
            Ensure(!closed && Text(window).Contains("창을 유지", StringComparison.Ordinal)
                && !Text(window).Contains(SecretInternal, StringComparison.Ordinal),
                "A close-time error must remain visible without disclosing its raw path/message.");
            AssertIdle(window);
            window.Close();
            Ensure(closed, "After reviewing the error, the user must be able to close explicitly.");
        }
        finally { done.TrySetResult(Export()); if (!closed) window.Close(); }
    }

    private static async Task CaptureFailureAsync()
    {
        MainWindow window = Prepare();
        int writes = 0;
        try
        {
            bool result = await window.RunLocalReportSaveAsync(
                () => throw new InvalidOperationException(SecretDescription),
                _ => { writes++; return Task.FromResult(Export()); });
            Ensure(!result && writes == 0 && !Text(window).Contains(SecretDescription, StringComparison.Ordinal),
                "Capture failure must never start the writer or expose raw error contents.");
            AssertIdle(window);
        }
        finally { window.Close(); }
    }

    private static async Task ClosedAndRegistrationAsync()
    {
        MainWindow window = Prepare();
        window.EnsureLocalReportTab();
        window.EnsureLocalReportTab();
        Ensure(Tabs(window).Items.OfType<TabItem>().Count(tab => Equals(tab.Header, "로컬 보고서")) == 1,
            "Content-rendered and explicit setup must not create duplicate report tabs.");
        window.Close();
        int calls = 0;
        bool result = await window.RunLocalReportSaveAsync(
            () => { calls++; return Report(); }, _ => { calls++; return Task.FromResult(Export()); });
        Ensure(!result && calls == 0, "A closed window must not capture or save a report.");
    }

    private static async Task DispatcherOwnershipAsync()
    {
        MainWindow window = Prepare();
        try
        {
            bool rejected = await Task.Run(async () =>
            {
                try { await window.RunLocalReportSaveAsync(Report, _ => Task.FromResult(Export())); }
                catch (InvalidOperationException) { return true; }
                return false;
            });
            Ensure(rejected && !window.CurrentApplicationOperation.IsBusy,
                "Cross-thread access must be rejected before acquiring an orphan lease.");
        }
        finally { window.Close(); }
    }

    private static MainWindow Prepare()
    {
        // Do not Show(): Loaded/Activated native WLAN/network readers stay uncalled.
        MainWindow window = new();
        FrameworkElement root = (FrameworkElement)window.Content;
        root.Measure(new Size(1400, 1000));
        root.Arrange(new Rect(0, 0, 1400, 1000));
        root.UpdateLayout();
        window.EnsureLocalReportTab();
        Tabs(window).SelectedItem = Tabs(window).Items.OfType<TabItem>().Single(tab => Equals(tab.Header, "로컬 보고서"));
        return window;
    }
    private static TabControl Tabs(MainWindow window) => Find<TabControl>((DependencyObject)window.Content)
        ?? throw new InvalidOperationException("Missing initialized report tab host.");
    private static T? Find<T>(DependencyObject item) where T : DependencyObject
    {
        if (item is T result) return result;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(item); i++)
        {
            T? child = Find<T>(VisualTreeHelper.GetChild(item, i));
            if (child is not null) return child;
        }
        return null;
    }
    private static T Field<T>(MainWindow window, string name) =>
        (T)(typeof(MainWindow).GetField(name, PrivateInstance)!.GetValue(window)
            ?? throw new InvalidOperationException($"Missing production field {name}."));
    private static void SetField(MainWindow window, string name, object value) =>
        typeof(MainWindow).GetField(name, PrivateInstance)!.SetValue(window, value);
    private static string Text(MainWindow window) => Field<TextBlock>(window, "_reportResultText").Text;
    private static TaskCompletionSource<LocalReportExportResult> Pending() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static void AssertIdle(MainWindow window) => Ensure(!window.CurrentApplicationOperation.IsBusy
        && Field<Button>(window, "_generateReportButton").IsEnabled,
        "Report controls must be restored and its global lease released.");
    private static async Task UntilAsync(Func<bool> condition)
    {
        Stopwatch elapsed = Stopwatch.StartNew();
        while (!condition())
        {
            if (elapsed.Elapsed > TimeSpan.FromSeconds(3)) throw new TimeoutException("Shutdown UI did not settle.");
            await Task.Delay(5);
        }
        await Dispatcher.Yield(DispatcherPriority.Background);
    }
}
