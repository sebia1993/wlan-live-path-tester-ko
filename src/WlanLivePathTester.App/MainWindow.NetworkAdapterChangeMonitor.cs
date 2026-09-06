using System.Net.NetworkInformation;
using System.Windows.Controls;
using System.Windows.Media;
using WlanLivePathTester.Core.Operations;

namespace WlanLivePathTester.App;

public partial class MainWindow
{
    private DeferredNetworkRefreshController? _networkAdapterRefreshController;
    private bool _networkAdapterChangeMonitorStarted;
    private bool _networkAdapterChangeMonitorClosed;

    private void InitializeNetworkAdapterRefreshController()
    {
        Dispatcher.VerifyAccess();
        if (_networkAdapterRefreshController is not null || _networkAdapterChangeMonitorClosed) return;
        _networkAdapterRefreshController = new DeferredNetworkRefreshController(
            Dispatcher,
            CanRunDeferredNetworkAdapterRefresh,
            () => RunNetworkAdapterDiagnosticsAsync(automatic: true),
            type => SetNetworkAdapterSelectionText(
                $"자동 어댑터 갱신 오류: {type}. 수동 새로고침으로 다시 확인하십시오.", Brushes.DarkRed));
        _applicationOperations.StateChanged += OnOperationStateForAdapterRefreshChanged;
    }

    internal void EnsureNetworkAdapterChangeMonitor()
    {
        Dispatcher.VerifyAccess();
        if (_networkAdapterChangeMonitorStarted || _networkAdapterChangeMonitorClosed
            || _applicationOperationWindowClosed) return;
        try
        {
            NetworkChange.NetworkAddressChanged += OnNetworkAdapterAddressChanged;
            NetworkChange.NetworkAvailabilityChanged += OnNetworkAdapterAvailabilityChanged;
            _networkAdapterChangeMonitorStarted = true;
        }
        catch (Exception exception)
        {
            NetworkChange.NetworkAddressChanged -= OnNetworkAdapterAddressChanged;
            NetworkChange.NetworkAvailabilityChanged -= OnNetworkAdapterAvailabilityChanged;
            SetNetworkAdapterSelectionText(
                $"네트워크 변경 감시 연결 오류: {exception.GetType().Name}. 수동 새로고침은 사용할 수 있습니다.", Brushes.DarkOrange);
        }
        _networkAdapterRefreshController?.StateMayHaveChanged();
    }

    private bool CanRunDeferredNetworkAdapterRefresh()
    {
        Dispatcher.VerifyAccess();
        ApplicationOperationSnapshot state = CurrentApplicationOperation;
        return !_applicationOperationWindowClosed && !_networkAdapterChangeMonitorClosed
            && !_applicationOperationClosePending && !_routeProxyClosePending
            && !_routeReportCloseRequested && !_localDiagnosticSystemSuspended
            && _networkAdapterDiagnosticsTabAdded
            && _networkAdapterSelectionText is not null
            && _networkAdapterWarningText is not null
            && _networkAdapterInventoryText is not null
            && !state.IsBusy && !state.ShutdownRequested
            && !_measurementRunning && _observationCancellation is null
            && _routeComparisonCancellationV3 is null
            && _routeProxyOperationCompletion is not { Task.IsCompleted: false }
            && !RouteReportSaveBusy
            && FindApplicationTabControl()?.SelectedItem is TabItem { IsEnabled: true };
    }

    private void OnNetworkAdapterAddressChanged(object? sender, EventArgs e) =>
        QueueNetworkAdapterRefresh();

    private void OnNetworkAdapterAvailabilityChanged(object? sender, NetworkAvailabilityEventArgs e) =>
        QueueNetworkAdapterRefresh();

    private void QueueNetworkAdapterRefresh() =>
        _networkAdapterRefreshController?.RequestRefresh();

    private void OnOperationStateForAdapterRefreshChanged(
        object? sender, ApplicationOperationStateChangedEventArgs e)
    {
        // Notifications can be posted from different threads and arrive out of
        // order. The scheduler re-reads the current coordinator snapshot on the
        // Dispatcher, never acts on this event's possibly stale snapshot.
        _networkAdapterRefreshController?.StateMayHaveChanged();
    }

    // All activation, network-event and power-resume paths enqueue here.
    // They must not collect synchronously or silently switch an active NIC.
    private void RefreshNetworkAdapterDiagnostics() => QueueNetworkAdapterRefresh();

    private void DisposeNetworkAdapterRefreshController()
    {
        Dispatcher.VerifyAccess();
        _networkAdapterChangeMonitorClosed = true;
        _applicationOperations.StateChanged -= OnOperationStateForAdapterRefreshChanged;
        if (_networkAdapterChangeMonitorStarted)
        {
            NetworkChange.NetworkAddressChanged -= OnNetworkAdapterAddressChanged;
            NetworkChange.NetworkAvailabilityChanged -= OnNetworkAdapterAvailabilityChanged;
            _networkAdapterChangeMonitorStarted = false;
        }
        _networkAdapterRefreshController?.Dispose();
        _networkAdapterRefreshController = null;
    }
}
