using WlanLivePathTester.Core.Models;

namespace WlanLivePathTester.Core.Observation;

public static class BrowserObservationCalculator
{
    private const double PauseThresholdMbps = 0.5;
    private const double ActiveTrafficThresholdMbps = 5.0;
    private const double SuddenDropRatio = 0.25;
    private const double BusyBaselineThresholdMbps = 5.0;

    public static BrowserObservationSample CreateSample(
        InterfaceCounterSnapshot previousCounter,
        InterfaceCounterSnapshot currentCounter,
        WlanSnapshot? previousWlan,
        WlanSnapshot? currentWlan,
        bool isBaseline,
        double baselineReceiveMbps,
        double? previousAdjustedReceiveMbps = null)
    {
        ArgumentNullException.ThrowIfNull(previousCounter);
        ArgumentNullException.ThrowIfNull(currentCounter);

        TimeSpan interval = currentCounter.Timestamp - previousCounter.Timestamp;
        bool invalidInterval = interval <= TimeSpan.Zero;
        bool adapterChanged = !string.Equals(
            NormalizeInterfaceId(previousCounter.InterfaceId),
            NormalizeInterfaceId(currentCounter.InterfaceId),
            StringComparison.OrdinalIgnoreCase);
        bool counterReset = !adapterChanged
            && (currentCounter.BytesReceived < previousCounter.BytesReceived
                || currentCounter.BytesSent < previousCounter.BytesSent);
        bool wlanDisconnected = currentWlan is null || !currentWlan.IsConnected;
        bool previousWlanUnavailable =
            previousWlan is null || !previousWlan.IsConnected;

        long receiveDelta = 0;
        long transmitDelta = 0;
        double? rawReceiveMbps = null;
        double? rawTransmitMbps = null;
        double? adjustedReceiveMbps = null;

        if (!invalidInterval && !adapterChanged && !counterReset)
        {
            receiveDelta = currentCounter.BytesReceived - previousCounter.BytesReceived;
            transmitDelta = currentCounter.BytesSent - previousCounter.BytesSent;
            rawReceiveMbps = ToMbps(receiveDelta, interval);
            rawTransmitMbps = ToMbps(transmitDelta, interval);
            adjustedReceiveMbps = isBaseline
                ? rawReceiveMbps
                : Math.Max(0, rawReceiveMbps.Value - Math.Max(0, baselineReceiveMbps));
        }

        bool bssidChanged = HasBssidChanged(previousWlan, currentWlan);
        bool pauseDetected = !isBaseline
            && !wlanDisconnected
            && !previousWlanUnavailable
            && adjustedReceiveMbps.HasValue
            && previousAdjustedReceiveMbps is >= ActiveTrafficThresholdMbps
            && adjustedReceiveMbps.Value < PauseThresholdMbps;
        bool suddenDropDetected = !isBaseline
            && !wlanDisconnected
            && !previousWlanUnavailable
            && adjustedReceiveMbps.HasValue
            && previousAdjustedReceiveMbps is >= ActiveTrafficThresholdMbps
            && adjustedReceiveMbps.Value <= previousAdjustedReceiveMbps.Value * SuddenDropRatio;

        List<string> notes = [];
        if (invalidInterval)
        {
            notes.Add("샘플 시각 간격이 올바르지 않아 처리량을 계산하지 않았습니다.");
        }

        if (adapterChanged)
        {
            notes.Add("관찰 중 Wi-Fi 인터페이스가 변경되어 해당 구간의 카운터 차이를 계산하지 않았습니다.");
        }

        if (counterReset)
        {
            notes.Add("인터페이스 누적 카운터가 감소해 롤오버·재설정 구간으로 처리했습니다.");
        }

        if (wlanDisconnected)
        {
            notes.Add("현재 WLAN 연결 정보를 확인하지 못했습니다.");
        }

        if (bssidChanged)
        {
            notes.Add("관찰 중 BSSID가 변경되었습니다.");
        }

        if (pauseDetected)
        {
            notes.Add("활성 다운로드 이후 수신 처리량이 일시 정지 수준으로 낮아졌습니다.");
        }
        else if (suddenDropDetected)
        {
            notes.Add("직전 활성 구간 대비 수신 처리량이 급격히 감소했습니다.");
        }

        return new BrowserObservationSample(
            Timestamp: currentCounter.Timestamp,
            Interval: interval,
            IsBaseline: isBaseline,
            InterfaceId: currentCounter.InterfaceId,
            ReceiveBytesDelta: receiveDelta,
            TransmitBytesDelta: transmitDelta,
            RawReceiveMbps: rawReceiveMbps,
            RawTransmitMbps: rawTransmitMbps,
            AdjustedReceiveMbps: adjustedReceiveMbps,
            RssiDbm: currentWlan?.RssiDbm,
            Bssid: currentWlan?.Bssid,
            ReceiveLinkSpeedBps: currentWlan?.ReceiveLinkSpeedBps,
            TransmitLinkSpeedBps: currentWlan?.TransmitLinkSpeedBps,
            InvalidInterval: invalidInterval,
            AdapterChanged: adapterChanged,
            CounterReset: counterReset,
            WlanDisconnected: wlanDisconnected,
            BssidChanged: bssidChanged,
            PauseDetected: pauseDetected,
            SuddenDropDetected: suddenDropDetected,
            Note: notes.Count == 0 ? null : string.Join(" ", notes));
    }

    public static double CalculateBaselineReceiveMbps(
        IEnumerable<BrowserObservationSample> samples)
    {
        ArgumentNullException.ThrowIfNull(samples);

        BrowserObservationSample[] baseline = samples
            .Where(sample => sample.IsBaseline
                && !sample.WlanDisconnected
                && sample.RawReceiveMbps.HasValue)
            .ToArray();

        return baseline.Length == 0
            ? 0
            : WeightedAverage(
                baseline,
                sample => sample.RawReceiveMbps!.Value);
    }

    public static BrowserObservationSummary Summarize(
        DateTimeOffset startedAt,
        DateTimeOffset completedAt,
        IReadOnlyList<BrowserObservationSample> samples)
    {
        ArgumentNullException.ThrowIfNull(samples);

        BrowserObservationSample[] baselineSamples = samples
            .Where(sample => sample.IsBaseline
                && !sample.WlanDisconnected
                && sample.RawReceiveMbps.HasValue)
            .ToArray();
        BrowserObservationSample[] activeSamples = samples
            .Where(sample => !sample.IsBaseline
                && !sample.WlanDisconnected
                && sample.AdjustedReceiveMbps.HasValue)
            .ToArray();

        double baselineReceiveMbps = baselineSamples.Length == 0
            ? 0
            : WeightedAverage(baselineSamples, sample => sample.RawReceiveMbps!.Value);
        double? averageAdjustedReceiveMbps = activeSamples.Length == 0
            ? null
            : WeightedAverage(activeSamples, sample => sample.AdjustedReceiveMbps!.Value);
        double? peakAdjustedReceiveMbps = activeSamples.Length == 0
            ? null
            : activeSamples.Max(sample => sample.AdjustedReceiveMbps!.Value);
        TimeSpan observedDuration = TimeSpan.FromSeconds(
            activeSamples.Sum(sample => Math.Max(0, sample.Interval.TotalSeconds)));

        long totalReceiveBytes = 0;
        foreach (BrowserObservationSample sample in activeSamples)
        {
            totalReceiveBytes = checked(totalReceiveBytes + sample.ReceiveBytesDelta);
        }

        int pauseCount = samples.Count(sample => sample.PauseDetected);
        int suddenDropCount = samples.Count(sample => sample.SuddenDropDetected);
        int bssidChangeCount = samples.Count(sample => sample.BssidChanged);
        int adapterChangeCount = samples.Count(sample => sample.AdapterChanged);
        int counterResetCount = samples.Count(sample => sample.CounterReset);
        int wlanDisconnectedCount = samples.Count(sample => sample.WlanDisconnected);

        ObservationConfidence confidence = activeSamples.Length < 3
            || baselineReceiveMbps >= BusyBaselineThresholdMbps
            || adapterChangeCount > 0
            || counterResetCount > 0
            || wlanDisconnectedCount > 0
            ? ObservationConfidence.Low
            : ObservationConfidence.Medium;

        string message = averageAdjustedReceiveMbps is double average
            ? $"브라우저 관찰 평균 수신 처리량은 백그라운드 기준치를 제외하고 {average:F1} Mbps입니다."
            : "활성 관찰 구간에서 계산 가능한 수신 처리량 샘플이 없습니다.";

        return new BrowserObservationSummary(
            StartedAt: startedAt,
            CompletedAt: completedAt,
            ObservedDuration: observedDuration,
            BaselineReceiveMbps: baselineReceiveMbps,
            AverageAdjustedReceiveMbps: averageAdjustedReceiveMbps,
            PeakAdjustedReceiveMbps: peakAdjustedReceiveMbps,
            TotalReceiveBytes: totalReceiveBytes,
            ActiveSampleCount: activeSamples.Length,
            PauseCount: pauseCount,
            SuddenDropCount: suddenDropCount,
            BssidChangeCount: bssidChangeCount,
            AdapterChangeCount: adapterChangeCount,
            CounterResetCount: counterResetCount,
            WlanDisconnectedSampleCount: wlanDisconnectedCount,
            Confidence: confidence,
            Samples: samples.ToArray(),
            Message: message,
            Limitation: "이 결과는 Wi-Fi 인터페이스 전체 트래픽입니다. 브라우저 외 다른 프로그램의 송수신이 섞일 수 있으며 프로세스별 다운로드 속도로 단정할 수 없습니다.");
    }

    private static double WeightedAverage(
        IEnumerable<BrowserObservationSample> samples,
        Func<BrowserObservationSample, double> selector)
    {
        double weightedTotal = 0;
        double totalSeconds = 0;

        foreach (BrowserObservationSample sample in samples)
        {
            double seconds = Math.Max(0, sample.Interval.TotalSeconds);
            if (seconds <= 0)
            {
                continue;
            }

            weightedTotal += selector(sample) * seconds;
            totalSeconds += seconds;
        }

        return totalSeconds <= 0 ? 0 : weightedTotal / totalSeconds;
    }

    private static double ToMbps(long bytes, TimeSpan interval) =>
        bytes * 8d / interval.TotalSeconds / 1_000_000d;

    private static bool HasBssidChanged(WlanSnapshot? previous, WlanSnapshot? current) =>
        previous?.IsConnected == true
        && current?.IsConnected == true
        && !string.IsNullOrWhiteSpace(previous.Bssid)
        && !string.IsNullOrWhiteSpace(current.Bssid)
        && !string.Equals(previous.Bssid, current.Bssid, StringComparison.OrdinalIgnoreCase);

    private static string NormalizeInterfaceId(string value) =>
        value.Trim().Trim('{', '}');
}
