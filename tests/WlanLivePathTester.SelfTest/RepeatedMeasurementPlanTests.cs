using System.Runtime.CompilerServices;
using WlanLivePathTester.Core.Measurements;
using WlanLivePathTester.Core.Models;

namespace WlanLivePathTester.SelfTest;

internal static class RepeatedMeasurementPlanTests
{
    [ModuleInitializer]
    internal static void Run()
    {
        RejectsExcessivePlannedTraffic();
        SeparatesNotCompletedRuns();
        Console.WriteLine("PASS  반복 측정 트래픽 상한과 미완료 구분");
    }

    private static void RejectsExcessivePlannedTraffic()
    {
        MeasurementTargetDefinition target = new(
            Name: "대용량 대상",
            Url: "https://example.invalid/file.bin",
            PathKind: NetworkPathKind.External,
            RequireProxy: true,
            RequireDirect: false,
            MaxBytes: 1024L * 1024 * 1024,
            TimeoutSeconds: 60,
            Streams: 1,
            MaxRedirects: 0);
        RepeatedMeasurementPlan plan = new(
            RepeatCount: 3,
            IncludeWarmup: true,
            DelayMilliseconds: 0);

        IReadOnlyList<string> errors = plan.ValidateForTarget(target);
        if (!errors.Any(error => error.Contains(
                "최대 예상 수신량",
                StringComparison.Ordinal)))
        {
            throw new InvalidOperationException(
                "대상 하나의 반복 측정이 2GiB를 넘으면 차단해야 합니다.");
        }
    }

    private static void SeparatesNotCompletedRuns()
    {
        RepeatedMeasurementPlan plan = new(
            RepeatCount: 3,
            IncludeWarmup: false,
            DelayMilliseconds: 0);
        RepeatedMeasurementSummary summary =
            RepeatedMeasurementAggregator.Summarize(
                plan,
                [
                    new RepeatedMeasurementRun(
                        Sequence: 1,
                        IsWarmup: false,
                        Result: SuccessfulResult())
                ]);

        if (summary.CompletedMeasurementCount != 1
            || summary.SuccessfulMeasurementCount != 1
            || summary.FailedMeasurementCount != 0
            || summary.NotCompletedMeasurementCount != 2)
        {
            throw new InvalidOperationException(
                "완료·성공·실패·미완료 횟수를 정확히 분리해야 합니다.");
        }

        if (summary.Confidence != RepeatedMeasurementConfidence.Low)
        {
            throw new InvalidOperationException(
                "미완료 본 측정이 있으면 신뢰도는 Low여야 합니다.");
        }
    }

    private static DownloadMeasurementResult SuccessfulResult()
    {
        DateTimeOffset start = DateTimeOffset.UnixEpoch;
        return new DownloadMeasurementResult(
            TargetName: "합성 대상",
            PathKind: NetworkPathKind.External,
            Status: MeasurementStatus.Success,
            StartedAt: start,
            CompletedAt: start.AddSeconds(8),
            BytesReceived: 100L * 1024 * 1024,
            AverageMbps: 100,
            TimeToFirstByte: TimeSpan.FromMilliseconds(100),
            HttpStatusCode: 200,
            ProxyWasUsed: true,
            StreamsRequested: 1,
            StreamsCompleted: 1,
            RedirectCount: 0,
            FinalUrl: "https://example.invalid/file.bin",
            Samples:
            [
                new ThroughputSample(1, TimeSpan.FromSeconds(1), 12_500_000, 100),
                new ThroughputSample(1, TimeSpan.FromSeconds(2), 12_500_000, 100)
            ],
            ResponseHeaders: new Dictionary<string, string>
            {
                ["Content-Length"] = "104857600"
            },
            ErrorCode: null,
            Message: "합성 성공");
    }
}
