using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using WlanLivePathTester.App;
using WlanLivePathTester.Core.Operations;
using static WlanLivePathTester.RouteUiLifetimeSmoke.TestContext;

namespace WlanLivePathTester.RouteUiLifetimeSmoke;

internal static class ReportTests
{
    internal static readonly (string Name, Func<Task> Run)[] Cases =
    [
        ("actual route report button uses existing writer and shared UI lease", ActualWriterAsync),
        ("report cancellation before TryStart invokes no writer", PreStartCancellationAsync),
        ("report stop is once and holds ownership through real return", StopAsync),
        ("cancellation after file-set commit preserves success", CommittedAsync),
        ("report lease also drains asynchronous cancellation callbacks", CallbackDrainAsync),
        ("close-time report failure keeps previous export and permits retry", FailureCloseAsync),
        ("callback failure keeps window open without exposing payload", CallbackFailureAsync),
        ("successful close waits for the committed writer", SuccessfulCloseAsync),
        ("report lock restores late tabs and existing bindings", ReportBindingsAsync)
    ];

    private static void Wait(ManualResetEventSlim gate)
    {
        if (!gate.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("Synthetic writer gate timed out.");
    }

    private static async Task ActualWriterAsync()
    {
        MainWindow window = Prepare("경로 보고서");
        using Output output = new();
        int calls = 0;
        Set(window, "_latestRouteComparisonRunV3", Run());
        window.SaveRouteReportOverride = (_, token) => { Interlocked.Increment(ref calls); return output.Write(token); };
        try
        {
            Click(window, "_routeComparisonReportGenerateV2");
            await UntilAsync(() => calls == 1 && !window.CurrentApplicationOperation.IsBusy);
            string html = Field<string>(window, "_latestRouteComparisonReportHtmlV2");
            Ensure(File.Exists(html) && ReportText(window).Contains("저장 완료", StringComparison.Ordinal),
                "Actual report click must publish the existing writer's file set.");
            Ensure(!ReportText(window).Contains(output.DirectoryPath, StringComparison.Ordinal), "Do not reflect full user path in completion text.");
            Idle(window);
        }
        finally { window.Close(); }
    }

    private static async Task PreStartCancellationAsync()
    {
        MainWindow window = Prepare("경로 보고서");
        bool canceled = false;
        int calls = 0;
        EventHandler<ApplicationOperationStateChangedEventArgs> observer = (_, e) =>
        {
            if (e.Snapshot.Kind == ApplicationOperationKind.RouteComparisonReportSave && !canceled)
            {
                canceled = true;
                Coordinator(window).RequestCancellation();
            }
        };
        Coordinator(window).StateChanged += observer;
        try
        {
            bool saved = await window.RunRouteReportSaveAsync(_ => { calls++; throw new InvalidOperationException(); });
            Ensure(canceled && !saved && calls == 0, "Cancellation before TryStart must not be lost.");
            Idle(window);
        }
        finally { Coordinator(window).StateChanged -= observer; window.Close(); }
    }

    private static async Task StopAsync()
    {
        MainWindow window = Prepare("경로 보고서");
        using Output output = new();
        using ManualResetEventSlim release = new();
        var entered = Pending<bool>();
        int cancellations = 0;
        Task<bool> active = window.RunRouteReportSaveAsync(token =>
        {
            using var registration = token.Register(() => Interlocked.Increment(ref cancellations));
            entered.TrySetResult(true);
            Wait(release);
            token.ThrowIfCancellationRequested();
            return output.Write(token);
        });
        try
        {
            await entered.Task;
            Click(window, "_routeComparisonReportCancelV2");
            Click(window, "_routeComparisonReportCancelV2");
            await UntilAsync(() => cancellations == 1);
            Ensure(!active.IsCompleted && window.CurrentApplicationOperation.IsBusy, "Do not free a canceled writer early.");
            release.Set();
            Ensure(!await active && cancellations == 1 && !Directory.Exists(output.DirectoryPath),
                "Canceled precommit writer must not publish files or report success.");
            Idle(window);
        }
        finally { release.Set(); await active; window.Close(); }
    }

    private static async Task CommittedAsync()
    {
        MainWindow window = Prepare("경로 보고서");
        using Output output = new();
        using ManualResetEventSlim release = new();
        var committed = Pending<bool>();
        Task<bool> active = window.RunRouteReportSaveAsync(token =>
        {
            var saved = output.Write(token);
            committed.TrySetResult(true);
            Wait(release);
            return saved;
        });
        try
        {
            await committed.Task;
            Click(window, "_routeComparisonReportCancelV2");
            Ensure(!active.IsCompleted, "Published files do not mean the writer has returned yet.");
            release.Set();
            Ensure(await active && ReportText(window).Contains("저장 완료", StringComparison.Ordinal),
                "A committed file set must not become canceled after a late stop request.");
            Ensure(File.Exists(Field<string>(window, "_latestRouteComparisonReportHtmlV2")), "Keep committed output.");
            Idle(window);
        }
        finally { release.Set(); await active; window.Close(); }
    }

    private static async Task CallbackDrainAsync()
    {
        MainWindow window = Prepare("경로 보고서");
        using Output output = new();
        using ManualResetEventSlim writerRelease = new();
        using ManualResetEventSlim callbackRelease = new();
        var entered = Pending<bool>();
        var callbackStarted = Pending<bool>();
        var writerReturned = Pending<bool>();
        Task<bool> active = window.RunRouteReportSaveAsync(token =>
        {
            var saved = output.Write(token);
            token.Register(() => { callbackStarted.TrySetResult(true); Wait(callbackRelease); });
            entered.TrySetResult(true);
            Wait(writerRelease);
            writerReturned.TrySetResult(true);
            return saved;
        });
        try
        {
            await entered.Task;
            Click(window, "_routeComparisonReportCancelV2");
            await callbackStarted.Task;
            writerRelease.Set();
            await writerReturned.Task;
            await DrainAsync();
            Ensure(!active.IsCompleted && window.CurrentApplicationOperation.IsBusy,
                "Core lease must retain ReportSaveSession's asynchronous cancellation callback lifetime.");
            callbackRelease.Set();
            Ensure(await active, "Already committed output remains successful after callback join.");
        }
        finally { writerRelease.Set(); callbackRelease.Set(); await active; window.Close(); }
    }

    private static async Task FailureCloseAsync()
    {
        MainWindow window = Prepare("경로 보고서");
        using Output output = new();
        await window.RunRouteReportSaveAsync(token => output.Write(token));
        string oldPath = Field<string>(window, "_latestRouteComparisonReportHtmlV2");
        using ManualResetEventSlim release = new();
        var entered = Pending<bool>();
        bool closed = false;
        window.Closed += (_, _) => closed = true;
        Task<bool> active = window.RunRouteReportSaveAsync(_ =>
        {
            entered.TrySetResult(true);
            Wait(release);
            throw new IOException(Secret);
        });
        try
        {
            await entered.Task;
            window.Close();
            Ensure(!closed && !active.IsCompleted, "Close must await the actual failing writer.");
            release.Set();
            Ensure(!await active, "Failed writer must not become success.");
            await UntilAsync(() => !Field<bool>(window, "_applicationOperationClosePending"));
            Ensure(!closed && !window.CurrentApplicationOperation.ShutdownRequested,
                "Keep the window for error review and reopen admission.");
            Ensure(Field<string>(window, "_latestRouteComparisonReportHtmlV2") == oldPath
                && File.Exists(oldPath) && !ReportText(window).Contains(Secret, StringComparison.Ordinal),
                "Keep previous export and hide raw error payload.");
            Ensure(await window.RunRouteReportSaveAsync(token => output.Write(token)), "A subsequent retry should succeed.");
            window.Close();
            Ensure(closed, "A later user close must remain possible.");
        }
        finally { release.Set(); await active; if (!closed) window.Close(); }
    }

    private static async Task CallbackFailureAsync()
    {
        MainWindow window = Prepare("경로 보고서");
        using Output output = new();
        using ManualResetEventSlim release = new();
        var entered = Pending<bool>();
        bool closed = false;
        window.Closed += (_, _) => closed = true;
        Task<bool> active = window.RunRouteReportSaveAsync(token =>
        {
            var saved = output.Write(token);
            token.Register(() => throw new IOException(Secret));
            entered.TrySetResult(true);
            Wait(release);
            return saved;
        });
        try
        {
            await entered.Task;
            window.Close();
            release.Set();
            Ensure(await active, "Callback failure does not erase a committed file set.");
            await UntilAsync(() => !Field<bool>(window, "_applicationOperationClosePending"));
            Ensure(!closed && ReportText(window).Contains("콜백", StringComparison.Ordinal)
                && !ReportText(window).Contains(Secret, StringComparison.Ordinal),
                "A failed cancellation callback must remain visible for review without raw error text.");
            window.Close();
            Ensure(closed, "Review must not permanently veto closing.");
        }
        finally { release.Set(); await active; if (!closed) window.Close(); }
    }

    private static async Task SuccessfulCloseAsync()
    {
        MainWindow window = Prepare("경로 보고서");
        using Output output = new();
        using ManualResetEventSlim release = new();
        var entered = Pending<bool>();
        var closed = Pending<bool>();
        window.Closed += (_, _) => closed.TrySetResult(true);
        Task<bool> active = window.RunRouteReportSaveAsync(token =>
        {
            var saved = output.Write(token);
            entered.TrySetResult(true);
            Wait(release);
            return saved;
        });
        try
        {
            await entered.Task;
            window.Close();
            Ensure(!closed.Task.IsCompleted && !active.IsCompleted, "No early close while writer is still running.");
            release.Set();
            Ensure(await active, "Committed save should succeed.");
            await closed.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Ensure(!window.CurrentApplicationOperation.IsBusy, "Close must follow lease cleanup.");
        }
        finally { release.Set(); await active; if (!closed.Task.IsCompleted) window.Close(); }
    }

    private static async Task ReportBindingsAsync()
    {
        MainWindow window = Prepare("경로 보고서");
        using Output output = new();
        using ManualResetEventSlim release = new();
        var entered = Pending<bool>();
        TabItem peer = new() { Header = "bound-report-peer" };
        CheckBox source = new() { IsChecked = true };
        BindingOperations.SetBinding(peer, UIElement.IsEnabledProperty, new Binding(nameof(CheckBox.IsChecked)) { Source = source });
        Tabs(window).Items.Add(peer);
        Task<bool> active = window.RunRouteReportSaveAsync(token =>
        {
            entered.TrySetResult(true);
            Wait(release);
            return output.Write(token);
        });
        try
        {
            await entered.Task;
            TabItem late = new();
            Tabs(window).Items.Add(late);
            source.IsChecked = false;
            source.IsChecked = true;
            await DrainAsync();
            Ensure(!peer.IsEnabled && !late.IsEnabled, "Route report must use common peer tab locks.");
            source.IsChecked = false;
            release.Set();
            await active;
            await DrainAsync();
            Ensure(BindingOperations.IsDataBound(peer, UIElement.IsEnabledProperty) && !peer.IsEnabled && late.IsEnabled,
                "Report cleanup restores live binding, not a stale Boolean.");
        }
        finally { release.Set(); await active; window.Close(); }
    }
}
