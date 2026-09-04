using WlanLivePathTester.Core.Models;
using WlanLivePathTester.Core.NetworkEnvironment;
using WlanLivePathTester.Windows.NetworkEnvironment;
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

        if (!CheckNetworkEnvironmentBoundary())
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

    private static bool CheckNetworkEnvironmentBoundary()
    {
        LocalNetworkEnvironmentSnapshot snapshot =
            LocalNetworkEnvironmentReader.ReadCurrent();
        Console.WriteLine(
            $"Network environment: adapters={snapshot.Adapters.Count}, active={snapshot.Assessment.ActiveAdapterCount}, wireless={snapshot.Assessment.ActiveWirelessCount}, gateways={snapshot.Assessment.ActiveDefaultGatewayCount}, ambiguous={snapshot.Assessment.RouteSelectionMayBeAmbiguous}");
        Console.WriteLine(snapshot.Message);

        if (string.IsNullOrWhiteSpace(snapshot.Message))
        {
            Console.Error.WriteLine("The network environment reader returned an empty message.");
            return false;
        }

        if (snapshot.Assessment.TotalAdapterCount != snapshot.Adapters.Count)
        {
            Console.Error.WriteLine("The network environment summary adapter count is inconsistent.");
            return false;
        }

        foreach (LocalNetworkAdapterSnapshot adapter in snapshot.Adapters)
        {
            if (string.IsNullOrWhiteSpace(adapter.DisplayName)
                || adapter.DisplayName.Contains('\r')
                || adapter.DisplayName.Contains('\n'))
            {
                Console.Error.WriteLine("An adapter display name is empty or contains a line break.");
                return false;
            }

            if (adapter.GatewayCount < 0
                || adapter.UnicastAddressCount < 0
                || adapter.SpeedBitsPerSecond is <= 0)
            {
                Console.Error.WriteLine("An adapter exposed an invalid count or link speed.");
                return false;
            }
        }

        return true;
    }
}
