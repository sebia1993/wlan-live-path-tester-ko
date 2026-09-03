using System.Runtime.InteropServices;

namespace WlanLivePathTester.Windows.Interop;

internal static partial class WinHttpNative
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct CurrentUserIeProxyConfig
    {
        internal int AutoDetect;
        internal nint AutoConfigUrl;
        internal nint Proxy;
        internal nint ProxyBypass;
    }

    [LibraryImport("winhttp.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool WinHttpGetIEProxyConfigForCurrentUser(
        out CurrentUserIeProxyConfig proxyConfig);

    [LibraryImport("kernel32.dll")]
    internal static partial nint GlobalFree(nint memory);
}
