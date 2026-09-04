using System.Windows;

namespace WlanLivePathTester.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Activated += OnApplicationActivated;
    }

    private void OnApplicationActivated(object? sender, EventArgs e)
    {
        if (MainWindow is MainWindow window)
        {
            window.EnsureNetworkEnvironmentTab();
            window.EnsureRepeatedMeasurementReportTab();
        }
    }
}
