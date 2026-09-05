using System.Diagnostics.CodeAnalysis;
using System.Runtime.Versioning;
using WlanLivePathTester.Core.Models;
using WlanLivePathTester.Core.Proxy;
using WlanLivePathTester.Core.Routing;

namespace WlanLivePathTester.Windows.Proxy;

public enum WindowsRouteProxyImportStatus
{
    Ready,
    Direct,
    NeedsAutomaticLookupConsent,
    NoConfiguredProxy,
    InvalidInput,
    ConfigurationReadFailed,
    AutomaticResolutionFailed,
    UnsafeOrUnsupportedResult,
    Canceled,
    Busy,
    Failed
}

// The raw URL, snapshot and selection are private, not serializable properties.
public sealed class WindowsRouteProxyImportResult
{
    private readonly string? _targetKey;
    private readonly ProxyDirectiveSourceSelectionResult? _selection;
    private readonly TimeProvider _clock;
    private readonly long _timestamp;

    internal WindowsRouteProxyImportResult(
        WindowsRouteProxyImportStatus status,
        ProxyConfigurationSource source,
        Uri? target,
        ProxyDirectiveSourceSelectionResult? selection,
        bool automaticLookupAttempted,
        bool autoLogonRetried,
        bool wasBypassed,
        TimeProvider clock)
    {
        Status = status;
        Source = source;
        _targetKey = target?.AbsoluteUri;
        _selection = selection;
        _clock = clock;
        _timestamp = clock.GetTimestamp();
        CapturedAt = clock.GetUtcNow();
        AutomaticLookupAttempted = automaticLookupAttempted;
        AutoLogonRetried = autoLogonRetried;
        WasBypassed = wasBypassed;
        ProxyEndpointCount = selection?.ProxyEndpointCount ?? 0;
        DirectDirectiveCount = selection?.DirectDirectiveCount ?? 0;
    }

    public WindowsRouteProxyImportStatus Status { get; }
    public ProxyConfigurationSource Source { get; }
    public DateTimeOffset CapturedAt { get; }
    public bool AutomaticLookupAttempted { get; }
    public bool AutoLogonRetried { get; }
    public bool WasBypassed { get; }
    public int ProxyEndpointCount { get; }
    public int DirectDirectiveCount { get; }
    public bool HasSelection => _selection is not null;

    public string Message => Status switch
    {
        WindowsRouteProxyImportStatus.Ready => "현재 외부 대상의 Windows 프록시 후보를 불러왔습니다. 주소 원문은 표시하지 않습니다.",
        WindowsRouteProxyImportStatus.Direct => "현재 외부 대상의 판정은 DIRECT입니다. 다른 URL에도 적용되는 전역 판정은 아닙니다.",
        WindowsRouteProxyImportStatus.NeedsAutomaticLookupConsent => "PAC/WPAD가 설정되어 있습니다. 자동 판정 조회에 동의해야 불러올 수 있으며 수동 설정으로 대체하지 않았습니다.",
        WindowsRouteProxyImportStatus.NoConfiguredProxy => "사용 가능한 프록시 설정이 없습니다. 이를 DIRECT로 추정하지 않았습니다.",
        WindowsRouteProxyImportStatus.InvalidInput => "사용자 정보·fragment·제어 문자 없는 2048자 이하의 절대 HTTP(S) URL과 1000~30000ms 제한 시간이 필요합니다.",
        WindowsRouteProxyImportStatus.ConfigurationReadFailed => "Windows 프록시 설정을 읽지 못했습니다. DIRECT로 추정하지 않았습니다.",
        WindowsRouteProxyImportStatus.AutomaticResolutionFailed => "자동 프록시 판정이 실패했거나 판정 중 설정 출처가 바뀌었습니다. 수동 fallback 결과는 적용하지 않았습니다.",
        WindowsRouteProxyImportStatus.UnsafeOrUnsupportedResult => "판정 일부가 해석되지 않았거나 현재 가져오기 방식에서 순서를 안전하게 보존할 수 없습니다. 일부 후보만으로 대체하지 않았습니다.",
        WindowsRouteProxyImportStatus.Canceled => "불러오기를 취소했습니다. 네이티브 호출이 있었다면 반환 후 결과를 폐기했습니다.",
        WindowsRouteProxyImportStatus.Busy => "기존 프록시 불러오기가 아직 종료되지 않았습니다.",
        _ => "프록시 불러오기를 완료하지 못했습니다. 원문 입력과 예외 메시지는 표시하지 않았습니다."
    };

    public override string ToString() =>
        $"{Status} · {Source} · 프록시 후보 {ProxyEndpointCount}개 · DIRECT {DirectDirectiveCount}개";

    public bool TryGetSelection(
        Uri? target,
        [NotNullWhen(true)] out ProxyDirectiveSourceSelectionResult? selection)
    {
        selection = null;
        if (_selection is null || !WindowsRouteProxyImporter.IsValidTarget(target)
            || !string.Equals(_targetKey, target!.AbsoluteUri, StringComparison.Ordinal))
        {
            return false;
        }

        TimeSpan age = _clock.GetElapsedTime(_timestamp);
        if (age < TimeSpan.Zero || age >= TimeSpan.FromMinutes(5))
        {
            return false;
        }

        selection = _selection;
        return true;
    }
}

[SupportedOSPlatform("windows")]
public sealed class WindowsRouteProxyImporter
{
    private readonly Func<CurrentUserProxyConfiguration> _readConfiguration;
    private readonly Func<Uri, int, ResolvedProxyRoute> _resolveTarget;
    private readonly TimeProvider _clock;
    private int _running;

    public WindowsRouteProxyImporter()
        : this(
            CurrentUserProxySettingsReader.ReadRaw,
            (target, timeout) => ProxyRouteResolver.ResolveDetailed(
                target.AbsoluteUri, NetworkPathKind.External, timeout))
    {
    }

    internal WindowsRouteProxyImporter(
        Func<CurrentUserProxyConfiguration> readConfiguration,
        Func<Uri, int, ResolvedProxyRoute> resolveTarget,
        TimeProvider? clock = null)
    {
        _readConfiguration = readConfiguration
            ?? throw new ArgumentNullException(nameof(readConfiguration));
        _resolveTarget = resolveTarget
            ?? throw new ArgumentNullException(nameof(resolveTarget));
        _clock = clock ?? TimeProvider.System;
    }

    public async Task<WindowsRouteProxyImportResult> ImportAsync(
        Uri? target,
        bool allowAutomaticProxyLookup = false,
        int timeoutMilliseconds = 5000,
        CancellationToken cancellationToken = default)
    {
        if (!IsValidTarget(target) || timeoutMilliseconds is < 1000 or > 30000)
        {
            return Result(WindowsRouteProxyImportStatus.InvalidInput);
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return Result(WindowsRouteProxyImportStatus.Canceled);
        }

        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0)
        {
            return Result(WindowsRouteProxyImportStatus.Busy);
        }

        try
        {
            // Never abandon a synchronous WinHTTP call on cancellation. It owns
            // native resources until it returns; the shared busy gate stays held.
            return await Task.Run(
                () => Capture(target!, allowAutomaticProxyLookup,
                    timeoutMilliseconds, cancellationToken),
                CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            Volatile.Write(ref _running, 0);
        }
    }

    internal static bool IsValidTarget(Uri? target) =>
        target is { IsAbsoluteUri: true }
        && target.Scheme is "http" or "https"
        && target.OriginalString.Length <= 2048
        && !target.OriginalString.Any(char.IsControl)
        && target.OriginalString == target.OriginalString.Trim()
        && string.IsNullOrEmpty(target.UserInfo)
        && string.IsNullOrEmpty(target.Fragment)
        && !string.IsNullOrWhiteSpace(target.Host);

    private WindowsRouteProxyImportResult Capture(
        Uri target, bool allowAutomatic, int timeout, CancellationToken token)
    {
        bool attempted = false;
        bool autoLogonRetried = false;
        try
        {
            token.ThrowIfCancellationRequested();
            CurrentUserProxyConfiguration configuration = _readConfiguration();
            token.ThrowIfCancellationRequested();
            if (!configuration.ReadSucceeded)
            {
                return Result(WindowsRouteProxyImportStatus.ConfigurationReadFailed);
            }

            bool automatic = configuration.AutoDetectEnabled
                || !string.IsNullOrWhiteSpace(configuration.AutoConfigUrl);
            if (!automatic)
            {
                return ImportManual(target, configuration);
            }

            if (!allowAutomatic)
            {
                return Result(WindowsRouteProxyImportStatus.NeedsAutomaticLookupConsent);
            }

            token.ThrowIfCancellationRequested();
            attempted = true;
            ResolvedProxyRoute resolved = _resolveTarget(target, timeout);
            autoLogonRetried = resolved.Summary.AutoLogonRetried;
            token.ThrowIfCancellationRequested();
            ProxyRouteResolution summary = resolved.Summary;
            // The existing measurement resolver may offer ManualFallback. Do not
            // treat that fallback (or a changed configuration) as a PAC decision.
            if (!summary.IsSuccess || summary.Source is not (
                ProxyConfigurationSource.Pac or ProxyConfigurationSource.Wpad
                or ProxyConfigurationSource.WpadThenPac))
            {
                return Result(WindowsRouteProxyImportStatus.AutomaticResolutionFailed,
                    attempted: attempted, autoLogon: autoLogonRetried);
            }

            ProxySelection route = resolved.Selection;
            if (route.Hops.Count is < 1 or > 32
                || route.InvalidDirectiveCount != 0 || summary.InvalidDirectiveCount != 0
                || route.RouteKind != summary.RouteKind
                || route.ProxyCandidateCount != summary.ProxyCandidateCount
                || (route.RouteKind == ProxyRouteKind.Direct && route.ProxyCandidateCount > 0))
            {
                return Result(WindowsRouteProxyImportStatus.UnsafeOrUnsupportedResult,
                    attempted: attempted, autoLogon: autoLogonRetried);
            }

            List<string> directives = [];
            foreach (ProxyRouteHop hop in route.Hops)
            {
                if (hop.Kind == ProxyRouteKind.Direct && hop.ProxyUri is null)
                {
                    directives.Add("DIRECT");
                }
                else if (hop.Kind == ProxyRouteKind.Proxy
                    && Uri.TryCreate(hop.ProxyUri, UriKind.Absolute, out Uri? proxy)
                    && proxy.Scheme is "http" or "https"
                    && string.IsNullOrEmpty(proxy.UserInfo)
                    && string.IsNullOrEmpty(proxy.Query)
                    && string.IsNullOrEmpty(proxy.Fragment)
                    && proxy.AbsolutePath == "/"
                    && proxy.Port is >= 1 and <= 65535)
                {
                    // Use the existing resolver's normalized endpoint, not its
                    // free-form message or a reconstructed guess at the PAC script.
                    directives.Add(proxy.GetLeftPart(UriPartial.Authority));
                }
                else
                {
                    return Result(WindowsRouteProxyImportStatus.UnsafeOrUnsupportedResult,
                        attempted: attempted, autoLogon: autoLogonRetried);
                }
            }

            ProxyDirectiveSourceSnapshot snapshot = new(
                _clock.GetUtcNow(), ProxyDirectiveSourceReadStatus.Success,
                route.RouteKind == ProxyRouteKind.Direct, string.Join("; ", directives),
                ProxyDirectiveSourceReadStatus.NotAttempted, false, null,
                configuration.AutoDetectEnabled,
                !string.IsNullOrWhiteSpace(configuration.AutoConfigUrl));
            return FromSnapshot(target, snapshot, summary.Source,
                attempted, autoLogonRetried, wasBypassed: false);
        }
        catch (OperationCanceledException)
        {
            return Result(WindowsRouteProxyImportStatus.Canceled,
                attempted: attempted, autoLogon: autoLogonRetried);
        }
        catch (Exception)
        {
            return Result(WindowsRouteProxyImportStatus.Failed,
                attempted: attempted, autoLogon: autoLogonRetried);
        }
    }

    private WindowsRouteProxyImportResult ImportManual(
        Uri target, CurrentUserProxyConfiguration configuration)
    {
        if (string.IsNullOrWhiteSpace(configuration.ManualProxy))
        {
            return Result(WindowsRouteProxyImportStatus.NoConfiguredProxy);
        }

        // Reuse the existing local bypass matcher through SelectManual. The
        // actual endpoint list remains the strict parser's original input.
        ProxySelection manual = ProxyDirectiveParser.SelectManual(
            target, configuration.ManualProxy, configuration.BypassList);
        ProxyDirectiveSourceSnapshot snapshot = new(
            _clock.GetUtcNow(), ProxyDirectiveSourceReadStatus.NotAttempted,
            false, null, ProxyDirectiveSourceReadStatus.Success, true,
            manual.WasBypassed ? "DIRECT" : configuration.ManualProxy,
            false, false);
        return FromSnapshot(target, snapshot, ProxyConfigurationSource.Manual,
            attempted: false, autoLogon: false, manual.WasBypassed);
    }

    private WindowsRouteProxyImportResult FromSnapshot(
        Uri target, ProxyDirectiveSourceSnapshot snapshot,
        ProxyConfigurationSource source, bool attempted, bool autoLogon, bool wasBypassed)
    {
        ProxyDirectiveSourceSelectionResult selection =
            ProxyDirectiveSourceSnapshotSelectionPolicy.Select(snapshot);
        ProxyEndpointParseResult parsed = ProxyEndpointParser.Parse(
            selection.SelectedDirectiveText, target);
        if (!selection.HasUsableSelection || !parsed.IsUsable || parsed.Errors.Count > 0
            || selection.ParseResult?.Issues.Any(issue =>
                issue.Severity == ProxyDirectiveIssueSeverity.Error) == true)
        {
            return Result(WindowsRouteProxyImportStatus.UnsafeOrUnsupportedResult,
                attempted: attempted, autoLogon: autoLogon);
        }

        bool direct = parsed.Decision is ProxyEndpointDecision.Direct
            or ProxyEndpointDecision.DirectWithProxyAlternatives;
        return Result(direct ? WindowsRouteProxyImportStatus.Direct
            : WindowsRouteProxyImportStatus.Ready,
            source, target, selection, attempted, autoLogon, wasBypassed);
    }

    private WindowsRouteProxyImportResult Result(
        WindowsRouteProxyImportStatus status,
        ProxyConfigurationSource source = ProxyConfigurationSource.Unknown,
        Uri? target = null, ProxyDirectiveSourceSelectionResult? selection = null,
        bool attempted = false, bool autoLogon = false, bool wasBypassed = false) =>
        new(status, source, target, selection, attempted, autoLogon, wasBypassed, _clock);
}
