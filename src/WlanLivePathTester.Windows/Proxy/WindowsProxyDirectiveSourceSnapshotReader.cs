using System.Runtime.Versioning;
using System.Text.Json.Serialization;
using WlanLivePathTester.Core.Proxy;

namespace WlanLivePathTester.Windows.Proxy;

public sealed record WindowsManualProxyConfigurationReadResult(
    ProxyDirectiveSourceReadStatus Status,
    bool ManualProxyConfigured,
    [property: JsonIgnore] string? ManualProxyDirective,
    bool AutoDetectEnabled,
    bool PacConfigured,
    [property: JsonIgnore] string? PacUrl);

public sealed record WindowsTargetProxyDecisionReadResult(
    ProxyDirectiveSourceReadStatus Status,
    bool IsDirect,
    [property: JsonIgnore] string? DirectiveText);

public interface IWindowsManualProxyConfigurationSource
{
    Task<WindowsManualProxyConfigurationReadResult> ReadAsync(
        CancellationToken cancellationToken);
}

public interface IWindowsTargetProxyDecisionSource
{
    Task<WindowsTargetProxyDecisionReadResult> ReadAsync(
        Uri targetUri,
        WindowsManualProxyConfigurationReadResult manualConfiguration,
        CancellationToken cancellationToken);
}

[SupportedOSPlatform("windows")]
public sealed class WindowsProxyDirectiveSourceSnapshotReader
{
    private readonly IWindowsManualProxyConfigurationSource
        _manualConfigurationSource;
    private readonly IWindowsTargetProxyDecisionSource
        _targetDecisionSource;
    private readonly Func<DateTimeOffset> _clock;

    public WindowsProxyDirectiveSourceSnapshotReader(
        IWindowsManualProxyConfigurationSource manualConfigurationSource,
        IWindowsTargetProxyDecisionSource targetDecisionSource)
        : this(
            manualConfigurationSource,
            targetDecisionSource,
            static () => DateTimeOffset.UtcNow)
    {
    }

    public WindowsProxyDirectiveSourceSnapshotReader(
        IWindowsManualProxyConfigurationSource manualConfigurationSource,
        IWindowsTargetProxyDecisionSource targetDecisionSource,
        Func<DateTimeOffset> clock)
    {
        _manualConfigurationSource = manualConfigurationSource
            ?? throw new ArgumentNullException(
                nameof(manualConfigurationSource));
        _targetDecisionSource = targetDecisionSource
            ?? throw new ArgumentNullException(
                nameof(targetDecisionSource));
        _clock = clock
            ?? throw new ArgumentNullException(nameof(clock));
    }

    public async Task<ProxyDirectiveSourceSnapshot> ReadAsync(
        Uri targetUri,
        CancellationToken cancellationToken = default)
    {
        ValidateTargetUri(targetUri);
        cancellationToken.ThrowIfCancellationRequested();
        DateTimeOffset capturedAt = _clock();

        WindowsManualProxyConfigurationReadResult manual;
        try
        {
            manual = await _manualConfigurationSource.ReadAsync(
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            manual = FailedManualRead();
        }

        manual = NormalizeManualResult(manual);
        if (manual.Status != ProxyDirectiveSourceReadStatus.Success)
        {
            return CreateSnapshot(
                capturedAt,
                targetStatus:
                    ProxyDirectiveSourceReadStatus.NotAttempted,
                targetDecisionIsDirect: false,
                targetDirective: null,
                manual);
        }

        bool targetDecisionRequired = manual.AutoDetectEnabled
            || manual.PacConfigured;
        if (!targetDecisionRequired)
        {
            return CreateSnapshot(
                capturedAt,
                targetStatus:
                    ProxyDirectiveSourceReadStatus.NotAttempted,
                targetDecisionIsDirect: false,
                targetDirective: null,
                manual);
        }

        cancellationToken.ThrowIfCancellationRequested();
        WindowsTargetProxyDecisionReadResult targetDecision;
        try
        {
            targetDecision = await _targetDecisionSource.ReadAsync(
                    targetUri,
                    manual,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            targetDecision = FailedTargetRead();
        }

        targetDecision = NormalizeTargetResult(targetDecision);
        return CreateSnapshot(
            capturedAt,
            targetDecision.Status,
            targetDecision.IsDirect,
            targetDecision.Status
                    == ProxyDirectiveSourceReadStatus.Success
                ? NormalizeOptionalText(targetDecision.DirectiveText)
                : null,
            manual);
    }

    private static void ValidateTargetUri(Uri targetUri)
    {
        ArgumentNullException.ThrowIfNull(targetUri);
        bool supported = targetUri.IsAbsoluteUri
            && (targetUri.Scheme.Equals(
                    Uri.UriSchemeHttp,
                    StringComparison.OrdinalIgnoreCase)
                || targetUri.Scheme.Equals(
                    Uri.UriSchemeHttps,
                    StringComparison.OrdinalIgnoreCase));
        if (!supported)
        {
            throw new ArgumentException(
                "대상별 프록시 판정 URL은 절대 HTTP 또는 HTTPS URL이어야 합니다.",
                nameof(targetUri));
        }
    }

    private static WindowsManualProxyConfigurationReadResult
        NormalizeManualResult(
            WindowsManualProxyConfigurationReadResult? result)
    {
        if (result is null
            || result.Status
                != ProxyDirectiveSourceReadStatus.Success)
        {
            return FailedManualRead();
        }

        bool manualConfigured = result.ManualProxyConfigured;
        return result with
        {
            Status = ProxyDirectiveSourceReadStatus.Success,
            ManualProxyConfigured = manualConfigured,
            ManualProxyDirective = manualConfigured
                ? NormalizeOptionalText(result.ManualProxyDirective)
                : null,
            PacConfigured = result.PacConfigured,
            PacUrl = result.PacConfigured
                ? NormalizeOptionalText(result.PacUrl)
                : null
        };
    }

    private static WindowsTargetProxyDecisionReadResult
        NormalizeTargetResult(
            WindowsTargetProxyDecisionReadResult? result)
    {
        if (result is null
            || result.Status
                != ProxyDirectiveSourceReadStatus.Success)
        {
            return FailedTargetRead();
        }

        return result with
        {
            Status = ProxyDirectiveSourceReadStatus.Success,
            DirectiveText = NormalizeOptionalText(
                result.DirectiveText)
        };
    }

    private static ProxyDirectiveSourceSnapshot CreateSnapshot(
        DateTimeOffset capturedAt,
        ProxyDirectiveSourceReadStatus targetStatus,
        bool targetDecisionIsDirect,
        string? targetDirective,
        WindowsManualProxyConfigurationReadResult manual) =>
        new(
            capturedAt,
            targetStatus,
            targetDecisionIsDirect,
            targetDirective,
            manual.Status,
            manual.ManualProxyConfigured,
            manual.ManualProxyDirective,
            manual.AutoDetectEnabled,
            manual.PacConfigured);

    private static WindowsManualProxyConfigurationReadResult
        FailedManualRead() =>
        new(
            ProxyDirectiveSourceReadStatus.Failed,
            ManualProxyConfigured: false,
            ManualProxyDirective: null,
            AutoDetectEnabled: false,
            PacConfigured: false,
            PacUrl: null);

    private static WindowsTargetProxyDecisionReadResult
        FailedTargetRead() =>
        new(
            ProxyDirectiveSourceReadStatus.Failed,
            IsDirect: false,
            DirectiveText: null);

    private static string? NormalizeOptionalText(string? value)
    {
        string candidate = (value ?? string.Empty).Trim();
        return candidate.Length == 0 ? null : candidate;
    }
}
