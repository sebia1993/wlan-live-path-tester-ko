using System.Runtime.CompilerServices;
using System.Text.Json;
using WlanLivePathTester.Core.Observation;
using WlanLivePathTester.Core.Reporting;

namespace WlanLivePathTester.ReportSmoke;

internal static class BrowserObservationSystemSuspendReportV2Tests
{
#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Run()
    {
        BrowserObservationResult result = new(
            BrowserObservationStatus.Canceled,
            null,
            null,
            "시스템 절전 또는 최대 절전 전환으로 관찰을 중단했습니다.",
            BrowserObservationTerminationReason.SystemSuspend);

        VerifyDedicatedReport(result);
        VerifyUnifiedReportAndFinding(result);
        Console.WriteLine(
            "PASS browser observation SystemSuspend report v2 tests");
    }

    private static void VerifyDedicatedReport(
        BrowserObservationResult result)
    {
        BrowserObservationSessionReportDocument report =
            BrowserObservationSessionReportWriter.CreateDocument(
                result,
                "0.1.0-test",
                DateTimeOffset.UnixEpoch.AddHours(9));

        Ensure(report.Status == "Canceled",
            "샘플 없는 절전 중단 상태는 Canceled여야 합니다.");
        Ensure(report.TerminationReason == "SystemSuspend",
            "전용 보고서에 SystemSuspend가 필요합니다.");
        Ensure(report.TerminationDisplay == "시스템 절전 전환",
            "전용 보고서에 한국어 절전 설명이 필요합니다.");
        Ensure(report.Summary is null,
            "샘플 없는 절전 중단은 요약 없이 저장할 수 있어야 합니다.");

        string json = BrowserObservationSessionReportWriter.RenderJson(
            report);
        string csv = BrowserObservationSessionReportWriter.RenderCsv(
            report);
        string html = BrowserObservationSessionReportWriter.RenderHtml(
            report);

        using JsonDocument parsed = JsonDocument.Parse(json);
        Ensure(parsed.RootElement
                .GetProperty("terminationReason")
                .GetString() == "SystemSuspend",
            "전용 JSON에 SystemSuspend가 필요합니다.");
        Ensure(csv.Contains(
                "\"observation\",\"terminationReason\",\"SystemSuspend\"",
                StringComparison.Ordinal),
            "전용 CSV에 SystemSuspend 행이 필요합니다.");
        Ensure(html.Contains("SystemSuspend", StringComparison.Ordinal)
               && html.Contains(
                   "저장된 관찰 샘플이 없습니다.",
                   StringComparison.Ordinal),
            "전용 HTML에 절전 원인과 샘플 없음 설명이 필요합니다.");
    }

    private static void VerifyUnifiedReportAndFinding(
        BrowserObservationResult result)
    {
        ReportObservationSection observation =
            ReportObservationMapper.FromResult(result)
            ?? throw new InvalidOperationException(
                "절전 결과를 통합 관찰 섹션으로 매핑해야 합니다.");
        Ensure(observation.TerminationReason == "SystemSuspend",
            "통합 관찰 섹션에 SystemSuspend가 필요합니다.");

        IReadOnlyList<ReportFinding> findings =
            ReportFindingEngine.Evaluate(
                HealthyWlan(),
                HealthyProxy(),
                Array.Empty<ReportTextSection>(),
                observation,
                Array.Empty<ReportMeasurementSection>());
        ReportFinding finding = findings.Single(item =>
            item.Code.Equals(
                "BROWSER_OBSERVATION_SYSTEM_SUSPEND",
                StringComparison.Ordinal));
        Ensure(finding.Severity == "Warning",
            "SystemSuspend Finding은 Warning이어야 합니다.");
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
