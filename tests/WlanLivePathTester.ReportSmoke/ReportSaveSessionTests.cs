using System.Runtime.CompilerServices;
using WlanLivePathTester.Core.Reporting;

namespace WlanLivePathTester.ReportSmoke;

internal static class ReportSaveSessionTests
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(15);
#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Run()
    {
        RunAsync().GetAwaiter().GetResult();
        Console.WriteLine("PASS report save session: 12 cancellation/lifetime scenarios");
    }

    private static async Task RunAsync()
    {
        await IdleCancellationDoesNotPoisonNextSave();
        await BusySessionRejectsSecondDelegate();
        await SaveExceptionRemainsObservable();
        await CooperativeCancellationAllowsRestart();
        await SuccessfulCommitSurvivesLateCancel();
        await CompletionWaitsForCancellationCallbacks();
        await CallbackFailureIsFlaggedWithoutReplacingSaveSuccess();
        await CloseDrainsAndRejectsNewSaves();
        await CloseObservesFailureWithoutHidingOriginalTask();
        await ConcurrentStartsAdmitOnlyOne();
        await CancelAndCompleteRacesDoNotDisposeLiveSources();
        await RecoveryErrorIsNotReportedAsCancellation();
    }

    private static async Task IdleCancellationDoesNotPoisonNextSave()
    {
        ReportSaveSession session = new();
        session.RequestCancellation();
        await session.CancelAndWaitAsync();
        Ensure(session.State == ReportSaveSessionState.Idle, "Idle cancel must remain idle.");
        Ensure(await Start(session, token => token.IsCancellationRequested ? -1 : 42) == 42,
            "The next save needs a fresh uncanceled token.");
        Ensure(!session.IsBusy, "Completion must release the session.");
        await session.CloseAsync();
    }

    private static async Task BusySessionRejectsSecondDelegate()
    {
        ReportSaveSession session = new();
        using ManualResetEventSlim entered = new();
        using ManualResetEventSlim release = new();
        Task<int> first = Start(session, token => { entered.Set(); Wait(release); return 1; });
        try
        {
            Wait(entered);
            int calls = 0;
            Ensure(!session.TryStart(token => Interlocked.Increment(ref calls), out Task<int>? rejected)
                && rejected is null && calls == 0, "Rejected delegate must not run.");
            Ensure(session.State == ReportSaveSessionState.Running, "Expected running state.");
        }
        finally { release.Set(); }
        Ensure(await Bounded(first) == 1, "First save result was lost.");
        await session.CloseAsync();
    }

    private static async Task SaveExceptionRemainsObservable()
    {
        ReportSaveSession session = new();
        IOException expected = new("synthetic-private-error");
        Task<int> task = Start<int>(session, token => throw expected);
        try { await Bounded(task); throw new InvalidOperationException("Missing save failure."); }
        catch (IOException actual) { Ensure(ReferenceEquals(actual, expected), "Preserve original save failure."); }
        Ensure(!session.IsBusy, "Failed save must release the session.");
        Ensure(await Bounded(Start(session, token => 7)) == 7, "Retry after failure must be possible.");
    }

    private static async Task CooperativeCancellationAllowsRestart()
    {
        ReportSaveSession session = new();
        using ManualResetEventSlim entered = new();
        Task<int> task = Start(session, token =>
        {
            entered.Set();
            Ensure(token.WaitHandle.WaitOne(Limit), "Cancellation was not delivered.");
            token.ThrowIfCancellationRequested();
            return 1;
        });
        Wait(entered);
        session.RequestCancellation();
        await ExpectCanceled(task);
        Ensure(!session.IsBusy, "Canceled save must finish cleanup before completion.");
        Ensure(await Bounded(Start(session, token => token.IsCancellationRequested ? -1 : 8)) == 8,
            "Restart must not inherit the old token.");
    }

    private static async Task SuccessfulCommitSurvivesLateCancel()
    {
        ReportSaveSession session = new();
        Task<int> task = Start(session, token =>
        {
            session.RequestCancellation();
            return 9;
        });
        Ensure(await Bounded(task) == 9 && task.Status == TaskStatus.RanToCompletion,
            "Late cancellation must not revoke a committed result.");
    }

    private static async Task CompletionWaitsForCancellationCallbacks()
    {
        ReportSaveSession session = new();
        using ManualResetEventSlim registered = new();
        using ManualResetEventSlim callbackEntered = new();
        using ManualResetEventSlim releaseCallback = new();
        using ManualResetEventSlim delegateReturned = new();
        Task<int> task = Start(session, token =>
        {
            _ = token.Register(() => { callbackEntered.Set(); Wait(releaseCallback); });
            registered.Set();
            Ensure(token.WaitHandle.WaitOne(Limit), "Cancellation signal missing.");
            delegateReturned.Set();
            return 10;
        });
        try
        {
            Wait(registered);
            session.RequestCancellation();
            Wait(callbackEntered);
            Wait(delegateReturned);
            Ensure(!task.IsCompleted && session.IsBusy, "Callback lifetime must outlive the delegate when necessary.");
            Ensure(session.State == ReportSaveSessionState.CancellationRequested, "Expected cancellation-pending state.");
        }
        finally { releaseCallback.Set(); }
        Ensure(await Bounded(task) == 10, "Committed result must survive callback draining.");
        Ensure(!session.IsBusy, "Source must be disposed only after callbacks ended.");
    }

    private static async Task CallbackFailureIsFlaggedWithoutReplacingSaveSuccess()
    {
        ReportSaveSession session = new();
        using ManualResetEventSlim registered = new();
        Task<int> task = Start(session, token =>
        {
            _ = token.Register(() => throw new InvalidOperationException("private-callback-message"));
            registered.Set();
            Ensure(token.WaitHandle.WaitOne(Limit), "Cancellation signal missing.");
            return 11;
        });
        Wait(registered);
        session.RequestCancellation();
        Ensure(await Bounded(task) == 11 && session.CancellationCallbackFailed,
            "Callback failure is a warning, not revocation of save success.");
        Ensure(await Bounded(Start(session, token => 12)) == 12 && !session.CancellationCallbackFailed,
            "Callback warning must reset for a new operation.");
    }

    private static async Task CloseDrainsAndRejectsNewSaves()
    {
        ReportSaveSession session = new();
        using ManualResetEventSlim entered = new();
        using ManualResetEventSlim release = new();
        Task<int> save = Start(session, token => { entered.Set(); Wait(release); return 13; });
        Task close;
        try
        {
            Wait(entered);
            close = session.CloseAsync();
            Ensure(!close.IsCompleted && session.State == ReportSaveSessionState.Closing,
                "Close must await the active save.");
            Ensure(!session.TryStart(token => 0, out Task<int>? rejected) && rejected is null,
                "Closing session must reject new saves.");
        }
        finally { release.Set(); }
        Ensure(await Bounded(save) == 13, "Close must preserve the already committed result.");
        await close.WaitAsync(Limit);
        Ensure(session.State == ReportSaveSessionState.Closed, "Expected closed state.");
        Ensure(!session.TryStart(token => 0, out Task<int>? after), "Closed session cannot restart.");
        Ensure(after is null, "Rejected task must be null.");
        await session.CloseAsync();
    }

    private static async Task CloseObservesFailureWithoutHidingOriginalTask()
    {
        ReportSaveSession session = new();
        using ManualResetEventSlim entered = new();
        using ManualResetEventSlim release = new();
        Task<int> save = Start<int>(session, token => { entered.Set(); Wait(release); throw new IOException("failure"); });
        Wait(entered);
        Task close = session.CloseAsync();
        release.Set();
        await close.WaitAsync(Limit);
        try { await Bounded(save); throw new InvalidOperationException("Failure was hidden."); }
        catch (IOException) { }
        Ensure(save.IsFaulted && session.State == ReportSaveSessionState.Closed, "Join and result are independent.");
    }

    private static async Task ConcurrentStartsAdmitOnlyOne()
    {
        ReportSaveSession session = new();
        using ManualResetEventSlim release = new();
        List<Task<int>> admitted = [];
        object sync = new();
        try
        {
            await Task.WhenAll(Enumerable.Range(0, 32).Select(index => Task.Run(() =>
            {
                if (session.TryStart(token => { Wait(release); return 1; }, out Task<int>? task))
                    lock (sync) admitted.Add(task);
            }))).WaitAsync(Limit);
            Ensure(admitted.Count == 1, "Exactly one concurrent save may acquire the session.");
        }
        finally { release.Set(); }
        Ensure(await Bounded(admitted.Single()) == 1, "Admitted task must finish.");
    }

    private static async Task CancelAndCompleteRacesDoNotDisposeLiveSources()
    {
        ReportSaveSession session = new();
        for (int index = 0; index < 100; index++)
        {
            Task<int> save = Start(session, token => 1);
            Task cancels = Task.WhenAll(Enumerable.Range(0, 4).Select(number => Task.Run(session.RequestCancellation)));
            try { await Bounded(save); } catch (OperationCanceledException) { }
            await cancels.WaitAsync(Limit);
            Ensure(!session.IsBusy, "No operation may leak after a cancel/finish race.");
        }
        Ensure(await Bounded(Start(session, token => 14)) == 14, "Race tests must not poison the session.");
    }

    private static async Task RecoveryErrorIsNotReportedAsCancellation()
    {
        ReportSaveSession session = new();
        Task<int> save = Start<int>(session, token =>
        {
            session.RequestCancellation();
            throw new ReportFileSetRecoveryException(new IOException("private cleanup error"));
        });
        try { await Bounded(save); throw new InvalidOperationException("Recovery error was hidden."); }
        catch (ReportFileSetRecoveryException) { }
        Ensure(save.IsFaulted && !save.IsCanceled, "Recovery-required errors take precedence over a canceled token.");
    }

    private static Task<T> Start<T>(ReportSaveSession session, Func<CancellationToken, T> operation)
    {
        if (!session.TryStart(operation, out Task<T>? task)) throw new InvalidOperationException("Unexpected start rejection.");
        return task;
    }
    private static Task<T> Bounded<T>(Task<T> task) => task.WaitAsync(Limit);
    private static async Task ExpectCanceled(Task task)
    {
        try { await task.WaitAsync(Limit); throw new InvalidOperationException("Expected canceled task."); }
        catch (OperationCanceledException) { }
    }
    private static void Wait(ManualResetEventSlim signal) => Ensure(signal.Wait(Limit), "Test synchronization timed out.");
    private static void Ensure(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
}
