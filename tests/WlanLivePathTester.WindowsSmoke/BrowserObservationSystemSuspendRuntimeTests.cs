using System.Runtime.CompilerServices;
using WlanLivePathTester.Core.Models;
using WlanLivePathTester.Core.Observation;
using WlanLivePathTester.Core.Reporting;
using WlanLivePathTester.Windows.Observation;
using WlanLivePathTester.Windows.Wlan;

namespace WlanLivePathTester.WindowsSmoke;

internal static class BrowserObservationSystemSuspendRuntimeTests
{
    private const string InterfaceId =
        "F1B2C3D4-E5F6-47A8-9123-1234567890AB";
    private const string InterfaceDescription =
        "Synthetic Suspend Wi-Fi Adapter";
    private const string SecretSsid = "CORP-SUSPEND-SECRET";
    private const string SecretBssid = "AA:BB:CC:DD:EE:40";
    private static readonly DateTimeOffset Start =
        DateTimeOffset.UnixEpoch.AddDays(5);

#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Run()
    {
        SystemSuspendOverridesCancellationAndPreservesValidSamples();
        ExplicitUserStopRemainsCanceledByUser();
        Console.WriteLine(
            "PASS browser observation system suspend runtime tests");
    }

    private static void
        SystemSuspendOverridesCancellationAndPreservesValidSamples()
    {
        BrowserObservationCancellationContext context = new();
        using CancellationTokenSource cancellation = new();
        CancellationRuntime runtime = new(
            context,
            cancellation,
            BrowserObservationTerminationReason.SystemSuspend,
            cancelOnDelayCall: 6);

        BrowserObservationResult result = RunObservation(
            runtime,
            cancellation.Token,
            context);

        Ensure(result.Status == BrowserObservationStatus.Canceled,
            $"합성 Suspend는 Canceled 상태여야 합니다: {result.Status}");
        Ensure(result.TerminationReason
               == BrowserObservationTerminationReason.SystemSuspend,
            "절전 취소는 사용자 중지가 아닌 SystemSuspend여야 합니다.");
        Ensure(result.Message.Contains(
                "전원 전환 전후",
                StringComparison.Ordinal),
            "절전 전후 카운터를 결합하지 않는다는 설명이 필요합니다.");

        BrowserObservationSummary summary = result.Summary
            ?? throw new InvalidOperationException(
                "절전 전 유효 샘플은 부분 요약으로 보존돼야 합니다.");
        Ensure(summary.Samples.Count == 5,
            "기준 샘플 네 개와 활성 샘플 한 개만 보존해야 합니다.");
        Ensure(summary.ActiveSampleCount == 1,
            "절전 전 활성 샘플 한 개만 집계해야 합니다.");
        Ensure(summary.ObservedDuration == TimeSpan.FromMilliseconds(500),
            "절전 대기 시간은 활성 관찰 시간에 포함하면 안 됩니다.");
        Ensure(summary.TotalReceiveBytes == 6_312_500,
            "절전 전 마지막 정상 활성 샘플의 수신량만 유지해야 합니다.");
        Ensure(summary.Samples[^1].Timestamp
               == Start.AddMilliseconds(2500),
            "마지막 유효 카운터 시각까지만 시간축에 남아야 합니다.");
        Ensure(runtime.DelayCallCount == 6,
            "여섯 번째 delay에서 Suspend가 발생해야 합니다.");
        Ensure(runtime.WlanReadCount == 6
               && runtime.CounterReadCount == 6,
            "Suspend 뒤 WLAN 또는 카운터를 추가로 읽으면 안 됩니다.");

        BrowserObservationSessionReportDocument dedicated =
            BrowserObservationSessionReportWriter.CreateDocument(
                result,
                "0.1.0-test",
                Start.AddMinutes(1));
        Ensure(dedicated.TerminationReason == "SystemSuspend",
            "관찰 전용 보고서에 SystemSuspend가 필요합니다.");
        Ensure(dedicated.Summary?.Samples.Count == 5,
            "관찰 전용 보고서에는 절전 전 샘플만 있어야 합니다.");

        ReportObservationSection mapped =
            ReportObservationMapper.FromResult(result)
            ?? throw new InvalidOperationException(
                "절전 결과를 통합 보고서 관찰 섹션으로 매핑해야 합니다.");
        Ensure(mapped.TerminationReason == "SystemSuspend",
            "통합 보고서에도 SystemSuspend가 필요합니다.");
        IReadOnlyList<ReportFinding> findings =
            ReportFindingEngine.Evaluate(
                HealthyWlan(),
                HealthyProxy(),
                Array.Empty<ReportTextSection>(),
                mapped,
                Array.Empty<ReportMeasurementSection>());
        ReportFinding suspendFinding = findings.Single(finding =>
            finding.Code.Equals(
                "BROWSER_OBSERVATION_SYSTEM_SUSPEND",
                StringComparison.Ordinal));
        Ensure(suspendFinding.Severity == "Warning",
            "시스템 절전 Finding은 Warning이어야 합니다.");

        LocalDiagnosticReport unified = CreateUnifiedReport(
            mapped,
            findings);
        string allOutputs = string.Join(
            Environment.NewLine,
            BrowserObservationSessionReportWriter.RenderJson(dedicated),
            BrowserObservationSessionReportWriter.RenderCsv(dedicated),
            BrowserObservationSessionReportWriter.RenderHtml(dedicated),
            LocalReportWriter.RenderJson(unified),
            LocalReportWriter.RenderCsv(unified),
            LocalReportWriter.RenderHtml(unified));

        Ensure(allOutputs.Contains(
                "SystemSuspend",
                StringComparison.Ordinal),
            "전용·통합 보고서 출력에 구조화 절전 원인이 필요합니다.");
        Ensure(allOutputs.Contains(
                suspendFinding.Title,
                StringComparison.Ordinal),
            "통합 사람용 보고서에 절전 Finding 제목이 필요합니다.");
        AssertSecretsAbsent(allOutputs);
    }

    private static void ExplicitUserStopRemainsCanceledByUser()
    {
        BrowserObservationCancellationContext context = new();
        using CancellationTokenSource cancellation = new();
        CancellationRuntime runtime = new(
            context,
            cancellation,
            BrowserObservationTerminationReason.CanceledByUser,
            cancelOnDelayCall: 1);

        BrowserObservationResult result = RunObservation(
            runtime,
            cancellation.Token,
            context);

        Ensure(result.Status == BrowserObservationStatus.Canceled,
            "명시 사용자 중지는 Canceled여야 합니다.");
        Ensure(result.TerminationReason
               == BrowserObservationTerminationReason.CanceledByUser,
            "사용자 중지를 SystemSuspend로 오인하면 안 됩니다.");
        Ensure(result.Summary is null,
            "첫 샘플 전 사용자 중지에는 요약이 없어야 합니다.");
        Ensure(result.Message.Contains(
                "사용자 요청",
                StringComparison.Ordinal),
            "사용자 중지 전용 설명이 필요합니다.");
    }

    private static BrowserObservationResult RunObservation(
        CancellationRuntime runtime,
        CancellationToken cancellationToken,
        BrowserObservationCancellationContext context) =>
        new BrowserObservationRunner(runtime)
            .RunAsync(
                new BrowserObservationOptions(
                    BaselineSeconds: 2,
                    ObservationSeconds: 5,
                    SampleIntervalMilliseconds: 500),
                progress: null,
                cancellationToken,
                context)
            .GetAwaiter()
            .GetResult();

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

    private static void AssertSecretsAbsent(string content)
    {
        string[] secrets =
        [
            InterfaceId,
            InterfaceDescription,
            SecretSsid,
            SecretBssid
        ];

        foreach (string secret in secrets)
        {
            Ensure(!content.Contains(
                    secret,
                    StringComparison.OrdinalIgnoreCase),
                $"절전 보고서 출력에 합성 민감값이 남았습니다: {secret}");
        }
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed class CancellationRuntime : IBrowserObservationRuntime
    {
        private readonly BrowserObservationCancellationContext _context;
        private readonly CancellationTokenSource _cancellation;
        private readonly BrowserObservationTerminationReason _reason;
        private readonly int _cancelOnDelayCall;
        private DateTimeOffset _utcNow = Start;

        public CancellationRuntime(
            BrowserObservationCancellationContext context,
            CancellationTokenSource cancellation,
            BrowserObservationTerminationReason reason,
            int cancelOnDelayCall)
        {
            _context = context;
            _cancellation = cancellation;
            _reason = reason;
            _cancelOnDelayCall = cancelOnDelayCall;
        }

        public bool IsSupportedPlatform => true;

        public DateTimeOffset UtcNow => _utcNow;

        public int DelayCallCount { get; private set; }

        public int WlanReadCount { get; private set; }

        public int CounterReadCount { get; private set; }

        public WlanReadResult ReadWlan()
        {
            WlanReadCount++;
            DateTimeOffset timestamp = Start.AddMilliseconds(
                Math.Max(0, WlanReadCount - 1) * 500L);
            _utcNow = timestamp;
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
                Message: "합성 WLAN identity");

        public InterfaceCounterReadResult ReadCounter(
            string? preferredInterfaceId,
            string? preferredInterfaceDescription,
            InterfaceCounterSelectionMode selectionMode)
        {
            CounterReadCount++;
            if (selectionMode
                    != InterfaceCounterSelectionMode.RequireExactInterfaceId
                || !string.Equals(
                    Normalize(preferredInterfaceId),
                    Normalize(InterfaceId),
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "절전 테스트에서도 시작 시 고정한 정확한 Wi-Fi ID만 요청해야 합니다.");
            }

            int sampleIndex = CounterReadCount - 1;
            DateTimeOffset timestamp = Start.AddMilliseconds(
                sampleIndex * 500L);
            _utcNow = timestamp;
            long received = 1_000_000;
            if (sampleIndex > 0)
            {
                int baselineSamples = Math.Min(sampleIndex, 4);
                int activeSamples = Math.Max(0, sampleIndex - 4);
                received += baselineSamples * 62_500L;
                received += activeSamples * 6_312_500L;
            }

            return new InterfaceCounterReadResult(
                InterfaceCounterReadStatus.Success,
                new InterfaceCounterSnapshot(
                    Timestamp: timestamp,
                    InterfaceId: InterfaceId,
                    InterfaceName: "Synthetic Suspend Wi-Fi",
                    InterfaceDescription: InterfaceDescription,
                    BytesReceived: received,
                    BytesSent: 100_000 + sampleIndex * 10_000L,
                    IsOperational: true),
                "합성 카운터 성공");
        }

        public Task DelayAsync(
            TimeSpan delay,
            CancellationToken cancellationToken)
        {
            DelayCallCount++;
            if (DelayCallCount == _cancelOnDelayCall)
            {
                if (_reason
                    == BrowserObservationTerminationReason.SystemSuspend)
                {
                    _context.RequestSystemSuspend();
                }
                else
                {
                    _context.RequestUserCancellation();
                }

                _cancellation.Cancel();
            }

            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        private static string Normalize(string? value)
        {
            string trimmed = (value ?? string.Empty)
                .Trim()
                .Trim('{', '}');
            return Guid.TryParse(trimmed, out Guid parsed)
                ? parsed.ToString("D")
                : trimmed.ToLowerInvariant();
        }
    }
}
