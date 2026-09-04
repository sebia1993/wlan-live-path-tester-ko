using System.Runtime.CompilerServices;
using WlanLivePathTester.Core.Models;
using WlanLivePathTester.Core.Observation;
using WlanLivePathTester.Core.Reporting;
using WlanLivePathTester.Windows.Observation;
using WlanLivePathTester.Windows.Wlan;

namespace WlanLivePathTester.WindowsSmoke;

internal static class BrowserObservationTimingRuntimeTests
{
    private const string InterfaceId =
        "C1B2C3D4-E5F6-47A8-9123-1234567890AB";
    private const string Description = "Synthetic Timing Wi-Fi";
    private static readonly DateTimeOffset Start =
        DateTimeOffset.UnixEpoch.AddDays(2);

#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Run()
    {
        StopsBeforeIncludingExcessiveGap();
        StopsBeforeIncludingNonPositiveInterval();
        Console.WriteLine(
            "PASS injectable observation timing discontinuity tests");
    }

    private static void StopsBeforeIncludingExcessiveGap()
    {
        TimingRuntime runtime = new(
            wlanResults:
            [
                ConnectedWlan(Start),
                ConnectedWlan(Start.AddMilliseconds(500)),
                ConnectedWlan(Start.AddSeconds(1)),
                ConnectedWlan(Start.AddMilliseconds(1500)),
                ConnectedWlan(Start.AddSeconds(2)),
                ConnectedWlan(Start.AddMilliseconds(2500)),
                ConnectedWlan(Start.AddSeconds(3)),
                ConnectedWlan(Start.AddMilliseconds(8001))
            ],
            counterResults:
            [
                Counter(Start, 1_000_000),
                Counter(Start.AddMilliseconds(500), 1_062_500),
                Counter(Start.AddSeconds(1), 1_125_000),
                Counter(Start.AddMilliseconds(1500), 1_187_500),
                Counter(Start.AddSeconds(2), 1_250_000),
                Counter(Start.AddMilliseconds(2500), 7_562_500),
                Counter(Start.AddSeconds(3), 13_875_000),
                Counter(Start.AddMilliseconds(8001), 63_875_000)
            ]);

        BrowserObservationResult result = RunObservation(runtime);

        Ensure(result.Status == BrowserObservationStatus.PartialSuccess,
            $"유효 샘플 뒤 시간 단절은 PartialSuccess여야 합니다: {result.Status}");
        Ensure(result.TerminationReason
               == BrowserObservationTerminationReason.TimingDiscontinuity,
            "5초 초과 간격은 TimingDiscontinuity여야 합니다.");
        Ensure(result.Message.Contains(
                "허용 상한",
                StringComparison.Ordinal),
            "시간 단절 결과에 실제 간격과 허용 상한 설명이 필요합니다.");

        BrowserObservationSummary summary = result.Summary
            ?? throw new InvalidOperationException(
                "기준 샘플 네 개와 활성 샘플 두 개는 부분 요약으로 보존돼야 합니다.");
        Ensure(summary.Samples.Count == 6,
            "비정상적인 일곱 번째 카운터 구간은 샘플에 포함하면 안 됩니다.");
        Ensure(summary.ActiveSampleCount == 2,
            "시간 단절 전 활성 처리량 샘플 두 개만 통계에 포함해야 합니다.");
        Ensure(summary.Samples.All(sample =>
                sample.Timestamp <= Start.AddSeconds(3)),
            "시간 단절 이후의 카운터 타임스탬프가 보고서 샘플에 남으면 안 됩니다.");
        Ensure(summary.CompletedAt == Start.AddSeconds(3),
            "부분 요약 종료 시각은 마지막 유효 카운터 시각이어야 합니다.");
        Ensure(summary.ObservedDuration == TimeSpan.FromSeconds(1),
            "활성 샘플 두 개의 1초만 관찰 시간에 포함하고 비정상 5.001초 구간은 제외해야 합니다.");
        Ensure(summary.TotalReceiveBytes == 12_625_000,
            "비정상 구간의 50MB 델타를 총 수신량에 포함하면 안 됩니다.");
        Ensure(summary.AverageAdjustedReceiveMbps is > 99 and < 101,
            "시간 단절 전 정상 활성 샘플의 조정 평균은 약 100 Mbps여야 합니다.");
        Ensure(runtime.CounterReadCount == 8,
            "단절을 판정하기 위한 현재 카운터까지 읽고 즉시 종료해야 합니다.");

        ReportObservationSection mapped =
            ReportObservationMapper.FromResult(result)
            ?? throw new InvalidOperationException(
                "시간 단절 결과를 통합 보고서 관찰 섹션으로 매핑해야 합니다.");
        Ensure(mapped.TerminationReason == "TimingDiscontinuity",
            "통합 보고서 매퍼가 시간 단절 종료 원인을 보존해야 합니다.");

        IReadOnlyList<ReportFinding> findings =
            ReportFindingEngine.Evaluate(
                HealthyWlan(),
                HealthyProxy(),
                Array.Empty<ReportTextSection>(),
                mapped,
                Array.Empty<ReportMeasurementSection>());
        Ensure(findings.Any(finding => finding.Code ==
                "BROWSER_OBSERVATION_TIMING_DISCONTINUITY"),
            "실제 러너 결과에서 시간 단절 고정 Finding까지 생성돼야 합니다.");
    }

    private static void StopsBeforeIncludingNonPositiveInterval()
    {
        TimingRuntime runtime = new(
            wlanResults:
            [
                ConnectedWlan(Start),
                ConnectedWlan(Start)
            ],
            counterResults:
            [
                Counter(Start, 1_000_000),
                Counter(Start, 2_000_000)
            ]);

        BrowserObservationResult result = RunObservation(runtime);

        Ensure(result.Status == BrowserObservationStatus.Failed,
            "첫 샘플의 0초 간격은 유효 결과가 없으므로 Failed여야 합니다.");
        Ensure(result.TerminationReason
               == BrowserObservationTerminationReason.TimingDiscontinuity,
            "0초 카운터 간격도 TimingDiscontinuity여야 합니다.");
        Ensure(result.Summary is null,
            "0초 간격 카운터의 바이트는 샘플이나 요약에 포함하면 안 됩니다.");
        Ensure(result.Message.Contains(
                "같거나 과거",
                StringComparison.Ordinal),
            "0·음수 간격의 원인을 명확히 설명해야 합니다.");
        Ensure(runtime.DelayCallCount == 1,
            "첫 샘플 경계까지의 합성 delay만 실행해야 합니다.");
    }

    private static BrowserObservationResult RunObservation(
        TimingRuntime runtime) =>
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

    private static WlanReadResult ConnectedWlan(
        DateTimeOffset timestamp) =>
        new(
            WlanReadStatus.Success,
            [
                new WlanSnapshot(
                    Timestamp: timestamp,
                    IsConnected: true,
                    Ssid: "Synthetic-Timing-SSID",
                    Bssid: "AA:BB:CC:DD:EE:10",
                    RssiDbm: -55,
                    Channel: 36,
                    PhyType: "802.11ax",
                    ReceiveLinkSpeedBps: 1_200_000_000,
                    TransmitLinkSpeedBps: 1_200_000_000,
                    InterfaceDescription: Description,
                    InterfaceState: "Connected",
                    SignalQualityPercent: 90,
                    CenterFrequencyMhz: 5180,
                    Authentication: "WPA2-Enterprise",
                    Cipher: "CCMP",
                    InterfaceId: InterfaceId)
            ],
            nativeErrorCode: null,
            message: "합성 WLAN 연결");

    private static InterfaceCounterReadResult Counter(
        DateTimeOffset timestamp,
        long received) =>
        new(
            InterfaceCounterReadStatus.Success,
            new InterfaceCounterSnapshot(
                Timestamp: timestamp,
                InterfaceId: InterfaceId,
                InterfaceName: "Synthetic Timing Wi-Fi",
                InterfaceDescription: Description,
                BytesReceived: received,
                BytesSent: 100_000,
                IsOperational: true),
            "합성 카운터 성공");

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

    private static void Ensure(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed class TimingRuntime : IBrowserObservationRuntime
    {
        private readonly Queue<WlanReadResult> _wlanResults;
        private readonly Queue<InterfaceCounterReadResult> _counterResults;
        private readonly WlanInterfaceIdentityReadResult _identityResult;
        private DateTimeOffset _utcNow;

        public TimingRuntime(
            IEnumerable<WlanReadResult> wlanResults,
            IEnumerable<InterfaceCounterReadResult> counterResults)
        {
            _wlanResults = new Queue<WlanReadResult>(wlanResults);
            _counterResults = new Queue<InterfaceCounterReadResult>(
                counterResults);
            _identityResult = new WlanInterfaceIdentityReadResult(
                IsSuccess: true,
                Interfaces:
                [
                    new WlanInterfaceIdentity(
                        InterfaceId,
                        Description,
                        IsConnected: true)
                ],
                Message: "합성 WLAN ID");
            _utcNow = Start;
        }

        public bool IsSupportedPlatform => true;

        public DateTimeOffset UtcNow => _utcNow;

        public int DelayCallCount { get; private set; }

        public int CounterReadCount { get; private set; }

        public WlanReadResult ReadWlan()
        {
            if (_wlanResults.Count == 0)
            {
                throw new InvalidOperationException(
                    "합성 WLAN 결과가 예상보다 많이 요청됐습니다.");
            }

            WlanReadResult result = _wlanResults.Dequeue();
            WlanSnapshot? connected = result.FirstConnectedInterface;
            if (connected is not null
                && connected.Timestamp > _utcNow)
            {
                _utcNow = connected.Timestamp;
            }

            return result;
        }

        public WlanInterfaceIdentityReadResult ReadWlanIdentity() =>
            _identityResult;

        public InterfaceCounterReadResult ReadCounter(
            string? preferredInterfaceId,
            string? preferredInterfaceDescription,
            InterfaceCounterSelectionMode selectionMode)
        {
            CounterReadCount++;
            if (selectionMode
                != InterfaceCounterSelectionMode.RequireExactInterfaceId)
            {
                throw new InvalidOperationException(
                    "시간 연속성 테스트도 정확한 고정 ID 모드여야 합니다.");
            }

            if (_counterResults.Count == 0)
            {
                throw new InvalidOperationException(
                    "합성 카운터 결과가 예상보다 많이 요청됐습니다.");
            }

            InterfaceCounterReadResult result =
                _counterResults.Dequeue();
            if (result.Snapshot is not null
                && result.Snapshot.Timestamp > _utcNow)
            {
                _utcNow = result.Snapshot.Timestamp;
            }

            return result;
        }

        public Task DelayAsync(
            TimeSpan delay,
            CancellationToken cancellationToken)
        {
            DelayCallCount++;
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
    }
}
