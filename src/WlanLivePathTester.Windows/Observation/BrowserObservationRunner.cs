using System.Runtime.Versioning;
using WlanLivePathTester.Core.Models;
using WlanLivePathTester.Core.Observation;
using WlanLivePathTester.Windows.Wlan;

namespace WlanLivePathTester.Windows.Observation;

[SupportedOSPlatform("windows")]
public sealed class BrowserObservationRunner
{
    private readonly IBrowserObservationRuntime _runtime;

    public BrowserObservationRunner()
        : this(WindowsBrowserObservationRuntime.Instance)
    {
    }

    public BrowserObservationRunner(
        IBrowserObservationRuntime runtime)
    {
        _runtime = runtime
            ?? throw new ArgumentNullException(nameof(runtime));
    }

    public Task<BrowserObservationResult> RunAsync(
        BrowserObservationOptions options,
        IProgress<BrowserObservationProgress>? progress = null,
        CancellationToken cancellationToken = default,
        BrowserObservationCancellationContext? cancellationContext = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!_runtime.RequiresWorkerThread)
        {
            return RunCoreAsync(
                options,
                progress,
                cancellationToken,
                cancellationContext);
        }

        return Task.Run(
            () => RunCoreAsync(
                options,
                progress,
                cancellationToken,
                cancellationContext),
            CancellationToken.None);
    }

    private async Task<BrowserObservationResult> RunCoreAsync(
        BrowserObservationOptions options,
        IProgress<BrowserObservationProgress>? progress,
        CancellationToken cancellationToken,
        BrowserObservationCancellationContext? cancellationContext)
    {
        IReadOnlyList<string> validationErrors = options.Validate();
        if (validationErrors.Count > 0)
        {
            return new BrowserObservationResult(
                BrowserObservationStatus.InvalidOptions,
                null,
                null,
                string.Join(" ", validationErrors),
                BrowserObservationTerminationReason.InvalidOptions);
        }

        if (!_runtime.IsSupportedPlatform)
        {
            return new BrowserObservationResult(
                BrowserObservationStatus.UnsupportedPlatform,
                null,
                null,
                "Windows에서만 브라우저 다운로드 관찰을 실행할 수 있습니다.",
                BrowserObservationTerminationReason.UnsupportedPlatform);
        }

        progress?.Report(new BrowserObservationProgress(
            BrowserObservationPhase.Preparing,
            TimeSpan.Zero,
            TimeSpan.FromSeconds(
                options.BaselineSeconds + options.ObservationSeconds),
            null,
            "현재 WLAN 연결과 정확히 대응되는 물리 Wi-Fi 카운터를 확인하고 있습니다."));

        WlanReadResult initialWlanRead = _runtime.ReadWlan();
        WlanInterfaceIdentityReadResult identityRead =
            _runtime.ReadWlanIdentity();
        WlanSnapshot? initialWlan =
            WlanInterfaceIdentityReader.AttachIdentity(
                initialWlanRead.FirstConnectedInterface,
                identityRead);
        if (initialWlan is null)
        {
            return new BrowserObservationResult(
                BrowserObservationStatus.NoWirelessConnection,
                null,
                null,
                $"연결된 WLAN 인터페이스가 없어 관찰을 시작하지 않았습니다. {initialWlanRead.Message}",
                BrowserObservationTerminationReason.NoWirelessConnection);
        }

        InterfaceCounterSelectionMode initialSelectionMode =
            string.IsNullOrWhiteSpace(initialWlan.InterfaceId)
                ? InterfaceCounterSelectionMode
                    .PreferExactIdentityThenDescription
                : InterfaceCounterSelectionMode.RequireExactInterfaceId;
        InterfaceCounterReadResult initialCounterRead =
            _runtime.ReadCounter(
                initialWlan.InterfaceId,
                initialWlan.InterfaceDescription,
                initialSelectionMode);

        if (!initialCounterRead.IsSuccess)
        {
            string identityContext = identityRead.IsSuccess
                ? string.Empty
                : $" WLAN 인터페이스 ID 조회도 제한됐습니다: {identityRead.Message}";
            BrowserObservationStatus initialFailureStatus =
                initialCounterRead.Status
                    == InterfaceCounterReadStatus.CounterProviderMismatch
                    ? BrowserObservationStatus.CounterProviderMismatch
                    : BrowserObservationStatus.InterfaceUnavailable;
            return new BrowserObservationResult(
                initialFailureStatus,
                null,
                initialWlan,
                initialCounterRead.Message
                + identityContext
                + " 다른 활성 Wi-Fi 인터페이스를 임의로 선택하지 않았습니다.",
                BrowserObservationTerminationPolicy.FromStatus(
                    initialFailureStatus));
        }

        InterfaceCounterSnapshot previousCounter =
            initialCounterRead.Snapshot!;
        PinnedObservationInterface binding;
        try
        {
            binding = ObservationInterfaceBindingPolicy.Pin(
                initialWlan,
                previousCounter);
        }
        catch (InvalidDataException exception)
        {
            return new BrowserObservationResult(
                BrowserObservationStatus.CounterProviderMismatch,
                null,
                initialWlan,
                exception.Message
                + " 서로 다른 NIC의 카운터를 결합하지 않기 위해 관찰을 시작하지 않았습니다.",
                BrowserObservationTerminationReason.CounterProviderMismatch);
        }

        ObservationWlanIdentityContinuityTracker wlanIdentityTracker =
            new(binding);
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
        int totalSampleCount = checked(
            baselineSampleCount + activeSampleCount);

        try
        {
            for (int index = 0; index < totalSampleCount; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await _runtime.DelayAsync(
                        TimeSpan.FromMilliseconds(
                            options.SampleIntervalMilliseconds),
                        cancellationToken)
                    .ConfigureAwait(false);

                WlanReadResult currentWlanRead =
                    _runtime.ReadWlan();
                WlanSnapshot? currentWlan =
                    WlanInterfaceIdentityReader.AttachIdentity(
                        currentWlanRead.FirstConnectedInterface,
                        identityRead);

                if (currentWlan is not null
                    && string.IsNullOrWhiteSpace(currentWlan.InterfaceId))
                {
                    WlanInterfaceIdentityReadResult refreshedIdentity =
                        _runtime.ReadWlanIdentity();
                    if (refreshedIdentity.IsSuccess)
                    {
                        identityRead = refreshedIdentity;
                        currentWlan =
                            WlanInterfaceIdentityReader.AttachIdentity(
                                currentWlan,
                                identityRead);
                    }
                }

                int unavailableBeforeObservation =
                    wlanIdentityTracker.ConsecutiveUnavailableCount;
                ObservationWlanIdentityContinuityObservation
                    wlanContinuity = wlanIdentityTracker.Observe(
                        currentWlan);
                if (!wlanContinuity.ShouldContinue)
                {
                    bool interfaceChanged = wlanContinuity.Status
                        == ObservationWlanIdentityContinuityStatus.Changed;
                    return CreateInterruptedResult(
                        interfaceChanged
                            ? BrowserObservationStatus.AdapterChanged
                            : BrowserObservationStatus.AdapterUnavailable,
                        startedAt,
                        samples,
                        initialWlan,
                        wlanContinuity.Message
                        + (interfaceChanged
                            ? " 서로 다른 NIC의 처리량을 한 결과에 섞지 않고 관찰을 종료했습니다."
                            : " 고정 카운터는 다른 NIC로 전환하지 않았으며, WLAN 상태 연속성이 확인되지 않은 현재 구간은 읽지 않고 종료했습니다."),
                        progress,
                        interfaceChanged
                            ? BrowserObservationTerminationReason
                                .AdapterChanged
                            : BrowserObservationTerminationReason
                                .WlanIdentityUnavailable);
                }

                bool wlanIdentityUnavailable = wlanContinuity.Status
                    == ObservationWlanIdentityContinuityStatus
                        .TransientlyUnavailable;
                WlanSnapshot? trustedCurrentWlan =
                    wlanContinuity.CurrentIdentityAvailable
                        ? currentWlan
                        : null;

                InterfaceCounterReadResult currentCounterRead =
                    _runtime.ReadCounter(
                        binding.CounterInterfaceId,
                        preferredInterfaceDescription: null,
                        InterfaceCounterSelectionMode
                            .RequireExactInterfaceId);

                if (!currentCounterRead.IsSuccess)
                {
                    BrowserObservationStatus failureStatus =
                        currentCounterRead.Status
                            == InterfaceCounterReadStatus
                                .CounterProviderMismatch
                            ? BrowserObservationStatus
                                .CounterProviderMismatch
                            : BrowserObservationStatus.AdapterUnavailable;
                    return CreateInterruptedResult(
                        failureStatus,
                        startedAt,
                        samples,
                        initialWlan,
                        currentCounterRead.Message
                        + " 시작 시 고정한 물리 Wi-Fi 대신 다른 인터페이스로 자동 전환하지 않았습니다.",
                        progress);
                }

                InterfaceCounterSnapshot currentCounter =
                    currentCounterRead.Snapshot!;
                ObservationInterfaceContinuityResult counterContinuity =
                    ObservationInterfaceBindingPolicy.VerifyCounter(
                        binding,
                        currentCounter);
                if (!counterContinuity.ShouldContinue)
                {
                    return CreateInterruptedResult(
                        BrowserObservationStatus.CounterProviderMismatch,
                        startedAt,
                        samples,
                        initialWlan,
                        counterContinuity.Message
                        + " 해당 샘플을 사용하지 않고 관찰을 종료했습니다.",
                        progress);
                }

                ObservationTimingContinuityDecision timing =
                    ObservationTimingContinuityPolicy.Evaluate(
                        previousCounter.Timestamp,
                        currentCounter.Timestamp,
                        options.SampleIntervalMilliseconds);
                if (!timing.ShouldContinue)
                {
                    BrowserObservationStatus timingStatus = samples.Count == 0
                        ? BrowserObservationStatus.Failed
                        : BrowserObservationStatus.PartialSuccess;
                    return CreateInterruptedResult(
                        timingStatus,
                        startedAt,
                        samples,
                        initialWlan,
                        timing.Message,
                        progress,
                        BrowserObservationTerminationReason
                            .TimingDiscontinuity,
                        previousCounter.Timestamp);
                }

                bool isBaseline = index < baselineSampleCount;
                BrowserObservationSample sample =
                    BrowserObservationCalculator.CreateSample(
                        previousCounter,
                        currentCounter,
                        previousWlan,
                        trustedCurrentWlan,
                        isBaseline,
                        baselineReceiveMbps,
                        previousAdjustedReceiveMbps);

                if (sample.AdapterChanged)
                {
                    return CreateInterruptedResult(
                        BrowserObservationStatus.CounterProviderMismatch,
                        startedAt,
                        samples,
                        initialWlan,
                        "고정된 카운터 인터페이스 ID가 샘플 사이에 변경됐습니다. 해당 구간을 사용하지 않고 관찰을 종료했습니다.",
                        progress);
                }

                if (wlanIdentityUnavailable)
                {
                    sample = sample with
                    {
                        Note = AppendObservationNote(
                            sample.Note,
                            $"WLAN 연결 ID 일시 미확인 {wlanContinuity.ConsecutiveUnavailableCount}/{wlanContinuity.UnavailableThreshold}; 시작 시 고정한 카운터만 사용")
                    };
                }
                else if (unavailableBeforeObservation > 0)
                {
                    sample = sample with
                    {
                        Note = AppendObservationNote(
                            sample.Note,
                            $"WLAN 연결 ID가 {unavailableBeforeObservation}회 미확인 후 시작 시 고정한 동일 인터페이스로 복구")
                    };
                }

                samples.Add(sample);

                if (isBaseline)
                {
                    baselineReceiveMbps =
                        BrowserObservationCalculator
                            .CalculateBaselineReceiveMbps(samples);
                }
                else if (sample.AdjustedReceiveMbps.HasValue)
                {
                    previousAdjustedReceiveMbps =
                        sample.AdjustedReceiveMbps.Value;
                }

                TimeSpan elapsed = currentCounter.Timestamp - startedAt;
                int remainingSamples = totalSampleCount - index - 1;
                TimeSpan remaining = TimeSpan.FromMilliseconds(
                    remainingSamples
                    * (long)options.SampleIntervalMilliseconds);
                BrowserObservationPhase phase = isBaseline
                    ? BrowserObservationPhase.Baseline
                    : BrowserObservationPhase.Observing;
                string message = isBaseline
                    ? $"고정된 물리 Wi-Fi의 백그라운드 트래픽 기준 수집 중 {index + 1}/{baselineSampleCount}"
                    : $"고정된 물리 Wi-Fi의 브라우저 다운로드 관찰 중 {index - baselineSampleCount + 1}/{activeSampleCount}";

                progress?.Report(new BrowserObservationProgress(
                    phase,
                    elapsed,
                    remaining,
                    sample,
                    message));

                previousCounter = currentCounter;
                previousWlan = trustedCurrentWlan;
            }
        }
        catch (OperationCanceledException)
        {
            DateTimeOffset canceledAt = _runtime.UtcNow;
            BrowserObservationSummary? canceledSummary =
                samples.Count == 0
                    ? null
                    : BrowserObservationCalculator.Summarize(
                        startedAt,
                        canceledAt,
                        samples);
            BrowserObservationTerminationReason cancellationReason =
                cancellationContext?.ResolveCancellationReason()
                ?? BrowserObservationTerminationReason.CanceledByUser;
            string cancellationMessage = cancellationReason
                == BrowserObservationTerminationReason.SystemSuspend
                    ? "시스템 절전 또는 최대 절전 전환으로 브라우저 관찰을 중단했습니다. 전원 전환 전후의 Wi-Fi 카운터를 한 결과에 결합하지 않습니다."
                    : "사용자 요청으로 브라우저 관찰을 중단했습니다. 수집된 샘플만 로컬 결과에 유지합니다.";
            progress?.Report(new BrowserObservationProgress(
                BrowserObservationPhase.Canceled,
                canceledAt - startedAt,
                TimeSpan.Zero,
                samples.Count == 0 ? null : samples[^1],
                cancellationMessage));
            return new BrowserObservationResult(
                BrowserObservationStatus.Canceled,
                canceledSummary,
                initialWlan,
                cancellationMessage,
                cancellationReason);
        }
        catch (Exception exception)
        {
            DateTimeOffset failedAt = _runtime.UtcNow;
            BrowserObservationSummary? failedSummary =
                samples.Count == 0
                    ? null
                    : BrowserObservationCalculator.Summarize(
                        startedAt,
                        failedAt,
                        samples);
            progress?.Report(new BrowserObservationProgress(
                BrowserObservationPhase.Failed,
                failedAt - startedAt,
                TimeSpan.Zero,
                samples.Count == 0 ? null : samples[^1],
                $"관찰 중 오류가 발생했습니다: {exception.Message}"));
            return new BrowserObservationResult(
                failedSummary is null
                    ? BrowserObservationStatus.Failed
                    : BrowserObservationStatus.PartialSuccess,
                failedSummary,
                initialWlan,
                $"브라우저 관찰 중 오류가 발생했습니다: {exception.Message}",
                BrowserObservationTerminationReason.Failed);
        }

        DateTimeOffset completedAt = _runtime.UtcNow;
        BrowserObservationSummary summary =
            BrowserObservationCalculator.Summarize(
                startedAt,
                completedAt,
                samples);
        progress?.Report(new BrowserObservationProgress(
            BrowserObservationPhase.Completed,
            completedAt - startedAt,
            TimeSpan.Zero,
            samples.Count == 0 ? null : samples[^1],
            "고정된 물리 Wi-Fi 인터페이스의 브라우저 다운로드 관찰을 완료했습니다."));

        BrowserObservationStatus status = summary.ActiveSampleCount > 0
            ? BrowserObservationStatus.Success
            : BrowserObservationStatus.PartialSuccess;
        return new BrowserObservationResult(
            status,
            summary,
            initialWlan,
            summary.Message,
            BrowserObservationTerminationReason.Completed);
    }

    private BrowserObservationResult CreateInterruptedResult(
        BrowserObservationStatus status,
        DateTimeOffset startedAt,
        IReadOnlyList<BrowserObservationSample> samples,
        WlanSnapshot initialWlan,
        string message,
        IProgress<BrowserObservationProgress>? progress,
        BrowserObservationTerminationReason? terminationReason = null,
        DateTimeOffset? completedAtOverride = null)
    {
        DateTimeOffset completedAt = completedAtOverride
            ?? _runtime.UtcNow;
        BrowserObservationSummary? summary = samples.Count == 0
            ? null
            : BrowserObservationCalculator.Summarize(
                startedAt,
                completedAt,
                samples);
        BrowserObservationSample? latest = samples.Count == 0
            ? null
            : samples[^1];

        progress?.Report(new BrowserObservationProgress(
            BrowserObservationPhase.Failed,
            completedAt - startedAt,
            TimeSpan.Zero,
            latest,
            message));
        return new BrowserObservationResult(
            status,
            summary,
            initialWlan,
            message,
            terminationReason
                ?? BrowserObservationTerminationPolicy.FromStatus(status));
    }

    private static string AppendObservationNote(
        string? existing,
        string addition)
    {
        if (string.IsNullOrWhiteSpace(existing))
        {
            return addition;
        }

        if (existing.Contains(addition, StringComparison.Ordinal))
        {
            return existing;
        }

        return existing.TrimEnd() + " " + addition;
    }

    private static int CalculateSampleCount(
        int seconds,
        int sampleIntervalMilliseconds)
    {
        long durationMilliseconds = checked(seconds * 1000L);
        return checked((int)Math.Ceiling(
            durationMilliseconds
            / (double)sampleIntervalMilliseconds));
    }
}
