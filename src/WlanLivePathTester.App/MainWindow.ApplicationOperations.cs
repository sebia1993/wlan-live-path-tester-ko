using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WlanLivePathTester.Core.Operations;

namespace WlanLivePathTester.App;

public partial class MainWindow
{
    private ApplicationOperationUiSession? _applicationOperationUi;
    private bool _applicationOperationClosePending;
    private bool _applicationOperationWindowClosed;
    private ApplicationOperationUiLease? _observationUiLease;

    private void InitializeApplicationOperations()
    {
        _applicationOperationUi = new ApplicationOperationUiSession(Dispatcher);
        Closing += OnApplicationOperationClosing;
        Closed += OnApplicationOperationClosed;
    }

    private ApplicationOperationUiLease? TryBeginUiApplicationOperation(
        ApplicationOperationKind kind,
        Action? requestCancellation = null)
    {
        Dispatcher.VerifyAccess();
        TabControl? tabs = FindVisualDescendant<TabControl>(this);
        if (_applicationOperationWindowClosed || _applicationOperationUi is null
            || tabs?.SelectedItem is not TabItem selected || !selected.IsEnabled)
        {
            ShowApplicationOperationBlocked("현재 화면에서는 새 작업을 시작할 수 없습니다.");
            return null;
        }

        // Compatibility boundary while the remaining feature handlers migrate
        // to the shared lease. Do not treat their still-running work as idle.
        if (_measurementRunning || _observationCancellation is not null
            || _routeComparisonCancellationV3 is not null
            || _routeProxyOperationCompletion is { Task.IsCompleted: false }
            || RouteReportSaveBusy)
        {
            ShowApplicationOperationBlocked(
                "측정·관찰·경로 작업 또는 보고서 저장이 진행 중입니다. 완료하거나 중지한 뒤 다시 실행하십시오.");
            return null;
        }

        ApplicationOperationUiLease? lease = _applicationOperationUi.TryBegin(
            kind, tabs, requestCancellation, out ApplicationOperationStartStatus status);
        if (lease is null)
        {
            ShowApplicationOperationBlocked(status == ApplicationOperationStartStatus.ShutdownPending
                ? "창 종료를 처리하고 있어 새 작업을 시작하지 않았습니다."
                : "다른 작업이 종료 처리 중입니다. 실제 완료 후 다시 실행하십시오.");
        }
        return lease;
    }

    private void ShowApplicationOperationBlocked(string message)
    {
        if (_applicationOperationWindowClosed) return;
        MeasurementStatusText.Foreground = Brushes.DarkOrange;
        MeasurementStatusText.Text = message;
    }

    private async void OnApplicationOperationClosing(object? sender, CancelEventArgs e)
    {
        ApplicationOperationUiSession? session = _applicationOperationUi;
        if (session is null || _applicationOperationWindowClosed) return;
        if (_applicationOperationClosePending)
        {
            e.Cancel = true;
            return;
        }
        if (!session.Snapshot.IsBusy) return;

        e.Cancel = true;
        _applicationOperationClosePending = true;
        ShowApplicationOperationBlocked(
            "활성 작업의 종료를 처리하고 있습니다. 동기 Windows 호출은 반환될 때까지 기다리며 새 작업은 시작하지 않습니다.");
        try
        {
            // Never block the WPF dispatcher with Wait/Result: the active
            // operation needs its continuation to restore UI and release its lease.
            await session.RequestShutdownAsync();
            _applicationOperationClosePending = false;
            if (!_applicationOperationWindowClosed
                && !Dispatcher.HasShutdownStarted && !Dispatcher.HasShutdownFinished)
            {
                Close();
                // Another Closing handler may veto the second close attempt.
                if (!_applicationOperationWindowClosed) session.CancelShutdownRequest();
            }
        }
        catch (Exception)
        {
            session.CancelShutdownRequest();
            ShowApplicationOperationBlocked(
                "작업 종료 처리를 완료하지 못했습니다. 현재 작업 상태를 확인한 뒤 다시 닫으십시오.");
        }
        finally
        {
            _applicationOperationClosePending = false;
        }
    }

    private void OnApplicationOperationClosed(object? sender, EventArgs e)
    {
        _applicationOperationWindowClosed = true;
        _applicationOperationUi?.RequestCancellation();
        Closing -= OnApplicationOperationClosing;
        Closed -= OnApplicationOperationClosed;
    }
}
