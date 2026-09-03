using WlanLivePathTester.Core.Models;
using WlanLivePathTester.Windows.Proxy;
using WlanLivePathTester.Windows.Wlan;

namespace WlanLivePathTester.WindowsSmoke;

internal static class Program
{
    private static int Main()
    {
        if (!OperatingSystem.IsWindows())
        {
            Console.Error.WriteLine("Windows API smoke test must run on Windows.");
            return 2;
        }

        if (!CheckProxySettingsBoundary())
        {
            return 1;
        }

        WlanReadResult stoppedService = new(
            WlanReadStatus.NativeError,
            Array.Empty<WlanSnapshot>(),
            1062,
            "Synthetic native error");

        if (stoppedService.Status != WlanReadStatus.ServiceNotRunning
            || !stoppedService.Message.Contains("WlanSvc", StringComparison.Ordinal))
        {
            Console.Error.WriteLine("Native error 1062 was not normalized to ServiceNotRunning.");
            return 1;
        }

        for (int attempt = 1; attempt <= 2; attempt++)
        {
            WlanReadResult result = NativeWlanReader.ReadCurrent();
            Console.WriteLine(
                $"WLAN attempt {attempt}: status={result.Status}, interfaces={result.Interfaces.Count}, nativeError={result.NativeErrorCode?.ToString() ?? "none"}");
            Console.WriteLine(result.Message);

            if (result.Status == WlanReadStatus.UnsupportedPlatform)
            {
                Console.Error.WriteLine("The Windows build unexpectedly reported an unsupported platform.");
                return 1;
            }

            if (string.IsNullOrWhiteSpace(result.Message))
            {
                Console.Error.WriteLine("The WLAN reader returned an empty diagnostic message.");
                return 1;
            }

            if (result.Status == WlanReadStatus.Success && result.FirstConnectedInterface is null)
            {
                Console.Error.WriteLine("Success requires at least one connected WLAN interface.");
                return 1;
            }
        }

        Console.WriteLine("Windows API smoke test completed without an unhandled exception.");
        return 0;
    }

    private static bool CheckProxySettingsBoundary()
    {
        CurrentUserProxySettings settings = CurrentUserProxySettingsReader.Read();
        Console.WriteLine(
            $"Proxy settings: success={settings.ReadSucceeded}, mode={settings.Mode}, nativeError={settings.Win32Error?.ToString() ?? "none"}");

        foreach (string? value in new[]
                 {
                     settings.AutoConfigUrl,
                     settings.ManualProxy,
                     settings.BypassList
                 })
        {
            if (value is not null && value != "[설정됨]")
            {
                Console.Error.WriteLine("The public proxy-settings API exposed an unmasked value.");
                return false;
            }
        }

        return true;
    }
}
