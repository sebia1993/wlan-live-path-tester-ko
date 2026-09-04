using System.Runtime.CompilerServices;
using System.Text.Json;
using WlanLivePathTester.Core.Observation;
using WlanLivePathTester.Core.Reporting;

namespace WlanLivePathTester.ReportSmoke;

internal static class UnifiedObservationTerminationReportTests
{
    private const string SecretEmail = "user@example.invalid";
    private const string SecretIp = "10.20.30.40";
    private const string SecretUrl =
        "https://internal.example.invalid/private.bin";

#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Run()
    {
        VerifyExplicitReasonsAcrossAllFormats();
        VerifyLegacyStatusFallback();
        VerifyMessageRedactionAndSingleDisplay();
        VerifyPositionalCompatibility();
        Console.WriteLine("PASS unified observation termination JSON CSV HTML tests");
    }

    private static void VerifyExplicitReasonsAcrossAllFormats()
    {
        (BrowserObservationStatus Status,
            BrowserObservationTerminationReason Reason)[] cases =
        [
            (BrowserObservationStatus.Success,
                BrowserObservationTerminationReason.Completed),
            (BrowserObservationStatus.Canceled,
                BrowserObservationTerminationReason.CanceledByUser),
            (BrowserObservationStatus.AdapterChanged,
                BrowserObservationTerminationReason.AdapterChanged),
            (BrowserObservationStatus.AdapterUnavailable,
                BrowserObservationTerminationReason.AdapterUnavailable),
            (BrowserObservationStatus.CounterProviderMismatch,
                BrowserObservationTerminationReason.CounterProviderMismatch),
            (BrowserObservationStatus.Canceled,
                BrowserObservationTerminationReason.SystemSuspend),
            (BrowserObservationStatus.PartialSuccess,
                BrowserObservationTerminationReason.TimingDiscontinuity),
            (BrowserObservationStatus.Failed,
                BrowserObservationTerminationReason.Failed)
        ];

        foreach ((BrowserObservationStatus status,
                  BrowserObservationTerminationReason reason) in cases)
        {
            BrowserObservationResult source = new(
                status,
                summary: null,
                initialWlan: null,
                message: "합성 관찰 결과",
                terminationReason: reason);
            ReportObservationSection mapped =
                ReportObservationMapper.FromResult(source)
                ?? throw new InvalidOperationException(
                    "관찰 결과를 통합 보고서 섹션으로 매핑해야 합니다.");

            Ensure(mapped.Status == status.ToString(),
                $"기존 관찰 상태를 보존해야 합니다: {status}");
            Ensure(mapped.TerminationReason == reason.ToString(),
                $"구조화 종료 원인을 보존해야 합니다: {reason}");

            LocalDiagnosticReport report = CreateReport(mapped);
            string json = LocalReportWriter.RenderJson(report);
            string csv = LocalReportWriter.RenderCsv(report);
            string html = LocalReportWriter.RenderHtml(report);

            using JsonDocument parsed = JsonDocument.Parse(json);
            string? jsonReason = parsed.RootElement
                .GetProperty("browserObservation")
                .GetProperty("terminationReason")
                .GetString();
            Ensure(jsonReason == reason.ToString(),
                $"JSON 종료 원인이 잘못됐습니다: {reason}");
            Ensure(csv.Contains(
                    $"\"browserObservation\",\"terminationReason\",\"{reason}\"",
                    StringComparison.Ordinal),
                $"CSV 종료 원인이 없습니다: {reason}");
            Ensure(html.Contains(reason.ToString(), StringComparison.Ordinal),
                $"HTML 종료 원인이 없습니다: {reason}");
            Ensure(html.Contains(
                    BrowserObservationTerminationPolicy.ToDisplayText(reason),
                    StringComparison.Ordinal),
                $"HTML 한국어 종료 설명이 없습니다: {reason}");
        }
    }

    private static void VerifyLegacyStatusFallback()
    {
        BrowserObservationResult legacy = new(
            BrowserObservationStatus.Canceled,
            null,
            null,
            "기존 네 값 결과");
        ReportObservationSection mapped =
            ReportObservationMapper.FromResult(legacy)
            ?? throw new InvalidOperationException(
                "기존 관찰 결과를 통합 보고서로 매핑해야 합니다.");

        Ensure(mapped.TerminationReason == "CanceledByUser",
            "명시 종료 원인이 없는 기존 Canceled 결과는 사용자 중지로 안전하게 매핑해야 합니다.");
        Ensure(mapped.Message.Contains(
                "사용자 중지 (CanceledByUser)",
                StringComparison.Ordinal),
            "기존 결과도 HTML에 표시할 종료 설명을 가져야 합니다.");
    }

    private static void VerifyMessageRedactionAndSingleDisplay()
    {
        string message =
            $"실패 사용자 {SecretEmail} 주소 {SecretIp} URL {SecretUrl}";
        BrowserObservationResult result = new(
            BrowserObservationStatus.AdapterUnavailable,
            summary: null,
            initialWlan: null,
            message: message,
            terminationReason:
                BrowserObservationTerminationReason.AdapterUnavailable);
        ReportObservationSection mapped =
            ReportObservationMapper.FromResult(result)
            ?? throw new InvalidOperationException(
                "민감값 포함 결과를 매핑해야 합니다.");

        LocalDiagnosticReport report = CreateReport(mapped);
        string combined = string.Join(
            Environment.NewLine,
            LocalReportWriter.RenderJson(report),
            LocalReportWriter.RenderCsv(report),
            LocalReportWriter.RenderHtml(report));

        Ensure(!combined.Contains(
                SecretEmail,
                StringComparison.OrdinalIgnoreCase),
            "통합 보고서에 이메일 원문이 남으면 안 됩니다.");
        Ensure(!combined.Contains(
                SecretIp,
                StringComparison.OrdinalIgnoreCase),
            "통합 보고서에 IP 원문이 남으면 안 됩니다.");
        Ensure(!combined.Contains(
                SecretUrl,
                StringComparison.OrdinalIgnoreCase),
            "통합 보고서에 URL 원문이 남으면 안 됩니다.");

        string display =
            "종료 원인: 고정 Wi-Fi 사용 불가 (AdapterUnavailable)";
        Ensure(CountOccurrences(mapped.Message, display) == 1,
            "종료 원인 표시를 메시지에 중복 추가하면 안 됩니다.");
    }

    private static void VerifyPositionalCompatibility()
    {
        ReportObservationSection section = new(
            Status: "Success",
            StartedAt: null,
            CompletedAt: null,
            ObservedSeconds: null,
            BaselineReceiveMbps: null,
            AverageAdjustedReceiveMbps: null,
            PeakAdjustedReceiveMbps: null,
            TotalReceiveBytes: null,
            ActiveSampleCount: null,
            PauseCount: null,
            SuddenDropCount: null,
            BssidChangeCount: null,
            AdapterChangeCount: null,
            CounterResetCount: null,
            WlanDisconnectedSampleCount: null,
            Confidence: "Unknown",
            Message: "legacy",
            Limitation: "legacy",
            Samples: Array.Empty<ReportObservationSample>());

        var (status, _, _, _, _, _, _, _, _, _, _, _, _, _, _, _,
            message, _, _) = section;
        Ensure(status == "Success" && message == "legacy",
            "기존 ReportObservationSection positional 생성자와 deconstruction을 유지해야 합니다.");
        Ensure(section.TerminationReason is null,
            "기존 보고서 섹션은 optional 종료 원인을 null로 유지해야 합니다.");
    }

    private static LocalDiagnosticReport CreateReport(
        ReportObservationSection observation) =>
        new(
            SchemaVersion: "1.1-test",
            Metadata: new ReportMetadata(
                GeneratedAt: DateTimeOffset.UnixEpoch,
                ApplicationName: "WLAN Live Path Tester KO",
                ApplicationVersion: "0.1.0-test",
                OperatingSystem: "Windows test",
                RuntimeVersion: ".NET test",
                Culture: "ko-KR",
                SensitiveValuesIncluded: false,
                DataHandlingStatement: "합성 로컬 보고서"),
            Wlan: new ReportWlanSection(
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
                ReadError: null),
            Proxy: new ReportProxySection(
                ReadSucceeded: true,
                Mode: "Manual",
                AutoDetectEnabled: false,
                PacConfigured: false,
                ManualProxyConfigured: true,
                BypassConfigured: true,
                Win32Error: null,
                Statement: "프록시 값은 마스킹됨"),
            Measurements: Array.Empty<ReportTextSection>(),
            BrowserObservation: observation,
            Findings: Array.Empty<ReportFinding>(),
            Limitations: Array.Empty<string>(),
            StructuredMeasurements:
                Array.Empty<ReportMeasurementSection>());

    private static int CountOccurrences(
        string source,
        string value)
    {
        int count = 0;
        int offset = 0;
        while ((offset = source.IndexOf(
                   value,
                   offset,
                   StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += value.Length;
        }

        return count;
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
