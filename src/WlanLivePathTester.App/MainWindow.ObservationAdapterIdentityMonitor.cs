using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using WlanLivePathTester.Windows.Adapters;
using WlanLivePathTester.Windows.Wlan;

namespace WlanLivePathTester.App;

public partial class MainWindow
{
    private const int AdapterMismatchSamplesBeforeCancel = 3;
    private static readonly TimeSpan AdapterIdentitySampleInterval =
        TimeSpan.FromMilliseconds(500);

    private bool _observationAdapterIdentityMonitorHooked;
    private Task? _observationAdapterIdentityMonitorTask;
    private string? _observationExpectedAdapterId;
    private bool _observationCanceledByAdapterIdentityChange;
    private string? _observationAdapterIdentityChangeMessage;

    internal void EnsureObservationAdapterIdentityMonitor()
    {
        if (_observationAdapterIdentityMonitorHooked
            || _startObservationButton is null)
        {
            return;
        }

        _startObservationButton.Click +=
            OnObservationStartedForAdapterIdentityMonitor;
        _startObservationButton.IsEnabledChanged +=
            OnObservationCompletedForAdapterIdentityMonitor;
        Closed += OnObservationAdapterIdentityMonitorWindowClosed;
        _observationAdapterIdentityMonitorHooked = true;
    }

    private void OnObservationStartedForAdapterIdentityMonitor(
        object sender,
        RoutedEventArgs e)
    {
        CancellationTokenSource? cancellation =
            _observationCancellation;
        if (cancellation is null
            || cancellation.IsCancellationRequested
            || _observationBlockedByAdapterSelection
            || string.IsNullOrWhiteSpace(_recommendedWirelessAdapterId))
        {
            return;
        }

        string expectedId =
            NetworkAdapterInventoryReader.NormalizeInterfaceId(
                _recommendedWirelessAdapterId);
        if (string.IsNullOrWhiteSpace(expectedId))
        {
            return;
        }

        _observationExpectedAdapterId = expectedId;
        _observationCanceledByAdapterIdentityChange = false;
        _observationAdapterIdentityChangeMessage = null;
        _observationAdapterIdentityMonitorTask =
            MonitorObservationAdapterIdentityAsync(
                cancellation,
                expectedId);
    }

    private async Task MonitorObservationAdapterIdentityAsync(
        CancellationTokenSource observationCancellation,
        string expectedId)
    {
        int consecutiveMismatches = 0;

        while (!observationCancellation.IsCancellationRequested
               && ReferenceEquals(
                   _observationCancellation,
                   observationCancellation))
        {
            try
            {
                await Task.Delay(
                    AdapterIdentitySampleInterval,
                    observationCancellation.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            if (observationCancellation.IsCancellationRequested
                || !ReferenceEquals(
                    _observationCancellation,
                    observationCancellation))
            {
                break;
            }

            string currentId;
            try
            {
                currentId = NetworkAdapterInventoryReader.NormalizeInterfaceId(
                    NativeWlanReader.ReadCurrent()
                        .FirstConnectedInterface?
                        .InterfaceId);
            }
            catch (Exception exception) when (
                exception is InvalidOperationException
                    or PlatformNotSupportedException)
            {
                currentId = string.Empty;
            }

            if (currentId.Equals(
                    expectedId,
                    StringComparison.OrdinalIgnoreCase))
            {
                consecutiveMismatches = 0;
                continue;
            }

            consecutiveMismatches++;
            if (consecutiveMismatches
                < AdapterMismatchSamplesBeforeCancel)
            {
                continue;
            }

            string reason = string.IsNullOrWhiteSpace(currentId)
                ? "관찰 중 Native WLAN 연결 인터페이스 ID를 연속해서 확인하지 못했습니다."
                : "관찰 중 Native WLAN 연결이 시작 시점과 다른 Wi-Fi 인터페이스로 변경되었습니다.";
            _observationCanceledByAdapterIdentityChange = true;
            _observationAdapterIdentityChangeMessage =
                reason + " 처리량 결과 혼합을 막기 위해 관찰을 중단했습니다.";
            _networkAdapterRefreshDeferred = true;

            try
            {
                observationCancellation.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // The observation completed between the identity check and cancel.
            }

            if (!Dispatcher.HasShutdownStarted
                && !Dispatcher.HasShutdownFinished)
            {
                _ = Dispatcher.BeginInvoke(
                    DispatcherPriority.Background,
                    () =>
                    {
                        AppendAdapterIdentityChangeWarning();
                        ApplyObservationAdapterGuard();
                    });
            }

            break;
        }
    }

    private void OnObservationCompletedForAdapterIdentityMonitor(
        object sender,
        DependencyPropertyChangedEventArgs e)
    {
        if (sender is not Button { IsEnabled: true })
        {
            return;
        }

        if (_observationCanceledByAdapterIdentityChange)
        {
            AppendAdapterIdentityChangeWarning();
            AppendObservationAdapterIdentityStopReason();
        }

        _observationExpectedAdapterId = null;
        _observationAdapterIdentityMonitorTask = null;
    }

    private void AppendAdapterIdentityChangeWarning()
    {
        if (_networkAdapterWarningText is null
            || string.IsNullOrWhiteSpace(
                _observationAdapterIdentityChangeMessage))
        {
            return;
        }

        if (_networkAdapterWarningText.Text.Contains(
                _observationAdapterIdentityChangeMessage,
                StringComparison.Ordinal))
        {
            return;
        }

        _networkAdapterWarningText.Text =
            string.IsNullOrWhiteSpace(_networkAdapterWarningText.Text)
            || _networkAdapterWarningText.Text.Equals(
                "추가 경고 없음",
                StringComparison.Ordinal)
                ? _observationAdapterIdentityChangeMessage
                : _networkAdapterWarningText.Text
                  + Environment.NewLine
                  + _observationAdapterIdentityChangeMessage;
    }

    private void AppendObservationAdapterIdentityStopReason()
    {
        if (_observationResultText is null
            || string.IsNullOrWhiteSpace(
                _observationAdapterIdentityChangeMessage)
            || _observationResultText.Text.Contains(
                _observationAdapterIdentityChangeMessage,
                StringComparison.Ordinal))
        {
            return;
        }

        _observationResultText.Text =
            _observationResultText.Text.TrimEnd()
            + Environment.NewLine
            + "중단 사유: "
            + _observationAdapterIdentityChangeMessage;
    }

    private void OnObservationAdapterIdentityMonitorWindowClosed(
        object? sender,
        EventArgs e)
    {
        if (_observationAdapterIdentityMonitorHooked
            && _startObservationButton is not null)
        {
            _startObservationButton.Click -=
                OnObservationStartedForAdapterIdentityMonitor;
            _startObservationButton.IsEnabledChanged -=
                OnObservationCompletedForAdapterIdentityMonitor;
        }

        _observationAdapterIdentityMonitorHooked = false;
        _observationAdapterIdentityMonitorTask = null;
        _observationExpectedAdapterId = null;
    }
}
