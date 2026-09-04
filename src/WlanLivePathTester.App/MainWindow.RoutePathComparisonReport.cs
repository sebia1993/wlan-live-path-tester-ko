using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WlanLivePathTester.Core.Reporting;
using WlanLivePathTester.Core.Routing;
using WlanLivePathTester.Windows.Routing;

namespace WlanLivePathTester.App;

public partial class MainWindow
{
    private Button? _generateRouteComparisonReportButton;
    private Button? _openRouteComparisonReportFolderButton;
    private Button? _openRouteComparisonReportHtmlButton;
    private TextBlock? _routeComparisonReportResultText;
    private string? _lastRouteComparisonReportDirectory;
    private string? _lastRouteComparisonReportHtmlPath;
    private bool _routeComparisonReportTabAdded;

    internal void EnsureRoutePathComparisonReportTab()
    {
        if (_routeComparisonReportTabAdded)
        {
            return;
        }

        TabControl? tabControl = FindVisualDescendant<TabControl>(this);
        if (tabControl is null)
        {
            return;
        }

        tabControl.Items.Add(CreateRoutePathComparisonReportTab());
        _routeComparisonReportTabAdded = true;
    }

    private TabItem CreateRoutePathComparisonReportTab()
    {
        _generateRouteComparisonReportButton = new Button
        {
            Content = "경로 비교 보고서 생성",
            MinWidth = 190,
            Padding = new Thickness(12, 8, 12, 8)
        };
        _generateRouteComparisonReportButton.Click +=
            OnGenerateRouteComparisonReportClick;

        _openRouteComparisonReportFolderButton = new Button
        {
            Content = "보고서 폴더 열기",
            MinWidth = 140,
            Padding = new Thickness(12, 8, 12, 8),
            IsEnabled = false
        };
        _openRouteComparisonReportFolderButton.Click +=
            OnOpenRouteComparisonReportFolderClick;

        _openRouteComparisonReportHtmlButton = new Button
        {
            Content = "최신 HTML 열기",
            MinWidth = 130,
            Padding = new Thickness(12, 8, 12, 8),
            IsEnabled = false
        };
        _openRouteComparisonReportHtmlButton.Click +=
            OnOpenRouteComparisonReportHtmlClick;

        _routeComparisonReportResultText = new TextBlock
        {
            FontFamily = new FontFamily("Consolas"),
            TextWrapping = TextWrapping.Wrap,
            Text = "아직 내부·프록시 경로 비교 보고서를 생성하지 않았습니다."
        };

        StackPanel actions = new()
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 16, 0, 0)
        };
        actions.Children.Add(_generateRouteComparisonReportButton);
        actions.Children.Add(new Border { Width = 10 });
        actions.Children.Add(_openRouteComparisonReportFolderButton);
        actions.Children.Add(new Border { Width = 10 });
        actions.Children.Add(_openRouteComparisonReportHtmlButton);

        StackPanel content = new();
        content.Children.Add(new TextBlock
        {
            FontSize = 22,
            FontWeight = FontWeights.SemiBold,
            Text = "내부 · 프록시 경로 비교 보고서"
        });
        content.Children.Add(new TextBlock
        {
            Margin = new Thickness(0, 8, 0, 0),
            Foreground = new SolidColorBrush(Color.FromRgb(86, 101, 115)),
            TextWrapping = TextWrapping.Wrap,
            Text = "현재 라우팅 메모리 이력에서 목적별 최신 결과를 다시 비교해 Ready·Incomplete·Ambiguous·Diverged 상태와 고정 규칙 Finding을 JSON·CSV·단일 HTML로 저장합니다."
        });
        content.Children.Add(new Border
        {
            Margin = new Thickness(0, 16, 0, 0),
            Padding = new Thickness(14),
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(Color.FromRgb(232, 246, 243)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(115, 198, 182)),
            BorderThickness = new Thickness(1),
            Child = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Text = "보고서 생성은 기존 메모리 이력만 사용합니다. DNS·HTTP·PAC/WPAD·외부 API 요청이나 업로드는 수행하지 않습니다. 인터페이스는 범주와 SHA-256 앞 10자리 지문으로만 기록합니다."
            }
        });
        content.Children.Add(new Border
        {
            Margin = new Thickness(0, 12, 0, 0),
            Padding = new Thickness(14),
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(Color.FromRgb(255, 248, 231)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(232, 206, 138)),
            BorderThickness = new Thickness(1),
            Child = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Text = "프록시 엔드포인트 라우팅 근거가 없으면 보고서는 Incomplete로 저장됩니다. 외부 사이트 참고 경로만으로 회사 프록시의 실제 로컬 연결 경로를 확정하지 않습니다."
            }
        });
        content.Children.Add(actions);
        content.Children.Add(new Border
        {
            Margin = new Thickness(0, 18, 0, 0),
            Padding = new Thickness(16),
            CornerRadius = new CornerRadius(8),
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(216, 221, 227)),
            BorderThickness = new Thickness(1),
            Child = _routeComparisonReportResultText
        });

        return new TabItem
        {
            Header = "경로 비교 보고서",
            Content = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = new Border
                {
                    Padding = new Thickness(20),
                    Child = content
                }
            }
        };
    }

    private async void OnGenerateRouteComparisonReportClick(
        object sender,
        RoutedEventArgs e)
    {
        if (_generateRouteComparisonReportButton is null
            || !_generateRouteComparisonReportButton.IsEnabled)
        {
            return;
        }

        if (_measurementRunning
            || _observationCancellation is not null
            || _routeEvidenceCancellation is not null)
        {
            SetRouteComparisonReportResult(
                "측정·브라우저 관찰 또는 라우팅 확인이 진행 중입니다. 완료하거나 중지한 뒤 보고서를 생성하십시오.",
                Brushes.DarkOrange);
            return;
        }

        IReadOnlyList<DestinationRouteEvidence> history =
            RouteEvidenceResultHistory.Snapshot();
        if (history.Count == 0)
        {
            SetRouteComparisonReportResult(
                "비교할 라우팅 근거가 없습니다. 먼저 라우팅 근거 탭에서 내부 대상과 프록시 엔드포인트를 확인하십시오.",
                Brushes.DarkOrange);
            return;
        }

        _generateRouteComparisonReportButton.IsEnabled = false;
        SetRouteComparisonReportResult(
            "현재 라우팅 이력을 비교하고 로컬 보고서를 생성하고 있습니다.",
            Brushes.DarkSlateGray);

        try
        {
            RoutePathComparisonResult comparison =
                RoutePathComparisonEvaluator.Evaluate(history);
            Version? version = Assembly.GetExecutingAssembly()
                .GetName()
                .Version;
            RoutePathComparisonReportDocument document =
                RoutePathComparisonReportWriter.CreateDocument(
                    comparison,
                    version?.ToString() ?? "개발 빌드");
            RoutePathComparisonReportExportResult export =
                await Task.Run(() =>
                    RoutePathComparisonReportWriter.WriteAll(
                        document,
                        GetDefaultReportDirectory()));

            _lastRouteComparisonReportDirectory = export.OutputDirectory;
            _lastRouteComparisonReportHtmlPath = export.HtmlPath;
            if (_openRouteComparisonReportFolderButton is not null)
            {
                _openRouteComparisonReportFolderButton.IsEnabled = true;
            }

            if (_openRouteComparisonReportHtmlButton is not null)
            {
                _openRouteComparisonReportHtmlButton.IsEnabled = true;
            }

            StringBuilder builder = new();
            builder.AppendLine("내부·프록시 경로 비교 보고서 생성 완료");
            builder.AppendLine($"비교 상태: {document.Status}");
            builder.AppendLine($"판정: {document.Findings.Count}개");
            builder.AppendLine($"폴더: {export.OutputDirectory}");
            builder.AppendLine($"JSON: {Path.GetFileName(export.JsonPath)}");
            builder.AppendLine($"CSV: {Path.GetFileName(export.CsvPath)}");
            builder.AppendLine($"HTML: {Path.GetFileName(export.HtmlPath)}");
            builder.AppendLine($"무결성: {Path.GetFileName(export.Sha256Path)}");
            builder.AppendLine("외부 전송은 수행하지 않았습니다.");
            SetRouteComparisonReportResult(
                builder.ToString().TrimEnd(),
                comparison.Status switch
                {
                    RoutePathComparisonStatus.Ready => Brushes.DarkGreen,
                    RoutePathComparisonStatus.Incomplete
                        or RoutePathComparisonStatus.Ambiguous
                        => Brushes.DarkOrange,
                    _ => Brushes.DarkRed
                });
        }
        catch (Exception exception)
        {
            SetRouteComparisonReportResult(
                $"경로 비교 보고서 생성 중 오류가 발생했습니다: {exception.Message}",
                Brushes.DarkRed);
        }
        finally
        {
            _generateRouteComparisonReportButton.IsEnabled = true;
        }
    }

    private void OnOpenRouteComparisonReportFolderClick(
        object sender,
        RoutedEventArgs e) =>
        OpenRouteComparisonReportPath(
            _lastRouteComparisonReportDirectory,
            "경로 비교 보고서 폴더를 찾을 수 없습니다.");

    private void OnOpenRouteComparisonReportHtmlClick(
        object sender,
        RoutedEventArgs e) =>
        OpenRouteComparisonReportPath(
            _lastRouteComparisonReportHtmlPath,
            "최신 경로 비교 HTML 보고서를 찾을 수 없습니다.");

    private void OpenRouteComparisonReportPath(
        string? path,
        string missingMessage)
    {
        if (string.IsNullOrWhiteSpace(path)
            || (!Directory.Exists(path) && !File.Exists(path)))
        {
            SetRouteComparisonReportResult(
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
            SetRouteComparisonReportResult(
                $"로컬 경로를 열지 못했습니다: {exception.Message}",
                Brushes.DarkRed);
        }
    }

    private void SetRouteComparisonReportResult(
        string text,
        Brush brush)
    {
        if (_routeComparisonReportResultText is null)
        {
            return;
        }

        _routeComparisonReportResultText.Text = text;
        _routeComparisonReportResultText.Foreground = brush;
    }
}
