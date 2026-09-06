using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using WlanLivePathTester.App;
using WlanLivePathTester.Core.Adapters;
using WlanLivePathTester.Core.Observation;
using WlanLivePathTester.Core.Operations;

namespace WlanLivePathTester.OperationLifecycleSmoke;

internal static class SmokeContext
{
    internal static readonly ApplicationOperationKind[] DiagnosticKinds =
    [
        ApplicationOperationKind.RouteEvidence,
        ApplicationOperationKind.NetworkAdapterDiagnostics,
        ApplicationOperationKind.NetworkEnvironmentCapture
    ];
    private static readonly BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    internal static MainWindow Prepare()
    {
        // Never Show: no Loaded/Activated inventory, OS event subscription,
        // actual WLAN reader, DNS, PAC/WPAD or HTTP request is initiated here.
        MainWindow window = new();
        window.CollectNetworkAdapterDiagnostics = _ => Task.FromResult(Adapter("synthetic-adapter"));
        window.CollectNetworkEnvironment = _ => Task.FromResult(new LocalDiagnosticText("synthetic-environment"));
        window.CollectRouteEvidence = (_, _, _) => Task.FromResult(new LocalDiagnosticText("synthetic-route"));
        FrameworkElement root = (FrameworkElement)window.Content;
        root.Measure(new Size(1400, 1000));
        root.Arrange(new Rect(0, 0, 1400, 1000));
        root.UpdateLayout();
        Controller(window).Suspend();
        window.EnsureRouteEvidenceTab();
        window.EnsureNetworkEnvironmentTab();
        window.EnsureNetworkAdapterDiagnosticsTab();
        window.EnsureRouteEvidenceReportTab();
        window.EnsureRepeatedMeasurementReportTab();
        window.EnsureBrowserObservationSessionReportTab();
        window.EnsureNetworkAdapterReportTab();
        window.EnsureNetworkEnvironmentReportTab();
        foreach (AuxiliaryReportSection section in window.AuxiliaryReportSections.Values)
        {
            AuxiliaryReportKind kind = section.Kind;
            section.CaptureAndSave = () => Task.FromResult<AuxiliaryReportExport?>(Export(kind.ToString()));
        }
        Select(window, ApplicationOperationKind.RouteEvidence);
        return window;
    }

    internal static NetworkAdapterDiagnosticPresentation Adapter(string text) => new(
        text, WirelessAdapterSelectionStatus.Selected, "추가 경고 없음", false,
        "synthetic-inventory", "synthetic-memory-only-id");
    internal static AuxiliaryReportExport Export(string name = "synthetic") => new(
        Path.Combine(Path.GetTempPath(), "WlanLifecycleSynthetic", name),
        name + ".json", name + ".csv", name + ".html", name + "_SHA256SUMS.txt", "synthetic saved result");

    internal static void Select(MainWindow window, ApplicationOperationKind kind)
    {
        string header = kind switch
        {
            ApplicationOperationKind.RouteEvidence => "라우팅 근거",
            ApplicationOperationKind.NetworkAdapterDiagnostics => "어댑터 진단",
            ApplicationOperationKind.NetworkEnvironmentCapture => "인터페이스 환경",
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        Tabs(window).SelectedItem = Tabs(window).Items.OfType<TabItem>().Single(tab => Equals(tab.Header, header));
    }
    internal static Task<bool> Run(MainWindow window, ApplicationOperationKind kind) => kind switch
    {
        ApplicationOperationKind.RouteEvidence => window.RunRouteEvidenceAsync(),
        ApplicationOperationKind.NetworkAdapterDiagnostics => window.RunNetworkAdapterDiagnosticsAsync(),
        ApplicationOperationKind.NetworkEnvironmentCapture => window.RunNetworkEnvironmentAsync(),
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };
    internal static void SetReader(MainWindow window, ApplicationOperationKind kind, Func<CancellationToken, Task<LocalDiagnosticText>> read)
    {
        switch (kind)
        {
            case ApplicationOperationKind.RouteEvidence:
                window.CollectRouteEvidence = (_, _, token) => read(token);
                break;
            case ApplicationOperationKind.NetworkEnvironmentCapture:
                window.CollectNetworkEnvironment = read;
                break;
            case ApplicationOperationKind.NetworkAdapterDiagnostics:
                window.CollectNetworkAdapterDiagnostics = async token => Adapter((await read(token)).Text);
                break;
            default: throw new ArgumentOutOfRangeException(nameof(kind));
        }
    }
    internal static string Result(MainWindow window, ApplicationOperationKind kind) => kind switch
    {
        ApplicationOperationKind.RouteEvidence => Field<TextBlock>(window, "_routeEvidenceResultText").Text,
        ApplicationOperationKind.NetworkEnvironmentCapture => Field<TextBlock>(window, "_networkEnvironmentResultText").Text,
        ApplicationOperationKind.NetworkAdapterDiagnostics => Field<TextBlock>(window, "_networkAdapterSelectionText").Text,
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };
    internal static Button Start(MainWindow window, ApplicationOperationKind kind) => kind switch
    {
        ApplicationOperationKind.RouteEvidence => Field<Button>(window, "_analyzeRouteEvidenceButton"),
        ApplicationOperationKind.NetworkEnvironmentCapture => Field<Button>(window, "_readNetworkEnvironmentButton"),
        ApplicationOperationKind.NetworkAdapterDiagnostics => Field<Button>(window, "_refreshNetworkAdapterDiagnosticsButton"),
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };
    internal static DeferredNetworkRefreshController Controller(MainWindow window) => Field<DeferredNetworkRefreshController>(window, "_networkAdapterRefreshController");
    internal static ApplicationOperationCoordinator Core(MainWindow window) => Field<ApplicationOperationCoordinator>(window, "_applicationOperations");
    internal static T Field<T>(MainWindow window, string name) =>
        (T)(typeof(MainWindow).GetField(name, PrivateInstance)!.GetValue(window)
            ?? throw new InvalidOperationException($"Missing field {name}"));
    internal static void Power(MainWindow window, ObservationPowerTransition transition) =>
        typeof(MainWindow).GetMethod("HandleObservationPowerTransition", PrivateInstance)!.Invoke(window, [transition]);
    internal static void NotifyStaleIdle(MainWindow window) =>
        typeof(MainWindow).GetMethod("OnOperationStateForAdapterRefreshChanged", PrivateInstance)!.Invoke(window,
            [null, new ApplicationOperationStateChangedEventArgs(ApplicationOperationSnapshot.Idle())]);
    internal static TabControl Tabs(MainWindow window) => Find<TabControl>((DependencyObject)window.Content)
        ?? throw new InvalidOperationException("Real tab host missing");
    private static T? Find<T>(DependencyObject node) where T : DependencyObject
    {
        if (node is T result) return result;
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(node); index++)
        {
            T? found = Find<T>(VisualTreeHelper.GetChild(node, index));
            if (found is not null) return found;
        }
        return null;
    }
    internal static TaskCompletionSource<T> Pending<T>() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal static async Task DrainAsync() => await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
    internal static async Task UntilAsync(Func<bool> condition)
    {
        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(3));
        while (!condition()) await Task.Delay(1, deadline.Token);
    }
    internal static void Ensure(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
