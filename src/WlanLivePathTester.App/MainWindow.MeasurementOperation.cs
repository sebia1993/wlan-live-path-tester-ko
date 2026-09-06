using System.Windows.Media;
using WlanLivePathTester.Core.Operations;

namespace WlanLivePathTester.App;

public partial class MainWindow
{
    // The two-argument entry point remains for single downloads and existing
    // smoke tests. Repeated measurements supply their own fixed operation kind.
    private Task RunMeasurementOperationAsync(
        Func<CancellationToken, Task> operation,
        string runningMessage) =>
        RunCoordinatedMeasurementAsync(
            ApplicationOperationKind.DownloadMeasurement,
            (token, _) => operation(token),
            runningMessage);

    private async Task<bool> RunCoordinatedMeasurementAsync(
        ApplicationOperationKind kind,
        Func<CancellationToken, ApplicationOperationUiLease, Task> operation,
        string runningMessage)
    {
        Dispatcher.VerifyAccess();
        ArgumentNullException.ThrowIfNull(operation);
        if (kind is not (ApplicationOperationKind.DownloadMeasurement
            or ApplicationOperationKind.RepeatedMeasurement))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        using CancellationTokenSource cancellation = new();
        using ApplicationOperationUiLease? lease = TryBeginUiApplicationOperation(
            kind, cancellation.Cancel);
        if (lease is null) return false;

        try
        {
            _measurementRunning = true;
            _cancelMeasurement = () => lease.RequestCancellation();
            SetMeasurementBusy(true);
            MeasurementStatusText.Foreground = Brushes.DarkSlateGray;
            MeasurementStatusText.Text = runningMessage;
            cancellation.Token.ThrowIfCancellationRequested();
            await operation(cancellation.Token, lease);
            MeasurementStatusText.Foreground = cancellation.IsCancellationRequested
                ? Brushes.DarkOrange : Brushes.DarkGreen;
            MeasurementStatusText.Text = cancellation.IsCancellationRequested
                ? "측정 취소 처리가 완료되었습니다." : "측정이 완료되었습니다.";
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            MeasurementStatusText.Foreground = Brushes.DarkOrange;
            MeasurementStatusText.Text = "측정 취소 처리가 완료되었습니다.";
        }
        catch (Exception exception)
        {
            MeasurementStatusText.Foreground = Brushes.DarkRed;
            MeasurementStatusText.Text =
                $"측정 처리 오류: {exception.GetType().Name}. 예외 원문은 표시하지 않았습니다.";
        }
        finally
        {
            // Keep the lease through UI cleanup. The using declaration releases
            // it even when a control event throws while restoring the UI.
            _cancelMeasurement = null;
            _measurementRunning = false;
            try { SetMeasurementBusy(false); }
            finally { UpdateRepeatedMeasurementButtons(); }
        }

        return true;
    }
}
