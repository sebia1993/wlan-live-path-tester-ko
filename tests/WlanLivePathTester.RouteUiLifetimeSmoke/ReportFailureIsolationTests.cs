using WlanLivePathTester.App;
using WlanLivePathTester.Core.Operations;
using static WlanLivePathTester.RouteUiLifetimeSmoke.TestContext;

namespace WlanLivePathTester.RouteUiLifetimeSmoke;

internal static class ReportFailureIsolationTests
{
    internal static readonly (string Name, Func<Task> Run)[] Cases =
    [
        ("new canceled or malformed report does not inherit prior failure or replace export", VerifyAsync)
    ];

    private static async Task VerifyAsync()
    {
        MainWindow window = Prepare("경로 보고서");
        using Output output = new();
        using ManualResetEventSlim release = new();
        var entered = Pending<bool>();
        MainWindow.RouteReportSaveOutput? committed = null;
        Task<bool> first = window.RunRouteReportSaveAsync(token =>
        {
            committed = output.Write(token);
            token.Register(() => throw new IOException(Secret));
            entered.TrySetResult(true);
            if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("Synthetic writer gate timed out.");
            return committed;
        });
        try
        {
            await entered.Task;
            Click(window, "_routeComparisonReportCancelV2");
            release.Set();
            Ensure(await first && ReportText(window).Contains("콜백 오류", StringComparison.Ordinal),
                "The first committed report should retain its callback failure diagnostic.");
            string oldPath = Field<string>(window, "_latestRouteComparisonReportHtmlV2");
            bool cancelOnce = false;
            EventHandler<ApplicationOperationStateChangedEventArgs> cancel = (_, e) =>
            {
                if (e.Snapshot.Kind == ApplicationOperationKind.RouteComparisonReportSave && !cancelOnce)
                {
                    cancelOnce = true;
                    Coordinator(window).RequestCancellation();
                }
            };
            Coordinator(window).StateChanged += cancel;
            try
            {
                Ensure(!await window.RunRouteReportSaveAsync(_ => throw new InvalidOperationException("must not run")),
                    "The next report should cancel before its save session starts.");
            }
            finally { Coordinator(window).StateChanged -= cancel; }
            Ensure(!ReportText(window).Contains("콜백 오류", StringComparison.Ordinal),
                "A pre-start canceled attempt must not inherit a previous session's callback flag.");
            Ensure(!await window.RunRouteReportSaveAsync(_ => new MainWindow.RouteReportSaveOutput(null!, committed!.Export)),
                "An invalid result must fail validation.");
            Ensure(Field<string>(window, "_latestRouteComparisonReportHtmlV2") == oldPath && File.Exists(oldPath),
                "Malformed output must not replace the previous successful export.");
            Idle(window);
        }
        finally { release.Set(); await first; window.Close(); }
    }
}
