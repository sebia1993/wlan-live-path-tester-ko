using WlanLivePathTester.Core.Models;
using WlanLivePathTester.Core.Observation;
using WlanLivePathTester.Windows.Observation;

namespace WlanLivePathTester.ObservationSmoke;

internal static class Program
{
    private static int Main()
    {
        (string Name, Action Test)[] tests =
        [
            ("누적 바이트를 Mbps로 변환", ConvertsCounterDeltaToMbps),
            ("카운터 감소를 롤오버·재설정으로 처리", DetectsCounterReset),
            ("인터페이스 변경 구간 제외", DetectsAdapterChange),
            ("BSSID 변경 기록", DetectsBssidChange),
            ("활성 다운로드 정지와 급락 감지", DetectsPauseAndSuddenDrop),
            ("백그라운드 트래픽이 높으면 낮은 신뢰도", LowersConfidenceForBusyBaseline),
            ("관찰 옵션 범위 검증", ValidatesObservationOptions),
            ("Windows 인터페이스 카운터 조회 경계", ExercisesWindowsCounterReader)
        ];

        int failures = 0;
        foreach ((string name, Action test) in tests)
        {
            try
            {
                test();
                Console.WriteLine($"PASS  {name}");
            }
            catch (Exception exception)
            {
                failures++;
                Console.Error.WriteLine($"FAIL  {name}: {exception.Message}");
            }
        }

        Console.WriteLine($"관찰 smoke 총 {tests.Length}개, 실패 {failures}개");
        return failures == 0 ? 0 : 1;
    }

    private static void ConvertsCounterDeltaToMbps()
    {
        InterfaceCounterSnapshot previous = Counter(0, "wifi-a", 1_000, 2_000);
        InterfaceCounterSnapshot current = Counter(1, "wifi-a", 100_001_000, 2_000);

        BrowserObservationSample sample = BrowserObservationCalculator.CreateSample(
            previous,
            current,
            ConnectedWlan("AA:AA:AA:AA:AA:AA"),
            ConnectedWlan("AA:AA:AA:AA:AA:AA"),
            isBaseline: false,
            baselineReceiveMbps: 0);

        Assert(sample.RawReceiveMbps is >= 799.99 and <= 800.01,
            $"100,000,000바이트/초는 약 800 Mbps여야 합니다: {sample.RawReceiveMbps}");
        Assert(sample.ReceiveBytesDelta == 100_000_000,
            "수신 바이트 차이는 100,000,000이어야 합니다.");
    }

    private static void DetectsCounterReset()
    {
        BrowserObservationSample sample = BrowserObservationCalculator.CreateSample(
            Counter(0, "wifi-a", 5_000, 5_000),
            Counter(1, "wifi-a", 100, 100),
            ConnectedWlan("AA:AA:AA:AA:AA:AA"),
            ConnectedWlan("AA:AA:AA:AA:AA:AA"),
            isBaseline: false,
            baselineReceiveMbps: 0);

        Assert(sample.CounterReset, "감소한 누적 카운터를 재설정 구간으로 표시해야 합니다.");
        Assert(sample.RawReceiveMbps is null,
            "카운터 재설정 구간에서는 처리량을 계산하면 안 됩니다.");
    }

    private static void DetectsAdapterChange()
    {
        BrowserObservationSample sample = BrowserObservationCalculator.CreateSample(
            Counter(0, "wifi-a", 1_000, 1_000),
            Counter(1, "wifi-b", 50_000, 10_000),
            ConnectedWlan("AA:AA:AA:AA:AA:AA"),
            ConnectedWlan("AA:AA:AA:AA:AA:AA"),
            isBaseline: false,
            baselineReceiveMbps: 0);

        Assert(sample.AdapterChanged, "인터페이스 ID 변경을 기록해야 합니다.");
        Assert(sample.ReceiveBytesDelta == 0 && sample.RawReceiveMbps is null,
            "서로 다른 인터페이스의 누적 카운터를 빼면 안 됩니다.");
    }

    private static void DetectsBssidChange()
    {
        BrowserObservationSample sample = BrowserObservationCalculator.CreateSample(
            Counter(0, "wifi-a", 0, 0),
            Counter(1, "wifi-a", 1_000_000, 0),
            ConnectedWlan("AA:AA:AA:AA:AA:AA"),
            ConnectedWlan("BB:BB:BB:BB:BB:BB"),
            isBaseline: false,
            baselineReceiveMbps: 0);

        Assert(sample.BssidChanged, "연결된 BSSID 변경을 기록해야 합니다.");
    }

    private static void DetectsPauseAndSuddenDrop()
    {
        BrowserObservationSample sample = BrowserObservationCalculator.CreateSample(
            Counter(0, "wifi-a", 0, 0),
            Counter(1, "wifi-a", 10_000, 0),
            ConnectedWlan("AA:AA:AA:AA:AA:AA"),
            ConnectedWlan("AA:AA:AA:AA:AA:AA"),
            isBaseline: false,
            baselineReceiveMbps: 0,
            previousAdjustedReceiveMbps: 100);

        Assert(sample.AdjustedReceiveMbps is < 0.5,
            "합성 샘플은 일시 정지 기준보다 낮아야 합니다.");
        Assert(sample.PauseDetected, "활성 구간 이후 일시 정지를 감지해야 합니다.");
        Assert(sample.SuddenDropDetected, "직전 구간 대비 급락을 감지해야 합니다.");
    }

    private static void LowersConfidenceForBusyBaseline()
    {
        WlanSnapshot wlan = ConnectedWlan("AA:AA:AA:AA:AA:AA");
        List<BrowserObservationSample> samples = [];

        InterfaceCounterSnapshot counter0 = Counter(0, "wifi-a", 0, 0);
        InterfaceCounterSnapshot counter1 = Counter(1, "wifi-a", 750_000, 0);
        samples.Add(BrowserObservationCalculator.CreateSample(
            counter0,
            counter1,
            wlan,
            wlan,
            isBaseline: true,
            baselineReceiveMbps: 0));

        InterfaceCounterSnapshot counter2 = Counter(2, "wifi-a", 14_000_000, 0);
        samples.Add(BrowserObservationCalculator.CreateSample(
            counter1,
            counter2,
            wlan,
            wlan,
            isBaseline: false,
            baselineReceiveMbps: 6));

        InterfaceCounterSnapshot counter3 = Counter(3, "wifi-a", 27_250_000, 0);
        samples.Add(BrowserObservationCalculator.CreateSample(
            counter2,
            counter3,
            wlan,
            wlan,
            isBaseline: false,
            baselineReceiveMbps: 6,
            previousAdjustedReceiveMbps: samples[^1].AdjustedReceiveMbps));

        BrowserObservationSummary summary = BrowserObservationCalculator.Summarize(
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch.AddSeconds(3),
            samples);

        Assert(summary.BaselineReceiveMbps is >= 5.99 and <= 6.01,
            $"백그라운드 기준은 약 6 Mbps여야 합니다: {summary.BaselineReceiveMbps}");
        Assert(summary.Confidence == ObservationConfidence.Low,
            "백그라운드 트래픽이 높으면 신뢰도를 낮춰야 합니다.");
        Assert(summary.AverageAdjustedReceiveMbps is > 90,
            "기준치를 제외한 활성 처리량을 계산해야 합니다.");
    }

    private static void ValidatesObservationOptions()
    {
        BrowserObservationOptions invalid = new(
            BaselineSeconds: 1,
            ObservationSeconds: 4,
            SampleIntervalMilliseconds: 100);
        Assert(invalid.Validate().Count == 3,
            "세 가지 범위 오류를 모두 반환해야 합니다.");

        BrowserObservationOptions valid = new();
        Assert(valid.Validate().Count == 0,
            "기본 관찰 옵션은 유효해야 합니다.");
    }

    private static void ExercisesWindowsCounterReader()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new InvalidOperationException("Windows runner에서 실행해야 합니다.");
        }

        InterfaceCounterReadResult result = WindowsInterfaceCounterReader.ReadCurrent();
        Assert(result.Status != InterfaceCounterReadStatus.UnsupportedPlatform,
            "Windows에서 지원하지 않는 플랫폼으로 판정하면 안 됩니다.");
        Assert(!string.IsNullOrWhiteSpace(result.Message),
            "인터페이스 카운터 조회 결과에는 설명 문구가 필요합니다.");

        if (result.IsSuccess)
        {
            Assert(result.Snapshot!.BytesReceived >= 0 && result.Snapshot.BytesSent >= 0,
                "누적 바이트는 음수가 아니어야 합니다.");
        }
    }

    private static InterfaceCounterSnapshot Counter(
        int seconds,
        string interfaceId,
        long received,
        long sent) =>
        new(
            Timestamp: DateTimeOffset.UnixEpoch.AddSeconds(seconds),
            InterfaceId: interfaceId,
            InterfaceName: "Synthetic Wi-Fi",
            InterfaceDescription: "Synthetic wireless adapter",
            BytesReceived: received,
            BytesSent: sent,
            IsOperational: true);

    private static WlanSnapshot ConnectedWlan(string bssid) =>
        new(
            Timestamp: DateTimeOffset.UnixEpoch,
            IsConnected: true,
            Ssid: "SYNTHETIC-SSID",
            Bssid: bssid,
            RssiDbm: -55,
            Channel: 36,
            PhyType: "802.11ax",
            ReceiveLinkSpeedBps: 1_200_000_000,
            TransmitLinkSpeedBps: 1_200_000_000,
            InterfaceDescription: "Synthetic wireless adapter",
            InterfaceId: "wifi-a");

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
