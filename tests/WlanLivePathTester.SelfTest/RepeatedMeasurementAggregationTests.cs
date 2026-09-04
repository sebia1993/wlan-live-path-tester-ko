using System.Runtime.CompilerServices;
using WlanLivePathTester.Core.Measurements;
using WlanLivePathTester.Core.Models;

namespace WlanLivePathTester.SelfTest;

internal static class RepeatedMeasurementAggregationTests
{
    [ModuleInitializer]
    internal static void Run()
    {
        ExcludesWarmupAndCalculatesMedian();
        ClassifiesStableThreeRunsAsHigh();
        ClassifiesLargeVariationAsLow();
        ClassifiesPartialFailureAsLow();
        CapsCachedStableRunsAtMedium();
        Console.WriteLine("PASS  반복 측정 중앙값·편차·신뢰도 규칙");
    }

    private static void ExcludesWarmupAndCalculatesMedian()
    {
        RepeatedMeasurementPlan plan = new(3, IncludeWarmup: true, DelayMilliseconds: 0);
        RepeatedMeasurementRun[] runs =
        [
            Run(0, isWarmup: true, MbpsResult(900)),
            Run(1, isWarmup: false, MbpsResult(100)),
            Run(2, isWarmup: false, MbpsResult(80)),
            Run(3, isWarmup: false, MbpsResult(120))
        ];

        RepeatedMeasurementSummary summary =
            RepeatedMeasurementAggregator.Summarize(plan, runs);

        Ensure(summary.MedianMbps == 100,
            "예열 결과를 제외한 중앙값은 100 Mbps여야 합니다.");
        Ensure(summary.MinimumMbps == 80 && summary.MaximumMbps == 120,
            "본 측정 최소·최대값을 계산해야 합니다.");
        Ensure(summary.RepresentativeSequence == 1,
            "중앙값에 가장 가까운 본 측정 순번을 기록해야 합니다.");
    }

    private static void ClassifiesStableThreeRunsAsHigh()
    {
        RepeatedMeasurementPlan plan = new(3, IncludeWarmup: false, DelayMilliseconds: 0);
        RepeatedMeasurementSummary summary =
            RepeatedMeasurementAggregator.Summarize(
                plan,
                [
                    Run(1, false, MbpsResult(98)),
                    Run(2, false, MbpsResult(100)),
                    Run(3, false, MbpsResult(102))
                ]);

        Ensure(summary.Confidence == RepeatedMeasurementConfidence.High,
            "캐시 근거가 없는 안정적인 성공 3회는 High여야 합니다.");
        Ensure(summary.CoefficientOfVariation is < 0.03,
            "안정적인 합성 결과의 변동계수는 낮아야 합니다.");
    }

    private static void ClassifiesLargeVariationAsLow()
    {
        RepeatedMeasurementPlan plan = new(3, IncludeWarmup: false, DelayMilliseconds: 0);
        RepeatedMeasurementSummary summary =
            RepeatedMeasurementAggregator.Summarize(
                plan,
                [
                    Run(1, false, MbpsResult(20)),
                    Run(2, false, MbpsResult(100)),
                    Run(3, false, MbpsResult(180))
                ]);

        Ensure(summary.Confidence == RepeatedMeasurementConfidence.Low,
            "변동계수 40% 초과 결과는 Low여야 합니다.");
    }

    private static void ClassifiesPartialFailureAsLow()
    {
        RepeatedMeasurementPlan plan = new(3, IncludeWarmup: false, DelayMilliseconds: 0);
        RepeatedMeasurementSummary summary =
            RepeatedMeasurementAggregator.Summarize(
                plan,
                [
                    Run(1, false, MbpsResult(100)),
                    Run(2, false, FailedResult()),
                    Run(3, false, MbpsResult(102))
                ]);

        Ensure(summary.Confidence == RepeatedMeasurementConfidence.Low,
            "계획한 본 측정 중 실패가 있으면 Low여야 합니다.");
        Ensure(summary.SuccessfulMeasurementCount == 2
               && summary.FailedMeasurementCount == 1,
            "성공·실패 본 측정 횟수를 분리해야 합니다.");
    }

    private static void CapsCachedStableRunsAtMedium()
    {
        RepeatedMeasurementPlan plan = new(3, IncludeWarmup: false, DelayMilliseconds: 0);
        IReadOnlyDictionary<string, string> cacheHeaders =
            new Dictionary<string, string>
            {
                ["Age"] = "120",
                ["X-Cache"] = "HIT"
            };
        RepeatedMeasurementSummary summary =
            RepeatedMeasurementAggregator.Summarize(
                plan,
                [
                    Run(1, false, MbpsResult(99, cacheHeaders)),
                    Run(2, false, MbpsResult(100, cacheHeaders)),
                    Run(3, false, MbpsResult(101, cacheHeaders))
                ]);

        Ensure(summary.CacheHitPossible,
            "캐시 헤더가 있으면 적중 가능성을 기록해야 합니다.");
        Ensure(summary.Confidence == RepeatedMeasurementConfidence.Medium,
            "안정적이어도 캐시 적중 가능성이 있으면 High를 허용하지 않아야 합니다.");
    }

    private static RepeatedMeasurementRun Run(
        int sequence,
        bool isWarmup,
        DownloadMeasurementResult result) =>
        new(sequence, isWarmup, result);

    private static DownloadMeasurementResult MbpsResult(
        double mbps,
        IReadOnlyDictionary<string, string>? headers = null)
    {
        DateTimeOffset start = DateTimeOffset.UnixEpoch.AddSeconds(mbps);
        return new DownloadMeasurementResult(
            TargetName: "합성 대상",
            PathKind: NetworkPathKind.External,
            Status: MeasurementStatus.Success,
            StartedAt: start,
            CompletedAt: start.AddSeconds(8),
            BytesReceived: 100L * 1024 * 1024,
            AverageMbps: mbps,
            TimeToFirstByte: TimeSpan.FromMilliseconds(100),
            HttpStatusCode: 200,
            ProxyWasUsed: true,
            StreamsRequested: 1,
            StreamsCompleted: 1,
            RedirectCount: 0,
            FinalUrl: "https://example.invalid/file.bin",
            Samples:
            [
                new ThroughputSample(1, TimeSpan.FromSeconds(1), 12_500_000, mbps),
                new ThroughputSample(1, TimeSpan.FromSeconds(2), 12_500_000, mbps)
            ],
            ResponseHeaders: headers
                ?? new Dictionary<string, string>
                {
                    ["Content-Length"] = "104857600"
                },
            ErrorCode: null,
            Message: "합성 성공");
    }

    private static DownloadMeasurementResult FailedResult()
    {
        DateTimeOffset start = DateTimeOffset.UnixEpoch;
        return new DownloadMeasurementResult(
            TargetName: "합성 대상",
            PathKind: NetworkPathKind.External,
            Status: MeasurementStatus.TimedOut,
            StartedAt: start,
            CompletedAt: start.AddSeconds(5),
            BytesReceived: 0,
            AverageMbps: null,
            TimeToFirstByte: null,
            HttpStatusCode: null,
            ProxyWasUsed: true,
            StreamsRequested: 1,
            StreamsCompleted: 0,
            RedirectCount: 0,
            FinalUrl: "https://example.invalid/file.bin",
            Samples: Array.Empty<ThroughputSample>(),
            ResponseHeaders: new Dictionary<string, string>(),
            ErrorCode: "WINHTTP_TIMEOUT",
            Message: "합성 시간 초과");
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
