using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WlanLivePathTester.Core.Reporting;
using WlanLivePathTester.Core.Routing;

namespace WlanLivePathTester.App;

public partial class MainWindow
{
    private Button? _routeComparisonReportGenerateV2;
    private Button? _routeComparisonReportOpenFolderV2;
    private Button? _routeComparisonReportOpenHtmlV2;
    private TextBlock? _routeComparisonReportResultV2;
    private string? _latestRouteComparisonReportDirectoryV2;
    private string? _latestRouteComparisonReportHtmlV2;
    private bool _routeComparisonReportTabAddedV2;

    internal void EnsureRouteComparisonReportTabV2()
    {
        if (_routeComparisonReportTabAddedV2)
        {
            return;
        }

        TabControl? tabControl =
            FindRouteComparisonDescendantV3<TabControl>(this);
        if (tabControl is null)
        {
            return;
        }

        tabControl.Items.Add(CreateRouteComparisonReportTabV2());
        _routeComparisonReportTabAddedV2 = true;
    }

    private TabItem CreateRouteComparisonReportTabV2()
    {
        _routeComparisonReportGenerateV2 = new Button
        {
            Content = "경로 비교 보고서 생성",
            MinWidth = 190,
            Padding = new Thickness(13, 8, 13, 8)
        };
        _routeComparisonReportGenerateV2.Click +=
            OnGenerateRouteComparisonReportV2;

        _routeComparisonReportOpenFolderV2 = new Button
        {
            Content = "보고서 폴더 열기",
            MinWidth = 140,
            Padding = new Thickness(13, 8, 13, 8),
            IsEnabled = false
        };
        _routeComparisonReportOpenFolderV2.Click +=
            OnOpenRouteComparisonReportFolderV2;

        _routeComparisonReportOpenHtmlV2 = new Button
        {
            Content = "최신 HTML 열기",
            MinWidth = 130,
            Padding = new Thickness(13, 8, 13, 8),
            IsEnabled = false
        };
        _routeComparisonReportOpenHtmlV2.Click +=
            OnOpenRouteComparisonReportHtmlV2;

        _routeComparisonReportResultV2 = new TextBlock
        {
            FontFamily = new FontFamily("Consolas"),
            TextWrapping = TextWrapping.Wrap,
            Text =
                "아직 경로 비교 보고서를 생성하지 않았습니다. 먼저 경로 비교 탭에서 실행 결과를 만드십시오."
        };

        WrapPanel buttons = new()
        {
            Margin = new Thickness(0, 16, 0, 0)
        };
        buttons.Children.Add(_routeComparisonReportGenerateV2);
        buttons.Children.Add(new Border { Width = 10 });
        buttons.Children.Add(_routeComparisonReportOpenFolderV2);
        buttons.Children.Add(new Border { Width = 10 });
        buttons.Children.Add(_routeComparisonReportOpenHtmlV2);

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
                "가장 최근 구조화 경로 비교 결과를 안전 DTO로 다시 매핑한 뒤 JSON·CSV·단일 HTML·SHA-256으로 현재 PC에 저장합니다. 보고서 생성은 DNS·라우팅·HTTP·프록시 요청을 추가로 수행하지 않습니다."
        });
        content.Children.Add(CreateRouteReportNoticeV2(
            "내부·외부 URL, 프록시 호스트·지시문, 전체 인터페이스 GUID·이름·설명, IP·MAC·게이트웨이·DNS·SSID·BSSID와 원본 경로 객체를 저장하지 않습니다.",
            Color.FromRgb(232, 246, 243),
            Color.FromRgb(115, 198, 182)));
        content.Children.Add(CreateRouteReportNoticeV2(
            "보고서에는 상태·개수·Boolean·알려진 인터페이스 범주·SHA-256 앞 10자리 지문과 고정 Finding만 포함됩니다. 회사 밖으로 공유하기 전에는 내용을 직접 다시 확인하십시오.",
            Color.FromRgb(255, 248, 231),
            Color.FromRgb(232, 206, 138)));
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
            Child = _routeComparisonReportResultV2
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

    private async void OnGenerateRouteComparisonReportV2(
        object sender,
        RoutedEventArgs e)
    {
        if (_routeComparisonReportGenerateV2 is null
            || !_routeComparisonReportGenerateV2.IsEnabled)
        {
            return;
        }

        if (_measurementRunning
            || _observationCancellation is not null
            || _routeComparisonCancellationV3 is not null)
        {
            SetRouteComparisonReportResultV2(
                "다운로드 측정, 브라우저 관찰 또는 경로 비교가 진행 중입니다. 완료하거나 중지한 뒤 보고서를 생성하십시오.",
                Brushes.DarkOrange);
            return;
        }

        InternalProxyRouteComparisonRunResult? run =
            LatestRouteComparisonRunV3;
        if (run is null)
        {
            SetRouteComparisonReportResultV2(
                "저장할 구조화 경로 비교 결과가 없습니다. 먼저 경로 비교 탭에서 비교를 실행하십시오.",
                Brushes.DarkOrange);
            return;
        }

        _routeComparisonReportGenerateV2.IsEnabled = false;
        SetRouteComparisonReportResultV2(
            "최근 경로 비교를 안전 보고서 모델로 다시 매핑하고 로컬 파일을 생성하고 있습니다.",
            Brushes.DarkSlateGray);

        try
        {
            Version? assemblyVersion = Assembly
                .GetExecutingAssembly()
                .GetName()
                .Version;
            string applicationVersion = assemblyVersion is null
                ? "development"
                : assemblyVersion.ToString();
            InternalProxyRouteComparisonRunReportDocument document =
                InternalProxyRouteComparisonRunReportWriter
                    .CreateDocument(
                        run,
                        applicationVersion);
            string outputDirectory =
                GetRouteComparisonReportDirectoryV2();
            InternalProxyRouteComparisonRunReportExportResult export =
                await Task.Run(() =>
                    InternalProxyRouteComparisonRunReportWriter.WriteAll(
                        document,
                        outputDirectory));

            _latestRouteComparisonReportDirectoryV2 =
                export.OutputDirectory;
            _latestRouteComparisonReportHtmlV2 = export.HtmlPath;
            if (_routeComparisonReportOpenFolderV2 is not null)
            {
                _routeComparisonReportOpenFolderV2.IsEnabled = true;
            }

            if (_routeComparisonReportOpenHtmlV2 is not null)
            {
                _routeComparisonReportOpenHtmlV2.IsEnabled = true;
            }

            StringBuilder builder = new();
            builder.AppendLine("경로 비교 보고서 생성 완료");
            builder.AppendLine(
                $"실행 상태: {document.RouteComparison.RunStatus}");
            builder.AppendLine(
                $"비교 상태: {document.RouteComparison.Comparison?.Status ?? "없음"}");
            builder.AppendLine(
                $"판정: {document.RouteComparison.Finding.Severity} · {document.RouteComparison.Finding.Code}");
            builder.AppendLine(
                $"JSON: {Path.GetFileName(export.JsonPath)}");
            builder.AppendLine(
                $"CSV: {Path.GetFileName(export.CsvPath)}");
            builder.AppendLine(
                $"HTML: {Path.GetFileName(export.HtmlPath)}");
            builder.AppendLine(
                $"무결성: {Path.GetFileName(export.Sha256Path)}");
            builder.AppendLine(
                "전체 사용자 경로는 화면에 표시하지 않았습니다.");
            builder.AppendLine(
                "추가 DNS·라우팅·HTTP·프록시 요청과 외부 전송은 수행하지 않았습니다.");
            SetRouteComparisonReportResultV2(
                builder.ToString().TrimEnd(),
                GetRouteComparisonReportBrushV2(
                    document.RouteComparison.RunStatus,
                    document.RouteComparison.Comparison?.Status));
        }
        catch (Exception exception)
        {
            SetRouteComparisonReportResultV2(
                $"경로 비교 보고서 생성 중 로컬 처리 오류가 발생했습니다. 오류 유형: {exception.GetType().Name}. 예외 메시지와 원본 입력은 화면에 표시하지 않았습니다.",
                Brushes.DarkRed);
        }
        finally
        {
            _routeComparisonReportGenerateV2.IsEnabled = true;
        }
    }

    private void OnOpenRouteComparisonReportFolderV2(
        object sender,
        RoutedEventArgs e) =>
        OpenRouteComparisonReportPathV2(
            _latestRouteComparisonReportDirectoryV2,
            requireDirectory: true,
            "경로 비교 보고서 폴더를 찾을 수 없습니다.");

    private void OnOpenRouteComparisonReportHtmlV2(
        object sender,
        RoutedEventArgs e) =>
        OpenRouteComparisonReportPathV2(
            _latestRouteComparisonReportHtmlV2,
            requireDirectory: false,
            "최신 경로 비교 HTML 보고서를 찾을 수 없습니다.");

    private void OpenRouteComparisonReportPathV2(
        string? path,
        bool requireDirectory,
        string missingMessage)
    {
        bool exists = !string.IsNullOrWhiteSpace(path)
            && (requireDirectory
                ? Directory.Exists(path)
                : File.Exists(path));
        if (!exists)
        {
            SetRouteComparisonReportResultV2(
                missingMessage,
                Brushes.DarkOrange);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = path!,
                UseShellExecute = true
            });
        }
        catch (Exception exception)
        {
            SetRouteComparisonReportResultV2(
                $"로컬 보고서 경로를 열지 못했습니다. 오류 유형: {exception.GetType().Name}. 전체 경로와 예외 메시지는 표시하지 않았습니다.",
                Brushes.DarkRed);
        }
    }

    private static string GetRouteComparisonReportDirectoryV2()
    {
        string localApplicationData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localApplicationData))
        {
            throw new InvalidOperationException(
                "로컬 애플리케이션 데이터 경로를 확인할 수 없습니다.");
        }

        return Path.Combine(
            localApplicationData,
            "WlanLivePathTesterKO",
            "Reports",
            "RouteComparison");
    }

    private static Border CreateRouteReportNoticeV2(
        string text,
        Color background,
        Color border) =>
        new()
        {
            Margin = new Thickness(0, 12, 0, 0),
            Padding = new Thickness(14),
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(background),
            BorderBrush = new SolidColorBrush(border),
            BorderThickness = new Thickness(1),
            Child = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Text = text
            }
        };

    private static Brush GetRouteComparisonReportBrushV2(
        string runStatus,
        string? comparisonStatus)
    {
        if (runStatus.Equals(
                "Completed",
                StringComparison.Ordinal))
        {
            return comparisonStatus switch
            {
                "Ready" => Brushes.DarkGreen,
                "Diverged" => Brushes.DarkBlue,
                "Ambiguous" => Brushes.DarkOrange,
                _ => Brushes.DarkRed
            };
        }

        return runStatus switch
        {
            "DirectPathSelected" => Brushes.DarkBlue,
            "Canceled" => Brushes.DarkOrange,
            "ProxySourceUnavailable" => Brushes.DarkSlateGray,
            _ => Brushes.DarkRed
        };
    }

    private void SetRouteComparisonReportResultV2(
        string text,
        Brush brush)
    {
        if (_routeComparisonReportResultV2 is null)
        {
            return;
        }

        _routeComparisonReportResultV2.Text = text;
        _routeComparisonReportResultV2.Foreground = brush;
    }
}
