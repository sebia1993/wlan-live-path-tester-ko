using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WlanLivePathTester.Core.Models;
using WlanLivePathTester.Core.Proxy;
using WlanLivePathTester.Core.Reporting;
using WlanLivePathTester.Core.Routing;
using WlanLivePathTester.Windows.Routing;
using WlanLivePathTester.Windows.Wlan;

namespace WlanLivePathTester.App;

public partial class MainWindow
{
    private TextBox? _internalRouteTargetTextBox;
    private TextBox? _externalRouteTargetTextBox;
    private TextBox? _proxyRouteTextBox;
    private Button? _runInternalProxyRouteComparisonButton;
    private Button? _stopInternalProxyRouteComparisonButton;
    private Button? _generateInternalProxyRouteReportButton;
    private Button? _openInternalProxyRouteReportFolderButton;
    private Button? _openInternalProxyRouteReportHtmlButton;
    private TextBlock? _internalProxyRouteResultText;
    private CancellationTokenSource? _internalProxyRouteCancellation;
    private InternalProxyRouteComparisonResult?
        _lastInternalProxyRouteComparison;
    private string? _lastInternalProxyRouteReportDirectory;
    private string? _lastInternalProxyRouteReportHtmlPath;
    private bool _internalProxyRouteComparisonTabAdded;
    private bool _internalProxyRouteClosedHooked;

    internal void EnsureInternalProxyRouteComparisonTab()
    {
        if (_internalProxyRouteComparisonTabAdded)
        {
            return;
        }

        TabControl? tabControl = FindVisualDescendant<TabControl>(this);
        if (tabControl is null)
        {
            return;
        }

        tabControl.Items.Add(CreateInternalProxyRouteComparisonTab());
        _internalProxyRouteComparisonTabAdded = true;
        if (!_internalProxyRouteClosedHooked)
        {
            Closed += OnInternalProxyRouteWindowClosed;
            _internalProxyRouteClosedHooked = true;
        }
    }

    private TabItem CreateInternalProxyRouteComparisonTab()
    {
        _internalRouteTargetTextBox = new TextBox
        {
            MinWidth = 520,
            Text = string.Empty,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        _externalRouteTargetTextBox = new TextBox
        {
            MinWidth = 520,
            Text = string.Empty,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        _proxyRouteTextBox = new TextBox
        {
            MinWidth = 520,
            MinHeight = 82,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Text = string.Empty
        };

        _runInternalProxyRouteComparisonButton = new Button
        {
            Content = "로컬 경로 비교 실행",
            MinWidth = 170,
            Padding = new Thickness(12, 8, 12, 8)
        };
        _runInternalProxyRouteComparisonButton.Click +=
            OnRunInternalProxyRouteComparisonClick;

        _stopInternalProxyRouteComparisonButton = new Button
        {
            Content = "경로 확인 중지",
            MinWidth = 130,
            Padding = new Thickness(12, 8, 12, 8),
            IsEnabled = false
        };
        _stopInternalProxyRouteComparisonButton.Click +=
            OnStopInternalProxyRouteComparisonClick;

        _generateInternalProxyRouteReportButton = new Button
        {
            Content = "비교 보고서 생성",
            MinWidth = 150,
            Padding = new Thickness(12, 8, 12, 8),
            IsEnabled = false
        };
        _generateInternalProxyRouteReportButton.Click +=
            OnGenerateInternalProxyRouteReportClick;

        _openInternalProxyRouteReportFolderButton = new Button
        {
            Content = "보고서 폴더 열기",
            MinWidth = 140,
            Padding = new Thickness(12, 8, 12, 8),
            IsEnabled = false
        };
        _openInternalProxyRouteReportFolderButton.Click +=
            OnOpenInternalProxyRouteReportFolderClick;

        _openInternalProxyRouteReportHtmlButton = new Button
        {
            Content = "최신 HTML 열기",
            MinWidth = 130,
            Padding = new Thickness(12, 8, 12, 8),
            IsEnabled = false
        };
        _openInternalProxyRouteReportHtmlButton.Click +=
            OnOpenInternalProxyRouteReportHtmlClick;

        _internalProxyRouteResultText = new TextBlock
        {
            FontFamily = new FontFamily("Consolas"),
            TextWrapping = TextWrapping.Wrap,
            Text = "아직 로컬 경로를 비교하지 않았습니다."
        };

        StackPanel content = new();
        content.Children.Add(new TextBlock
        {
            FontSize = 22,
            FontWeight = FontWeights.SemiBold,
            Text = "내부 DIRECT–프록시 로컬 경로 비교"
        });
        content.Children.Add(new TextBlock
        {
            Margin = new Thickness(0, 8, 0, 0),
            Foreground = new SolidColorBrush(
                Color.FromRgb(86, 101, 115)),
            TextWrapping = TextWrapping.Wrap,
            Text = "실행 버튼을 누른 경우에만 내부 대상과 적용 프록시 후보의 운영체제 DNS·Windows 최적 인터페이스를 확인합니다. 프록시에 연결하거나 인증·다운로드하지 않습니다."
        });
        content.Children.Add(CreateRouteInputBlock(
            "내부 승인 DIRECT 대상",
            "내부 파일 서버의 HTTP(S) URL, DNS 호스트 또는 IP를 입력합니다. 이 화면은 PAC의 DIRECT 여부를 자동 보증하지 않으므로 승인된 내부 대상을 사용하십시오.",
            _internalRouteTargetTextBox));
        content.Children.Add(CreateRouteInputBlock(
            "외부 측정 대상 URL",
            "프록시 스킴 매핑을 선택하기 위한 절대 HTTP 또는 HTTPS URL입니다. 이 단계에서 외부 사이트로 HEAD·GET을 보내지 않습니다.",
            _externalRouteTargetTextBox));
        content.Children.Add(CreateRouteInputBlock(
            "Windows 프록시 결과 또는 수동 서버 목록",
            "예: PROXY proxy.example:8080; DIRECT 또는 http=proxy-a:8080;https=proxy-b:8443. 입력 원문은 화면에만 있으며 결과·보고서에는 호스트 지문만 남습니다.",
            _proxyRouteTextBox));

        content.Children.Add(new Border
        {
            Margin = new Thickness(0, 16, 0, 0),
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
                Text = "DIRECT가 프록시보다 먼저면 프록시 DNS 조회를 수행하지 않습니다. 프록시 뒤 DIRECT fallback이 있어도 실제 프록시 연결 실패나 DIRECT 전환은 시험하지 않습니다. 회사 밖으로 스크린샷을 공유할 때는 입력란의 프록시 원문을 가리십시오."
            }
        });

        StackPanel firstButtons = new()
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 16, 0, 0)
        };
        firstButtons.Children.Add(
            _runInternalProxyRouteComparisonButton);
        firstButtons.Children.Add(new Border { Width = 10 });
        firstButtons.Children.Add(
            _stopInternalProxyRouteComparisonButton);
        firstButtons.Children.Add(new Border { Width = 10 });
        firstButtons.Children.Add(
            _generateInternalProxyRouteReportButton);
        content.Children.Add(firstButtons);

        StackPanel reportButtons = new()
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 10, 0, 0)
        };
        reportButtons.Children.Add(
            _openInternalProxyRouteReportFolderButton);
        reportButtons.Children.Add(new Border { Width = 10 });
        reportButtons.Children.Add(
            _openInternalProxyRouteReportHtmlButton);
        content.Children.Add(reportButtons);

        content.Children.Add(new Border
        {
            Margin = new Thickness(0, 18, 0, 0),
            Padding = new Thickness(16),
            CornerRadius = new CornerRadius(8),
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(
                Color.FromRgb(216, 221, 227)),
            BorderThickness = new Thickness(1),
            Child = _internalProxyRouteResultText
        });

        return new TabItem
        {
            Header = "로컬 경로 비교",
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

    private static Border CreateRouteInputBlock(
        string title,
        string description,
        Control input)
    {
        StackPanel panel = new();
        panel.Children.Add(new TextBlock
        {
            FontWeight = FontWeights.SemiBold,
            Text = title
        });
        panel.Children.Add(new TextBlock
        {
            Margin = new Thickness(0, 4, 0, 8),
            Foreground = new SolidColorBrush(
                Color.FromRgb(86, 101, 115)),
            TextWrapping = TextWrapping.Wrap,
            Text = description
        });
        panel.Children.Add(input);

        return new Border
        {
            Margin = new Thickness(0, 16, 0, 0),
            Padding = new Thickness(14),
            CornerRadius = new CornerRadius(8),
            BorderBrush = new SolidColorBrush(
                Color.FromRgb(216, 221, 227)),
            BorderThickness = new Thickness(1),
            Background = Brushes.White,
            Child = panel
        };
    }

    private async void OnRunInternalProxyRouteComparisonClick(
        object sender,
        RoutedEventArgs e)
    {
        if (_internalProxyRouteCancellation is not null)
        {
            return;
        }

        if (_measurementRunning || _observationCancellation is not null)
        {
            SetInternalProxyRouteResult(
                "다운로드 측정 또는 브라우저 관찰이 진행 중입니다. 완료하거나 중지한 뒤 로컬 경로 비교를 실행하십시오.",
                Brushes.DarkOrange);
            return;
        }

        string internalTarget =
            _internalRouteTargetTextBox?.Text.Trim()
            ?? string.Empty;
        string externalTarget =
            _externalRouteTargetTextBox?.Text.Trim()
            ?? string.Empty;
        string proxyText =
            _proxyRouteTextBox?.Text.Trim()
            ?? string.Empty;

        if (string.IsNullOrWhiteSpace(internalTarget))
        {
            SetInternalProxyRouteResult(
                "내부 승인 DIRECT 대상 URL, 호스트 또는 IP를 입력하십시오.",
                Brushes.DarkOrange);
            return;
        }

        if (!TryCreateExternalHttpUri(
                externalTarget,
                out Uri? externalUri))
        {
            SetInternalProxyRouteResult(
                "외부 측정 대상은 사용자 정보와 fragment가 없는 절대 HTTP 또는 HTTPS URL이어야 합니다.",
                Brushes.DarkOrange);
            return;
        }

        if (string.IsNullOrWhiteSpace(proxyText))
        {
            SetInternalProxyRouteResult(
                "Windows 프록시 결과 또는 수동 프록시 서버 목록을 입력하십시오. DIRECT 단독 결과도 입력할 수 있습니다.",
                Brushes.DarkOrange);
            return;
        }

        ProxyEndpointParseResult parsed = ProxyEndpointParser.Parse(
            proxyText,
            externalUri);
        if (parsed.Errors.Count > 0)
        {
            SetInternalProxyRouteResult(
                "프록시 문자열을 안전하게 해석하지 못했습니다. 지원 형식과 포트·URI 규칙을 확인하십시오.",
                Brushes.DarkOrange);
            return;
        }

        CancellationTokenSource activeCancellation = new();
        _internalProxyRouteCancellation = activeCancellation;
        SetInternalProxyRouteRunningState(isRunning: true);
        SetInternalProxyRouteResult(
            "내부 대상과 적용 프록시 후보의 Windows 로컬 경로를 확인하고 있습니다. 프록시 연결·인증·다운로드는 수행하지 않습니다.",
            Brushes.DarkSlateGray);

        try
        {
            string? expectedWlanInterfaceId =
                ReadCurrentWlanInterfaceId();
            DestinationRouteEvidence internalRoute =
                await LocalRouteEvidenceReader.ReadAsync(
                    internalTarget,
                    "내부 DIRECT 대상",
                    RouteProbePurpose.InternalDirectTarget,
                    dnsTimeoutSeconds: 5,
                    activeCancellation.Token);
            internalRoute = RouteWlanCorrelationEvaluator.Apply(
                internalRoute,
                expectedWlanInterfaceId);

            ProxyEndpointRouteAnalysisResult proxyRoute =
                await new ProxyEndpointRouteAnalyzer().AnalyzeAsync(
                    parsed,
                    expectedWlanInterfaceId,
                    dnsTimeoutSeconds: 5,
                    activeCancellation.Token);

            InternalProxyRouteComparisonResult comparison =
                InternalProxyRouteComparison.Compare(
                    internalRoute,
                    proxyRoute,
                    expectedWlanInterfaceId);
            _lastInternalProxyRouteComparison = comparison;
            if (_generateInternalProxyRouteReportButton is not null)
            {
                _generateInternalProxyRouteReportButton.IsEnabled = true;
            }

            InternalProxyRouteComparisonReportDocument display =
                InternalProxyRouteComparisonReportWriter.CreateDocument(
                    comparison,
                    GetApplicationVersion());
            SetInternalProxyRouteResult(
                FormatInternalProxyRouteResult(display),
                GetInternalProxyRouteResultBrush(comparison.Status));
        }
        catch (OperationCanceledException)
        {
            SetInternalProxyRouteResult(
                "사용자 요청으로 로컬 경로 비교를 중단했습니다. 완료되지 않은 결과는 저장하지 않았습니다.",
                Brushes.DarkOrange);
        }
        catch (Exception exception)
        {
            SetInternalProxyRouteResult(
                $"로컬 경로 비교 중 오류가 발생했습니다: {exception.GetType().Name}. 입력값 원문은 결과에 표시하지 않았습니다.",
                Brushes.DarkRed);
        }
        finally
        {
            if (ReferenceEquals(
                    _internalProxyRouteCancellation,
                    activeCancellation))
            {
                _internalProxyRouteCancellation = null;
            }

            activeCancellation.Dispose();
            SetInternalProxyRouteRunningState(isRunning: false);
        }
    }

    private void OnStopInternalProxyRouteComparisonClick(
        object sender,
        RoutedEventArgs e)
    {
        CancellationTokenSource? active =
            _internalProxyRouteCancellation;
        if (active is null)
        {
            return;
        }

        active.Cancel();
        SetInternalProxyRouteResult(
            "로컬 경로 확인 중지 요청을 처리하고 있습니다.",
            Brushes.DarkOrange);
    }

    private async void OnGenerateInternalProxyRouteReportClick(
        object sender,
        RoutedEventArgs e)
    {
        if (_internalProxyRouteCancellation is not null
            || _lastInternalProxyRouteComparison is null)
        {
            SetInternalProxyRouteResult(
                "저장할 완료된 로컬 경로 비교 결과가 없습니다.",
                Brushes.DarkOrange);
            return;
        }

        if (_generateInternalProxyRouteReportButton is not null)
        {
            _generateInternalProxyRouteReportButton.IsEnabled = false;
        }

        try
        {
            InternalProxyRouteComparisonReportDocument document =
                InternalProxyRouteComparisonReportWriter.CreateDocument(
                    _lastInternalProxyRouteComparison,
                    GetApplicationVersion());
            InternalProxyRouteComparisonReportExportResult export =
                await Task.Run(() =>
                    InternalProxyRouteComparisonReportWriter.WriteAll(
                        document,
                        GetDefaultReportDirectory()));

            _lastInternalProxyRouteReportDirectory =
                export.OutputDirectory;
            _lastInternalProxyRouteReportHtmlPath = export.HtmlPath;
            if (_openInternalProxyRouteReportFolderButton is not null)
            {
                _openInternalProxyRouteReportFolderButton.IsEnabled = true;
            }

            if (_openInternalProxyRouteReportHtmlButton is not null)
            {
                _openInternalProxyRouteReportHtmlButton.IsEnabled = true;
            }

            StringBuilder builder = new();
            builder.AppendLine("로컬 경로 비교 보고서 생성 완료");
            builder.AppendLine($"상태: {document.Status}");
            builder.AppendLine(
                $"JSON: {Path.GetFileName(export.JsonPath)}");
            builder.AppendLine(
                $"CSV: {Path.GetFileName(export.CsvPath)}");
            builder.AppendLine(
                $"HTML: {Path.GetFileName(export.HtmlPath)}");
            builder.AppendLine(
                $"무결성: {Path.GetFileName(export.Sha256Path)}");
            builder.AppendLine($"폴더: {export.OutputDirectory}");
            builder.AppendLine("외부 전송은 수행하지 않았습니다.");
            SetInternalProxyRouteResult(
                builder.ToString().TrimEnd(),
                Brushes.DarkGreen);
        }
        catch (Exception exception)
        {
            SetInternalProxyRouteResult(
                $"로컬 경로 비교 보고서 생성 중 오류가 발생했습니다: {exception.GetType().Name}",
                Brushes.DarkRed);
        }
        finally
        {
            if (_generateInternalProxyRouteReportButton is not null)
            {
                _generateInternalProxyRouteReportButton.IsEnabled =
                    _lastInternalProxyRouteComparison is not null;
            }
        }
    }

    private void OnOpenInternalProxyRouteReportFolderClick(
        object sender,
        RoutedEventArgs e) =>
        OpenInternalProxyRouteReportPath(
            _lastInternalProxyRouteReportDirectory,
            "로컬 경로 비교 보고서 폴더를 찾을 수 없습니다.");

    private void OnOpenInternalProxyRouteReportHtmlClick(
        object sender,
        RoutedEventArgs e) =>
        OpenInternalProxyRouteReportPath(
            _lastInternalProxyRouteReportHtmlPath,
            "최신 로컬 경로 비교 HTML을 찾을 수 없습니다.");

    private void OpenInternalProxyRouteReportPath(
        string? path,
        string missingMessage)
    {
        if (string.IsNullOrWhiteSpace(path)
            || (!Directory.Exists(path) && !File.Exists(path)))
        {
            SetInternalProxyRouteResult(
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
            SetInternalProxyRouteResult(
                $"로컬 보고서 경로를 열지 못했습니다: {exception.GetType().Name}",
                Brushes.DarkRed);
        }
    }

    private static bool TryCreateExternalHttpUri(
        string value,
        out Uri? uri)
    {
        uri = null;
        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? candidate)
            || (!candidate.Scheme.Equals(
                    Uri.UriSchemeHttp,
                    StringComparison.OrdinalIgnoreCase)
                && !candidate.Scheme.Equals(
                    Uri.UriSchemeHttps,
                    StringComparison.OrdinalIgnoreCase))
            || !string.IsNullOrEmpty(candidate.UserInfo)
            || !string.IsNullOrEmpty(candidate.Fragment))
        {
            return false;
        }

        uri = candidate;
        return true;
    }

    private static string? ReadCurrentWlanInterfaceId()
    {
        WlanReadResult wlanRead = NativeWlanReader.ReadCurrent();
        WlanInterfaceIdentityReadResult identityRead =
            WlanInterfaceIdentityReader.ReadCurrent();
        WlanSnapshot? currentWlan =
            WlanInterfaceIdentityReader.AttachIdentity(
                wlanRead.FirstConnectedInterface,
                identityRead);
        return currentWlan?.InterfaceId;
    }

    private static string FormatInternalProxyRouteResult(
        InternalProxyRouteComparisonReportDocument report)
    {
        StringBuilder builder = new();
        builder.AppendLine(
            $"상태: {FormatInternalProxyRouteStatus(report.Status)}");
        builder.AppendLine(report.Message);
        builder.AppendLine(
            $"같은 로컬 인터페이스: {FormatNullableBoolean(report.SameLocalInterface)}");
        builder.AppendLine(
            $"내부 경로: {FormatReportInterface(report.InternalInterface)}");
        builder.AppendLine(
            $"프록시 경로: {FormatReportInterface(report.ProxyInterface)}");
        builder.AppendLine(
            $"프록시 후보: {report.ProxyCandidateCount}개 · 성공: {report.ProxySuccessfulCandidateCount}개 · 서로 다른 인터페이스: {report.ProxyDistinctInterfaceCount}개");
        builder.AppendLine(
            $"DIRECT 우선: {(report.ProxyDirectPathSelected ? "예" : "아니요")} · DIRECT fallback: {(report.ProxyDirectFallbackPresent ? "있음" : "없음")}");
        builder.AppendLine(
            $"VPN·터널: {(report.AnyVpnOrTunnelInterface ? "포함" : "확인 안 됨")} · 가상 인터페이스: {(report.AnyVirtualInterface ? "포함" : "확인 안 됨")}");

        if (report.Findings.Count > 0)
        {
            builder.AppendLine("판정:");
            foreach (ReportFinding finding in report.Findings)
            {
                builder.AppendLine(
                    $"- [{finding.Severity}] {finding.Title}");
                builder.AppendLine($"  {finding.Interpretation}");
            }
        }

        if (report.Warnings.Count > 0)
        {
            builder.AppendLine("주의:");
            foreach (string warning in report.Warnings)
            {
                builder.AppendLine($"- {warning}");
            }
        }

        builder.AppendLine($"한계: {report.Limitation}");
        return builder.ToString().TrimEnd();
    }

    private static string FormatInternalProxyRouteStatus(
        string status) =>
        status switch
        {
            "Ready" => "비교 완료 · 동일 로컬 인터페이스",
            "Diverged" => "비교 완료 · 로컬 인터페이스 분기",
            "Ambiguous" => "비교 근거 모호",
            _ => "비교 미완료"
        };

    private static string FormatReportInterface(
        InternalProxyRouteComparisonReportInterface? routeInterface)
    {
        if (routeInterface is null)
        {
            return "단일 인터페이스 확인 불가";
        }

        return string.Join(
            " · ",
            $"지문 {routeInterface.InterfaceFingerprint}",
            routeInterface.Category,
            $"현재 WLAN {FormatNullableBoolean(routeInterface.MatchesExpectedWlan)}",
            $"VPN {FormatNullableBoolean(routeInterface.IsVpn)}",
            $"가상 {FormatNullableBoolean(routeInterface.IsVirtual)}");
    }

    private static string FormatNullableBoolean(bool? value) =>
        value switch
        {
            true => "예",
            false => "아니요",
            _ => "판정 안 함"
        };

    private static Brush GetInternalProxyRouteResultBrush(
        InternalProxyRouteComparisonStatus status) =>
        status switch
        {
            InternalProxyRouteComparisonStatus.Ready =>
                Brushes.DarkGreen,
            InternalProxyRouteComparisonStatus.Incomplete =>
                Brushes.DarkOrange,
            _ => Brushes.DarkRed
        };

    private static string GetApplicationVersion() =>
        Assembly.GetExecutingAssembly()
            .GetName()
            .Version?
            .ToString()
        ?? "개발 빌드";

    private void SetInternalProxyRouteRunningState(bool isRunning)
    {
        if (_runInternalProxyRouteComparisonButton is not null)
        {
            _runInternalProxyRouteComparisonButton.IsEnabled =
                !isRunning;
        }

        if (_stopInternalProxyRouteComparisonButton is not null)
        {
            _stopInternalProxyRouteComparisonButton.IsEnabled =
                isRunning;
        }

        if (_internalRouteTargetTextBox is not null)
        {
            _internalRouteTargetTextBox.IsEnabled = !isRunning;
        }

        if (_externalRouteTargetTextBox is not null)
        {
            _externalRouteTargetTextBox.IsEnabled = !isRunning;
        }

        if (_proxyRouteTextBox is not null)
        {
            _proxyRouteTextBox.IsEnabled = !isRunning;
        }

        if (_generateInternalProxyRouteReportButton is not null)
        {
            _generateInternalProxyRouteReportButton.IsEnabled =
                !isRunning
                && _lastInternalProxyRouteComparison is not null;
        }
    }

    private void SetInternalProxyRouteResult(
        string text,
        Brush brush)
    {
        if (_internalProxyRouteResultText is null)
        {
            return;
        }

        _internalProxyRouteResultText.Text = text;
        _internalProxyRouteResultText.Foreground = brush;
    }

    private void OnInternalProxyRouteWindowClosed(
        object? sender,
        EventArgs e)
    {
        try
        {
            _internalProxyRouteCancellation?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The comparison completed while the window was closing.
        }

        if (_internalProxyRouteClosedHooked)
        {
            Closed -= OnInternalProxyRouteWindowClosed;
            _internalProxyRouteClosedHooked = false;
        }
    }
}
