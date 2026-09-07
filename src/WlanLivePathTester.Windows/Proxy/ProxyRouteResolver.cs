using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using WlanLivePathTester.Core.Models;
using WlanLivePathTester.Core.Proxy;
using WlanLivePathTester.Core.Security;
using WlanLivePathTester.Windows.Interop;

namespace WlanLivePathTester.Windows.Proxy;

[SupportedOSPlatform("windows")]
public static class ProxyRouteResolver
{
    private const int DefaultTimeoutMilliseconds = 5000;
    private const int MinimumTimeoutMilliseconds = 1000;
    private const int MaximumTimeoutMilliseconds = 30000;
    private const int ErrorInvalidParameter = 87;
    private const int ErrorWinHttpTimeout = 12002;
    private const int ErrorWinHttpUnrecognizedScheme = 12006;
    private const int ErrorWinHttpLoginFailure = 12015;
    private const int ErrorWinHttpOperationCancelled = 12017;
    private const int ErrorWinHttpBadAutoProxyScript = 12166;
    private const int ErrorWinHttpUnableToDownloadScript = 12167;
    private const int ErrorWinHttpAutoProxyServiceError = 12178;
    private const int ErrorWinHttpAutoDetectionFailed = 12180;
    private const string UserAgent = "WlanLivePathTester/0.1";

    public static ProxyRouteResolution Resolve(string url, NetworkPathKind expectedPath,
        int timeoutMilliseconds = DefaultTimeoutMilliseconds) =>
        ResolveDetailed(url, expectedPath, timeoutMilliseconds).Summary;

    internal static ResolvedProxyRoute ResolveDetailed(string url, NetworkPathKind expectedPath,
        int timeoutMilliseconds = DefaultTimeoutMilliseconds)
    {
        if (!OperatingSystem.IsWindows())
            return Failure(ProxyResolutionStatus.UnsupportedPlatform,
                "Windows에서만 현재 사용자 프록시 경로를 확인할 수 있습니다.");
        if (!TryValidateUrl(url, out Uri? destination, out string validationMessage))
            return Failure(ProxyResolutionStatus.InvalidUrl, validationMessage);
        if (!Enum.IsDefined(expectedPath))
            return Failure(ProxyResolutionStatus.InvalidUrl, "프록시 판정의 기대 경로가 올바르지 않습니다.");
        if (timeoutMilliseconds is < MinimumTimeoutMilliseconds or > MaximumTimeoutMilliseconds)
            return Failure(ProxyResolutionStatus.InvalidUrl,
                $"프록시 판정 제한 시간은 {MinimumTimeoutMilliseconds}~{MaximumTimeoutMilliseconds}ms 범위여야 합니다.");
        try
        {
            CurrentUserProxyConfiguration configuration = CurrentUserProxySettingsReader.ReadRaw();
            if (!configuration.ReadSucceeded)
                return Failure(ProxyResolutionStatus.ConfigurationReadFailed,
                    "현재 사용자의 Windows 프록시 설정을 읽지 못했습니다.", configuration.Win32Error);
            if (!NetworkInputBoundary.TrySingleLine(configuration.AutoConfigUrl,
                    NetworkInputBoundary.MaximumUrlLength, out string pacValue, out _, allowEmpty: true))
                return Failure(ProxyResolutionStatus.ConfigurationInvalid, "PAC URL 원문이 올바르지 않습니다.");
            if (configuration.AutoDetectEnabled || pacValue.Length != 0)
                return ResolveAutomatic(destination!, expectedPath, configuration, timeoutMilliseconds);
            ProxyConfigurationSource source = string.IsNullOrWhiteSpace(configuration.ManualProxy)
                ? ProxyConfigurationSource.None : ProxyConfigurationSource.Manual;
            ProxySelection selection = ProxyDirectiveParser.SelectManual(
                destination!, configuration.ManualProxy, configuration.BypassList);
            return CreateFromSelection(selection, expectedPath, source, autoLogonRetried: false,
                networkLookupPerformed: false, win32ErrorCode: null, prefixMessage: null);
        }
        catch (Exception exception) when (exception is DllNotFoundException
            or EntryPointNotFoundException or BadImageFormatException or ExternalException
            or ArgumentException or OverflowException)
        {
            return Failure(ProxyResolutionStatus.NativeError,
                $"Windows WinHTTP 프록시 판정을 완료하지 못했습니다. 오류 유형: {exception.GetType().Name}.");
        }
    }

    private static ResolvedProxyRoute ResolveAutomatic(Uri destination, NetworkPathKind expectedPath,
        CurrentUserProxyConfiguration configuration, int timeoutMilliseconds)
    {
        // Validation happens before allocating native strings or issuing PAC
        // requests. Private/intranet PAC hosts remain valid; this is syntax only.
        if (!NetworkInputBoundary.TrySingleLine(configuration.AutoConfigUrl,
                NetworkInputBoundary.MaximumUrlLength, out string pacValue, out _, allowEmpty: true))
            return Failure(ProxyResolutionStatus.ConfigurationInvalid, "PAC URL 원문이 올바르지 않습니다.");
        Uri? pacUri = null;
        if (pacValue.Length != 0 && !NetworkInputBoundary.TryHttpUri(configuration.AutoConfigUrl, out pacUri, out _))
            return Failure(ProxyResolutionStatus.ConfigurationInvalid, "PAC URL은 유효한 HTTP 또는 HTTPS 주소여야 합니다.");

        nint session = WinHttpNative.WinHttpOpen(UserAgent, WinHttpNative.AccessTypeNoProxy,
            nint.Zero, nint.Zero, flags: 0);
        if (session == nint.Zero)
            return Failure(ProxyResolutionStatus.NativeError, "WinHTTP 프록시 판정 세션을 열지 못했습니다.",
                Marshal.GetLastWin32Error(), networkLookupPerformed: false);
        nint autoConfigUrlPointer = nint.Zero;
        try
        {
            if (!WinHttpNative.WinHttpSetTimeouts(session, timeoutMilliseconds,
                    timeoutMilliseconds, timeoutMilliseconds, timeoutMilliseconds))
                return Failure(ProxyResolutionStatus.NativeError, "WinHTTP 프록시 판정 제한 시간을 설정하지 못했습니다.",
                    Marshal.GetLastWin32Error(), networkLookupPerformed: false);
            if (pacUri is not null) autoConfigUrlPointer = Marshal.StringToHGlobalUni(pacUri.AbsoluteUri);
            AutoProxyOutcome? lastOutcome = null;
            ProxyConfigurationSource lastSource = ProxyConfigurationSource.Unknown;
            bool autoLogonRetried = false;
            if (configuration.AutoDetectEnabled)
            {
                AutoProxyAttempt wpadAttempt = QueryWithConditionalAutoLogon(
                    session, destination.AbsoluteUri, useAutoDetect: true, autoConfigUrlPointer: nint.Zero);
                autoLogonRetried |= wpadAttempt.AutoLogonRetried;
                lastOutcome = wpadAttempt.Outcome;
                lastSource = ProxyConfigurationSource.Wpad;
                if (wpadAttempt.Outcome.Success)
                    return CreateFromAutomaticOutcome(wpadAttempt.Outcome, destination,
                        expectedPath, ProxyConfigurationSource.Wpad, autoLogonRetried);
                if (!IsRecoverableAutoProxyError(wpadAttempt.Outcome.Win32ErrorCode))
                    return FailureFromAutoProxy(wpadAttempt.Outcome.Win32ErrorCode,
                        autoLogonRetried, ProxyConfigurationSource.Wpad);
            }
            if (autoConfigUrlPointer != nint.Zero)
            {
                AutoProxyAttempt pacAttempt = QueryWithConditionalAutoLogon(
                    session, destination.AbsoluteUri, useAutoDetect: false, autoConfigUrlPointer: autoConfigUrlPointer);
                autoLogonRetried |= pacAttempt.AutoLogonRetried;
                lastOutcome = pacAttempt.Outcome;
                lastSource = configuration.AutoDetectEnabled ? ProxyConfigurationSource.WpadThenPac : ProxyConfigurationSource.Pac;
                if (pacAttempt.Outcome.Success)
                    return CreateFromAutomaticOutcome(pacAttempt.Outcome, destination, expectedPath, lastSource, autoLogonRetried);
                if (!IsRecoverableAutoProxyError(pacAttempt.Outcome.Win32ErrorCode))
                    return FailureFromAutoProxy(pacAttempt.Outcome.Win32ErrorCode, autoLogonRetried, lastSource);
            }
            if (!string.IsNullOrWhiteSpace(configuration.ManualProxy))
            {
                ProxySelection fallback = ProxyDirectiveParser.SelectManual(
                    destination, configuration.ManualProxy, configuration.BypassList);
                if (fallback.RouteKind != ProxyRouteKind.Unknown)
                    return CreateFromSelection(fallback, expectedPath, ProxyConfigurationSource.ManualFallback,
                        autoLogonRetried, networkLookupPerformed: true, win32ErrorCode: lastOutcome?.Win32ErrorCode,
                        prefixMessage: "PAC/WPAD 자동 판정에 실패해 현재 사용자의 수동 프록시 설정을 적용했습니다.");
            }
            if (lastOutcome is null)
                return Failure(ProxyResolutionStatus.ConfigurationInvalid,
                    "자동 프록시가 설정되어 있지만 실행할 PAC/WPAD 방식이 없습니다.");
            return FailureFromAutoProxy(lastOutcome.Win32ErrorCode, autoLogonRetried, lastSource);
        }
        finally
        {
            if (autoConfigUrlPointer != nint.Zero) Marshal.FreeHGlobal(autoConfigUrlPointer);
            _ = WinHttpNative.WinHttpCloseHandle(session);
        }
    }

    private static ResolvedProxyRoute CreateFromAutomaticOutcome(AutoProxyOutcome outcome,
        Uri destination, NetworkPathKind expectedPath, ProxyConfigurationSource source, bool autoLogonRetried)
    {
        ProxySelection selection = outcome.AccessType == WinHttpNative.AccessTypeNoProxy
            ? ProxySelection.Direct() : ProxyDirectiveParser.SelectAutoProxyList(destination, outcome.ProxyList);
        return CreateFromSelection(selection, expectedPath, source, autoLogonRetried,
            networkLookupPerformed: true, win32ErrorCode: null, prefixMessage: null);
    }

    private static AutoProxyAttempt QueryWithConditionalAutoLogon(nint session, string url,
        bool useAutoDetect, nint autoConfigUrlPointer)
    {
        WinHttpNative.AutoProxyOptions options = CreateAutoProxyOptions(
            useAutoDetect, autoConfigUrlPointer, autoLogonIfChallenged: false);
        AutoProxyOutcome outcome = QueryAutomaticProxy(session, url, ref options);
        if (outcome.Success || outcome.Win32ErrorCode != ErrorWinHttpLoginFailure)
            return new AutoProxyAttempt(outcome, AutoLogonRetried: false);
        options.AutoLogonIfChallenged = 1;
        outcome = QueryAutomaticProxy(session, url, ref options);
        return new AutoProxyAttempt(outcome, AutoLogonRetried: true);
    }

    private static WinHttpNative.AutoProxyOptions CreateAutoProxyOptions(
        bool useAutoDetect, nint autoConfigUrlPointer, bool autoLogonIfChallenged) => new()
    {
        Flags = useAutoDetect ? WinHttpNative.AutoProxyAutoDetect : WinHttpNative.AutoProxyConfigUrl,
        AutoDetectFlags = useAutoDetect ? WinHttpNative.AutoDetectTypeDhcp | WinHttpNative.AutoDetectTypeDnsA : 0,
        AutoConfigUrl = useAutoDetect ? nint.Zero : autoConfigUrlPointer,
        AutoLogonIfChallenged = autoLogonIfChallenged ? 1 : 0
    };

    private static AutoProxyOutcome QueryAutomaticProxy(nint session, string url, ref WinHttpNative.AutoProxyOptions options)
    {
        WinHttpNative.ProxyInfo proxyInfo = default;
        bool success = WinHttpNative.WinHttpGetProxyForUrl(session, url, ref options, out proxyInfo);
        int errorCode = success ? 0 : Marshal.GetLastWin32Error();
        try
        {
            return new AutoProxyOutcome(success, proxyInfo.AccessType, CopyString(proxyInfo.Proxy), errorCode);
        }
        finally
        {
            FreeGlobal(proxyInfo.Proxy);
            FreeGlobal(proxyInfo.ProxyBypass);
        }
    }

    private static ResolvedProxyRoute CreateFromSelection(ProxySelection selection,
        NetworkPathKind expectedPath, ProxyConfigurationSource source, bool autoLogonRetried,
        bool networkLookupPerformed, int? win32ErrorCode, string? prefixMessage)
    {
        if (selection.RouteKind == ProxyRouteKind.Unknown)
            return Failure(ProxyResolutionStatus.ConfigurationInvalid,
                selection.Error ?? "프록시 경로를 해석하지 못했습니다.", win32ErrorCode,
                autoLogonRetried, networkLookupPerformed, source, selection.InvalidDirectiveCount);
        ProxyPathExpectation expectation = ProxyRouteExpectationEvaluator.Evaluate(expectedPath, selection.RouteKind);
        string routeMessage = selection switch
        {
            { WasBypassed: true } => "현재 사용자의 프록시 바이패스 목록에 따라 DIRECT 경로로 판정했습니다.",
            { RouteKind: ProxyRouteKind.Direct } when source == ProxyConfigurationSource.None =>
                "현재 사용자 설정에서 적용할 프록시를 찾지 못해 DIRECT 경로로 판정했습니다.",
            { RouteKind: ProxyRouteKind.Direct } => "대상 URL에 적용되는 프록시가 없어 DIRECT 경로로 판정했습니다.",
            _ => $"대상 URL은 프록시 후보 {selection.ProxyCandidateCount}개를 사용하는 경로로 판정했습니다. 프록시 주소는 표시하지 않습니다."
        };
        if (selection.InvalidDirectiveCount > 0)
            routeMessage += $" 해석할 수 없는 지시문 {selection.InvalidDirectiveCount}개는 제외했습니다.";
        if (!string.IsNullOrWhiteSpace(prefixMessage)) routeMessage = $"{prefixMessage} {routeMessage}";
        ProxyRouteResolution summary = new(
            Status: ProxyResolutionStatus.Success, RouteKind: selection.RouteKind, Source: source,
            Expectation: expectation, ProxyCandidateCount: selection.ProxyCandidateCount,
            HasDirectFallback: selection.HasDirectFallback, WasBypassed: selection.WasBypassed,
            AutoLogonRetried: autoLogonRetried, NetworkLookupPerformed: networkLookupPerformed,
            InvalidDirectiveCount: selection.InvalidDirectiveCount, Win32ErrorCode: win32ErrorCode, Message: routeMessage);
        return new ResolvedProxyRoute(summary, selection);
    }

    private static ResolvedProxyRoute FailureFromAutoProxy(int errorCode, bool autoLogonRetried, ProxyConfigurationSource source)
    {
        ProxyResolutionStatus status = errorCode switch
        {
            ErrorWinHttpTimeout => ProxyResolutionStatus.TimedOut,
            ErrorWinHttpLoginFailure => ProxyResolutionStatus.AutoProxyAuthenticationFailed,
            ErrorWinHttpBadAutoProxyScript or ErrorWinHttpUnableToDownloadScript
                or ErrorWinHttpAutoProxyServiceError or ErrorWinHttpAutoDetectionFailed => ProxyResolutionStatus.AutoProxyFailed,
            _ => ProxyResolutionStatus.NativeError
        };
        string message = errorCode switch
        {
            ErrorWinHttpTimeout => "PAC/WPAD 프록시 판정 시간이 초과되었습니다.",
            ErrorWinHttpLoginFailure => "PAC/WPAD 정보를 가져오는 과정에서 Windows 통합 인증을 완료하지 못했습니다.",
            ErrorWinHttpBadAutoProxyScript => "PAC 스크립트를 해석하지 못했습니다.",
            ErrorWinHttpUnableToDownloadScript => "PAC 스크립트를 내려받지 못했습니다.",
            ErrorWinHttpAutoProxyServiceError => "Windows 자동 프록시 서비스가 요청을 처리하지 못했습니다.",
            ErrorWinHttpAutoDetectionFailed => "WPAD 자동 검색에서 PAC 위치를 찾지 못했습니다.",
            _ => $"WinHTTP 프록시 자동 판정 오류 {errorCode}: {DescribeError(errorCode)}"
        };
        return Failure(status, message, errorCode, autoLogonRetried, networkLookupPerformed: true, source: source);
    }

    private static bool IsRecoverableAutoProxyError(int errorCode) => errorCode is 0 or ErrorInvalidParameter
        or ErrorWinHttpAutoProxyServiceError or ErrorWinHttpAutoDetectionFailed or ErrorWinHttpBadAutoProxyScript
        or ErrorWinHttpLoginFailure or ErrorWinHttpOperationCancelled or ErrorWinHttpTimeout
        or ErrorWinHttpUnableToDownloadScript or ErrorWinHttpUnrecognizedScheme;

    private static ResolvedProxyRoute Failure(ProxyResolutionStatus status, string message,
        int? win32ErrorCode = null, bool autoLogonRetried = false, bool networkLookupPerformed = false,
        ProxyConfigurationSource source = ProxyConfigurationSource.Unknown, int invalidDirectiveCount = 0)
    {
        ProxyRouteResolution summary = new(Status: status, RouteKind: ProxyRouteKind.Unknown,
            Source: source, Expectation: ProxyPathExpectation.Unknown, ProxyCandidateCount: 0,
            HasDirectFallback: false, WasBypassed: false, AutoLogonRetried: autoLogonRetried,
            NetworkLookupPerformed: networkLookupPerformed, InvalidDirectiveCount: invalidDirectiveCount,
            Win32ErrorCode: win32ErrorCode, Message: message);
        return new ResolvedProxyRoute(summary, ProxySelection.Unknown(message, invalidDirectiveCount));
    }

    private static bool TryValidateUrl(string rawUrl, out Uri? destination, out string message) =>
        NetworkInputBoundary.TryHttpUri(rawUrl, out destination, out message);
    private static string? CopyString(nint pointer) => pointer == nint.Zero ? null : Marshal.PtrToStringUni(pointer);
    private static void FreeGlobal(nint pointer)
    {
        if (pointer != nint.Zero) _ = WinHttpNative.GlobalFree(pointer);
    }
    private static string DescribeError(int errorCode) => new Win32Exception(errorCode).Message;
    private sealed record AutoProxyOutcome(bool Success, uint AccessType, string? ProxyList, int Win32ErrorCode);
    private sealed record AutoProxyAttempt(AutoProxyOutcome Outcome, bool AutoLogonRetried);
}
