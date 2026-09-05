using System.Runtime.Versioning;

namespace WlanLivePathTester.Windows.Proxy;

public sealed class DelegateWindowsManualProxyConfigurationSource
    : IWindowsManualProxyConfigurationSource
{
    private readonly Func<CancellationToken,
        Task<WindowsManualProxyConfigurationReadResult>> _reader;

    public DelegateWindowsManualProxyConfigurationSource(
        Func<CancellationToken,
            Task<WindowsManualProxyConfigurationReadResult>> reader)
    {
        _reader = reader
            ?? throw new ArgumentNullException(nameof(reader));
    }

    public Task<WindowsManualProxyConfigurationReadResult> ReadAsync(
        CancellationToken cancellationToken) =>
        _reader(cancellationToken);
}

public sealed class DelegateWindowsTargetProxyDecisionSource
    : IWindowsTargetProxyDecisionSource
{
    private readonly Func<Uri,
        WindowsManualProxyConfigurationReadResult,
        CancellationToken,
        Task<WindowsTargetProxyDecisionReadResult>> _reader;

    public DelegateWindowsTargetProxyDecisionSource(
        Func<Uri,
            WindowsManualProxyConfigurationReadResult,
            CancellationToken,
            Task<WindowsTargetProxyDecisionReadResult>> reader)
    {
        _reader = reader
            ?? throw new ArgumentNullException(nameof(reader));
    }

    public Task<WindowsTargetProxyDecisionReadResult> ReadAsync(
        Uri targetUri,
        WindowsManualProxyConfigurationReadResult manualConfiguration,
        CancellationToken cancellationToken) =>
        _reader(
            targetUri,
            manualConfiguration,
            cancellationToken);
}

[SupportedOSPlatform("windows")]
public static class WindowsProxyDirectiveSourceCoordinatorFactory
{
    public static WindowsProxyDirectiveSourceExecutionCoordinator Create(
        Func<CancellationToken,
            Task<WindowsManualProxyConfigurationReadResult>>
            manualConfigurationReader,
        Func<Uri,
            WindowsManualProxyConfigurationReadResult,
            CancellationToken,
            Task<WindowsTargetProxyDecisionReadResult>>
            targetDecisionReader,
        Func<DateTimeOffset>? clock = null)
    {
        ArgumentNullException.ThrowIfNull(manualConfigurationReader);
        ArgumentNullException.ThrowIfNull(targetDecisionReader);

        IWindowsManualProxyConfigurationSource manualSource =
            new DelegateWindowsManualProxyConfigurationSource(
                manualConfigurationReader);
        IWindowsTargetProxyDecisionSource targetSource =
            new DelegateWindowsTargetProxyDecisionSource(
                targetDecisionReader);
        WindowsProxyDirectiveSourceSnapshotReader snapshotReader =
            clock is null
                ? new WindowsProxyDirectiveSourceSnapshotReader(
                    manualSource,
                    targetSource)
                : new WindowsProxyDirectiveSourceSnapshotReader(
                    manualSource,
                    targetSource,
                    clock);
        return new WindowsProxyDirectiveSourceExecutionCoordinator(
            snapshotReader);
    }
}
