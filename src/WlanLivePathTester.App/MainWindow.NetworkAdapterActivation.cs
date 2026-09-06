namespace WlanLivePathTester.App;

public partial class MainWindow
{
    internal void RefreshNetworkAdapterDiagnosticsIfIdle()
    {
        if (!_networkAdapterDiagnosticsTabAdded || _applicationOperationWindowClosed) return;
        // The shared scheduler retains this request while busy and performs it
        // once the current Core/UI lease has actually completed.
        RefreshNetworkAdapterDiagnostics();
    }
}
