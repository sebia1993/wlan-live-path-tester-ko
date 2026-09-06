using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using WlanLivePathTester.App;
using WlanLivePathTester.Core.Observation;
using WlanLivePathTester.Core.Operations;
using WlanLivePathTester.Core.Routing;
using static WlanLivePathTester.OperationLifecycleSmoke.SmokeContext;

namespace WlanLivePathTester.OperationLifecycleSmoke;

internal static class DiagnosticAndRefreshTests
{
    internal static readonly (string Name, Func<Task> Run)[] Cases =
    [
        ("all operation kinds block three readers and five report writers", ExclusionMatrixAsync),
        ("three real diagnostic entry points restore controls", SuccessfulReadsAsync),
        ("route input snapshot and actual stop callback once", RouteStopAsync),
        ("three diagnostic failures hide payload and release lease", FailureCleanupAsync),
        ("cooperative deadline retains lease until ignored-token read returns", DeadlineAsync),
        ("suspend discards stale inventory and resume schedules fresh read", SuspendResumeAsync),
        ("window close drains all three diagnostic types", DeferredCloseAsync),
        ("diagnostic and scheduler enforce dispatcher ownership", DispatcherOwnershipAsync),
        ("closed windows ignore queued events and reject reentry", ClosedWindowAsync),
        ("automatic refresh waits behind every global kind and stale idle event", AutomaticMatrixAsync),
        ("one thousand worker notifications coalesce", BurstAsync),
        ("notifications during refresh produce only one follow-up", FollowUpAsync),
        ("rejected acquisition stays pending without a timer loop", RejectedRefreshAsync),
        ("scheduler exceptions are bounded and retry requires a new request", FailedRefreshAsync),
        ("pending request survives readiness and shutdown transitions", ReadinessAndShutdownAsync),
        ("disposed scheduler suppresses queued and post-completion work", DisposedSchedulerAsync)
    ];

    private static async Task ExclusionMatrixAsync()
    {
        MainWindow window = Prepare();
        int calls = 0;
        try
        {
            foreach (ApplicationOperationKind kind in DiagnosticKinds)
                SetReader(window, kind, _ => { calls++; return Task.FromResult(new LocalDiagnosticText("unexpected")); });
            foreach (AuxiliaryReportSection report in window.AuxiliaryReportSections.Values)
                report.CaptureAndSave = () => { calls++; return Task.FromResult<AuxiliaryReportExport?>(Export()); };
            foreach (ApplicationOperationKind owner in Enum.GetValues<ApplicationOperationKind>().Where(k => k != ApplicationOperationKind.None))
            {
                using ApplicationOperationLease active = Core(window).TryBegin(owner).Lease!;
                foreach (ApplicationOperationKind kind in DiagnosticKinds)
                    Ensure(!await Run(window, kind), "A blocked reader must not acquire a lease.");
                foreach (AuxiliaryReportSection report in window.AuxiliaryReportSections.Values)
                    Ensure(!await report.SaveAsync(), "A blocked report must not collect or write.");
                Ensure(calls == 0 && window.CurrentApplicationOperation.OperationId == active.OperationId,
                    "Rejection must preserve the real active operation and execute no delegate.");
            }
        }
        finally { window.Close(); }
    }

    private static async Task SuccessfulReadsAsync()
    {
        MainWindow window = Prepare();
        try
        {
            foreach (ApplicationOperationKind kind in DiagnosticKinds)
            {
                Select(window, kind);
                SetReader(window, kind, _ =>
                {
                    Ensure(window.CurrentApplicationOperation.Kind == kind && !Start(window, kind).IsEnabled,
                        "Reader must run after acquiring its fixed kind and disabling its start action.");
                    Ensure(Tabs(window).Items.OfType<TabItem>().Where(t => !ReferenceEquals(t, Tabs(window).SelectedItem)).All(t => !t.IsEnabled),
                        "Peer tabs must be locked before the native reader is entered.");
                    return Task.FromResult(new LocalDiagnosticText("fresh-" + kind));
                });
                Ensure(await Run(window, kind), "Valid diagnostic should acquire the lease.");
                Ensure(Result(window, kind) == "fresh-" + kind && Start(window, kind).IsEnabled
                    && !window.CurrentApplicationOperation.IsBusy, "Successful read must apply once and restore UI.");
            }
        }
        finally { window.Close(); }
    }

    private static async Task RouteStopAsync()
    {
        MainWindow window = Prepare();
        TaskCompletionSource<LocalDiagnosticText> done = Pending<LocalDiagnosticText>();
        CancellationToken token = default;
        int cancelCalls = 0;
        string? seenTarget = null;
        RouteProbePurpose seenPurpose = default;
        try
        {
            Field<TextBox>(window, "_routeEvidenceTargetTextBox").Text = "initial.example.invalid";
            window.CollectRouteEvidence = (target, purpose, current) =>
            {
                token = current; seenTarget = target; seenPurpose = purpose;
                token.Register(() => Interlocked.Increment(ref cancelCalls));
                return done.Task;
            };
            Task<bool> running = window.RunRouteEvidenceAsync();
            Ensure(!Field<TextBox>(window, "_routeEvidenceTargetTextBox").IsEnabled
                && !Field<Button>(window, "_useExternalRouteTargetButton").IsEnabled, "Input and copy actions must be locked.");
            Field<TextBox>(window, "_routeEvidenceTargetTextBox").Text = "changed.example.invalid";
            Field<ComboBox>(window, "_routeEvidencePurposeComboBox").SelectedIndex = 2;
            Button stop = Field<Button>(window, "_cancelRouteEvidenceButton");
            stop.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            stop.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Ensure(token.IsCancellationRequested && cancelCalls == 1 && !stop.IsEnabled
                && !running.IsCompleted && window.CurrentApplicationOperation.IsBusy,
                "Stop must run once without disposing a reader's token or releasing the lease early.");
            Ensure(seenTarget == "initial.example.invalid" && seenPurpose == RouteProbePurpose.InternalDirectTarget,
                "The read must use the original target and purpose.");
            done.SetResult(new LocalDiagnosticText("late-result-must-not-apply"));
            await running;
            Ensure(!Result(window, ApplicationOperationKind.RouteEvidence).Contains("late-result", StringComparison.Ordinal)
                && !window.CurrentApplicationOperation.IsBusy && !stop.IsEnabled
                && Field<TextBox>(window, "_routeEvidenceTargetTextBox").IsEnabled,
                "Canceled late result must be suppressed and inputs restored.");
        }
        finally { done.TrySetResult(new LocalDiagnosticText("cleanup")); window.Close(); }
    }

    private static async Task FailureCleanupAsync()
    {
        const string secret = "https://private-reader.example.invalid/token";
        MainWindow window = Prepare();
        try
        {
            foreach (ApplicationOperationKind kind in DiagnosticKinds)
            {
                Select(window, kind);
                SetReader(window, kind, _ => Task.FromException<LocalDiagnosticText>(new IOException(secret)));
                Ensure(await Run(window, kind), "Failure still counts as an acquired attempt, not a rejected refresh.");
                Ensure(Result(window, kind).Contains(nameof(IOException), StringComparison.Ordinal)
                    && !Result(window, kind).Contains(secret, StringComparison.Ordinal)
                    && !window.CurrentApplicationOperation.IsBusy && Start(window, kind).IsEnabled,
                    "Reader failure must show only the type and clean up.");
            }
        }
        finally { window.Close(); }
    }

    private static async Task DeadlineAsync()
    {
        MainWindow window = Prepare();
        TaskCompletionSource<int> done = Pending<int>();
        TaskCompletionSource<bool> tokenCanceled = Pending<bool>();
        int applied = 0;
        string status = string.Empty;
        try
        {
            Task<bool> running = window.RunLocalDiagnosticAsync(ApplicationOperationKind.RouteEvidence,
                token => { token.Register(() => tokenCanceled.TrySetResult(true)); return done.Task; },
                _ => applied++, _ => { }, (text, _) => status = text,
                cooperativeTimeout: TimeSpan.FromMilliseconds(20));
            await tokenCanceled.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Ensure(!running.IsCompleted && window.CurrentApplicationOperation.IsBusy,
                "Deadline must cancel cooperation, never abandon an unfinished native call.");
            done.SetResult(42);
            await running;
            Ensure(applied == 0 && status.Contains("제한 시간", StringComparison.Ordinal)
                && !window.CurrentApplicationOperation.IsBusy, "Timed-out result must not be published.");
        }
        finally { done.TrySetResult(0); window.Close(); }
    }

    private static async Task SuspendResumeAsync()
    {
        MainWindow window = Prepare();
        TaskCompletionSource<NetworkAdapterDiagnosticPresentation> done = Pending<NetworkAdapterDiagnosticPresentation>();
        int calls = 0;
        CancellationToken token = default;
        try
        {
            Select(window, ApplicationOperationKind.NetworkAdapterDiagnostics);
            window.CollectNetworkAdapterDiagnostics = current =>
            {
                token = current;
                return ++calls == 1 ? done.Task : Task.FromResult(Adapter("fresh-after-resume"));
            };
            Task<bool> read = window.RunNetworkAdapterDiagnosticsAsync();
            Power(window, ObservationPowerTransition.Suspend);
            Ensure(token.IsCancellationRequested, "Suspend must cancel the active diagnostic.");
            Power(window, ObservationPowerTransition.Resume);
            await Controller(window).ProcessPendingAsync();
            Ensure(calls == 1, "Resume must not start another read before the old native call returns.");
            done.SetResult(Adapter("old-before-suspend"));
            await read;
            Ensure(Result(window, ApplicationOperationKind.NetworkAdapterDiagnostics) != "old-before-suspend",
                "Resume must not make the pre-suspend result valid again.");
            await DrainAsync();
            await Controller(window).ProcessPendingAsync();
            Ensure(calls == 2 && Result(window, ApplicationOperationKind.NetworkAdapterDiagnostics) == "fresh-after-resume",
                "A fresh read must run once after real idle.");
        }
        finally { done.TrySetResult(Adapter("cleanup")); window.Close(); }
    }

    private static async Task DeferredCloseAsync()
    {
        foreach (ApplicationOperationKind kind in DiagnosticKinds)
        {
            MainWindow window = Prepare();
            Select(window, kind);
            TaskCompletionSource<LocalDiagnosticText> done = Pending<LocalDiagnosticText>();
            TaskCompletionSource<bool> closed = Pending<bool>();
            CancellationToken token = default;
            window.Closed += (_, _) => closed.TrySetResult(true);
            SetReader(window, kind, current => { token = current; return done.Task; });
            Task<bool> read = Run(window, kind);
            try
            {
                window.Close();
                window.Close();
                Ensure(!closed.Task.IsCompleted && token.IsCancellationRequested && !read.IsCompleted,
                    "Close must remain deferred while the actual diagnostic is unfinished.");
                done.SetResult(new LocalDiagnosticText("late-close-result"));
                await read;
                await closed.Task.WaitAsync(TimeSpan.FromSeconds(3));
                Ensure(!window.CurrentApplicationOperation.IsBusy, "Closed must follow actual lease release.");
            }
            finally { done.TrySetResult(new LocalDiagnosticText("cleanup")); if (!closed.Task.IsCompleted) window.Close(); }
        }
    }

    private static async Task DispatcherOwnershipAsync()
    {
        MainWindow window = Prepare();
        try
        {
            bool readerRejected = await Task.Run(async () =>
            {
                try { await window.RunRouteEvidenceAsync(); }
                catch (InvalidOperationException) { return true; }
                return false;
            });
            DeferredNetworkRefreshController controller = Controller(window);
            bool schedulerRejected = await Task.Run(async () =>
            {
                try { await controller.ProcessPendingAsync(); }
                catch (InvalidOperationException) { return true; }
                return false;
            });
            Ensure(readerRejected && schedulerRejected && !window.CurrentApplicationOperation.IsBusy,
                "Worker callers must not mutate WPF or acquire orphan leases.");
        }
        finally { window.Close(); }
    }

    private static async Task ClosedWindowAsync()
    {
        MainWindow window = Prepare();
        DeferredNetworkRefreshController controller = Controller(window);
        int calls = 0;
        window.CollectNetworkAdapterDiagnostics = _ => { calls++; return Task.FromResult(Adapter("unexpected")); };
        controller.RequestRefresh();
        window.Close();
        await Task.Run(() => { controller.RequestRefresh(); controller.StateMayHaveChanged(); });
        await DrainAsync();
        await controller.ProcessPendingAsync();
        foreach (ApplicationOperationKind kind in DiagnosticKinds) Ensure(!await Run(window, kind), "Closed reader must reject start.");
        window.EnsureNetworkAdapterDiagnosticsTab();
        window.EnsureNetworkAdapterChangeMonitor();
        Ensure(calls == 0 && !controller.IsTimerEnabled && !controller.HasPendingRequest,
            "Close must suppress queued callbacks and must not re-register monitoring.");
    }

    private static async Task AutomaticMatrixAsync()
    {
        MainWindow window = Prepare();
        DeferredNetworkRefreshController controller = Controller(window);
        int calls = 0;
        window.CollectNetworkAdapterDiagnostics = _ => { calls++; return Task.FromResult(Adapter("auto")); };
        try
        {
            foreach (ApplicationOperationKind owner in Enum.GetValues<ApplicationOperationKind>().Where(k => k != ApplicationOperationKind.None))
            {
                int before = calls;
                ApplicationOperationLease active = Core(window).TryBegin(owner).Lease!;
                controller.Resume();
                controller.RequestRefresh();
                NotifyStaleIdle(window);
                await DrainAsync();
                await controller.ProcessPendingAsync();
                Ensure(calls == before && controller.HasPendingRequest && !controller.IsTimerEnabled,
                    "Stale idle notifications must not override the current owner.");
                active.Dispose();
                await DrainAsync();
                await controller.ProcessPendingAsync();
                Ensure(calls == before + 1 && !controller.HasPendingRequest,
                    "Every global kind must release one pending automatic refresh on completion.");
                controller.Suspend();
            }
        }
        finally { window.Close(); }
    }

    private static async Task BurstAsync()
    {
        int calls = 0;
        using DeferredNetworkRefreshController controller = new(Dispatcher.CurrentDispatcher,
            () => true, () => { calls++; return Task.FromResult(true); }, _ => { });
        await Task.Run(() => Parallel.For(0, 1000, _ => controller.RequestRefresh()));
        await DrainAsync();
        await controller.ProcessPendingAsync();
        await DrainAsync();
        Ensure(calls == 1 && !controller.HasPendingRequest && !controller.IsTimerEnabled,
            "A worker event burst must cause one read, not one queued read per event.");
    }

    private static async Task FollowUpAsync()
    {
        MainWindow window = Prepare();
        DeferredNetworkRefreshController controller = Controller(window);
        TaskCompletionSource<NetworkAdapterDiagnosticPresentation> done = Pending<NetworkAdapterDiagnosticPresentation>();
        int calls = 0;
        window.CollectNetworkAdapterDiagnostics = _ => ++calls == 1 ? done.Task : Task.FromResult(Adapter("follow-up"));
        try
        {
            controller.Resume();
            Task first = controller.ProcessPendingAsync();
            Ensure(calls == 1 && controller.IsRunning, "First refresh must hold its lease.");
            await Task.Run(() => Parallel.For(0, 1000, _ => controller.RequestRefresh()));
            await DrainAsync();
            await controller.ProcessPendingAsync();
            Ensure(calls == 1, "No second refresh may overlap the first.");
            done.SetResult(Adapter("first"));
            await first;
            await controller.ProcessPendingAsync();
            await DrainAsync();
            Ensure(calls == 2 && !controller.HasPendingRequest,
                "Requests during a refresh must coalesce into exactly one later refresh.");
        }
        finally { done.TrySetResult(Adapter("cleanup")); window.Close(); }
    }

    private static async Task RejectedRefreshAsync()
    {
        int calls = 0;
        using DeferredNetworkRefreshController controller = new(Dispatcher.CurrentDispatcher,
            () => true, () => { calls++; return Task.FromResult(false); }, _ => { });
        controller.RequestRefresh();
        await DrainAsync();
        await controller.ProcessPendingAsync();
        await DrainAsync();
        Ensure(calls == 1 && controller.HasPendingRequest && !controller.IsTimerEnabled,
            "A denied lease must remain pending without an endless timer retry loop.");
        controller.StateMayHaveChanged();
        await DrainAsync();
        Ensure(controller.IsTimerEnabled, "A new state event may schedule another attempt.");
    }

    private static async Task FailedRefreshAsync()
    {
        int calls = 0;
        string failure = string.Empty;
        using DeferredNetworkRefreshController controller = new(Dispatcher.CurrentDispatcher,
            () => true, () => ++calls == 1 ? Task.FromException<bool>(new IOException("secret")) : Task.FromResult(true),
            type => failure = type);
        controller.RequestRefresh();
        await DrainAsync();
        await controller.ProcessPendingAsync();
        await controller.ProcessPendingAsync();
        Ensure(calls == 1 && failure == nameof(IOException) && !controller.IsTimerEnabled,
            "A fault must expose only its type and not self-retry.");
        controller.RequestRefresh();
        await DrainAsync();
        await controller.ProcessPendingAsync();
        Ensure(calls == 2 && !controller.HasPendingRequest, "A new event must allow recovery.");
    }

    private static async Task ReadinessAndShutdownAsync()
    {
        MainWindow window = Prepare();
        DeferredNetworkRefreshController controller = Controller(window);
        int calls = 0;
        window.CollectNetworkAdapterDiagnostics = _ => { calls++; return Task.FromResult(Adapter("after-shutdown-veto")); };
        try
        {
            await Core(window).RequestShutdownAsync();
            controller.Resume();
            controller.RequestRefresh();
            await DrainAsync();
            await controller.ProcessPendingAsync();
            Ensure(calls == 0 && controller.HasPendingRequest && !controller.IsTimerEnabled,
                "Idle with shutdown requested is not available for refresh.");
            Core(window).CancelShutdownRequest();
            await DrainAsync();
            await controller.ProcessPendingAsync();
            Ensure(calls == 1, "Canceled shutdown must release a retained request.");
        }
        finally { window.Close(); }
        bool ready = false;
        int readyCalls = 0;
        using DeferredNetworkRefreshController deferred = new(Dispatcher.CurrentDispatcher,
            () => ready, () => { readyCalls++; return Task.FromResult(true); }, _ => { });
        deferred.RequestRefresh();
        await DrainAsync();
        await deferred.ProcessPendingAsync();
        Ensure(readyCalls == 0 && deferred.HasPendingRequest, "Missing UI readiness must retain the request.");
        ready = true;
        deferred.StateMayHaveChanged();
        await DrainAsync();
        await deferred.ProcessPendingAsync();
        Ensure(readyCalls == 1, "Late UI registration must allow one refresh.");
    }

    private static async Task DisposedSchedulerAsync()
    {
        TaskCompletionSource<bool> done = Pending<bool>();
        int calls = 0;
        DeferredNetworkRefreshController controller = new(Dispatcher.CurrentDispatcher,
            () => true, () => { calls++; return done.Task; }, _ => throw new InvalidOperationException());
        controller.RequestRefresh();
        await DrainAsync();
        Task active = controller.ProcessPendingAsync();
        controller.RequestRefresh();
        controller.Dispose();
        done.SetResult(true);
        await active;
        await DrainAsync();
        controller.Resume();
        controller.RequestRefresh();
        await controller.ProcessPendingAsync();
        Ensure(calls == 1 && !controller.IsRunning && !controller.IsTimerEnabled && !controller.HasPendingRequest,
            "Disposal must not release work early or schedule any new callback afterward.");
    }
}
