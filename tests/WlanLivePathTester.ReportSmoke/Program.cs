using System.Security.Cryptography;
using System.Text.Json;
using WlanLivePathTester.Core.Reporting;

namespace WlanLivePathTester.ReportSmoke;

internal static class Program
{
    private static int Main()
    {
        (string Name, Action Test)[] tests =
        [
            ("민감 문자열 마스킹", RedactsSensitiveText),
            ("CSV 수식 주입 방지", ProtectsCsvFormula),
            ("JSON 직렬화와 비밀값 비노출", SerializesSafeJson),
            ("외부 리소스 없는 HTML과 인코딩", RendersOfflineSafeHtml),
            ("결정론적 Finding 생성", CreatesDeterministicFindings),
            ("JSON CSV HTML SHA-256 로컬 저장", WritesAllLocalFiles)
        ];

        int failures = 0;
        foreach ((string name, Action test) in tests)
        {
            try
            {
                test();
                Console.WriteLine($"PASS  {name}");
            }
            catch (Exception exception)
            {
                failures++;
                Console.Error.WriteLine($"FAIL  {name}: {exception.Message}");
            }
        }

        Console.WriteLine($"보고서 smoke 총 {tests.Length}개, 실패 {failures}개");
        return failures == 0 ? 0 : 1;
    }

    private static void RedactsSensitiveText()
    {
        const string source = "사용자 C:\\Users\\alice 로그 https://example.invalid/file.bin?token=secret IP 192.168.10.20 MAC AA:BB:CC:DD:EE:FF mail alice@example.invalid";
        string redacted = SensitiveDataRedactor.RedactText(source)
            ?? throw new InvalidOperationException("마스킹 결과가 없습니다.");

        Assert(!redacted.Contains("alice", StringComparison.OrdinalIgnoreCase),
            $"사용자 식별자가 남았습니다: {redacted}");
        Assert(!redacted.Contains("192.168.10.20", StringComparison.Ordinal),
            $"IPv4 주소가 남았습니다: {redacted}");
        Assert(!redacted.Contains("AA:BB:CC:DD:EE:FF", StringComparison.OrdinalIgnoreCase),
            $"MAC 주소가 남았습니다: {redacted}");
        Assert(!redacted.Contains("token=secret", StringComparison.Ordinal),
            $"URL 쿼리가 남았습니다: {redacted}");
        Assert(redacted.Contains("[호스트 마스킹됨]", StringComparison.Ordinal),
            $"URL 호스트 마스킹 표기가 필요합니다: {redacted}");
    }

    private static void ProtectsCsvFormula()
    {
        string protectedValue = SensitiveDataRedactor.ProtectCsvFormula(
            "=HYPERLINK(\"malicious\")");
        Assert(protectedValue.StartsWith("'=", StringComparison.Ordinal),
            "수식 시작 문자는 작은따옴표로 비활성화해야 합니다.");
    }

    private static void SerializesSafeJson()
    {
        LocalDiagnosticReport report = CreateSyntheticReport();
        string json = LocalReportWriter.RenderJson(report);
        using JsonDocument document = JsonDocument.Parse(json);

        Assert(document.RootElement.GetProperty("schemaVersion").GetString() == "1.0",
            "JSON 스키마 버전을 기록해야 합니다.");
        Assert(!json.Contains("192.168.10.20", StringComparison.Ordinal),
            "JSON에 실제 합성 IP 원문이 남으면 안 됩니다.");
        Assert(!json.Contains("AA:BB:CC:DD:EE:FF", StringComparison.OrdinalIgnoreCase),
            "JSON에 실제 합성 BSSID 원문이 남으면 안 됩니다.");
    }

    private static void RendersOfflineSafeHtml()
    {
        LocalDiagnosticReport report = CreateSyntheticReport();
        string html = LocalReportWriter.RenderHtml(report);

        Assert(html.StartsWith("<!doctype html>", StringComparison.OrdinalIgnoreCase),
            "HTML5 doctype이 필요합니다.");
        Assert(html.Contains("Content-Security-Policy", StringComparison.Ordinal),
            "로컬 HTML에 CSP가 필요합니다.");
        Assert(!html.Contains("<script", StringComparison.OrdinalIgnoreCase),
            "HTML에 스크립트를 포함하면 안 됩니다.");
        Assert(!html.Contains("<iframe", StringComparison.OrdinalIgnoreCase),
            "HTML에 iframe을 포함하면 안 됩니다.");
        Assert(!html.Contains("<link", StringComparison.OrdinalIgnoreCase),
            "외부 스타일시트 링크를 포함하면 안 됩니다.");
        Assert(!html.Contains("<img src=x", StringComparison.OrdinalIgnoreCase),
            "측정 문구의 HTML을 실행 가능한 태그로 출력하면 안 됩니다.");
        Assert(html.Contains("&lt;img", StringComparison.OrdinalIgnoreCase),
            "측정 문구의 HTML 특수 문자를 인코딩해야 합니다.");
    }

    private static void CreatesDeterministicFindings()
    {
        LocalDiagnosticReport report = CreateSyntheticReport();
        IReadOnlyList<ReportFinding> findings = ReportFindingEngine.Evaluate(
            report.Wlan,
            report.Proxy,
            report.Measurements,
            report.BrowserObservation);

        Assert(findings.Any(item => item.Code == "WLAN_WEAK_RSSI"),
            "약한 RSSI Finding이 필요합니다.");
        Assert(findings.Any(item => item.Code == "PROXY_AUTHENTICATION_FAILURE"),
            "HTTP 407 Finding이 필요합니다.");
        Assert(findings.Any(item => item.Code == "BSSID_CHANGE_WITH_THROUGHPUT_DROP"),
            "BSSID 변경과 처리량 저하 Finding이 필요합니다.");
    }

    private static void WritesAllLocalFiles()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "WlanLivePathTester.ReportSmoke",
            Guid.NewGuid().ToString("N"));

        try
        {
            LocalReportExportResult result = LocalReportWriter.WriteAll(
                CreateSyntheticReport(),
                directory,
                "합성 보고서");

            string[] paths =
            [
                result.JsonPath,
                result.CsvPath,
                result.HtmlPath,
                result.Sha256Path
            ];
            Assert(paths.All(File.Exists), "네 개의 로컬 산출물이 모두 필요합니다.");
            Assert(result.Sha256.Count == 3, "JSON·CSV·HTML 해시 세 개가 필요합니다.");

            foreach ((string fileName, string expectedHash) in result.Sha256)
            {
                string path = Path.Combine(result.OutputDirectory, fileName);
                using FileStream stream = File.OpenRead(path);
                string actualHash = Convert.ToHexString(SHA256.HashData(stream))
                    .ToLowerInvariant();
                Assert(actualHash == expectedHash,
                    $"SHA-256이 일치하지 않습니다: {fileName}");
            }

            string checksumText = File.ReadAllText(result.Sha256Path);
            Assert(result.Sha256.All(pair => checksumText.Contains(
                    pair.Value + "  " + pair.Key,
                    StringComparison.Ordinal)),
                "무결성 파일에 모든 해시와 파일명이 필요합니다.");
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private static LocalDiagnosticReport CreateSyntheticReport()
    {
        DateTimeOffset now = DateTimeOffset.UnixEpoch.AddHours(9);
        ReportWlanSection wlan = new(
            CapturedAt: now,
            IsConnected: true,
            InterfaceDescription: "Synthetic Wi-Fi Adapter",
            InterfaceState: "Connected",
            Ssid: "[SSID 마스킹됨]",
            Bssid: "[BSSID 마스킹됨]",
            RssiDbm: -78,
            SignalQualityPercent: 42,
            Channel: 36,
            CenterFrequencyMhz: 5180,
            Band: "5 GHz",
            PhyType: "802.11ax",
            ReceiveLinkMbps: 1200,
            TransmitLinkMbps: 1200,
            Authentication: "WPA2-Enterprise",
            Cipher: "CCMP",
            ReadError: null);
        ReportProxySection proxy = new(
            ReadSucceeded: true,
            Mode: "PAC",
            AutoDetectEnabled: false,
            PacConfigured: true,
            ManualProxyConfigured: false,
            BypassConfigured: true,
            Win32Error: null,
            Statement: "프록시 주소와 PAC URL은 포함하지 않았습니다.");
        ReportTextSection measurement = new(
            SectionId: "external",
            Title: "외부망 다운로드 측정 <img src=x onerror=alert(1)>",
            Content: SensitiveDataRedactor.RedactText(
                "HTTP 407 · https://example.invalid/file.bin?token=secret · 192.168.10.20")
                ?? string.Empty,
            CapturedAt: now);
        ReportObservationSection observation = new(
            Status: "Success",
            StartedAt: now,
            CompletedAt: now.AddSeconds(10),
            ObservedSeconds: 10,
            BaselineReceiveMbps: 0.2,
            AverageAdjustedReceiveMbps: 45,
            PeakAdjustedReceiveMbps: 90,
            TotalReceiveBytes: 56_250_000,
            ActiveSampleCount: 10,
            PauseCount: 1,
            SuddenDropCount: 1,
            BssidChangeCount: 1,
            AdapterChangeCount: 0,
            CounterResetCount: 0,
            WlanDisconnectedSampleCount: 0,
            Confidence: "Medium",
            Message: "브라우저 다운로드 관찰 완료",
            Limitation: "Wi-Fi 인터페이스 전체 트래픽입니다.",
            Samples:
            [
                new ReportObservationSample(
                    Timestamp: now.AddSeconds(1),
                    IntervalSeconds: 1,
                    IsBaseline: false,
                    ReceiveBytesDelta: 10_000_000,
                    TransmitBytesDelta: 100_000,
                    RawReceiveMbps: 80,
                    RawTransmitMbps: 0.8,
                    AdjustedReceiveMbps: 79.8,
                    RssiDbm: -78,
                    ReceiveLinkMbps: 1200,
                    TransmitLinkMbps: 1200,
                    BssidChanged: true,
                    AdapterChanged: false,
                    CounterReset: false,
                    WlanDisconnected: false,
                    PauseDetected: true,
                    SuddenDropDetected: true,
                    Note: "합성 샘플")
            ]);

        IReadOnlyList<ReportFinding> findings = ReportFindingEngine.Evaluate(
            wlan,
            proxy,
            [measurement],
            observation);
        return new LocalDiagnosticReport(
            SchemaVersion: "1.0",
            Metadata: new ReportMetadata(
                GeneratedAt: now,
                ApplicationName: "WLAN Live Path Tester KO",
                ApplicationVersion: "0.1.0-test",
                OperatingSystem: "Synthetic Windows",
                RuntimeVersion: ".NET synthetic",
                Culture: "ko-KR",
                SensitiveValuesIncluded: false,
                DataHandlingStatement: "로컬 생성, 외부 업로드 없음"),
            Wlan: wlan,
            Proxy: proxy,
            Measurements: [measurement],
            BrowserObservation: observation,
            Findings: findings,
            Limitations: ReportFindingEngine.DefaultLimitations());
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
