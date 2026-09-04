using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WlanLivePathTester.Core.Adapters;
using WlanLivePathTester.Core.Reporting;
using WlanLivePathTester.Windows.Adapters;
using WlanLivePathTester.Windows.Wlan;

namespace WlanLivePathTester.App;

public partial class MainWindow
{
    private Button? _generateNetworkAdapterReportButton;
    private Button? _openNetworkAdapterReportFolderButton;
    private Button? _openNetworkAdapterReportHtmlButton;
    private TextBlock? _networkAdapterReportResultText;
    private string? _lastNetworkAdapterReportDirectory;
    private string? _lastNetworkAdapterReportHtmlPath;
    private bool _networkAdapterReportTabAdded;

    internal void EnsureNetworkAdapterReportTab()
    {
        if (_networkAdapterReportTabAdded)
        {
            return;
        }

        TabControl? tabControl = FindVisualDescendant<TabControl>(this);
        if (tabControl is null)
        {
            return;
        }

        tabControl.Items.Add(CreateNetworkAdapterReportTab());
        _networkAdapterReportTabAdded = true;
    }

    private TabItem CreateNetworkAdapterReportTab()
    {
        _generateNetworkAdapterReportButton = new Button
        {
            Content = "어댑터 진단 보고서 생성",
            MinWidth = 200,
            Padding = new Thickness(12, 8, 12, 8)
        };
        _generateNetworkAdapterReportButton.Click +=
            OnGenerateNetworkAdapterReportClick;

        _openNetworkAdapterReportFolderButton = new Button
        {
            Content = "보고서 폴더 열기",
            MinWidth = 140,
            Padding = new Thickness(12, 8, 12, 8),
            IsEnabled = false
        };
        _openNetworkAdapterReportFolderButton.Click +=
            OnOpenNetworkAdapterReportFolderClick;

        _openNetworkAdapterReportHtmlButton = new Button
        {
            Content = "최신 HTML 열기",
            MinWidth = 130,
            Padding = new Thickness(12, 8, 12, 8),
            IsEnabled = false
        };
        _openNetworkAdapterReportHtmlButton.Click +=
            OnOpenNetworkAdapterReportHtmlClick;

        _networkAdapterReportResultText = new TextBlock
        {
            FontFamily = new FontFamily("Consolas"),
            TextWrapping = TextWrapping.Wrap,
            Text = "아직 어댑터 진단 보고서를 생성하지 않았습니다."
        };

        StackPanel buttons = new()
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 16, 0, 0)
        };
        buttons.Children.Add(_generateNetworkAdapterReportButton);
        buttons.Children.Add(new Border { Width = 10 });
        buttons.Children.Add(_openNetworkAdapterReportFolderButton);
        buttons.Children.Add(new Border { Width = 10 });
        buttons.Children.Add(_openNetworkAdapterReportHtmlButton);

        StackPanel content = new();
        content.Children.Add(new TextBlock
        {
            FontSize = 22,
            FontWeight = FontWeights.SemiBold,
            Text = "어댑터 진단 구조화 보고서"
        });
        content.Children.Add(new TextBlock
        {
            Margin = new Thickness(0, 8, 0, 0),
            Foreground = new SolidColorBrush(Color.FromRgb(86, 101, 115)),
            TextWrapping = TextWrapping.Wrap,
            Text = "현재 물리 Wi-Fi 선택 결과, 다중 NIC·VPN·가상 어댑터 분류와 경고를 JSON·CSV·단일 HTML로 저장합니다. 보고서 생성은 로컬 인터페이스 정보만 읽으며 네트워크 요청을 만들지 않습니다."
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
                Text = "IP·MAC·게이트웨이 주소와 전체 인터페이스 GUID는 저장하지 않습니다. 인터페이스 ID는 SHA-256 앞 10자리 지문으로만 기록하고, HTML은 외부 JavaScript·CSS·웹폰트·이미지·iframe을 포함하지 않습니다."
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
                Text = "분류 규칙은 모든 기업 VPN·보안 에이전트·드라이버를 완전히 식별하지 못할 수 있습니다. 회사 밖으로 공유하기 전에는 파일 내용을 직접 다시 확인하십시오."
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
            Child = _networkAdapterReportResultText
        });

        return new TabItem
        {
            Header = "어댑터 보고서",
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

    private async void OnGenerateNetworkAdapterReportClick(
        object sender,
        RoutedEventArgs e)
    {
        if (_generateNetworkAdapterReportButton is null
            || !_generateNetworkAdapterReportButton.IsEnabled)
        {
            return;
        }

        if (_measurementRunning || _observationCancellation is not null)
        {
            SetNetworkAdapterReportResult(
                "측정 또는 브라우저 관찰이 진행 중입니다. 완료하거나 중지한 뒤 보고서를 생성하십시오.",
                Brushes.DarkOrange);
            return;
        }

        _generateNetworkAdapterReportButton.IsEnabled = false;
        SetNetworkAdapterReportResult(
            "현재 로컬 어댑터 선택과 분류 결과를 정리하고 있습니다.",
            Brushes.DarkSlateGray);

        try
        {
            string? connectedInterfaceId = NativeWlanReader
                .ReadCurrent()
                .FirstConnectedInterface?
                .InterfaceId;
            NetworkAdapterInventoryReadResult inventory =
                NetworkAdapterInventoryReader.Read(connectedInterfaceId);
            WirelessAdapterSelectionResult selection =
                NetworkAdapterSelector.Select(inventory.Adapters);

            if (inventory.Warnings.Count > 0)
            {
                selection = selection with
                {
                    Warnings = inventory.Warnings
                        .Concat(selection.Warnings)
                        .Distinct(StringComparer.Ordinal)
                        .ToArray()
                };
            }

            Version? version = Assembly.GetExecutingAssembly()
                .GetName()
                .Version;
            NetworkAdapterDiagnosticsReportDocument document =
                NetworkAdapterDiagnosticsReportWriter.CreateDocument(
                    selection,
                    version?.ToString() ?? "개발 빌드");
            NetworkAdapterDiagnosticsReportExportResult export =
                await Task.Run(() =>
                    NetworkAdapterDiagnosticsReportWriter.WriteAll(
                        document,
                        GetDefaultReportDirectory()));

            _lastNetworkAdapterReportDirectory = export.OutputDirectory;
            _lastNetworkAdapterReportHtmlPath = export.HtmlPath;
            if (_openNetworkAdapterReportFolderButton is not null)
            {
                _openNetworkAdapterReportFolderButton.IsEnabled = true;
            }

            if (_openNetworkAdapterReportHtmlButton is not null)
            {
                _openNetworkAdapterReportHtmlButton.IsEnabled = true;
            }

            StringBuilder builder = new();
            builder.AppendLine("어댑터 진단 보고서 생성 완료");
            builder.AppendLine($"선택 상태: {document.SelectionStatus}");
            builder.AppendLine($"어댑터: {document.Adapters.Count}개");
            builder.AppendLine($"경고: {document.Warnings.Count}개");
            builder.AppendLine($"폴더: {export.OutputDirectory}");
            builder.AppendLine($"JSON: {Path.GetFileName(export.JsonPath)}");
            builder.AppendLine($"CSV: {Path.GetFileName(export.CsvPath)}");
            builder.AppendLine($"HTML: {Path.GetFileName(export.HtmlPath)}");
            builder.AppendLine($"무결성: {Path.GetFileName(export.Sha256Path)}");
            builder.AppendLine("외부 전송은 수행하지 않았습니다.");
            SetNetworkAdapterReportResult(
                builder.ToString().TrimEnd(),
                Brushes.DarkGreen);
        }
        catch (Exception exception)
        {
            SetNetworkAdapterReportResult(
                $"어댑터 진단 보고서 생성 중 오류가 발생했습니다: {exception.Message}",
                Brushes.DarkRed);
        }
        finally
        {
            _generateNetworkAdapterReportButton.IsEnabled = true;
        }
    }

    private void OnOpenNetworkAdapterReportFolderClick(
        object sender,
        RoutedEventArgs e) =>
        OpenNetworkAdapterReportPath(
            _lastNetworkAdapterReportDirectory,
            "어댑터 진단 보고서 폴더를 찾을 수 없습니다.");

    private void OnOpenNetworkAdapterReportHtmlClick(
        object sender,
        RoutedEventArgs e) =>
        OpenNetworkAdapterReportPath(
            _lastNetworkAdapterReportHtmlPath,
            "최신 어댑터 진단 HTML 보고서를 찾을 수 없습니다.");

    private void OpenNetworkAdapterReportPath(
        string? path,
        string missingMessage)
    {
        if (string.IsNullOrWhiteSpace(path)
            || (!Directory.Exists(path) && !File.Exists(path)))
        {
            SetNetworkAdapterReportResult(
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
            SetNetworkAdapterReportResult(
                $"로컬 경로를 열지 못했습니다: {exception.Message}",
                Brushes.DarkRed);
        }
    }

    private void SetNetworkAdapterReportResult(
        string text,
        Brush brush)
    {
        if (_networkAdapterReportResultText is null)
        {
            return;
        }

        _networkAdapterReportResultText.Text = text;
        _networkAdapterReportResultText.Foreground = brush;
    }
}
