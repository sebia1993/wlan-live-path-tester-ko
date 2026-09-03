using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using WlanLivePathTester.Core.Http;
using WlanLivePathTester.Core.Models;
using WlanLivePathTester.Windows.Interop;
using WlanLivePathTester.Windows.Proxy;

namespace WlanLivePathTester.Windows.Http;

[SupportedOSPlatform("windows")]
public static class WinHttpRequestExecutor
{
    private const string UserAgent = "WlanLivePathTester/0.1";
    private const int MinimumTimeoutMilliseconds = 1000;
    private const int MaximumTimeoutMilliseconds = 300000;
    private const long MaximumResponseBytes = 1024L * 1024 * 1024;
    private const long MaximumChallengeBodyBytes = 1024L * 1024;
    private const int MaximumResendRequests = 2;
    private const int MaximumProxyAuthenticationAttempts = 1;
    private const int ReadBufferSize = 64 * 1024;

    public static WinHttpRequestResult Execute(WinHttpRequestOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

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

        if (!resolved.Summary.IsSuccess)
        {
            return Failure(
                WinHttpRequestStatus.ProxyResolutionFailed,
                resolved.Summary.Message,
                resolved.Summary.Win32ErrorCode,
                route: resolved.Summary);
        }

        if (options.RequireExpectedPath
            && resolved.Summary.Expectation == ProxyPathExpectation.Mismatch)
        {
            return Failure(
                WinHttpRequestStatus.PathMismatch,
                "대상 URL의 프록시 경로가 선택한 내부망·외부망 기대 경로와 일치하지 않아 요청을 보내지 않았습니다.",
                route: resolved.Summary);
        }

        if (resolved.Selection.RouteKind == ProxyRouteKind.Direct)
        {
            return ExecuteAttempt(options, destination, resolved.Summary, proxyEndpoint: null);
        }

        WinHttpRequestResult? lastFailure = null;
        foreach (string proxyEndpoint in resolved.Selection.ProxyUris)
        {
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
            route: resolved.Summary);
    }

    internal static WinHttpRequestResult ExecuteExplicitForSmoke(
        WinHttpRequestOptions options,
        string? proxyEndpoint)
    {
        ArgumentNullException.ThrowIfNull(options);

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
                route: route);
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
            return FailureFromNative(
                error,
                "WinHTTP 세션을 열지 못했습니다.",
                usesProxy,
                route);
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
            return FailureFromNative(
                error,
                "WinHTTP 요청 제한 시간을 설정하지 못했습니다.",
                usesProxy,
                route);
        }

        nint connectRaw = WinHttpNative.WinHttpConnect(
            session.DangerousGetHandle(),
            destination.IdnHost,
            checked((ushort)destination.Port),
            reserved: 0);

        if (connectRaw == nint.Zero)
        {
            int error = Marshal.GetLastWin32Error();
            return FailureFromNative(
                error,
                "대상 호스트 연결 핸들을 만들지 못했습니다.",
                usesProxy,
                route);
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
            return FailureFromNative(
                error,
                "WinHTTP 요청 핸들을 만들지 못했습니다.",
                usesProxy,
                route);
        }

        using SafeWinHttpHandle request = SafeWinHttpHandle.FromRaw(requestRaw);
        uint redirectPolicy = WinHttpNative.RedirectPolicyNever;
        if (!WinHttpNative.WinHttpSetOption(
                request.DangerousGetHandle(),
                WinHttpNative.OptionRedirectPolicy,
                ref redirectPolicy,
                sizeof(uint)))
        {
            int error = Marshal.GetLastWin32Error();
            return FailureFromNative(
                error,
                "자동 리다이렉트 차단 정책을 설정하지 못했습니다.",
                usesProxy,
                route);
        }

        return SendAndReceive(options, request, usesProxy, route);
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
                    return FinishFailure(
                        stopwatch,
                        WinHttpRequestStatus.NetworkError,
                        "현재 Windows 사용자 자격 증명을 프록시 요청에 적용하지 못했습니다.",
                        error,
                        usesProxy,
                        route,
                        authenticationChoice,
                        authenticationAttempts);
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
                return FinishNativeFailure(
                    stopwatch,
                    error,
                    "WinHTTP 요청을 전송하지 못했습니다.",
                    usesProxy,
                    route,
                    authenticationChoice,
                    authenticationAttempts);
            }

            if (!WinHttpNative.WinHttpReceiveResponse(
                    request.DangerousGetHandle(),
                    nint.Zero))
            {
                int error = Marshal.GetLastWin32Error();
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
                    authenticationAttempts);
            }

            if (!TryReadStatusCode(request, out int statusCode, out int statusError))
            {
                return FinishNativeFailure(
                    stopwatch,
                    statusError,
                    "HTTP 상태 코드를 읽지 못했습니다.",
                    usesProxy,
                    route,
                    authenticationChoice,
                    authenticationAttempts);
            }

            if (statusCode == 407)
            {
                BodyReadResult challengeBody = ReadBody(
                    request,
                    MaximumChallengeBodyBytes);
                if (!challengeBody.Success)
                {
                    return FinishNativeFailure(
                        stopwatch,
                        challengeBody.Win32ErrorCode,
                        "프록시 인증 응답 본문을 정리하지 못했습니다.",
                        usesProxy,
                        route,
                        authenticationChoice,
                        authenticationAttempts);
                }

                if (authenticationAttempts >= MaximumProxyAuthenticationAttempts)
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
                        statusCode);
                }

                if (!WinHttpNative.WinHttpQueryAuthSchemes(
                        request.DangerousGetHandle(),
                        out uint supportedSchemes,
                        out uint firstScheme,
                        out uint authTarget))
                {
                    int error = Marshal.GetLastWin32Error();
                    return FinishNativeFailure(
                        stopwatch,
                        error,
                        "프록시가 제공한 인증 방식을 확인하지 못했습니다.",
                        usesProxy,
                        route,
                        authenticationChoice,
                        authenticationAttempts);
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
                        statusCode);
                }

                authenticationChoice = decision.Choice;
                authenticationScheme = decision.NativeScheme;
                authenticationAttempts++;
                continue;
            }

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
                    statusCode);
            }

            if (statusCode is >= 300 and < 400)
            {
                return FinishFailure(
                    stopwatch,
                    WinHttpRequestStatus.RedirectResponse,
                    "리다이렉트 응답을 받았습니다. 보안을 위해 자동 이동하지 않았습니다.",
                    null,
                    usesProxy,
                    route,
                    authenticationChoice,
                    authenticationAttempts,
                    statusCode);
            }

            if (statusCode is < 200 or >= 400)
            {
                return FinishFailure(
                    stopwatch,
                    WinHttpRequestStatus.HttpErrorResponse,
                    $"HTTP 상태 {statusCode} 응답을 받았습니다.",
                    null,
                    usesProxy,
                    route,
                    authenticationChoice,
                    authenticationAttempts,
                    statusCode);
            }

            BodyReadResult body = options.Method == WinHttpRequestMethod.Get
                ? ReadBody(request, options.MaxResponseBytes)
                : BodyReadResult.Empty;

            if (!body.Success)
            {
                return FinishNativeFailure(
                    stopwatch,
                    body.Win32ErrorCode,
                    "응답 본문을 읽지 못했습니다.",
                    usesProxy,
                    route,
                    authenticationChoice,
                    authenticationAttempts,
                    statusCode,
                    body.BytesRead);
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
                    : "WinHTTP 요청을 완료했습니다. 수신 데이터는 저장하지 않았습니다.");
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

    private static unsafe BodyReadResult ReadBody(
        SafeWinHttpHandle request,
        long maximumBytes)
    {
        byte[] buffer = new byte[ReadBufferSize];
        long total = 0;

        while (true)
        {
            long remaining = maximumBytes - total;
            if (remaining <= 0)
            {
                return new BodyReadResult(
                    Success: true,
                    BytesRead: total,
                    LimitReached: true,
                    Win32ErrorCode: 0);
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
                    return new BodyReadResult(
                        Success: false,
                        BytesRead: total,
                        LimitReached: false,
                        Win32ErrorCode: Marshal.GetLastWin32Error());
                }
            }

            if (bytesRead == 0)
            {
                return new BodyReadResult(
                    Success: true,
                    BytesRead: total,
                    LimitReached: false,
                    Win32ErrorCode: 0);
            }

            total = checked(total + bytesRead);
        }
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
        ProxyRouteResolution? route)
    {
        return Failure(
            errorCode == WinHttpNative.ErrorWinHttpTimeout
                ? WinHttpRequestStatus.TimedOut
                : WinHttpRequestStatus.NetworkError,
            $"{context} WinHTTP 오류 {errorCode}: {new Win32Exception(errorCode).Message}",
            errorCode,
            proxyWasUsed,
            route);
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
        long bytesReceived = 0)
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
            bytesReceived);
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
        long bytesReceived = 0)
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
            Message: message);
    }

    private static WinHttpRequestResult Failure(
        WinHttpRequestStatus status,
        string message,
        int? win32ErrorCode = null,
        bool proxyWasUsed = false,
        ProxyRouteResolution? route = null)
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
            Message: message);
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

    private sealed record BodyReadResult(
        bool Success,
        long BytesRead,
        bool LimitReached,
        int Win32ErrorCode)
    {
        internal static BodyReadResult Empty { get; } = new(
            Success: true,
            BytesRead: 0,
            LimitReached: false,
            Win32ErrorCode: 0);
    }
}
