using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using WlanLivePathTester.Core.Observation;

namespace WlanLivePathTester.App;

public partial class MainWindow
{
    private const int WmPowerBroadcast = 0x0218;
    private const int PbtApmSuspend = 0x0004;
    private const int PbtApmPowerStatusChange = 0x000A;
    private const int PbtApmResumeSuspend = 0x0007;
    private const int PbtApmResumeAutomatic = 0x0012;

    private readonly ObservationPowerTransitionState
        _observationPowerTransitionState = new();
    private HwndSource? _observationPowerHwndSource;
    private bool _observationPowerTransitionMonitorHooked;
    private bool _observationInterruptedBySystemSuspend;
    private string? _observationSystemSuspendMessage;

    internal void EnsureObservationPowerTransitionMonitor()
    {
        if (_observationPowerTransitionMonitorHooked)
        {
            return;
        }

        nint handle = new WindowInteropHelper(this).Handle;
        if (handle == nint.Zero)
        {
            _ = Dispatcher.BeginInvoke(
                DispatcherPriority.Loaded,
                EnsureObservationPowerTransitionMonitor);
            return;
        }

        HwndSource? source = HwndSource.FromHwnd(handle);
        if (source is null)
        {
            return;
        }

        source.AddHook(ObservationPowerWindowMessageHook);
        _observationPowerHwndSource = source;

        if (_startObservationButton is not null)
        {
            _startObservationButton.Click +=
                OnObservationStartForPowerTransition;
            _startObservationButton.IsEnabledChanged +=
                OnObservationAvailabilityForPowerTransitionChanged;
        }

        Closed += OnObservationPowerTransitionWindowClosed;
        _observationPowerTransitionMonitorHooked = true;
    }

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
            PbtApmResumeSuspend or PbtApmResumeAutomatic =>
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

    private void OnObservationStartForPowerTransition(
        object sender,
        RoutedEventArgs e)
    {
        _observationPowerTransitionState.BeginObservation();
        _observationInterruptedBySystemSuspend = false;
        _observationSystemSuspendMessage = null;

        _ = Dispatcher.BeginInvoke(
            DispatcherPriority.Background,
            () =>
            {
                if (_observationCancellation is null)
                {
                    _ = _observationPowerTransitionState
                        .CompleteObservation();
                }
            });
    }

    private void OnObservationAvailabilityForPowerTransitionChanged(
        object sender,
        DependencyPropertyChangedEventArgs e)
    {
        if (sender is not Button { IsEnabled: true })
        {
            return;
        }

        _ = Dispatcher.BeginInvoke(
            DispatcherPriority.Background,
            FinalizeObservationPowerTransition);
    }

    private void HandleObservationPowerTransition(
        ObservationPowerTransition transition)
    {
        ObservationPowerTransitionDecision decision =
            _observationPowerTransitionState.Handle(transition);

        if (transition == ObservationPowerTransition.Suspend
            && decision.ShouldCancelObservation)
        {
            _observationInterruptedBySystemSuspend = true;
            _observationSystemSuspendMessage =
                "시스템이 절전 또는 최대 절전 상태로 전환되어 서로 다른 전원 세션의 Wi-Fi 카운터를 결합하지 않고 관찰을 중단했습니다.";

            try
            {
                _observationCancellation?.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // The observation completed at the same time as suspend.
            }

            AppendSystemPowerTransitionStatus(
                _observationSystemSuspendMessage,
                Brushes.DarkOrange);
            return;
        }

        if (transition == ObservationPowerTransition.Resume
            && decision.ShouldReevaluateAdapters)
        {
            AppendSystemPowerTransitionStatus(
                decision.Message,
                Brushes.DarkOrange);
            _ = Dispatcher.BeginInvoke(
                DispatcherPriority.Background,
                ReevaluateAdaptersAfterPowerResume);
        }
    }

    private void FinalizeObservationPowerTransition()
    {
        bool interrupted = _observationPowerTransitionState
            .CompleteObservation();
        if (!interrupted
            || !_observationInterruptedBySystemSuspend
            || _lastBrowserObservation is null)
        {
            if (_observationPowerTransitionState
                .AdapterReevaluationRequired)
            {
                ReevaluateAdaptersAfterPowerResume();
            }

            return;
        }

        string reason = _observationSystemSuspendMessage
            ?? "시스템 절전 전환으로 브라우저 관찰을 중단했습니다.";
        BrowserObservationResult current = _lastBrowserObservation;
        _lastBrowserObservation = current with
        {
            Message = AppendUniqueSentence(current.Message, reason),
            TerminationReason =
                BrowserObservationTerminationReason.SystemSuspend
        };

        if (_observationResultText is not null)
        {
            string terminationLine =
                "종료 원인: 시스템 절전 전환 (SystemSuspend)";
            if (!_observationResultText.Text.Contains(
                    terminationLine,
                    StringComparison.Ordinal))
            {
                _observationResultText.Text =
                    _observationResultText.Text.TrimEnd()
                    + Environment.NewLine
                    + terminationLine;
            }
        }

        ReevaluateAdaptersAfterPowerResume();
    }

    private void ReevaluateAdaptersAfterPowerResume()
    {
        if (_measurementRunning || _observationCancellation is not null)
        {
            return;
        }

        RefreshNetworkAdapterDiagnosticsIfIdle();
        _observationPowerTransitionState.MarkAdaptersReevaluated();
    }

    private void AppendSystemPowerTransitionStatus(
        string message,
        Brush brush)
    {
        if (_observationResultText is null)
        {
            return;
        }

        string line = "전원 상태: " + message;
        if (_observationResultText.Text.Contains(
                line,
                StringComparison.Ordinal))
        {
            return;
        }

        _observationResultText.Text =
            _observationResultText.Text.TrimEnd()
            + Environment.NewLine
            + line;
        _observationResultText.Foreground = brush;
    }

    private static string AppendUniqueSentence(
        string existing,
        string addition)
    {
        if (existing.Contains(addition, StringComparison.Ordinal))
        {
            return existing;
        }

        return string.IsNullOrWhiteSpace(existing)
            ? addition
            : existing.TrimEnd() + " " + addition;
    }

    private void OnObservationPowerTransitionWindowClosed(
        object? sender,
        EventArgs e)
    {
        if (_observationPowerHwndSource is not null)
        {
            _observationPowerHwndSource.RemoveHook(
                ObservationPowerWindowMessageHook);
        }

        if (_startObservationButton is not null)
        {
            _startObservationButton.Click -=
                OnObservationStartForPowerTransition;
            _startObservationButton.IsEnabledChanged -=
                OnObservationAvailabilityForPowerTransitionChanged;
        }

        _observationPowerHwndSource = null;
        _observationPowerTransitionMonitorHooked = false;
    }
}
