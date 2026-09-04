using System.Windows;
using System.Windows.Threading;

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
        if (MainWindow is not MainWindow window)
        {
            return;
        }

        AttachLocalDiagnosticFeatures(window);
        if (!window.Dispatcher.HasShutdownStarted
            && !window.Dispatcher.HasShutdownFinished)
        {
            _ = window.Dispatcher.BeginInvoke(
                DispatcherPriority.Loaded,
                () => AttachLocalDiagnosticFeatures(window));
        }
    }

    private static void AttachLocalDiagnosticFeatures(MainWindow window)
    {
        window.EnsureNetworkEnvironmentTab();
        window.EnsureWlanInterfaceCorrelationTab();
        window.EnsureNetworkAdapterDiagnosticsTab();
        window.EnsureNetworkEnvironmentReportTab();
        window.EnsureNetworkAdapterReportTab();
        window.EnsureRepeatedMeasurementReportTab();
        window.EnsureNetworkAdapterChangeMonitor();
        window.RefreshNetworkAdapterDiagnosticsIfIdle();
    }
}
