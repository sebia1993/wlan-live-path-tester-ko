using System.ComponentModel;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using WlanLivePathTester.App;
using WlanLivePathTester.Core.Operations;
using static WlanLivePathTester.RouteUiLifetimeSmoke.TestContext;

namespace WlanLivePathTester.RouteUiLifetimeSmoke;

internal static class BasicTests
{
    internal static readonly (string Name, Func<Task> Run)[] Cases =
    [
        ("basic WLAN and local proxy handlers execute through central admission", BasicHandlersAsync),
        ("actual WLAN correlation button restores controls after success and failure", CorrelationAsync),
        ("basic and correlation reads defer close and suppress late results", CloseReadsAsync),
        ("a final-close veto reopens admission without duplicate closing tasks", CloseVetoAsync),
        ("single measurement cancel callback failure is visible and does not free work", MeasurementCancelFailureAsync),
        ("all new entry points reject worker thread starts", DispatcherAsync),
        ("closed windows cannot recreate tabs or start readers", ClosedAsync)
    ];

    private static async Task BasicHandlersAsync()
    {
        MainWindow window = Prepare("WLAN · 프록시");
        try
        {
            foreach (bool proxy in new[] { false, true })
            {
                string marker = proxy ? "synthetic-local-settings" : "synthetic-wlan";
                Func<CancellationToken, Task<string>> collector = _ =>
                {
                    Ensure(window.CurrentApplicationOperation.Kind == (proxy
                        ? ApplicationOperationKind.ProxyRouteResolution : ApplicationOperationKind.NetworkAdapterDiagnostics),
                        "The basic collector must already own the shared operation lease.");
                    return Task.FromResult(marker);
                };
                if (proxy) window.CollectBasicProxySettings = collector;
                else window.CollectBasicWlan = collector;
                Button sender = new() { IsEnabled = true };
                string handler = proxy ? "OnReadProxySettingsClick" : "OnReadWlanStatusClick";
                typeof(MainWindow).GetMethod(handler, Hidden)!.Invoke(window, [sender, new RoutedEventArgs()]);
                await DrainAsync();
                Ensure((proxy ? window.ProxyResultText.Text : window.WlanResultText.Text) == marker && sender.IsEnabled,
                    "Production handler must publish the synthetic result and restore its button.");
                Idle(window);
            }
        }
        finally { window.Close(); }
    }

    private static async Task CorrelationAsync()
    {
        MainWindow window = Prepare("WLAN NIC 대응");
        try
        {
            int calls = 0;
            window.CollectWlanCorrelation = _ =>
            {
                calls++;
                Ensure(window.CurrentApplicationOperation.IsBusy
                    && !Field<Button>(window, "_correlateWlanInterfaceButton").IsEnabled,
                    "Correlation collector must not run before its UI is locked.");
                return Task.FromResult(new LocalDiagnosticText("synthetic-correlated"));
            };
            Click(window, "_correlateWlanInterfaceButton");
            await DrainAsync();
            Ensure(calls == 1 && Field<TextBlock>(window, "_wlanInterfaceCorrelationResultText").Text == "synthetic-correlated",
                "Actual correlation click must use the injectable production path.");
            window.CollectWlanCorrelation = _ => Task.FromException<LocalDiagnosticText>(new IOException(Secret));
            await window.RunWlanCorrelationAsync();
            string message = Field<TextBlock>(window, "_wlanInterfaceCorrelationResultText").Text;
            Ensure(message.Contains(nameof(IOException), StringComparison.Ordinal) && !message.Contains(Secret, StringComparison.Ordinal),
                "Correlation failures must not expose exception payloads.");
            Ensure(Field<Button>(window, "_correlateWlanInterfaceButton").IsEnabled, "Failure must restore the start button.");
            Idle(window);
        }
        finally { window.Close(); }
    }

    private static async Task CloseReadsAsync()
    {
        for (int mode = 0; mode < 3; mode++)
        {
            MainWindow window = Prepare(mode == 2 ? "WLAN NIC 대응" : "WLAN · 프록시");
            var release = Pending<string>();
            var closed = Pending<bool>();
            CancellationToken observed = default;
            Func<CancellationToken, Task<string>> collector = token => { observed = token; return release.Task; };
            window.CollectBasicWlan = collector;
            window.CollectBasicProxySettings = collector;
            window.CollectWlanCorrelation = async token => new LocalDiagnosticText(await collector(token));
            window.Closed += (_, _) => closed.TrySetResult(true);
            Task<bool> active = mode == 2 ? window.RunWlanCorrelationAsync() : window.RunBasicDiagnosticAsync(mode == 1);
            try
            {
                window.Close();
                Ensure(!closed.Task.IsCompleted && observed.IsCancellationRequested && !active.IsCompleted,
                    "A basic Windows read must retain its real lifetime during close.");
                release.SetResult("late-value-must-not-apply");
                await active;
                await closed.Task.WaitAsync(TimeSpan.FromSeconds(3));
                string text = mode == 0 ? window.WlanResultText.Text : mode == 1 ? window.ProxyResultText.Text
                    : Field<TextBlock>(window, "_wlanInterfaceCorrelationResultText").Text;
                Ensure(!text.Contains("late-value", StringComparison.Ordinal) && !window.CurrentApplicationOperation.IsBusy,
                    "A close-canceled snapshot must not be published after native return.");
            }
            finally { release.TrySetResult("cleanup"); if (!closed.Task.IsCompleted) window.Close(); }
        }
    }

    private static async Task CloseVetoAsync()
    {
        MainWindow window = Prepare();
        var release = Pending<int>();
        bool vetoed = false;
        bool closed = false;
        CancelEventHandler veto = (_, e) =>
        {
            if (!window.CurrentApplicationOperation.IsBusy && !vetoed) { vetoed = true; e.Cancel = true; }
        };
        window.Closing += veto;
        window.Closed += (_, _) => closed = true;
        Task<bool> active = window.RunRouteUiOperationAsync(ApplicationOperationKind.RouteComparison, _ => release.Task, _ => { });
        try
        {
            window.Close();
            release.SetResult(1);
            await active;
            await UntilAsync(() => vetoed && !Field<bool>(window, "_applicationOperationClosePending"));
            Ensure(!closed && !window.CurrentApplicationOperation.ShutdownRequested,
                "A vetoed final close must not permanently block new work.");
            int applied = 0;
            await window.RunRouteUiOperationAsync(ApplicationOperationKind.RouteComparison, _ => Task.FromResult(2), _ => applied++);
            Ensure(applied == 1, "Admission must recover after the final close is vetoed.");
            window.Close();
            Ensure(closed, "A later close must execute normally.");
        }
        finally { window.Closing -= veto; release.TrySetResult(0); if (!closed) window.Close(); }
    }

    private static async Task MeasurementCancelFailureAsync()
    {
        MainWindow window = Prepare();
        var release = Pending<bool>();
        CancellationTokenRegistration registration = default;
        Func<CancellationToken, Task> work = token =>
        {
            registration = token.Register(() => throw new IOException(Secret));
            return release.Task;
        };
        Task active = (Task)typeof(MainWindow).GetMethod("RunMeasurementOperationAsync", Hidden)!
            .Invoke(window, [work, "synthetic measurement"] )!;
        try
        {
            typeof(MainWindow).GetMethod("OnCancelMeasurementClick", Hidden)!
                .Invoke(window, [window, new RoutedEventArgs()]);
            Ensure(window.CurrentApplicationOperation.CancellationCallbackFailed
                && window.CurrentApplicationOperation.IsBusy && !active.IsCompleted,
                "Callback failure must be recorded without releasing the active measurement.");
            Ensure(window.MeasurementStatusText.Text.Contains("실패", StringComparison.Ordinal)
                && !window.MeasurementStatusText.Text.Contains(Secret, StringComparison.Ordinal),
                "The cancel button must not falsely report success or reflect raw callback text.");
            release.SetResult(true);
            await active;
            Idle(window);
        }
        finally { registration.Dispose(); release.TrySetResult(true); await active; window.Close(); }
    }

    private static async Task DispatcherAsync()
    {
        MainWindow window = Prepare();
        try
        {
            Func<Task>[] starts =
            [
                () => window.RunManualRouteComparisonAsync(),
                () => window.ImportRouteProxyAsync(),
                () => window.CompareImportedRouteAsync(),
                () => window.RunBasicDiagnosticAsync(false),
                () => window.RunWlanCorrelationAsync(),
                () => window.RunRouteReportSaveAsync(_ => throw new InvalidOperationException("must not run"))
            ];
            foreach (Func<Task> start in starts)
            {
                bool rejected = await Task.Run(async () =>
                {
                    try { await start(); }
                    catch (InvalidOperationException) { return true; }
                    return false;
                });
                Ensure(rejected && !window.CurrentApplicationOperation.IsBusy,
                    "Worker threads may not manipulate UI or acquire orphan leases.");
            }
        }
        finally { window.Close(); }
    }

    private static async Task ClosedAsync()
    {
        MainWindow window = Prepare();
        int tabsBefore = Tabs(window).Items.Count;
        int calls = 0;
        window.CollectBasicWlan = _ => { calls++; return Task.FromResult("unexpected"); };
        window.CollectBasicProxySettings = window.CollectBasicWlan;
        window.CollectWlanCorrelation = _ => { calls++; return Task.FromResult(new LocalDiagnosticText("unexpected")); };
        window.CompareManualRouteOverride = (_, _, _, _) => { calls++; return Task.FromResult(Run()); };
        window.ImportRouteProxyOverride = (_, _, _) => { calls++; return Task.FromResult(Imported()); };
        window.Close();
        Ensure(!await window.RunBasicDiagnosticAsync(false) && !await window.RunBasicDiagnosticAsync(true)
            && !await window.RunWlanCorrelationAsync() && !await window.RunManualRouteComparisonAsync()
            && !await window.ImportRouteProxyAsync(), "Closed windows must reject all new work.");
        window.EnsureRouteComparisonTabV3();
        window.EnsureRouteComparisonReportTabV2();
        window.EnsureRouteProxyImportControls();
        window.EnsureWlanInterfaceCorrelationTab();
        Ensure(calls == 0 && Tabs(window).Items.Count == tabsBefore, "Closing must not allow new readers or duplicate tab registration.");
    }
}
