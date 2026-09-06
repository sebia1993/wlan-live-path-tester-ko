using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using WlanLivePathTester.App;
using WlanLivePathTester.Core.Models;
using WlanLivePathTester.Core.Observation;
using WlanLivePathTester.Core.Operations;
using WlanLivePathTester.Core.Routing;
using WlanLivePathTester.Windows.Proxy;
using static WlanLivePathTester.RouteUiLifetimeSmoke.TestContext;

namespace WlanLivePathTester.RouteUiLifetimeSmoke;

internal static class RouteTests
{
    internal static readonly (string Name, Func<Task> Run)[] Cases =
    [
        ("every global kind blocks route import report and basic collectors", ExclusionAsync),
        ("manual route uses captured raw input and restores controls", ManualAsync),
        ("actual route stop forwards one request and discards late result", CancelAsync),
        ("import forwards consent and retains authoritative selection", ImportAsync),
        ("import is invalidated when target changes during collection", ChangedTargetAsync),
        ("expired imported selection never reaches comparison", ExpiredAsync),
        ("manual and import close await actual return", CloseAsync),
        ("route/import restore late tabs and current bindings", BindingsAsync),
        ("route/import suspend discard pre-suspend results", SuspendAsync),
        ("close inside Core acquisition is deferred and executes no reader", ReentrantCloseAsync),
        ("route/import exceptions preserve previous comparison without secrets", FailuresAsync)
    ];

    private static async Task ExclusionAsync()
    {
        MainWindow window = Prepare();
        int calls = 0;
        try
        {
            window.CompareManualRouteOverride = (_, _, _, _) => { calls++; return Task.FromResult(Run()); };
            window.ImportRouteProxyOverride = (_, _, _) => { calls++; return Task.FromResult(Imported()); };
            window.CompareImportedRouteOverride = (_, _, _, _) => { calls++; return Task.FromResult(Run()); };
            window.CollectBasicWlan = _ => { calls++; return Task.FromResult("unexpected"); };
            window.CollectBasicProxySettings = window.CollectBasicWlan;
            window.CollectWlanCorrelation = _ => { calls++; return Task.FromResult(new LocalDiagnosticText("unexpected")); };
            foreach (ApplicationOperationKind kind in Enum.GetValues<ApplicationOperationKind>().Where(k => k != ApplicationOperationKind.None))
            {
                ArmImport(window, Imported());
                using ApplicationOperationLease owner = Coordinator(window).TryBegin(kind).Lease!;
                Ensure(!await window.RunManualRouteComparisonAsync(), "Blocked manual route must not acquire.");
                Ensure(!await window.ImportRouteProxyAsync(), "Blocked import must not acquire.");
                Ensure(!await window.CompareImportedRouteAsync(), "Blocked imported comparison must not acquire.");
                Ensure(!await window.RunBasicDiagnosticAsync(false), "Blocked basic WLAN must not acquire.");
                Ensure(!await window.RunBasicDiagnosticAsync(true), "Blocked local proxy read must not acquire.");
                Ensure(!await window.RunWlanCorrelationAsync(), "Blocked correlation must not acquire.");
                Ensure(!await window.RunRouteReportSaveAsync(_ => { calls++; throw new InvalidOperationException(); }), "Blocked report must not acquire.");
                Ensure(calls == 0 && window.CurrentApplicationOperation.OperationId == owner.OperationId,
                    "No rejected collector/writer may run or replace its owner.");
            }
        }
        finally { window.Close(); }
    }

    private static async Task ManualAsync()
    {
        MainWindow window = Prepare();
        try
        {
            const string raw = " PROXY proxy.example.invalid:8080\t";
            Field<TextBox>(window, "_routeComparisonProxyDirectiveV3").Text = raw;
            window.CompareManualRouteOverride = (inside, directive, outside, _) =>
            {
                Ensure(directive == raw && inside == "internal.example.invalid" && outside!.AbsoluteUri == External,
                    "Manual UI must preserve raw proxy input and capture targets.");
                Ensure(window.CurrentApplicationOperation.Kind == ApplicationOperationKind.RouteComparison
                    && !Field<TextBox>(window, "_routeComparisonInternalTargetV3").IsEnabled
                    && !Field<Button>(window, "_importRouteProxyButton").IsEnabled, "Shared lease must precede collection.");
                return Task.FromResult(Run());
            };
            Ensure(await window.RunManualRouteComparisonAsync() && window.LatestRouteComparisonRunV3 is not null,
                "Successful manual result must be applied.");
            Idle(window);
            Ensure(Field<Button>(window, "_importRouteProxyButton").IsEnabled
                && !Field<Button>(window, "_routeComparisonCancelV3").IsEnabled, "Same-tab controls must restore.");
        }
        finally { window.Close(); }
    }

    private static async Task CancelAsync()
    {
        MainWindow window = Prepare();
        var old = Run();
        Set(window, "_latestRouteComparisonRunV3", old);
        var done = Pending<InternalProxyRouteComparisonRunResult>();
        int cancellations = 0;
        CancellationTokenRegistration registration = default;
        try
        {
            window.CompareManualRouteOverride = (_, _, _, token) =>
            {
                registration = token.Register(() => Interlocked.Increment(ref cancellations));
                return done.Task;
            };
            Task<bool> active = window.RunManualRouteComparisonAsync();
            Click(window, "_routeComparisonCancelV3");
            Click(window, "_routeComparisonCancelV3");
            Ensure(cancellations == 1 && !active.IsCompleted && window.CurrentApplicationOperation.IsBusy,
                "Cancel must not release native ownership before return.");
            done.SetResult(Run());
            await active;
            Ensure(ReferenceEquals(window.LatestRouteComparisonRunV3, old), "Late canceled result must not overwrite previous evidence.");
            Idle(window);
        }
        finally { registration.Dispose(); done.TrySetResult(Run()); window.Close(); }
    }

    private static async Task ImportAsync()
    {
        MainWindow window = Prepare();
        var imported = Imported();
        bool? forwarded = null;
        try
        {
            window.ImportRouteProxyOverride = (target, allowed, _) =>
            {
                forwarded = allowed;
                Ensure(target!.AbsoluteUri == External, "Use captured target.");
                return Task.FromResult(allowed ? imported : new WindowsRouteProxyImportResult(
                    WindowsRouteProxyImportStatus.NeedsAutomaticLookupConsent, ProxyConfigurationSource.None,
                    target, null, false, false, false, TimeProvider.System));
            };
            await window.ImportRouteProxyAsync();
            Ensure(forwarded == false && Optional(window, "_importedRouteProxy") is null, "Automatic consent defaults to false.");
            Field<CheckBox>(window, "_allowAutomaticRouteProxy").IsChecked = true;
            await window.ImportRouteProxyAsync();
            Ensure(forwarded == true && ReferenceEquals(Optional(window, "_importedRouteProxy"), imported), "Successful import should be retained.");
            imported.TryGetSelection(new Uri(External), out var expected);
            window.CompareImportedRouteOverride = (_, selection, _, _) =>
            {
                Ensure(ReferenceEquals(selection, expected), "Imported provenance must not be reconstructed through manual parsing.");
                return Task.FromResult(Run());
            };
            Ensure(await window.CompareImportedRouteAsync(), "Valid imported route must execute.");
            Idle(window);
        }
        finally { window.Close(); }
    }

    private static async Task ChangedTargetAsync()
    {
        MainWindow window = Prepare();
        var done = Pending<WindowsRouteProxyImportResult>();
        try
        {
            window.ImportRouteProxyOverride = (_, _, _) => done.Task;
            Task<bool> active = window.ImportRouteProxyAsync();
            Field<TextBox>(window, "_routeComparisonExternalTargetV3").Text = "https://changed.example.invalid/test.bin";
            Field<TextBox>(window, "_routeComparisonExternalTargetV3").Text = External;
            done.SetResult(Imported());
            await active;
            Ensure(Optional(window, "_importedRouteProxy") is null && ImportText(window).Contains("폐기", StringComparison.Ordinal),
                "Changing away and back during import must still invalidate the result.");
            Idle(window);
        }
        finally { done.TrySetResult(Imported()); window.Close(); }
    }

    private static async Task ExpiredAsync()
    {
        MainWindow window = Prepare();
        Clock clock = new();
        int calls = 0;
        try
        {
            ArmImport(window, Imported(clock));
            clock.Ticks += TimeSpan.FromMinutes(5).Ticks;
            window.CompareImportedRouteOverride = (_, _, _, _) => { calls++; return Task.FromResult(Run()); };
            Ensure(!await window.CompareImportedRouteAsync() && calls == 0, "Expired import must not start a reader.");
            Ensure(Optional(window, "_importedRouteProxy") is null, "Expired selection must not remain armed.");
        }
        finally { window.Close(); }
    }

    private static async Task CloseAsync()
    {
        foreach (ApplicationOperationKind kind in new[] { ApplicationOperationKind.RouteComparison, ApplicationOperationKind.WindowsProxyImport })
        {
            MainWindow window = Prepare();
            var done = Pending<int>();
            var closed = Pending<bool>();
            CancellationToken observed = default;
            int applied = 0;
            window.Closed += (_, _) => closed.TrySetResult(true);
            Task<bool> active = window.RunRouteUiOperationAsync(kind,
                token => { observed = token; return done.Task; }, _ => applied++);
            try
            {
                window.Close();
                window.Close();
                Ensure(observed.IsCancellationRequested && !closed.Task.IsCompleted && !active.IsCompleted,
                    "The single close owner must drain both route kinds.");
                done.SetResult(1);
                await active;
                await closed.Task.WaitAsync(TimeSpan.FromSeconds(3));
                Ensure(applied == 0 && !window.CurrentApplicationOperation.IsBusy, "Closed follows cleanup, never a late publication.");
            }
            finally { done.TrySetResult(0); if (!closed.Task.IsCompleted) window.Close(); }
        }
    }

    private static async Task BindingsAsync()
    {
        foreach (ApplicationOperationKind kind in new[] { ApplicationOperationKind.RouteComparison, ApplicationOperationKind.WindowsProxyImport })
        {
            MainWindow window = Prepare();
            var done = Pending<int>();
            TabItem peer = new() { Header = "binding" };
            Toggle source = new();
            BindingOperations.SetBinding(peer, UIElement.IsEnabledProperty, new Binding(nameof(Toggle.Enabled)) { Source = source });
            Tabs(window).Items.Add(peer);
            Task<bool> active = window.RunRouteUiOperationAsync(kind, _ => done.Task, _ => { });
            TabItem late = new() { Header = "late" };
            Tabs(window).Items.Add(late);
            try
            {
                source.Enabled = false;
                source.Enabled = true;
                await DrainAsync();
                Ensure(!peer.IsEnabled && !late.IsEnabled, "Source changes or new tabs must not unlock peers.");
                source.Enabled = false;
                done.SetResult(1);
                await active;
                await DrainAsync();
                Ensure(BindingOperations.IsDataBound(peer, UIElement.IsEnabledProperty) && !peer.IsEnabled && late.IsEnabled,
                    "Restore the live binding and late tab state, not an old effective Boolean.");
                source.Enabled = true;
                await DrainAsync();
                Ensure(peer.IsEnabled, "Restored binding must still work.");
            }
            finally { done.TrySetResult(0); window.Close(); }
        }
    }

    private static async Task SuspendAsync()
    {
        MainWindow window = Prepare();
        var done = Pending<int>();
        int applied = 0;
        Task<bool> active = window.RunRouteUiOperationAsync(ApplicationOperationKind.RouteComparison, _ => done.Task, _ => applied++);
        try
        {
            var power = typeof(MainWindow).GetMethod("HandleObservationPowerTransition", Hidden)!;
            power.Invoke(window, [ObservationPowerTransition.Suspend]);
            power.Invoke(window, [ObservationPowerTransition.Resume]);
            done.SetResult(1);
            await active;
            Ensure(applied == 0, "Resume must not revalidate a pre-suspend snapshot.");
            await window.RunRouteUiOperationAsync(ApplicationOperationKind.RouteComparison, _ => Task.FromResult(2), _ => applied++);
            Ensure(applied == 1, "A fresh operation may succeed after resume.");
        }
        finally { done.TrySetResult(0); window.Close(); }
    }

    private static async Task ReentrantCloseAsync()
    {
        MainWindow window = Prepare();
        var closed = Pending<bool>();
        bool requested = false;
        int calls = 0;
        window.Closed += (_, _) => closed.TrySetResult(true);
        EventHandler<ApplicationOperationStateChangedEventArgs> handler = (_, e) =>
        {
            if (e.Snapshot.IsBusy && !requested) { requested = true; window.Close(); }
        };
        Coordinator(window).StateChanged += handler;
        try
        {
            await window.RunRouteUiOperationAsync(ApplicationOperationKind.RouteComparison,
                _ => { calls++; return Task.FromResult(1); }, _ => { });
            await closed.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Ensure(requested && calls == 0 && !window.CurrentApplicationOperation.IsBusy,
                "An acquisition-time close must latch cancellation and post final Close safely.");
        }
        finally { Coordinator(window).StateChanged -= handler; if (!closed.Task.IsCompleted) window.Close(); }
    }

    private static async Task FailuresAsync()
    {
        MainWindow window = Prepare();
        var old = Run();
        Set(window, "_latestRouteComparisonRunV3", old);
        try
        {
            foreach (ApplicationOperationKind kind in new[] { ApplicationOperationKind.RouteComparison, ApplicationOperationKind.WindowsProxyImport })
            {
                await window.RunRouteUiOperationAsync<int>(kind, _ => Task.FromException<int>(new IOException(Secret)), _ => throw new InvalidOperationException());
                Ensure(RouteText(window).Contains(nameof(IOException), StringComparison.Ordinal) && !RouteText(window).Contains(Secret, StringComparison.Ordinal),
                    "Display only the error type.");
                Ensure(ReferenceEquals(old, window.LatestRouteComparisonRunV3), "Failure cannot erase the previous comparison.");
                Idle(window);
            }
        }
        finally { window.Close(); }
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
