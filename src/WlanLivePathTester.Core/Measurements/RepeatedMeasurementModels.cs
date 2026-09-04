using WlanLivePathTester.Core.Models;

namespace WlanLivePathTester.Core.Measurements;

public enum RepeatedMeasurementConfidence
{
    NotApplicable,
    Low,
    Medium,
    High
}

public sealed record RepeatedMeasurementPlan(
    int RepeatCount,
    bool IncludeWarmup,
    int DelayMilliseconds)
{
    public const int MinimumRepeatCount = 1;
    public const int MaximumRepeatCount = 5;
    public const int MinimumDelayMilliseconds = 0;
    public const int MaximumDelayMilliseconds = 10000;
    public const long MaximumPlannedBytesPerTarget = 2L * 1024 * 1024 * 1024;

    public static RepeatedMeasurementPlan Recommended { get; } = new(
        RepeatCount: 3,
        IncludeWarmup: true,
        DelayMilliseconds: 500);

    public int TotalRunCount => RepeatCount + (IncludeWarmup ? 1 : 0);

    public IReadOnlyList<string> Validate()
    {
        List<string> errors = [];
        if (RepeatCount is < MinimumRepeatCount or > MaximumRepeatCount)
        {
            errors.Add($"본 측정 횟수는 {MinimumRepeatCount}~{MaximumRepeatCount}회여야 합니다.");
        }

        if (DelayMilliseconds is < MinimumDelayMilliseconds or > MaximumDelayMilliseconds)
        {
            errors.Add($"측정 간 대기 시간은 {MinimumDelayMilliseconds}~{MaximumDelayMilliseconds}ms여야 합니다.");
        }

        return errors;
    }

    public IReadOnlyList<string> ValidateForTarget(
        MeasurementTargetDefinition target)
    {
        ArgumentNullException.ThrowIfNull(target);

        List<string> errors = [.. Validate()];
        if (RepeatCount is < MinimumRepeatCount or > MaximumRepeatCount)
        {
            return errors;
        }

        long plannedBytes;
        try
        {
            plannedBytes = checked(target.MaxBytes * TotalRunCount);
        }
        catch (OverflowException)
        {
            errors.Add("반복 측정의 최대 예상 수신량을 계산할 수 없습니다.");
            return errors;
        }

        if (plannedBytes > MaximumPlannedBytesPerTarget)
        {
            errors.Add($"대상 하나의 반복 측정 최대 예상 수신량은 {MaximumPlannedBytesPerTarget / 1024 / 1024}MiB를 넘을 수 없습니다.");
        }

        return errors;
    }

    public long GetPlannedMaximumBytes(
        MeasurementTargetDefinition target)
    {
        ArgumentNullException.ThrowIfNull(target);
        IReadOnlyList<string> errors = ValidateForTarget(target);
        if (errors.Count > 0)
        {
            throw new ArgumentException(string.Join(" ", errors), nameof(target));
        }

        return checked(target.MaxBytes * TotalRunCount);
    }
}

public sealed record RepeatedMeasurementRun(
    int Sequence,
    bool IsWarmup,
    DownloadMeasurementResult Result);

public sealed record RepeatedMeasurementSummary(
    int PlannedMeasurementCount,
    int CompletedMeasurementCount,
    int SuccessfulMeasurementCount,
    int FailedMeasurementCount,
    int NotCompletedMeasurementCount,
    double? MedianMbps,
    double? MinimumMbps,
    double? MaximumMbps,
    double? MeanMbps,
    double? StandardDeviationMbps,
    double? CoefficientOfVariation,
    int? RepresentativeSequence,
    bool CacheHitPossible,
    RepeatedMeasurementConfidence Confidence,
    IReadOnlyList<string> ConfidenceReasons);

public sealed record RepeatedMeasurementResult(
    string TargetName,
    NetworkPathKind PathKind,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    RepeatedMeasurementPlan Plan,
    IReadOnlyList<RepeatedMeasurementRun> Runs,
    RepeatedMeasurementSummary Summary)
{
    public bool WasCanceled => Runs.Any(run =>
        run.Result.Status == MeasurementStatus.Canceled)
        || Summary.NotCompletedMeasurementCount > 0;

    public long TotalBytesReceived => Runs.Sum(run => run.Result.BytesReceived);
}

public sealed record RepeatedMeasurementProgress(
    int CompletedRunCount,
    int TotalRunCount,
    int CurrentSequence,
    bool IsWarmup,
    string Message,
    DownloadMeasurementResult? LatestResult);
