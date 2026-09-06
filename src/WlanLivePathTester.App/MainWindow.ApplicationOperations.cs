using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using WlanLivePathTester.Core.Operations;

namespace WlanLivePathTester.App;

public partial class MainWindow
{
    private readonly ApplicationOperationCoordinator
        _applicationOperations = new();
    private ApplicationOperationUiSession? _applicationOperationUi;
    private bool _applicationOperationClosePending;
    private bool _applicationOperationWindowClosed;
    private ApplicationOperationUiLease? _observationUiLease;

    internal ApplicationOperationSnapshot CurrentApplicationOperation => _applicationOperations.Snapshot;

    private void InitializeApplicationOperations()
    {
        _applicationOperationUi = new ApplicationOperationUiSession(Dispatcher, _applicationOperations);
        InitializeNetworkAdapterRefreshController();
        Closing += OnApplicationOperationClosing;
        Closed += OnApplicationOperationClosed;
    }

    private ApplicationOperationUiLease? TryBeginUiApplicationOperation(
        ApplicationOperationKind kind, Action? requestCancellation = null, bool showRejection = true)
    {
        Dispatcher.VerifyAccess();
        TabControl? tabs = FindApplicationTabControl();
        if (_applicationOperationWindowClosed || _applicationOperationUi is null
            || tabs?.SelectedItem is not TabItem selected || !selected.IsEnabled)
        {
            if (showRejection) ShowApplicationOperationBlocked("현재 화면에서는 새 작업을 시작할 수 없습니다.");
            return null;
        }
        if (_applicationOperationClosePending || _localDiagnosticSystemSuspended)
        {
            if (showRejection) ShowApplicationOperationBlocked("창 종료 또는 절전 전환 중이므로 새 작업을 시작하지 않았습니다.");
            return null;
        }
        // No feature-specific CTS/Boolean can independently declare the app
        // idle. The same coordinator is authoritative for all operation kinds.
        ApplicationOperationUiLease? lease = _applicationOperationUi.TryBegin(
            kind, tabs, requestCancellation, out ApplicationOperationStartStatus status);
        if (lease is null && showRejection)
            ShowApplicationOperationBlocked(status == ApplicationOperationStartStatus.ShutdownPending
                ? "창 종료를 처리하고 있어 새 작업을 시작하지 않았습니다."
                : $"다른 작업이 진행 중입니다: {FormatApplicationOperationKind(CurrentApplicationOperation.Kind)}. 실제 완료 후 다시 실행하십시오.");
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
        if (_applicationOperationClosePending) { e.Cancel = true; return; }
        // Snapshot also covers a reentrant close inside Core.TryBegin's state
        // notification, before the UI adapter has attached its new lease.
        if (!session.Snapshot.IsBusy) return;
        e.Cancel = true;
        _applicationOperationClosePending = true;
        ShowApplicationOperationBlocked("활성 작업의 종료를 처리하고 있습니다. 실제 호출·파일 정리 완료까지 기다리며 새 작업은 시작하지 않습니다.");
        try
        {
            await session.RequestShutdownAsync();
            if (_applicationOperationWindowClosed || Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished) return;
            // Always post: the lease can finish synchronously from a cancel
            // callback. Never call Close recursively inside the original event.
            await Dispatcher.InvokeAsync(() =>
            {
                if (_applicationOperationWindowClosed) return;
                bool review = KeepWindowOpenForLocalReportReview();
                review |= KeepWindowOpenForAuxiliaryReportReview();
                review |= KeepWindowOpenForRouteReportReview();
                if (review)
                {
                    session.CancelShutdownRequest();
                    return;
                }
                _applicationOperationClosePending = false;
                Close();
                if (!_applicationOperationWindowClosed) session.CancelShutdownRequest();
            }, DispatcherPriority.Background);
        }
        catch (Exception)
        {
            session.CancelShutdownRequest();
            ShowApplicationOperationBlocked("作業 종료 처리를 완료하지 못했습니다. 현재 작업 상태를 확인한 뒤 다시 닫으십시오.".Replace("作業", "작업"));
        }
        finally
        {
            _applicationOperationClosePending = false;
            UpdateRouteProxyImportControls();
            _networkAdapterRefreshController?.StateMayHaveChanged();
        }
    }

    private void OnApplicationOperationClosed(object? sender, EventArgs e)
    {
        _applicationOperationWindowClosed = true;
        DisposeNetworkAdapterRefreshController();
        DisposeAuxiliaryReportSections();
        _applicationOperationUi?.RequestCancellation();
        Closing -= OnApplicationOperationClosing;
        Closed -= OnApplicationOperationClosed;
    }

    private static string FormatApplicationOperationKind(ApplicationOperationKind kind) => kind switch
    {
        ApplicationOperationKind.DownloadMeasurement => "다운로드 측정",
        ApplicationOperationKind.ProxyRouteResolution => "프록시 경로 판정",
        ApplicationOperationKind.RepeatedMeasurement => "반복 측정",
        ApplicationOperationKind.BrowserObservation => "브라우저 관찰",
        ApplicationOperationKind.RouteEvidence => "로컬 경로 확인",
        ApplicationOperationKind.RouteComparison => "내부 DIRECT·프록시 경로 비교",
        ApplicationOperationKind.WindowsProxyImport => "Windows 프록시 판정 가져오기",
        ApplicationOperationKind.RouteComparisonReportSave => "경로 비교 보고서 저장",
        ApplicationOperationKind.DiagnosticReportSave => "진단 보고서 저장",
        ApplicationOperationKind.NetworkAdapterDiagnostics => "네트워크 어댑터 진단",
        ApplicationOperationKind.NetworkEnvironmentCapture => "네트워크 환경 수집",
        _ => "알 수 없는 작업"
    };

    private static string FormatApplicationCancellationFailure(ApplicationOperationCancellationStatus status) => status switch
    {
        ApplicationOperationCancellationStatus.CallbackFailed => "취소 callback 처리에 실패했습니다. 실제 작업 완료 전까지 새 작업은 계속 차단됩니다.",
        ApplicationOperationCancellationStatus.NotSupported => "현재 작업은 즉시 취소를 지원하지 않습니다. 실제 완료까지 기다립니다.",
        ApplicationOperationCancellationStatus.NotActive => "현재 중지할 작업이 없습니다.",
        _ => string.Empty
    };
}
