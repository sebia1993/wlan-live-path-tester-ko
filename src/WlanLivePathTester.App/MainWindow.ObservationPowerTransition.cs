using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using WlanLivePathTester.Core.Observation;

namespace WlanLivePathTester.App;

public partial class MainWindow
{
    private const int WmPowerBroadcast = 0x0218;
    private const int PbtApmSuspend = 0x0004;
    private const int PbtApmResumeCritical = 0x0006;
    private const int PbtApmResumeSuspend = 0x0007;
    private const int PbtApmPowerStatusChange = 0x000A;
    private const int PbtApmResumeAutomatic = 0x0012;

    private readonly ObservationPowerTransitionState
        _observationPowerTransitionState = new();
    private HwndSource? _observationPowerMessageSource;
    private bool _observationPowerMessageHooked;
    private bool _observationPowerSourceEventHooked;
    private bool _observationPowerClosedHooked;

    internal void EnsureObservationPowerTransitionMonitor()
    {
        EnsureObservationPowerClosedHook();
        if (_observationPowerMessageHooked)
        {
            return;
        }

        nint handle = new WindowInteropHelper(this).Handle;
        if (handle == nint.Zero)
        {
            EnsureObservationPowerSourceInitializedHook();
            return;
        }

        HwndSource? source = HwndSource.FromHwnd(handle);
        if (source is null)
        {
            EnsureObservationPowerSourceInitializedHook();
            return;
        }

        source.AddHook(ObservationPowerWindowMessageHook);
        _observationPowerMessageSource = source;
        _observationPowerMessageHooked = true;
        RemoveObservationPowerSourceInitializedHook();
    }

    private void EnsureObservationPowerClosedHook()
    {
        if (_observationPowerClosedHooked)
        {
            return;
        }

        Closed += OnObservationPowerMonitorWindowClosed;
        _observationPowerClosedHooked = true;
    }

    private void EnsureObservationPowerSourceInitializedHook()
    {
        if (_observationPowerSourceEventHooked)
        {
            return;
        }

        SourceInitialized += OnObservationPowerSourceInitialized;
        _observationPowerSourceEventHooked = true;
    }

    private void RemoveObservationPowerSourceInitializedHook()
    {
        if (!_observationPowerSourceEventHooked)
        {
            return;
        }

        SourceInitialized -= OnObservationPowerSourceInitialized;
        _observationPowerSourceEventHooked = false;
    }

    private void OnObservationPowerSourceInitialized(
        object? sender,
        EventArgs e) =>
        EnsureObservationPowerTransitionMonitor();

    private nint ObservationPowerWindowMessageHook(
        nint hwnd,
        int message,
        nint wParam,
        nint lParam,
        ref bool handled)
    {
        if (message != WmPowerBroadcast)
        {
            return nint.Zero;
        }

        ObservationPowerTransition? transition = wParam.ToInt64() switch
        {
            PbtApmSuspend => ObservationPowerTransition.Suspend,
            PbtApmResumeCritical
                or PbtApmResumeSuspend
                or PbtApmResumeAutomatic =>
                ObservationPowerTransition.Resume,
            PbtApmPowerStatusChange =>
                ObservationPowerTransition.PowerStatusChanged,
            _ => null
        };

        if (transition.HasValue)
        {
            HandleObservationPowerTransition(transition.Value);
        }

        return nint.Zero;
    }

    private void HandleObservationPowerTransition(
        ObservationPowerTransition transition)
    {
        ObservationPowerTransitionDecision decision =
            _observationPowerTransitionState.Handle(transition);

        if (transition == ObservationPowerTransition.Suspend
            && decision.ShouldCancelObservation)
        {
            _observationCancellationContext.RequestSystemSuspend();
            try
            {
                _observationCancellation?.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // The observation completed while Windows dispatched suspend.
            }

            SetObservationResult(
                "시스템 절전 또는 최대 절전 전환을 감지했습니다. 전원 전환 전후의 Wi-Fi 카운터를 결합하지 않도록 현재 관찰을 중단하고 있습니다.");
            return;
        }

        if (transition == ObservationPowerTransition.Resume
            && decision.ShouldReevaluateAdapters)
        {
            QueueAdapterRefreshAfterPowerResume();
        }
    }

    private void QueueAdapterRefreshAfterPowerResume()
    {
        if (Dispatcher.HasShutdownStarted
            || Dispatcher.HasShutdownFinished)
        {
            return;
        }

        _ = Dispatcher.BeginInvoke(
            DispatcherPriority.Background,
            () => TryRefreshAdaptersAfterPowerResume());
    }

    private void TryRefreshAdaptersAfterPowerResume()
    {
        if (_measurementRunning
            || _observationCancellation is not null)
        {
            return;
        }

        if (!_observationPowerTransitionState
            .TryMarkAdaptersReevaluated())
        {
            return;
        }

        RefreshNetworkAdapterDiagnostics();
    }

    private void OnObservationPowerMonitorWindowClosed(
        object? sender,
        EventArgs e)
    {
        if (_observationPowerMessageSource is not null)
        {
            _observationPowerMessageSource.RemoveHook(
                ObservationPowerWindowMessageHook);
        }

        RemoveObservationPowerSourceInitializedHook();
        if (_observationPowerClosedHooked)
        {
            Closed -= OnObservationPowerMonitorWindowClosed;
        }

        _observationPowerMessageSource = null;
        _observationPowerMessageHooked = false;
        _observationPowerClosedHooked = false;
    }
}
