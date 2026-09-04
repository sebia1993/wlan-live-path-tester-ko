using System.Runtime.CompilerServices;
using WlanLivePathTester.Core.Models;
using WlanLivePathTester.Core.Observation;
using WlanLivePathTester.Windows.Observation;
using WlanLivePathTester.Windows.Wlan;

namespace WlanLivePathTester.WindowsSmoke;

internal static class BrowserObservationRuntimeHarnessTests
{
    private const string PrimaryInterfaceId =
        "A1B2C3D4-E5F6-47A8-9123-1234567890AB";
    private const string SecondaryInterfaceId =
        "B1B2C3D4-E5F6-47A8-9123-1234567890AB";
    private const string InterfaceDescription =
        "Synthetic Wi-Fi 6E Adapter";
    private static readonly DateTimeOffset Start =
        DateTimeOffset.UnixEpoch.AddDays(1);

#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Run()
    {
        CompletesWithPinnedPhysicalWifi();
        CancelsBeforeFirstSampleWithoutWaiting();
        StopsWhenNativeWlanChangesPhysicalNic();
        StopsWhenPinnedAdapterBecomesUnavailable();
        StopsWhenCounterProviderReturnsAnotherNic();
        RejectsInitialWlanAndCounterMismatch();
        ContinuesAcrossBssidRoamingOnSameNic();
        RejectsUnsupportedRuntimeWithoutReadingProviders();
        RejectsMissingWlanConnection();
        Console.WriteLine(
            "PASS injectable browser observation runtime end-to-end tests");
    }

    private static void CompletesWithPinnedPhysicalWifi()
    {
        SyntheticObservationRuntime runtime =
            CreateFullRuntime(roamAtSample: null);
        BrowserObservationResult result = RunObservation(runtime);

        Ensure(result.Status == BrowserObservationStatus.Success,
            $"합성 정상 관찰은 Success여야 합니다: {result.Status}");
        Ensure(result.TerminationReason
               == BrowserObservationTerminationReason.Completed,
            "정상 관찰은 Completed 종료 원인이어야 합니다.");
        Ensure(result.Summary is not null,
            "정상 관찰에는 요약이 필요합니다.");
        Ensure(result.Summary!.Samples.Count == 14,
            "2초 기준 + 5초 관찰을 500ms 간격으로 실행하면 샘플 14개가 필요합니다.");
        Ensure(result.Summary.ActiveSampleCount == 10,
            "활성 관찰 샘플은 10개여야 합니다.");
        Ensure(result.Summary.AverageAdjustedReceiveMbps is > 99 and < 101,
            "합성 활성 처리량은 기준치를 제외하고 약 100 Mbps여야 합니다.");
        Ensure(result.Summary.Confidence == ObservationConfidence.Medium,
            "충분한 정상 샘플은 Medium 신뢰도여야 합니다.");
        Ensure(runtime.DelayCallCount == 14,
            "각 샘플 앞에서 합성 delay가 한 번씩 호출돼야 합니다.");
        Ensure(runtime.WlanReadCount == 15,
            "초기 WLAN과 14개 후속 WLAN 상태를 읽어야 합니다.");
        Ensure(runtime.CounterRequests.Count == 15,
            "초기 카운터와 14개 후속 카운터를 읽어야 합니다.");
        Ensure(runtime.CounterRequests.All(request =>
                request.SelectionMode
                == InterfaceCounterSelectionMode.RequireExactInterfaceId),
            "유효한 WLAN GUID가 있으면 모든 카운터 읽기는 정확 ID 모드여야 합니다.");
        Ensure(runtime.CounterRequests.All(request =>
                Normalize(request.PreferredInterfaceId)
                == Normalize(PrimaryInterfaceId)),
            "모든 카운터 읽기는 시작 시 고정한 같은 인터페이스 ID를 사용해야 합니다.");
        Ensure(runtime.CounterRequests.Skip(1).All(request =>
                request.PreferredInterfaceDescription is null),
            "후속 카운터 읽기는 설명 fallback 없이 고정 ID만 사용해야 합니다.");
    }

    private static void CancelsBeforeFirstSampleWithoutWaiting()
    {
        SyntheticObservationRuntime runtime = new(
            wlanResults:
            [
                ConnectedWlanResult(
                    PrimaryInterfaceId,
                    "AA:BB:CC:DD:EE:01",
                    Start)
            ],
            counterResults:
            [
                SuccessfulCounter(
                    PrimaryInterfaceId,
                    Start,
                    1_000_000,
                    100_000)
            ]);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        BrowserObservationResult result = RunObservation(
            runtime,
            cancellation.Token);

        Ensure(result.Status == BrowserObservationStatus.Canceled,
            "첫 샘플 전 사용자 취소는 Canceled여야 합니다.");
        Ensure(result.TerminationReason
               == BrowserObservationTerminationReason.CanceledByUser,
            "사용자 취소는 CanceledByUser여야 합니다.");
        Ensure(result.Summary is null,
            "샘플 전 취소에는 요약이 없어야 합니다.");
        Ensure(runtime.DelayCallCount == 0,
            "이미 취소된 토큰은 합성 delay 전에 중단해야 합니다.");
        Ensure(runtime.CounterRequests.Count == 1,
            "사용자 취소 전에도 초기 고정 카운터 확인까지만 수행해야 합니다.");
    }

    private static void StopsWhenNativeWlanChangesPhysicalNic()
    {
        SyntheticObservationRuntime runtime = new(
            wlanResults:
            [
                ConnectedWlanResult(
                    PrimaryInterfaceId,
                    "AA:BB:CC:DD:EE:01",
                    Start),
                ConnectedWlanResult(
                    SecondaryInterfaceId,
                    "AA:BB:CC:DD:EE:02",
                    Start.AddMilliseconds(500))
            ],
            counterResults:
            [
                SuccessfulCounter(
                    PrimaryInterfaceId,
                    Start,
                    1_000_000,
                    100_000)
            ]);

        BrowserObservationResult result = RunObservation(runtime);

        Ensure(result.Status == BrowserObservationStatus.AdapterChanged,
            "다른 Native WLAN GUID는 AdapterChanged여야 합니다.");
        Ensure(result.TerminationReason
               == BrowserObservationTerminationReason.AdapterChanged,
            "물리 WLAN 변경 종료 원인이 필요합니다.");
        Ensure(result.Summary is null,
            "첫 후속 샘플 전에 NIC가 바뀌면 요약이 없어야 합니다.");
        Ensure(runtime.CounterRequests.Count == 1,
            "WLAN ID 변경을 확인한 뒤 다른 카운터를 읽으면 안 됩니다.");
    }

    private static void StopsWhenPinnedAdapterBecomesUnavailable()
    {
        SyntheticObservationRuntime runtime = new(
            wlanResults:
            [
                ConnectedWlanResult(
                    PrimaryInterfaceId,
                    "AA:BB:CC:DD:EE:01",
                    Start),
                ConnectedWlanResult(
                    PrimaryInterfaceId,
                    "AA:BB:CC:DD:EE:01",
                    Start.AddMilliseconds(500))
            ],
            counterResults:
            [
                SuccessfulCounter(
                    PrimaryInterfaceId,
                    Start,
                    1_000_000,
                    100_000),
                new InterfaceCounterReadResult(
                    InterfaceCounterReadStatus.InterfaceNotOperational,
                    Snapshot: null,
                    Message: "합성 고정 NIC Down")
            ]);

        BrowserObservationResult result = RunObservation(runtime);

        Ensure(result.Status == BrowserObservationStatus.AdapterUnavailable,
            "고정 NIC Down은 AdapterUnavailable이어야 합니다.");
        Ensure(result.TerminationReason
               == BrowserObservationTerminationReason.AdapterUnavailable,
            "고정 NIC 사용 불가 종료 원인이 필요합니다.");
        Ensure(result.Summary is null,
            "첫 카운터 읽기 실패에는 요약이 없어야 합니다.");
        Ensure(runtime.CounterRequests.Count == 2,
            "초기 카운터와 실패한 고정 카운터만 읽어야 합니다.");
        Ensure(runtime.CounterRequests[1].SelectionMode
               == InterfaceCounterSelectionMode.RequireExactInterfaceId,
            "사용 불가 판정에서도 다른 NIC fallback을 허용하면 안 됩니다.");
    }

    private static void StopsWhenCounterProviderReturnsAnotherNic()
    {
        SyntheticObservationRuntime runtime = new(
            wlanResults:
            [
                ConnectedWlanResult(
                    PrimaryInterfaceId,
                    "AA:BB:CC:DD:EE:01",
                    Start),
                ConnectedWlanResult(
                    PrimaryInterfaceId,
                    "AA:BB:CC:DD:EE:01",
                    Start.AddMilliseconds(500))
            ],
            counterResults:
            [
                SuccessfulCounter(
                    PrimaryInterfaceId,
                    Start,
                    1_000_000,
                    100_000),
                SuccessfulCounter(
                    SecondaryInterfaceId,
                    Start.AddMilliseconds(500),
                    7_000_000,
                    200_000)
            ]);

        BrowserObservationResult result = RunObservation(runtime);

        Ensure(result.Status
               == BrowserObservationStatus.CounterProviderMismatch,
            "고정 요청에 다른 카운터 ID가 반환되면 CounterProviderMismatch여야 합니다.");
        Ensure(result.TerminationReason
               == BrowserObservationTerminationReason.CounterProviderMismatch,
            "카운터 공급자 불일치 종료 원인이 필요합니다.");
        Ensure(result.Summary is null,
            "불일치 카운터 샘플은 요약에 포함하면 안 됩니다.");
    }

    private static void RejectsInitialWlanAndCounterMismatch()
    {
        SyntheticObservationRuntime runtime = new(
            wlanResults:
            [
                ConnectedWlanResult(
                    PrimaryInterfaceId,
                    "AA:BB:CC:DD:EE:01",
                    Start)
            ],
            counterResults:
            [
                SuccessfulCounter(
                    SecondaryInterfaceId,
                    Start,
                    1_000_000,
                    100_000)
            ]);

        BrowserObservationResult result = RunObservation(runtime);

        Ensure(result.Status
               == BrowserObservationStatus.CounterProviderMismatch,
            "초기 WLAN과 카운터 ID가 다르면 관찰을 시작하면 안 됩니다.");
        Ensure(result.TerminationReason
               == BrowserObservationTerminationReason.CounterProviderMismatch,
            "초기 ID 불일치 종료 원인이 필요합니다.");
        Ensure(runtime.DelayCallCount == 0,
            "초기 ID 불일치는 샘플 loop 전에 차단해야 합니다.");
    }

    private static void ContinuesAcrossBssidRoamingOnSameNic()
    {
        SyntheticObservationRuntime runtime =
            CreateFullRuntime(roamAtSample: 7);
        BrowserObservationResult result = RunObservation(runtime);

        Ensure(result.Status == BrowserObservationStatus.Success,
            "같은 NIC의 BSSID 로밍은 관찰을 계속해야 합니다.");
        Ensure(result.TerminationReason
               == BrowserObservationTerminationReason.Completed,
            "같은 NIC 로밍 관찰은 정상 완료여야 합니다.");
        Ensure(result.Summary?.BssidChangeCount == 1,
            "한 번의 BSSID 변경을 기록해야 합니다.");
        Ensure(result.Summary?.AdapterChangeCount == 0,
            "같은 NIC의 BSSID 로밍을 어댑터 변경으로 기록하면 안 됩니다.");
        Ensure(result.Summary?.Samples.Count == 14,
            "로밍 뒤에도 계획한 모든 샘플을 수집해야 합니다.");
    }

    private static void RejectsUnsupportedRuntimeWithoutReadingProviders()
    {
        SyntheticObservationRuntime runtime = new(
            wlanResults: Array.Empty<WlanReadResult>(),
            counterResults: Array.Empty<InterfaceCounterReadResult>(),
            isSupportedPlatform: false);

        BrowserObservationResult result = RunObservation(runtime);

        Ensure(result.Status
               == BrowserObservationStatus.UnsupportedPlatform,
            "미지원 합성 런타임은 UnsupportedPlatform이어야 합니다.");
        Ensure(result.TerminationReason
               == BrowserObservationTerminationReason.UnsupportedPlatform,
            "미지원 플랫폼 종료 원인이 필요합니다.");
        Ensure(runtime.WlanReadCount == 0
               && runtime.IdentityReadCount == 0
               && runtime.CounterRequests.Count == 0,
            "미지원 플랫폼에서는 WLAN·ID·카운터 공급자를 호출하면 안 됩니다.");
    }

    private static void RejectsMissingWlanConnection()
    {
        SyntheticObservationRuntime runtime = new(
            wlanResults:
            [
                new WlanReadResult(
                    WlanReadStatus.NotConnected,
                    Array.Empty<WlanSnapshot>(),
                    nativeErrorCode: null,
                    message: "합성 WLAN 미연결")
            ],
            counterResults: Array.Empty<InterfaceCounterReadResult>());

        BrowserObservationResult result = RunObservation(runtime);

        Ensure(result.Status
               == BrowserObservationStatus.NoWirelessConnection,
            "연결 WLAN이 없으면 NoWirelessConnection이어야 합니다.");
        Ensure(result.TerminationReason
               == BrowserObservationTerminationReason.NoWirelessConnection,
            "WLAN 미연결 종료 원인이 필요합니다.");
        Ensure(runtime.CounterRequests.Count == 0,
            "연결 WLAN이 없으면 카운터 공급자를 호출하면 안 됩니다.");
    }

    private static SyntheticObservationRuntime CreateFullRuntime(
        int? roamAtSample)
    {
        const int totalSamples = 14;
        List<WlanReadResult> wlanResults = [];
        List<InterfaceCounterReadResult> counterResults = [];

        wlanResults.Add(ConnectedWlanResult(
            PrimaryInterfaceId,
            "AA:BB:CC:DD:EE:01",
            Start));
        counterResults.Add(SuccessfulCounter(
            PrimaryInterfaceId,
            Start,
            1_000_000,
            100_000));

        long received = 1_000_000;
        long sent = 100_000;
        string bssid = "AA:BB:CC:DD:EE:01";
        for (int sample = 1; sample <= totalSamples; sample++)
        {
            if (roamAtSample == sample)
            {
                bssid = "AA:BB:CC:DD:EE:02";
            }

            bool isBaseline = sample <= 4;
            received += isBaseline ? 62_500 : 6_312_500;
            sent += isBaseline ? 6_250 : 62_500;
            DateTimeOffset timestamp = Start.AddMilliseconds(
                sample * 500L);
            wlanResults.Add(ConnectedWlanResult(
                PrimaryInterfaceId,
                bssid,
                timestamp));
            counterResults.Add(SuccessfulCounter(
                PrimaryInterfaceId,
                timestamp,
                received,
                sent));
        }

        return new SyntheticObservationRuntime(
            wlanResults,
            counterResults);
    }

    private static BrowserObservationResult RunObservation(
        SyntheticObservationRuntime runtime,
        CancellationToken cancellationToken = default) =>
        new BrowserObservationRunner(runtime)
            .RunAsync(
                new BrowserObservationOptions(
                    BaselineSeconds: 2,
                    ObservationSeconds: 5,
                    SampleIntervalMilliseconds: 500),
                progress: null,
                cancellationToken)
            .GetAwaiter()
            .GetResult();

    private static WlanReadResult ConnectedWlanResult(
        string interfaceId,
        string bssid,
        DateTimeOffset timestamp) =>
        new(
            WlanReadStatus.Success,
            [
                new WlanSnapshot(
                    Timestamp: timestamp,
                    IsConnected: true,
                    Ssid: "Synthetic-SSID",
                    Bssid: bssid,
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
                    InterfaceId: interfaceId)
            ],
            nativeErrorCode: null,
            message: "합성 WLAN 연결");

    private static InterfaceCounterReadResult SuccessfulCounter(
        string interfaceId,
        DateTimeOffset timestamp,
        long received,
        long sent) =>
        new(
            InterfaceCounterReadStatus.Success,
            new InterfaceCounterSnapshot(
                Timestamp: timestamp,
                InterfaceId: interfaceId,
                InterfaceName: "Synthetic Wi-Fi",
                InterfaceDescription: InterfaceDescription,
                BytesReceived: received,
                BytesSent: sent,
                IsOperational: true),
            "합성 카운터 성공");

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
        string? PreferredInterfaceDescription,
        InterfaceCounterSelectionMode SelectionMode);

    private sealed class SyntheticObservationRuntime
        : IBrowserObservationRuntime
    {
        private readonly Queue<WlanReadResult> _wlanResults;
        private readonly Queue<InterfaceCounterReadResult> _counterResults;
        private readonly WlanInterfaceIdentityReadResult _identityResult;
        private DateTimeOffset _utcNow;

        public SyntheticObservationRuntime(
            IEnumerable<WlanReadResult> wlanResults,
            IEnumerable<InterfaceCounterReadResult> counterResults,
            bool isSupportedPlatform = true)
        {
            _wlanResults = new Queue<WlanReadResult>(wlanResults);
            _counterResults = new Queue<InterfaceCounterReadResult>(
                counterResults);
            IsSupportedPlatform = isSupportedPlatform;
            _utcNow = Start;
            _identityResult = new WlanInterfaceIdentityReadResult(
                IsSuccess: true,
                Interfaces:
                [
                    new WlanInterfaceIdentity(
                        PrimaryInterfaceId,
                        InterfaceDescription,
                        IsConnected: true),
                    new WlanInterfaceIdentity(
                        SecondaryInterfaceId,
                        InterfaceDescription + " Secondary",
                        IsConnected: true)
                ],
                Message: "합성 WLAN ID 목록");
        }

        public bool IsSupportedPlatform { get; }

        public DateTimeOffset UtcNow => _utcNow;

        public int WlanReadCount { get; private set; }

        public int IdentityReadCount { get; private set; }

        public int DelayCallCount { get; private set; }

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
            WlanSnapshot? connected = result.FirstConnectedInterface;
            if (connected is not null
                && connected.Timestamp > _utcNow)
            {
                _utcNow = connected.Timestamp;
            }

            return result;
        }

        public WlanInterfaceIdentityReadResult ReadWlanIdentity()
        {
            IdentityReadCount++;
            return _identityResult;
        }

        public InterfaceCounterReadResult ReadCounter(
            string? preferredInterfaceId,
            string? preferredInterfaceDescription,
            InterfaceCounterSelectionMode selectionMode)
        {
            CounterRequests.Add(new CounterRequest(
                preferredInterfaceId,
                preferredInterfaceDescription,
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
            DelayCallCount++;
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
    }
}
