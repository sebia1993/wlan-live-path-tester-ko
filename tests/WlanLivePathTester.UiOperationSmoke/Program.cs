using System.ComponentModel;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using WlanLivePathTester.App;
using WlanLivePathTester.Core.Operations;

namespace WlanLivePathTester.UiOperationSmoke;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
        SynchronizationContext.SetSynchronizationContext(
            new DispatcherSynchronizationContext(dispatcher));
        Task tests = RunAsync().WaitAsync(TimeSpan.FromSeconds(45));
        _ = tests.ContinueWith(
            _ => dispatcher.BeginInvokeShutdown(DispatcherPriority.Normal),
            CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
        Dispatcher.Run();
        try
        {
            tests.GetAwaiter().GetResult();
            Console.WriteLine("PASS WPF operation smoke: 10 groups; no DNS, HTTP or WLAN reads");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static async Task RunAsync()
    {
        await Task.Yield();
        VerifyOperationPairMatrix();
        VerifySharedRouteOwnership();
        VerifyLateTabsAndRemovedTabs();
        await VerifyBindingRestorationAsync();
        VerifyCancellationAndStaleLease();
        await VerifyShutdownDrainAsync();
        VerifyExceptionCleanup();
        await VerifyDispatcherOwnershipAsync();
        await VerifyRealMeasurementEntryPointAsync();
        await VerifyRealDeferredCloseAsync();
    }

    private static void VerifyOperationPairMatrix()
    {
        ApplicationOperationKind[] kinds =
        [
            ApplicationOperationKind.DownloadMeasurement,
            ApplicationOperationKind.ProxyRouteResolution,
            ApplicationOperationKind.BrowserObservation
        ];
        foreach (ApplicationOperationKind first in kinds)
        foreach (ApplicationOperationKind second in kinds)
        {
            (TabControl host, TabItem owner, TabItem peer) = Tabs();
            TabItem preDisabled = new() { IsEnabled = false };
            host.Items.Add(preDisabled);
            ApplicationOperationUiSession session = new(Dispatcher.CurrentDispatcher);
            using ApplicationOperationUiLease active = Begin(session, host, first);
            Ensure(owner.IsEnabled && !peer.IsEnabled && !preDisabled.IsEnabled,
                "Only the active tab should remain available.");
            ApplicationOperationUiLease? blocked = session.TryBegin(
                second, host, null, out ApplicationOperationStartStatus status);
            Ensure(blocked is null && status == ApplicationOperationStartStatus.Busy,
                "Every active operation pair must reject a second lease.");
            active.Dispose();
            Ensure(owner.IsEnabled && peer.IsEnabled && !preDisabled.IsEnabled,
                "Pre-existing tab states must be restored.");
            Ensure(ReferenceEquals(peer.ReadLocalValue(UIElement.IsEnabledProperty),
                    DependencyProperty.UnsetValue),
                "An inherited/default IsEnabled value must not become a local Boolean.");
        }
        Console.WriteLine("PASS operation kind pair matrix (9 pairs)");
    }

    private static void VerifySharedRouteOwnership()
    {
        (TabControl host, _, _) = Tabs();
        ApplicationOperationCoordinator shared = new();
        ApplicationOperationUiSession session = new(Dispatcher.CurrentDispatcher, shared);
        using ApplicationOperationLease route = shared.TryBegin(
            ApplicationOperationKind.RouteComparison).Lease!;
        Ensure(session.TryBegin(ApplicationOperationKind.DownloadMeasurement, host, null,
            out ApplicationOperationStartStatus blocked) is null
            && blocked == ApplicationOperationStartStatus.Busy && !session.HasActiveUiLease,
            "Existing route actions must block the UI adapter through the same coordinator.");
        route.Dispose();
        using ApplicationOperationUiLease observation = Begin(
            session, host, ApplicationOperationKind.BrowserObservation);
        Ensure(shared.TryBegin(ApplicationOperationKind.WindowsProxyImport).Status
            == ApplicationOperationStartStatus.Busy,
            "UI actions must block existing Windows proxy import through the same coordinator.");
        Console.WriteLine("PASS shared route/import and UI coordinator ownership");
    }

    private static void VerifyLateTabsAndRemovedTabs()
    {
        (TabControl host, _, TabItem peer) = Tabs();
        ApplicationOperationUiSession session = new(Dispatcher.CurrentDispatcher);
        ApplicationOperationUiLease active = Begin(session, host);
        TabItem late = new();
        host.Items.Add(late);
        Ensure(!late.IsEnabled, "Tabs registered during an operation must be locked.");
        host.Items.Remove(peer);
        active.Dispose();
        Ensure(peer.IsEnabled && late.IsEnabled,
            "Removed and late-added tabs must both have their state restored.");
        TabItem after = new();
        host.Items.Add(after);
        Ensure(after.IsEnabled, "The old collection listener must be detached.");
        Console.WriteLine("PASS late/removed tabs and collection listener cleanup");
    }

    private static async Task VerifyBindingRestorationAsync()
    {
        (TabControl host, _, TabItem peer) = Tabs();
        Toggle source = new();
        BindingOperations.SetBinding(peer, UIElement.IsEnabledProperty,
            new Binding(nameof(Toggle.Enabled)) { Source = source, Mode = BindingMode.OneWay });
        ApplicationOperationUiSession session = new(Dispatcher.CurrentDispatcher);
        ApplicationOperationUiLease active = Begin(session, host);
        source.Enabled = false;
        source.Enabled = true;
        await Dispatcher.Yield(DispatcherPriority.Background);
        Ensure(!peer.IsEnabled, "Binding changes must not unlock a peer during an operation.");
        source.Enabled = false;
        active.Dispose();
        await Dispatcher.Yield(DispatcherPriority.Background);
        Ensure(BindingOperations.IsDataBound(peer, UIElement.IsEnabledProperty)
            && !peer.IsEnabled, "Cleanup must restore the live binding, not an old Boolean.");
        source.Enabled = true;
        await Dispatcher.Yield(DispatcherPriority.Background);
        Ensure(peer.IsEnabled, "A restored binding must continue to update.");
        Console.WriteLine("PASS binding preservation and live-source restoration");
    }

    private static void VerifyCancellationAndStaleLease()
    {
        (TabControl host, _, TabItem peer) = Tabs();
        ApplicationOperationUiSession session = new(Dispatcher.CurrentDispatcher);
        int calls = 0;
        ApplicationOperationUiLease old = Begin(session, host,
            requestCancellation: () => calls++);
        Ensure(old.RequestCancellation() == ApplicationOperationCancellationStatus.Requested,
            "The first cancel request must reach the operation.");
        Ensure(old.RequestCancellation() == ApplicationOperationCancellationStatus.AlreadyRequested
            && calls == 1 && session.Snapshot.IsBusy && !peer.IsEnabled,
            "Cancellation must run once without releasing the operation.");
        old.Dispose();
        using ApplicationOperationUiLease next = Begin(session, host);
        old.Dispose();
        Ensure(!old.IsCurrent && next.IsCurrent && !peer.IsEnabled,
            "An old lease must not restore tabs or accept stale progress for a new operation.");
        Ensure(old.RequestCancellation() == ApplicationOperationCancellationStatus.NotActive,
            "An old cancel action must not cancel new work.");
        Console.WriteLine("PASS cancellation lifetime and stale-lease isolation");
    }

    private static async Task VerifyShutdownDrainAsync()
    {
        (TabControl host, _, TabItem peer) = Tabs();
        ApplicationOperationUiSession session = new(Dispatcher.CurrentDispatcher);
        int calls = 0;
        ApplicationOperationUiLease active = Begin(session, host,
            requestCancellation: () => calls++);
        Task<ApplicationOperationShutdownResult> shutdown = session.RequestShutdownAsync();
        Ensure(!shutdown.IsCompleted && calls == 1 && !peer.IsEnabled,
            "Shutdown must request cancellation and wait for real completion.");
        Ensure(session.TryBegin(ApplicationOperationKind.ProxyRouteResolution, host, null,
            out ApplicationOperationStartStatus blocked) is null
            && blocked == ApplicationOperationStartStatus.ShutdownPending,
            "No new action may start during shutdown.");
        active.Dispose();
        await shutdown.WaitAsync(TimeSpan.FromSeconds(5));
        Ensure(peer.IsEnabled && !session.Snapshot.IsBusy,
            "Tabs must be restored before shutdown completion.");
        Ensure(session.CancelShutdownRequest(), "A vetoed close must be reopenable.");
        ApplicationOperationUiLease native = Begin(session, host,
            ApplicationOperationKind.ProxyRouteResolution);
        Task<ApplicationOperationShutdownResult> nativeShutdown = session.RequestShutdownAsync();
        Ensure(!nativeShutdown.IsCompleted && !session.Snapshot.SupportsCancellation,
            "A synchronous native action cannot be represented as immediately canceled.");
        native.Dispose();
        await nativeShutdown.WaitAsync(TimeSpan.FromSeconds(5));
        Console.WriteLine("PASS cancelable and noncancelable shutdown draining");
    }

    private static void VerifyExceptionCleanup()
    {
        (TabControl host, _, TabItem peer) = Tabs();
        ApplicationOperationUiSession session = new(Dispatcher.CurrentDispatcher);
        try
        {
            using ApplicationOperationUiLease lease = Begin(session, host);
            throw new InvalidOperationException("synthetic action failure");
        }
        catch (InvalidOperationException)
        {
            Ensure(!session.Snapshot.IsBusy && peer.IsEnabled,
                "Exception cleanup must restore controls and release the operation.");
        }
        Console.WriteLine("PASS exception cleanup");
    }

    private static async Task VerifyDispatcherOwnershipAsync()
    {
        (TabControl host, _, _) = Tabs();
        ApplicationOperationUiSession session = new(Dispatcher.CurrentDispatcher);
        bool rejected = await Task.Run(() =>
        {
            try { session.TryBegin(ApplicationOperationKind.DownloadMeasurement, host, null, out _); }
            catch (InvalidOperationException) { return true; }
            return false;
        });
        Ensure(rejected && !session.Snapshot.IsBusy,
            "A worker thread must not mutate the UI or acquire an orphan lease.");
        Console.WriteLine("PASS UI dispatcher ownership");
    }

    private static async Task VerifyRealMeasurementEntryPointAsync()
    {
        // Construct but never show the window. No Loaded hook, WLAN read or
        // network reader is run; only the injected synthetic delegate executes.
        MainWindow window = new();
        TabControl host = PrepareWindow(window);
        TaskCompletionSource done = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int calls = 0;
        try
        {
            Func<CancellationToken, Task> operation = _ => { calls++; return done.Task; };
            Task first = StartSyntheticMeasurement(window, operation);
            Ensure(calls == 1, "The real measurement entry point must run the injected action.");
            Task second = StartSyntheticMeasurement(window, _ => { calls++; return Task.CompletedTask; });
            await second;
            Ensure(calls == 1, "The real entry point must reject a concurrent action.");
            Invoke(window, "OnResolveProxyRouteClick", window, new RoutedEventArgs());
            Ensure(Session(window).Snapshot.Kind == ApplicationOperationKind.DownloadMeasurement,
                "The real proxy click must not start WinHTTP while measurement owns the lease.");
            Ensure(window.CurrentApplicationOperation.OperationId == Session(window).Snapshot.OperationId,
                "The production UI session must expose the same global operation ID.");
            done.TrySetResult();
            await first.WaitAsync(TimeSpan.FromSeconds(5));
            Ensure(!Session(window).Snapshot.IsBusy && host.Items.OfType<TabItem>().All(t => t.IsEnabled),
                "The real measurement finally must release the lease and restore peer tabs.");
        }
        finally { done.TrySetResult(); window.Close(); }
        Console.WriteLine("PASS real measurement and proxy-click integration with injected work");
    }

    private static async Task VerifyRealDeferredCloseAsync()
    {
        MainWindow window = new();
        _ = PrepareWindow(window);
        TaskCompletionSource done = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource closedSignal = new(TaskCreationOptions.RunContinuationsAsynchronously);
        bool closed = false;
        CancellationToken observed = default;
        window.Closed += (_, _) => { closed = true; closedSignal.TrySetResult(); };
        Task operation = StartSyntheticMeasurement(window, token =>
        {
            observed = token;
            return done.Task;
        });
        try
        {
            window.Close();
            Ensure(!closed && observed.IsCancellationRequested && Session(window).Snapshot.IsBusy,
                "Close must cancel but retain the window until the action actually finishes.");
            done.TrySetResult();
            await operation.WaitAsync(TimeSpan.FromSeconds(5));
            // Wait for the actual event rather than assuming a single yield
            // also drains an independently scheduled shutdown continuation.
            await closedSignal.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Ensure(closed && !Session(window).Snapshot.IsBusy,
                "The production close handler must close only after UI cleanup and lease release.");
        }
        finally { done.TrySetResult(); if (!closed) window.Close(); }
        Console.WriteLine("PASS production deferred close without blocking the dispatcher");
    }

    private static TabControl PrepareWindow(MainWindow window)
    {
        FrameworkElement root = (FrameworkElement)window.Content;
        root.Measure(new Size(1200, 900));
        root.Arrange(new Rect(0, 0, 1200, 900));
        root.UpdateLayout();
        TabControl host = FindTabs(root)
            ?? throw new InvalidOperationException("Production tab host was not created.");
        host.SelectedIndex = 0;
        return host;
    }

    private static TabControl? FindTabs(DependencyObject item)
    {
        if (item is TabControl tabs) return tabs;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(item); i++)
        {
            TabControl? found = FindTabs(VisualTreeHelper.GetChild(item, i));
            if (found is not null) return found;
        }
        return null;
    }

    private static object? Invoke(MainWindow window, string name, params object[] arguments) =>
        (typeof(MainWindow).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Missing production method: {name}"))
        .Invoke(window, arguments);

    private static Task StartSyntheticMeasurement(MainWindow window, Func<CancellationToken, Task> operation) =>
        (Task)(Invoke(window, "RunMeasurementOperationAsync", operation, "합성 측정")
            ?? throw new InvalidOperationException("The measurement method returned no task."));

    private static ApplicationOperationUiSession Session(MainWindow window) =>
        (ApplicationOperationUiSession)(typeof(MainWindow)
            .GetField("_applicationOperationUi", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(window) ?? throw new InvalidOperationException("UI session missing."));

    private static ApplicationOperationUiLease Begin(
        ApplicationOperationUiSession session, TabControl host,
        ApplicationOperationKind kind = ApplicationOperationKind.DownloadMeasurement,
        Action? requestCancellation = null) =>
        session.TryBegin(kind, host, requestCancellation, out _)
            ?? throw new InvalidOperationException("Expected an exclusive UI lease.");

    private static (TabControl Host, TabItem Owner, TabItem Peer) Tabs()
    {
        TabItem owner = new() { Header = "active" };
        TabItem peer = new() { Header = "peer" };
        TabControl host = new();
        host.Items.Add(owner);
        host.Items.Add(peer);
        host.SelectedItem = owner;
        return (host, owner, peer);
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class Toggle : INotifyPropertyChanged
    {
        private bool _enabled = true;
        public event PropertyChangedEventHandler? PropertyChanged;
        public bool Enabled
        {
            get => _enabled;
            set { _enabled = value; PropertyChanged?.Invoke(this, new(nameof(Enabled))); }
        }
    }
}
