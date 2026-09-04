using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WlanLivePathTester.Core.Models;
using WlanLivePathTester.Core.Reporting;
using WlanLivePathTester.Core.Wlan;
using WlanLivePathTester.Windows.Proxy;
using WlanLivePathTester.Windows.Wlan;

namespace WlanLivePathTester.App;

public partial class MainWindow
{
    private Button? _generateReportButton;
    private Button? _openReportFolderButton;
    private Button? _openLatestReportButton;
    private TextBlock? _reportResultText;
    private string? _lastReportDirectory;
    private string? _lastReportHtmlPath;
    private bool _reportTabAdded;

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);

        if (_reportTabAdded)
        {
            return;
        }

        TabControl? tabControl = FindVisualDescendant<TabControl>(this);
        if (tabControl is null)
        {
            return;
        }

        tabControl.Items.Add(CreateReportTab());
        _reportTabAdded = true;
    }

    private TabItem CreateReportTab()
    {
        _generateReportButton = new Button
        {
            Content = "로컬 보고서 생성",
            MinWidth = 160,
            Padding = new Thickness(12, 8, 12, 8)
        };
        _generateReportButton.Click += OnGenerateReportClick;

        _openReportFolderButton = new Button
        {
            Content = "보고서 폴더 열기",
            MinWidth = 140,
            Padding = new Thickness(12, 8, 12, 8),
            IsEnabled = false
        };
        _openReportFolderButton.Click += OnOpenReportFolderClick;

        _openLatestReportButton = new Button
        {
            Content = "최신 HTML 열기",
            MinWidth = 130,
            Padding = new Thickness(12, 8, 12, 8),
            IsEnabled = false
        };
        _openLatestReportButton.Click += OnOpenLatestReportClick;

        _reportResultText = new TextBlock
        {
            Margin = new Thickness(0, 16, 0, 0),
            FontFamily = new FontFamily("Consolas"),
            TextWrapping = TextWrapping.Wrap,
            Text = "아직 보고서를 생성하지 않았습니다."
        };

        StackPanel buttons = new()
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 16, 0, 0)
        };
        buttons.Children.Add(_generateReportButton);
        buttons.Children.Add(new Border { Width = 10 });
        buttons.Children.Add(_openReportFolderButton);
        buttons.Children.Add(new Border { Width = 10 });
        buttons.Children.Add(_openLatestReportButton);

        StackPanel content = new();
        content.Children.Add(new TextBlock
        {
            FontSize = 22,
            FontWeight = FontWeights.SemiBold,
            Text = "로컬 진단 보고서"
        });
        content.Children.Add(new TextBlock
        {
            Margin = new Thickness(0, 8, 0, 0),
            Foreground = new SolidColorBrush(Color.FromRgb(86, 101, 115)),
            TextWrapping = TextWrapping.Wrap,
            Text = "현재 WLAN·프록시 설정, 화면에 남아 있는 측정 결과와 브라우저 관찰 결과를 JSON·CSV·단일 HTML로 저장합니다. 보고서 생성은 네트워크 요청을 만들지 않습니다."
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
                Text = "기본 마스킹: SSID·BSSID·IP·MAC·이메일·URL 호스트·Windows 사용자 경로. HTML은 외부 JavaScript·폰트·이미지·iframe 없이 생성되며 각 파일의 SHA-256 목록을 함께 저장합니다."
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
                Text = "마스킹은 보조 수단입니다. 회사 밖으로 공유하기 전에는 HTML·CSV·JSON 내용을 직접 다시 확인하십시오. 보고서는 자동 업로드되지 않습니다."
            }
        });
        content.Children.Add(buttons);
        content.Children.Add(new Border
        {
            Margin = new Thickness(0, 20, 0, 0),
            Padding = new Thickness(16),
            CornerRadius = new CornerRadius(8),
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(216, 221, 227)),
            BorderThickness = new Thickness(1),
            Child = _reportResultText
        });

        return new TabItem
        {
            Header = "로컬 보고서",
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

    private async void OnGenerateReportClick(object sender, RoutedEventArgs e)
    {
        if (_generateReportButton is null || !_generateReportButton.IsEnabled)
        {
            return;
        }

        if (_measurementRunning || _observationCancellation is not null)
        {
            SetReportResult("측정 또는 브라우저 관찰이 진행 중입니다. 진행 중인 작업을 끝내거나 중지한 뒤 보고서를 생성하십시오.");
            return;
        }

        _generateReportButton.IsEnabled = false;
        SetReportResult("현재 로컬 결과를 정리하고 있습니다.");

        try
        {
            LocalDiagnosticReport report = BuildLocalReport();
            string outputDirectory = GetDefaultReportDirectory();
            LocalReportExportResult export = await Task.Run(
                () => LocalReportWriter.WriteAll(report, outputDirectory));

            _lastReportDirectory = export.OutputDirectory;
            _lastReportHtmlPath = export.HtmlPath;
            if (_openReportFolderButton is not null)
            {
                _openReportFolderButton.IsEnabled = true;
            }

            if (_openLatestReportButton is not null)
            {
                _openLatestReportButton.IsEnabled = true;
            }

            StringBuilder builder = new();
            builder.AppendLine("로컬 보고서 생성 완료");
            builder.AppendLine($"폴더: {export.OutputDirectory}");
            builder.AppendLine($"JSON: {Path.GetFileName(export.JsonPath)}");
            builder.AppendLine($"CSV: {Path.GetFileName(export.CsvPath)}");
            builder.AppendLine($"HTML: {Path.GetFileName(export.HtmlPath)}");
            builder.AppendLine($"무결성: {Path.GetFileName(export.Sha256Path)}");
            builder.AppendLine("외부 전송은 수행하지 않았습니다.");
            SetReportResult(builder.ToString().TrimEnd());
        }
        catch (Exception exception)
        {
            SetReportResult($"보고서 생성 중 오류가 발생했습니다: {exception.Message}");
        }
        finally
        {
            _generateReportButton.IsEnabled = true;
        }
    }

    private void OnOpenReportFolderClick(object sender, RoutedEventArgs e)
    {
        OpenLocalPath(_lastReportDirectory, "보고서 폴더를 찾을 수 없습니다.");
    }

    private void OnOpenLatestReportClick(object sender, RoutedEventArgs e)
    {
        OpenLocalPath(_lastReportHtmlPath, "최신 HTML 보고서를 찾을 수 없습니다.");
    }

    private LocalDiagnosticReport BuildLocalReport()
    {
        DateTimeOffset generatedAt = DateTimeOffset.UtcNow;
        WlanReadResult wlanRead = NativeWlanReader.ReadCurrent();
        WlanSnapshot? wlan = wlanRead.FirstConnectedInterface;
        CurrentUserProxySettings proxy = CurrentUserProxySettingsReader.Read();
        IReadOnlyList<ReportTextSection> measurements = CaptureMeasurementTexts(generatedAt);
        ReportObservationSection? observation = ReportObservationMapper.FromResult(
            _lastBrowserObservationResult);

        ReportWlanSection wlanSection = new(
            CapturedAt: wlan?.Timestamp ?? generatedAt,
            IsConnected: wlan?.IsConnected == true,
            InterfaceDescription: SensitiveDataRedactor.RedactText(
                wlan?.InterfaceDescription) ?? "확인 불가",
            InterfaceState: wlan?.InterfaceState ?? wlanRead.Status.ToString(),
            Ssid: SensitiveDataRedactor.MaskSsid(wlan?.Ssid),
            Bssid: SensitiveDataRedactor.MaskBssid(wlan?.Bssid),
            RssiDbm: wlan?.RssiDbm,
            SignalQualityPercent: wlan?.SignalQualityPercent,
            Channel: wlan?.Channel,
            CenterFrequencyMhz: wlan?.CenterFrequencyMhz,
            Band: WlanChannelCalculator.GetBandName(wlan?.CenterFrequencyMhz),
            PhyType: wlan?.PhyType ?? "확인 불가",
            ReceiveLinkMbps: ToMbps(wlan?.ReceiveLinkSpeedBps),
            TransmitLinkMbps: ToMbps(wlan?.TransmitLinkSpeedBps),
            Authentication: wlan?.Authentication ?? "확인 불가",
            Cipher: wlan?.Cipher ?? "확인 불가",
            ReadError: SensitiveDataRedactor.RedactText(
                wlan?.ReadError ?? (wlan is null ? wlanRead.Message : null)));

        ReportProxySection proxySection = new(
            ReadSucceeded: proxy.ReadSucceeded,
            Mode: proxy.Mode,
            AutoDetectEnabled: proxy.AutoDetectEnabled,
            PacConfigured: proxy.AutoConfigUrl is not null,
            ManualProxyConfigured: proxy.ManualProxy is not null,
            BypassConfigured: proxy.BypassList is not null,
            Win32Error: proxy.Win32Error,
            Statement: "프록시 주소, PAC URL과 바이패스 원문은 보고서에 포함하지 않았습니다.");

        IReadOnlyList<ReportFinding> findings = ReportFindingEngine.Evaluate(
            wlanSection,
            proxySection,
            measurements,
            observation);

        Version? assemblyVersion = Assembly.GetExecutingAssembly().GetName().Version;
        ReportMetadata metadata = new(
            GeneratedAt: generatedAt,
            ApplicationName: "WLAN Live Path Tester KO",
            ApplicationVersion: assemblyVersion?.ToString() ?? "개발 빌드",
            OperatingSystem: RuntimeInformation.OSDescription,
            RuntimeVersion: RuntimeInformation.FrameworkDescription,
            Culture: CultureInfo.CurrentCulture.Name,
            SensitiveValuesIncluded: false,
            DataHandlingStatement: "보고서는 현재 PC에서 생성되며 자동 업로드, 텔레메트리 또는 온라인 분석을 수행하지 않습니다.");

        return new LocalDiagnosticReport(
            SchemaVersion: "1.0",
            Metadata: metadata,
            Wlan: wlanSection,
            Proxy: proxySection,
            Measurements: measurements,
            BrowserObservation: observation,
            Findings: findings,
            Limitations: ReportFindingEngine.DefaultLimitations());
    }

    private IReadOnlyList<ReportTextSection> CaptureMeasurementTexts(
        DateTimeOffset capturedAt)
    {
        List<ReportTextSection> sections = [];
        HashSet<string> seen = new(StringComparer.Ordinal);

        foreach (TextBlock textBlock in EnumerateVisualDescendants<TextBlock>(this))
        {
            if (string.IsNullOrWhiteSpace(textBlock.Name)
                || textBlock.Name.Contains("Report", StringComparison.OrdinalIgnoreCase)
                || (!textBlock.Name.EndsWith("ResultText", StringComparison.OrdinalIgnoreCase)
                    && !textBlock.Name.EndsWith("ProgressText", StringComparison.OrdinalIgnoreCase)
                    && !textBlock.Name.EndsWith("StatusText", StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            string content = SensitiveDataRedactor.RedactText(textBlock.Text)?.Trim()
                ?? string.Empty;
            if (string.IsNullOrWhiteSpace(content)
                || content.Equals("아직 확인하지 않았습니다.", StringComparison.Ordinal)
                || content.Equals("아직 측정하지 않았습니다.", StringComparison.Ordinal)
                || !seen.Add(textBlock.Name + "\n" + content))
            {
                continue;
            }

            sections.Add(new ReportTextSection(
                SectionId: SensitiveDataRedactor.SafeFileComponent(
                    textBlock.Name,
                    "result"),
                Title: GetReportSectionTitle(textBlock.Name),
                Content: content,
                CapturedAt: capturedAt));
        }

        return sections;
    }

    private static IEnumerable<T> EnumerateVisualDescendants<T>(
        DependencyObject parent)
        where T : DependencyObject
    {
        int count = VisualTreeHelper.GetChildrenCount(parent);
        for (int index = 0; index < count; index++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match)
            {
                yield return match;
            }

            foreach (T nested in EnumerateVisualDescendants<T>(child))
            {
                yield return nested;
            }
        }
    }

    private static string GetReportSectionTitle(string controlName)
    {
        if (controlName.Contains("Wlan", StringComparison.OrdinalIgnoreCase))
        {
            return "WLAN 화면 결과";
        }

        if (controlName.Contains("ProxyRoute", StringComparison.OrdinalIgnoreCase))
        {
            return "대상 URL 프록시 경로";
        }

        if (controlName.Contains("Proxy", StringComparison.OrdinalIgnoreCase))
        {
            return "Windows 프록시 설정";
        }

        if (controlName.Contains("Internal", StringComparison.OrdinalIgnoreCase))
        {
            return "내부망 다운로드 측정";
        }

        if (controlName.Contains("External", StringComparison.OrdinalIgnoreCase))
        {
            return "외부망 다운로드 측정";
        }

        if (controlName.Contains("Progress", StringComparison.OrdinalIgnoreCase))
        {
            return "측정 진행 정보";
        }

        return controlName;
    }

    private static string GetDefaultReportDirectory()
    {
        string documents = Environment.GetFolderPath(
            Environment.SpecialFolder.MyDocuments);
        if (string.IsNullOrWhiteSpace(documents))
        {
            documents = AppContext.BaseDirectory;
        }

        return Path.Combine(
            documents,
            "WLAN Live Path Tester KO",
            "Reports");
    }

    private void OpenLocalPath(string? path, string missingMessage)
    {
        if (string.IsNullOrWhiteSpace(path)
            || (!Directory.Exists(path) && !File.Exists(path)))
        {
            SetReportResult(missingMessage);
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
            SetReportResult($"로컬 경로를 열지 못했습니다: {exception.Message}");
        }
    }

    private void SetReportResult(string text)
    {
        if (_reportResultText is not null)
        {
            _reportResultText.Text = text;
        }
    }

    private static double? ToMbps(ulong? bitsPerSecond) =>
        bitsPerSecond.HasValue ? bitsPerSecond.Value / 1_000_000d : null;
}
