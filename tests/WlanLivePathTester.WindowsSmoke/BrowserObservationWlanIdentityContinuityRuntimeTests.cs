using System.Runtime.CompilerServices;
using WlanLivePathTester.Core.Models;
using WlanLivePathTester.Core.Observation;
using WlanLivePathTester.Core.Reporting;
using WlanLivePathTester.Windows.Observation;
using WlanLivePathTester.Windows.Wlan;

namespace WlanLivePathTester.WindowsSmoke;

internal static class BrowserObservationWlanIdentityContinuityRuntimeTests
{
    private const string PrimaryId =
        "41B2C3D4-E5F6-47A8-9123-1234567890AB";
    private const string SecondaryId =
        "51B2C3D4-E5F6-47A8-9123-1234567890AB";
    private const string Description =
        "Synthetic Identity Wi-Fi Adapter";
    private const string SecretSsid = "CORP-IDENTITY-SECRET";
    private const string SecretBssid = "AA:BB:CC:DD:EE:70";
    private static readonly DateTimeOffset Start =
        DateTimeOffset.UnixEpoch.AddDays(7);

#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Run()
    {
        CompletesAfterOneGapAndSameIdentityRecovery();
        StopsBeforeCounterOnThirdConsecutiveGap();
        StopsImmediatelyWhenDifferentIdentityAppearsAfterGap();
        Console.WriteLine(
            "PASS browser observation WLAN identity continuity runtime tests");
    }

    private static void
        CompletesAfterOneGapAndSameIdentityRecovery()
    {
        WlanSequence sequence = WlanSequence.ForCompletedRecovery();
        IdentityRuntime runtime = new(sequence);
        BrowserObservationResult result = RunObservation(runtime);

        Ensure(result.Status == BrowserObservationStatus.Success,
            $"한 번의 WLAN ID 누락 후 복구는 Success여야 합니다: {result.Status}");
        Ensure(result.TerminationReason
               == BrowserObservationTerminationReason.Completed,
            "동일 ID 복구 뒤 관찰은 Completed여야 합니다.");

        BrowserObservationSummary summary = result.Summary
            ?? throw new InvalidOperationException(
                "복구된 관찰에는 요약이 필요합니다.");
        Ensure(summary.Samples.Count == 14,
            "일시 누락 뒤에도 계획한 14개 샘플을 모두 보존해야 합니다.");
        Ensure(summary.WlanDisconnectedSampleCount == 1,
            "ID를 확인하지 못한 한 샘플을 WLAN 미확인으로 기록해야 합니다.");
        Ensure(summary.ActiveSampleCount == 9,
            "WLAN ID 미확인 활성 샘플 한 개를 통계에서 제외하고 나머지 9개를 사용해야 합니다.");
        Ensure(summary.AdapterChangeCount == 0,
            "동일 ID 복구를 물리 NIC 변경으로 기록하면 안 됩니다.");
        Ensure(summary.Confidence == ObservationConfidence.Low,
            "WLAN ID 미확인 샘플이 있으면 신뢰도는 Low여야 합니다.");

        BrowserObservationSample unavailable = summary.Samples[4];
        BrowserObservationSample recovered = summary.Samples[5];
        Ensure(unavailable.WlanDisconnected,
            "일시 ID 미확인 샘플은 WLAN 미확인 플래그가 필요합니다.");
        Ensure(unavailable.Note?.Contains(
                "1/3",
                StringComparison.Ordinal) == true,
            "일시 미확인 메모에 현재 횟수와 임계값이 필요합니다.");
        Ensure(!recovered.WlanDisconnected,
            "동일 ID 복구 샘플은 WLAN 연결 상태로 돌아와야 합니다.");
        Ensure(recovered.Note?.Contains(
                "동일 인터페이스로 복구",
                StringComparison.Ordinal) == true,
            "복구 샘플에 동일 고정 ID 복구 메모가 필요합니다.");
        Ensure(runtime.CounterReadCount == 15
               && runtime.WlanReadCount == 15,
            "정상 완료 시 초기 상태와 14개 후속 상태를 모두 읽어야 합니다.");
        Ensure(runtime.CounterRequests.All(request =>
                request.SelectionMode
                    == InterfaceCounterSelectionMode.RequireExactInterfaceId
                && Normalize(request.PreferredInterfaceId)
                    == Normalize(PrimaryId)),
            "WLAN ID 일시 누락 중에도 시작 시 고정한 정확한 카운터 ID만 사용해야 합니다.");

        ReportObservationSection mapped =
            ReportObservationMapper.FromResult(result)
            ?? throw new InvalidOperationException(
                "복구 결과를 통합 관찰 섹션으로 매핑해야 합니다.");
        IReadOnlyList<ReportFinding> findings = EvaluateFindings(mapped);
        Ensure(findings.Any(finding => finding.Code ==
                "BROWSER_OBSERVATION_COMPLETED"),
            "복구 후 정상 완료 Finding이 필요합니다.");
        Ensure(findings.Any(finding => finding.Code ==
                "BROWSER_OBSERVATION_LOW_CONFIDENCE"),
            "일시 ID 누락에 따른 낮은 신뢰도 Finding이 필요합니다.");
        Ensure(!findings.Any(finding => finding.Code ==
                "BROWSER_OBSERVATION_WLAN_IDENTITY_UNAVAILABLE"),
            "임계값 전에 동일 ID가 복구되면 종료 Finding을 생성하면 안 됩니다.");
    }

    private static void StopsBeforeCounterOnThirdConsecutiveGap()
    {
        WlanSequence sequence = WlanSequence.ForThreeMissingSamples();
        IdentityRuntime runtime = new(sequence);
        BrowserObservationResult result = RunObservation(runtime);

        Ensure(result.Status
               == BrowserObservationStatus.AdapterUnavailable,
            $"세 번째 WLAN ID 미확인은 AdapterUnavailable 상태여야 합니다: {result.Status}");
        Ensure(result.TerminationReason
               == BrowserObservationTerminationReason
                   .WlanIdentityUnavailable,
            "세 번 연속 미확인은 WlanIdentityUnavailable 종료 원인이어야 합니다.");
        Ensure(result.Message.Contains(
                "연속 3회",
                StringComparison.Ordinal),
            "중단 메시지에 실제 연속 미확인 횟수가 필요합니다.");

        BrowserObservationSummary summary = result.Summary
            ?? throw new InvalidOperationException(
                "임계값 전 샘플은 부분 요약으로 보존돼야 합니다.");
        Ensure(summary.Samples.Count == 6,
            "기준 4개와 임계값 전 미확인 2개만 보존해야 합니다.");
        Ensure(summary.WlanDisconnectedSampleCount == 2,
            "임계값 전 두 미확인 샘플을 기록해야 합니다.");
        Ensure(summary.ActiveSampleCount == 0,
            "WLAN identity가 없는 활성 샘플은 처리량 통계에 사용하면 안 됩니다.");
        Ensure(summary.Confidence == ObservationConfidence.Low,
            "연속 WLAN ID 미확인 결과는 Low 신뢰도여야 합니다.");
        Ensure(summary.Samples[4].Note?.Contains(
                "1/3",
                StringComparison.Ordinal) == true
               && summary.Samples[5].Note?.Contains(
                   "2/3",
                   StringComparison.Ordinal) == true,
            "임계값 전 샘플에 1/3과 2/3 메모가 필요합니다.");
        Ensure(runtime.WlanReadCount == 8,
            "초기·기준 4개·미확인 3개 WLAN 상태를 읽어야 합니다.");
        Ensure(runtime.CounterReadCount == 7,
            "세 번째 미확인에서는 카운터를 읽기 전에 중단해야 합니다.");

        BrowserObservationSessionReportDocument dedicated =
            BrowserObservationSessionReportWriter.CreateDocument(
                result,
                "0.1.0-test",
                Start.AddMinutes(1));
        Ensure(dedicated.TerminationReason
               == "WlanIdentityUnavailable",
            "전용 보고서에 WLAN ID 연속 미확인 종료 원인이 필요합니다.");
        Ensure(dedicated.Summary?.WlanDisconnectedSampleCount == 2,
            "전용 보고서에 임계값 전 미확인 샘플 수가 필요합니다.");

        ReportObservationSection mapped =
            ReportObservationMapper.FromResult(result)
            ?? throw new InvalidOperationException(
                "WLAN ID 미확인 결과를 통합 관찰 섹션으로 매핑해야 합니다.");
        Ensure(mapped.TerminationReason
               == "WlanIdentityUnavailable",
            "통합 보고서에 WLAN ID 연속 미확인 종료 원인이 필요합니다.");
        IReadOnlyList<ReportFinding> findings = EvaluateFindings(mapped);
        Ensure(findings.Count(finding => finding.Code ==
                "BROWSER_OBSERVATION_WLAN_IDENTITY_UNAVAILABLE") == 1,
            "WLAN ID 연속 미확인 Warning Finding이 정확히 한 개 필요합니다.");
        Ensure(!findings.Any(finding => finding.Code ==
                "BROWSER_OBSERVATION_ADAPTER_CHANGED"),
            "ID 미확인을 실제 다른 물리 NIC 변경으로 오인하면 안 됩니다.");
        Ensure(!findings.Any(finding => finding.Code ==
                "BROWSER_OBSERVATION_COUNTER_PROVIDER_MISMATCH"),
            "고정 카운터가 유지된 ID 미확인을 공급자 불일치로 오인하면 안 됩니다.");
    }

    private static void
        StopsImmediatelyWhenDifferentIdentityAppearsAfterGap()
    {
        WlanSequence sequence = WlanSequence.ForMissingThenChanged();
        IdentityRuntime runtime = new(sequence);
        BrowserObservationResult result = RunObservation(runtime);

        Ensure(result.Status == BrowserObservationStatus.AdapterChanged,
            "미확인 뒤 실제 다른 GUID가 나타나면 즉시 AdapterChanged여야 합니다.");
        Ensure(result.TerminationReason
               == BrowserObservationTerminationReason.AdapterChanged,
            "실제 다른 GUID는 AdapterChanged 종료 원인이어야 합니다.");
        Ensure(result.Message.Contains(
                "임계값을 기다리지 않고 즉시 중단",
                StringComparison.Ordinal),
            "실제 ID 변경은 미확인 임계값을 기다리지 않는다는 설명이 필요합니다.");

        BrowserObservationSummary summary = result.Summary
            ?? throw new InvalidOperationException(
                "변경 전 일시 미확인 샘플은 부분 요약으로 남아야 합니다.");
        Ensure(summary.Samples.Count == 5
               && summary.WlanDisconnectedSampleCount == 1,
            "기준 4개와 변경 전 미확인 1개만 보존해야 합니다.");
        Ensure(runtime.WlanReadCount == 7
               && runtime.CounterReadCount == 6,
            "다른 GUID를 확인한 현재 샘플에서는 카운터를 읽기 전에 중단해야 합니다.");

        ReportObservationSection mapped =
            ReportObservationMapper.FromResult(result)
            ?? throw new InvalidOperationException(
                "AdapterChanged 결과를 통합 관찰 섹션으로 매핑해야 합니다.");
        IReadOnlyList<ReportFinding> findings = EvaluateFindings(mapped);
        Ensure(findings.Count(finding => finding.Code ==
                "BROWSER_OBSERVATION_ADAPTER_CHANGED") == 1,
            "실제 다른 GUID에는 AdapterChanged Finding이 필요합니다.");
        Ensure(!findings.Any(finding => finding.Code ==
                "BROWSER_OBSERVATION_WLAN_IDENTITY_UNAVAILABLE"),
            "실제 다른 GUID를 WLAN ID 미확인 임계값 초과로 분류하면 안 됩니다.");
    }

    private static BrowserObservationResult RunObservation(
        IdentityRuntime runtime) =>
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

    private static IReadOnlyList<ReportFinding> EvaluateFindings(
        ReportObservationSection observation) =>
        ReportFindingEngine.Evaluate(
            HealthyWlan(),
            HealthyProxy(),
            Array.Empty<ReportTextSection>(),
            observation,
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

    private sealed class WlanSequence
    {
        private WlanSequence(
            IReadOnlyList<WlanReadResult> wlanResults,
            IReadOnlyList<InterfaceCounterReadResult> counterResults)
        {
            WlanResults = wlanResults;
            CounterResults = counterResults;
        }

        public IReadOnlyList<WlanReadResult> WlanResults { get; }

        public IReadOnlyList<InterfaceCounterReadResult> CounterResults
        {
            get;
        }

        public static WlanSequence ForCompletedRecovery()
        {
            const int sampleCount = 14;
            List<WlanReadResult> wlan = [];
            List<InterfaceCounterReadResult> counters = [];
            wlan.Add(Connected(Start, PrimaryId));
            counters.Add(Counter(Start, 1_000_000));

            long received = 1_000_000;
            for (int sample = 1; sample <= sampleCount; sample++)
            {
                DateTimeOffset timestamp = Start.AddMilliseconds(
                    sample * 500L);
                wlan.Add(sample == 5
                    ? MissingWlan()
                    : Connected(timestamp, PrimaryId));
                received += sample <= 4 ? 62_500 : 6_312_500;
                counters.Add(Counter(timestamp, received));
            }

            return new WlanSequence(wlan, counters);
        }

        public static WlanSequence ForThreeMissingSamples()
        {
            List<WlanReadResult> wlan = [];
            List<InterfaceCounterReadResult> counters = [];
            wlan.Add(Connected(Start, PrimaryId));
            counters.Add(Counter(Start, 1_000_000));
            long received = 1_000_000;

            for (int sample = 1; sample <= 7; sample++)
            {
                DateTimeOffset timestamp = Start.AddMilliseconds(
                    sample * 500L);
                wlan.Add(sample >= 5
                    ? MissingWlan()
                    : Connected(timestamp, PrimaryId));
                if (sample <= 6)
                {
                    received += sample <= 4
                        ? 62_500
                        : 6_312_500;
                    counters.Add(Counter(timestamp, received));
                }
            }

            return new WlanSequence(wlan, counters);
        }

        public static WlanSequence ForMissingThenChanged()
        {
            List<WlanReadResult> wlan = [];
            List<InterfaceCounterReadResult> counters = [];
            wlan.Add(Connected(Start, PrimaryId));
            counters.Add(Counter(Start, 1_000_000));
            long received = 1_000_000;

            for (int sample = 1; sample <= 6; sample++)
            {
                DateTimeOffset timestamp = Start.AddMilliseconds(
                    sample * 500L);
                wlan.Add(sample switch
                {
                    5 => MissingWlan(),
                    6 => Connected(timestamp, SecondaryId),
                    _ => Connected(timestamp, PrimaryId)
                });
                if (sample <= 5)
                {
                    received += sample <= 4
                        ? 62_500
                        : 6_312_500;
                    counters.Add(Counter(timestamp, received));
                }
            }

            return new WlanSequence(wlan, counters);
        }

        private static WlanReadResult Connected(
            DateTimeOffset timestamp,
            string interfaceId) =>
            new(
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
                        InterfaceDescription: Description,
                        InterfaceState: "Connected",
                        SignalQualityPercent: 90,
                        CenterFrequencyMhz: 5180,
                        Authentication: "WPA2-Enterprise",
                        Cipher: "CCMP",
                        InterfaceId: interfaceId)
                ],
                nativeErrorCode: null,
                message: "합성 WLAN 연결");

        private static WlanReadResult MissingWlan() =>
            new(
                WlanReadStatus.NotConnected,
                Array.Empty<WlanSnapshot>(),
                nativeErrorCode: null,
                message: "합성 WLAN 일시 미확인");

        private static InterfaceCounterReadResult Counter(
            DateTimeOffset timestamp,
            long received) =>
            new(
                InterfaceCounterReadStatus.Success,
                new InterfaceCounterSnapshot(
                    Timestamp: timestamp,
                    InterfaceId: PrimaryId,
                    InterfaceName: "Synthetic Identity Wi-Fi",
                    InterfaceDescription: Description,
                    BytesReceived: received,
                    BytesSent: 100_000,
                    IsOperational: true),
                "합성 카운터 성공");
    }

    private sealed class IdentityRuntime : IBrowserObservationRuntime
    {
        private readonly Queue<WlanReadResult> _wlanResults;
        private readonly Queue<InterfaceCounterReadResult> _counterResults;
        private readonly WlanInterfaceIdentityReadResult _identityResult;
        private DateTimeOffset _utcNow = Start;

        public IdentityRuntime(WlanSequence sequence)
        {
            _wlanResults = new Queue<WlanReadResult>(
                sequence.WlanResults);
            _counterResults = new Queue<InterfaceCounterReadResult>(
                sequence.CounterResults);
            _identityResult = new WlanInterfaceIdentityReadResult(
                IsSuccess: true,
                Interfaces:
                [
                    new WlanInterfaceIdentity(
                        PrimaryId,
                        Description,
                        IsConnected: true),
                    new WlanInterfaceIdentity(
                        SecondaryId,
                        Description + " Secondary",
                        IsConnected: true)
                ],
                Message: "합성 WLAN identity 목록");
        }

        public bool IsSupportedPlatform => true;

        public DateTimeOffset UtcNow => _utcNow;

        public int WlanReadCount { get; private set; }

        public int CounterReadCount { get; private set; }

        public List<CounterRequest> CounterRequests { get; } = [];

        public WlanReadResult ReadWlan()
        {
            WlanReadCount++;
            if (_wlanResults.Count == 0)
            {
                throw new InvalidOperationException(
                    "합성 WLAN 결과가 예상보다 많이 요청됐습니다.");
            }

            WlanReadResult result = _wlanResults.Dequeue();
            WlanSnapshot? current = result.FirstConnectedInterface;
            if (current is not null)
            {
                _utcNow = current.Timestamp;
            }
            else
            {
                _utcNow = Start.AddMilliseconds(
                    Math.Max(0, WlanReadCount - 1) * 500L);
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
            if (result.Snapshot is not null)
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
