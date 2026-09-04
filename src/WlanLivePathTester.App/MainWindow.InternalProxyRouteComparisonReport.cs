using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WlanLivePathTester.Core.Reporting;

namespace WlanLivePathTester.App;

public partial class MainWindow
{
    private Button?
        _generateInternalProxyRouteComparisonReportButton;
    private Button?
        _openInternalProxyRouteComparisonReportFolderButton;
    private Button?
        _openInternalProxyRouteComparisonReportHtmlButton;
    private TextBlock?
        _internalProxyRouteComparisonReportResultText;
    private string?
        _lastInternalProxyRouteComparisonReportDirectory;
    private string?
        _lastInternalProxyRouteComparisonReportHtmlPath;
    private bool _internalProxyRouteComparisonReportTabAdded;

    internal void EnsureInternalProxyRouteComparisonReportTab()
    {
        if (_internalProxyRouteComparisonReportTabAdded)
        {
            return;
        }

        TabControl? tabControl = FindVisualDescendant<TabControl>(this);
        if (tabControl is null)
        {
            return;
        }

        tabControl.Items.Add(
            CreateInternalProxyRouteComparisonReportTab());
        _internalProxyRouteComparisonReportTabAdded = true;
    }

    private TabItem CreateInternalProxyRouteComparisonReportTab()
    {
        _generateInternalProxyRouteComparisonReportButton = new Button
        {
            Content = "경로 비교 보고서 생성",
            MinWidth = 190,
            Padding = new Thickness(12, 8, 12, 8)
        };
        _generateInternalProxyRouteComparisonReportButton.Click +=
            OnGenerateInternalProxyRouteComparisonReportClick;

        _openInternalProxyRouteComparisonReportFolderButton = new Button
        {
            Content = "보고서 폴더 열기",
            MinWidth = 140,
            Padding = new Thickness(12, 8, 12, 8),
            IsEnabled = false
        };
        _openInternalProxyRouteComparisonReportFolderButton.Click +=
            OnOpenInternalProxyRouteComparisonReportFolderClick;

        _openInternalProxyRouteComparisonReportHtmlButton = new Button
        {
            Content = "최신 HTML 열기",
            MinWidth = 130,
            Padding = new Thickness(12, 8, 12, 8),
            IsEnabled = false
        };
        _openInternalProxyRouteComparisonReportHtmlButton.Click +=
            OnOpenInternalProxyRouteComparisonReportHtmlClick;

        _internalProxyRouteComparisonReportResultText = new TextBlock
        {
            FontFamily = new FontFamily("Consolas"),
            TextWrapping = TextWrapping.Wrap,
            Text =
                "아직 내부·프록시 경로 비교 보고서를 생성하지 않았습니다."
        };

        StackPanel buttons = new()
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 16, 0, 0)
        };
        buttons.Children.Add(
            _generateInternalProxyRouteComparisonReportButton);
        buttons.Children.Add(new Border { Width = 10 });
        buttons.Children.Add(
            _openInternalProxyRouteComparisonReportFolderButton);
        buttons.Children.Add(new Border { Width = 10 });
        buttons.Children.Add(
            _openInternalProxyRouteComparisonReportHtmlButton);

        StackPanel content = new();
        content.Children.Add(new TextBlock
        {
            FontSize = 22,
            FontWeight = FontWeights.SemiBold,
            Text = "내부 DIRECT·프록시 경로 비교 보고서"
        });
        content.Children.Add(new TextBlock
        {
            Margin = new Thickness(0, 8, 0, 0),
            Foreground = new SolidColorBrush(
                Color.FromRgb(86, 101, 115)),
            TextWrapping = TextWrapping.Wrap,
            Text =
                "가장 최근 경로 비교의 상태, 인터페이스 지문·범주, 프록시 후보와 고정 Finding을 JSON·CSV·단일 HTML로 저장합니다. 보고서 생성은 DNS나 네트워크 요청을 추가로 수행하지 않습니다."
        });
        content.Children.Add(new Border
        {
            Margin = new Thickness(0, 16, 0, 0),
            Padding = new Thickness(14),
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(
                Color.FromRgb(232, 246, 243)),
            BorderBrush = new SolidColorBrush(
                Color.FromRgb(115, 198, 182)),
            BorderThickness = new Thickness(1),
            Child = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Text =
                    "내부 URL·프록시 호스트·전체 인터페이스 GUID·이름·설명·IP·MAC·게이트웨이·DNS·SSID·BSSID를 저장하지 않습니다. JSON·CSV·HTML의 SHA-256 목록을 함께 생성합니다."
            }
        });
        content.Children.Add(new Border
        {
            Margin = new Thickness(0, 12, 0, 0),
            Padding = new Thickness(14),
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(
                Color.FromRgb(255, 248, 231)),
            BorderBrush = new SolidColorBrush(
                Color.FromRgb(232, 206, 138)),
            BorderThickness = new Thickness(1),
            Child = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Text =
                    "경로 비교 결과는 앱 메모리에만 유지됩니다. 앱 종료 전에 필요한 보고서를 생성하고, 회사 밖으로 공유하기 전에는 내용을 직접 다시 확인하십시오."
            }
        });
        content.Children.Add(buttons);
        content.Children.Add(new Border
        {
            Margin = new Thickness(0, 18, 0, 0),
            Padding = new Thickness(16),
            CornerRadius = new CornerRadius(8),
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(
                Color.FromRgb(216, 221, 227)),
            BorderThickness = new Thickness(1),
            Child =
                _internalProxyRouteComparisonReportResultText
        });

        return new TabItem
        {
            Header = "경로 보고서",
            Content = new ScrollViewer
            {
                VerticalScrollBarVisibility =
                    ScrollBarVisibility.Auto,
                Content = new Border
                {
                    Padding = new Thickness(20),
                    Child = content
                }
            }
        };
    }

    private async void
        OnGenerateInternalProxyRouteComparisonReportClick(
            object sender,
            RoutedEventArgs e)
    {
        if (_generateInternalProxyRouteComparisonReportButton is null
            || !_generateInternalProxyRouteComparisonReportButton
                .IsEnabled)
        {
            return;
        }

        if (_measurementRunning
            || _observationCancellation is not null
            || _internalProxyRouteComparisonCancellation is not null)
        {
            SetInternalProxyRouteComparisonReportResult(
                "측정·브라우저 관찰 또는 경로 비교가 진행 중입니다. 완료하거나 중지한 뒤 보고서를 생성하십시오.",
                Brushes.DarkOrange);
            return;
        }

        if (!HasLatestInternalProxyRouteComparison
            || _lastProxyEndpointRouteAnalysis is null
            || _lastInternalProxyRouteComparison is null
            || _lastInternalProxyRouteComparisonFinding is null)
        {
            SetInternalProxyRouteComparisonReportResult(
                "저장할 경로 비교 결과가 없습니다. 먼저 경로 비교 탭에서 분석을 완료하십시오.",
                Brushes.DarkOrange);
            return;
        }

        _generateInternalProxyRouteComparisonReportButton.IsEnabled =
            false;
        SetInternalProxyRouteComparisonReportResult(
            "최근 내부·프록시 경로 비교를 로컬 구조화 보고서로 정리하고 있습니다.",
            Brushes.DarkSlateGray);

        try
        {
            Version? version = Assembly.GetExecutingAssembly()
                .GetName()
                .Version;
            InternalProxyRouteComparisonReportDocument document =
                InternalProxyRouteComparisonReportWriter.CreateDocument(
                    _lastInternalProxyRouteComparison,
                    _lastProxyEndpointRouteAnalysis,
                    _lastInternalProxyRouteComparisonFinding,
                    version?.ToString() ?? "개발 빌드");
            InternalProxyRouteComparisonReportExportResult export =
                await Task.Run(() =>
                    InternalProxyRouteComparisonReportWriter.WriteAll(
                        document,
                        GetDefaultReportDirectory()));

            _lastInternalProxyRouteComparisonReportDirectory =
                export.OutputDirectory;
            _lastInternalProxyRouteComparisonReportHtmlPath =
                export.HtmlPath;
            if (_openInternalProxyRouteComparisonReportFolderButton
                is not null)
            {
                _openInternalProxyRouteComparisonReportFolderButton
                    .IsEnabled = true;
            }

            if (_openInternalProxyRouteComparisonReportHtmlButton
                is not null)
            {
                _openInternalProxyRouteComparisonReportHtmlButton
                    .IsEnabled = true;
            }

            StringBuilder builder = new();
            builder.AppendLine("경로 비교 보고서 생성 완료");
            builder.AppendLine(
                $"비교 상태: {document.Comparison.Status}");
            builder.AppendLine(
                $"관계: {document.Comparison.Relation}");
            builder.AppendLine(
                $"판정: {document.Finding.Severity} · {document.Finding.Code}");
            builder.AppendLine(
                $"프록시·DIRECT 항목: {document.ProxyEntries.Count}개");
            builder.AppendLine($"폴더: {export.OutputDirectory}");
            builder.AppendLine(
                $"JSON: {Path.GetFileName(export.JsonPath)}");
            builder.AppendLine(
                $"CSV: {Path.GetFileName(export.CsvPath)}");
            builder.AppendLine(
                $"HTML: {Path.GetFileName(export.HtmlPath)}");
            builder.AppendLine(
                $"무결성: {Path.GetFileName(export.Sha256Path)}");
            builder.AppendLine("추가 네트워크 요청과 외부 전송은 수행하지 않았습니다.");
            SetInternalProxyRouteComparisonReportResult(
                builder.ToString().TrimEnd(),
                GetInternalProxyRouteComparisonReportBrush(
                    document.Comparison.Status));
        }
        catch (Exception exception)
        {
            SetInternalProxyRouteComparisonReportResult(
                $"경로 비교 보고서 생성 중 로컬 파일 오류가 발생했습니다. 오류 유형: {exception.GetType().Name}. 예외 원문은 화면에 표시하지 않았습니다.",
                Brushes.DarkRed);
        }
        finally
        {
            _generateInternalProxyRouteComparisonReportButton.IsEnabled =
                true;
        }
    }

    private void
        OnOpenInternalProxyRouteComparisonReportFolderClick(
            object sender,
            RoutedEventArgs e) =>
        OpenInternalProxyRouteComparisonReportPath(
            _lastInternalProxyRouteComparisonReportDirectory,
            "경로 비교 보고서 폴더를 찾을 수 없습니다.");

    private void OnOpenInternalProxyRouteComparisonReportHtmlClick(
        object sender,
        RoutedEventArgs e) =>
        OpenInternalProxyRouteComparisonReportPath(
            _lastInternalProxyRouteComparisonReportHtmlPath,
            "최신 경로 비교 HTML 보고서를 찾을 수 없습니다.");

    private void OpenInternalProxyRouteComparisonReportPath(
        string? path,
        string missingMessage)
    {
        if (string.IsNullOrWhiteSpace(path)
            || (!Directory.Exists(path) && !File.Exists(path)))
        {
            SetInternalProxyRouteComparisonReportResult(
                missingMessage,
                Brushes.DarkOrange);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
        }
        catch (Exception exception)
        {
            SetInternalProxyRouteComparisonReportResult(
                $"로컬 경로를 열지 못했습니다. 오류 유형: {exception.GetType().Name}.",
                Brushes.DarkRed);
        }
    }

    private static Brush
        GetInternalProxyRouteComparisonReportBrush(
            string status) =>
        status switch
        {
            "Ready" => Brushes.DarkGreen,
            "Diverged" => Brushes.DarkBlue,
            "Ambiguous" => Brushes.DarkOrange,
            _ => Brushes.DarkRed
        };

    private void SetInternalProxyRouteComparisonReportResult(
        string text,
        Brush brush)
    {
        if (_internalProxyRouteComparisonReportResultText is null)
        {
            return;
        }

        _internalProxyRouteComparisonReportResultText.Text = text;
        _internalProxyRouteComparisonReportResultText.Foreground = brush;
    }
}
