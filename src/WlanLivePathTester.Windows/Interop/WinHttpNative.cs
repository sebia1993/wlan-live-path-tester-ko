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

    [LibraryImport("winhttp.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool WinHttpCloseHandle(nint handle);

    [LibraryImport("kernel32.dll")]
    internal static partial nint GlobalFree(nint memory);
}
