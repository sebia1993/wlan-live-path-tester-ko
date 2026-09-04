using WlanLivePathTester.Core.Models;

namespace WlanLivePathTester.Core.Measurements;

public enum MeasurementConfidence
{
    NotApplicable,
    Low,
    Medium,
    High
}

public enum MeasurementCacheClassification
{
    Unknown,
    NoHitEvidence,
    PossibleHit
}

public sealed record MeasurementQualityAssessment(
    MeasurementConfidence Confidence,
    MeasurementCacheClassification CacheClassification,
    IReadOnlyList<string> Reasons);

public static class MeasurementQualityEvaluator
{
    private const long MinimumUsefulBytes = 10L * 1024 * 1024;
    private const long PreferredBytes = 50L * 1024 * 1024;
    private static readonly TimeSpan MinimumUsefulDuration = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan PreferredDuration = TimeSpan.FromSeconds(5);

    public static MeasurementQualityAssessment Evaluate(
        DownloadMeasurementResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        MeasurementCacheClassification cache = ClassifyCache(
            result.ResponseHeaders);
        List<string> reasons = [];

        if (!result.IsSuccess)
        {
            reasons.Add("성공한 처리량 측정이 아니므로 속도 신뢰도를 평가하지 않습니다.");
            return new MeasurementQualityAssessment(
                MeasurementConfidence.NotApplicable,
                cache,
                reasons);
        }

        bool lowConfidence = false;
        bool mediumConfidence = false;

        if (result.BytesReceived < MinimumUsefulBytes)
        {
            lowConfidence = true;
            reasons.Add("실제 수신량이 10 MiB 미만입니다.");
        }
        else if (result.BytesReceived < PreferredBytes)
        {
            mediumConfidence = true;
            reasons.Add("실제 수신량이 권장 50 MiB보다 적습니다.");
        }

        if (result.Duration < MinimumUsefulDuration)
        {
            lowConfidence = true;
            reasons.Add("측정 시간이 2초 미만입니다.");
        }
        else if (result.Duration < PreferredDuration)
        {
            mediumConfidence = true;
            reasons.Add("측정 시간이 권장 5초보다 짧습니다.");
        }

        if (result.StreamsCompleted < result.StreamsRequested
            || result.Status == MeasurementStatus.PartialSuccess)
        {
            lowConfidence = true;
            reasons.Add("요청한 스트림 중 일부만 완료되었습니다.");
        }

        if (result.Samples.Count < 2)
        {
            mediumConfidence = true;
            reasons.Add("시간축 처리량 샘플이 2개 미만입니다.");
        }

        if (cache == MeasurementCacheClassification.PossibleHit)
        {
            mediumConfidence = true;
            reasons.Add("응답 헤더에 프록시 또는 CDN 캐시 적중 가능성이 있습니다.");
        }

        MeasurementConfidence confidence = lowConfidence
            ? MeasurementConfidence.Low
            : mediumConfidence
                ? MeasurementConfidence.Medium
                : MeasurementConfidence.High;

        if (reasons.Count == 0)
        {
            reasons.Add("수신량, 측정 시간, 스트림 완료와 캐시 헤더 기준을 충족했습니다.");
        }

        return new MeasurementQualityAssessment(
            confidence,
            cache,
            reasons);
    }

    public static MeasurementCacheClassification ClassifyCache(
        IReadOnlyDictionary<string, string> headers)
    {
        ArgumentNullException.ThrowIfNull(headers);

        if (TryGet(headers, "Age", out string ageValue)
            && long.TryParse(ageValue, out long ageSeconds)
            && ageSeconds > 0)
        {
            return MeasurementCacheClassification.PossibleHit;
        }

        if (TryGet(headers, "X-Cache", out string xCache)
            && xCache.Contains("hit", StringComparison.OrdinalIgnoreCase))
        {
            return MeasurementCacheClassification.PossibleHit;
        }

        if (TryGet(headers, "Cache-Status", out string cacheStatus)
            && cacheStatus.Contains("hit", StringComparison.OrdinalIgnoreCase))
        {
            return MeasurementCacheClassification.PossibleHit;
        }

        return headers.Count == 0
            ? MeasurementCacheClassification.Unknown
            : MeasurementCacheClassification.NoHitEvidence;
    }

    private static bool TryGet(
        IReadOnlyDictionary<string, string> headers,
        string name,
        out string value)
    {
        if (headers.TryGetValue(name, out string? found)
            && !string.IsNullOrWhiteSpace(found))
        {
            value = found.Trim();
            return true;
        }

        value = string.Empty;
        return false;
    }
}
