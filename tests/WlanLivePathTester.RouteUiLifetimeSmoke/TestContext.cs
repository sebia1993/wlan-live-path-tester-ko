global using System.IO;
using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using WlanLivePathTester.App;
using WlanLivePathTester.Core.Models;
using WlanLivePathTester.Core.Operations;
using WlanLivePathTester.Core.Proxy;
using WlanLivePathTester.Core.Reporting;
using WlanLivePathTester.Core.Routing;
using WlanLivePathTester.Windows.Proxy;

namespace WlanLivePathTester.RouteUiLifetimeSmoke;

internal static class TestContext
{
    internal const string External = "https://external.example.invalid/test.bin";
    internal const string Secret = "https://private.example.invalid/credential";
    internal static readonly BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    internal static MainWindow Prepare(string tab = "경로 비교")
    {
        // Do not Show(): no native startup, Loaded/Activated or NetworkChange hookup.
        MainWindow window = new();
        FrameworkElement root = (FrameworkElement)window.Content;
        root.Measure(new Size(1400, 1000));
        root.Arrange(new Rect(0, 0, 1400, 1000));
        root.UpdateLayout();
        window.EnsureRouteComparisonTabV3();
        window.EnsureRouteProxyImportControls();
        window.EnsureRouteComparisonReportTabV2();
        window.EnsureWlanInterfaceCorrelationTab();
        Field<DeferredNetworkRefreshController>(window, "_networkAdapterRefreshController").Suspend();
        Field<TextBox>(window, "_routeComparisonInternalTargetV3").Text = "internal.example.invalid";
        Field<TextBox>(window, "_routeComparisonExternalTargetV3").Text = External;
        Field<TextBox>(window, "_routeComparisonProxyDirectiveV3").Text = "PROXY proxy.example.invalid:8080";
        Select(window, tab);
        return window;
    }
    internal static TabControl Tabs(MainWindow window) => Find<TabControl>((DependencyObject)window.Content)!
        ?? throw new InvalidOperationException("Missing production tab host.");
    internal static T? Find<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T match) return match;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            T? child = Find<T>(VisualTreeHelper.GetChild(root, i));
            if (child is not null) return child;
        }
        return null;
    }
    internal static void Select(MainWindow window, string header) =>
        Tabs(window).SelectedItem = Tabs(window).Items.OfType<TabItem>().Single(t => Equals(t.Header, header));
    internal static T Field<T>(MainWindow window, string name) =>
        (T)(typeof(MainWindow).GetField(name, Hidden)!.GetValue(window)
            ?? throw new InvalidOperationException("Missing field " + name));
    internal static object? Optional(MainWindow window, string name) => typeof(MainWindow).GetField(name, Hidden)!.GetValue(window);
    internal static void Set(MainWindow window, string name, object? value) => typeof(MainWindow).GetField(name, Hidden)!.SetValue(window, value);
    internal static void Click(MainWindow window, string name) => Field<Button>(window, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    internal static ApplicationOperationCoordinator Coordinator(MainWindow window) => Field<ApplicationOperationCoordinator>(window, "_applicationOperations");
    internal static TaskCompletionSource<T> Pending<T>() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal static async Task DrainAsync() => await Dispatcher.Yield(DispatcherPriority.ContextIdle);
    internal static async Task UntilAsync(Func<bool> predicate)
    {
        Stopwatch clock = Stopwatch.StartNew();
        while (!predicate())
        {
            if (clock.Elapsed > TimeSpan.FromSeconds(4)) throw new TimeoutException("UI condition not reached.");
            await Task.Delay(5);
        }
    }
    internal static InternalProxyRouteComparisonRunResult Run() => WlanLivePathTester.UnifiedReportSmoke.Fixtures.Run();
    internal static WindowsRouteProxyImportResult Imported(TimeProvider? clock = null)
    {
        ProxyDirectiveSourceSelectionResult selection = ProxyDirectiveSourceSelectionPolicy.Select(
            targetDecisionWasEvaluated: true, targetDecisionIsDirect: false,
            targetSpecificDirective: "PROXY proxy.example.invalid:8080",
            manualProxyConfigured: true, manualProxyDirective: "PROXY unused.example.invalid:3128");
        return new WindowsRouteProxyImportResult(WindowsRouteProxyImportStatus.Ready,
            ProxyConfigurationSource.Pac, new Uri(External), selection,
            true, false, false, clock ?? TimeProvider.System);
    }
    internal static void ArmImport(MainWindow window, WindowsRouteProxyImportResult result) => Set(window, "_importedRouteProxy", result);
    internal static string RouteText(MainWindow window) => Field<TextBox>(window, "_routeComparisonResultV3").Text;
    internal static string ImportText(MainWindow window) => Field<TextBlock>(window, "_importedRouteProxySummary").Text;
    internal static string ReportText(MainWindow window) => Field<TextBlock>(window, "_routeComparisonReportResultV2").Text;
    internal static void Idle(MainWindow window)
    {
        Ensure(!window.CurrentApplicationOperation.IsBusy, "Core lease must be released.");
        Ensure(Tabs(window).Items.OfType<TabItem>().All(t => t.IsEnabled), "Peer tabs must be restored.");
    }
    internal static void Ensure(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
    internal sealed class Clock : TimeProvider
    {
        internal long Ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => Ticks;
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch.AddTicks(Ticks);
    }
    internal sealed class Output : IDisposable
    {
        internal string DirectoryPath { get; } = Path.Combine(Path.GetTempPath(), "wlan-route-ui-" + Guid.NewGuid().ToString("N"));
        internal MainWindow.RouteReportSaveOutput Write(CancellationToken token = default)
        {
            var document = InternalProxyRouteComparisonRunReportWriter.CreateDocument(Run(), "synthetic-ui");
            var export = InternalProxyRouteComparisonRunReportWriter.WriteAll(document, DirectoryPath, "SyntheticRoute", token);
            return new MainWindow.RouteReportSaveOutput(document, export);
        }
        public void Dispose()
        {
            if (Directory.Exists(DirectoryPath)) Directory.Delete(DirectoryPath, true);
        }
    }
}
