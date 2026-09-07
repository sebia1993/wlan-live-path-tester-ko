using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WlanLivePathTester.Core.Models;
using WlanLivePathTester.Core.Operations;
using WlanLivePathTester.Core.Reporting;
using WlanLivePathTester.Core.Routing;
using WlanLivePathTester.Core.Security;
using WlanLivePathTester.Windows.Routing;
using WlanLivePathTester.Windows.Wlan;

namespace WlanLivePathTester.App;

public partial class MainWindow
{
    private readonly InternalProxyRouteComparisonCoordinator _routeComparisonCoordinatorV3 = new();
    private TabItem? _routeComparisonTabV3;
    private TextBox? _routeComparisonInternalTargetV3;
    private TextBox? _routeComparisonExternalTargetV3;
    private TextBox? _routeComparisonProxyDirectiveV3;
    private TextBox? _routeComparisonResultV3;
    private Button? _routeComparisonStartV3;
    private Button? _routeComparisonCancelV3;
    private InternalProxyRouteComparisonRunResult? _latestRouteComparisonRunV3;
    private bool _routeComparisonTabAddedV3;

    internal InternalProxyRouteComparisonRunResult? LatestRouteComparisonRunV3 => _latestRouteComparisonRunV3;
    internal Func<string, string, Uri?, CancellationToken, Task<InternalProxyRouteComparisonRunResult>>?
        CompareManualRouteOverride { get; set; }

    internal void EnsureRouteComparisonTabV3()
    {
        Dispatcher.VerifyAccess();
        if (_routeComparisonTabAddedV3 || _applicationOperationWindowClosed) return;
        TabControl? tabs = FindApplicationTabControl();
        if (tabs is null) return;
        _routeComparisonTabV3 = CreateRouteComparisonTabV3();
        tabs.Items.Add(_routeComparisonTabV3);
        _routeComparisonTabAddedV3 = true;
        Closed += OnRouteComparisonWindowClosedV3;
    }

    private TabItem CreateRouteComparisonTabV3()
    {
        _routeComparisonInternalTargetV3 = CreateRouteInputV3(false,
            InternalProxyRouteComparisonCoordinator.MaximumInternalTargetLength, 38,
            "회사 정책상 DIRECT인 승인된 내부 URL, 호스트 또는 IP를 입력하십시오.");
        _routeComparisonExternalTargetV3 = CreateRouteInputV3(false, 2048, 38,
            "프록시 지시문이 적용되는 절대 HTTP 또는 HTTPS URL을 입력하십시오.");
        _routeComparisonProxyDirectiveV3 = CreateRouteInputV3(true, 16 * 1024, 118,
            "예: PROXY proxy.example:8080; DIRECT 또는 http=host:port;https=host:port");
        _routeComparisonStartV3 = new Button
        {
            Content = "내부↔프록시 경로 비교", MinWidth = 205, Padding = new Thickness(13, 8, 13, 8)
        };
        _routeComparisonStartV3.Click += OnStartRouteComparisonV3;
        _routeComparisonCancelV3 = new Button
        {
            Content = "경로 확인 중지", MinWidth = 125, Padding = new Thickness(13, 8, 13, 8), IsEnabled = false
        };
        _routeComparisonCancelV3.Click += OnCancelRouteComparisonV3;
        _routeComparisonResultV3 = new TextBox
        {
            MinHeight = 430, Padding = new Thickness(12), FontFamily = new FontFamily("Consolas"),
            FontSize = 13, IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Background = Brushes.White,
            Text = "아직 비교하지 않았습니다. 승인된 내부 DIRECT 대상, 외부 HTTP(S) URL과 해당 URL에 적용되는 프록시 지시문을 입력하십시오."
        };
        StackPanel form = new();
        form.Children.Add(CreateRouteLabelV3("내부 DIRECT 기준 대상"));
        form.Children.Add(_routeComparisonInternalTargetV3);
        form.Children.Add(CreateRouteHintV3("URL·호스트·IPv4·IPv6를 지원합니다. 회사 정책상 프록시를 우회하는 승인된 내부 대상만 사용하십시오."));
        form.Children.Add(CreateRouteLabelV3("외부 프록시 판정 대상 URL", 16));
        form.Children.Add(_routeComparisonExternalTargetV3);
        form.Children.Add(CreateRouteHintV3("프록시 매핑의 http=·https= 적용 범위를 선택하기 위해 필요합니다. 절대 HTTP(S) URL만 허용합니다."));
        form.Children.Add(CreateRouteLabelV3("프록시 지시문 또는 PAC/WPAD 판정 결과", 16));
        form.Children.Add(_routeComparisonProxyDirectiveV3);
        form.Children.Add(CreateRouteHintV3("PROXY·HTTPS·SOCKS·DIRECT fallback과 Windows 프로토콜별 수동 프록시 형식을 지원합니다. 입력 원문은 자동 저장하거나 업로드하지 않습니다."));
        WrapPanel buttons = new() { Margin = new Thickness(0, 16, 0, 0) };
        buttons.Children.Add(_routeComparisonStartV3);
        buttons.Children.Add(new Border { Width = 10 });
        buttons.Children.Add(_routeComparisonCancelV3);
        form.Children.Add(buttons);
        StackPanel content = new();
        content.Children.Add(new TextBlock
        {
            FontSize = 22, FontWeight = FontWeights.SemiBold, Text = "내부 DIRECT ↔ 프록시 로컬 경로 비교"
        });
        content.Children.Add(new TextBlock
        {
            Margin = new Thickness(0, 8, 0, 0), Foreground = Brushes.DarkSlateGray, TextWrapping = TextWrapping.Wrap,
            Text = "사용자가 비교 버튼을 누르면 입력과 프록시 경로를 검증하고 필요한 경우에만 운영체제 DNS와 Windows 최적 로컬 인터페이스를 확인합니다. HTTP 다운로드·프록시 로그인·외부 API·결과 업로드는 수행하지 않습니다."
        });
        content.Children.Add(CreateRouteNoticeV3(
            "Ready는 내부와 프록시까지의 첫 로컬 NIC가 같다는 뜻입니다. Diverged는 서로 다르지만 VPN·터널·유선 우선순위 또는 의도된 분할 라우팅일 수 있으므로 자동 장애로 확정하지 않습니다.",
            Color.FromRgb(255, 248, 231), Color.FromRgb(232, 206, 138)));
        content.Children.Add(CreateRouteNoticeV3(
            "결과에는 내부·외부 URL, 프록시 호스트, 전체 인터페이스 GUID·이름·설명 대신 검증된 상태·개수와 SHA-256 앞 10자리 인터페이스 지문만 사용합니다.",
            Color.FromRgb(232, 246, 243), Color.FromRgb(115, 198, 182)));
        content.Children.Add(new Border
        {
            Margin = new Thickness(0, 16, 0, 0), Padding = new Thickness(16), CornerRadius = new CornerRadius(8),
            Background = Brushes.White, BorderBrush = Brushes.LightGray, BorderThickness = new Thickness(1), Child = form
        });
        content.Children.Add(new TextBlock
        {
            Margin = new Thickness(0, 18, 0, 8), FontSize = 17, FontWeight = FontWeights.SemiBold, Text = "안전한 비교 결과"
        });
        content.Children.Add(_routeComparisonResultV3);
        return new TabItem
        {
            Header = "경로 비교", Content = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = new Border { Padding = new Thickness(20), Child = content }
            }
        };
    }

    private async void OnStartRouteComparisonV3(object sender, RoutedEventArgs e) =>
        await RunManualRouteComparisonAsync();

    internal Task<bool> RunManualRouteComparisonAsync()
    {
        Dispatcher.VerifyAccess();
        string internalTarget = _routeComparisonInternalTargetV3?.Text ?? string.Empty;
        string externalRaw = _routeComparisonExternalTargetV3?.Text ?? string.Empty;
        string proxyDirective = _routeComparisonProxyDirectiveV3?.Text ?? string.Empty;
        Uri? externalTarget = NetworkInputBoundary.TryHttpUri(externalRaw, out Uri? parsed, out _)
            ? parsed : null;
        var runner = CompareManualRouteOverride;
        return RunRouteUiOperationAsync(ApplicationOperationKind.RouteComparison,
            async token =>
            {
                if (runner is not null) return await runner(internalTarget, proxyDirective, externalTarget, token);
                string? wlan = await Task.Run(ReadCurrentWlanInterfaceIdV3, token);
                token.ThrowIfCancellationRequested();
                return await _routeComparisonCoordinatorV3.RunManualDirectiveAsync(
                    internalTarget, proxyDirective, externalTarget, wlan,
                    dnsTimeoutSeconds: 5, cancellationToken: token);
            },
            ApplyRouteComparisonResult);
    }

    private void ApplyRouteComparisonResult(InternalProxyRouteComparisonRunResult run)
    {
        ArgumentNullException.ThrowIfNull(run);
        _latestRouteComparisonRunV3 = run;
        SetRouteComparisonResultV3(InternalProxyRouteComparisonRunTextRenderer.Render(run), GetRouteComparisonBrushV3(run));
    }

    private void OnCancelRouteComparisonV3(object sender, RoutedEventArgs e) => RequestRouteOperationCancellation();

    private static string? ReadCurrentWlanInterfaceIdV3()
    {
        WlanReadResult wlanRead = NativeWlanReader.ReadCurrent();
        WlanInterfaceIdentityReadResult identityRead = WlanInterfaceIdentityReader.ReadCurrent();
        WlanSnapshot? connected = WlanInterfaceIdentityReader.AttachIdentity(wlanRead.FirstConnectedInterface, identityRead);
        return connected?.InterfaceId;
    }

    private void SetRouteComparisonBusyV3(bool isBusy)
    {
        if (_routeComparisonStartV3 is not null) _routeComparisonStartV3.IsEnabled = !isBusy;
        if (_routeComparisonCancelV3 is not null) _routeComparisonCancelV3.IsEnabled = isBusy;
        if (_routeComparisonInternalTargetV3 is not null) _routeComparisonInternalTargetV3.IsEnabled = !isBusy;
        if (_routeComparisonExternalTargetV3 is not null) _routeComparisonExternalTargetV3.IsEnabled = !isBusy;
        if (_routeComparisonProxyDirectiveV3 is not null) _routeComparisonProxyDirectiveV3.IsEnabled = !isBusy;
    }

    private void SetRouteComparisonResultV3(string text, Brush brush)
    {
        if (_applicationOperationWindowClosed || _routeComparisonResultV3 is null) return;
        _routeComparisonResultV3.Text = text;
        _routeComparisonResultV3.Foreground = brush;
        _routeComparisonResultV3.ScrollToHome();
    }

    private static Brush GetRouteComparisonBrushV3(InternalProxyRouteComparisonRunResult run) =>
        run.Status == InternalProxyRouteComparisonRunStatus.Completed
            ? run.Comparison?.Status switch
            {
                InternalProxyRouteComparisonStatus.Ready => Brushes.DarkGreen,
                InternalProxyRouteComparisonStatus.Diverged => Brushes.DarkBlue,
                InternalProxyRouteComparisonStatus.Ambiguous => Brushes.DarkOrange,
                _ => Brushes.DarkRed
            }
            : run.Status switch
            {
                InternalProxyRouteComparisonRunStatus.DirectPathSelected => Brushes.DarkBlue,
                InternalProxyRouteComparisonRunStatus.Canceled => Brushes.DarkOrange,
                InternalProxyRouteComparisonRunStatus.ProxySourceUnavailable => Brushes.DarkSlateGray,
                _ => Brushes.DarkRed
            };

    private void OnRouteComparisonWindowClosedV3(object? sender, EventArgs e)
    {
        if (_routeComparisonStartV3 is not null) _routeComparisonStartV3.Click -= OnStartRouteComparisonV3;
        if (_routeComparisonCancelV3 is not null) _routeComparisonCancelV3.Click -= OnCancelRouteComparisonV3;
        Closed -= OnRouteComparisonWindowClosedV3;
    }

    private static TextBox CreateRouteInputV3(bool acceptsReturn, int maxLength, double minimumHeight, string toolTip) => new()
    {
        MinHeight = minimumHeight, MaxLength = maxLength, Padding = new Thickness(8, 6, 8, 6),
        AcceptsReturn = acceptsReturn, TextWrapping = acceptsReturn ? TextWrapping.Wrap : TextWrapping.NoWrap,
        VerticalScrollBarVisibility = acceptsReturn ? ScrollBarVisibility.Auto : ScrollBarVisibility.Hidden, ToolTip = toolTip
    };
    private static TextBlock CreateRouteLabelV3(string text, double topMargin = 0) => new()
    {
        Margin = new Thickness(0, topMargin, 0, 6), FontWeight = FontWeights.SemiBold, Text = text
    };
    private static TextBlock CreateRouteHintV3(string text) => new()
    {
        Margin = new Thickness(0, 5, 0, 0), Foreground = Brushes.DarkSlateGray, TextWrapping = TextWrapping.Wrap, Text = text
    };
    private static Border CreateRouteNoticeV3(string text, Color background, Color border) => new()
    {
        Margin = new Thickness(0, 12, 0, 0), Padding = new Thickness(14), CornerRadius = new CornerRadius(8),
        Background = new SolidColorBrush(background), BorderBrush = new SolidColorBrush(border), BorderThickness = new Thickness(1),
        Child = new TextBlock { TextWrapping = TextWrapping.Wrap, Text = text }
    };
    private static T? FindRouteComparisonDescendantV3<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T value) return value;
        if (root is MainWindow window && typeof(T) == typeof(TabControl))
            return window.FindApplicationTabControl() as T;
        return FindVisualDescendant<T>(root);
    }
}
