using System.Net.NetworkInformation;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace WlanLivePathTester.App;

public partial class MainWindow
{
    private DispatcherTimer? _networkAdapterRefreshTimer;
    private bool _networkAdapterChangeMonitorStarted;
    private bool _networkAdapterRefreshDeferred;

    internal void EnsureNetworkAdapterChangeMonitor()
    {
        if (_networkAdapterChangeMonitorStarted)
        {
            return;
        }

        NetworkChange.NetworkAddressChanged +=
            OnNetworkAdapterAddressChanged;
        NetworkChange.NetworkAvailabilityChanged +=
            OnNetworkAdapterAvailabilityChanged;
        Closed += OnNetworkAdapterMonitorWindowClosed;

        _networkAdapterRefreshTimer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(750),
            DispatcherPriority.Background,
            OnNetworkAdapterRefreshTimerTick,
            Dispatcher)
        {
            IsEnabled = false
        };

        if (_startObservationButton is not null)
        {
            _startObservationButton.IsEnabledChanged +=
                OnObservationAvailabilityForAdapterRefreshChanged;
        }

        _networkAdapterChangeMonitorStarted = true;
    }

    private void OnNetworkAdapterAddressChanged(
        object? sender,
        EventArgs e) =>
        QueueNetworkAdapterRefresh();

    private void OnNetworkAdapterAvailabilityChanged(
        object? sender,
        NetworkAvailabilityEventArgs e) =>
        QueueNetworkAdapterRefresh();

    private void QueueNetworkAdapterRefresh()
    {
        if (Dispatcher.HasShutdownStarted
            || Dispatcher.HasShutdownFinished)
        {
            return;
        }

        _ = Dispatcher.BeginInvoke(
            DispatcherPriority.Background,
            () =>
            {
                _networkAdapterRefreshDeferred = true;
                if (_measurementRunning
                    || _observationCancellation is not null)
                {
                    AppendDeferredAdapterWarning();
                    return;
                }

                StartNetworkAdapterRefreshDebounce();
            });
    }

    private void StartNetworkAdapterRefreshDebounce()
    {
        if (_networkAdapterRefreshTimer is null)
        {
            return;
        }

        _networkAdapterRefreshTimer.Stop();
        _networkAdapterRefreshTimer.Start();
    }

    private void OnNetworkAdapterRefreshTimerTick(
        object? sender,
        EventArgs e)
    {
        _networkAdapterRefreshTimer?.Stop();
        if (_measurementRunning || _observationCancellation is not null)
        {
            _networkAdapterRefreshDeferred = true;
            AppendDeferredAdapterWarning();
            return;
        }

        _networkAdapterRefreshDeferred = false;
        RefreshNetworkAdapterDiagnostics();
    }

    private void OnObservationAvailabilityForAdapterRefreshChanged(
        object sender,
        DependencyPropertyChangedEventArgs e)
    {
        if (!_networkAdapterRefreshDeferred
            || sender is not Button { IsEnabled: true }
            || _measurementRunning
            || _observationCancellation is not null)
        {
            return;
        }

        StartNetworkAdapterRefreshDebounce();
    }

    private void AppendDeferredAdapterWarning()
    {
        if (_networkAdapterWarningText is null)
        {
            return;
        }

        const string warning =
            "네트워크 인터페이스 변경이 감지되었습니다. 현재 측정·관찰이 끝나면 어댑터 선택을 다시 평가합니다.";
        if (_networkAdapterWarningText.Text.Contains(
                warning,
                StringComparison.Ordinal))
        {
            return;
        }

        _networkAdapterWarningText.Text =
            string.IsNullOrWhiteSpace(_networkAdapterWarningText.Text)
            || _networkAdapterWarningText.Text.Equals(
                "추가 경고 없음",
                StringComparison.Ordinal)
                ? warning
                : _networkAdapterWarningText.Text
                  + Environment.NewLine
                  + warning;
    }

    private void OnNetworkAdapterMonitorWindowClosed(
        object? sender,
        EventArgs e)
    {
        NetworkChange.NetworkAddressChanged -=
            OnNetworkAdapterAddressChanged;
        NetworkChange.NetworkAvailabilityChanged -=
            OnNetworkAdapterAvailabilityChanged;

        if (_startObservationButton is not null)
        {
            _startObservationButton.IsEnabledChanged -=
                OnObservationAvailabilityForAdapterRefreshChanged;
        }

        if (_networkAdapterRefreshTimer is not null)
        {
            _networkAdapterRefreshTimer.Stop();
            _networkAdapterRefreshTimer.Tick -=
                OnNetworkAdapterRefreshTimerTick;
        }

        _networkAdapterChangeMonitorStarted = false;
    }
}
