using System.Diagnostics;
using System.Runtime.Versioning;
using WlanLivePathTester.Core.Measurements;
using WlanLivePathTester.Core.Models;
using WlanLivePathTester.Core.Security;
using WlanLivePathTester.Windows.Http;

namespace WlanLivePathTester.Windows.Measurements;

[SupportedOSPlatform("windows")]
public static class DownloadMeasurementRunner
{
    public static Task<DownloadMeasurementResult> RunAsync(
        MeasurementTargetDefinition target,
        bool performHeadPreflight = true,
        CancellationToken cancellationToken = default)
    {
        return RunCoreAsync(
            target,
            WinHttpRequestExecutor.Execute,
            performHeadPreflight,
            cancellationToken);
    }

    public static async Task<IReadOnlyList<DownloadMeasurementResult>> RunManyAsync(
        IEnumerable<MeasurementTargetDefinition> targets,
        bool performHeadPreflight = true,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(targets);

        List<DownloadMeasurementResult> results = [];
        foreach (MeasurementTargetDefinition target in targets)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                results.Add(CreateCanceled(target));
                break;
            }

            results.Add(await RunAsync(
                target,
                performHeadPreflight,
                cancellationToken).ConfigureAwait(false));
        }

        return results;
    }

    internal static Task<DownloadMeasurementResult> RunExplicitForSmokeAsync(
        MeasurementTargetDefinition target,
        string? proxyEndpoint,
        bool performHeadPreflight = true,
        CancellationToken cancellationToken = default)
    {
        return RunCoreAsync(
            target,
            options => WinHttpRequestExecutor.ExecuteExplicitForSmoke(
                options,
                proxyEndpoint),
            performHeadPreflight,
            cancellationToken);
    }

    private static async Task<DownloadMeasurementResult> RunCoreAsync(
        MeasurementTargetDefinition target,
        Func<WinHttpRequestOptions, WinHttpRequestResult> transport,
        bool performHeadPreflight,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(transport);

        DateTimeOffset startedAt = DateTimeOffset.UtcNow;
        IReadOnlyList<string> validationErrors = TargetValidator.Validate(target);
        string? semanticError = ValidatePathSemantics(target);

        if (validationErrors.Count > 0 || semanticError is not null)
        {
            IEnumerable<string> semanticErrors = semanticError is null
                ? Array.Empty<string>()
                : new[] { semanticError };
            string validationMessage = string.Join(
                " ",
                validationErrors.Concat(semanticErrors));

            return CreateTerminalResult(
                target,
                MeasurementStatus.Blocked,
                startedAt,
                bytesReceived: 0,
                averageMbps: null,
                timeToFirstByte: null,
                httpStatusCode: null,
                proxyWasUsed: null,
                streamsCompleted: 0,
                redirectCount: 0,
                finalUrl: target.Url,
                samples: [],
                responseHeaders: EmptyHeaders,
                errorCode: "TARGET_VALIDATION_FAILED",
                message: validationMessage);
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return CreateCanceled(target, startedAt);
        }

        Uri startUri = new(target.Url, UriKind.Absolute);
        Uri downloadStartUri = startUri;
        int preflightRedirects = 0;

        if (performHeadPreflight)
        {
            RequestChainResult preflight;
            try
            {
                preflight = await Task.Run(
                    () => ExecuteChain(
                        target,
                        startUri,
                        WinHttpRequestMethod.Head,
                        maxResponseBytes: 0,
                        transport,
                        cancellationToken),
                    CancellationToken.None).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return CreateCanceled(target, startedAt);
            }

            bool headUnsupported = preflight.Response.Status
                == WinHttpRequestStatus.HttpErrorResponse
                && preflight.Response.HttpStatusCode is 405 or 501;

            if (!preflight.Response.IsSuccess && !headUnsupported)
            {
                return FromFailure(
                    target,
                    startedAt,
                    preflight,
                    streamsCompleted: 0);
            }

            downloadStartUri = preflight.FinalUri;
            preflightRedirects = preflight.RedirectCount;
        }

        long[] streamLimits = AllocateBytes(
            target.MaxBytes,
            target.Streams);

        Stopwatch downloadStopwatch = Stopwatch.StartNew();
        Task<RequestChainResult>[] tasks = new Task<RequestChainResult>[target.Streams];

        for (int index = 0; index < target.Streams; index++)
        {
            long streamLimit = streamLimits[index];
            tasks[index] = Task.Run(
                () => ExecuteChain(
                    target,
                    downloadStartUri,
                    WinHttpRequestMethod.Get,
                    streamLimit,
                    transport,
                    cancellationToken),
                CancellationToken.None);
        }

        RequestChainResult[] streamResults;
        try
        {
            streamResults = await Task.WhenAll(tasks).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            downloadStopwatch.Stop();
            return CreateCanceled(target, startedAt);
        }

        downloadStopwatch.Stop();

        RequestChainResult[] successful = streamResults
            .Where(result => result.Response.IsSuccess)
            .ToArray();

        if (successful.Length == 0)
        {
            RequestChainResult representative = streamResults
                .OrderBy(result => FailurePriority(result.Response.Status))
                .First();

            return FromFailure(
                target,
                startedAt,
                representative,
                streamsCompleted: 0,
                redirectCountOverride: preflightRedirects
                    + streamResults.Max(result => result.RedirectCount));
        }

        long totalBytes = streamResults.Sum(
            result => result.Response.BytesReceived);
        double averageMbps = CalculateMbps(
            totalBytes,
            downloadStopwatch.Elapsed);
        TimeSpan? averageTtfb = AverageTimeToFirstByte(successful);
        int redirectCount = preflightRedirects
            + streamResults.Max(result => result.RedirectCount);

        List<ThroughputSample> samples = [];
        for (int index = 0; index < streamResults.Length; index++)
        {
            foreach (ThroughputSample sample in
                     streamResults[index].Response.ThroughputSamples)
            {
                samples.Add(sample with { StreamIndex = index + 1 });
            }
        }

        RequestChainResult firstSuccessful = successful[0];
        bool partial = successful.Length != streamResults.Length;
        MeasurementStatus status = partial
            ? MeasurementStatus.PartialSuccess
            : MeasurementStatus.Success;

        string message = partial
            ? $"{target.Streams}개 스트림 중 {successful.Length}개가 완료됐습니다. 실패한 스트림은 평균 처리량 계산에 수신된 바이트만 반영했습니다."
            : target.PathKind == NetworkPathKind.Internal
                ? "내부망 DIRECT 다운로드 측정을 완료했습니다. 이 결과에는 대상 내부 서버의 처리 성능도 포함될 수 있습니다."
                : "회사 프록시를 포함한 외부 서비스 체감 다운로드 측정을 완료했습니다. 프록시 내부 장애를 단독으로 확정하지 않습니다.";

        return CreateTerminalResult(
            target,
            status,
            startedAt,
            totalBytes,
            averageMbps,
            averageTtfb,
            firstSuccessful.Response.HttpStatusCode,
            firstSuccessful.Response.ProxyWasUsed,
            successful.Length,
            redirectCount,
            firstSuccessful.FinalUri.AbsoluteUri,
            samples,
            firstSuccessful.Response.ResponseHeaders,
            partial ? "PARTIAL_STREAM_FAILURE" : null,
            message);
    }

    private static RequestChainResult ExecuteChain(
        MeasurementTargetDefinition target,
        Uri startUri,
        WinHttpRequestMethod method,
        long maxResponseBytes,
        Func<WinHttpRequestOptions, WinHttpRequestResult> transport,
        CancellationToken cancellationToken)
    {
        Uri current = startUri;
        int redirects = 0;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            WinHttpRequestOptions request = new(
                Url: current.AbsoluteUri,
                ExpectedPath: target.PathKind,
                Method: method,
                TimeoutMilliseconds: checked(target.TimeoutSeconds * 1000),
                MaxResponseBytes: maxResponseBytes,
                RequireExpectedPath: true);

            WinHttpRequestResult response = transport(request);
            if (response.Status != WinHttpRequestStatus.RedirectResponse)
            {
                return new RequestChainResult(response, current, redirects);
            }

            if (redirects >= target.MaxRedirects)
            {
                return new RequestChainResult(
                    response with
                    {
                        Status = WinHttpRequestStatus.RedirectLimitExceeded,
                        ErrorCode = "REDIRECT_LIMIT_EXCEEDED",
                        Message = $"최대 리다이렉트 수 {target.MaxRedirects}회에 도달해 요청을 중단했습니다."
                    },
                    current,
                    redirects);
            }

            RedirectValidationResult validation =
                RedirectTargetValidator.Evaluate(
                    current,
                    response.RedirectLocation,
                    target.PathKind);

            if (!validation.IsAllowed || validation.Destination is null)
            {
                return new RequestChainResult(
                    response with
                    {
                        Status = WinHttpRequestStatus.RedirectDenied,
                        ErrorCode = validation.ErrorCode,
                        Message = validation.Message
                    },
                    current,
                    redirects);
            }

            current = validation.Destination;
            redirects++;
        }
    }

    private static DownloadMeasurementResult FromFailure(
        MeasurementTargetDefinition target,
        DateTimeOffset startedAt,
        RequestChainResult failure,
        int streamsCompleted,
        int? redirectCountOverride = null)
    {
        MeasurementStatus status = failure.Response.Status switch
        {
            WinHttpRequestStatus.TimedOut => MeasurementStatus.TimedOut,
            WinHttpRequestStatus.Canceled => MeasurementStatus.Canceled,
            WinHttpRequestStatus.PathMismatch => MeasurementStatus.PathMismatch,
            WinHttpRequestStatus.ProxyAuthenticationUnsupported
                or WinHttpRequestStatus.ProxyAuthenticationFailed
                => MeasurementStatus.ProxyAuthenticationRequired,
            WinHttpRequestStatus.InvalidRequest
                or WinHttpRequestStatus.RedirectDenied
                or WinHttpRequestStatus.RedirectLimitExceeded
                => MeasurementStatus.Blocked,
            _ => MeasurementStatus.Failed
        };

        return CreateTerminalResult(
            target,
            status,
            startedAt,
            failure.Response.BytesReceived,
            averageMbps: null,
            failure.Response.TimeToFirstByte,
            failure.Response.HttpStatusCode,
            failure.Response.ProxyWasUsed,
            streamsCompleted,
            redirectCountOverride ?? failure.RedirectCount,
            failure.FinalUri.AbsoluteUri,
            failure.Response.ThroughputSamples,
            failure.Response.ResponseHeaders,
            failure.Response.ErrorCode ?? failure.Response.Status.ToString(),
            failure.Response.Message);
    }

    private static DownloadMeasurementResult CreateTerminalResult(
        MeasurementTargetDefinition target,
        MeasurementStatus status,
        DateTimeOffset startedAt,
        long bytesReceived,
        double? averageMbps,
        TimeSpan? timeToFirstByte,
        int? httpStatusCode,
        bool? proxyWasUsed,
        int streamsCompleted,
        int redirectCount,
        string finalUrl,
        IReadOnlyList<ThroughputSample> samples,
        IReadOnlyDictionary<string, string> responseHeaders,
        string? errorCode,
        string message)
    {
        return new DownloadMeasurementResult(
            TargetName: target.Name,
            PathKind: target.PathKind,
            Status: status,
            StartedAt: startedAt,
            CompletedAt: DateTimeOffset.UtcNow,
            BytesReceived: bytesReceived,
            AverageMbps: averageMbps,
            TimeToFirstByte: timeToFirstByte,
            HttpStatusCode: httpStatusCode,
            ProxyWasUsed: proxyWasUsed,
            StreamsRequested: target.Streams,
            StreamsCompleted: streamsCompleted,
            RedirectCount: redirectCount,
            FinalUrl: UrlDisplayFormatter.WithoutSensitiveQuery(finalUrl),
            Samples: samples,
            ResponseHeaders: responseHeaders,
            ErrorCode: errorCode,
            Message: message);
    }

    private static DownloadMeasurementResult CreateCanceled(
        MeasurementTargetDefinition target,
        DateTimeOffset? startedAt = null)
    {
        return CreateTerminalResult(
            target,
            MeasurementStatus.Canceled,
            startedAt ?? DateTimeOffset.UtcNow,
            bytesReceived: 0,
            averageMbps: null,
            timeToFirstByte: null,
            httpStatusCode: null,
            proxyWasUsed: null,
            streamsCompleted: 0,
            redirectCount: 0,
            finalUrl: target.Url,
            samples: [],
            responseHeaders: EmptyHeaders,
            errorCode: "MEASUREMENT_CANCELED",
            message: "사용자 취소 요청으로 측정을 중단했습니다.");
    }

    private static string? ValidatePathSemantics(
        MeasurementTargetDefinition target)
    {
        if (target.PathKind == NetworkPathKind.Internal && target.RequireProxy)
        {
            return "내부망 대상은 프록시 필수로 설정할 수 없습니다.";
        }

        if (target.PathKind == NetworkPathKind.External && target.RequireDirect)
        {
            return "외부망 대상은 DIRECT 필수로 설정할 수 없습니다.";
        }

        return null;
    }

    private static long[] AllocateBytes(long totalBytes, int streams)
    {
        long[] allocations = new long[streams];
        long quotient = totalBytes / streams;
        long remainder = totalBytes % streams;

        for (int index = 0; index < streams; index++)
        {
            allocations[index] = quotient + (index < remainder ? 1 : 0);
        }

        return allocations;
    }

    private static double CalculateMbps(long bytes, TimeSpan duration)
    {
        double seconds = Math.Max(duration.TotalSeconds, 0.001);
        return bytes * 8d / seconds / 1_000_000d;
    }

    private static TimeSpan? AverageTimeToFirstByte(
        IEnumerable<RequestChainResult> results)
    {
        long[] ticks = results
            .Select(result => result.Response.TimeToFirstByte?.Ticks)
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .ToArray();

        return ticks.Length == 0
            ? null
            : TimeSpan.FromTicks(checked((long)ticks.Average()));
    }

    private static int FailurePriority(WinHttpRequestStatus status) =>
        status switch
        {
            WinHttpRequestStatus.PathMismatch => 0,
            WinHttpRequestStatus.ProxyAuthenticationFailed => 1,
            WinHttpRequestStatus.ProxyAuthenticationUnsupported => 2,
            WinHttpRequestStatus.RedirectDenied => 3,
            WinHttpRequestStatus.RedirectLimitExceeded => 4,
            WinHttpRequestStatus.TimedOut => 5,
            _ => 10
        };

    private static IReadOnlyDictionary<string, string> EmptyHeaders { get; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    private sealed record RequestChainResult(
        WinHttpRequestResult Response,
        Uri FinalUri,
        int RedirectCount);
}
