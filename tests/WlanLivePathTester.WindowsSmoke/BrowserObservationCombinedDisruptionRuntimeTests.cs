using System.Runtime.CompilerServices;
using System.Text.Json;
using WlanLivePathTester.Core.Models;
using WlanLivePathTester.Core.Observation;
using WlanLivePathTester.Core.Reporting;
using WlanLivePathTester.Windows.Observation;
using WlanLivePathTester.Windows.Wlan;

namespace WlanLivePathTester.WindowsSmoke;

internal static class BrowserObservationCombinedDisruptionRuntimeTests
{
    private const string InterfaceId =
        "11B2C3D4-E5F6-47A8-9123-1234567890AB";
    private const string InterfaceDescription =
        "Synthetic Combined Wi-Fi Adapter";
    private const string SecretSsid = "CORP-COMBINED-SECRET";
    private const string SecretBssid = "AA:BB:CC:DD:EE:50";
    private static readonly DateTimeOffset Start =
        DateTimeOffset.UnixEpoch.AddDays(6);

#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Run()
    {
        PreservesResetEvidenceBeforeTimingDiscontinuity();
        Console.WriteLine(
            "PASS browser observation combined disruption runtime test");
    }

    private static void
        PreservesResetEvidenceBeforeTimingDiscontinuity()
    {
        CombinedRuntime runtime = new();
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

        Ensure(result.Status == BrowserObservationStatus.PartialSuccess,
            $"재설정 후 시간 단절은 PartialSuccess여야 합니다: {result.Status}");
        Ensure(result.TerminationReason
               == BrowserObservationTerminationReason.TimingDiscontinuity,
            "최종 직접 종료 원인은 TimingDiscontinuity여야 합니다.");
        Ensure(result.Message.Contains(
                "허용 상한",
                StringComparison.Ordinal),
            "시간 단절의 실제 간격과 허용 상한 설명이 필요합니다.");

        BrowserObservationSummary summary = result.Summary
            ?? throw new InvalidOperationException(
                "재설정 뒤 정상 활성 샘플이 있으므로 부분 요약이 필요합니다.");
        Ensure(summary.Samples.Count == 6,
            "기준 4개, 재설정 1개, 정상 활성 1개만 시간축에 남아야 합니다.");
        Ensure(summary.CounterResetCount == 1,
            "카운터 재설정 근거를 한 번 보존해야 합니다.");
        Ensure(summary.ActiveSampleCount == 1,
            "재설정 샘플은 처리량 통계에서 제외하고 정상 활성 샘플 하나만 집계해야 합니다.");
        Ensure(summary.ObservedDuration
               == TimeSpan.FromMilliseconds(500),
            "정상 활성 샘플 0.5초만 관찰 시간에 포함해야 합니다.");
        Ensure(summary.TotalReceiveBytes == 6_312_500,
            "재설정 구간과 시간 단절 구간의 바이트를 총량에서 제외해야 합니다.");
        Ensure(summary.AverageAdjustedReceiveMbps is > 99 and < 101,
            "복구된 정상 활성 샘플의 조정 평균은 약 100 Mbps여야 합니다.");
        Ensure(summary.PeakAdjustedReceiveMbps is > 99 and < 101,
            "복구된 정상 활성 샘플의 최고값은 약 100 Mbps여야 합니다.");
        Ensure(summary.Confidence == ObservationConfidence.Low,
            "카운터 재설정이 포함된 부분 결과는 Low 신뢰도여야 합니다.");
        Ensure(summary.CompletedAt == Start.AddSeconds(3),
            "부분 요약은 마지막 유효 카운터 시각에 끝나야 합니다.");

        BrowserObservationSample resetSample = summary.Samples.Single(
            sample => sample.CounterReset);
        Ensure(!resetSample.IsBaseline,
            "합성 재설정은 활성 관찰 구간에서 발생해야 합니다.");
        Ensure(resetSample.ReceiveBytesDelta == 0
               && resetSample.TransmitBytesDelta == 0,
            "재설정 구간의 Rx·Tx 델타는 0이어야 합니다.");
        Ensure(resetSample.RawReceiveMbps is null
               && resetSample.RawTransmitMbps is null
               && resetSample.AdjustedReceiveMbps is null,
            "재설정 구간에는 계산된 Mbps가 없어야 합니다.");
        Ensure(summary.Samples.All(sample =>
                sample.Timestamp <= Start.AddSeconds(3)),
            "5.001초 시간 단절 카운터를 시간축에 포함하면 안 됩니다.");
        Ensure(runtime.CounterReadCount == 8
               && runtime.WlanReadCount == 8,
            "단절을 판정할 현재 상태까지만 읽고 즉시 종료해야 합니다.");

        BrowserObservationSessionReportDocument dedicated =
            BrowserObservationSessionReportWriter.CreateDocument(
                result,
                "0.1.0-test",
                Start.AddMinutes(1));
        BrowserObservationSessionReportSummary dedicatedSummary =
            dedicated.Summary
            ?? throw new InvalidOperationException(
                "전용 보고서에 부분 요약이 필요합니다.");
        Ensure(dedicated.TerminationReason
               == "TimingDiscontinuity",
            "전용 보고서가 최종 시간 단절 원인을 유지해야 합니다.");
        Ensure(dedicatedSummary.CounterResetCount == 1
               && dedicatedSummary.ActiveSampleCount == 1,
            "전용 보고서가 재설정 근거와 정상 활성 샘플 수를 함께 유지해야 합니다.");
        Ensure(dedicatedSummary.Samples.Count == 6,
            "전용 보고서에도 시간 단절 전 샘플만 있어야 합니다.");

        ReportObservationSection mapped =
            ReportObservationMapper.FromResult(result)
            ?? throw new InvalidOperationException(
                "복합 결과를 통합 보고서 관찰 섹션으로 매핑해야 합니다.");
        Ensure(mapped.TerminationReason
               == "TimingDiscontinuity"
               && mapped.CounterResetCount == 1,
            "통합 보고서가 최종 종료 원인과 이전 재설정 근거를 모두 유지해야 합니다.");

        IReadOnlyList<ReportFinding> findings =
            ReportFindingEngine.Evaluate(
                HealthyWlan(),
                HealthyProxy(),
                Array.Empty<ReportTextSection>(),
                mapped,
                Array.Empty<ReportMeasurementSection>());
        string[] expectedCodes =
        [
            "BROWSER_OBSERVATION_TIMING_DISCONTINUITY",
            "BROWSER_OBSERVATION_COUNTER_RESET",
            "BROWSER_OBSERVATION_LOW_CONFIDENCE"
        ];
        foreach (string code in expectedCodes)
        {
            Ensure(findings.Count(finding => finding.Code == code) == 1,
                $"복합 결과에는 Finding {code}가 정확히 한 개 필요합니다.");
        }

        string[] forbiddenCodes =
        [
            "BROWSER_OBSERVATION_ADAPTER_CHANGED",
            "BROWSER_OBSERVATION_ADAPTER_UNAVAILABLE",
            "BROWSER_OBSERVATION_COUNTER_PROVIDER_MISMATCH",
            "BROWSER_OBSERVATION_COMPLETED"
        ];
        foreach (string code in forbiddenCodes)
        {
            Ensure(!findings.Any(finding => finding.Code == code),
                $"복합 재설정·시간 단절을 {code}로 오인하면 안 됩니다.");
        }

        LocalDiagnosticReport unified = CreateUnifiedReport(
            mapped,
            findings);
        string dedicatedJson =
            BrowserObservationSessionReportWriter.RenderJson(dedicated);
        string dedicatedCsv =
            BrowserObservationSessionReportWriter.RenderCsv(dedicated);
        string dedicatedHtml =
            BrowserObservationSessionReportWriter.RenderHtml(dedicated);
        string unifiedJson = LocalReportWriter.RenderJson(unified);
        string unifiedCsv = LocalReportWriter.RenderCsv(unified);
        string unifiedHtml = LocalReportWriter.RenderHtml(unified);

        using JsonDocument parsed = JsonDocument.Parse(unifiedJson);
        Ensure(parsed.RootElement
                .GetProperty("browserObservation")
                .GetProperty("terminationReason")
                .GetString() == "TimingDiscontinuity",
            "통합 JSON에 최종 시간 단절 원인이 필요합니다.");
        Ensure(parsed.RootElement
                .GetProperty("browserObservation")
                .GetProperty("counterResetCount")
                .GetInt32() == 1,
            "통합 JSON에 카운터 재설정 횟수가 필요합니다.");
        foreach (string code in expectedCodes.Take(2))
        {
            Ensure(unifiedJson.Contains(code, StringComparison.Ordinal)
                   && unifiedCsv.Contains(code, StringComparison.Ordinal),
                $"통합 JSON·CSV에 Finding 코드 {code}가 필요합니다.");
        }
        Ensure(unifiedHtml.Contains(
                "샘플 시간 연속성 중단",
                StringComparison.Ordinal)
               && unifiedHtml.Contains(
                   "인터페이스 카운터 재설정",
                   StringComparison.Ordinal),
            "통합 HTML에 두 복합 Finding의 사람이 읽을 수 있는 제목이 필요합니다.");

        string allOutputs = string.Join(
            Environment.NewLine,
            dedicatedJson,
            dedicatedCsv,
            dedicatedHtml,
            unifiedJson,
            unifiedCsv,
            unifiedHtml);
        AssertSecretsAbsent(allOutputs);
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
                $"복합 장애 보고서 출력에 합성 민감값이 남았습니다: {secret}");
        }
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed class CombinedRuntime : IBrowserObservationRuntime
    {
        private static readonly DateTimeOffset[] Timestamps =
        [
            Start,
            Start.AddMilliseconds(500),
            Start.AddSeconds(1),
            Start.AddMilliseconds(1500),
            Start.AddSeconds(2),
            Start.AddMilliseconds(2500),
            Start.AddSeconds(3),
            Start.AddMilliseconds(8001)
        ];

        private static readonly long[] ReceiveCounters =
        [
            1_000_000,
            1_062_500,
            1_125_000,
            1_187_500,
            1_250_000,
            500_000,
            6_812_500,
            56_812_500
        ];

        private int _wlanReadIndex;
        private int _counterReadIndex;
        private DateTimeOffset _utcNow = Start;

        public bool IsSupportedPlatform => true;

        public DateTimeOffset UtcNow => _utcNow;

        public int WlanReadCount => _wlanReadIndex;

        public int CounterReadCount => _counterReadIndex;

        public WlanReadResult ReadWlan()
        {
            if (_wlanReadIndex >= Timestamps.Length)
            {
                throw new InvalidOperationException(
                    "합성 WLAN 상태가 예상보다 많이 요청됐습니다.");
            }

            DateTimeOffset timestamp = Timestamps[_wlanReadIndex++];
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
            if (_counterReadIndex >= Timestamps.Length)
            {
                throw new InvalidOperationException(
                    "합성 카운터가 예상보다 많이 요청됐습니다.");
            }

            if (selectionMode
                    != InterfaceCounterSelectionMode.RequireExactInterfaceId
                || !string.Equals(
                    Normalize(preferredInterfaceId),
                    Normalize(InterfaceId),
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "복합 장애 테스트에서도 고정한 정확한 물리 Wi-Fi ID만 요청해야 합니다.");
            }

            int index = _counterReadIndex++;
            DateTimeOffset timestamp = Timestamps[index];
            _utcNow = timestamp;
            return new InterfaceCounterReadResult(
                InterfaceCounterReadStatus.Success,
                new InterfaceCounterSnapshot(
                    Timestamp: timestamp,
                    InterfaceId: InterfaceId,
                    InterfaceName: "Synthetic Combined Wi-Fi",
                    InterfaceDescription: InterfaceDescription,
                    BytesReceived: ReceiveCounters[index],
                    BytesSent: index == 5
                        ? 50_000
                        : 100_000 + index * 10_000L,
                    IsOperational: true),
                "합성 카운터 성공");
        }

        public Task DelayAsync(
            TimeSpan delay,
            CancellationToken cancellationToken)
        {
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
