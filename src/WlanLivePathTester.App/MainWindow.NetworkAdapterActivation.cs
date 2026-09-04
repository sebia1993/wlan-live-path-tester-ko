namespace WlanLivePathTester.App;

public partial class MainWindow
{
    internal void RefreshNetworkAdapterDiagnosticsIfIdle()
    {
        if (!_networkAdapterDiagnosticsTabAdded
            || _measurementRunning
            || _observationCancellation is not null)
        {
            return;
        }

        RefreshNetworkAdapterDiagnostics();
    }
}
