using System.Runtime.CompilerServices;
using System.Text.Json;
using WlanLivePathTester.Core.Models;
using WlanLivePathTester.Core.Observation;
using WlanLivePathTester.Core.Reporting;
using WlanLivePathTester.Windows.Observation;
using WlanLivePathTester.Windows.Wlan;

namespace WlanLivePathTester.WindowsSmoke;

internal static class BrowserObservationReportPipelineRuntimeTests
{
    private const string InterfaceId =
        "E1B2C3D4-E5F6-47A8-9123-1234567890AB";
    private const string InterfaceDescription =
        "Synthetic Pipeline Wi-Fi";
    private const string SecretSsid = "CORP-SECRET-SSID";
    private const string SecretBssid = "AA:BB:CC:DD:EE:30";
    private const string SecretEmail = "user@example.invalid";
    private const string SecretIp = "10.20.30.40";
    private const string SecretUrl =
        "https://internal.example.invalid/private.bin";
    private const string FindingCode =
        "BROWSER_OBSERVATION_COUNTER_PROVIDER_MISMATCH";
    private static readonly DateTimeOffset Start =
        DateTimeOffset.UnixEpoch.AddDays(4);

#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Run()
    {
        PreservesTerminationAcrossDedicatedAndUnifiedReports();
        Console.WriteLine(
            "PASS browser observation report pipeline end-to-end test");
    }

    private static void
        PreservesTerminationAcrossDedicatedAndUnifiedReports()
    {
        PipelineRuntime runtime = new();
        BrowserObservationResult result =
            new BrowserObservationRunner(runtime)
                .RunAsync(
                    new BrowserObservationOptions(
                        BaselineSeconds: 2,
                        ObservationSeconds: 5,
                        SampleIntervalMilliseconds: 500),
                    progress: null,
                    cancellationToken: default)
                .GetAwaiter()
                .GetResult();

        Ensure(result.Status
               == BrowserObservationStatus.CounterProviderMismatch,
            $"합성 공급자 불일치는 전용 상태를 유지해야 합니다: {result.Status}");
        Ensure(result.TerminationReason
               == BrowserObservationTerminationReason
                   .CounterProviderMismatch,
            "러너 결과에 CounterProviderMismatch 종료 원인이 필요합니다.");
        Ensure(result.Summary is null,
            "첫 후속 카운터 실패에는 처리량 요약이 없어야 합니다.");
        Ensure(runtime.CounterReadCount == 2,
            "초기 고정 카운터와 실패한 후속 카운터만 읽어야 합니다.");

        BrowserObservationSessionReportDocument dedicated =
            BrowserObservationSessionReportWriter.CreateDocument(
                result,
                "0.1.0-test",
                Start.AddMinutes(1));
        string dedicatedJson =
            BrowserObservationSessionReportWriter.RenderJson(dedicated);
        string dedicatedCsv =
            BrowserObservationSessionReportWriter.RenderCsv(dedicated);
        string dedicatedHtml =
            BrowserObservationSessionReportWriter.RenderHtml(dedicated);

        Ensure(dedicated.Status == "CounterProviderMismatch"
               && dedicated.TerminationReason
                   == "CounterProviderMismatch",
            "관찰 전용 보고서가 러너 상태와 종료 원인을 보존해야 합니다.");
        EnsureContainsTermination(
            dedicatedJson,
            dedicatedCsv,
            dedicatedHtml,
            "관찰 전용 보고서");
        AssertSecretsAbsent(
            string.Join(
                Environment.NewLine,
                dedicatedJson,
                dedicatedCsv,
                dedicatedHtml),
            "관찰 전용 보고서");

        ReportObservationSection mapped =
            ReportObservationMapper.FromResult(result)
            ?? throw new InvalidOperationException(
                "러너 결과를 통합 보고서 관찰 섹션으로 매핑해야 합니다.");
        Ensure(mapped.Status == "CounterProviderMismatch"
               && mapped.TerminationReason
                   == "CounterProviderMismatch",
            "통합 보고서 매퍼가 러너 상태와 구조화 종료 원인을 보존해야 합니다.");

        IReadOnlyList<ReportFinding> findings =
            ReportFindingEngine.Evaluate(
                HealthyWlan(),
                HealthyProxy(),
                Array.Empty<ReportTextSection>(),
                mapped,
                Array.Empty<ReportMeasurementSection>());
        ReportFinding terminationFinding = findings.Single(finding =>
            finding.Code.Equals(
                FindingCode,
                StringComparison.Ordinal));
        Ensure(terminationFinding.Severity == "Warning",
            "카운터 공급자 불일치 Finding은 Warning이어야 합니다.");

        LocalDiagnosticReport unified = CreateUnifiedReport(
            mapped,
            findings);
        string unifiedJson = LocalReportWriter.RenderJson(unified);
        string unifiedCsv = LocalReportWriter.RenderCsv(unified);
        string unifiedHtml = LocalReportWriter.RenderHtml(unified);

        EnsureContainsTermination(
            unifiedJson,
            unifiedCsv,
            unifiedHtml,
            "통합 보고서");
        Ensure(unifiedJson.Contains(FindingCode, StringComparison.Ordinal),
            "통합 JSON에 Finding 코드가 필요합니다.");
        Ensure(unifiedCsv.Contains(FindingCode, StringComparison.Ordinal),
            "통합 CSV에 Finding 코드가 필요합니다.");
        Ensure(unifiedHtml.Contains(
                terminationFinding.Title,
                StringComparison.Ordinal),
            "통합 HTML에는 고정 코드에 대응하는 사람이 읽을 수 있는 Finding 제목이 필요합니다.");
        Ensure(unifiedHtml.Contains(
                terminationFinding.Interpretation,
                StringComparison.Ordinal),
            "통합 HTML에는 Finding 해석이 필요합니다.");
        AssertSecretsAbsent(
            string.Join(
                Environment.NewLine,
                unifiedJson,
                unifiedCsv,
                unifiedHtml),
            "통합 보고서");

        using JsonDocument parsed = JsonDocument.Parse(unifiedJson);
        JsonElement observation = parsed.RootElement
            .GetProperty("browserObservation");
        Ensure(observation
                .GetProperty("terminationReason")
                .GetString() == "CounterProviderMismatch",
            "통합 JSON 구조에서 종료 원인을 직접 읽을 수 있어야 합니다.");
        Ensure(parsed.RootElement
                .GetProperty("findings")
                .EnumerateArray()
                .Count(item => item.GetProperty("code").GetString()
                    == FindingCode) == 1,
            "통합 JSON에는 해당 종료 Finding이 정확히 한 개여야 합니다.");
    }

    private static void EnsureContainsTermination(
        string json,
        string csv,
        string html,
        string label)
    {
        Ensure(json.Contains(
                "CounterProviderMismatch",
                StringComparison.Ordinal),
            $"{label} JSON에 종료 원인이 없습니다.");
        Ensure(csv.Contains(
                "CounterProviderMismatch",
                StringComparison.Ordinal),
            $"{label} CSV에 종료 원인이 없습니다.");
        Ensure(html.Contains(
                "CounterProviderMismatch",
                StringComparison.Ordinal),
            $"{label} HTML에 종료 원인이 없습니다.");
    }

    private static LocalDiagnosticReport CreateUnifiedReport(
        ReportObservationSection observation,
        IReadOnlyList<ReportFinding> findings) =>
        new(
            SchemaVersion: "1.1-test",
            Metadata: new ReportMetadata(
                GeneratedAt: Start.AddMinutes(1),
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
            Limitations: ReportFindingEngine.DefaultLimitations(),
            StructuredMeasurements:
                Array.Empty<ReportMeasurementSection>());

    private static ReportWlanSection HealthyWlan() =>
        new(
            CapturedAt: Start,
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

    private static void AssertSecretsAbsent(
        string content,
        string label)
    {
        string[] secrets =
        [
            InterfaceId,
            InterfaceDescription,
            SecretSsid,
            SecretBssid,
            SecretEmail,
            SecretIp,
            SecretUrl,
            "internal.example.invalid"
        ];

        foreach (string secret in secrets)
        {
            Ensure(!content.Contains(
                    secret,
                    StringComparison.OrdinalIgnoreCase),
                $"{label}에 민감값이 남았습니다: {secret}");
        }
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed class PipelineRuntime : IBrowserObservationRuntime
    {
        private int _wlanReadCount;
        private int _counterReadCount;

        public bool IsSupportedPlatform => true;

        public DateTimeOffset UtcNow => Start.AddMilliseconds(500);

        public int CounterReadCount => _counterReadCount;

        public WlanReadResult ReadWlan()
        {
            _wlanReadCount++;
            DateTimeOffset timestamp = _wlanReadCount == 1
                ? Start
                : Start.AddMilliseconds(500);
            return new WlanReadResult(
                WlanReadStatus.Success,
                [
                    new WlanSnapshot(
                        Timestamp: timestamp,
                        IsConnected: true,
                        Ssid: SecretSsid,
                        Bssid: SecretBssid,
                        RssiDbm: -55,
                        Channel: 36,
                        PhyType: "802.11ax",
                        ReceiveLinkSpeedBps: 1_200_000_000,
                        TransmitLinkSpeedBps: 1_200_000_000,
                        InterfaceDescription: InterfaceDescription,
                        InterfaceState: "Connected",
                        SignalQualityPercent: 90,
                        CenterFrequencyMhz: 5180,
                        Authentication: "WPA2-Enterprise",
                        Cipher: "CCMP",
                        InterfaceId: InterfaceId)
                ],
                nativeErrorCode: null,
                message: "합성 WLAN 연결");
        }

        public WlanInterfaceIdentityReadResult ReadWlanIdentity() =>
            new(
                IsSuccess: true,
                Interfaces:
                [
                    new WlanInterfaceIdentity(
                        InterfaceId,
                        InterfaceDescription,
                        IsConnected: true)
                ],
                Message: "합성 WLAN ID");

        public InterfaceCounterReadResult ReadCounter(
            string? preferredInterfaceId,
            string? preferredInterfaceDescription,
            InterfaceCounterSelectionMode selectionMode)
        {
            _counterReadCount++;
            if (selectionMode
                != InterfaceCounterSelectionMode.RequireExactInterfaceId)
            {
                throw new InvalidOperationException(
                    "보고서 파이프라인 테스트는 정확 ID 카운터 모드여야 합니다.");
            }

            if (_counterReadCount == 1)
            {
                return new InterfaceCounterReadResult(
                    InterfaceCounterReadStatus.Success,
                    new InterfaceCounterSnapshot(
                        Timestamp: Start,
                        InterfaceId: InterfaceId,
                        InterfaceName: "Synthetic Pipeline Wi-Fi",
                        InterfaceDescription: InterfaceDescription,
                        BytesReceived: 1_000_000,
                        BytesSent: 100_000,
                        IsOperational: true),
                    "합성 초기 카운터");
            }

            return new InterfaceCounterReadResult(
                InterfaceCounterReadStatus.CounterProviderMismatch,
                Snapshot: null,
                Message:
                    $"합성 공급자 불일치 {SecretEmail} {SecretIp} {SecretUrl}");
        }

        public Task DelayAsync(
            TimeSpan delay,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
    }
}
