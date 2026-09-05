using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using WlanLivePathTester.Core.Reporting;
using WlanLivePathTester.Core.Routing;

namespace WlanLivePathTester.App;

public partial class MainWindow
{
    private readonly ReportSaveSession _routeReportSaveSession = new();
    private Button? _routeComparisonReportGenerateV2;
    private Button? _routeComparisonReportCancelV2;
    private Button? _routeComparisonReportOpenFolderV2;
    private Button? _routeComparisonReportOpenHtmlV2;
    private TextBlock? _routeComparisonReportResultV2;
    private string? _latestRouteComparisonReportDirectoryV2;
    private string? _latestRouteComparisonReportHtmlV2;
    private bool _routeComparisonReportTabAddedV2;
    private TabControl? _routeReportTabHost;
    private TabItem? _routeReportTab;
    private readonly Dictionary<TabItem, bool> _routeReportPeerStates = new();
    private Task _routeReportUiSettled = Task.CompletedTask;
    private bool _routeReportCloseRequested;
    private bool _routeReportAllowClose;
    private bool _routeReportWindowClosed;
    private bool _routeReportNeedsReview;

    private bool RouteReportSaveBusy => _routeReportSaveSession.IsBusy || !_routeReportUiSettled.IsCompleted;

    internal void EnsureRouteComparisonReportTabV2()
    {
        if (_routeComparisonReportTabAddedV2 || _routeReportWindowClosed) return;
        _routeReportTabHost = FindRouteComparisonDescendantV3<TabControl>(this);
        if (_routeReportTabHost is null) return;
        _routeReportTab = CreateRouteComparisonReportTabV2();
        _routeReportTabHost.Items.Add(_routeReportTab);
        _routeComparisonReportTabAddedV2 = true;
        Closing += OnRouteReportWindowClosing;
        Closed += OnRouteReportWindowClosed;
    }

    private TabItem CreateRouteComparisonReportTabV2()
    {
        _routeComparisonReportGenerateV2 = CreateRouteReportSaveButton("경로 비교 보고서 생성");
        _routeComparisonReportGenerateV2.Click += OnGenerateRouteComparisonReportV2;
        _routeComparisonReportCancelV2 = CreateRouteReportSaveButton("저장 취소", enabled: false);
        _routeComparisonReportCancelV2.Click += OnCancelRouteComparisonReportV2;
        _routeComparisonReportOpenFolderV2 = CreateRouteReportSaveButton("보고서 폴더 열기", enabled: false);
        _routeComparisonReportOpenFolderV2.Click += OnOpenRouteComparisonReportFolderV2;
        _routeComparisonReportOpenHtmlV2 = CreateRouteReportSaveButton("최신 HTML 열기", enabled: false);
        _routeComparisonReportOpenHtmlV2.Click += OnOpenRouteComparisonReportHtmlV2;
        _routeComparisonReportResultV2 = new TextBlock
        {
            FontFamily = new FontFamily("Consolas"), TextWrapping = TextWrapping.Wrap,
            Text = "아직 경로 보고서를 생성하지 않았습니다. 먼저 경로 비교 탭에서 실행 결과를 만드십시오."
        };
        WrapPanel buttons = new() { Margin = new Thickness(0, 16, 0, 0) };
        buttons.Children.Add(_routeComparisonReportGenerateV2);
        buttons.Children.Add(_routeComparisonReportCancelV2);
        buttons.Children.Add(_routeComparisonReportOpenFolderV2);
        buttons.Children.Add(_routeComparisonReportOpenHtmlV2);
        StackPanel content = new();
        content.Children.Add(new TextBlock
        {
            FontSize = 22, FontWeight = FontWeights.SemiBold,
            Text = "내부 DIRECT·프록시 경로 비교 보고서"
        });
        content.Children.Add(new TextBlock
        {
            Margin = new Thickness(0, 10, 0, 0), TextWrapping = TextWrapping.Wrap,
            Text = "최근 실행 결과를 JSON·CSV·단일 HTML·SHA-256으로 로컬 저장합니다. 추가 DNS·라우팅·HTTP·프록시 요청이나 업로드는 수행하지 않습니다."
        });
        content.Children.Add(new TextBlock
        {
            Margin = new Thickness(0, 10, 0, 0), TextWrapping = TextWrapping.Wrap,
            Text = "원문 URL·프록시 호스트·전체 GUID·인터페이스 이름은 저장하지 않습니다. 지문도 익명성을 보장하지 않으므로 외부 공유 전 내용을 검토하십시오."
        });
        content.Children.Add(new TextBlock
        {
            Margin = new Thickness(0, 10, 0, 0), TextWrapping = TextWrapping.Wrap,
            Text = "저장 취소는 단계 사이에서 반영됩니다. SHA256SUMS가 게시된 뒤에는 저장 성공을 유지합니다. 저장 중 일반 창 닫기는 정리가 끝날 때까지 보류합니다."
        });
        content.Children.Add(buttons);
        content.Children.Add(new Border
        {
            Margin = new Thickness(0, 18, 0, 0), Padding = new Thickness(16),
            BorderThickness = new Thickness(1), BorderBrush = Brushes.LightGray,
            Child = _routeComparisonReportResultV2
        });
        return new TabItem
        {
            Header = "경로 보고서",
            Content = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = new Border { Padding = new Thickness(20), Child = content }
            }
        };
    }

    private async void OnGenerateRouteComparisonReportV2(object sender, RoutedEventArgs e)
    {
        if (RouteReportSaveBusy || _routeReportCloseRequested || _routeReportWindowClosed) return;
        if (_measurementRunning || _observationCancellation is not null || _routeComparisonCancellationV3 is not null)
        {
            SetRouteComparisonReportResultV2("측정·관찰·경로 비교를 완료하거나 중지한 뒤 저장하십시오.", Brushes.DarkOrange);
            return;
        }
        InternalProxyRouteComparisonRunResult? run = LatestRouteComparisonRunV3;
        if (run is null)
        {
            SetRouteComparisonReportResultV2("저장할 경로 비교 결과가 없습니다.", Brushes.DarkOrange);
            return;
        }

        _routeReportNeedsReview = false;
        TaskCompletionSource<bool> uiSettled = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _routeReportUiSettled = uiSettled.Task;
        try
        {
            string version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "development";
            string directory = GetRouteComparisonReportDirectoryV2();
            if (!_routeReportSaveSession.TryStart(token =>
            {
                token.ThrowIfCancellationRequested();
                var document = InternalProxyRouteComparisonRunReportWriter.CreateDocument(run, version);
                var export = InternalProxyRouteComparisonRunReportWriter.WriteAll(
                    document, directory, "WlanRouteComparison", token);
                return new RouteReportSaveOutput(document, export);
            }, out Task<RouteReportSaveOutput>? completion))
            {
                SetRouteComparisonReportResultV2("다른 저장 작업이 정리 중이거나 창이 종료 중입니다.", Brushes.DarkOrange);
                return;
            }
            SetRouteReportSaveBusy(true);
            SetRouteComparisonReportResultV2("로컬 보고서를 생성하고 있습니다. 저장 취소는 단계 사이에서 반영됩니다.", Brushes.DarkSlateGray);
            RouteReportSaveOutput saved = await completion;
            if (_routeReportWindowClosed) return;
            var export = saved.Export;
            _latestRouteComparisonReportDirectoryV2 = export.OutputDirectory;
            _latestRouteComparisonReportHtmlV2 = export.HtmlPath;
            _routeReportNeedsReview = export.CleanupIncomplete;
            StringBuilder builder = new();
            builder.AppendLine(export.CleanupIncomplete ? "저장 완료 · 임시 파일 정리 확인 필요" : "경로 비교 보고서 저장 완료");
            builder.AppendLine($"실행: {saved.Document.RouteComparison.RunStatus}");
            builder.AppendLine($"비교: {saved.Document.RouteComparison.Comparison?.Status ?? "없음"}");
            builder.AppendLine($"판정: {saved.Document.RouteComparison.Finding.Code}");
            builder.AppendLine($"JSON: {Path.GetFileName(export.JsonPath)}");
            builder.AppendLine($"CSV: {Path.GetFileName(export.CsvPath)}");
            builder.AppendLine($"HTML: {Path.GetFileName(export.HtmlPath)}");
            builder.AppendLine($"SHA-256: {Path.GetFileName(export.Sha256Path)}");
            builder.AppendLine("전체 사용자 경로와 원본 입력은 표시하지 않았습니다.");
            SetRouteComparisonReportResultV2(builder.ToString().TrimEnd(),
                export.CleanupIncomplete ? Brushes.DarkOrange : Brushes.DarkGreen);
        }
        catch (ReportFileSetRecoveryException)
        {
            _routeReportNeedsReview = true;
            SetRouteComparisonReportResultV2(
                "저장을 완료하지 못했고 일부 파일의 정리도 실패했습니다. 보고서 폴더를 확인하십시오. 기존 보고서는 보존했습니다.", Brushes.DarkRed);
        }
        catch (OperationCanceledException)
        {
            SetRouteComparisonReportResultV2(
                "저장을 취소하고 이번 실행의 부분 파일을 정리했습니다. 이전 성공 보고서는 유지합니다.", Brushes.DarkOrange);
        }
        catch (Exception exception)
        {
            _routeReportNeedsReview = true;
            SetRouteComparisonReportResultV2(
                $"보고서 저장 오류: {exception.GetType().Name}. 원문 경로·입력·예외 메시지는 표시하지 않았습니다.", Brushes.DarkRed);
        }
        finally
        {
            if (_routeReportSaveSession.CancellationCallbackFailed)
            {
                _routeReportNeedsReview = true;
                if (!_routeReportWindowClosed && _routeComparisonReportResultV2 is not null)
                    _routeComparisonReportResultV2.Text += "\n취소 콜백 오류가 있어 로컬 검토가 필요합니다.";
            }
            if (!_routeReportWindowClosed) SetRouteReportSaveBusy(false);
            uiSettled.TrySetResult(true);
        }
    }

    private void OnCancelRouteComparisonReportV2(object sender, RoutedEventArgs e)
    {
        if (!RouteReportSaveBusy || _routeReportWindowClosed) return;
        _routeReportSaveSession.RequestCancellation();
        if (_routeComparisonReportCancelV2 is not null) _routeComparisonReportCancelV2.IsEnabled = false;
        SetRouteComparisonReportResultV2("취소 요청됨 · 현재 단계와 파일 정리가 끝나는 것을 기다립니다.", Brushes.DarkOrange);
    }

    private void OnRouteReportWindowClosing(object? sender, CancelEventArgs e)
    {
        if (_routeReportAllowClose || !RouteReportSaveBusy) return;
        e.Cancel = true;
        if (_routeReportCloseRequested) return;
        _routeReportCloseRequested = true;
        _routeReportSaveSession.RequestCancellation();
        SetRouteComparisonReportResultV2("창 닫기를 보류하고 저장 취소·파일 정리를 진행합니다.", Brushes.DarkOrange);
        _ = FinishDeferredRouteReportCloseAsync();
    }

    private async Task FinishDeferredRouteReportCloseAsync()
    {
        try
        {
            await _routeReportSaveSession.CancelAndWaitAsync();
            await _routeReportUiSettled;
            // Always post: Close() must never run recursively inside the original Closing event.
            await Dispatcher.InvokeAsync(() =>
            {
                if (_routeReportWindowClosed) return;
                if (_routeReportNeedsReview)
                {
                    _routeReportCloseRequested = false;
                    if (_routeComparisonReportResultV2 is not null)
                        _routeComparisonReportResultV2.Text += "\n저장 또는 정리 오류를 확인하도록 창을 유지했습니다.";
                    return;
                }
                _routeReportAllowClose = true;
                try { Close(); }
                finally
                {
                    _routeReportAllowClose = false;
                    if (!_routeReportWindowClosed) _routeReportCloseRequested = false;
                }
            }, DispatcherPriority.Background);
        }
        catch (Exception exception)
        {
            _routeReportCloseRequested = false;
            SetRouteComparisonReportResultV2($"종료 처리 확인 필요: {exception.GetType().Name}.", Brushes.DarkRed);
        }
    }

    private void OnRouteReportWindowClosed(object? sender, EventArgs e)
    {
        _routeReportWindowClosed = true;
        Closing -= OnRouteReportWindowClosing;
        Closed -= OnRouteReportWindowClosed;
        if (_routeReportTabHost is not null)
            ((INotifyCollectionChanged)_routeReportTabHost.Items).CollectionChanged -= OnRouteReportTabsChanged;
        _routeReportPeerStates.Clear();
        _ = _routeReportSaveSession.CloseAsync();
    }

    private void SetRouteReportSaveBusy(bool busy)
    {
        if (_routeComparisonReportGenerateV2 is not null) _routeComparisonReportGenerateV2.IsEnabled = !busy;
        if (_routeComparisonReportCancelV2 is not null) _routeComparisonReportCancelV2.IsEnabled = busy;
        if (_routeComparisonReportOpenFolderV2 is not null)
            _routeComparisonReportOpenFolderV2.IsEnabled = !busy && _latestRouteComparisonReportDirectoryV2 is not null;
        if (_routeComparisonReportOpenHtmlV2 is not null)
            _routeComparisonReportOpenHtmlV2.IsEnabled = !busy && _latestRouteComparisonReportHtmlV2 is not null;
        if (_routeReportTabHost is null) return;
        var items = (INotifyCollectionChanged)_routeReportTabHost.Items;
        if (busy)
        {
            items.CollectionChanged += OnRouteReportTabsChanged;
            LockRouteReportPeerTabs();
        }
        else
        {
            items.CollectionChanged -= OnRouteReportTabsChanged;
            foreach (var pair in _routeReportPeerStates)
                pair.Key.SetCurrentValue(UIElement.IsEnabledProperty, pair.Value);
            _routeReportPeerStates.Clear();
        }
    }

    private void OnRouteReportTabsChanged(object? sender, NotifyCollectionChangedEventArgs e) => LockRouteReportPeerTabs();
    private void LockRouteReportPeerTabs()
    {
        if (_routeReportTabHost is null) return;
        foreach (TabItem tab in _routeReportTabHost.Items.OfType<TabItem>())
        {
            if (ReferenceEquals(tab, _routeReportTab) || _routeReportPeerStates.ContainsKey(tab)) continue;
            _routeReportPeerStates.Add(tab, tab.IsEnabled);
            tab.SetCurrentValue(UIElement.IsEnabledProperty, false);
        }
    }

    private void OnOpenRouteComparisonReportFolderV2(object sender, RoutedEventArgs e) =>
        OpenRouteComparisonReportPathV2(_latestRouteComparisonReportDirectoryV2, true);
    private void OnOpenRouteComparisonReportHtmlV2(object sender, RoutedEventArgs e) =>
        OpenRouteComparisonReportPathV2(_latestRouteComparisonReportHtmlV2, false);
    private void OpenRouteComparisonReportPathV2(string? path, bool directory)
    {
        if (RouteReportSaveBusy || _routeReportCloseRequested || _routeReportWindowClosed) return;
        if (string.IsNullOrWhiteSpace(path) || !(directory ? Directory.Exists(path) : File.Exists(path)))
        {
            SetRouteComparisonReportResultV2("저장된 로컬 보고서 경로를 찾을 수 없습니다.", Brushes.DarkOrange);
            return;
        }
        try { Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true }); }
        catch (Exception exception)
        {
            SetRouteComparisonReportResultV2($"로컬 경로 열기 오류: {exception.GetType().Name}.", Brushes.DarkRed);
        }
    }

    private static string GetRouteComparisonReportDirectoryV2()
    {
        string root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(root)) throw new InvalidOperationException("LOCAL_APP_DATA_UNAVAILABLE");
        return Path.Combine(root, "WlanLivePathTesterKO", "Reports", "RouteComparison");
    }
    private static Button CreateRouteReportSaveButton(string text, bool enabled = true) => new()
    {
        Content = text, IsEnabled = enabled, MinWidth = 115,
        Padding = new Thickness(12, 8, 12, 8), Margin = new Thickness(0, 0, 8, 8)
    };
    private void SetRouteComparisonReportResultV2(string text, Brush brush)
    {
        if (_routeReportWindowClosed || _routeComparisonReportResultV2 is null) return;
        _routeComparisonReportResultV2.Text = text;
        _routeComparisonReportResultV2.Foreground = brush;
    }
    private sealed record RouteReportSaveOutput(InternalProxyRouteComparisonRunReportDocument Document,
        InternalProxyRouteComparisonRunReportExportResult Export);
}
