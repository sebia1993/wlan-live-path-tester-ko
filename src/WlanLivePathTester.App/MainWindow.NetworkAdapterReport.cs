using System.Reflection;
using WlanLivePathTester.Core.Adapters;
using WlanLivePathTester.Core.Reporting;
using WlanLivePathTester.Windows.Adapters;
using WlanLivePathTester.Windows.Wlan;

namespace WlanLivePathTester.App;

public partial class MainWindow
{
    internal void EnsureNetworkAdapterReportTab() => EnsureAuxiliaryReportTab(
        AuxiliaryReportKind.NetworkAdapter,
        "어댑터 보고서",
        "어댑터 진단 구조화 보고서",
        "현재 물리 Wi-Fi 선택 결과, 다중 NIC·VPN·가상 어댑터 분류와 경고를 JSON·CSV·단일 HTML로 저장합니다. 보고서 생성은 로컬 인터페이스 정보만 읽으며 네트워크 요청을 만들지 않습니다.",
        "IP·MAC·게이트웨이 주소와 전체 인터페이스 GUID는 저장하지 않습니다. 인터페이스 ID는 SHA-256 앞 10자리 지문으로만 기록하고, HTML은 외부 JavaScript·CSS·웹폰트·이미지·iframe을 포함하지 않습니다.",
        "분류 규칙은 모든 기업 VPN·보안 에이전트·드라이버를 완전히 식별하지 못할 수 있습니다. 회사 밖으로 공유하기 전에는 파일 내용을 직접 다시 확인하십시오.",
        "저장할 어댑터 진단 결과가 없습니다.",
        CaptureAndSaveAdapterReportAsync);

    private async Task<AuxiliaryReportExport?> CaptureAndSaveAdapterReportAsync()
    {
        string version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "개발 빌드";
        string directory = GetDefaultReportDirectory();
        return await Task.Run(() =>
        {
            string? connectedInterfaceId = NativeWlanReader.ReadCurrent().FirstConnectedInterface?.InterfaceId;
            NetworkAdapterInventoryReadResult inventory = NetworkAdapterInventoryReader.Read(connectedInterfaceId);
            WirelessAdapterSelectionResult selection = NetworkAdapterSelector.Select(inventory.Adapters);
            if (inventory.Warnings.Count > 0)
                selection = selection with
                {
                    Warnings = inventory.Warnings.Concat(selection.Warnings).Distinct(StringComparer.Ordinal).ToArray()
                };
            NetworkAdapterDiagnosticsReportDocument document = NetworkAdapterDiagnosticsReportWriter.CreateDocument(selection, version);
            NetworkAdapterDiagnosticsReportExportResult export = NetworkAdapterDiagnosticsReportWriter.WriteAll(document, directory);
            return new AuxiliaryReportExport(export.OutputDirectory, export.JsonPath, export.CsvPath,
                export.HtmlPath, export.Sha256Path,
                $"선택 상태: {document.SelectionStatus}\n어댑터: {document.Adapters.Count}개\n경고: {document.Warnings.Count}개");
        });
    }
}
