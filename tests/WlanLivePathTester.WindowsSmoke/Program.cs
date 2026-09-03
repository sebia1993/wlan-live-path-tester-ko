using WlanLivePathTester.Windows.Wlan;

namespace WlanLivePathTester.WindowsSmoke;

internal static class Program
{
    private static int Main()
    {
        if (!OperatingSystem.IsWindows())
        {
            Console.Error.WriteLine("Windows WLAN API smoke test must run on Windows.");
            return 2;
        }

        for (int attempt = 1; attempt <= 2; attempt++)
        {
            WlanReadResult result = NativeWlanReader.ReadCurrent();
            Console.WriteLine(
                $"Attempt {attempt}: status={result.Status}, interfaces={result.Interfaces.Count}, nativeError={result.NativeErrorCode?.ToString() ?? "none"}");
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

        Console.WriteLine("Windows WLAN API smoke test completed without an unhandled exception.");
        return 0;
    }
}
