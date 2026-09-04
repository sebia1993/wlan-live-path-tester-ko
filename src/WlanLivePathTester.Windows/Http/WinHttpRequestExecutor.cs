using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using WlanLivePathTester.Core.Http;
using WlanLivePathTester.Core.Measurements;
using WlanLivePathTester.Core.Models;
using WlanLivePathTester.Windows.Interop;
using WlanLivePathTester.Windows.Proxy;

namespace WlanLivePathTester.Windows.Http;

[SupportedOSPlatform("windows")]
public static class WinHttpRequestExecutor
{
    private static readonly string[] SelectedHeaderNames =
    [
        "Age",
        "Via",
        "Cache-Status",
        "X-Cache",
        "Content-Length",
        "Content-Range",
        "Location",
        "ETag",
        "Last-Modified"
    ];

    private const string UserAgent = "WlanLivePathTester/0.1";
    private const int MinimumTimeoutMilliseconds = 1000;
    private const int MaximumTimeoutMilliseconds = 300000;
    private const long MaximumResponseBytes = 1024L * 1024 * 1024;
    private const long MaximumChallengeBodyBytes = 1024L * 1024;
    private const int MaximumResendRequests = 2;
    private const int MaximumProxyAuthenticationAttempts = 1;
    private const int ReadBufferSize = 64 * 1024;
    private static readonly TimeSpan SampleInterval = TimeSpan.FromSeconds(1);

    public static WinHttpRequestResult Execute(WinHttpRequestOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.CancellationToken.IsCancellationRequested)
        {
            return Canceled(options.Url);
        }

        if (!OperatingSystem.IsWindows())
        {
            return Failure(
                WinHttpRequestStatus.UnsupportedPlatform,
                "Windows에서만 WinHTTP 요청을 실행할 수 있습니다.");
        }

        if (!TryValidate(options, out Uri? destination, out string validationMessage))
        {
            return Failure(WinHttpRequestStatus.InvalidRequest, validationMessage);
        }

        ResolvedProxyRoute resolved = ProxyRouteResolver.ResolveDetailed(
            destination!.AbsoluteUri,
            options.ExpectedPath,
            Math.Min(options.TimeoutMilliseconds, 30000));

        if (options.CancellationToken.IsCancellationRequested)
        {
            return Canceled(options.Url, route: resolved.Summary);
        }

        if (!resolved.Summary.IsSuccess)
        {
            return Failure(
                WinHttpRequestStatus.ProxyResolutionFailed,
                resolved.Summary.Message,
                resolved.Summary.Win32ErrorCode,
                route: resolved.Summary,
                finalUrl: options.Url);
        }

        if (options.RequireExpectedPath
            && resolved.Summary.Expectation == ProxyPathExpectation.Mismatch)
        {
            return Failure(
                WinHttpRequestStatus.PathMismatch,
                "대상 URL의 프록시 경로가 선택한 내부망·외부망 기대 경로와 일치하지 않아 요청을 보내지 않았습니다.",
                route: resolved.Summary,
                finalUrl: options.Url);
        }

        if (resolved.Selection.RouteKind == ProxyRouteKind.Direct)
        {
            return ExecuteAttempt(options, destination, resolved.Summary, proxyEndpoint: null);
        }

        WinHttpRequestResult? lastFailure = null;
        foreach (string proxyEndpoint in resolved.Selection.ProxyUris)
        {
            if (options.CancellationToken.IsCancellationRequested)
            {
                return Canceled(options.Url, route: resolved.Summary, proxyWasUsed: true);
            }

            WinHttpRequestResult result = ExecuteAttempt(
                options,
                destination,
                resolved.Summary,
                proxyEndpoint);

            if (result.Status is not WinHttpRequestStatus.NetworkError
                and not WinHttpRequestStatus.TimedOut)
            {
                return result;
            }

            lastFailure = result;
        }

        return lastFailure ?? Failure(
            WinHttpRequestStatus.ProxyResolutionFailed,
            "사용할 수 있는 프록시 후보가 없습니다.",
            route: resolved.Summary,
            finalUrl: options.Url);
    }

    internal static WinHttpRequestResult ExecuteExplicitForSmoke(
        WinHttpRequestOptions options,
        string? proxyEndpoint)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.CancellationToken.IsCancellationRequested)
        {
            return Canceled(
                options.Url,
                proxyWasUsed: !string.IsNullOrWhiteSpace(proxyEndpoint));
        }

        if (!OperatingSystem.IsWindows())
        {
            return Failure(
                WinHttpRequestStatus.UnsupportedPlatform,
                "Windows에서만 WinHTTP 요청을 실행할 수 있습니다.");
        }

        if (!TryValidate(options, out Uri? destination, out string validationMessage))
        {
            return Failure(WinHttpRequestStatus.InvalidRequest, validationMessage);
        }

        bool usesProxy = !string.IsNullOrWhiteSpace(proxyEndpoint);
        ProxyRouteKind routeKind = usesProxy ? ProxyRouteKind.Proxy : ProxyRouteKind.Direct;
        ProxyRouteResolution route = new(
            Status: ProxyResolutionStatus.Success,
            RouteKind: routeKind,
            Source: usesProxy ? ProxyConfigurationSource.Manual : ProxyConfigurationSource.None,
            Expectation: ProxyRouteExpectationEvaluator.Evaluate(options.ExpectedPath, routeKind),
            ProxyCandidateCount: usesProxy ? 1 : 0,
            HasDirectFallback: false,
            WasBypassed: false,
            AutoLogonRetried: false,
            NetworkLookupPerformed: false,
            InvalidDirectiveCount: 0,
            Win32ErrorCode: null,
            Message: "합성 Windows smoke test 경로입니다.");

        return ExecuteAttempt(options, destination!, route, proxyEndpoint);
    }

    private static WinHttpRequestResult ExecuteAttempt(
        WinHttpRequestOptions options,
        Uri destination,
        ProxyRouteResolution route,
        string? proxyEndpoint)
    {
        bool usesProxy = !string.IsNullOrWhiteSpace(proxyEndpoint);
        if (options.CancellationToken.IsCancellationRequested)
        {
            return Canceled(options.Url, route, usesProxy);
        }

        string? proxyName;
        try
        {
            proxyName = usesProxy ? NormalizeProxyName(proxyEndpoint!) : null;
        }
        catch (ArgumentException)
        {
            return Failure(
                WinHttpRequestStatus.InvalidRequest,
                "프록시 주소 형식을 해석하지 못해 요청을 보내지 않았습니다.",
                proxyWasUsed: usesProxy,
                route: route,
                finalUrl: options.Url);
        }

        nint sessionRaw = WinHttpNative.WinHttpOpenWithProxy(
            UserAgent,
            usesProxy ? WinHttpNative.AccessTypeNamedProxy : WinHttpNative.AccessTypeNoProxy,
            proxyName,
            proxyBypass: null,
            flags: 0);

        if (sessionRaw == nint.Zero)
        {
            int error = Marshal.GetLastWin32Error();
            return options.CancellationToken.IsCancellationRequested
                ? Canceled(options.Url, route, usesProxy)
                : FailureFromNative(
                    error,
                    "WinHTTP 세션을 열지 못했습니다.",
                    usesProxy,
                    route,
                    options.Url);
        }

        using SafeWinHttpHandle session = SafeWinHttpHandle.FromRaw(sessionRaw);

        if (!WinHttpNative.WinHttpSetTimeouts(
                session.DangerousGetHandle(),
                options.TimeoutMilliseconds,
                options.TimeoutMilliseconds,
                options.TimeoutMilliseconds,
                options.TimeoutMilliseconds))
        {
            int error = Marshal.GetLastWin32Error();
            return options.CancellationToken.IsCancellationRequested
                ? Canceled(options.Url, route, usesProxy)
                : FailureFromNative(
                    error,
                    "WinHTTP 요청 제한 시간을 설정하지 못했습니다.",
                    usesProxy,
                    route,
                    options.Url);
        }

        nint connectRaw = WinHttpNative.WinHttpConnect(
            session.DangerousGetHandle(),
            destination.IdnHost,
            checked((ushort)destination.Port),
            reserved: 0);

        if (connectRaw == nint.Zero)
        {
            int error = Marshal.GetLastWin32Error();
            return options.CancellationToken.IsCancellationRequested
                ? Canceled(options.Url, route, usesProxy)
                : FailureFromNative(
                    error,
                    "대상 호스트 연결 핸들을 만들지 못했습니다.",
                    usesProxy,
                    route,
                    options.Url);
        }

        using SafeWinHttpHandle connect = SafeWinHttpHandle.FromRaw(connectRaw);
        uint requestFlags = destination.Scheme.Equals(
            Uri.UriSchemeHttps,
            StringComparison.OrdinalIgnoreCase)
            ? WinHttpNative.FlagSecure
            : 0;

        string objectName = string.IsNullOrEmpty(destination.PathAndQuery)
            ? "/"
            : destination.PathAndQuery;

        nint requestRaw = WinHttpNative.WinHttpOpenRequest(
            connect.DangerousGetHandle(),
            options.Method == WinHttpRequestMethod.Head ? "HEAD" : "GET",
            objectName,
            nint.Zero,
            nint.Zero,
            nint.Zero,
            requestFlags);

        if (requestRaw == nint.Zero)
        {
            int error = Marshal.GetLastWin32Error();
            return options.CancellationToken.IsCancellationRequested
                ? Canceled(options.Url, route, usesProxy)
                : FailureFromNative(
                    error,
                    "WinHTTP 요청 핸들을 만들지 못했습니다.",
                    usesProxy,
                    route,
                    options.Url);
        }

        using SafeWinHttpHandle request = SafeWinHttpHandle.FromRaw(requestRaw);
        using CancellationTokenRegistration cancellationRegistration =
            options.CancellationToken.Register(request.CancelPendingOperation);

        if (options.CancellationToken.IsCancellationRequested)
        {
            return Canceled(options.Url, route, usesProxy);
        }

        uint redirectPolicy = WinHttpNative.RedirectPolicyNever;
        if (!WinHttpNative.WinHttpSetOption(
                request.DangerousGetHandle(),
                WinHttpNative.OptionRedirectPolicy,
                ref redirectPolicy,
                sizeof(uint)))
        {
            int error = Marshal.GetLastWin32Error();
            return options.CancellationToken.IsCancellationRequested
                ? Canceled(options.Url, route, usesProxy)
                : FailureFromNative(
                    error,
                    "자동 리다이렉트 차단 정책을 설정하지 못했습니다.",
                    usesProxy,
                    route,
                    options.Url);
        }

        WinHttpRequestResult result = SendAndReceive(options, request, usesProxy, route);
        return options.CancellationToken.IsCancellationRequested
            ? AsCanceled(result, options.Url)
            : result;
    }

    private static WinHttpRequestResult SendAndReceive(
        WinHttpRequestOptions options,
        SafeWinHttpHandle request,
        bool usesProxy,
        ProxyRouteResolution route)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        ProxyAuthenticationChoice authenticationChoice = ProxyAuthenticationChoice.None;
        uint authenticationScheme = 0;
        int authenticationAttempts = 0;
        int resendRequests = 0;

        while (true)
        {
            if (options.CancellationToken.IsCancellationRequested)
            {
                return FinishCanceled(
                    stopwatch,
                    usesProxy,
                    route,
                    authenticationChoice,
                    authenticationAttempts,
                    finalUrl: options.Url);
            }

            if (authenticationChoice != ProxyAuthenticationChoice.None)
            {
                if (!WinHttpNative.WinHttpSetCredentials(
                        request.DangerousGetHandle(),
                        ProxyAuthenticationPolicy.AuthTargetProxy,
                        authenticationScheme,
                        userName: null,
                        password: null,
                        authParams: nint.Zero))
                {
                    int error = Marshal.GetLastWin32Error();
                    return options.CancellationToken.IsCancellationRequested
                        ? FinishCanceled(
                            stopwatch,
                            usesProxy,
                            route,
                            authenticationChoice,
                            authenticationAttempts,
                            finalUrl: options.Url)
                        : FinishFailure(
                            stopwatch,
                            WinHttpRequestStatus.NetworkError,
                            "현재 Windows 사용자 자격 증명을 프록시 요청에 적용하지 못했습니다.",
                            error,
                            usesProxy,
                            route,
                            authenticationChoice,
                            authenticationAttempts,
                            finalUrl: options.Url);
                }
            }

            if (!WinHttpNative.WinHttpSendRequest(
                    request.DangerousGetHandle(),
                    nint.Zero,
                    0,
                    nint.Zero,
                    0,
                    0,
                    0))
            {
                int error = Marshal.GetLastWin32Error();
                return options.CancellationToken.IsCancellationRequested
                    ? FinishCanceled(
                        stopwatch,
                        usesProxy,
                        route,
                        authenticationChoice,
                        authenticationAttempts,
                        finalUrl: options.Url)
                    : FinishNativeFailure(
                        stopwatch,
                        error,
                        "WinHTTP 요청을 전송하지 못했습니다.",
                        usesProxy,
                        route,
                        authenticationChoice,
                        authenticationAttempts,
                        finalUrl: options.Url);
            }

            if (!WinHttpNative.WinHttpReceiveResponse(
                    request.DangerousGetHandle(),
                    nint.Zero))
            {
                int error = Marshal.GetLastWin32Error();
                if (options.CancellationToken.IsCancellationRequested)
                {
                    return FinishCanceled(
                        stopwatch,
                        usesProxy,
                        route,
                        authenticationChoice,
                        authenticationAttempts,
                        finalUrl: options.Url);
                }

                if (error == WinHttpNative.ErrorWinHttpResendRequest
                    && resendRequests < MaximumResendRequests)
                {
                    resendRequests++;
                    continue;
                }

                return FinishNativeFailure(
                    stopwatch,
                    error,
                    "WinHTTP 응답을 받지 못했습니다.",
                    usesProxy,
                    route,
                    authenticationChoice,
                    authenticationAttempts,
                    finalUrl: options.Url);
            }

            if (options.CancellationToken.IsCancellationRequested)
            {
                return FinishCanceled(
                    stopwatch,
                    usesProxy,
                    route,
                    authenticationChoice,
                    authenticationAttempts,
                    finalUrl: options.Url);
            }

            if (!TryReadStatusCode(request, out int statusCode, out int statusError))
            {
                return options.CancellationToken.IsCancellationRequested
                    ? FinishCanceled(
                        stopwatch,
                        usesProxy,
                        route,
                        authenticationChoice,
                        authenticationAttempts,
                        finalUrl: options.Url)
                    : FinishNativeFailure(
                        stopwatch,
                        statusError,
                        "HTTP 상태 코드를 읽지 못했습니다.",
                        usesProxy,
                        route,
                        authenticationChoice,
                        authenticationAttempts,
                        finalUrl: options.Url);
            }

            if (statusCode == 407)
            {
                BodyReadResult challengeBody = ReadBody(
                    request,
                    MaximumChallengeBodyBytes,
                    options.CancellationToken);
                if (!challengeBody.Success)
                {
                    return options.CancellationToken.IsCancellationRequested
                        ? FinishCanceled(
                            stopwatch,
                            usesProxy,
                            route,
                            authenticationChoice,
                            authenticationAttempts,
                            challengeBody.BytesRead,
                            options.Url)
                        : FinishNativeFailure(
                            stopwatch,
                            challengeBody.Win32ErrorCode,
                            "프록시 인증 응답 본문을 정리하지 못했습니다.",
                            usesProxy,
                            route,
                            authenticationChoice,
                            authenticationAttempts,
                            bytesReceived: challengeBody.BytesRead,
                            finalUrl: options.Url);
                }

                if (!ProxyAuthenticationPolicy.CanAttempt(
                        authenticationAttempts,
                        MaximumProxyAuthenticationAttempts))
                {
                    return FinishFailure(
                        stopwatch,
                        WinHttpRequestStatus.ProxyAuthenticationFailed,
                        "현재 Windows 사용자 자격 증명으로 프록시 인증을 완료하지 못했습니다. 반복 407을 중단했습니다.",
                        null,
                        usesProxy,
                        route,
                        authenticationChoice,
                        authenticationAttempts,
                        statusCode,
                        finalUrl: options.Url,
                        errorCode: "PROXY_AUTHENTICATION_FAILED");
                }

                if (!WinHttpNative.WinHttpQueryAuthSchemes(
                        request.DangerousGetHandle(),
                        out uint supportedSchemes,
                        out uint firstScheme,
                        out uint authTarget))
                {
                    int error = Marshal.GetLastWin32Error();
                    return options.CancellationToken.IsCancellationRequested
                        ? FinishCanceled(
                            stopwatch,
                            usesProxy,
                            route,
                            authenticationChoice,
                            authenticationAttempts,
                            finalUrl: options.Url)
                        : FinishNativeFailure(
                            stopwatch,
                            error,
                            "프록시가 제공한 인증 방식을 확인하지 못했습니다.",
                            usesProxy,
                            route,
                            authenticationChoice,
                            authenticationAttempts,
                            finalUrl: options.Url);
                }

                ProxyAuthenticationDecision decision = ProxyAuthenticationPolicy.Select(
                    supportedSchemes,
                    firstScheme,
                    authTarget);

                if (decision.Status != ProxyAuthenticationDecisionStatus.Selected)
                {
                    return FinishFailure(
                        stopwatch,
                        WinHttpRequestStatus.ProxyAuthenticationUnsupported,
                        decision.Message,
                        null,
                        usesProxy,
                        route,
                        ProxyAuthenticationChoice.None,
                        authenticationAttempts,
                        statusCode,
                        finalUrl: options.Url,
                        errorCode: "PROXY_AUTHENTICATION_UNSUPPORTED");
                }

                authenticationChoice = decision.Choice;
                authenticationScheme = decision.NativeScheme;
                authenticationAttempts++;
                continue;
            }

            TimeSpan timeToFirstByte = stopwatch.Elapsed;
            IReadOnlyDictionary<string, string> responseHeaders =
                ReadSelectedHeaders(request);
            string? redirectLocation = GetHeader(responseHeaders, "Location");

            if (statusCode == 401)
            {
                return FinishFailure(
                    stopwatch,
                    WinHttpRequestStatus.ServerAuthenticationRequired,
                    "원격 서버 인증이 필요합니다. 이 도구는 웹사이트 자격 증명이나 브라우저 쿠키를 사용하지 않습니다.",
                    null,
                    usesProxy,
                    route,
                    authenticationChoice,
                    authenticationAttempts,
                    statusCode,
                    finalUrl: options.Url,
                    timeToFirstByte: timeToFirstByte,
                    responseHeaders: responseHeaders,
                    errorCode: "SERVER_AUTHENTICATION_REQUIRED");
            }

            if (statusCode is >= 300 and < 400)
            {
                return FinishFailure(
                    stopwatch,
                    WinHttpRequestStatus.RedirectResponse,
                    "리다이렉트 응답을 받았습니다. 상위 측정 계층에서 새 URL을 다시 검증합니다.",
                    null,
                    usesProxy,
                    route,
                    authenticationChoice,
                    authenticationAttempts,
                    statusCode,
                    finalUrl: options.Url,
                    timeToFirstByte: timeToFirstByte,
                    redirectLocation: redirectLocation,
                    responseHeaders: responseHeaders,
                    errorCode: "HTTP_REDIRECT");
            }

            if (statusCode is < 200 or >= 400)
            {
                string errorCode = statusCode switch
                {
                    403 => "HTTP_403",
                    407 => "HTTP_407",
                    429 => "HTTP_429",
                    _ => $"HTTP_{statusCode}"
                };

                return FinishFailure(
                    stopwatch,
                    WinHttpRequestStatus.HttpErrorResponse,
                    $"HTTP 상태 {statusCode} 응답을 받았습니다.",
                    null,
                    usesProxy,
                    route,
                    authenticationChoice,
                    authenticationAttempts,
                    statusCode,
                    finalUrl: options.Url,
                    timeToFirstByte: timeToFirstByte,
                    responseHeaders: responseHeaders,
                    errorCode: errorCode);
            }

            BodyReadResult body = options.Method == WinHttpRequestMethod.Get
                ? ReadBody(request, options.MaxResponseBytes, options.CancellationToken)
                : BodyReadResult.Empty;

            if (!body.Success)
            {
                return options.CancellationToken.IsCancellationRequested
                    ? FinishCanceled(
                        stopwatch,
                        usesProxy,
                        route,
                        authenticationChoice,
                        authenticationAttempts,
                        body.BytesRead,
                        options.Url,
                        timeToFirstByte,
                        responseHeaders)
                    : FinishNativeFailure(
                        stopwatch,
                        body.Win32ErrorCode,
                        "응답 본문을 읽지 못했습니다.",
                        usesProxy,
                        route,
                        authenticationChoice,
                        authenticationAttempts,
                        statusCode,
                        body.BytesRead,
                        options.Url,
                        timeToFirstByte,
                        responseHeaders);
            }

            if (options.CancellationToken.IsCancellationRequested)
            {
                return FinishCanceled(
                    stopwatch,
                    usesProxy,
                    route,
                    authenticationChoice,
                    authenticationAttempts,
                    body.BytesRead,
                    options.Url,
                    timeToFirstByte,
                    responseHeaders);
            }

            stopwatch.Stop();
            WinHttpRequestStatus completedStatus = body.LimitReached
                ? WinHttpRequestStatus.ResponseLimitReached
                : WinHttpRequestStatus.Success;

            return new WinHttpRequestResult(
                Status: completedStatus,
                HttpStatusCode: statusCode,
                ProxyWasUsed: usesProxy,
                AuthenticationMethod: ToPublicAuthenticationMethod(authenticationChoice),
                AuthenticationAttempts: authenticationAttempts,
                BytesReceived: body.BytesRead,
                Duration: stopwatch.Elapsed,
                ResponseWasTruncated: body.LimitReached,
                Win32ErrorCode: null,
                Route: route,
                Message: body.LimitReached
                    ? "설정된 최대 수신 바이트에 도달해 응답 본문 읽기를 중단했습니다. 수신 데이터는 저장하지 않았습니다."
                    : "WinHTTP 요청을 완료했습니다. 수신 데이터는 저장하지 않았습니다.")
            {
                TimeToFirstByte = timeToFirstByte,
                FinalUrl = options.Url,
                ResponseHeaders = responseHeaders,
                ThroughputSamples = body.Samples
            };
        }
    }

    private static bool TryReadStatusCode(
        SafeWinHttpHandle request,
        out int statusCode,
        out int errorCode)
    {
        uint status = 0;
        uint size = sizeof(uint);
        bool success = WinHttpNative.WinHttpQueryHeaders(
            request.DangerousGetHandle(),
            WinHttpNative.QueryStatusCode | WinHttpNative.QueryFlagNumber,
            nint.Zero,
            out status,
            ref size,
            nint.Zero);

        statusCode = success ? checked((int)status) : 0;
        errorCode = success ? 0 : Marshal.GetLastWin32Error();
        return success;
    }

    private static IReadOnlyDictionary<string, string> ReadSelectedHeaders(
        SafeWinHttpHandle request)
    {
        Dictionary<string, string> headers =
            new(StringComparer.OrdinalIgnoreCase);

        foreach (string headerName in SelectedHeaderNames)
        {
            string? value = TryReadHeader(request, headerName);
            if (!string.IsNullOrWhiteSpace(value))
            {
                headers[headerName] = value.Trim();
            }
        }

        return headers;
    }

    private static string? TryReadHeader(
        SafeWinHttpHandle request,
        string headerName)
    {
        uint bufferLength = 0;
        _ = WinHttpNative.WinHttpQueryHeadersText(
            request.DangerousGetHandle(),
            WinHttpNative.QueryCustom,
            headerName,
            nint.Zero,
            ref bufferLength,
            nint.Zero);

        int firstError = Marshal.GetLastWin32Error();
        if (firstError != WinHttpNative.ErrorInsufficientBuffer
            || bufferLength == 0
            || bufferLength > 1024 * 1024)
        {
            return null;
        }

        nint buffer = Marshal.AllocHGlobal(checked((int)bufferLength));
        try
        {
            if (!WinHttpNative.WinHttpQueryHeadersText(
                    request.DangerousGetHandle(),
                    WinHttpNative.QueryCustom,
                    headerName,
                    buffer,
                    ref bufferLength,
                    nint.Zero))
            {
                return null;
            }

            return Marshal.PtrToStringUni(buffer);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static string? GetHeader(
        IReadOnlyDictionary<string, string> headers,
        string name) =>
        headers.TryGetValue(name, out string? value) ? value : null;

    private static unsafe BodyReadResult ReadBody(
        SafeWinHttpHandle request,
        long maximumBytes,
        CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[ReadBufferSize];
        List<ThroughputSample> samples = [];
        Stopwatch bodyStopwatch = Stopwatch.StartNew();
        long total = 0;
        long previousSampleBytes = 0;
        TimeSpan previousSampleTime = TimeSpan.Zero;

        while (true)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                AddFinalSample(
                    samples,
                    bodyStopwatch.Elapsed,
                    total,
                    previousSampleTime,
                    previousSampleBytes);

                return new BodyReadResult(
                    Success: false,
                    BytesRead: total,
                    LimitReached: false,
                    Win32ErrorCode: 0,
                    Samples: samples);
            }

            long remaining = maximumBytes - total;
            if (remaining <= 0)
            {
                AddFinalSample(
                    samples,
                    bodyStopwatch.Elapsed,
                    total,
                    previousSampleTime,
                    previousSampleBytes);

                return new BodyReadResult(
                    Success: true,
                    BytesRead: total,
                    LimitReached: true,
                    Win32ErrorCode: 0,
                    Samples: samples);
            }

            uint bytesToRead = checked((uint)Math.Min(buffer.Length, remaining));
            uint bytesRead;
            fixed (byte* pointer = buffer)
            {
                if (!WinHttpNative.WinHttpReadData(
                        request.DangerousGetHandle(),
                        (nint)pointer,
                        bytesToRead,
                        out bytesRead))
                {
                    AddFinalSample(
                        samples,
                        bodyStopwatch.Elapsed,
                        total,
                        previousSampleTime,
                        previousSampleBytes);

                    return new BodyReadResult(
                        Success: false,
                        BytesRead: total,
                        LimitReached: false,
                        Win32ErrorCode: Marshal.GetLastWin32Error(),
                        Samples: samples);
                }
            }

            if (bytesRead == 0)
            {
                AddFinalSample(
                    samples,
                    bodyStopwatch.Elapsed,
                    total,
                    previousSampleTime,
                    previousSampleBytes);

                return new BodyReadResult(
                    Success: true,
                    BytesRead: total,
                    LimitReached: false,
                    Win32ErrorCode: 0,
                    Samples: samples);
            }

            total = checked(total + bytesRead);
            TimeSpan elapsed = bodyStopwatch.Elapsed;
            if (elapsed - previousSampleTime >= SampleInterval)
            {
                AddSample(
                    samples,
                    elapsed,
                    total - previousSampleBytes,
                    elapsed - previousSampleTime);
                previousSampleBytes = total;
                previousSampleTime = elapsed;
            }
        }
    }

    private static void AddFinalSample(
        ICollection<ThroughputSample> samples,
        TimeSpan elapsed,
        long totalBytes,
        TimeSpan previousTime,
        long previousBytes)
    {
        long intervalBytes = totalBytes - previousBytes;
        if (intervalBytes <= 0)
        {
            return;
        }

        AddSample(samples, elapsed, intervalBytes, elapsed - previousTime);
    }

    private static void AddSample(
        ICollection<ThroughputSample> samples,
        TimeSpan offset,
        long intervalBytes,
        TimeSpan interval)
    {
        double seconds = Math.Max(interval.TotalSeconds, 0.001);
        double mbps = intervalBytes * 8d / seconds / 1_000_000d;
        samples.Add(new ThroughputSample(
            StreamIndex: 0,
            Offset: offset,
            IntervalBytes: intervalBytes,
            Mbps: mbps));
    }

    private static bool TryValidate(
        WinHttpRequestOptions options,
        out Uri? destination,
        out string message)
    {
        destination = null;
        message = string.Empty;

        if (!Uri.TryCreate(options.Url, UriKind.Absolute, out Uri? parsed)
            || (!parsed.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                && !parsed.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
        {
            message = "HTTP 또는 HTTPS 절대 URL만 요청할 수 있습니다.";
            return false;
        }

        if (!string.IsNullOrEmpty(parsed.UserInfo))
        {
            message = "URL에 사용자 이름이나 비밀번호를 포함할 수 없습니다.";
            return false;
        }

        if (!string.IsNullOrEmpty(parsed.Fragment))
        {
            message = "URL fragment를 포함할 수 없습니다.";
            return false;
        }

        if (!Enum.IsDefined(options.Method))
        {
            message = "HEAD 또는 GET 요청만 허용합니다.";
            return false;
        }

        if (options.TimeoutMilliseconds is < MinimumTimeoutMilliseconds or > MaximumTimeoutMilliseconds)
        {
            message = $"요청 제한 시간은 {MinimumTimeoutMilliseconds}~{MaximumTimeoutMilliseconds}ms 범위여야 합니다.";
            return false;
        }

        if (options.MaxResponseBytes is < 0 or > MaximumResponseBytes)
        {
            message = $"최대 응답 크기는 0~{MaximumResponseBytes}바이트 범위여야 합니다.";
            return false;
        }

        if (options.Method == WinHttpRequestMethod.Get && options.MaxResponseBytes == 0)
        {
            message = "GET 요청의 최대 응답 크기는 1바이트 이상이어야 합니다.";
            return false;
        }

        destination = parsed;
        return true;
    }

    private static string NormalizeProxyName(string proxyEndpoint)
    {
        if (!Uri.TryCreate(proxyEndpoint, UriKind.Absolute, out Uri? proxy))
        {
            throw new ArgumentException("프록시 주소 형식을 해석하지 못했습니다.", nameof(proxyEndpoint));
        }

        string host = proxy.HostNameType == UriHostNameType.IPv6
            ? $"[{proxy.IdnHost}]"
            : proxy.IdnHost;
        return $"{host}:{proxy.Port}";
    }

    private static WinHttpRequestResult FailureFromNative(
        int errorCode,
        string context,
        bool proxyWasUsed,
        ProxyRouteResolution? route,
        string finalUrl)
    {
        return Failure(
            errorCode == WinHttpNative.ErrorWinHttpTimeout
                ? WinHttpRequestStatus.TimedOut
                : WinHttpRequestStatus.NetworkError,
            $"{context} WinHTTP 오류 {errorCode}: {new Win32Exception(errorCode).Message}",
            errorCode,
            proxyWasUsed,
            route,
            finalUrl,
            errorCode: $"WINHTTP_{errorCode}");
    }

    private static WinHttpRequestResult FinishNativeFailure(
        Stopwatch stopwatch,
        int errorCode,
        string context,
        bool proxyWasUsed,
        ProxyRouteResolution route,
        ProxyAuthenticationChoice authenticationChoice,
        int authenticationAttempts,
        int? httpStatusCode = null,
        long bytesReceived = 0,
        string finalUrl = "",
        TimeSpan? timeToFirstByte = null,
        IReadOnlyDictionary<string, string>? responseHeaders = null)
    {
        return FinishFailure(
            stopwatch,
            errorCode == WinHttpNative.ErrorWinHttpTimeout
                ? WinHttpRequestStatus.TimedOut
                : WinHttpRequestStatus.NetworkError,
            $"{context} WinHTTP 오류 {errorCode}: {new Win32Exception(errorCode).Message}",
            errorCode,
            proxyWasUsed,
            route,
            authenticationChoice,
            authenticationAttempts,
            httpStatusCode,
            bytesReceived,
            finalUrl,
            timeToFirstByte,
            responseHeaders: responseHeaders,
            errorCode: $"WINHTTP_{errorCode}");
    }

    private static WinHttpRequestResult FinishCanceled(
        Stopwatch stopwatch,
        bool proxyWasUsed,
        ProxyRouteResolution route,
        ProxyAuthenticationChoice authenticationChoice,
        int authenticationAttempts,
        long bytesReceived = 0,
        string finalUrl = "",
        TimeSpan? timeToFirstByte = null,
        IReadOnlyDictionary<string, string>? responseHeaders = null)
    {
        return FinishFailure(
            stopwatch,
            WinHttpRequestStatus.Canceled,
            "사용자 취소 요청으로 현재 WinHTTP 요청 핸들을 닫고 측정을 중단했습니다.",
            null,
            proxyWasUsed,
            route,
            authenticationChoice,
            authenticationAttempts,
            bytesReceived: bytesReceived,
            finalUrl: finalUrl,
            timeToFirstByte: timeToFirstByte,
            responseHeaders: responseHeaders,
            errorCode: "MEASUREMENT_CANCELED");
    }

    private static WinHttpRequestResult FinishFailure(
        Stopwatch stopwatch,
        WinHttpRequestStatus status,
        string message,
        int? win32ErrorCode,
        bool proxyWasUsed,
        ProxyRouteResolution route,
        ProxyAuthenticationChoice authenticationChoice,
        int authenticationAttempts,
        int? httpStatusCode = null,
        long bytesReceived = 0,
        string finalUrl = "",
        TimeSpan? timeToFirstByte = null,
        string? redirectLocation = null,
        IReadOnlyDictionary<string, string>? responseHeaders = null,
        string? errorCode = null)
    {
        stopwatch.Stop();
        return new WinHttpRequestResult(
            Status: status,
            HttpStatusCode: httpStatusCode,
            ProxyWasUsed: proxyWasUsed,
            AuthenticationMethod: ToPublicAuthenticationMethod(authenticationChoice),
            AuthenticationAttempts: authenticationAttempts,
            BytesReceived: bytesReceived,
            Duration: stopwatch.Elapsed,
            ResponseWasTruncated: false,
            Win32ErrorCode: win32ErrorCode,
            Route: route,
            Message: message)
        {
            TimeToFirstByte = timeToFirstByte,
            FinalUrl = finalUrl,
            RedirectLocation = redirectLocation,
            ResponseHeaders = responseHeaders ?? EmptyHeaders,
            ErrorCode = errorCode
        };
    }

    private static WinHttpRequestResult Canceled(
        string finalUrl,
        ProxyRouteResolution? route = null,
        bool proxyWasUsed = false)
    {
        return Failure(
            WinHttpRequestStatus.Canceled,
            "사용자 취소 요청으로 측정을 시작하지 않았거나 현재 요청을 중단했습니다.",
            proxyWasUsed: proxyWasUsed,
            route: route,
            finalUrl: finalUrl,
            errorCode: "MEASUREMENT_CANCELED");
    }

    private static WinHttpRequestResult AsCanceled(
        WinHttpRequestResult result,
        string fallbackFinalUrl)
    {
        return result with
        {
            Status = WinHttpRequestStatus.Canceled,
            Message = "사용자 취소 요청으로 현재 WinHTTP 요청 핸들을 닫고 측정을 중단했습니다.",
            FinalUrl = string.IsNullOrWhiteSpace(result.FinalUrl)
                ? fallbackFinalUrl
                : result.FinalUrl,
            ErrorCode = "MEASUREMENT_CANCELED"
        };
    }

    private static WinHttpRequestResult Failure(
        WinHttpRequestStatus status,
        string message,
        int? win32ErrorCode = null,
        bool proxyWasUsed = false,
        ProxyRouteResolution? route = null,
        string finalUrl = "",
        string? errorCode = null)
    {
        return new WinHttpRequestResult(
            Status: status,
            HttpStatusCode: null,
            ProxyWasUsed: proxyWasUsed,
            AuthenticationMethod: ProxyAuthenticationMethod.None,
            AuthenticationAttempts: 0,
            BytesReceived: 0,
            Duration: TimeSpan.Zero,
            ResponseWasTruncated: false,
            Win32ErrorCode: win32ErrorCode,
            Route: route,
            Message: message)
        {
            FinalUrl = finalUrl,
            ErrorCode = errorCode,
            ResponseHeaders = EmptyHeaders,
            ThroughputSamples = EmptySamples
        };
    }

    private static ProxyAuthenticationMethod ToPublicAuthenticationMethod(
        ProxyAuthenticationChoice choice)
    {
        return choice switch
        {
            ProxyAuthenticationChoice.Negotiate => ProxyAuthenticationMethod.Negotiate,
            ProxyAuthenticationChoice.Ntlm => ProxyAuthenticationMethod.Ntlm,
            _ => ProxyAuthenticationMethod.None
        };
    }

    private static IReadOnlyDictionary<string, string> EmptyHeaders { get; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    private static IReadOnlyList<ThroughputSample> EmptySamples { get; } =
        Array.Empty<ThroughputSample>();

    private sealed record BodyReadResult(
        bool Success,
        long BytesRead,
        bool LimitReached,
        int Win32ErrorCode,
        IReadOnlyList<ThroughputSample> Samples)
    {
        internal static BodyReadResult Empty { get; } = new(
            Success: true,
            BytesRead: 0,
            LimitReached: false,
            Win32ErrorCode: 0,
            Samples: EmptySamples);
    }
}
