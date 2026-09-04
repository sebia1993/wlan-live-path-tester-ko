namespace WlanLivePathTester.Core.Measurements;

public static class RepeatedMeasurementAggregator
{
    private const double HighConfidenceVariation = 0.20;
    private const double MediumConfidenceVariation = 0.40;

    public static RepeatedMeasurementSummary Summarize(
        RepeatedMeasurementPlan plan,
        IReadOnlyList<RepeatedMeasurementRun> runs)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(runs);

        IReadOnlyList<string> planErrors = plan.Validate();
        if (planErrors.Count > 0)
        {
            throw new ArgumentException(
                string.Join(" ", planErrors),
                nameof(plan));
        }

        RepeatedMeasurementRun[] measuredRuns = runs
            .Where(run => !run.IsWarmup)
            .OrderBy(run => run.Sequence)
            .ToArray();
        RepeatedMeasurementRun[] successfulRuns = measuredRuns
            .Where(run => run.Result.IsSuccess)
            .Where(run => run.Result.AverageMbps is > 0)
            .ToArray();
        int failedCount = measuredRuns.Length - successfulRuns.Length;
        int notCompletedCount = Math.Max(
            0,
            plan.RepeatCount - measuredRuns.Length);
        double[] values = successfulRuns
            .Select(run => run.Result.AverageMbps!.Value)
            .Order()
            .ToArray();

        bool cacheHitPossible = successfulRuns.Any(run =>
            MeasurementQualityEvaluator.ClassifyCache(
                run.Result.ResponseHeaders)
            == MeasurementCacheClassification.PossibleHit);

        if (values.Length == 0)
        {
            List<string> noResultReasons =
            [
                "성공한 본 측정 결과가 없어 대표 처리량을 계산하지 않았습니다."
            ];
            if (notCompletedCount > 0)
            {
                noResultReasons.Add($"계획한 본 측정 중 {notCompletedCount}회는 시작되지 않았거나 완료되지 않았습니다.");
            }

            return new RepeatedMeasurementSummary(
                PlannedMeasurementCount: plan.RepeatCount,
                CompletedMeasurementCount: measuredRuns.Length,
                SuccessfulMeasurementCount: 0,
                FailedMeasurementCount: failedCount,
                NotCompletedMeasurementCount: notCompletedCount,
                MedianMbps: null,
                MinimumMbps: null,
                MaximumMbps: null,
                MeanMbps: null,
                StandardDeviationMbps: null,
                CoefficientOfVariation: null,
                RepresentativeSequence: null,
                CacheHitPossible: cacheHitPossible,
                Confidence: RepeatedMeasurementConfidence.NotApplicable,
                ConfidenceReasons: noResultReasons);
        }

        double mean = values.Average();
        double median = Median(values);
        double variance = values.Average(value => Math.Pow(value - mean, 2));
        double standardDeviation = Math.Sqrt(variance);
        double coefficientOfVariation = mean <= 0
            ? 0
            : standardDeviation / mean;
        RepeatedMeasurementRun representative = successfulRuns
            .OrderBy(run => Math.Abs(run.Result.AverageMbps!.Value - median))
            .ThenBy(run => run.Sequence)
            .First();

        List<string> reasons = [];
        bool anyLowIndividual = successfulRuns.Any(run =>
            MeasurementQualityEvaluator.Evaluate(run.Result).Confidence
            == MeasurementConfidence.Low);

        RepeatedMeasurementConfidence confidence;
        if (successfulRuns.Length < 2)
        {
            confidence = RepeatedMeasurementConfidence.Low;
            reasons.Add("성공한 본 측정이 2회 미만이어서 반복 안정성을 판단할 수 없습니다.");
        }
        else if (failedCount > 0
                 || notCompletedCount > 0
                 || anyLowIndividual)
        {
            confidence = RepeatedMeasurementConfidence.Low;
            if (failedCount > 0)
            {
                reasons.Add($"실행된 본 측정 중 {failedCount}회가 성공하지 않았습니다.");
            }

            if (notCompletedCount > 0)
            {
                reasons.Add($"계획한 본 측정 중 {notCompletedCount}회가 완료되지 않았습니다.");
            }

            if (anyLowIndividual)
            {
                reasons.Add("수신량·측정 시간·스트림 완료 기준에서 낮은 신뢰도의 개별 결과가 있습니다.");
            }
        }
        else if (coefficientOfVariation > MediumConfidenceVariation)
        {
            confidence = RepeatedMeasurementConfidence.Low;
            reasons.Add($"본 측정 변동계수가 {coefficientOfVariation:P1}로 40%를 초과합니다.");
        }
        else if (successfulRuns.Length >= 3
                 && coefficientOfVariation <= HighConfidenceVariation
                 && !cacheHitPossible)
        {
            confidence = RepeatedMeasurementConfidence.High;
            reasons.Add($"성공한 본 측정 {successfulRuns.Length}회의 변동계수가 {coefficientOfVariation:P1}로 20% 이하입니다.");
        }
        else
        {
            confidence = RepeatedMeasurementConfidence.Medium;
            if (successfulRuns.Length < 3)
            {
                reasons.Add("성공 결과가 3회 미만이어서 높은 신뢰도로 분류하지 않았습니다.");
            }
            else
            {
                reasons.Add($"본 측정 변동계수가 {coefficientOfVariation:P1}로 40% 이하입니다.");
            }
        }

        if (cacheHitPossible)
        {
            if (confidence == RepeatedMeasurementConfidence.High)
            {
                confidence = RepeatedMeasurementConfidence.Medium;
            }

            reasons.Add("응답 헤더에서 프록시 또는 CDN 캐시 적중 가능성이 확인됐습니다.");
        }

        return new RepeatedMeasurementSummary(
            PlannedMeasurementCount: plan.RepeatCount,
            CompletedMeasurementCount: measuredRuns.Length,
            SuccessfulMeasurementCount: successfulRuns.Length,
            FailedMeasurementCount: failedCount,
            NotCompletedMeasurementCount: notCompletedCount,
            MedianMbps: median,
            MinimumMbps: values.Min(),
            MaximumMbps: values.Max(),
            MeanMbps: mean,
            StandardDeviationMbps: standardDeviation,
            CoefficientOfVariation: coefficientOfVariation,
            RepresentativeSequence: representative.Sequence,
            CacheHitPossible: cacheHitPossible,
            Confidence: confidence,
            ConfidenceReasons: reasons);
    }

    private static double Median(IReadOnlyList<double> sortedValues)
    {
        int midpoint = sortedValues.Count / 2;
        return sortedValues.Count % 2 == 1
            ? sortedValues[midpoint]
            : (sortedValues[midpoint - 1] + sortedValues[midpoint]) / 2d;
    }
}
