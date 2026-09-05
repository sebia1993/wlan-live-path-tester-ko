using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WlanLivePathTester.Core.Operations;

namespace WlanLivePathTester.App;

public partial class MainWindow
{
    // One owner shared by both the existing route/import handlers and the
    // measurement/observation UI lifetime adapter.
    private readonly ApplicationOperationCoordinator
        _applicationOperations = new();
    private ApplicationOperationLease? _routeComparisonOperationLeaseV3;
    private ApplicationOperationLease? _routeReportOperationLeaseV2;
    private ApplicationOperationUiSession? _applicationOperationUi;
    private bool _applicationOperationClosePending;
    private bool _applicationOperationWindowClosed;
    private ApplicationOperationUiLease? _observationUiLease;

    internal ApplicationOperationSnapshot CurrentApplicationOperation =>
        _applicationOperations.Snapshot;

    private bool TryBeginApplicationOperation(
        ApplicationOperationKind kind,
        Action? requestCancellation,
        out ApplicationOperationLease? lease,
        out string rejectionMessage)
    {
        ApplicationOperationStartResult start =
            _applicationOperations.TryBegin(kind, requestCancellation);
        lease = start.Lease;
        if (start.Started)
        {
            rejectionMessage = string.Empty;
            return true;
        }

        rejectionMessage = start.Status switch
        {
            ApplicationOperationStartStatus.ShutdownPending =>
                "창 종료 처리가 진행 중이므로 새 작업을 시작하지 않았습니다.",
            ApplicationOperationStartStatus.Busy =>
                $"다른 작업이 진행 중입니다: {FormatApplicationOperationKind(start.Snapshot.Kind)}. 완료하거나 취소한 뒤 다시 실행하십시오.",
            _ => "현재 앱 실행 상태에서 새 작업을 시작하지 못했습니다."
        };
        return false;
    }

    private void InitializeApplicationOperations()
    {
        _applicationOperationUi = new ApplicationOperationUiSession(
            Dispatcher, _applicationOperations);
        Closing += OnApplicationOperationClosing;
        Closed += OnApplicationOperationClosed;
    }

    private ApplicationOperationUiLease? TryBeginUiApplicationOperation(
        ApplicationOperationKind kind,
        Action? requestCancellation = null)
    {
        Dispatcher.VerifyAccess();
        TabControl? tabs = FindVisualDescendant<TabControl>(this);
        // A constructed but not yet shown Window can own initialized content
        // without having its content presenter in the Window visual tree.
        // Resolve the same tab host from that content; never invent a host or
        // bypass the selected/enabled-tab and shared-lease checks below.
        if (tabs is null && Content is DependencyObject contentRoot)
        {
            tabs = contentRoot as TabControl
                ?? FindVisualDescendant<TabControl>(contentRoot);
        }
        if (_applicationOperationWindowClosed || _applicationOperationUi is null
            || tabs?.SelectedItem is not TabItem selected || !selected.IsEnabled)
        {
            ShowApplicationOperationBlocked("현재 화면에서는 새 작업을 시작할 수 없습니다.");
            return null;
        }

        // Compatibility guard for feature handlers not yet migrated to leases.
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
        // Route import/report features already have their own deferred-close
        // handler. Do not start a competing Close continuation for their lease.
        if (!session.HasActiveUiLease) return;

        e.Cancel = true;
        _applicationOperationClosePending = true;
        ShowApplicationOperationBlocked(
            "활성 작업의 종료를 처리하고 있습니다. 동기 Windows 호출은 반환될 때까지 기다리며 새 작업은 시작하지 않습니다.");
        try
        {
            await session.RequestShutdownAsync();
            _applicationOperationClosePending = false;
            if (!_applicationOperationWindowClosed
                && !Dispatcher.HasShutdownStarted && !Dispatcher.HasShutdownFinished)
            {
                Close();
                // Preserve the ability to operate when a different Closing
                // handler vetoes the final close attempt.
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

    private static string FormatApplicationOperationKind(ApplicationOperationKind kind) =>
        kind switch
        {
            ApplicationOperationKind.DownloadMeasurement => "다운로드 측정",
            ApplicationOperationKind.ProxyRouteResolution => "프록시 경로 판정",
            ApplicationOperationKind.RepeatedMeasurement => "반복 측정",
            ApplicationOperationKind.BrowserObservation => "브라우저 관찰",
            ApplicationOperationKind.RouteEvidence => "로컬 경로 확인",
            ApplicationOperationKind.RouteComparison => "내부 DIRECT·프록시 경로 비교",
            ApplicationOperationKind.WindowsProxyImport => "Windows 프록시 판정 가져오기",
            ApplicationOperationKind.RouteComparisonReportSave => "경로 비교 보고서 저장",
            ApplicationOperationKind.DiagnosticReportSave => "통합 진단 보고서 저장",
            ApplicationOperationKind.NetworkAdapterDiagnostics => "네트워크 어댑터 진단",
            ApplicationOperationKind.NetworkEnvironmentCapture => "네트워크 환경 수집",
            _ => "알 수 없는 작업"
        };

    private static string FormatApplicationCancellationFailure(ApplicationOperationCancellationStatus status) =>
        status switch
        {
            ApplicationOperationCancellationStatus.CallbackFailed =>
                "취소 callback 처리에 실패했습니다. 작업이 실제로 끝날 때까지 새 작업은 계속 차단됩니다.",
            ApplicationOperationCancellationStatus.NotSupported =>
                "현재 작업은 즉시 취소를 지원하지 않습니다. 작업이 실제로 끝날 때까지 기다려야 합니다.",
            _ => string.Empty
        };
}
