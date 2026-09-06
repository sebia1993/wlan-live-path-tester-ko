using System.Reflection;
using WlanLivePathTester.Core.NetworkEnvironment;
using WlanLivePathTester.Core.Reporting;
using WlanLivePathTester.Windows.NetworkEnvironment;

namespace WlanLivePathTester.App;

public partial class MainWindow
{
    internal void EnsureNetworkEnvironmentReportTab() => EnsureAuxiliaryReportTab(
        AuxiliaryReportKind.NetworkEnvironment,
        "인터페이스 보고서",
        "인터페이스 환경 구조화 보고서",
        "현재 로컬 인터페이스 환경을 다시 수집해 JSON·CSV·단일 HTML과 SHA-256으로 저장합니다. 보고서 생성 과정은 네트워크 요청을 만들지 않습니다.",
        "인터페이스 이름·설명·GUID·IP·게이트웨이·DNS·MAC 주소 원문은 보고서에 넣지 않습니다. 어댑터는 번호, 범주, 상태, 링크 속도, 주소 계열·개수, 게이트웨이 유무, VPN·가상 여부로만 기록합니다.",
        "익명화된 보고서도 회사 밖으로 공유하기 전에는 내용을 직접 다시 확인하십시오. 실제 목적지 경로는 이 보고서만으로 확정할 수 없습니다.",
        "저장할 인터페이스 환경 결과가 없습니다.",
        CaptureAndSaveEnvironmentReportAsync);

    private async Task<AuxiliaryReportExport?> CaptureAndSaveEnvironmentReportAsync()
    {
        string version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "개발 빌드";
        string directory = GetDefaultReportDirectory();
        return await Task.Run(() =>
        {
            LocalNetworkEnvironmentSnapshot snapshot = LocalNetworkEnvironmentReader.ReadCurrent();
            NetworkEnvironmentReportDocument document = NetworkEnvironmentReportWriter.CreateDocument(snapshot, version);
            NetworkEnvironmentReportExportResult export = NetworkEnvironmentReportWriter.WriteAll(document, directory);
            return new AuxiliaryReportExport(export.OutputDirectory, export.JsonPath, export.CsvPath,
                export.HtmlPath, export.Sha256Path,
                $"익명화된 인터페이스: {document.Adapters.Count}개\n판정: {document.Findings.Count}개");
        });
    }
}
