using System.Runtime.Versioning;
using WlanLivePathTester.Core.Measurements;
using WlanLivePathTester.Core.Models;

namespace WlanLivePathTester.Windows.Measurements;

[SupportedOSPlatform("windows")]
public static class RepeatedMeasurementRunner
{
    public static async Task<RepeatedMeasurementResult> RunAsync(
        MeasurementTargetDefinition target,
        RepeatedMeasurementPlan plan,
        bool performHeadPreflight = true,
        IProgress<RepeatedMeasurementProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(plan);

        IReadOnlyList<string> planErrors = plan.ValidateForTarget(target);
        if (planErrors.Count > 0)
        {
            throw new ArgumentException(
                string.Join(" ", planErrors),
                nameof(plan));
        }

        DateTimeOffset startedAt = DateTimeOffset.UtcNow;
        List<RepeatedMeasurementRun> runs = [];
        int totalRuns = plan.TotalRunCount;

        for (int runIndex = 0; runIndex < totalRuns; runIndex++)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            bool isWarmup = plan.IncludeWarmup && runIndex == 0;
            int sequence = isWarmup ? 0 : runIndex + (plan.IncludeWarmup ? 0 : 1);
            progress?.Report(new RepeatedMeasurementProgress(
                CompletedRunCount: runs.Count,
                TotalRunCount: totalRuns,
                CurrentSequence: sequence,
                IsWarmup: isWarmup,
                Message: isWarmup
                    ? "예열 측정을 시작합니다. 이 결과는 대표값 계산에서 제외합니다."
                    : $"본 측정 {sequence}/{plan.RepeatCount}회를 시작합니다.",
                LatestResult: null));

            DownloadMeasurementResult result =
                await DownloadMeasurementRunner.RunAsync(
                    target,
                    performHeadPreflight,
                    cancellationToken).ConfigureAwait(false);
            runs.Add(new RepeatedMeasurementRun(
                Sequence: sequence,
                IsWarmup: isWarmup,
                Result: result));

            progress?.Report(new RepeatedMeasurementProgress(
                CompletedRunCount: runs.Count,
                TotalRunCount: totalRuns,
                CurrentSequence: sequence,
                IsWarmup: isWarmup,
                Message: isWarmup
                    ? $"예열 측정이 {FormatStatus(result.Status)} 상태로 끝났습니다."
                    : $"본 측정 {sequence}/{plan.RepeatCount}회가 {FormatStatus(result.Status)} 상태로 끝났습니다.",
                LatestResult: result));

            if (result.Status == MeasurementStatus.Canceled
                || cancellationToken.IsCancellationRequested)
            {
                break;
            }

            bool hasNextRun = runIndex + 1 < totalRuns;
            if (hasNextRun && plan.DelayMilliseconds > 0)
            {
                try
                {
                    await Task.Delay(
                        plan.DelayMilliseconds,
                        cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                    when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }

        RepeatedMeasurementResult repeatedResult = new(
            TargetName: target.Name,
            PathKind: target.PathKind,
            StartedAt: startedAt,
            CompletedAt: DateTimeOffset.UtcNow,
            Plan: plan,
            Runs: runs,
            Summary: RepeatedMeasurementAggregator.Summarize(plan, runs));
        RepeatedMeasurementResultHistory.Add(repeatedResult);
        return repeatedResult;
    }

    private static string FormatStatus(MeasurementStatus status) =>
        status switch
        {
            MeasurementStatus.Success => "성공",
            MeasurementStatus.PartialSuccess => "일부 성공",
            MeasurementStatus.Canceled => "취소",
            MeasurementStatus.TimedOut => "시간 초과",
            MeasurementStatus.Blocked => "정책 차단",
            MeasurementStatus.PathMismatch => "경로 불일치",
            MeasurementStatus.ProxyAuthenticationRequired => "프록시 인증 실패",
            _ => "실패"
        };
}
