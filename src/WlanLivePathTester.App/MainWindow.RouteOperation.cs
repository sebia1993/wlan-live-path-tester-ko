using System.Windows.Media;
using WlanLivePathTester.Core.Operations;

namespace WlanLivePathTester.App;

public partial class MainWindow
{
    private bool _routeOperationRunning;

    // The production entry points and synthetic WPF tests use this same path.
    // RunLocalDiagnosticAsync owns the CTS, UI lease, deadline and suspend rule.
    internal Task<bool> RunRouteUiOperationAsync<T>(
        ApplicationOperationKind kind,
        Func<CancellationToken, Task<T>> collect,
        Action<T> apply)
    {
        Dispatcher.VerifyAccess();
        ArgumentNullException.ThrowIfNull(collect);
        ArgumentNullException.ThrowIfNull(apply);
        if (kind is not (ApplicationOperationKind.RouteComparison
            or ApplicationOperationKind.WindowsProxyImport))
            throw new ArgumentOutOfRangeException(nameof(kind));
        if (_applicationOperationWindowClosed || _routeOperationRunning
            || _routeComparisonTabV3?.IsEnabled != true)
            return Task.FromResult(false);

        return RunLocalDiagnosticAsync(kind, collect, apply,
            busy =>
            {
                _routeOperationRunning = busy;
                SetRouteComparisonBusyV3(isBusy: busy);
                UpdateRouteProxyImportControls();
            },
            (message, brush) =>
            {
                SetRouteComparisonResultV3(message, brush);
                if (kind == ApplicationOperationKind.WindowsProxyImport)
                    SetRouteProxyImportSummary(message);
            });
    }

    private void RequestRouteOperationCancellation()
    {
        Dispatcher.VerifyAccess();
        ApplicationOperationKind kind = CurrentApplicationOperation.Kind;
        if (!_routeOperationRunning || kind is not
            (ApplicationOperationKind.RouteComparison or ApplicationOperationKind.WindowsProxyImport)) return;
        ApplicationOperationCancellationStatus status = CancelLocalDiagnostic(kind);
        if (_routeComparisonCancelV3 is not null) _routeComparisonCancelV3.IsEnabled = false;
        string failure = FormatApplicationCancellationFailure(status);
        string message = string.IsNullOrEmpty(failure)
            ? "중지 요청됨 · 현재 호출이 실제로 반환될 때까지 기다립니다. 늦은 결과는 적용하지 않습니다."
            : failure;
        SetRouteComparisonResultV3(message,
            status == ApplicationOperationCancellationStatus.CallbackFailed ? Brushes.DarkRed : Brushes.DarkOrange);
        if (kind == ApplicationOperationKind.WindowsProxyImport) SetRouteProxyImportSummary(message);
    }
}
