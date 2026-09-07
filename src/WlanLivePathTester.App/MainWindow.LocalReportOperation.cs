using System.IO;
using System.Text;
using System.Windows;
using WlanLivePathTester.Core.Operations;
using WlanLivePathTester.Core.Reporting;

namespace WlanLivePathTester.App;

public partial class MainWindow
{
    private bool _localReportSaveRunning;
    private bool _localReportReviewAfterClose;

    // Both production and WPF smoke tests use this path. The injected capture
    // and writer do not introduce a second coordinator or a parallel save path.
    internal async Task<bool> RunLocalReportSaveAsync(
        Func<LocalDiagnosticReport> capture,
        Func<LocalDiagnosticReport, Task<LocalReportExportResult>> save)
    {
        Dispatcher.VerifyAccess();
        ArgumentNullException.ThrowIfNull(capture);
        ArgumentNullException.ThrowIfNull(save);
        if (_applicationOperationWindowClosed || _localReportSaveRunning
            || _generateReportButton is not { IsEnabled: true }) return false;

        // The existing LocalReportWriter does not support rollback-aware
        // cancellation. Do not abandon a live write or call it canceled.
        using ApplicationOperationUiLease? lease =
            TryBeginUiApplicationOperation(ApplicationOperationKind.DiagnosticReportSave);
        if (lease is null)
        {
            SetReportResult(MeasurementStatusText.Text);
            return false;
        }

        bool folderWasEnabled = _openReportFolderButton?.IsEnabled == true;
        bool htmlWasEnabled = _openLatestReportButton?.IsEnabled == true;
        bool saved = false;
        _localReportSaveRunning = true;
        _localReportReviewAfterClose = false;
        try
        {
            _generateReportButton.IsEnabled = false;
            if (_openReportFolderButton is not null) _openReportFolderButton.IsEnabled = false;
            if (_openLatestReportButton is not null) _openLatestReportButton.IsEnabled = false;
            SetReportResult("현재 로컬 결과와 최근 경로 비교를 정리하고 있습니다.");
            var run = LatestRouteComparisonRunV3;
            LocalDiagnosticReport report = LocalReportRouteComparison.Attach(capture(), run);
            LocalReportExportResult export = await save(report);
            if (export is null) throw new InvalidOperationException("REPORT_EXPORT_MISSING");
            if (_applicationOperationWindowClosed || !lease.IsCurrent) return false;

            _lastReportDirectory = export.OutputDirectory;
            _lastReportHtmlPath = export.HtmlPath;
            saved = true;
            StringBuilder builder = new();
            builder.AppendLine("로컬 보고서 생성 완료");
            builder.AppendLine(GuidedReportSummary.Render(report));
            builder.AppendLine($"폴더: {export.OutputDirectory}");
            builder.AppendLine($"JSON: {Path.GetFileName(export.JsonPath)}");
            builder.AppendLine($"CSV: {Path.GetFileName(export.CsvPath)}");
            builder.AppendLine($"HTML: {Path.GetFileName(export.HtmlPath)}");
            builder.AppendLine($"무결성: {Path.GetFileName(export.Sha256Path)}");
            builder.AppendLine($"구조화 다운로드 결과: {report.StructuredMeasurements?.Count ?? 0}개");
            builder.AppendLine($"경로 비교: {report.InternalProxyRouteComparison?.RunStatus ?? "미실행"}");
            builder.AppendLine("경로 비교를 다시 실행하거나 결과를 외부로 전송하지 않았습니다.");
            SetReportResult(builder.ToString().TrimEnd());
            return true;
        }
        catch (Exception exception)
        {
            // A failed save must not silently close the window and hide a
            // possibly partial legacy file set. A subsequent user close is allowed.
            _localReportReviewAfterClose = _applicationOperationClosePending;
            SetReportResult($"보고서 저장 오류: {exception.GetType().Name}. 이전 성공 보고서는 유지합니다. 출력 폴더를 확인하십시오. 예외 원문은 표시하지 않았습니다.");
            return false;
        }
        finally
        {
            _localReportSaveRunning = false;
            if (!_applicationOperationWindowClosed)
            {
                _generateReportButton.IsEnabled = true;
                if (_openReportFolderButton is not null) _openReportFolderButton.IsEnabled = saved || folderWasEnabled;
                if (_openLatestReportButton is not null) _openLatestReportButton.IsEnabled = saved || htmlWasEnabled;
            }
            // The using declaration restores peer tabs and releases the Core
            // lease only after the local report controls and paths have settled.
        }
    }

    private bool KeepWindowOpenForLocalReportReview()
    {
        if (!_localReportReviewAfterClose) return false;
        _localReportReviewAfterClose = false;
        SetReportResult((_reportResultText?.Text ?? string.Empty)
            + "\n저장 오류를 확인하도록 창을 유지했습니다. 확인 후 다시 닫을 수 있습니다.");
        return true;
    }

    private void OnLocalReportWindowClosed(object? sender, EventArgs e)
    {
        if (_generateReportButton is not null) _generateReportButton.Click -= OnGenerateReportClick;
        if (_openReportFolderButton is not null) _openReportFolderButton.Click -= OnOpenReportFolderClick;
        if (_openLatestReportButton is not null) _openLatestReportButton.Click -= OnOpenLatestReportClick;
        Closed -= OnLocalReportWindowClosed;
    }
}
