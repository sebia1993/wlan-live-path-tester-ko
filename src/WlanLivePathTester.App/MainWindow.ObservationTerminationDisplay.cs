using System.Windows;
using System.Windows.Controls;
using WlanLivePathTester.Core.Observation;

namespace WlanLivePathTester.App;

public partial class MainWindow
{
    private bool _observationTerminationDisplayHooked;

    internal void EnsureObservationTerminationDisplay()
    {
        if (_observationTerminationDisplayHooked
            || _startObservationButton is null)
        {
            return;
        }

        _startObservationButton.IsEnabledChanged +=
            OnObservationAvailabilityForTerminationDisplayChanged;
        Closed += OnObservationTerminationDisplayWindowClosed;
        _observationTerminationDisplayHooked = true;
    }

    private void OnObservationAvailabilityForTerminationDisplayChanged(
        object sender,
        DependencyPropertyChangedEventArgs e)
    {
        if (sender is not Button { IsEnabled: true })
        {
            return;
        }

        AppendObservationTerminationReason();
    }

    private void AppendObservationTerminationReason()
    {
        BrowserObservationResult? result = _lastBrowserObservation;
        if (result is null
            || result.TerminationReason
                == BrowserObservationTerminationReason.None
            || _observationResultText is null)
        {
            return;
        }

        string line =
            $"종료 원인: {FormatObservationTerminationReason(result.TerminationReason)} ({result.TerminationReason})";
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
    }

    private static string FormatObservationTerminationReason(
        BrowserObservationTerminationReason reason) =>
        reason switch
        {
            BrowserObservationTerminationReason.Completed => "정상 완료",
            BrowserObservationTerminationReason.CanceledByUser => "사용자 중지",
            BrowserObservationTerminationReason.AdapterChanged => "관찰 Wi-Fi 인터페이스 변경",
            BrowserObservationTerminationReason.AdapterUnavailable => "고정 Wi-Fi 사용 불가",
            BrowserObservationTerminationReason.CounterProviderMismatch => "고정 ID와 카운터 공급자 불일치",
            BrowserObservationTerminationReason.InvalidOptions => "관찰 설정 오류",
            BrowserObservationTerminationReason.UnsupportedPlatform => "지원하지 않는 실행 환경",
            BrowserObservationTerminationReason.NoWirelessConnection => "연결된 WLAN 없음",
            BrowserObservationTerminationReason.Failed => "분류되지 않은 실행 오류",
            _ => "기록되지 않음"
        };

    private void OnObservationTerminationDisplayWindowClosed(
        object? sender,
        EventArgs e)
    {
        if (_observationTerminationDisplayHooked
            && _startObservationButton is not null)
        {
            _startObservationButton.IsEnabledChanged -=
                OnObservationAvailabilityForTerminationDisplayChanged;
        }

        _observationTerminationDisplayHooked = false;
    }
}
