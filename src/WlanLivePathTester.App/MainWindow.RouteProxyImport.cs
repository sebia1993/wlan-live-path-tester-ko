using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using WlanLivePathTester.Core.Proxy;
using WlanLivePathTester.Core.Reporting;
using WlanLivePathTester.Core.Routing;
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
    private TaskCompletionSource? _routeProxyOperationCompletion;
    private bool _routeProxyImportAttached;
    private bool _routeProxyWindowClosed;
    private bool _routeProxyClosePending;

    internal void EnsureRouteProxyImportControls()
    {
        if (_routeProxyImportAttached || _routeProxyWindowClosed
            || _routeComparisonProxyDirectiveV3?.Parent is not Panel form
            || _routeComparisonExternalTargetV3 is null
            || _routeComparisonStartV3 is null)
        {
            return;
        }

        _allowAutomaticRouteProxy = new CheckBox
        {
            IsChecked = false,
            Margin = new Thickness(0, 8, 0, 8),
            Content = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Text = "PAC/WPAD 조회·스크립트 획득 및 필요 시 Windows 통합 인증을 허용합니다."
            },
            ToolTip = "기본값은 로컬 설정 읽기만 수행합니다. WinHTTP 단계별 제한 시간은 5초이며 전체 완료 상한이 아닙니다. 동기 네이티브 호출 중 취소는 반환 후 반영됩니다."
        };
        _importRouteProxyButton = new Button
        {
            Content = "Windows 프록시 불러오기",
            Padding = new Thickness(12, 8, 12, 8),
            Margin = new Thickness(0, 0, 8, 0)
        };
        _compareImportedRouteProxyButton = new Button
        {
            Content = "불러온 Windows 판정으로 비교",
            Padding = new Thickness(12, 8, 12, 8),
            IsEnabled = false
        };
        _importedRouteProxySummary = new TextBlock
        {
            Margin = new Thickness(0, 8, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            Text = "아직 불러오지 않았습니다. 가져오기는 아래 수동 입력을 덮어쓰지 않습니다."
        };
        _importRouteProxyButton.Click += OnImportRouteProxyClick;
        _compareImportedRouteProxyButton.Click += OnCompareImportedRouteProxyClick;
        _routeComparisonExternalTargetV3.TextChanged += OnRouteProxyTargetChanged;
        _routeComparisonStartV3.IsEnabledChanged += OnRouteProxyBusyChanged;
        _routeComparisonStartV3.Content = "수동 입력으로 비교";

        WrapPanel buttons = new();
        buttons.Children.Add(_importRouteProxyButton);
        buttons.Children.Add(_compareImportedRouteProxyButton);
        StackPanel panel = new();
        panel.Children.Add(new TextBlock
        {
            FontWeight = FontWeights.SemiBold,
            Text = "Windows 프록시 판정 가져오기"
        });
        panel.Children.Add(_allowAutomaticRouteProxy);
        panel.Children.Add(buttons);
        panel.Children.Add(_importedRouteProxySummary);
        panel.Children.Add(CreateRouteHintV3(
            "불러온 판정은 동일 URL에서 5분 이내에만 사용합니다. URL·네트워크·VPN·프록시 정책 변경 시 다시 불러오십시오. 후보 수는 지시문 기준이며 실제 분석 시 대상 범위를 다시 적용합니다. 실패 시 수동 입력으로 자동 전환하지 않습니다."));
        form.Children.Insert(Math.Max(0, form.Children.IndexOf(_routeComparisonProxyDirectiveV3) - 1),
            new Border
            {
                Margin = new Thickness(0, 16, 0, 8),
                Padding = new Thickness(12),
                BorderThickness = new Thickness(1),
                BorderBrush = Brushes.LightGray,
                Child = panel
            });
        Closing += OnRouteProxyImportClosing;
        Closed += OnRouteProxyImportClosed;
        _routeProxyImportAttached = true;
        UpdateRouteProxyImportControls();
    }

    private async void OnImportRouteProxyClick(object sender, RoutedEventArgs e)
    {
        Uri? target = ReadRouteProxyTarget();
        bool allowAutomatic = _allowAutomaticRouteProxy?.IsChecked == true;
        await RunRouteProxyUiOperation(async token =>
        {
            // A failed new import must not leave an earlier successful decision armed.
            _importedRouteProxy = null;
            SetRouteProxyImportSummary("설정을 읽고 있습니다. 취소를 요청해도 네이티브 조회가 반환될 때까지 추가 실행은 차단됩니다.");
            WindowsRouteProxyImportResult result = await _windowsRouteProxyImporter.ImportAsync(
                target, allowAutomatic, timeoutMilliseconds: 5000, cancellationToken: token);
            if (_routeProxyWindowClosed) return;
            if (!token.IsCancellationRequested && result.HasSelection)
            {
                _importedRouteProxy = result;
            }
            SetRouteProxyImportSummary(
                $"{result}\n{result.Message}\n자동 판정 호출: {(result.AutomaticLookupAttempted ? "수행" : "미수행")} · Windows 통합 인증 재시도: {(result.AutoLogonRetried ? "있음" : "없음")} · 수동 bypass: {(result.WasBypassed ? "적용" : "미적용")}");
        });
    }

    private async void OnCompareImportedRouteProxyClick(object sender, RoutedEventArgs e)
    {
        Uri? target = ReadRouteProxyTarget();
        WindowsRouteProxyImportResult? imported = _importedRouteProxy;
        if (imported is null || !imported.TryGetSelection(target, out _))
        {
            _importedRouteProxy = null;
            SetRouteProxyImportSummary("같은 URL의 유효한 Windows 판정이 없습니다. 다시 불러오십시오. 수동 입력으로 자동 대체하지 않았습니다.");
            UpdateRouteProxyImportControls();
            return;
        }

        string internalTarget = _routeComparisonInternalTargetV3?.Text.Trim() ?? string.Empty;
        await RunRouteProxyUiOperation(async token =>
        {
            string? wlanId = await Task.Run(ReadCurrentWlanInterfaceIdV3, token);
            token.ThrowIfCancellationRequested();
            if (!imported.TryGetSelection(target, out ProxyDirectiveSourceSelectionResult? selection))
            {
                _importedRouteProxy = null;
                SetRouteProxyImportSummary("판정이 만료됐습니다. 다시 불러오십시오.");
                return;
            }

            // Preserve TargetSpecificAutoProxy / Manual provenance. Never send an
            // imported PAC result through the manual-entry coordinator overload.
            InternalProxyRouteComparisonRunResult run = await _routeComparisonCoordinatorV3.RunAsync(
                internalTarget, selection, target, wlanId,
                dnsTimeoutSeconds: 5, cancellationToken: token);
            if (_routeProxyWindowClosed) return;
            _latestRouteComparisonRunV3 = run;
            SetRouteComparisonResultV3(
                InternalProxyRouteComparisonRunTextRenderer.Render(run), GetRouteComparisonBrushV3(run));
        });
    }

    private async Task RunRouteProxyUiOperation(Func<CancellationToken, Task> operation)
    {
        if (_routeProxyWindowClosed || _routeProxyClosePending
            || _routeComparisonCancellationV3 is not null || _measurementRunning
            || _observationCancellation is not null || _routeComparisonTabV3?.IsEnabled != true)
        {
            return;
        }

        CancellationTokenSource active = new();
        TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _routeComparisonCancellationV3 = active;
        _routeProxyOperationCompletion = completion;
        try
        {
            SetRouteComparisonBusyV3(isBusy: true);
            UpdateRouteProxyImportControls();
            await operation(active.Token);
        }
        catch (OperationCanceledException)
        {
            if (!_routeProxyWindowClosed)
                SetRouteProxyImportSummary("사용자 요청으로 작업을 중지했습니다. 미완료 판정은 적용하지 않았습니다.");
        }
        catch (Exception)
        {
            _importedRouteProxy = null;
            if (!_routeProxyWindowClosed)
                SetRouteProxyImportSummary("Windows 판정 연결을 완료하지 못했습니다. 원문 입력과 예외 메시지는 표시하지 않았습니다.");
        }
        finally
        {
            if (ReferenceEquals(_routeComparisonCancellationV3, active))
                _routeComparisonCancellationV3 = null;
            active.Dispose();
            if (!_routeProxyWindowClosed)
            {
                SetRouteComparisonBusyV3(isBusy: false);
                UpdateRouteProxyImportControls();
            }
            _routeProxyOperationCompletion = null;
            completion.TrySetResult();
        }
    }

    private Uri? ReadRouteProxyTarget() => Uri.TryCreate(
        _routeComparisonExternalTargetV3?.Text, UriKind.Absolute, out Uri? target) ? target : null;

    private void OnRouteProxyTargetChanged(object sender, TextChangedEventArgs e)
    {
        if (_importedRouteProxy is null) return;
        _importedRouteProxy = null;
        SetRouteProxyImportSummary("외부 URL이 변경되어 이전 Windows 판정을 폐기했습니다. 다시 불러오십시오.");
        UpdateRouteProxyImportControls();
    }

    private void OnRouteProxyBusyChanged(object sender, DependencyPropertyChangedEventArgs e) =>
        UpdateRouteProxyImportControls();

    private void UpdateRouteProxyImportControls()
    {
        if (_routeProxyWindowClosed) return;
        bool idle = _routeComparisonCancellationV3 is null && !_routeProxyClosePending;
        if (_importRouteProxyButton is not null) _importRouteProxyButton.IsEnabled = idle;
        if (_allowAutomaticRouteProxy is not null) _allowAutomaticRouteProxy.IsEnabled = idle;
        if (_compareImportedRouteProxyButton is not null)
            _compareImportedRouteProxyButton.IsEnabled = idle && _importedRouteProxy?.HasSelection == true;
    }

    private void SetRouteProxyImportSummary(string text)
    {
        if (!_routeProxyWindowClosed && _importedRouteProxySummary is not null)
            _importedRouteProxySummary.Text = text;
    }

    private async void OnRouteProxyImportClosing(object? sender, CancelEventArgs e)
    {
        Task? pending = _routeProxyOperationCompletion?.Task;
        if (pending is null) return;
        e.Cancel = true;
        if (_routeProxyClosePending) return;
        _routeProxyClosePending = true;
        _routeComparisonCancellationV3?.Cancel();
        UpdateRouteProxyImportControls();
        await pending;
        if (!_routeProxyWindowClosed && !Dispatcher.HasShutdownStarted)
        {
            _ = Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
            {
                _routeProxyClosePending = false;
                if (!_routeProxyWindowClosed) Close();
            }));
        }
    }

    private void OnRouteProxyImportClosed(object? sender, EventArgs e)
    {
        _routeProxyWindowClosed = true;
        _importedRouteProxy = null;
        if (_routeComparisonExternalTargetV3 is not null)
            _routeComparisonExternalTargetV3.TextChanged -= OnRouteProxyTargetChanged;
        if (_routeComparisonStartV3 is not null)
            _routeComparisonStartV3.IsEnabledChanged -= OnRouteProxyBusyChanged;
        if (_importRouteProxyButton is not null)
            _importRouteProxyButton.Click -= OnImportRouteProxyClick;
        if (_compareImportedRouteProxyButton is not null)
            _compareImportedRouteProxyButton.Click -= OnCompareImportedRouteProxyClick;
        Closing -= OnRouteProxyImportClosing;
        Closed -= OnRouteProxyImportClosed;
    }
}
