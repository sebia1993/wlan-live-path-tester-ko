using System.Reflection;
using WlanLivePathTester.Core.Reporting;
using WlanLivePathTester.Core.Routing;
using WlanLivePathTester.Windows.Routing;

namespace WlanLivePathTester.App;

public partial class MainWindow
{
    internal void EnsureRouteEvidenceReportTab() => EnsureAuxiliaryReportTab(
        AuxiliaryReportKind.RouteEvidence,
        "라우팅 보고서",
        "라우팅 근거 구조화 보고서",
        "현재 앱 실행에서 확인한 최근 라우팅 근거 최대 12건을 JSON·CSV·단일 HTML과 SHA-256으로 저장합니다. 보고서 생성 자체는 네트워크 요청을 만들지 않습니다.",
        "보고서에는 라우팅 목적·상태·DNS 사용 여부·주소 수·인터페이스 범주·짧은 ID 지문만 기록합니다. 해석한 IP, 게이트웨이, DNS 서버, MAC, 인터페이스 이름·설명과 전체 GUID는 모델 자체에 포함하지 않습니다.",
        "라우팅 이력은 앱 종료 시 사라지는 메모리 이력입니다. 외부 사이트 참고 경로는 회사 프록시의 실제 HTTP 연결 경로가 아닐 수 있으므로 프록시 경로 판정과 함께 해석하십시오.",
        "저장할 라우팅 근거가 없습니다. 먼저 라우팅 근거 탭에서 내부 대상 또는 프록시 엔드포인트를 확인하십시오.",
        CaptureAndSaveRouteEvidenceReportAsync);

    private async Task<AuxiliaryReportExport?> CaptureAndSaveRouteEvidenceReportAsync()
    {
        // Invoked only after DiagnosticReportSave has acquired the shared lease.
        IReadOnlyList<DestinationRouteEvidence> results = RouteEvidenceResultHistory.Snapshot();
        if (results.Count == 0) return null;
        string version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "개발 빌드";
        string directory = GetDefaultReportDirectory();
        return await Task.Run(() =>
        {
            RouteEvidenceReportDocument document = RouteEvidenceReportWriter.CreateDocument(results, version);
            RouteEvidenceReportExportResult export = RouteEvidenceReportWriter.WriteAll(document, directory);
            return new AuxiliaryReportExport(export.OutputDirectory, export.JsonPath, export.CsvPath,
                export.HtmlPath, export.Sha256Path, $"라우팅 결과: {document.Results.Count}건");
        });
    }
}
