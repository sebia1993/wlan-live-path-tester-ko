using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WlanLivePathTester.Core.Operations;
using WlanLivePathTester.Core.Reporting;
using WlanLivePathTester.Core.Routing;

namespace WlanLivePathTester.App;

public partial class MainWindow
{
    private readonly ReportSaveSession _routeReportSaveSession = new();
    private ApplicationOperationUiLease? _routeReportUiLease;
    private Button? _routeComparisonReportGenerateV2;
    private Button? _routeComparisonReportCancelV2;
    private Button? _routeComparisonReportOpenFolderV2;
    private Button? _routeComparisonReportOpenHtmlV2;
    private TextBlock? _routeComparisonReportResultV2;
    private string? _latestRouteComparisonReportDirectoryV2;
    private string? _latestRouteComparisonReportHtmlV2;
    private bool _routeComparisonReportTabAddedV2;
    private bool _routeReportReviewAfterClose;
    private bool RouteReportSaveBusy => _routeReportUiLease is not null || _routeReportSaveSession.IsBusy;

    internal sealed record RouteReportSaveOutput(InternalProxyRouteComparisonRunReportDocument Document,
        InternalProxyRouteComparisonRunReportExportResult Export);
    internal Func<InternalProxyRouteComparisonRunResult, CancellationToken, RouteReportSaveOutput>?
        SaveRouteReportOverride { get; set; }

    internal void EnsureRouteComparisonReportTabV2()
    {
        Dispatcher.VerifyAccess();
        if (_routeComparisonReportTabAddedV2 || _applicationOperationWindowClosed) return;
        TabControl? tabs = FindApplicationTabControl();
        if (tabs is null) return;
        tabs.Items.Add(CreateRouteComparisonReportTabV2());
        _routeComparisonReportTabAddedV2 = true;
        Closed += OnRouteReportWindowClosed;
    }

    private TabItem CreateRouteComparisonReportTabV2()
    {
        _routeComparisonReportGenerateV2 = CreateRouteReportSaveButton("경로 비교 보고서 생성");
        _routeComparisonReportGenerateV2.Click += OnGenerateRouteComparisonReportV2;
        _routeComparisonReportCancelV2 = CreateRouteReportSaveButton("저장 취소", false);
        _routeComparisonReportCancelV2.Click += OnCancelRouteComparisonReportV2;
        _routeComparisonReportOpenFolderV2 = CreateRouteReportSaveButton("보고서 폴더 열기", false);
        _routeComparisonReportOpenFolderV2.Click += OnOpenRouteComparisonReportFolderV2;
        _routeComparisonReportOpenHtmlV2 = CreateRouteReportSaveButton("최신 HTML 열기", false);
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
            FontSize = 22, FontWeight = FontWeights.SemiBold, Text = "내부 DIRECT·프록시 경로 비교 보고서"
        });
        foreach (string text in new[]
        {
            "최근 실행 결과를 JSON·CSV·단일 HTML·SHA-256으로 로컬 저장합니다. 추가 DNS·라우팅·HTTP·프록시 요청이나 업로드는 수행하지 않습니다.",
            "원문 URL·프록시 호스트·전체 GUID·인터페이스 이름은 저장하지 않습니다. 지문도 익명성을 보장하지 않으므로 외부 공유 전 내용을 검토하십시오.",
            "취소는 파일 저장 단계 사이에서 반영됩니다. SHA256SUMS가 게시된 뒤에는 저장 성공을 유지합니다. 저장 중 창 닫기는 실제 작업과 정리가 끝날 때까지 기다립니다."
        }) content.Children.Add(new TextBlock { Margin = new Thickness(0, 10, 0, 0), TextWrapping = TextWrapping.Wrap, Text = text });
        content.Children.Add(buttons);
        content.Children.Add(new Border
        {
            Margin = new Thickness(0, 18, 0, 0), Padding = new Thickness(16), BorderThickness = new Thickness(1),
            BorderBrush = Brushes.LightGray, Child = _routeComparisonReportResultV2
        });
        return new TabItem
        {
            Header = "경로 보고서", Content = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = new Border { Padding = new Thickness(20), Child = content }
            }
        };
    }

    private async void OnGenerateRouteComparisonReportV2(object sender, RoutedEventArgs e) =>
        await SaveRouteComparisonReportAsync();

    internal Task<bool> SaveRouteComparisonReportAsync()
    {
        Dispatcher.VerifyAccess();
        InternalProxyRouteComparisonRunResult? run = LatestRouteComparisonRunV3;
        if (run is null)
        {
            SetRouteComparisonReportResultV2("저장할 경로 비교 결과가 없습니다.", Brushes.DarkOrange);
            return Task.FromResult(false);
        }
        var writer = SaveRouteReportOverride;
        return RunRouteReportSaveAsync(token =>
        {
            if (writer is not null) return writer(run, token);
            token.ThrowIfCancellationRequested();
            string version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "development";
            var document = InternalProxyRouteComparisonRunReportWriter.CreateDocument(run, version);
            var export = InternalProxyRouteComparisonRunReportWriter.WriteAll(
                document, GetRouteComparisonReportDirectoryV2(), "WlanRouteComparison", token);
            return new RouteReportSaveOutput(document, export);
        });
    }

    internal async Task<bool> RunRouteReportSaveAsync(Func<CancellationToken, RouteReportSaveOutput> save)
    {
        Dispatcher.VerifyAccess();
        ArgumentNullException.ThrowIfNull(save);
        if (_applicationOperationWindowClosed || RouteReportSaveBusy
            || _routeComparisonReportGenerateV2 is not { IsEnabled: true }) return false;
        int cancellationRequested = 0;
        using ApplicationOperationUiLease? lease = TryBeginUiApplicationOperation(
            ApplicationOperationKind.RouteComparisonReportSave,
            () =>
            {
                // Cancellation may arrive in TryBegin's state notification,
                // before ReportSaveSession.TryStart has created its CTS.
                Interlocked.Exchange(ref cancellationRequested, 1);
                _routeReportSaveSession.RequestCancellation();
            });
        if (lease is null)
        {
            SetRouteComparisonReportResultV2(MeasurementStatusText.Text, Brushes.DarkOrange);
            return false;
        }
        _routeReportUiLease = lease;
        _routeReportReviewAfterClose = false;
        bool sessionStarted = false;
        try
        {
            SetRouteReportSaveBusy(true);
            if (Volatile.Read(ref cancellationRequested) != 0) throw new OperationCanceledException();
            if (!_routeReportSaveSession.TryStart(save, out Task<RouteReportSaveOutput>? completion))
                throw new InvalidOperationException("REPORT_SESSION_UNAVAILABLE");
            sessionStarted = true;
            if (Volatile.Read(ref cancellationRequested) != 0) _routeReportSaveSession.RequestCancellation();
            SetRouteComparisonReportResultV2("로컬 보고서를 생성하고 있습니다. 취소와 창 닫기는 파일 정리가 끝날 때까지 기다립니다.", Brushes.DarkSlateGray);
            // Includes the writer, async cancellation callbacks and CTS disposal.
            // Do not check cancellation after a committed file set was returned.
            RouteReportSaveOutput saved = await completion;
            if (_applicationOperationWindowClosed || !lease.IsCurrent) return false;
            ArgumentNullException.ThrowIfNull(saved);
            ArgumentNullException.ThrowIfNull(saved.Document);
            ArgumentNullException.ThrowIfNull(saved.Export);
            var export = saved.Export;
            StringBuilder message = new();
            message.AppendLine(export.CleanupIncomplete ? "저장 완료 · 임시 파일 정리 확인 필요" : "경로 비교 보고서 저장 완료");
            message.AppendLine($"실행: {saved.Document.RouteComparison.RunStatus}");
            message.AppendLine($"비교: {saved.Document.RouteComparison.Comparison?.Status ?? "없음"}");
            message.AppendLine($"판정: {saved.Document.RouteComparison.Finding.Code}");
            message.AppendLine($"JSON: {Path.GetFileName(export.JsonPath)}");
            message.AppendLine($"CSV: {Path.GetFileName(export.CsvPath)}");
            message.AppendLine($"HTML: {Path.GetFileName(export.HtmlPath)}");
            message.AppendLine($"SHA-256: {Path.GetFileName(export.Sha256Path)}");
            message.AppendLine("전체 사용자 경로와 원본 입력은 표시하지 않았습니다.");
            // Validate and format before replacing the last successful paths.
            _latestRouteComparisonReportDirectoryV2 = export.OutputDirectory;
            _latestRouteComparisonReportHtmlV2 = export.HtmlPath;
            if (export.CleanupIncomplete && _applicationOperationClosePending) _routeReportReviewAfterClose = true;
            SetRouteComparisonReportResultV2(message.ToString().TrimEnd(), export.CleanupIncomplete ? Brushes.DarkOrange : Brushes.DarkGreen);
            return true;
        }
        catch (ReportFileSetRecoveryException)
        {
            _routeReportReviewAfterClose = _applicationOperationClosePending;
            SetRouteComparisonReportResultV2("저장 및 일부 파일 정리를 완료하지 못했습니다. 기존 보고서는 보존했습니다. 보고서 폴더를 확인하십시오.", Brushes.DarkRed);
            return false;
        }
        catch (OperationCanceledException) when (Volatile.Read(ref cancellationRequested) != 0)
        {
            SetRouteComparisonReportResultV2("저장을 취소하고 이번 실행의 부분 파일을 정리했습니다. 이전 성공 보고서는 유지합니다.", Brushes.DarkOrange);
            return false;
        }
        catch (Exception exception)
        {
            _routeReportReviewAfterClose = _applicationOperationClosePending;
            SetRouteComparisonReportResultV2($"보고서 저장 오류: {exception.GetType().Name}. 원문 경로·입력·예외 메시지는 표시하지 않았습니다. 출력 폴더를 확인하십시오.", Brushes.DarkRed);
            return false;
        }
        finally
        {
            // A cancellation before TryStart must not inherit the preceding
            // save's callback-failure flag. TryStart resets that flag for its run.
            if (sessionStarted && _routeReportSaveSession.CancellationCallbackFailed)
            {
                _routeReportReviewAfterClose = _applicationOperationClosePending;
                if (!_applicationOperationWindowClosed && _routeComparisonReportResultV2 is not null)
                    _routeComparisonReportResultV2.Text += "\n취소 콜백 오류가 있어 로컬 검토가 필요합니다.";
            }
            try { if (!_applicationOperationWindowClosed) SetRouteReportSaveBusy(false); }
            finally { if (ReferenceEquals(_routeReportUiLease, lease)) _routeReportUiLease = null; }
            // The using declaration restores bindings/late peer tabs and then
            // releases Core ownership. There is no separate Closing continuation.
        }
    }

    private void OnCancelRouteComparisonReportV2(object sender, RoutedEventArgs e)
    {
        if (_routeReportUiLease is not { IsCurrent: true } lease || _applicationOperationWindowClosed) return;
        ApplicationOperationCancellationStatus status = lease.RequestCancellation();
        if (_routeComparisonReportCancelV2 is not null) _routeComparisonReportCancelV2.IsEnabled = false;
        string failure = FormatApplicationCancellationFailure(status);
        SetRouteComparisonReportResultV2(string.IsNullOrEmpty(failure)
                ? "취소 요청됨 · 현재 단계와 파일 정리가 끝나는 것을 기다립니다." : failure,
            status == ApplicationOperationCancellationStatus.CallbackFailed ? Brushes.DarkRed : Brushes.DarkOrange);
    }

    private bool KeepWindowOpenForRouteReportReview()
    {
        if (!_routeReportReviewAfterClose) return false;
        _routeReportReviewAfterClose = false;
        SetRouteComparisonReportResultV2((_routeComparisonReportResultV2?.Text ?? string.Empty)
            + "\n저장 또는 정리 오류를 확인하도록 창을 유지했습니다. 확인 후 다시 닫을 수 있습니다.", Brushes.DarkOrange);
        return true;
    }

    private void OnRouteReportWindowClosed(object? sender, EventArgs e)
    {
        if (_routeComparisonReportGenerateV2 is not null) _routeComparisonReportGenerateV2.Click -= OnGenerateRouteComparisonReportV2;
        if (_routeComparisonReportCancelV2 is not null) _routeComparisonReportCancelV2.Click -= OnCancelRouteComparisonReportV2;
        if (_routeComparisonReportOpenFolderV2 is not null) _routeComparisonReportOpenFolderV2.Click -= OnOpenRouteComparisonReportFolderV2;
        if (_routeComparisonReportOpenHtmlV2 is not null) _routeComparisonReportOpenHtmlV2.Click -= OnOpenRouteComparisonReportHtmlV2;
        Closed -= OnRouteReportWindowClosed;
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
    }

    private void OnOpenRouteComparisonReportFolderV2(object sender, RoutedEventArgs e) =>
        OpenRouteComparisonReportPathV2(_latestRouteComparisonReportDirectoryV2, true);
    private void OnOpenRouteComparisonReportHtmlV2(object sender, RoutedEventArgs e) =>
        OpenRouteComparisonReportPathV2(_latestRouteComparisonReportHtmlV2, false);
    private void OpenRouteComparisonReportPathV2(string? path, bool directory)
    {
        if (_applicationOperationWindowClosed || CurrentApplicationOperation.IsBusy
            || CurrentApplicationOperation.ShutdownRequested || _applicationOperationClosePending) return;
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
        if (_applicationOperationWindowClosed || _routeComparisonReportResultV2 is null) return;
        _routeComparisonReportResultV2.Text = text;
        _routeComparisonReportResultV2.Foreground = brush;
    }
}
