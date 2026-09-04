using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WlanLivePathTester.Core.NetworkEnvironment;
using WlanLivePathTester.Core.Reporting;
using WlanLivePathTester.Windows.NetworkEnvironment;

namespace WlanLivePathTester.App;

public partial class MainWindow
{
    private Button? _generateNetworkEnvironmentReportButton;
    private Button? _openNetworkEnvironmentReportFolderButton;
    private Button? _openNetworkEnvironmentReportHtmlButton;
    private TextBlock? _networkEnvironmentReportResultText;
    private string? _lastNetworkEnvironmentReportDirectory;
    private string? _lastNetworkEnvironmentReportHtmlPath;
    private bool _networkEnvironmentReportTabAdded;

    internal void EnsureNetworkEnvironmentReportTab()
    {
        if (_networkEnvironmentReportTabAdded)
        {
            return;
        }

        TabControl? tabControl = FindVisualDescendant<TabControl>(this);
        if (tabControl is null)
        {
            return;
        }

        tabControl.Items.Add(CreateNetworkEnvironmentReportTab());
        _networkEnvironmentReportTabAdded = true;
    }

    private TabItem CreateNetworkEnvironmentReportTab()
    {
        _generateNetworkEnvironmentReportButton = new Button
        {
            Content = "인터페이스 환경 보고서 생성",
            MinWidth = 210,
            Padding = new Thickness(12, 8, 12, 8)
        };
        _generateNetworkEnvironmentReportButton.Click +=
            OnGenerateNetworkEnvironmentReportClick;

        _openNetworkEnvironmentReportFolderButton = new Button
        {
            Content = "보고서 폴더 열기",
            MinWidth = 140,
            Padding = new Thickness(12, 8, 12, 8),
            IsEnabled = false
        };
        _openNetworkEnvironmentReportFolderButton.Click +=
            OnOpenNetworkEnvironmentReportFolderClick;

        _openNetworkEnvironmentReportHtmlButton = new Button
        {
            Content = "최신 HTML 열기",
            MinWidth = 130,
            Padding = new Thickness(12, 8, 12, 8),
            IsEnabled = false
        };
        _openNetworkEnvironmentReportHtmlButton.Click +=
            OnOpenNetworkEnvironmentReportHtmlClick;

        _networkEnvironmentReportResultText = new TextBlock
        {
            FontFamily = new FontFamily("Consolas"),
            TextWrapping = TextWrapping.Wrap,
            Text = "아직 인터페이스 환경 보고서를 생성하지 않았습니다."
        };

        StackPanel buttons = new()
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 16, 0, 0)
        };
        buttons.Children.Add(_generateNetworkEnvironmentReportButton);
        buttons.Children.Add(new Border { Width = 10 });
        buttons.Children.Add(_openNetworkEnvironmentReportFolderButton);
        buttons.Children.Add(new Border { Width = 10 });
        buttons.Children.Add(_openNetworkEnvironmentReportHtmlButton);

        StackPanel content = new();
        content.Children.Add(new TextBlock
        {
            FontSize = 22,
            FontWeight = FontWeights.SemiBold,
            Text = "인터페이스 환경 구조화 보고서"
        });
        content.Children.Add(new TextBlock
        {
            Margin = new Thickness(0, 8, 0, 0),
            Foreground = new SolidColorBrush(Color.FromRgb(86, 101, 115)),
            TextWrapping = TextWrapping.Wrap,
            Text = "현재 로컬 인터페이스 환경을 다시 수집해 JSON·CSV·단일 HTML과 SHA-256으로 저장합니다. 보고서 생성 과정은 네트워크 요청을 만들지 않습니다."
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
                Text = "인터페이스 이름·설명·GUID·IP·게이트웨이·DNS·MAC 주소 원문은 보고서에 넣지 않습니다. 어댑터는 번호, 범주, 상태, 링크 속도, 주소 계열·개수, 게이트웨이 유무, VPN·가상 여부로만 기록합니다."
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
                Text = "익명화된 보고서도 회사 밖으로 공유하기 전에는 내용을 직접 다시 확인하십시오. 실제 목적지 경로는 이 보고서만으로 확정할 수 없습니다."
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
            Child = _networkEnvironmentReportResultText
        });

        return new TabItem
        {
            Header = "인터페이스 보고서",
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

    private async void OnGenerateNetworkEnvironmentReportClick(
        object sender,
        RoutedEventArgs e)
    {
        if (_generateNetworkEnvironmentReportButton is null
            || !_generateNetworkEnvironmentReportButton.IsEnabled)
        {
            return;
        }

        _generateNetworkEnvironmentReportButton.IsEnabled = false;
        SetNetworkEnvironmentReportResult(
            "로컬 인터페이스 환경을 수집하고 보고서를 생성하고 있습니다.",
            Brushes.DarkSlateGray);

        try
        {
            LocalNetworkEnvironmentSnapshot snapshot = await Task.Run(
                LocalNetworkEnvironmentReader.ReadCurrent);
            Version? version = Assembly.GetExecutingAssembly()
                .GetName()
                .Version;
            NetworkEnvironmentReportDocument document =
                NetworkEnvironmentReportWriter.CreateDocument(
                    snapshot,
                    version?.ToString() ?? "개발 빌드");
            NetworkEnvironmentReportExportResult export =
                await Task.Run(() =>
                    NetworkEnvironmentReportWriter.WriteAll(
                        document,
                        GetDefaultReportDirectory()));

            _lastNetworkEnvironmentReportDirectory = export.OutputDirectory;
            _lastNetworkEnvironmentReportHtmlPath = export.HtmlPath;
            if (_openNetworkEnvironmentReportFolderButton is not null)
            {
                _openNetworkEnvironmentReportFolderButton.IsEnabled = true;
            }

            if (_openNetworkEnvironmentReportHtmlButton is not null)
            {
                _openNetworkEnvironmentReportHtmlButton.IsEnabled = true;
            }

            StringBuilder builder = new();
            builder.AppendLine("인터페이스 환경 보고서 생성 완료");
            builder.AppendLine($"익명화된 인터페이스: {document.Adapters.Count}개");
            builder.AppendLine($"판정: {document.Findings.Count}개");
            builder.AppendLine($"폴더: {export.OutputDirectory}");
            builder.AppendLine($"JSON: {Path.GetFileName(export.JsonPath)}");
            builder.AppendLine($"CSV: {Path.GetFileName(export.CsvPath)}");
            builder.AppendLine($"HTML: {Path.GetFileName(export.HtmlPath)}");
            builder.AppendLine($"무결성: {Path.GetFileName(export.Sha256Path)}");
            builder.AppendLine("외부 전송은 수행하지 않았습니다.");
            SetNetworkEnvironmentReportResult(
                builder.ToString().TrimEnd(),
                Brushes.DarkGreen);
        }
        catch (Exception exception)
        {
            SetNetworkEnvironmentReportResult(
                $"인터페이스 환경 보고서 생성 중 오류가 발생했습니다: {exception.Message}",
                Brushes.DarkRed);
        }
        finally
        {
            _generateNetworkEnvironmentReportButton.IsEnabled = true;
        }
    }

    private void OnOpenNetworkEnvironmentReportFolderClick(
        object sender,
        RoutedEventArgs e) =>
        OpenNetworkEnvironmentReportPath(
            _lastNetworkEnvironmentReportDirectory,
            "인터페이스 환경 보고서 폴더를 찾을 수 없습니다.");

    private void OnOpenNetworkEnvironmentReportHtmlClick(
        object sender,
        RoutedEventArgs e) =>
        OpenNetworkEnvironmentReportPath(
            _lastNetworkEnvironmentReportHtmlPath,
            "최신 인터페이스 환경 HTML 보고서를 찾을 수 없습니다.");

    private void OpenNetworkEnvironmentReportPath(
        string? path,
        string missingMessage)
    {
        if (string.IsNullOrWhiteSpace(path)
            || (!Directory.Exists(path) && !File.Exists(path)))
        {
            SetNetworkEnvironmentReportResult(
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
            SetNetworkEnvironmentReportResult(
                $"로컬 경로를 열지 못했습니다: {exception.Message}",
                Brushes.DarkRed);
        }
    }

    private void SetNetworkEnvironmentReportResult(
        string text,
        Brush brush)
    {
        if (_networkEnvironmentReportResultText is null)
        {
            return;
        }

        _networkEnvironmentReportResultText.Text = text;
        _networkEnvironmentReportResultText.Foreground = brush;
    }
}
