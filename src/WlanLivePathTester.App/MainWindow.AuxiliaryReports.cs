using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WlanLivePathTester.Core.Operations;

namespace WlanLivePathTester.App;

internal enum AuxiliaryReportKind
{
    RouteEvidence,
    RepeatedMeasurement,
    BrowserObservation,
    NetworkAdapter,
    NetworkEnvironment
}

internal sealed record AuxiliaryReportExport(
    string OutputDirectory, string JsonPath, string CsvPath, string HtmlPath,
    string Sha256Path, string Summary, bool IsWarning = false, bool IsError = false);

internal sealed class AuxiliaryReportSection : IDisposable
{
    private readonly Func<AuxiliaryReportSection, Task<bool>> _save;
    private readonly Action<AuxiliaryReportSection, bool> _open;
    internal AuxiliaryReportKind Kind { get; }
    internal TabItem Tab { get; }
    internal Button GenerateButton { get; }
    internal Button FolderButton { get; }
    internal Button HtmlButton { get; }
    internal TextBlock ResultText { get; }
    internal string NoDataMessage { get; }
    internal Func<Task<AuxiliaryReportExport?>> CaptureAndSave { get; set; }
    internal AuxiliaryReportExport? LatestExport { get; set; }
    internal bool IsRunning { get; set; }
    internal bool IsDisposed { get; private set; }

    internal AuxiliaryReportSection(
        AuxiliaryReportKind kind, string tabHeader, string title, string description,
        string privacy, string limitation, string noDataMessage,
        Func<Task<AuxiliaryReportExport?>> captureAndSave,
        Func<AuxiliaryReportSection, Task<bool>> save,
        Action<AuxiliaryReportSection, bool> open)
    {
        Kind = kind;
        NoDataMessage = noDataMessage;
        CaptureAndSave = captureAndSave;
        _save = save;
        _open = open;
        GenerateButton = Button("보고서 생성");
        FolderButton = Button("보고서 폴더 열기");
        HtmlButton = Button("최신 HTML 열기");
        FolderButton.IsEnabled = HtmlButton.IsEnabled = false;
        ResultText = new TextBlock
        {
            FontFamily = new FontFamily("Consolas"), TextWrapping = TextWrapping.Wrap,
            Text = "아직 보고서를 생성하지 않았습니다."
        };
        GenerateButton.Click += OnSave;
        FolderButton.Click += OnOpenFolder;
        HtmlButton.Click += OnOpenHtml;
        StackPanel content = new();
        content.Children.Add(new TextBlock { FontSize = 22, FontWeight = FontWeights.SemiBold, Text = title });
        content.Children.Add(new TextBlock
        {
            Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap,
            Foreground = Brushes.DarkSlateGray, Text = description
        });
        content.Children.Add(Notice(privacy, Color.FromRgb(232, 246, 243)));
        content.Children.Add(Notice(limitation + " 저장 중에는 다른 작업을 시작하지 않으며, 창 닫기는 실제 쓰기가 끝날 때까지 기다립니다.", Color.FromRgb(255, 248, 231)));
        WrapPanel buttons = new() { Margin = new Thickness(0, 16, 0, 0) };
        buttons.Children.Add(GenerateButton);
        buttons.Children.Add(FolderButton);
        buttons.Children.Add(HtmlButton);
        content.Children.Add(buttons);
        content.Children.Add(new Border
        {
            Margin = new Thickness(0, 18, 0, 0), Padding = new Thickness(16),
            BorderThickness = new Thickness(1), BorderBrush = Brushes.LightGray,
            CornerRadius = new CornerRadius(8), Background = Brushes.White, Child = ResultText
        });
        Tab = new TabItem
        {
            Header = tabHeader,
            Content = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = new Border { Padding = new Thickness(20), Child = content }
            }
        };
    }

    internal Task<bool> SaveAsync() => _save(this);
    private async void OnSave(object sender, RoutedEventArgs e) => await SaveAsync();
    private void OnOpenFolder(object sender, RoutedEventArgs e) => _open(this, true);
    private void OnOpenHtml(object sender, RoutedEventArgs e) => _open(this, false);
    internal void Show(string text, Brush brush)
    {
        if (IsDisposed) return;
        ResultText.Text = text;
        ResultText.Foreground = brush;
    }
    internal void UpdateControls()
    {
        if (IsDisposed) return;
        GenerateButton.IsEnabled = !IsRunning;
        FolderButton.IsEnabled = HtmlButton.IsEnabled = !IsRunning && LatestExport is not null;
    }
    public void Dispose()
    {
        Tab.Dispatcher.VerifyAccess();
        if (IsDisposed) return;
        IsDisposed = true;
        GenerateButton.Click -= OnSave;
        FolderButton.Click -= OnOpenFolder;
        HtmlButton.Click -= OnOpenHtml;
        GenerateButton.IsEnabled = FolderButton.IsEnabled = HtmlButton.IsEnabled = false;
    }
    private static Button Button(string text) => new()
    {
        Content = text, MinWidth = 135, Padding = new Thickness(12, 8, 12, 8),
        Margin = new Thickness(0, 0, 10, 0)
    };
    private static Border Notice(string text, Color color) => new()
    {
        Margin = new Thickness(0, 12, 0, 0), Padding = new Thickness(14),
        CornerRadius = new CornerRadius(8), Background = new SolidColorBrush(color),
        Child = new TextBlock { TextWrapping = TextWrapping.Wrap, Text = text }
    };
}

public partial class MainWindow
{
    private readonly Dictionary<AuxiliaryReportKind, AuxiliaryReportSection> _auxiliaryReportSections = new();
    private AuxiliaryReportSection? _auxiliaryReportReviewAfterClose;

    internal IReadOnlyDictionary<AuxiliaryReportKind, AuxiliaryReportSection> AuxiliaryReportSections => _auxiliaryReportSections;

    private void EnsureAuxiliaryReportTab(
        AuxiliaryReportKind kind, string tabHeader, string title, string description,
        string privacy, string limitation, string noDataMessage,
        Func<Task<AuxiliaryReportExport?>> captureAndSave)
    {
        Dispatcher.VerifyAccess();
        if (_applicationOperationWindowClosed || _auxiliaryReportSections.ContainsKey(kind)) return;
        TabControl? host = FindApplicationTabControl();
        if (host is null) return;
        AuxiliaryReportSection section = new(kind, tabHeader, title, description, privacy,
            limitation, noDataMessage, captureAndSave, RunAuxiliaryReportSaveAsync, OpenAuxiliaryReport);
        _auxiliaryReportSections.Add(kind, section);
        host.Items.Add(section.Tab);
    }

    private async Task<bool> RunAuxiliaryReportSaveAsync(AuxiliaryReportSection section)
    {
        Dispatcher.VerifyAccess();
        if (_applicationOperationWindowClosed || section.IsDisposed || section.IsRunning
            || !_auxiliaryReportSections.TryGetValue(section.Kind, out AuxiliaryReportSection? registered)
            || !ReferenceEquals(section, registered)) return false;
        using ApplicationOperationUiLease? lease =
            TryBeginUiApplicationOperation(ApplicationOperationKind.DiagnosticReportSave);
        if (lease is null)
        {
            section.Show(MeasurementStatusText.Text, Brushes.DarkOrange);
            return false;
        }
        // Existing writers have no uniform rollback-aware cancellation API.
        // Preserve their formats/storage semantics and await the real write.
        _auxiliaryReportReviewAfterClose = null;
        section.IsRunning = true;
        try
        {
            section.UpdateControls();
            section.Show("로컬 보고서 자료를 정리하고 저장하고 있습니다.", Brushes.DarkSlateGray);
            Func<Task<AuxiliaryReportExport?>> captureAndSave = section.CaptureAndSave;
            AuxiliaryReportExport? export = await captureAndSave();
            if (_applicationOperationWindowClosed || !lease.IsCurrent) return false;
            if (export is null)
            {
                section.Show(section.NoDataMessage, Brushes.DarkOrange);
                return false;
            }
            section.LatestExport = export;
            StringBuilder builder = new();
            builder.AppendLine("보고서 생성 완료");
            builder.AppendLine(export.Summary);
            builder.AppendLine($"JSON: {Path.GetFileName(export.JsonPath)}");
            builder.AppendLine($"CSV: {Path.GetFileName(export.CsvPath)}");
            builder.AppendLine($"HTML: {Path.GetFileName(export.HtmlPath)}");
            builder.AppendLine($"무결성: {Path.GetFileName(export.Sha256Path)}");
            builder.AppendLine("전체 사용자 경로는 표시하지 않았습니다. 폴더 열기로 로컬 파일을 확인하십시오. 외부 전송은 수행하지 않았습니다.");
            section.Show(builder.ToString().TrimEnd(), export.IsError ? Brushes.DarkRed
                : export.IsWarning ? Brushes.DarkOrange : Brushes.DarkGreen);
            return true;
        }
        catch (Exception exception)
        {
            if (_applicationOperationClosePending) _auxiliaryReportReviewAfterClose = section;
            section.Show($"보고서 저장 오류: {exception.GetType().Name}. 이전 성공 보고서는 유지합니다. 이번 실행의 일부 파일이 남을 수 있어 출력 폴더를 확인하십시오. 예외 원문은 표시하지 않았습니다.", Brushes.DarkRed);
            return false;
        }
        finally
        {
            section.IsRunning = false;
            section.UpdateControls();
            // Restore report controls first; the using declaration then
            // restores peer tabs and releases the same UI/Core lease.
        }
    }

    private bool KeepWindowOpenForAuxiliaryReportReview()
    {
        AuxiliaryReportSection? section = _auxiliaryReportReviewAfterClose;
        _auxiliaryReportReviewAfterClose = null;
        if (section is null) return false;
        section.Show(section.ResultText.Text + "\n저장 오류를 확인하도록 창을 유지했습니다. 확인 후 다시 닫을 수 있습니다.", Brushes.DarkRed);
        return true;
    }

    private void OpenAuxiliaryReport(AuxiliaryReportSection section, bool directory)
    {
        Dispatcher.VerifyAccess();
        if (_applicationOperationWindowClosed || section.IsDisposed || section.IsRunning
            || CurrentApplicationOperation.IsBusy || CurrentApplicationOperation.ShutdownRequested
            || _applicationOperationClosePending || _routeProxyClosePending || _routeReportCloseRequested) return;
        string? path = directory ? section.LatestExport?.OutputDirectory : section.LatestExport?.HtmlPath;
        if (string.IsNullOrWhiteSpace(path) || !(directory ? Directory.Exists(path) : File.Exists(path)))
        {
            section.Show("저장된 로컬 보고서 경로를 찾을 수 없습니다.", Brushes.DarkOrange);
            return;
        }
        try { Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true }); }
        catch (Exception exception)
        {
            section.Show($"로컬 보고서 열기 오류: {exception.GetType().Name}. 예외 원문은 표시하지 않았습니다.", Brushes.DarkRed);
        }
    }

    private void DisposeAuxiliaryReportSections()
    {
        foreach (AuxiliaryReportSection section in _auxiliaryReportSections.Values) section.Dispose();
        _auxiliaryReportReviewAfterClose = null;
    }
}
