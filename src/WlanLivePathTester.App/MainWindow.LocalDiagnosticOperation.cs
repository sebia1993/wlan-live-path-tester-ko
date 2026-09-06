using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WlanLivePathTester.Core.Operations;

namespace WlanLivePathTester.App;

public partial class MainWindow
{
    private ApplicationOperationUiLease? _localDiagnosticUiLease;
    private bool _localDiagnosticSystemSuspended;
    private bool _localDiagnosticInterruptedBySuspend;

    // True means a lease was acquired, not that collection succeeded. Automatic
    // refresh uses this to distinguish a rejected attempt from a native error.
    internal async Task<bool> RunLocalDiagnosticAsync<T>(
        ApplicationOperationKind kind,
        Func<CancellationToken, Task<T>> collect,
        Action<T> apply,
        Action<bool> setBusy,
        Action<string, Brush> setStatus,
        bool automatic = false,
        TimeSpan? cooperativeTimeout = null)
    {
        Dispatcher.VerifyAccess();
        ArgumentNullException.ThrowIfNull(collect);
        ArgumentNullException.ThrowIfNull(apply);
        ArgumentNullException.ThrowIfNull(setBusy);
        ArgumentNullException.ThrowIfNull(setStatus);
        if (kind is not (ApplicationOperationKind.RouteEvidence
            or ApplicationOperationKind.NetworkAdapterDiagnostics
            or ApplicationOperationKind.NetworkEnvironmentCapture
            or ApplicationOperationKind.RouteComparison
            or ApplicationOperationKind.WindowsProxyImport
            or ApplicationOperationKind.ProxyRouteResolution))
            throw new ArgumentOutOfRangeException(nameof(kind));
        if (automatic && kind != ApplicationOperationKind.NetworkAdapterDiagnostics)
            throw new ArgumentException("AUTOMATIC_DIAGNOSTIC_KIND_INVALID", nameof(kind));
        TimeSpan timeout = cooperativeTimeout ?? TimeSpan.FromSeconds(30);
        if (timeout <= TimeSpan.Zero || timeout > TimeSpan.FromMinutes(5))
            throw new ArgumentOutOfRangeException(nameof(cooperativeTimeout));
        if (_applicationOperationWindowClosed) return false;
        if (_localDiagnosticSystemSuspended)
        {
            if (!automatic) setStatus("시스템 절전 전환 중이므로 새 진단을 시작하지 않았습니다.", Brushes.DarkOrange);
            return false;
        }

        using CancellationTokenSource userCancellation = new();
        using CancellationTokenSource deadline = new();
        using CancellationTokenSource combined = CancellationTokenSource.CreateLinkedTokenSource(
            userCancellation.Token, deadline.Token);
        using ApplicationOperationUiLease? lease = TryBeginUiApplicationOperation(
            kind, userCancellation.Cancel, showRejection: !automatic);
        if (lease is null)
        {
            if (!automatic) setStatus(MeasurementStatusText.Text, Brushes.DarkOrange);
            return false;
        }
        _localDiagnosticUiLease = lease;
        _localDiagnosticInterruptedBySuspend = false;
        try
        {
            setBusy(true);
            setStatus("진단 정보를 수집하고 있습니다. 중지해도 현재 Windows 호출의 실제 반환까지 기다립니다.", Brushes.DarkSlateGray);
            deadline.CancelAfter(timeout);
            combined.Token.ThrowIfCancellationRequested();
            T result = await collect(combined.Token);
            // Never abandon a native call that ignores cancellation. After its
            // actual return, exclude a snapshot made obsolete by cancellation.
            combined.Token.ThrowIfCancellationRequested();
            if (lease.IsCurrent && !_applicationOperationWindowClosed && !_localDiagnosticSystemSuspended)
                apply(result);
        }
        catch (OperationCanceledException) when (combined.IsCancellationRequested)
        {
            if (!_applicationOperationWindowClosed)
            {
                string message = _localDiagnosticInterruptedBySuspend
                    ? "시스템 절전 전환으로 진단을 중단했습니다. 전환 전의 늦은 결과는 적용하지 않았습니다."
                    : deadline.IsCancellationRequested
                        ? "진단 제한 시간이 지나 결과를 적용하지 않았습니다. 현재 호출이 반환된 뒤 작업을 정리했습니다."
                        : "진단 중지를 처리했습니다. 취소 후 반환된 결과는 적용하지 않았습니다.";
                setStatus(message, Brushes.DarkOrange);
            }
        }
        catch (Exception exception)
        {
            if (!_applicationOperationWindowClosed)
                setStatus($"진단 처리 오류: {exception.GetType().Name}. 예외 원문은 표시하지 않았습니다.", Brushes.DarkRed);
        }
        finally
        {
            try { if (!_applicationOperationWindowClosed) setBusy(false); }
            finally
            {
                if (ReferenceEquals(_localDiagnosticUiLease, lease)) _localDiagnosticUiLease = null;
                _localDiagnosticInterruptedBySuspend = false;
            }
            // The using declarations restore peer bindings and release the
            // lease before disposing the linked cancellation sources.
        }
        return true;
    }

    private ApplicationOperationCancellationStatus CancelLocalDiagnostic(ApplicationOperationKind expectedKind)
    {
        Dispatcher.VerifyAccess();
        if (_localDiagnosticUiLease is not { IsCurrent: true } lease || CurrentApplicationOperation.Kind != expectedKind)
            return ApplicationOperationCancellationStatus.NotActive;
        return lease.RequestCancellation();
    }

    private void HandleLocalDiagnosticPowerTransition(bool suspended)
    {
        Dispatcher.VerifyAccess();
        if (_applicationOperationWindowClosed) return;
        _localDiagnosticSystemSuspended = suspended;
        if (suspended)
        {
            _networkAdapterRefreshController?.Suspend();
            if (_localDiagnosticUiLease is { IsCurrent: true } lease)
            {
                _localDiagnosticInterruptedBySuspend = true;
                lease.RequestCancellation();
            }
        }
        else _networkAdapterRefreshController?.Resume();
    }

    private TabControl? FindApplicationTabControl()
    {
        Dispatcher.VerifyAccess();
        TabControl? host = FindVisualDescendant<TabControl>(this);
        return host ?? (Content is DependencyObject content
            ? content as TabControl ?? FindVisualDescendant<TabControl>(content) : null);
    }
}
