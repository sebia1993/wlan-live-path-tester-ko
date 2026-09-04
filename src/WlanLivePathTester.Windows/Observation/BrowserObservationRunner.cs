using System.Runtime.Versioning;
using WlanLivePathTester.Core.Models;
using WlanLivePathTester.Core.Observation;
using WlanLivePathTester.Windows.Wlan;

namespace WlanLivePathTester.Windows.Observation;

[SupportedOSPlatform("windows")]
public sealed class BrowserObservationRunner
{
    public Task<BrowserObservationResult> RunAsync(
        BrowserObservationOptions options,
        IProgress<BrowserObservationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        return Task.Run(
            () => RunCoreAsync(options, progress, cancellationToken),
            CancellationToken.None);
    }

    private static async Task<BrowserObservationResult> RunCoreAsync(
        BrowserObservationOptions options,
        IProgress<BrowserObservationProgress>? progress,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<string> validationErrors = options.Validate();
        if (validationErrors.Count > 0)
        {
            return new BrowserObservationResult(
                BrowserObservationStatus.InvalidOptions,
                null,
                null,
                string.Join(" ", validationErrors));
        }

        if (!OperatingSystem.IsWindows())
        {
            return new BrowserObservationResult(
                BrowserObservationStatus.UnsupportedPlatform,
                null,
                null,
                "Windows에서만 브라우저 다운로드 관찰을 실행할 수 있습니다.");
        }

        progress?.Report(new BrowserObservationProgress(
            BrowserObservationPhase.Preparing,
            TimeSpan.Zero,
            TimeSpan.FromSeconds(options.BaselineSeconds + options.ObservationSeconds),
            null,
            "현재 WLAN 연결과 Wi-Fi 인터페이스 카운터를 확인하고 있습니다."));

        WlanReadResult initialWlanRead = NativeWlanReader.ReadCurrent();
        WlanSnapshot? initialWlan = initialWlanRead.FirstConnectedInterface;
        if (initialWlan is null)
        {
            return new BrowserObservationResult(
                BrowserObservationStatus.NoWirelessConnection,
                null,
                null,
                $"연결된 WLAN 인터페이스가 없어 관찰을 시작하지 않았습니다. {initialWlanRead.Message}");
        }

        string? preferredInterfaceId = initialWlan.InterfaceId;
        string? preferredInterfaceDescription = initialWlan.InterfaceDescription;
        InterfaceCounterReadResult initialCounterRead = WindowsInterfaceCounterReader.ReadCurrent(
            preferredInterfaceId,
            preferredInterfaceDescription);

        if (!initialCounterRead.IsSuccess)
        {
            return new BrowserObservationResult(
                BrowserObservationStatus.InterfaceUnavailable,
                null,
                initialWlan,
                initialCounterRead.Message);
        }

        InterfaceCounterSnapshot previousCounter = initialCounterRead.Snapshot!;
        WlanSnapshot? previousWlan = initialWlan;
        DateTimeOffset startedAt = previousCounter.Timestamp;
        List<BrowserObservationSample> samples = [];
        double baselineReceiveMbps = 0;
        double? previousAdjustedReceiveMbps = null;

        int baselineSampleCount = CalculateSampleCount(
            options.BaselineSeconds,
            options.SampleIntervalMilliseconds);
        int activeSampleCount = CalculateSampleCount(
            options.ObservationSeconds,
            options.SampleIntervalMilliseconds);
        int totalSampleCount = checked(baselineSampleCount + activeSampleCount);

        try
        {
            for (int index = 0; index < totalSampleCount; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Delay(
                    options.SampleIntervalMilliseconds,
                    cancellationToken).ConfigureAwait(false);

                WlanReadResult currentWlanRead = NativeWlanReader.ReadCurrent();
                WlanSnapshot? currentWlan = currentWlanRead.FirstConnectedInterface;
                if (currentWlan is not null)
                {
                    preferredInterfaceId = currentWlan.InterfaceId ?? preferredInterfaceId;
                    preferredInterfaceDescription = currentWlan.InterfaceDescription
                        ?? preferredInterfaceDescription;
                }

                InterfaceCounterReadResult currentCounterRead =
                    WindowsInterfaceCounterReader.ReadCurrent(
                        preferredInterfaceId,
                        preferredInterfaceDescription);

                if (!currentCounterRead.IsSuccess)
                {
                    if (samples.Count == 0)
                    {
                        return new BrowserObservationResult(
                            BrowserObservationStatus.InterfaceUnavailable,
                            null,
                            initialWlan,
                            currentCounterRead.Message);
                    }

                    BrowserObservationSummary partialSummary =
                        BrowserObservationCalculator.Summarize(
                            startedAt,
                            DateTimeOffset.UtcNow,
                            samples);
                    progress?.Report(new BrowserObservationProgress(
                        BrowserObservationPhase.Failed,
                        DateTimeOffset.UtcNow - startedAt,
                        TimeSpan.Zero,
                        samples[^1],
                        currentCounterRead.Message));
                    return new BrowserObservationResult(
                        BrowserObservationStatus.PartialSuccess,
                        partialSummary,
                        initialWlan,
                        $"일부 샘플을 수집한 뒤 인터페이스 카운터를 읽지 못했습니다. {currentCounterRead.Message}");
                }

                InterfaceCounterSnapshot currentCounter = currentCounterRead.Snapshot!;
                bool isBaseline = index < baselineSampleCount;
                BrowserObservationSample sample = BrowserObservationCalculator.CreateSample(
                    previousCounter,
                    currentCounter,
                    previousWlan,
                    currentWlan,
                    isBaseline,
                    baselineReceiveMbps,
                    previousAdjustedReceiveMbps);
                samples.Add(sample);

                if (isBaseline)
                {
                    baselineReceiveMbps =
                        BrowserObservationCalculator.CalculateBaselineReceiveMbps(samples);
                }
                else if (sample.AdjustedReceiveMbps.HasValue)
                {
                    previousAdjustedReceiveMbps = sample.AdjustedReceiveMbps.Value;
                }

                TimeSpan elapsed = currentCounter.Timestamp - startedAt;
                int remainingSamples = totalSampleCount - index - 1;
                TimeSpan remaining = TimeSpan.FromMilliseconds(
                    remainingSamples * (long)options.SampleIntervalMilliseconds);
                BrowserObservationPhase phase = isBaseline
                    ? BrowserObservationPhase.Baseline
                    : BrowserObservationPhase.Observing;
                string message = isBaseline
                    ? $"백그라운드 트래픽 기준 수집 중 {index + 1}/{baselineSampleCount}"
                    : $"브라우저 다운로드 관찰 중 {index - baselineSampleCount + 1}/{activeSampleCount}";

                progress?.Report(new BrowserObservationProgress(
                    phase,
                    elapsed,
                    remaining,
                    sample,
                    message));

                previousCounter = currentCounter;
                previousWlan = currentWlan;
            }
        }
        catch (OperationCanceledException)
        {
            BrowserObservationSummary? canceledSummary = samples.Count == 0
                ? null
                : BrowserObservationCalculator.Summarize(
                    startedAt,
                    DateTimeOffset.UtcNow,
                    samples);
            progress?.Report(new BrowserObservationProgress(
                BrowserObservationPhase.Canceled,
                DateTimeOffset.UtcNow - startedAt,
                TimeSpan.Zero,
                samples.Count == 0 ? null : samples[^1],
                "사용자 요청으로 브라우저 관찰을 중단했습니다."));
            return new BrowserObservationResult(
                BrowserObservationStatus.Canceled,
                canceledSummary,
                initialWlan,
                "사용자 요청으로 브라우저 관찰을 중단했습니다. 수집된 샘플만 로컬 결과에 유지합니다.");
        }
        catch (Exception exception)
        {
            BrowserObservationSummary? failedSummary = samples.Count == 0
                ? null
                : BrowserObservationCalculator.Summarize(
                    startedAt,
                    DateTimeOffset.UtcNow,
                    samples);
            progress?.Report(new BrowserObservationProgress(
                BrowserObservationPhase.Failed,
                DateTimeOffset.UtcNow - startedAt,
                TimeSpan.Zero,
                samples.Count == 0 ? null : samples[^1],
                $"관찰 중 오류가 발생했습니다: {exception.Message}"));
            return new BrowserObservationResult(
                failedSummary is null
                    ? BrowserObservationStatus.Failed
                    : BrowserObservationStatus.PartialSuccess,
                failedSummary,
                initialWlan,
                $"브라우저 관찰 중 오류가 발생했습니다: {exception.Message}");
        }

        DateTimeOffset completedAt = DateTimeOffset.UtcNow;
        BrowserObservationSummary summary = BrowserObservationCalculator.Summarize(
            startedAt,
            completedAt,
            samples);
        progress?.Report(new BrowserObservationProgress(
            BrowserObservationPhase.Completed,
            completedAt - startedAt,
            TimeSpan.Zero,
            samples.Count == 0 ? null : samples[^1],
            "브라우저 다운로드 관찰을 완료했습니다."));

        BrowserObservationStatus status = summary.ActiveSampleCount > 0
            ? BrowserObservationStatus.Success
            : BrowserObservationStatus.PartialSuccess;
        return new BrowserObservationResult(
            status,
            summary,
            initialWlan,
            summary.Message);
    }

    private static int CalculateSampleCount(
        int seconds,
        int sampleIntervalMilliseconds)
    {
        long durationMilliseconds = checked(seconds * 1000L);
        return checked((int)Math.Ceiling(
            durationMilliseconds / (double)sampleIntervalMilliseconds));
    }
}
