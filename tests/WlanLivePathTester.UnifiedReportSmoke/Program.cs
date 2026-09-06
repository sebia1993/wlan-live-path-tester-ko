using System.Windows.Threading;

namespace WlanLivePathTester.UnifiedReportSmoke;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
        Task tests = RunAsync().WaitAsync(TimeSpan.FromSeconds(90));
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
        int groups = 0;
        foreach ((string name, Action test) in UnifiedReportTests.Cases)
        {
            test();
            groups++;
            Console.WriteLine($"PASS {name}");
        }
        foreach ((string name, Func<Task> test) in UnifiedReportUiTests.Cases)
        {
            await test().WaitAsync(TimeSpan.FromSeconds(8));
            groups++;
            Console.WriteLine($"PASS {name}");
        }
        Console.WriteLine($"Unified report smoke: {groups} groups, 0 failures; synthetic route data and injected WPF capture/writer only.");
    }
}
