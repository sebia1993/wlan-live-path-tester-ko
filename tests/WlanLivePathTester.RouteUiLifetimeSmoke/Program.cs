using System.Windows.Threading;

namespace WlanLivePathTester.RouteUiLifetimeSmoke;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
        Task tests = RunAsync().WaitAsync(TimeSpan.FromSeconds(120));
        _ = tests.ContinueWith(_ => dispatcher.BeginInvokeShutdown(DispatcherPriority.Normal),
            CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
        Dispatcher.Run();
        try
        {
            tests.GetAwaiter().GetResult();
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
        var cases = RouteTests.Cases.Concat(ReportTests.Cases).Concat(BasicTests.Cases)
            .Concat(ReportFailureIsolationTests.Cases).Concat(GuidedNavigationTests.Cases).ToArray();
        foreach ((string name, Func<Task> run) in cases)
        {
            await run().WaitAsync(TimeSpan.FromSeconds(15));
            Console.WriteLine($"PASS {name}");
        }
        Console.WriteLine($"Central route UI lifetime smoke: {cases.Length} groups, 0 failures; synthetic collectors and local temporary report files only.");
    }
}
