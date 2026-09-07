using System.Windows;
using System.Windows.Controls;
using WlanLivePathTester.Core.Operations;
using WlanLivePathTester.Core.Proxy;
using WlanLivePathTester.Core.Routing;
using WlanLivePathTester.Core.Security;
using WlanLivePathTester.Windows.Proxy;

namespace WlanLivePathTester.App;

public partial class MainWindow
{
    private readonly WindowsRouteProxyImporter _windowsRouteProxyImporter = new();
    private WindowsRouteProxyImportResult? _importedRouteProxy;
    private Button? _importRouteProxyButton;
    private Button? _compareImportedRouteProxyButton;
    private CheckBox? _allowAutomaticRouteProxy;
    private TextBlock? _importedRouteProxySummary;
    private bool _routeProxyImportAttached;
    private long _routeProxyTargetRevision;

    internal Func<Uri?, bool, CancellationToken, Task<WindowsRouteProxyImportResult>>? ImportRouteProxyOverride { get; set; }
    internal Func<string, ProxyDirectiveSourceSelectionResult, Uri?, CancellationToken,
        Task<InternalProxyRouteComparisonRunResult>>? CompareImportedRouteOverride { get; set; }

    internal void EnsureRouteProxyImportControls()
    {
        Dispatcher.VerifyAccess();
        if (_routeProxyImportAttached || _applicationOperationWindowClosed
            || _routeComparisonProxyDirectiveV3?.Parent is not Panel form
            || _routeComparisonExternalTargetV3 is null || _routeComparisonStartV3 is null) return;
        _allowAutomaticRouteProxy = new CheckBox
        {
            IsChecked = false, Margin = new Thickness(0, 8, 0, 8),
            Content = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Text = "PAC/WPAD 조회·스크립트 획득 및 필요 시 Windows 통합 인증을 허용합니다."
            },
            ToolTip = "기본값은 로컬 설정 읽기입니다. 동기 Windows 조회는 취소·제한 시간 이후에도 실제 반환까지 기다립니다."
        };
        _importRouteProxyButton = new Button
        {
            Content = "Windows 프록시 불러오기", Padding = new Thickness(12, 8, 12, 8), Margin = new Thickness(0, 0, 8, 0)
        };
        _compareImportedRouteProxyButton = new Button
        {
            Content = "불러온 Windows 판정으로 비교", Padding = new Thickness(12, 8, 12, 8), IsEnabled = false
        };
        _importedRouteProxySummary = new TextBlock
        {
            Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap,
            Text = "아직 불러오지 않았습니다. 가져오기는 아래 수동 입력을 덮어쓰지 않습니다."
        };
        _importRouteProxyButton.Click += OnImportRouteProxyClick;
        _compareImportedRouteProxyButton.Click += OnCompareImportedRouteProxyClick;
        _routeComparisonExternalTargetV3.TextChanged += OnRouteProxyTargetChanged;
        _routeComparisonStartV3.Content = "수동 입력으로 비교";
        WrapPanel buttons = new();
        buttons.Children.Add(_importRouteProxyButton);
        buttons.Children.Add(_compareImportedRouteProxyButton);
        StackPanel panel = new();
        panel.Children.Add(new TextBlock { FontWeight = FontWeights.SemiBold, Text = "Windows 프록시 판정 가져오기" });
        panel.Children.Add(_allowAutomaticRouteProxy);
        panel.Children.Add(buttons);
        panel.Children.Add(_importedRouteProxySummary);
        panel.Children.Add(CreateRouteHintV3(
            "불러온 판정은 동일 URL에서 5분 이내에만 사용합니다. URL·네트워크·VPN·프록시 정책 변경 시 다시 불러오십시오. 실패 시 수동 입력으로 자동 전환하지 않습니다."));
        form.Children.Insert(Math.Max(0, form.Children.IndexOf(_routeComparisonProxyDirectiveV3) - 1),
            new Border { Margin = new Thickness(0, 16, 0, 8), Padding = new Thickness(12), Child = panel });
        Closed += OnRouteProxyImportClosed;
        _routeProxyImportAttached = true;
        UpdateRouteProxyImportControls();
    }

    private async void OnImportRouteProxyClick(object sender, RoutedEventArgs e) => await ImportRouteProxyAsync();

    internal Task<bool> ImportRouteProxyAsync()
    {
        Dispatcher.VerifyAccess();
        Uri? target = ReadRouteProxyTarget();
        bool allowAutomatic = _allowAutomaticRouteProxy?.IsChecked == true;
        long revision = _routeProxyTargetRevision;
        var importer = ImportRouteProxyOverride;
        return RunRouteUiOperationAsync(ApplicationOperationKind.WindowsProxyImport,
            async token =>
            {
                _importedRouteProxy = null;
                token.ThrowIfCancellationRequested();
                return importer is not null
                    ? await importer(target, allowAutomatic, token)
                    : await _windowsRouteProxyImporter.ImportAsync(target, allowAutomatic,
                        timeoutMilliseconds: 5000, cancellationToken: token);
            },
            result =>
            {
                ArgumentNullException.ThrowIfNull(result);
                if (revision != _routeProxyTargetRevision)
                {
                    _importedRouteProxy = null;
                    SetRouteProxyImportSummary("조회 중 외부 URL이 변경되어 반환된 판정을 폐기했습니다. 다시 불러오십시오.");
                    return;
                }
                _importedRouteProxy = result.HasSelection ? result : null;
                SetRouteProxyImportSummary(
                    $"{result}\n{result.Message}\n자동 판정 호출: {(result.AutomaticLookupAttempted ? "수행" : "미수행")} · Windows 통합 인증 재시도: {(result.AutoLogonRetried ? "있음" : "없음")} · 수동 bypass: {(result.WasBypassed ? "적용" : "미적용")}");
            });
    }

    private async void OnCompareImportedRouteProxyClick(object sender, RoutedEventArgs e) => await CompareImportedRouteAsync();

    internal Task<bool> CompareImportedRouteAsync()
    {
        Dispatcher.VerifyAccess();
        if (_applicationOperationWindowClosed || _routeOperationRunning) return Task.FromResult(false);
        Uri? target = ReadRouteProxyTarget();
        WindowsRouteProxyImportResult? imported = _importedRouteProxy;
        if (imported is null || !imported.TryGetSelection(target, out _))
        {
            _importedRouteProxy = null;
            SetRouteProxyImportSummary("같은 URL의 유효한 Windows 판정이 없습니다. 다시 불러오십시오. 수동 입력으로 자동 대체하지 않았습니다.");
            UpdateRouteProxyImportControls();
            return Task.FromResult(false);
        }
        string internalTarget = _routeComparisonInternalTargetV3?.Text ?? string.Empty;
        long revision = _routeProxyTargetRevision;
        var runner = CompareImportedRouteOverride;
        return RunRouteUiOperationAsync<InternalProxyRouteComparisonRunResult?>(ApplicationOperationKind.RouteComparison,
            async token =>
            {
                token.ThrowIfCancellationRequested();
                if (!imported.TryGetSelection(target, out ProxyDirectiveSourceSelectionResult? selection)) return null;
                if (runner is not null) return await runner(internalTarget, selection, target, token);
                string? wlan = await Task.Run(ReadCurrentWlanInterfaceIdV3, token);
                token.ThrowIfCancellationRequested();
                if (!imported.TryGetSelection(target, out selection)) return null;
                return await _routeComparisonCoordinatorV3.RunAsync(internalTarget, selection,
                    target, wlan, dnsTimeoutSeconds: 5, cancellationToken: token);
            },
            run =>
            {
                if (run is null || revision != _routeProxyTargetRevision)
                {
                    _importedRouteProxy = null;
                    SetRouteProxyImportSummary("판정이 만료됐거나 URL이 변경됐습니다. 결과를 적용하지 않았습니다. 다시 불러오십시오.");
                    return;
                }
                ApplyRouteComparisonResult(run);
            });
    }

    private Uri? ReadRouteProxyTarget()
    {
        string raw = _routeComparisonExternalTargetV3?.Text ?? string.Empty;
        return NetworkInputBoundary.TryHttpUri(raw, out Uri? target, out _) ? target : null;
    }

    private void OnRouteProxyTargetChanged(object sender, TextChangedEventArgs e)
    {
        _routeProxyTargetRevision++;
        _importedRouteProxy = null;
        SetRouteProxyImportSummary("외부 URL이 변경되어 이전 Windows 판정을 폐기했습니다. 다시 불러오십시오.");
        UpdateRouteProxyImportControls();
    }

    private void UpdateRouteProxyImportControls()
    {
        if (_applicationOperationWindowClosed) return;
        bool idle = !_routeOperationRunning && !_applicationOperationClosePending
            && !CurrentApplicationOperation.ShutdownRequested;
        if (_importRouteProxyButton is not null) _importRouteProxyButton.IsEnabled = idle;
        if (_allowAutomaticRouteProxy is not null) _allowAutomaticRouteProxy.IsEnabled = idle;
        if (_compareImportedRouteProxyButton is not null)
            _compareImportedRouteProxyButton.IsEnabled = idle && _importedRouteProxy?.HasSelection == true;
    }

    private void SetRouteProxyImportSummary(string text)
    {
        if (!_applicationOperationWindowClosed && _importedRouteProxySummary is not null)
            _importedRouteProxySummary.Text = text;
    }

    private void OnRouteProxyImportClosed(object? sender, EventArgs e)
    {
        _importedRouteProxy = null;
        if (_routeComparisonExternalTargetV3 is not null)
            _routeComparisonExternalTargetV3.TextChanged -= OnRouteProxyTargetChanged;
        if (_importRouteProxyButton is not null) _importRouteProxyButton.Click -= OnImportRouteProxyClick;
        if (_compareImportedRouteProxyButton is not null) _compareImportedRouteProxyButton.Click -= OnCompareImportedRouteProxyClick;
        Closed -= OnRouteProxyImportClosed;
    }
}
