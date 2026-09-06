using System.Reflection;
using WlanLivePathTester.Core.Measurements;
using WlanLivePathTester.Core.Reporting;
using WlanLivePathTester.Windows.Measurements;

namespace WlanLivePathTester.App;

public partial class MainWindow
{
    internal void EnsureRepeatedMeasurementReportTab() => EnsureAuxiliaryReportTab(
        AuxiliaryReportKind.RepeatedMeasurement,
        "반복 보고서",
        "반복 측정 구조화 보고서",
        "현재 실행 중 메모리에 남아 있는 반복 측정의 중앙값·편차·신뢰도와 각 회차 결과를 JSON·CSV·단일 HTML로 저장합니다. 보고서 생성은 네트워크 요청을 만들지 않습니다.",
        "보고서에는 대상 이름과 내부·외부 구분만 기록합니다. 대상 URL, 프록시 주소, PAC URL, SSID와 BSSID는 포함하지 않으며 HTML은 외부 JavaScript·폰트·이미지·iframe을 사용하지 않습니다.",
        "반복 측정 이력은 앱을 종료하면 사라지는 최대 8건의 메모리 이력입니다. 필요한 보고서는 앱을 종료하기 전에 생성하십시오.",
        "저장할 반복 측정 결과가 없습니다. 먼저 반복 측정을 실행하십시오.",
        CaptureAndSaveRepeatedReportAsync);

    private async Task<AuxiliaryReportExport?> CaptureAndSaveRepeatedReportAsync()
    {
        IReadOnlyList<RepeatedMeasurementResult> results = RepeatedMeasurementResultHistory.Snapshot();
        if (results.Count == 0) return null;
        string version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "개발 빌드";
        string directory = GetDefaultReportDirectory();
        return await Task.Run(() =>
        {
            RepeatedMeasurementReportDocument document = RepeatedMeasurementReportWriter.CreateDocument(results, version);
            RepeatedMeasurementReportExportResult export = RepeatedMeasurementReportWriter.WriteAll(document, directory);
            return new AuxiliaryReportExport(export.OutputDirectory, export.JsonPath, export.CsvPath,
                export.HtmlPath, export.Sha256Path, $"측정 요약: {document.Measurements.Count}건");
        });
    }
}
