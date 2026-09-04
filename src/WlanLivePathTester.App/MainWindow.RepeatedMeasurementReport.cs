using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WlanLivePathTester.Core.Measurements;
using WlanLivePathTester.Core.Reporting;
using WlanLivePathTester.Windows.Measurements;

namespace WlanLivePathTester.App;

public partial class MainWindow
{
    private Button? _generateRepeatedReportButton;
    private Button? _openRepeatedReportFolderButton;
    private Button? _openRepeatedReportHtmlButton;
    private TextBlock? _repeatedReportResultText;
    private string? _lastRepeatedReportDirectory;
    private string? _lastRepeatedReportHtmlPath;
    private bool _repeatedReportTabAdded;

    internal void EnsureRepeatedMeasurementReportTab()
    {
        if (_repeatedReportTabAdded)
        {
            return;
        }

        TabControl? tabControl = FindVisualDescendant<TabControl>(this);
        if (tabControl is null)
        {
            return;
        }

        tabControl.Items.Add(CreateRepeatedMeasurementReportTab());
        _repeatedReportTabAdded = true;
    }

    private TabItem CreateRepeatedMeasurementReportTab()
    {
        _generateRepeatedReportButton = new Button
        {
            Content = "반복 측정 보고서 생성",
            MinWidth = 190,
            Padding = new Thickness(12, 8, 12, 8)
        };
        _generateRepeatedReportButton.Click +=
            OnGenerateRepeatedReportClick;

        _openRepeatedReportFolderButton = new Button
        {
            Content = "보고서 폴더 열기",
            MinWidth = 140,
            Padding = new Thickness(12, 8, 12, 8),
            IsEnabled = false
        };
        _openRepeatedReportFolderButton.Click +=
            OnOpenRepeatedReportFolderClick;

        _openRepeatedReportHtmlButton = new Button
        {
            Content = "최신 HTML 열기",
            MinWidth = 130,
            Padding = new Thickness(12, 8, 12, 8),
            IsEnabled = false
        };
        _openRepeatedReportHtmlButton.Click +=
            OnOpenRepeatedReportHtmlClick;

        _repeatedReportResultText = new TextBlock
        {
            FontFamily = new FontFamily("Consolas"),
            TextWrapping = TextWrapping.Wrap,
            Text = "아직 반복 측정 보고서를 생성하지 않았습니다."
        };

        StackPanel buttons = new()
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 16, 0, 0)
        };
        buttons.Children.Add(_generateRepeatedReportButton);
        buttons.Children.Add(new Border { Width = 10 });
        buttons.Children.Add(_openRepeatedReportFolderButton);
        buttons.Children.Add(new Border { Width = 10 });
        buttons.Children.Add(_openRepeatedReportHtmlButton);

        StackPanel content = new();
        content.Children.Add(new TextBlock
        {
            FontSize = 22,
            FontWeight = FontWeights.SemiBold,
            Text = "반복 측정 구조화 보고서"
        });
        content.Children.Add(new TextBlock
        {
            Margin = new Thickness(0, 8, 0, 0),
            Foreground = new SolidColorBrush(Color.FromRgb(86, 101, 115)),
            TextWrapping = TextWrapping.Wrap,
            Text = "현재 실행 중 메모리에 남아 있는 반복 측정의 중앙값·편차·신뢰도와 각 회차 결과를 JSON·CSV·단일 HTML로 저장합니다. 보고서 생성은 네트워크 요청을 만들지 않습니다."
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
                Text = "보고서에는 대상 이름과 내부·외부 구분만 기록합니다. 대상 URL, 프록시 주소, PAC URL, SSID와 BSSID는 포함하지 않으며 HTML은 외부 JavaScript·폰트·이미지·iframe을 사용하지 않습니다."
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
                Text = "반복 측정 이력은 앱을 종료하면 사라지는 최대 8건의 메모리 이력입니다. 필요한 보고서는 앱을 종료하기 전에 생성하십시오."
            }
        });
        content.Children.Add(buttons);
        content.Children.Add(new Border
        {
            Margin = new Thickness(0, 18, 0, 0),
            Padding = new Thickness(16),
            CornerRadius = new CornerRadius(8),
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(216, 221, 227)),
            BorderThickness = new Thickness(1),
            Child = _repeatedReportResultText
        });

        return new TabItem
        {
            Header = "반복 보고서",
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

    private async void OnGenerateRepeatedReportClick(
        object sender,
        RoutedEventArgs e)
    {
        if (_generateRepeatedReportButton is null
            || !_generateRepeatedReportButton.IsEnabled)
        {
            return;
        }

        if (_measurementRunning || _observationCancellation is not null)
        {
            SetRepeatedReportResult(
                "측정 또는 브라우저 관찰이 진행 중입니다. 완료하거나 중지한 뒤 보고서를 생성하십시오.",
                Brushes.DarkOrange);
            return;
        }

        IReadOnlyList<RepeatedMeasurementResult> results =
            RepeatedMeasurementResultHistory.Snapshot();
        if (results.Count == 0)
        {
            SetRepeatedReportResult(
                "저장할 반복 측정 결과가 없습니다. 먼저 반복 측정을 실행하십시오.",
                Brushes.DarkOrange);
            return;
        }

        _generateRepeatedReportButton.IsEnabled = false;
        SetRepeatedReportResult(
            $"반복 측정 {results.Count}건을 로컬 보고서로 정리하고 있습니다.",
            Brushes.DarkSlateGray);

        try
        {
            Version? version = Assembly.GetExecutingAssembly()
                .GetName()
                .Version;
            RepeatedMeasurementReportDocument document =
                RepeatedMeasurementReportWriter.CreateDocument(
                    results,
                    version?.ToString() ?? "개발 빌드");
            RepeatedMeasurementReportExportResult export =
                await Task.Run(() =>
                    RepeatedMeasurementReportWriter.WriteAll(
                        document,
                        GetDefaultReportDirectory()));

            _lastRepeatedReportDirectory = export.OutputDirectory;
            _lastRepeatedReportHtmlPath = export.HtmlPath;
            if (_openRepeatedReportFolderButton is not null)
            {
                _openRepeatedReportFolderButton.IsEnabled = true;
            }

            if (_openRepeatedReportHtmlButton is not null)
            {
                _openRepeatedReportHtmlButton.IsEnabled = true;
            }

            StringBuilder builder = new();
            builder.AppendLine("반복 측정 보고서 생성 완료");
            builder.AppendLine($"측정 요약: {document.Measurements.Count}건");
            builder.AppendLine($"폴더: {export.OutputDirectory}");
            builder.AppendLine($"JSON: {Path.GetFileName(export.JsonPath)}");
            builder.AppendLine($"CSV: {Path.GetFileName(export.CsvPath)}");
            builder.AppendLine($"HTML: {Path.GetFileName(export.HtmlPath)}");
            builder.AppendLine($"무결성: {Path.GetFileName(export.Sha256Path)}");
            builder.AppendLine("외부 전송은 수행하지 않았습니다.");
            SetRepeatedReportResult(
                builder.ToString().TrimEnd(),
                Brushes.DarkGreen);
        }
        catch (Exception exception)
        {
            SetRepeatedReportResult(
                $"반복 측정 보고서 생성 중 오류가 발생했습니다: {exception.Message}",
                Brushes.DarkRed);
        }
        finally
        {
            _generateRepeatedReportButton.IsEnabled = true;
        }
    }

    private void OnOpenRepeatedReportFolderClick(
        object sender,
        RoutedEventArgs e) =>
        OpenRepeatedReportPath(
            _lastRepeatedReportDirectory,
            "반복 측정 보고서 폴더를 찾을 수 없습니다.");

    private void OnOpenRepeatedReportHtmlClick(
        object sender,
        RoutedEventArgs e) =>
        OpenRepeatedReportPath(
            _lastRepeatedReportHtmlPath,
            "최신 반복 측정 HTML 보고서를 찾을 수 없습니다.");

    private void OpenRepeatedReportPath(
        string? path,
        string missingMessage)
    {
        if (string.IsNullOrWhiteSpace(path)
            || (!Directory.Exists(path) && !File.Exists(path)))
        {
            SetRepeatedReportResult(missingMessage, Brushes.DarkOrange);
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
            SetRepeatedReportResult(
                $"로컬 경로를 열지 못했습니다: {exception.Message}",
                Brushes.DarkRed);
        }
    }

    private void SetRepeatedReportResult(
        string text,
        Brush brush)
    {
        if (_repeatedReportResultText is null)
        {
            return;
        }

        _repeatedReportResultText.Text = text;
        _repeatedReportResultText.Foreground = brush;
    }
}
