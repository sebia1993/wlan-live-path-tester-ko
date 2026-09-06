using System.Windows.Threading;

namespace WlanLivePathTester.OperationLifecycleSmoke;

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
        var cases = DiagnosticAndRefreshTests.Cases.Concat(AuxiliaryReportTests.Cases).ToArray();
        foreach ((string name, Func<Task> run) in cases)
        {
            await run().WaitAsync(TimeSpan.FromSeconds(15));
            Console.WriteLine($"PASS {name}");
        }
        Console.WriteLine($"Operation lifecycle smoke: {cases.Length} groups, 0 failures; injected readers/writers, no live WLAN, DNS, HTTP or PAC/WPAD.");
    }
}
