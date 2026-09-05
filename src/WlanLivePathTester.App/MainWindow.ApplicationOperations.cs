using WlanLivePathTester.Core.Operations;

namespace WlanLivePathTester.App;

public partial class MainWindow
{
    private readonly ApplicationOperationCoordinator
        _applicationOperations = new();
    private ApplicationOperationLease?
        _routeComparisonOperationLeaseV3;
    private ApplicationOperationLease?
        _routeReportOperationLeaseV2;

    internal ApplicationOperationSnapshot
        CurrentApplicationOperation =>
        _applicationOperations.Snapshot;

    private bool TryBeginApplicationOperation(
        ApplicationOperationKind kind,
        Action? requestCancellation,
        out ApplicationOperationLease? lease,
        out string rejectionMessage)
    {
        ApplicationOperationStartResult start =
            _applicationOperations.TryBegin(
                kind,
                requestCancellation);
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
            _ =>
                "현재 앱 실행 상태에서 새 작업을 시작하지 못했습니다."
        };
        return false;
    }

    private static string FormatApplicationOperationKind(
        ApplicationOperationKind kind) =>
        kind switch
        {
            ApplicationOperationKind.DownloadMeasurement =>
                "다운로드 측정",
            ApplicationOperationKind.ProxyRouteResolution =>
                "프록시 경로 판정",
            ApplicationOperationKind.RepeatedMeasurement =>
                "반복 측정",
            ApplicationOperationKind.BrowserObservation =>
                "브라우저 관찰",
            ApplicationOperationKind.RouteEvidence =>
                "로컬 경로 확인",
            ApplicationOperationKind.RouteComparison =>
                "내부 DIRECT·프록시 경로 비교",
            ApplicationOperationKind.WindowsProxyImport =>
                "Windows 프록시 판정 가져오기",
            ApplicationOperationKind.RouteComparisonReportSave =>
                "경로 비교 보고서 저장",
            ApplicationOperationKind.DiagnosticReportSave =>
                "통합 진단 보고서 저장",
            ApplicationOperationKind.NetworkAdapterDiagnostics =>
                "네트워크 어댑터 진단",
            ApplicationOperationKind.NetworkEnvironmentCapture =>
                "네트워크 환경 수집",
            _ => "알 수 없는 작업"
        };

    private static string FormatApplicationCancellationFailure(
        ApplicationOperationCancellationStatus status) =>
        status switch
        {
            ApplicationOperationCancellationStatus.CallbackFailed =>
                "취소 callback 처리에 실패했습니다. 작업이 실제로 끝날 때까지 새 작업은 계속 차단됩니다.",
            ApplicationOperationCancellationStatus.NotSupported =>
                "현재 작업은 즉시 취소를 지원하지 않습니다. 작업이 실제로 끝날 때까지 기다려야 합니다.",
            _ => string.Empty
        };
}
