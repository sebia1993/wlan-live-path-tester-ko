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
        BrowserObservationResult? result =
            _lastBrowserObservationResult;
        if (result is null || _observationResultText is null)
        {
            return;
        }

        BrowserObservationTerminationReason reason =
            result.EffectiveTerminationReason;
        if (reason == BrowserObservationTerminationReason.None)
        {
            return;
        }

        string line =
            $"종료 원인: {BrowserObservationTerminationPolicy.ToDisplayText(reason)} ({reason})";
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

        Closed -= OnObservationTerminationDisplayWindowClosed;
        _observationTerminationDisplayHooked = false;
    }
}
