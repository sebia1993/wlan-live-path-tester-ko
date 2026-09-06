using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using WlanLivePathTester.App;
using WlanLivePathTester.Core.Measurements;
using WlanLivePathTester.Core.Models;
using WlanLivePathTester.Core.Operations;

namespace WlanLivePathTester.RepeatedUiSmoke;

internal static class Program
{
    private static readonly RepeatedMeasurementPlan Plan = new(1, false, 0);
    private static readonly BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    [STAThread]
    private static int Main()
    {
        Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
        Task tests = RunAsync().WaitAsync(TimeSpan.FromSeconds(60));
        _ = tests.ContinueWith(_ => dispatcher.BeginInvokeShutdown(DispatcherPriority.Normal),
            CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
        Dispatcher.Run();
        try
        {
            tests.GetAwaiter().GetResult();
            Console.WriteLine("Repeated WPF smoke: 11 groups, 0 failures; injected runners only.");
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
        (string Name, Func<Task> Test)[] groups =
        [
            ("snapshot target plan and preflight", SnapshotAndHappyPathAsync),
            ("shared lease rejects reentry", ReentryAsync),
            ("real stop button waits for return", StopButtonAsync),
            ("queued progress cannot overwrite final result", QueuedFinalProgressAsync),
            ("previous target cannot repaint next target", CrossTargetProgressAsync),
            ("previous run cannot repaint next run", CrossRunProgressAsync),
            ("failure retains earlier evidence and hides exception", FailureRecoveryAsync),
            ("window close drains repeated work", DeferredCloseAsync),
            ("invalid plans and budgets never run", InvalidInputAsync),
            ("runner canceled result stops following targets", CanceledResultAsync),
            ("single tab registration and closed window", ClosedAndRegistrationAsync)
        ];
        foreach ((string name, Func<Task> test) in groups)
        {
            await test().WaitAsync(TimeSpan.FromSeconds(5));
            Console.WriteLine($"PASS {name}");
        }
    }

    private static async Task SnapshotAndHappyPathAsync()
    {
        MainWindow window = Prepare();
        try
        {
            MeasurementTargetDefinition[] targets = [Target("first"), Target("second")];
            List<string> observed = [];
            RepeatedMeasurementPlan warmup = new(2, true, 500);
            window.MeasurementHeadPreflightCheckBox.IsChecked = true;
            await window.RunRepeatedMeasurementSessionAsync(targets, warmup,
                (target, plan, head, _, _) =>
                {
                    Ensure(window.CurrentApplicationOperation.Kind == ApplicationOperationKind.RepeatedMeasurement,
                        "Repeated work must have its own fixed kind on the global coordinator.");
                    Ensure(head && plan == warmup, "Options must be captured before work begins.");
                    Ensure(!Field<ComboBox>(window, "_repeatCountComboBox").IsEnabled
                        && !Field<CheckBox>(window, "_repeatWarmupCheckBox").IsEnabled
                        && !Field<TextBox>(window, "_repeatDelayTextBox").IsEnabled,
                        "Repeat settings must stay locked during execution.");
                    observed.Add(target.Name);
                    targets[1] = Target("replacement-must-not-run");
                    window.MeasurementHeadPreflightCheckBox.IsChecked = false;
                    return Task.FromResult(Result(target, plan));
                });
            Ensure(observed.SequenceEqual(["first", "second"]), "Use a captured target array in order.");
            Ensure(Text(window).Contains("결과 확보 대상: 2/2", StringComparison.Ordinal)
                && Text(window).Contains("미시작 대상: 0", StringComparison.Ordinal),
                "Final output must distinguish collected results from unstarted targets.");
            AssertIdle(window);
        }
        finally { window.Close(); }
    }

    private static async Task ReentryAsync()
    {
        MainWindow window = Prepare();
        TaskCompletionSource<RepeatedMeasurementResult> done = Pending();
        int calls = 0;
        try
        {
            Task first = window.RunRepeatedMeasurementSessionAsync([Target()], Plan,
                (_, _, _, _, _) => { calls++; return done.Task; });
            await window.RunRepeatedMeasurementSessionAsync([Target()], Plan,
                (target, plan, _, _, _) => { calls++; return Task.FromResult(Result(target, plan)); });
            Task other = (Task)typeof(MainWindow).GetMethod("RunMeasurementOperationAsync", PrivateInstance)!
                .Invoke(window, new object[]
                {
                    new Func<CancellationToken, Task>(_ => { calls++; return Task.CompletedTask; }), "synthetic"
                })!;
            await other;
            Ensure(calls == 1 && window.CurrentApplicationOperation.IsBusy,
                "Neither repeated reentry nor a single download may call its runner.");
            done.SetResult(Result(Target(), Plan));
            await first;
            AssertIdle(window);
        }
        finally { done.TrySetResult(Result(Target(), Plan)); window.Close(); }
    }

    private static async Task StopButtonAsync()
    {
        MainWindow window = Prepare();
        TaskCompletionSource<RepeatedMeasurementResult> done = Pending();
        CancellationToken token = default;
        int cancellations = 0;
        int calls = 0;
        CancellationTokenRegistration registration = default;
        try
        {
            Task operation = window.RunRepeatedMeasurementSessionAsync([Target(), Target("unstarted")], Plan,
                (_, _, _, _, current) =>
                {
                    calls++;
                    token = current;
                    registration = token.Register(() => Interlocked.Increment(ref cancellations));
                    return done.Task;
                });
            Button stop = Field<Button>(window, "_repeatCancelButton");
            Ensure(stop.IsEnabled, "Stop must be available in the still-enabled repeated tab.");
            stop.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            stop.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Ensure(cancellations == 1 && token.IsCancellationRequested && !stop.IsEnabled,
                "Repeated stop must forward one cancellation through the active lease.");
            Ensure(!operation.IsCompleted && window.CurrentApplicationOperation.IsBusy,
                "Cancellation must not pretend an unfinished native call has returned.");
            done.SetResult(Result(Target(), Plan));
            await operation;
            Ensure(calls == 1 && Text(window).Contains("취소됨", StringComparison.Ordinal)
                && Text(window).Contains("결과 확보 대상: 1/2", StringComparison.Ordinal)
                && Text(window).Contains("미시작 대상: 1", StringComparison.Ordinal),
                "Retain the returned evidence as canceled and do not start the next target.");
            AssertIdle(window);
        }
        finally { registration.Dispose(); done.TrySetResult(Result(Target(), Plan)); window.Close(); }
    }

    private static async Task QueuedFinalProgressAsync()
    {
        MainWindow window = Prepare();
        try
        {
            await window.RunRepeatedMeasurementSessionAsync([Target()], Plan,
                (target, plan, _, progress, _) =>
                {
                    progress!.Report(Progress("stale-final-message"));
                    return Task.FromResult(Result(target, plan));
                });
            string final = Text(window);
            await DrainAsync();
            Ensure(Text(window) == final && !final.Contains("stale-final-message", StringComparison.Ordinal),
                "A synchronous runner's queued progress must not replace the final output.");
        }
        finally { window.Close(); }
    }

    private static async Task CrossTargetProgressAsync()
    {
        MainWindow window = Prepare();
        IProgress<RepeatedMeasurementProgress>? oldProgress = null;
        IProgress<RepeatedMeasurementProgress>? currentProgress = null;
        TaskCompletionSource<RepeatedMeasurementResult> done = Pending();
        int calls = 0;
        try
        {
            Task operation = window.RunRepeatedMeasurementSessionAsync([Target("first"), Target("second")], Plan,
                (target, plan, _, progress, _) =>
                {
                    calls++;
                    if (calls == 1)
                    {
                        oldProgress = progress;
                        return Task.FromResult(Result(target, plan));
                    }
                    currentProgress = progress;
                    return done.Task;
                });
            Ensure(calls == 2, "The synthetic first target should already have completed.");
            string before = Text(window);
            oldProgress!.Report(Progress("old-target"));
            await DrainAsync();
            Ensure(Text(window) == before, "Old target progress must not be relabeled as the next target.");
            currentProgress!.Report(Progress("current-target"));
            await DrainAsync();
            Ensure(Text(window).Contains("대상: 2/2", StringComparison.Ordinal)
                && Text(window).Contains("current-target", StringComparison.Ordinal),
                "Current progress must use its captured one-based target ordinal.");
            done.SetResult(Result(Target("second"), Plan));
            await operation;
        }
        finally { done.TrySetResult(Result(Target("second"), Plan)); window.Close(); }
    }

    private static async Task CrossRunProgressAsync()
    {
        MainWindow window = Prepare();
        IProgress<RepeatedMeasurementProgress>? old = null;
        TaskCompletionSource<RepeatedMeasurementResult> done = Pending();
        try
        {
            await window.RunRepeatedMeasurementSessionAsync([Target()], Plan,
                (target, plan, _, progress, _) => { old = progress; return Task.FromResult(Result(target, plan)); });
            Task next = window.RunRepeatedMeasurementSessionAsync([Target("next")], Plan,
                (_, _, _, _, _) => done.Task);
            long? nextId = window.CurrentApplicationOperation.OperationId;
            string before = Text(window);
            old!.Report(Progress("old-run"));
            await DrainAsync();
            Ensure(Text(window) == before && window.CurrentApplicationOperation.OperationId == nextId,
                "Previous-run notifications must not change the new run or its UI.");
            done.SetResult(Result(Target("next"), Plan));
            await next;
        }
        finally { done.TrySetResult(Result(Target("next"), Plan)); window.Close(); }
    }

    private static async Task FailureRecoveryAsync()
    {
        const string secret = "https://private-proxy.example.invalid/credential";
        MainWindow window = Prepare();
        int calls = 0;
        try
        {
            await window.RunRepeatedMeasurementSessionAsync([Target("first"), Target("second"), Target("third")], Plan,
                (target, plan, _, _, _) => ++calls == 1
                    ? Task.FromResult(Result(target, plan))
                    : throw new InvalidOperationException(secret));
            Ensure(calls == 2 && Text(window).Contains("결과 확보 대상: 1/3", StringComparison.Ordinal)
                && Text(window).Contains("미시작 대상: 1", StringComparison.Ordinal)
                && Text(window).Contains("first", StringComparison.Ordinal),
                "Failure must retain completed earlier targets and not attempt later targets.");
            Ensure(!Text(window).Contains(secret, StringComparison.Ordinal)
                && !window.MeasurementStatusText.Text.Contains(secret, StringComparison.Ordinal),
                "Neither repeated nor shared status may reflect raw exception text.");
            AssertIdle(window);
        }
        finally { window.Close(); }
    }

    private static async Task DeferredCloseAsync()
    {
        MainWindow window = Prepare();
        TaskCompletionSource<RepeatedMeasurementResult> done = Pending();
        TaskCompletionSource closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken token = default;
        window.Closed += (_, _) => closed.TrySetResult();
        Task operation = window.RunRepeatedMeasurementSessionAsync([Target()], Plan,
            (_, _, _, _, current) => { token = current; return done.Task; });
        try
        {
            window.Close();
            Ensure(!closed.Task.IsCompleted && token.IsCancellationRequested && !operation.IsCompleted,
                "Close must request cancellation and leave unfinished work alive.");
            done.SetResult(Result(Target(), Plan));
            await operation;
            await closed.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Ensure(!window.CurrentApplicationOperation.IsBusy, "Close must follow actual lease release.");
        }
        finally { done.TrySetResult(Result(Target(), Plan)); if (!closed.Task.IsCompleted) window.Close(); }
    }

    private static async Task InvalidInputAsync()
    {
        MainWindow window = Prepare();
        int calls = 0;
        RepeatedTargetRunner runner = (target, plan, _, _, _) =>
        {
            calls++;
            return Task.FromResult(Result(target, plan));
        };
        try
        {
            await window.RunRepeatedMeasurementSessionAsync([], Plan, runner);
            await window.RunRepeatedMeasurementSessionAsync(Enumerable.Range(0, 5).Select(i => Target(i.ToString())).ToArray(), Plan, runner);
            await window.RunRepeatedMeasurementSessionAsync([Target() with { MaxBytes = long.MaxValue }], new(5, true, 0), runner);
            await window.RunRepeatedMeasurementSessionAsync([Target() with { MaxBytes = 0 }], Plan, runner);
            await window.RunRepeatedMeasurementSessionAsync([Target()], new(6, false, 0), runner);
            await window.RunRepeatedMeasurementSessionAsync(
                [Target() with { MaxBytes = 1536L * 1024 * 1024 }, Target() with { MaxBytes = 1536L * 1024 * 1024 }], Plan, runner);
            Ensure(calls == 0 && !window.CurrentApplicationOperation.IsBusy,
                "Malformed counts, plans, nonpositive bytes, overflow and combined >2GiB must execute nothing.");
        }
        finally { window.Close(); }
    }

    private static async Task CanceledResultAsync()
    {
        MainWindow window = Prepare();
        int calls = 0;
        try
        {
            await window.RunRepeatedMeasurementSessionAsync([Target(), Target("never")], Plan,
                (target, plan, _, _, _) =>
                {
                    calls++;
                    return Task.FromResult(Result(target, plan, MeasurementStatus.Canceled));
                });
            Ensure(calls == 1 && Text(window).Contains("취소됨", StringComparison.Ordinal),
                "A runner's canceled result must stop the outer target loop as well.");
            AssertIdle(window);
        }
        finally { window.Close(); }
    }

    private static async Task ClosedAndRegistrationAsync()
    {
        MainWindow window = Prepare();
        TabControl host = Tabs(window);
        window.EnsureRepeatedMeasurementTab();
        Ensure(host.Items.OfType<TabItem>().Count(tab => Equals(tab.Header, "반복 측정")) == 1,
            "Repeated activation must not register a duplicate tab.");
        window.Close();
        int calls = 0;
        await window.RunRepeatedMeasurementSessionAsync([Target()], Plan,
            (target, plan, _, _, _) => { calls++; return Task.FromResult(Result(target, plan)); });
        Ensure(calls == 0, "Closed windows must not start a new repeated runner.");
    }

    private static MainWindow Prepare()
    {
        // Never Show(): no Loaded/Activated native inventory or network readers.
        MainWindow window = new();
        FrameworkElement root = (FrameworkElement)window.Content;
        root.Measure(new Size(1400, 1000));
        root.Arrange(new Rect(0, 0, 1400, 1000));
        root.UpdateLayout();
        window.EnsureRepeatedMeasurementTab();
        Tabs(window).SelectedItem = Tabs(window).Items.OfType<TabItem>()
            .Single(tab => Equals(tab.Header, "반복 측정"));
        return window;
    }

    private static TabControl Tabs(MainWindow window) => Find<TabControl>((DependencyObject)window.Content)
        ?? throw new InvalidOperationException("Missing real MainWindow tab host.");
    private static T? Find<T>(DependencyObject item) where T : DependencyObject
    {
        if (item is T found) return found;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(item); i++)
        {
            T? child = Find<T>(VisualTreeHelper.GetChild(item, i));
            if (child is not null) return child;
        }
        return null;
    }
    private static T Field<T>(MainWindow window, string name) =>
        (T)(typeof(MainWindow).GetField(name, PrivateInstance)!.GetValue(window)
            ?? throw new InvalidOperationException($"Missing field {name}."));
    private static string Text(MainWindow window) => Field<TextBlock>(window, "_repeatResultText").Text;
    private static async Task DrainAsync() => await Dispatcher.Yield(DispatcherPriority.Background);
    private static TaskCompletionSource<RepeatedMeasurementResult> Pending() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static void AssertIdle(MainWindow window)
    {
        Ensure(!window.CurrentApplicationOperation.IsBusy
            && Field<Button>(window, "_repeatInternalButton").IsEnabled
            && Field<Button>(window, "_repeatExternalButton").IsEnabled
            && !Field<Button>(window, "_repeatCancelButton").IsEnabled
            && Field<TextBox>(window, "_repeatDelayTextBox").IsEnabled,
            "Both success and failure must restore repeat controls and release the global lease.");
    }
    private static MeasurementTargetDefinition Target(string name = "synthetic") => new(
        Name: name, Url: "https://measurement.example.invalid/file.bin", PathKind: NetworkPathKind.External,
        RequireProxy: true, RequireDirect: false, MaxBytes: 1024 * 1024, TimeoutSeconds: 5, Streams: 1, MaxRedirects: 0);
    private static RepeatedMeasurementProgress Progress(string message) => new(0, 1, 1, false, message, null);
    private static RepeatedMeasurementResult Result(
        MeasurementTargetDefinition target, RepeatedMeasurementPlan plan,
        MeasurementStatus status = MeasurementStatus.Success)
    {
        DownloadMeasurementResult download = new(target.Name, target.PathKind, status,
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddSeconds(1), 1024 * 1024,
            8.388608, TimeSpan.FromMilliseconds(5), 200, true, 1, 1, 0,
            target.Url, [], new Dictionary<string, string>(), null, "synthetic");
        RepeatedMeasurementRun[] runs = Enumerable.Range(0, plan.TotalRunCount)
            .Select(index => new RepeatedMeasurementRun(
                plan.IncludeWarmup ? index : index + 1,
                plan.IncludeWarmup && index == 0, download)).ToArray();
        return new(target.Name, target.PathKind, DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch.AddSeconds(plan.TotalRunCount), plan, runs,
            RepeatedMeasurementAggregator.Summarize(plan, runs));
    }
    private static void Ensure(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
