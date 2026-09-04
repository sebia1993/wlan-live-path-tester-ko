using System.Runtime.CompilerServices;
using System.Text.Json;
using WlanLivePathTester.Core.Observation;
using WlanLivePathTester.Core.Reporting;

namespace WlanLivePathTester.ReportSmoke;

internal static class BrowserObservationWlanIdentityUnavailableReportTests
{
    private const string FindingCode =
        "BROWSER_OBSERVATION_WLAN_IDENTITY_UNAVAILABLE";

#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Run()
    {
        BrowserObservationResult result = new(
            BrowserObservationStatus.AdapterUnavailable,
            null,
            null,
            "Native WLAN 연결 또는 인터페이스 ID를 연속 3회 확인하지 못했습니다.",
            BrowserObservationTerminationReason.WlanIdentityUnavailable);

        VerifyDedicatedReport(result);
        VerifyUnifiedReportAndFinding(result);
        VerifyDisplayContract();
        Console.WriteLine(
            "PASS browser observation WLAN identity unavailable report tests");
    }

    private static void VerifyDedicatedReport(
        BrowserObservationResult result)
    {
        BrowserObservationSessionReportDocument report =
            BrowserObservationSessionReportWriter.CreateDocument(
                result,
                "0.1.0-test",
                DateTimeOffset.UnixEpoch.AddHours(9));

        Ensure(report.Status == "AdapterUnavailable",
            "WLAN ID 연속 미확인의 결과 가용 상태를 유지해야 합니다.");
        Ensure(report.TerminationReason
               == "WlanIdentityUnavailable",
            "전용 보고서에 WLAN ID 연속 미확인 종료 원인이 필요합니다.");
        Ensure(report.TerminationDisplay
               == "WLAN 연결 ID 연속 미확인",
            "전용 보고서에 안정된 한국어 종료 설명이 필요합니다.");
        Ensure(report.Summary is null,
            "샘플 없는 WLAN ID 미확인 결과도 보고서로 저장할 수 있어야 합니다.");

        string json = BrowserObservationSessionReportWriter.RenderJson(
            report);
        string csv = BrowserObservationSessionReportWriter.RenderCsv(
            report);
        string html = BrowserObservationSessionReportWriter.RenderHtml(
            report);

        using JsonDocument parsed = JsonDocument.Parse(json);
        Ensure(parsed.RootElement
                .GetProperty("terminationReason")
                .GetString() == "WlanIdentityUnavailable",
            "전용 JSON에 WLAN ID 종료 원인이 필요합니다.");
        Ensure(csv.Contains(
                "\"observation\",\"terminationReason\",\"WlanIdentityUnavailable\"",
                StringComparison.Ordinal),
            "전용 CSV에 WLAN ID 종료 원인 행이 필요합니다.");
        Ensure(html.Contains(
                "WlanIdentityUnavailable",
                StringComparison.Ordinal)
               && html.Contains(
                   "WLAN 연결 ID 연속 미확인",
                   StringComparison.Ordinal),
            "전용 HTML에 enum과 한국어 설명이 필요합니다.");
    }

    private static void VerifyUnifiedReportAndFinding(
        BrowserObservationResult result)
    {
        ReportObservationSection observation =
            ReportObservationMapper.FromResult(result)
            ?? throw new InvalidOperationException(
                "WLAN ID 미확인 결과를 통합 관찰 섹션으로 매핑해야 합니다.");
        Ensure(observation.Status == "AdapterUnavailable"
               && observation.TerminationReason
                   == "WlanIdentityUnavailable",
            "통합 관찰 섹션이 상태와 직접 종료 원인을 분리해 유지해야 합니다.");
        Ensure(observation.Message.Contains(
                "WLAN 연결 ID 연속 미확인 (WlanIdentityUnavailable)",
                StringComparison.Ordinal),
            "통합 HTML용 메시지에 사람이 읽을 수 있는 종료 설명이 필요합니다.");

        IReadOnlyList<ReportFinding> findings =
            ReportFindingEngine.Evaluate(
                HealthyWlan(),
                HealthyProxy(),
                Array.Empty<ReportTextSection>(),
                observation,
                Array.Empty<ReportMeasurementSection>());
        ReportFinding finding = findings.Single(item =>
            item.Code.Equals(FindingCode, StringComparison.Ordinal));
        Ensure(finding.Severity == "Warning",
            "WLAN ID 연속 미확인 Finding은 Warning이어야 합니다.");
        Ensure(finding.Title.Contains(
                "WLAN 연결 ID",
                StringComparison.Ordinal),
            "Finding 제목이 WLAN ID 연속성 문제를 직접 설명해야 합니다.");
        Ensure(!findings.Any(item => item.Code ==
                "BROWSER_OBSERVATION_ADAPTER_CHANGED"),
            "WLAN ID 미확인을 실제 다른 물리 NIC 변경으로 오인하면 안 됩니다.");
        Ensure(!findings.Any(item => item.Code ==
                "BROWSER_OBSERVATION_COUNTER_PROVIDER_MISMATCH"),
            "고정 카운터가 유지된 WLAN ID 미확인을 공급자 ID 불일치로 오인하면 안 됩니다.");

        LocalDiagnosticReport report = new(
            SchemaVersion: "1.1-test",
            Metadata: new ReportMetadata(
                GeneratedAt: DateTimeOffset.UnixEpoch,
                ApplicationName: "WLAN Live Path Tester KO",
                ApplicationVersion: "0.1.0-test",
                OperatingSystem: "Windows synthetic",
                RuntimeVersion: ".NET synthetic",
                Culture: "ko-KR",
                SensitiveValuesIncluded: false,
                DataHandlingStatement: "합성 로컬 보고서"),
            Wlan: HealthyWlan(),
            Proxy: HealthyProxy(),
            Measurements: Array.Empty<ReportTextSection>(),
            BrowserObservation: observation,
            Findings: findings,
            Limitations: Array.Empty<string>(),
            StructuredMeasurements:
                Array.Empty<ReportMeasurementSection>());

        string unifiedJson = LocalReportWriter.RenderJson(report);
        string unifiedCsv = LocalReportWriter.RenderCsv(report);
        string unifiedHtml = LocalReportWriter.RenderHtml(report);
        Ensure(unifiedJson.Contains(FindingCode, StringComparison.Ordinal)
               && unifiedCsv.Contains(FindingCode, StringComparison.Ordinal),
            "통합 JSON·CSV에 머신용 WLAN ID Finding 코드가 필요합니다.");
        Ensure(unifiedHtml.Contains(
                finding.Title,
                StringComparison.Ordinal)
               && unifiedHtml.Contains(
                   finding.Interpretation,
                   StringComparison.Ordinal),
            "통합 HTML에 WLAN ID Finding의 제목과 해석이 필요합니다.");
    }

    private static void VerifyDisplayContract()
    {
        Ensure(BrowserObservationTerminationPolicy.ToDisplayText(
                   BrowserObservationTerminationReason
                       .WlanIdentityUnavailable)
               == "WLAN 연결 ID 연속 미확인",
            "WLAN ID 종료 원인의 중앙 한국어 표시 계약이 필요합니다.");
    }

    private static ReportWlanSection HealthyWlan() =>
        new(
            CapturedAt: DateTimeOffset.UnixEpoch,
            IsConnected: true,
            InterfaceDescription: "[마스킹됨]",
            InterfaceState: "Connected",
            Ssid: "[마스킹됨]",
            Bssid: "[마스킹됨]",
            RssiDbm: -55,
            SignalQualityPercent: 90,
            Channel: 36,
            CenterFrequencyMhz: 5180,
            Band: "5 GHz",
            PhyType: "802.11ax",
            ReceiveLinkMbps: 1200,
            TransmitLinkMbps: 1200,
            Authentication: "WPA2-Enterprise",
            Cipher: "CCMP",
            ReadError: null);

    private static ReportProxySection HealthyProxy() =>
        new(
            ReadSucceeded: true,
            Mode: "Manual",
            AutoDetectEnabled: false,
            PacConfigured: false,
            ManualProxyConfigured: true,
            BypassConfigured: true,
            Win32Error: null,
            Statement: "프록시 값은 마스킹됨");

    private static void Ensure(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
