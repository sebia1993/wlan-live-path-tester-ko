using System.Runtime.Versioning;
using WlanLivePathTester.Core.Observation;
using WlanLivePathTester.Windows.Wlan;

namespace WlanLivePathTester.Windows.Observation;

public interface IBrowserObservationRuntime
{
    bool IsSupportedPlatform { get; }

    bool RequiresWorkerThread => false;

    DateTimeOffset UtcNow { get; }

    WlanReadResult ReadWlan();

    WlanInterfaceIdentityReadResult ReadWlanIdentity();

    InterfaceCounterReadResult ReadCounter(
        string? preferredInterfaceId,
        string? preferredInterfaceDescription,
        InterfaceCounterSelectionMode selectionMode);

    Task DelayAsync(
        TimeSpan delay,
        CancellationToken cancellationToken);
}

[SupportedOSPlatform("windows")]
internal sealed class WindowsBrowserObservationRuntime
    : IBrowserObservationRuntime
{
    public static WindowsBrowserObservationRuntime Instance { get; } =
        new();

    private WindowsBrowserObservationRuntime()
    {
    }

    public bool IsSupportedPlatform => OperatingSystem.IsWindows();

    public bool RequiresWorkerThread => true;

    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;

    public WlanReadResult ReadWlan() =>
        NativeWlanReader.ReadCurrent();

    public WlanInterfaceIdentityReadResult ReadWlanIdentity() =>
        WlanInterfaceIdentityReader.ReadCurrent();

    public InterfaceCounterReadResult ReadCounter(
        string? preferredInterfaceId,
        string? preferredInterfaceDescription,
        InterfaceCounterSelectionMode selectionMode) =>
        WindowsInterfaceCounterReader.ReadCurrent(
            preferredInterfaceId,
            preferredInterfaceDescription,
            selectionMode);

    public Task DelayAsync(
        TimeSpan delay,
        CancellationToken cancellationToken) =>
        Task.Delay(delay, cancellationToken);
}
