using System.Runtime.CompilerServices;
using System.Text.Json;
using WlanLivePathTester.Core.Reporting;

namespace WlanLivePathTester.ReportSmoke;

internal static class ObservationCounterResetFindingTests
{
    private const string FindingCode =
        "BROWSER_OBSERVATION_COUNTER_RESET";

#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Run()
    {
        AddsDedicatedFindingForOneReset();
        AddsOneFindingForMultipleResets();
        DoesNotInferNicChangeOrProviderMismatch();
        DoesNotAddFindingWithoutReset();
        RendersFindingAcrossReportFormats();
        Console.WriteLine(
            "PASS dedicated browser observation counter reset finding tests");
    }

    private static void AddsDedicatedFindingForOneReset()
    {
        IReadOnlyList<ReportFinding> findings = Evaluate(
            CreateObservation(counterResetCount: 1));
        ReportFinding finding = SingleFinding(findings);

        Ensure(finding.Severity == "Warning",
            "카운터 재설정은 관찰 통계 연속성에 영향을 주므로 Warning이어야 합니다.");
        Ensure(finding.Title.Contains(
                "카운터 재설정",
                StringComparison.Ordinal),
            "카운터 재설정을 직접 설명하는 제목이 필요합니다.");
        Ensure(finding.Evidence.Contains(
                "1회",
                StringComparison.Ordinal),
            "Finding 근거에 실제 재설정 횟수가 필요합니다.");
        Ensure(finding.Evidence.Contains(
                "통계에서 제외",
                StringComparison.Ordinal),
            "재설정 구간의 바이트와 Mbps를 제외했다는 설명이 필요합니다.");
        Ensure(findings.Any(item => item.Code ==
                "BROWSER_OBSERVATION_COMPLETED"),
            "카운터 재설정 후 관찰이 정상 완료된 경우 종료 Finding도 함께 유지해야 합니다.");
        Ensure(findings.Any(item => item.Code ==
                "BROWSER_OBSERVATION_LOW_CONFIDENCE"),
            "카운터 재설정 결과의 낮은 신뢰도 Finding도 함께 유지해야 합니다.");
    }

    private static void AddsOneFindingForMultipleResets()
    {
        IReadOnlyList<ReportFinding> findings = Evaluate(
            CreateObservation(counterResetCount: 3));

        Ensure(findings.Count(item => item.Code == FindingCode) == 1,
            "재설정 횟수가 여러 번이어도 Finding 코드는 한 번만 생성해야 합니다.");
        Ensure(SingleFinding(findings).Evidence.Contains(
                "3회",
                StringComparison.Ordinal),
            "여러 재설정의 실제 횟수를 근거에 기록해야 합니다.");
    }

    private static void DoesNotInferNicChangeOrProviderMismatch()
    {
        IReadOnlyList<ReportFinding> findings = Evaluate(
            CreateObservation(counterResetCount: 1));

        Ensure(!findings.Any(item => item.Code ==
                "BROWSER_OBSERVATION_ADAPTER_CHANGED"),
            "카운터 재설정만으로 물리 NIC 변경을 추정하면 안 됩니다.");
        Ensure(!findings.Any(item => item.Code ==
                "BROWSER_OBSERVATION_COUNTER_PROVIDER_MISMATCH"),
            "카운터 재설정만으로 공급자 ID 불일치를 추정하면 안 됩니다.");
        Ensure(!findings.Any(item => item.Code ==
                "BROWSER_OBSERVATION_ADAPTER_UNAVAILABLE"),
            "관찰이 계속 완료됐다면 NIC 사용 불가로 분류하면 안 됩니다.");
    }

    private static void DoesNotAddFindingWithoutReset()
    {
        IReadOnlyList<ReportFinding> zero = Evaluate(
            CreateObservation(counterResetCount: 0));
        IReadOnlyList<ReportFinding> unknown = Evaluate(
            CreateObservation(counterResetCount: null));

        Ensure(!zero.Any(item => item.Code == FindingCode),
            "재설정 횟수 0에는 전용 Finding을 추가하면 안 됩니다.");
        Ensure(!unknown.Any(item => item.Code == FindingCode),
            "재설정 횟수를 확인할 수 없는 이전 보고서에 전용 Finding을 추정하면 안 됩니다.");
    }

    private static void RendersFindingAcrossReportFormats()
    {
        ReportObservationSection observation =
            CreateObservation(counterResetCount: 1);
        IReadOnlyList<ReportFinding> findings = Evaluate(observation);
        ReportFinding resetFinding = SingleFinding(findings);
        LocalDiagnosticReport report = CreateReport(
            observation,
            findings);

        string json = LocalReportWriter.RenderJson(report);
        string csv = LocalReportWriter.RenderCsv(report);
        string html = LocalReportWriter.RenderHtml(report);

        using JsonDocument parsed = JsonDocument.Parse(json);
        Ensure(parsed.RootElement
                .GetProperty("findings")
                .EnumerateArray()
                .Count(item => item.GetProperty("code").GetString()
                    == FindingCode) == 1,
            "통합 JSON에 카운터 재설정 Finding 코드가 정확히 한 개 필요합니다.");
        Ensure(csv.Contains(FindingCode, StringComparison.Ordinal),
            "통합 CSV에 카운터 재설정 Finding 코드가 필요합니다.");
        Ensure(html.Contains(
                resetFinding.Title,
                StringComparison.Ordinal),
            "통합 HTML에 카운터 재설정 Finding 제목이 필요합니다.");
        Ensure(html.Contains(
                resetFinding.Interpretation,
                StringComparison.Ordinal),
            "통합 HTML에 카운터 재설정 해석이 필요합니다.");
    }

    private static IReadOnlyList<ReportFinding> Evaluate(
        ReportObservationSection observation) =>
        ReportFindingEngine.Evaluate(
            HealthyWlan(),
            HealthyProxy(),
            Array.Empty<ReportTextSection>(),
            observation,
            Array.Empty<ReportMeasurementSection>());

    private static ReportObservationSection CreateObservation(
        int? counterResetCount) =>
        new ReportObservationSection(
            Status: "Success",
            StartedAt: DateTimeOffset.UnixEpoch,
            CompletedAt: DateTimeOffset.UnixEpoch.AddSeconds(10),
            ObservedSeconds: 8,
            BaselineReceiveMbps: 1,
            AverageAdjustedReceiveMbps: 80,
            PeakAdjustedReceiveMbps: 100,
            TotalReceiveBytes: 80_000_000,
            ActiveSampleCount: 8,
            PauseCount: 0,
            SuddenDropCount: 0,
            BssidChangeCount: 0,
            AdapterChangeCount: 0,
            CounterResetCount: counterResetCount,
            WlanDisconnectedSampleCount: 0,
            Confidence: counterResetCount is > 0 ? "Low" : "Medium",
            Message: "합성 관찰 완료",
            Limitation: "합성 관찰 한계",
            Samples: Array.Empty<ReportObservationSample>())
        {
            TerminationReason = "Completed"
        };

    private static LocalDiagnosticReport CreateReport(
        ReportObservationSection observation,
        IReadOnlyList<ReportFinding> findings) =>
        new(
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

    private static ReportFinding SingleFinding(
        IEnumerable<ReportFinding> findings)
    {
        ReportFinding[] matches = findings
            .Where(item => item.Code == FindingCode)
            .ToArray();
        Ensure(matches.Length == 1,
            $"카운터 재설정 Finding이 정확히 한 개여야 합니다. Actual={matches.Length}");
        return matches[0];
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
