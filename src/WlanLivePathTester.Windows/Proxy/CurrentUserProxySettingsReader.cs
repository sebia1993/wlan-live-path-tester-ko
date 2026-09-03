using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using WlanLivePathTester.Windows.Interop;

namespace WlanLivePathTester.Windows.Proxy;

[SupportedOSPlatform("windows")]
public static class CurrentUserProxySettingsReader
{
    private const int ErrorFileNotFound = 2;

    public static CurrentUserProxySettings Read()
    {
        CurrentUserProxyConfiguration raw = ReadRaw();
        return new CurrentUserProxySettings(
            ReadSucceeded: raw.ReadSucceeded,
            Win32Error: raw.Win32Error,
            AutoDetectEnabled: raw.AutoDetectEnabled,
            AutoConfigUrl: MaskIfPresent(raw.AutoConfigUrl),
            ManualProxy: MaskIfPresent(raw.ManualProxy),
            BypassList: MaskIfPresent(raw.BypassList));
    }

    public static CurrentUserProxySettings ReadOrThrow()
    {
        CurrentUserProxySettings settings = Read();
        if (!settings.ReadSucceeded)
        {
            throw new Win32Exception(
                settings.Win32Error ?? 0,
                "현재 사용자의 Windows 프록시 설정을 읽지 못했습니다.");
        }

        return settings;
    }

    internal static CurrentUserProxyConfiguration ReadRaw()
    {
        if (!WinHttpNative.WinHttpGetIEProxyConfigForCurrentUser(
                out WinHttpNative.CurrentUserIeProxyConfig native))
        {
            int error = Marshal.GetLastWin32Error();
            if (error == ErrorFileNotFound)
            {
                return new CurrentUserProxyConfiguration(
                    ReadSucceeded: true,
                    Win32Error: null,
                    AutoDetectEnabled: false,
                    AutoConfigUrl: null,
                    ManualProxy: null,
                    BypassList: null);
            }

            return new CurrentUserProxyConfiguration(
                ReadSucceeded: false,
                Win32Error: error,
                AutoDetectEnabled: false,
                AutoConfigUrl: null,
                ManualProxy: null,
                BypassList: null);
        }

        try
        {
            return new CurrentUserProxyConfiguration(
                ReadSucceeded: true,
                Win32Error: null,
                AutoDetectEnabled: native.AutoDetect != 0,
                AutoConfigUrl: CopyString(native.AutoConfigUrl),
                ManualProxy: CopyString(native.Proxy),
                BypassList: CopyString(native.ProxyBypass));
        }
        finally
        {
            Free(native.AutoConfigUrl);
            Free(native.Proxy);
            Free(native.ProxyBypass);
        }
    }

    private static string? CopyString(nint pointer)
    {
        if (pointer == nint.Zero)
        {
            return null;
        }

        string? value = Marshal.PtrToStringUni(pointer);
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static string? MaskIfPresent(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : "[설정됨]";

    private static void Free(nint pointer)
    {
        if (pointer != nint.Zero)
        {
            _ = WinHttpNative.GlobalFree(pointer);
        }
    }
}
