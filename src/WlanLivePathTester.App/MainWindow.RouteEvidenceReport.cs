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
    private Button? _generateRouteEvidenceReportButton;
    private Button? _openRouteEvidenceReportFolderButton;
    private Button? _openRouteEvidenceReportHtmlButton;
    private TextBlock? _routeEvidenceReportResultText;
    private string? _lastRouteEvidenceReportDirectory;
    private string? _lastRouteEvidenceReportHtmlPath;
    private bool _routeEvidenceReportTabAdded;

    internal void EnsureRouteEvidenceReportTab()
    {
        if (_routeEvidenceReportTabAdded)
        {
            return;
        }

        TabControl? tabControl = FindVisualDescendant<TabControl>(this);
        if (tabControl is null)
        {
            return;
        }

        tabControl.Items.Add(CreateRouteEvidenceReportTab());
        _routeEvidenceReportTabAdded = true;
    }

    private TabItem CreateRouteEvidenceReportTab()
    {
        _generateRouteEvidenceReportButton = new Button
        {
            Content = "라우팅 근거 보고서 생성",
            MinWidth = 200,
            Padding = new Thickness(12, 8, 12, 8)
        };
        _generateRouteEvidenceReportButton.Click +=
            OnGenerateRouteEvidenceReportClick;

        _openRouteEvidenceReportFolderButton = new Button
        {
            Content = "보고서 폴더 열기",
            MinWidth = 140,
            Padding = new Thickness(12, 8, 12, 8),
            IsEnabled = false
        };
        _openRouteEvidenceReportFolderButton.Click +=
            OnOpenRouteEvidenceReportFolderClick;

        _openRouteEvidenceReportHtmlButton = new Button
        {
            Content = "최신 HTML 열기",
            MinWidth = 130,
            Padding = new Thickness(12, 8, 12, 8),
            IsEnabled = false
        };
        _openRouteEvidenceReportHtmlButton.Click +=
            OnOpenRouteEvidenceReportHtmlClick;

        _routeEvidenceReportResultText = new TextBlock
        {
            FontFamily = new FontFamily("Consolas"),
            TextWrapping = TextWrapping.Wrap,
            Text = "아직 라우팅 근거 보고서를 생성하지 않았습니다."
        };

        StackPanel actions = new()
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 16, 0, 0)
        };
        actions.Children.Add(_generateRouteEvidenceReportButton);
        actions.Children.Add(new Border { Width = 10 });
        actions.Children.Add(_openRouteEvidenceReportFolderButton);
        actions.Children.Add(new Border { Width = 10 });
        actions.Children.Add(_openRouteEvidenceReportHtmlButton);

        StackPanel content = new();
        content.Children.Add(new TextBlock
        {
            FontSize = 22,
            FontWeight = FontWeights.SemiBold,
            Text = "라우팅 근거 구조화 보고서"
        });
        content.Children.Add(new TextBlock
        {
            Margin = new Thickness(0, 8, 0, 0),
            Foreground = new SolidColorBrush(Color.FromRgb(86, 101, 115)),
            TextWrapping = TextWrapping.Wrap,
            Text = "현재 앱 실행에서 확인한 최근 라우팅 근거 최대 12건을 JSON·CSV·단일 HTML과 SHA-256으로 저장합니다. 보고서 생성 자체는 네트워크 요청을 만들지 않습니다."
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
                Text = "보고서에는 라우팅 목적·상태·DNS 사용 여부·주소 수·인터페이스 범주·짧은 ID 지문만 기록합니다. 해석한 IP, 게이트웨이, DNS 서버, MAC, 인터페이스 이름·설명과 전체 GUID는 모델 자체에 포함하지 않습니다."
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
                Text = "라우팅 이력은 앱 종료 시 사라지는 메모리 이력입니다. 외부 사이트 참고 경로는 회사 프록시의 실제 HTTP 연결 경로가 아닐 수 있으므로 프록시 경로 판정과 함께 해석하십시오."
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
            Child = _routeEvidenceReportResultText
        });

        return new TabItem
        {
            Header = "라우팅 보고서",
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

    private async void OnGenerateRouteEvidenceReportClick(
        object sender,
        RoutedEventArgs e)
    {
        if (_generateRouteEvidenceReportButton is null
            || !_generateRouteEvidenceReportButton.IsEnabled)
        {
            return;
        }

        if (_measurementRunning
            || _observationCancellation is not null
            || _routeEvidenceCancellation is not null)
        {
            SetRouteEvidenceReportResult(
                "측정·브라우저 관찰 또는 라우팅 확인이 진행 중입니다. 완료하거나 중지한 뒤 보고서를 생성하십시오.",
                Brushes.DarkOrange);
            return;
        }

        IReadOnlyList<DestinationRouteEvidence> results =
            RouteEvidenceResultHistory.Snapshot();
        if (results.Count == 0)
        {
            SetRouteEvidenceReportResult(
                "저장할 라우팅 근거가 없습니다. 먼저 라우팅 근거 탭에서 내부 대상 또는 프록시 엔드포인트를 확인하십시오.",
                Brushes.DarkOrange);
            return;
        }

        _generateRouteEvidenceReportButton.IsEnabled = false;
        SetRouteEvidenceReportResult(
            $"라우팅 근거 {results.Count}건을 로컬 보고서로 정리하고 있습니다.",
            Brushes.DarkSlateGray);

        try
        {
            Version? version = Assembly.GetExecutingAssembly()
                .GetName()
                .Version;
            RouteEvidenceReportDocument document =
                RouteEvidenceReportWriter.CreateDocument(
                    results,
                    version?.ToString() ?? "개발 빌드");
            RouteEvidenceReportExportResult export =
                await Task.Run(() =>
                    RouteEvidenceReportWriter.WriteAll(
                        document,
                        GetDefaultReportDirectory()));

            _lastRouteEvidenceReportDirectory = export.OutputDirectory;
            _lastRouteEvidenceReportHtmlPath = export.HtmlPath;
            if (_openRouteEvidenceReportFolderButton is not null)
            {
                _openRouteEvidenceReportFolderButton.IsEnabled = true;
            }

            if (_openRouteEvidenceReportHtmlButton is not null)
            {
                _openRouteEvidenceReportHtmlButton.IsEnabled = true;
            }

            StringBuilder builder = new();
            builder.AppendLine("라우팅 근거 보고서 생성 완료");
            builder.AppendLine($"라우팅 결과: {document.Results.Count}건");
            builder.AppendLine($"폴더: {export.OutputDirectory}");
            builder.AppendLine($"JSON: {Path.GetFileName(export.JsonPath)}");
            builder.AppendLine($"CSV: {Path.GetFileName(export.CsvPath)}");
            builder.AppendLine($"HTML: {Path.GetFileName(export.HtmlPath)}");
            builder.AppendLine($"무결성: {Path.GetFileName(export.Sha256Path)}");
            builder.AppendLine("외부 전송은 수행하지 않았습니다.");
            SetRouteEvidenceReportResult(
                builder.ToString().TrimEnd(),
                Brushes.DarkGreen);
        }
        catch (Exception exception)
        {
            SetRouteEvidenceReportResult(
                $"라우팅 근거 보고서 생성 중 오류가 발생했습니다: {exception.Message}",
                Brushes.DarkRed);
        }
        finally
        {
            _generateRouteEvidenceReportButton.IsEnabled = true;
        }
    }

    private void OnOpenRouteEvidenceReportFolderClick(
        object sender,
        RoutedEventArgs e) =>
        OpenRouteEvidenceReportPath(
            _lastRouteEvidenceReportDirectory,
            "라우팅 근거 보고서 폴더를 찾을 수 없습니다.");

    private void OnOpenRouteEvidenceReportHtmlClick(
        object sender,
        RoutedEventArgs e) =>
        OpenRouteEvidenceReportPath(
            _lastRouteEvidenceReportHtmlPath,
            "최신 라우팅 근거 HTML 보고서를 찾을 수 없습니다.");

    private void OpenRouteEvidenceReportPath(
        string? path,
        string missingMessage)
    {
        if (string.IsNullOrWhiteSpace(path)
            || (!Directory.Exists(path) && !File.Exists(path)))
        {
            SetRouteEvidenceReportResult(
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
            SetRouteEvidenceReportResult(
                $"로컬 경로를 열지 못했습니다: {exception.Message}",
                Brushes.DarkRed);
        }
    }

    private void SetRouteEvidenceReportResult(
        string text,
        Brush brush)
    {
        if (_routeEvidenceReportResultText is null)
        {
            return;
        }

        _routeEvidenceReportResultText.Text = text;
        _routeEvidenceReportResultText.Foreground = brush;
    }
}
