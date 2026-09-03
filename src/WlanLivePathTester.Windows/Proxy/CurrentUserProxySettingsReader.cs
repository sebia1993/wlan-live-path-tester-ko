using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using WlanLivePathTester.Windows.Interop;

namespace WlanLivePathTester.Windows.Proxy;

[SupportedOSPlatform("windows")]
public static class CurrentUserProxySettingsReader
{
    public static CurrentUserProxySettings Read(bool includeSensitiveValues = false)
    {
        if (!WinHttpNative.WinHttpGetIEProxyConfigForCurrentUser(
                out WinHttpNative.CurrentUserIeProxyConfig native))
        {
            int error = Marshal.GetLastWin32Error();
            return new CurrentUserProxySettings(
                ReadSucceeded: false,
                Win32Error: error,
                AutoDetectEnabled: false,
                AutoConfigUrl: null,
                ManualProxy: null,
                BypassList: null);
        }

        try
        {
            return new CurrentUserProxySettings(
                ReadSucceeded: true,
                Win32Error: null,
                AutoDetectEnabled: native.AutoDetect != 0,
                AutoConfigUrl: ReadAndMask(native.AutoConfigUrl, includeSensitiveValues),
                ManualProxy: ReadAndMask(native.Proxy, includeSensitiveValues),
                BypassList: ReadAndMask(native.ProxyBypass, includeSensitiveValues));
        }
        finally
        {
            Free(native.AutoConfigUrl);
            Free(native.Proxy);
            Free(native.ProxyBypass);
        }
    }

    public static CurrentUserProxySettings ReadOrThrow(bool includeSensitiveValues = false)
    {
        CurrentUserProxySettings settings = Read(includeSensitiveValues);
        if (!settings.ReadSucceeded)
        {
            throw new Win32Exception(
                settings.Win32Error ?? 0,
                "현재 사용자의 Windows 프록시 설정을 읽지 못했습니다.");
        }

        return settings;
    }

    private static string? ReadAndMask(nint pointer, bool includeSensitiveValues)
    {
        if (pointer == nint.Zero)
        {
            return null;
        }

        string? value = Marshal.PtrToStringUni(pointer);
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return includeSensitiveValues ? value : "[설정됨]";
    }

    private static void Free(nint pointer)
    {
        if (pointer != nint.Zero)
        {
            _ = WinHttpNative.GlobalFree(pointer);
        }
    }
}
