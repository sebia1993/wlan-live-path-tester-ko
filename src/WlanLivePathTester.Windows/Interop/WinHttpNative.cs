using System.Runtime.InteropServices;

namespace WlanLivePathTester.Windows.Interop;

internal static partial class WinHttpNative
{
    internal const uint AccessTypeDefaultProxy = 0;
    internal const uint AccessTypeNoProxy = 1;
    internal const uint AccessTypeNamedProxy = 3;
    internal const uint AccessTypeAutomaticProxy = 4;

    internal const uint AutoProxyAutoDetect = 0x00000001;
    internal const uint AutoProxyConfigUrl = 0x00000002;
    internal const uint AutoDetectTypeDhcp = 0x00000001;
    internal const uint AutoDetectTypeDnsA = 0x00000002;

    internal const uint FlagSecure = 0x00800000;
    internal const uint OptionRedirectPolicy = 88;
    internal const uint RedirectPolicyNever = 0;
    internal const uint QueryStatusCode = 19;
    internal const uint QueryCustom = 65535;
    internal const uint QueryFlagNumber = 0x20000000;

    internal const int ErrorInsufficientBuffer = 122;
    internal const int ErrorWinHttpTimeout = 12002;
    internal const int ErrorWinHttpResendRequest = 12032;

    [StructLayout(LayoutKind.Sequential)]
    internal struct CurrentUserIeProxyConfig
    {
        internal int AutoDetect;
        internal nint AutoConfigUrl;
        internal nint Proxy;
        internal nint ProxyBypass;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct AutoProxyOptions
    {
        internal uint Flags;
        internal uint AutoDetectFlags;
        internal nint AutoConfigUrl;
        internal nint Reserved;
        internal uint ReservedFlags;
        internal int AutoLogonIfChallenged;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct ProxyInfo
    {
        internal uint AccessType;
        internal nint Proxy;
        internal nint ProxyBypass;
    }

    [LibraryImport("winhttp.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool WinHttpGetIEProxyConfigForCurrentUser(
        out CurrentUserIeProxyConfig proxyConfig);

    [LibraryImport(
        "winhttp.dll",
        SetLastError = true,
        StringMarshalling = StringMarshalling.Utf16)]
    internal static partial nint WinHttpOpen(
        string userAgent,
        uint accessType,
        nint proxyName,
        nint proxyBypass,
        uint flags);

    [LibraryImport(
        "winhttp.dll",
        EntryPoint = "WinHttpOpen",
        SetLastError = true,
        StringMarshalling = StringMarshalling.Utf16)]
    internal static partial nint WinHttpOpenWithProxy(
        string userAgent,
        uint accessType,
        string? proxyName,
        string? proxyBypass,
        uint flags);

    [LibraryImport("winhttp.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool WinHttpSetTimeouts(
        nint session,
        int resolveTimeoutMilliseconds,
        int connectTimeoutMilliseconds,
        int sendTimeoutMilliseconds,
        int receiveTimeoutMilliseconds);

    [LibraryImport(
        "winhttp.dll",
        SetLastError = true,
        StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool WinHttpGetProxyForUrl(
        nint session,
        string url,
        ref AutoProxyOptions autoProxyOptions,
        out ProxyInfo proxyInfo);

    [LibraryImport(
        "winhttp.dll",
        SetLastError = true,
        StringMarshalling = StringMarshalling.Utf16)]
    internal static partial nint WinHttpConnect(
        nint session,
        string serverName,
        ushort serverPort,
        uint reserved);

    [LibraryImport(
        "winhttp.dll",
        SetLastError = true,
        StringMarshalling = StringMarshalling.Utf16)]
    internal static partial nint WinHttpOpenRequest(
        nint connect,
        string verb,
        string objectName,
        nint version,
        nint referrer,
        nint acceptTypes,
        uint flags);

    [LibraryImport("winhttp.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool WinHttpSetOption(
        nint handle,
        uint option,
        ref uint buffer,
        uint bufferLength);

    [LibraryImport(
        "winhttp.dll",
        SetLastError = true,
        StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool WinHttpSetCredentials(
        nint request,
        uint authTargets,
        uint authScheme,
        string? userName,
        string? password,
        nint authParams);

    [LibraryImport("winhttp.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool WinHttpSendRequest(
        nint request,
        nint additionalHeaders,
        uint additionalHeadersLength,
        nint optionalData,
        uint optionalDataLength,
        uint totalLength,
        nuint context);

    [LibraryImport("winhttp.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool WinHttpReceiveResponse(
        nint request,
        nint reserved);

    [LibraryImport("winhttp.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool WinHttpQueryAuthSchemes(
        nint request,
        out uint supportedSchemes,
        out uint firstScheme,
        out uint authTarget);

    [LibraryImport("winhttp.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool WinHttpQueryHeaders(
        nint request,
        uint infoLevel,
        nint name,
        out uint buffer,
        ref uint bufferLength,
        nint index);

    [LibraryImport(
        "winhttp.dll",
        EntryPoint = "WinHttpQueryHeaders",
        SetLastError = true,
        StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool WinHttpQueryHeadersText(
        nint request,
        uint infoLevel,
        string name,
        nint buffer,
        ref uint bufferLength,
        nint index);

    [LibraryImport("winhttp.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool WinHttpReadData(
        nint request,
        nint buffer,
        uint bytesToRead,
        out uint bytesRead);

    [LibraryImport("winhttp.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool WinHttpCloseHandle(nint handle);

    [LibraryImport("kernel32.dll")]
    internal static partial nint GlobalFree(nint memory);
}
