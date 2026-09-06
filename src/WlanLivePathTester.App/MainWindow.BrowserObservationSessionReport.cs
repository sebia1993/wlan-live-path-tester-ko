using System.Reflection;
using WlanLivePathTester.Core.Observation;
using WlanLivePathTester.Core.Reporting;

namespace WlanLivePathTester.App;

public partial class MainWindow
{
    internal void EnsureBrowserObservationSessionReportTab() => EnsureAuxiliaryReportTab(
        AuxiliaryReportKind.BrowserObservation,
        "관찰 보고서",
        "브라우저 관찰 전용 보고서",
        "가장 최근 브라우저 다운로드 관찰의 종료 원인, 처리량 요약과 시간축 샘플을 JSON·CSV·단일 HTML로 저장합니다. 보고서 생성은 새로운 네트워크 요청을 만들지 않습니다.",
        "SSID·BSSID·인터페이스 ID·이름·설명·IP·MAC·게이트웨이·DNS·다운로드 URL은 포함하지 않습니다. 각 파일의 SHA-256 목록을 함께 생성하며 외부 업로드는 수행하지 않습니다.",
        "관찰 결과는 앱 메모리에만 유지됩니다. 앱을 종료하기 전에 필요한 보고서를 생성하고, 회사 밖으로 공유하기 전에는 JSON·CSV·HTML을 직접 다시 확인하십시오.",
        "저장할 브라우저 관찰 결과가 없습니다. 먼저 브라우저 관찰을 실행하십시오.",
        CaptureAndSaveObservationReportAsync);

    private async Task<AuxiliaryReportExport?> CaptureAndSaveObservationReportAsync()
    {
        BrowserObservationResult? result = _lastBrowserObservationResult;
        if (result is null) return null;
        string version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "개발 빌드";
        string directory = GetDefaultReportDirectory();
        return await Task.Run(() =>
        {
            BrowserObservationSessionReportDocument document = BrowserObservationSessionReportWriter.CreateDocument(result, version);
            BrowserObservationSessionReportExportResult export = BrowserObservationSessionReportWriter.WriteAll(document, directory);
            BrowserObservationTerminationReason reason = result.EffectiveTerminationReason;
            string summary = $"상태: {document.Status}\n종료 원인: {document.TerminationDisplay} ({document.TerminationReason})\n시간축 샘플: {document.Summary?.Samples.Count ?? 0}개";
            return new AuxiliaryReportExport(export.OutputDirectory, export.JsonPath, export.CsvPath,
                export.HtmlPath, export.Sha256Path, summary,
                IsWarning: reason == BrowserObservationTerminationReason.CanceledByUser,
                IsError: reason is not (BrowserObservationTerminationReason.Completed or BrowserObservationTerminationReason.CanceledByUser));
        });
    }
}
