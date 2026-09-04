using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WlanLivePathTester.Core.Observation;
using WlanLivePathTester.Core.Reporting;

namespace WlanLivePathTester.App;

public partial class MainWindow
{
    private Button? _generateObservationSessionReportButton;
    private Button? _openObservationSessionReportFolderButton;
    private Button? _openObservationSessionReportHtmlButton;
    private TextBlock? _observationSessionReportResultText;
    private string? _lastObservationSessionReportDirectory;
    private string? _lastObservationSessionReportHtmlPath;
    private bool _observationSessionReportTabAdded;

    internal void EnsureBrowserObservationSessionReportTab()
    {
        if (_observationSessionReportTabAdded)
        {
            return;
        }

        TabControl? tabControl = FindVisualDescendant<TabControl>(this);
        if (tabControl is null)
        {
            return;
        }

        tabControl.Items.Add(CreateBrowserObservationSessionReportTab());
        _observationSessionReportTabAdded = true;
    }

    private TabItem CreateBrowserObservationSessionReportTab()
    {
        _generateObservationSessionReportButton = new Button
        {
            Content = "브라우저 관찰 보고서 생성",
            MinWidth = 210,
            Padding = new Thickness(12, 8, 12, 8)
        };
        _generateObservationSessionReportButton.Click +=
            OnGenerateObservationSessionReportClick;

        _openObservationSessionReportFolderButton = new Button
        {
            Content = "보고서 폴더 열기",
            MinWidth = 140,
            Padding = new Thickness(12, 8, 12, 8),
            IsEnabled = false
        };
        _openObservationSessionReportFolderButton.Click +=
            OnOpenObservationSessionReportFolderClick;

        _openObservationSessionReportHtmlButton = new Button
        {
            Content = "최신 HTML 열기",
            MinWidth = 130,
            Padding = new Thickness(12, 8, 12, 8),
            IsEnabled = false
        };
        _openObservationSessionReportHtmlButton.Click +=
            OnOpenObservationSessionReportHtmlClick;

        _observationSessionReportResultText = new TextBlock
        {
            FontFamily = new FontFamily("Consolas"),
            TextWrapping = TextWrapping.Wrap,
            Text = "아직 브라우저 관찰 전용 보고서를 생성하지 않았습니다."
        };

        StackPanel buttons = new()
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 16, 0, 0)
        };
        buttons.Children.Add(_generateObservationSessionReportButton);
        buttons.Children.Add(new Border { Width = 10 });
        buttons.Children.Add(_openObservationSessionReportFolderButton);
        buttons.Children.Add(new Border { Width = 10 });
        buttons.Children.Add(_openObservationSessionReportHtmlButton);

        StackPanel content = new();
        content.Children.Add(new TextBlock
        {
            FontSize = 22,
            FontWeight = FontWeights.SemiBold,
            Text = "브라우저 관찰 구조화 보고서"
        });
        content.Children.Add(new TextBlock
        {
            Margin = new Thickness(0, 8, 0, 0),
            Foreground = new SolidColorBrush(Color.FromRgb(86, 101, 115)),
            TextWrapping = TextWrapping.Wrap,
            Text = "가장 최근 브라우저 다운로드 관찰의 정상 완료·사용자 중지·Wi-Fi 변경·고정 NIC 사용 불가·카운터 공급자 불일치 원인을 JSON·CSV·단일 HTML에 구조화해 저장합니다."
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
                Text = "보고서 생성은 현재 메모리의 관찰 결과와 로컬 파일 시스템만 사용합니다. SSID·BSSID·인터페이스 ID·인터페이스 이름·IP·MAC·게이트웨이·URL은 포함하지 않습니다."
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
                Text = "관찰 결과는 앱을 종료하면 사라집니다. 필요한 산출물은 관찰이 끝난 뒤 앱을 종료하기 전에 생성하고, 회사 밖으로 공유하기 전에는 내용을 직접 다시 확인하십시오."
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
            Child = _observationSessionReportResultText
        });

        return new TabItem
        {
            Header = "관찰 보고서",
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

    private async void OnGenerateObservationSessionReportClick(
        object sender,
        RoutedEventArgs e)
    {
        if (_generateObservationSessionReportButton is null
            || !_generateObservationSessionReportButton.IsEnabled)
        {
            return;
        }

        if (_measurementRunning || _observationCancellation is not null)
        {
            SetObservationSessionReportResult(
                "측정 또는 브라우저 관찰이 진행 중입니다. 완료하거나 중지한 뒤 보고서를 생성하십시오.",
                Brushes.DarkOrange);
            return;
        }

        BrowserObservationResult? result = _lastBrowserObservation;
        if (result is null)
        {
            SetObservationSessionReportResult(
                "저장할 브라우저 관찰 결과가 없습니다. 먼저 브라우저 관찰을 실행하십시오.",
                Brushes.DarkOrange);
            return;
        }

        _generateObservationSessionReportButton.IsEnabled = false;
        SetObservationSessionReportResult(
            "최근 브라우저 관찰을 로컬 구조화 보고서로 정리하고 있습니다.",
            Brushes.DarkSlateGray);

        try
        {
            Version? version = Assembly.GetExecutingAssembly()
                .GetName()
                .Version;
            BrowserObservationSessionReportDocument document =
                BrowserObservationSessionReportWriter.CreateDocument(
                    result,
                    version?.ToString() ?? "개발 빌드");
            BrowserObservationSessionReportExportResult export =
                await Task.Run(() =>
                    BrowserObservationSessionReportWriter.WriteAll(
                        document,
                        GetDefaultReportDirectory()));

            _lastObservationSessionReportDirectory = export.OutputDirectory;
            _lastObservationSessionReportHtmlPath = export.HtmlPath;
            if (_openObservationSessionReportFolderButton is not null)
            {
                _openObservationSessionReportFolderButton.IsEnabled = true;
            }

            if (_openObservationSessionReportHtmlButton is not null)
            {
                _openObservationSessionReportHtmlButton.IsEnabled = true;
            }

            StringBuilder builder = new();
            builder.AppendLine("브라우저 관찰 보고서 생성 완료");
            builder.AppendLine($"상태: {document.Status}");
            builder.AppendLine($"종료 원인: {document.TerminationReason}");
            builder.AppendLine($"샘플: {document.Summary?.Samples.Count ?? 0}개");
            builder.AppendLine($"폴더: {export.OutputDirectory}");
            builder.AppendLine($"JSON: {Path.GetFileName(export.JsonPath)}");
            builder.AppendLine($"CSV: {Path.GetFileName(export.CsvPath)}");
            builder.AppendLine($"HTML: {Path.GetFileName(export.HtmlPath)}");
            builder.AppendLine($"무결성: {Path.GetFileName(export.Sha256Path)}");
            builder.AppendLine("외부 전송은 수행하지 않았습니다.");
            SetObservationSessionReportResult(
                builder.ToString().TrimEnd(),
                Brushes.DarkGreen);
        }
        catch (Exception exception)
        {
            SetObservationSessionReportResult(
                $"브라우저 관찰 보고서 생성 중 오류가 발생했습니다: {exception.Message}",
                Brushes.DarkRed);
        }
        finally
        {
            _generateObservationSessionReportButton.IsEnabled = true;
        }
    }

    private void OnOpenObservationSessionReportFolderClick(
        object sender,
        RoutedEventArgs e) =>
        OpenObservationSessionReportPath(
            _lastObservationSessionReportDirectory,
            "브라우저 관찰 보고서 폴더를 찾을 수 없습니다.");

    private void OnOpenObservationSessionReportHtmlClick(
        object sender,
        RoutedEventArgs e) =>
        OpenObservationSessionReportPath(
            _lastObservationSessionReportHtmlPath,
            "최신 브라우저 관찰 HTML 보고서를 찾을 수 없습니다.");

    private void OpenObservationSessionReportPath(
        string? path,
        string missingMessage)
    {
        if (string.IsNullOrWhiteSpace(path)
            || (!Directory.Exists(path) && !File.Exists(path)))
        {
            SetObservationSessionReportResult(
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
            SetObservationSessionReportResult(
                $"로컬 경로를 열지 못했습니다: {exception.Message}",
                Brushes.DarkRed);
        }
    }

    private void SetObservationSessionReportResult(
        string text,
        Brush brush)
    {
        if (_observationSessionReportResultText is null)
        {
            return;
        }

        _observationSessionReportResultText.Text = text;
        _observationSessionReportResultText.Foreground = brush;
    }
}
