namespace WlanLivePathTester.App;

public partial class MainWindow
{
    internal void RefreshNetworkAdapterDiagnosticsIfIdle()
    {
        EnsureObservationAdapterGuard();

        if (!_networkAdapterDiagnosticsTabAdded)
        {
            return;
        }

        if (_measurementRunning || _observationCancellation is not null)
        {
            ApplyObservationAdapterGuard();
            return;
        }

        RefreshNetworkAdapterDiagnostics();
    }
}
