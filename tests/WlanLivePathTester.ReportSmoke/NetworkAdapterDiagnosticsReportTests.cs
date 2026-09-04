using System.Net.NetworkInformation;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using WlanLivePathTester.Core.Adapters;
using WlanLivePathTester.Core.Reporting;

namespace WlanLivePathTester.ReportSmoke;

internal static class NetworkAdapterDiagnosticsReportTests
{
#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Run()
    {
        WirelessAdapterSelectionResult selection = CreateSelection();
        NetworkAdapterDiagnosticsReportDocument document =
            NetworkAdapterDiagnosticsReportWriter.CreateDocument(
                selection,
                "0.1.0-test",
                DateTimeOffset.UnixEpoch.AddHours(9));

        VerifyJson(document);
        VerifyCsv(document);
        VerifyHtml(document);
        VerifyLocalFiles(document);
        Console.WriteLine("PASS adapter diagnostics JSON CSV HTML and SHA-256 report tests");
    }

    private static void VerifyJson(
        NetworkAdapterDiagnosticsReportDocument document)
    {
        string json = NetworkAdapterDiagnosticsReportWriter.RenderJson(
            document);
        using JsonDocument parsed = JsonDocument.Parse(json);

        Ensure(parsed.RootElement
                .GetProperty("selectionStatus")
                .GetString() == "Selected",
            "JSON에 Wi-Fi 선택 상태가 필요합니다.");
        Ensure(parsed.RootElement
                .GetProperty("adapters")
                .GetArrayLength() == 3,
            "JSON에 세 합성 어댑터가 필요합니다.");
        Ensure(!json.Contains(
                "11111111-1111-1111-1111-111111111111",
                StringComparison.OrdinalIgnoreCase),
            "JSON에 전체 인터페이스 GUID가 남으면 안 됩니다.");
        Ensure(!json.Contains("192.168.10.20", StringComparison.Ordinal),
            "JSON에 IP 주소가 남으면 안 됩니다.");
        Ensure(!json.Contains("AA:BB:CC:DD:EE:FF", StringComparison.OrdinalIgnoreCase),
            "JSON에 MAC 주소가 남으면 안 됩니다.");
        Ensure(document.SelectedAdapterFingerprint?.Length == 10,
            "선택 ID는 SHA-256 앞 10자리 지문이어야 합니다.");
    }

    private static void VerifyCsv(
        NetworkAdapterDiagnosticsReportDocument document)
    {
        string csv = NetworkAdapterDiagnosticsReportWriter.RenderCsv(
            document);

        Ensure(csv.StartsWith("section,key,value", StringComparison.Ordinal),
            "CSV는 section,key,value 스키마를 사용해야 합니다.");
        Ensure(csv.Contains(
                "\"selection\",\"status\",\"Selected\"",
                StringComparison.Ordinal),
            "CSV에 선택 상태 행이 필요합니다.");
        Ensure(csv.Contains("'=HYPERLINK", StringComparison.Ordinal),
            "CSV에서 수식 시작 어댑터 이름을 비활성화해야 합니다.");
        Ensure(!csv.Contains(
                "11111111-1111-1111-1111-111111111111",
                StringComparison.OrdinalIgnoreCase),
            "CSV에 전체 인터페이스 GUID가 남으면 안 됩니다.");
        Ensure(!csv.Contains("192.168.10.20", StringComparison.Ordinal),
            "CSV에 IP 주소가 남으면 안 됩니다.");
    }

    private static void VerifyHtml(
        NetworkAdapterDiagnosticsReportDocument document)
    {
        string html = NetworkAdapterDiagnosticsReportWriter.RenderHtml(
            document);

        Ensure(html.StartsWith("<!doctype html>", StringComparison.OrdinalIgnoreCase),
            "HTML5 doctype이 필요합니다.");
        Ensure(html.Contains("Content-Security-Policy", StringComparison.Ordinal),
            "HTML에 CSP가 필요합니다.");
        Ensure(html.Contains("네트워크 어댑터 진단 보고서", StringComparison.Ordinal),
            "HTML에 보고서 제목이 필요합니다.");
        Ensure(!html.Contains("<script", StringComparison.OrdinalIgnoreCase),
            "HTML에 스크립트를 포함하면 안 됩니다.");
        Ensure(!html.Contains("<iframe", StringComparison.OrdinalIgnoreCase),
            "HTML에 iframe을 포함하면 안 됩니다.");
        Ensure(!html.Contains("<link", StringComparison.OrdinalIgnoreCase),
            "HTML에 외부 스타일시트 링크를 포함하면 안 됩니다.");
        Ensure(!html.Contains(
                "11111111-1111-1111-1111-111111111111",
                StringComparison.OrdinalIgnoreCase),
            "HTML에 전체 인터페이스 GUID가 남으면 안 됩니다.");
        Ensure(!html.Contains("192.168.10.20", StringComparison.Ordinal),
            "HTML에 IP 주소가 남으면 안 됩니다.");
        Ensure(html.Contains("=HYPERLINK", StringComparison.Ordinal),
            "HTML은 수식 모양 이름을 실행하지 않고 텍스트로 표시해야 합니다.");
    }

    private static void VerifyLocalFiles(
        NetworkAdapterDiagnosticsReportDocument document)
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "WlanAdapterReportSmoke",
            Guid.NewGuid().ToString("N"));

        try
        {
            NetworkAdapterDiagnosticsReportExportResult export =
                NetworkAdapterDiagnosticsReportWriter.WriteAll(
                    document,
                    directory,
                    "합성 어댑터 보고서");

            string[] files =
            [
                export.JsonPath,
                export.CsvPath,
                export.HtmlPath,
                export.Sha256Path
            ];
            Ensure(files.All(File.Exists),
                "어댑터 보고서 네 파일을 모두 생성해야 합니다.");
            Ensure(export.Sha256.Count == 3,
                "JSON·CSV·HTML 해시 세 개가 필요합니다.");

            foreach ((string fileName, string expectedHash) in export.Sha256)
            {
                string path = Path.Combine(export.OutputDirectory, fileName);
                using FileStream stream = File.OpenRead(path);
                string actualHash = Convert.ToHexString(SHA256.HashData(stream))
                    .ToLowerInvariant();
                Ensure(actualHash == expectedHash,
                    $"SHA-256이 일치하지 않습니다: {fileName}");
            }
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private static WirelessAdapterSelectionResult CreateSelection()
    {
        NetworkAdapterCandidate wireless = new(
            Id: "11111111-1111-1111-1111-111111111111",
            Name: "=HYPERLINK(\"https://example.invalid\")",
            Description: "Intel Wi-Fi 6E Adapter 192.168.10.20 AA:BB:CC:DD:EE:FF",
            InterfaceType: NetworkInterfaceType.Wireless80211,
            OperationalStatus: OperationalStatus.Up,
            SpeedBitsPerSecond: 1_200_000_000,
            HasUnicastAddress: true,
            HasDefaultGateway: true,
            IsNativeWlanConnected: true,
            IPv4InterfaceIndex: 10,
            IPv6InterfaceIndex: 10);
        NetworkAdapterCandidate vpn = new(
            Id: "22222222-2222-2222-2222-222222222222",
            Name: "Tailscale",
            Description: "Tailscale Tunnel",
            InterfaceType: NetworkInterfaceType.Tunnel,
            OperationalStatus: OperationalStatus.Up,
            SpeedBitsPerSecond: 100_000_000,
            HasUnicastAddress: true,
            HasDefaultGateway: false,
            IsNativeWlanConnected: false,
            IPv4InterfaceIndex: 20,
            IPv6InterfaceIndex: 20);
        NetworkAdapterCandidate virtualSwitch = new(
            Id: "33333333-3333-3333-3333-333333333333",
            Name: "vEthernet (Default Switch)",
            Description: "Hyper-V Virtual Ethernet Adapter",
            InterfaceType: NetworkInterfaceType.Ethernet,
            OperationalStatus: OperationalStatus.Up,
            SpeedBitsPerSecond: 10_000_000_000,
            HasUnicastAddress: true,
            HasDefaultGateway: false,
            IsNativeWlanConnected: false,
            IPv4InterfaceIndex: 30,
            IPv6InterfaceIndex: 30);

        return NetworkAdapterSelector.Select(
            [wireless, vpn, virtualSwitch]);
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
