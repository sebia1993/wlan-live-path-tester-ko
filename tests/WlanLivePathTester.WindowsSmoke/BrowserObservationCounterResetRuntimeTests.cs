using System.Runtime.CompilerServices;
using WlanLivePathTester.Core.Models;
using WlanLivePathTester.Core.Observation;
using WlanLivePathTester.Core.Reporting;
using WlanLivePathTester.Windows.Observation;
using WlanLivePathTester.Windows.Wlan;

namespace WlanLivePathTester.WindowsSmoke;

internal static class BrowserObservationCounterResetRuntimeTests
{
    private const string InterfaceId =
        "D1B2C3D4-E5F6-47A8-9123-1234567890AB";
    private const string Description =
        "Synthetic Counter Reset Wi-Fi";
    private static readonly DateTimeOffset Start =
        DateTimeOffset.UnixEpoch.AddDays(3);

#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Run()
    {
        RecoversAfterSingleCounterResetWithoutNegativeThroughput();
        Console.WriteLine(
            "PASS injectable browser observation counter reset test");
    }

    private static void
        RecoversAfterSingleCounterResetWithoutNegativeThroughput()
    {
        const int totalSamples = 14;
        const int resetSample = 6;
        List<WlanReadResult> wlanResults = [];
        List<InterfaceCounterReadResult> counterResults = [];

        wlanResults.Add(ConnectedWlan(Start));
        counterResults.Add(Counter(
            Start,
            bytesReceived: 1_000_000,
            bytesSent: 100_000));

        long received = 1_000_000;
        long sent = 100_000;
        for (int sample = 1; sample <= totalSamples; sample++)
        {
            DateTimeOffset timestamp = Start.AddMilliseconds(
                sample * 500L);
            wlanResults.Add(ConnectedWlan(timestamp));

            if (sample == resetSample)
            {
                received = 10_000;
                sent = 1_000;
            }
            else
            {
                bool isBaseline = sample <= 4;
                received += isBaseline ? 62_500 : 6_312_500;
                sent += isBaseline ? 6_250 : 62_500;
            }

            counterResults.Add(Counter(
                timestamp,
                received,
                sent));
        }

        CounterResetRuntime runtime = new(
            wlanResults,
            counterResults);
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

        Ensure(result.Status == BrowserObservationStatus.Success,
            $"단일 카운터 재설정 뒤 복구된 관찰은 Success여야 합니다: {result.Status}");
        Ensure(result.TerminationReason
               == BrowserObservationTerminationReason.Completed,
            "카운터 재설정 자체는 다른 NIC 변경이 아니므로 관찰을 정상 완료해야 합니다.");

        BrowserObservationSummary summary = result.Summary
            ?? throw new InvalidOperationException(
                "카운터 재설정 뒤 복구된 관찰에는 요약이 필요합니다.");
        Ensure(summary.Samples.Count == totalSamples,
            "카운터 재설정 샘플을 상태 근거로 남기고 이후 샘플을 계속 수집해야 합니다.");
        Ensure(summary.CounterResetCount == 1,
            "카운터 재설정은 정확히 한 번 기록돼야 합니다.");
        Ensure(summary.AdapterChangeCount == 0,
            "같은 인터페이스의 카운터 재설정을 NIC 변경으로 분류하면 안 됩니다.");
        Ensure(summary.Confidence == ObservationConfidence.Low,
            "카운터 재설정이 있으면 관찰 신뢰도는 Low여야 합니다.");

        BrowserObservationSample[] resetSamples = summary.Samples
            .Where(sample => sample.CounterReset)
            .ToArray();
        Ensure(resetSamples.Length == 1,
            "CounterReset 플래그가 있는 샘플은 한 개여야 합니다.");
        BrowserObservationSample reset = resetSamples[0];
        Ensure(reset.ReceiveBytesDelta == 0
               && reset.TransmitBytesDelta == 0,
            "감소한 누적 카운터를 음수 바이트 델타로 저장하면 안 됩니다.");
        Ensure(reset.RawReceiveMbps is null
               && reset.RawTransmitMbps is null
               && reset.AdjustedReceiveMbps is null,
            "카운터 재설정 구간에서 처리량 값을 계산하면 안 됩니다.");

        Ensure(summary.Samples.All(sample =>
                sample.ReceiveBytesDelta >= 0
                && sample.TransmitBytesDelta >= 0),
            "어떤 시간축 샘플에도 음수 바이트 델타가 있으면 안 됩니다.");
        Ensure(summary.Samples.All(sample =>
                sample.RawReceiveMbps is null
                || sample.RawReceiveMbps >= 0),
            "어떤 시간축 샘플에도 음수 수신 Mbps가 있으면 안 됩니다.");
        Ensure(summary.Samples.Skip(resetSample).Any(sample =>
                !sample.CounterReset
                && sample.RawReceiveMbps is > 100 and < 102),
            "재설정 다음 정상 카운터부터 처리량 계산이 복구돼야 합니다.");
        Ensure(summary.ActiveSampleCount == 9,
            "활성 구간 10개 중 재설정 샘플 한 개를 제외한 9개만 처리량 통계에 사용해야 합니다.");
        Ensure(summary.AverageAdjustedReceiveMbps is > 99 and < 101,
            "재설정 구간을 제외한 조정 평균은 약 100 Mbps여야 합니다.");
        Ensure(runtime.CounterReadCount == totalSamples + 1,
            "초기 카운터와 계획한 모든 후속 카운터를 읽어야 합니다.");
        Ensure(runtime.CounterRequests.All(request =>
                request.SelectionMode
                == InterfaceCounterSelectionMode.RequireExactInterfaceId
                && Normalize(request.PreferredInterfaceId)
                    == Normalize(InterfaceId)),
            "재설정 전후에도 같은 고정 물리 Wi-Fi ID만 요청해야 합니다.");

        ReportObservationSection mapped =
            ReportObservationMapper.FromResult(result)
            ?? throw new InvalidOperationException(
                "카운터 재설정 결과를 통합 보고서 관찰 섹션으로 매핑해야 합니다.");
        Ensure(mapped.CounterResetCount == 1,
            "통합 보고서에 카운터 재설정 횟수를 보존해야 합니다.");
        Ensure(mapped.TerminationReason == "Completed",
            "통합 보고서에는 정상 완료 종료 원인을 함께 보존해야 합니다.");

        IReadOnlyList<ReportFinding> findings =
            ReportFindingEngine.Evaluate(
                HealthyWlan(),
                HealthyProxy(),
                Array.Empty<ReportTextSection>(),
                mapped,
                Array.Empty<ReportMeasurementSection>());
        Ensure(findings.Any(finding => finding.Code ==
                "BROWSER_OBSERVATION_COMPLETED"),
            "실제 러너 결과에서 정상 완료 Finding이 생성돼야 합니다.");
        Ensure(findings.Any(finding => finding.Code ==
                "BROWSER_OBSERVATION_LOW_CONFIDENCE"),
            "카운터 재설정으로 낮아진 신뢰도 Finding이 생성돼야 합니다.");
    }

    private static WlanReadResult ConnectedWlan(
        DateTimeOffset timestamp) =>
        new(
            WlanReadStatus.Success,
            [
                new WlanSnapshot(
                    Timestamp: timestamp,
                    IsConnected: true,
                    Ssid: "Synthetic-Counter-Reset-SSID",
                    Bssid: "AA:BB:CC:DD:EE:20",
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
        long bytesReceived,
        long bytesSent) =>
        new(
            InterfaceCounterReadStatus.Success,
            new InterfaceCounterSnapshot(
                Timestamp: timestamp,
                InterfaceId: InterfaceId,
                InterfaceName: "Synthetic Counter Reset Wi-Fi",
                InterfaceDescription: Description,
                BytesReceived: bytesReceived,
                BytesSent: bytesSent,
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

    private static string Normalize(string? value)
    {
        string trimmed = (value ?? string.Empty)
            .Trim()
            .Trim('{', '}');
        return Guid.TryParse(trimmed, out Guid parsed)
            ? parsed.ToString("D")
            : trimmed.ToLowerInvariant();
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed record CounterRequest(
        string? PreferredInterfaceId,
        InterfaceCounterSelectionMode SelectionMode);

    private sealed class CounterResetRuntime
        : IBrowserObservationRuntime
    {
        private readonly Queue<WlanReadResult> _wlanResults;
        private readonly Queue<InterfaceCounterReadResult> _counterResults;
        private readonly WlanInterfaceIdentityReadResult _identityResult;
        private DateTimeOffset _utcNow;

        public CounterResetRuntime(
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

        public int CounterReadCount { get; private set; }

        public List<CounterRequest> CounterRequests { get; } = [];

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
            CounterRequests.Add(new CounterRequest(
                preferredInterfaceId,
                selectionMode));
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
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
    }
}
