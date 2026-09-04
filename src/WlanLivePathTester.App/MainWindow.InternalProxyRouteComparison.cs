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
    private readonly ProxyEndpointRouteAnalyzer
        _proxyEndpointRouteAnalyzer = new();
    private CancellationTokenSource?
        _internalProxyRouteComparisonCancellation;
    private TextBox? _internalRouteTargetTextBox;
    private TextBox? _proxyDirectiveTextBox;
    private Button? _startInternalProxyRouteComparisonButton;
    private Button? _cancelInternalProxyRouteComparisonButton;
    private TextBox? _internalProxyRouteComparisonResultTextBox;
    private bool _internalProxyRouteComparisonTabAdded;
    private bool _internalProxyRouteComparisonClosedHooked;
    private bool _previousInternalMeasurementButtonEnabled;
    private bool _previousExternalMeasurementButtonEnabled;
    private bool? _previousObservationButtonEnabled;
    private DestinationRouteEvidence? _lastInternalDirectRouteEvidence;
    private ProxyEndpointRouteAnalysisResult?
        _lastProxyEndpointRouteAnalysis;
    private InternalProxyRouteComparisonResult?
        _lastInternalProxyRouteComparison;
    private ReportFinding? _lastInternalProxyRouteComparisonFinding;

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
        if (!_internalProxyRouteComparisonClosedHooked)
        {
            Closed += OnInternalProxyRouteComparisonWindowClosed;
            _internalProxyRouteComparisonClosedHooked = true;
        }
    }

    private TabItem CreateInternalProxyRouteComparisonTab()
    {
        _internalRouteTargetTextBox = new TextBox
        {
            MinWidth = 520,
            Padding = new Thickness(8, 6, 8, 6),
            ToolTip =
                "회사 정책상 DIRECT로 접근하는 승인된 내부 URL, 호스트 또는 IP를 입력하십시오."
        };
        _proxyDirectiveTextBox = new TextBox
        {
            MinWidth = 520,
            MinHeight = 110,
            Padding = new Thickness(8),
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            ToolTip =
                "예: PROXY proxy.example:8080; DIRECT 또는 http=host:port;https=host:port"
        };
        _startInternalProxyRouteComparisonButton = new Button
        {
            Content = "내부↔프록시 경로 비교",
            MinWidth = 190,
            Padding = new Thickness(12, 8, 12, 8)
        };
        _startInternalProxyRouteComparisonButton.Click +=
            OnStartInternalProxyRouteComparisonClick;
        _cancelInternalProxyRouteComparisonButton = new Button
        {
            Content = "경로 확인 중지",
            MinWidth = 120,
            Padding = new Thickness(12, 8, 12, 8),
            IsEnabled = false
        };
        _cancelInternalProxyRouteComparisonButton.Click +=
            OnCancelInternalProxyRouteComparisonClick;
        _internalProxyRouteComparisonResultTextBox = new TextBox
        {
            MinHeight = 390,
            Padding = new Thickness(12),
            FontFamily = new FontFamily("Consolas"),
            FontSize = 13,
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Background = Brushes.White,
            Text =
                "아직 비교하지 않았습니다. 내부 DIRECT 대상과 현재 외부 대상에 적용된 프록시 지시문을 입력하십시오."
        };

        StackPanel form = new();
        form.Children.Add(CreateRouteComparisonLabel(
            "내부 DIRECT 기준 대상"));
        form.Children.Add(_internalRouteTargetTextBox);
        form.Children.Add(CreateRouteComparisonHint(
            "URL·호스트·IP를 지원합니다. 이 대상은 회사 정책상 프록시를 우회하는 승인된 내부 주소여야 합니다."));
        form.Children.Add(CreateRouteComparisonLabel(
            "프록시 지시문 또는 PAC/WPAD 판정 결과",
            topMargin: 16));
        form.Children.Add(_proxyDirectiveTextBox);
        form.Children.Add(CreateRouteComparisonHint(
            "PROXY·HTTPS·SOCKS·DIRECT fallback 또는 Windows의 http=·https= 형식을 지원합니다. 원문은 자동 저장·업로드하지 않습니다."));

        StackPanel buttons = new()
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 16, 0, 0)
        };
        buttons.Children.Add(_startInternalProxyRouteComparisonButton);
        buttons.Children.Add(new Border { Width = 10 });
        buttons.Children.Add(_cancelInternalProxyRouteComparisonButton);
        form.Children.Add(buttons);

        StackPanel content = new();
        content.Children.Add(new TextBlock
        {
            FontSize = 22,
            FontWeight = FontWeights.SemiBold,
            Text = "내부 DIRECT ↔ 프록시 로컬 경로 비교"
        });
        content.Children.Add(new TextBlock
        {
            Margin = new Thickness(0, 8, 0, 0),
            Foreground = new SolidColorBrush(
                Color.FromRgb(86, 101, 115)),
            TextWrapping = TextWrapping.Wrap,
            Text =
                "버튼을 누르면 내부 대상과 프록시 후보의 DNS 주소 및 Windows 최적 로컬 인터페이스를 확인합니다. HTTP 다운로드·프록시 로그인·외부 API·결과 업로드는 수행하지 않습니다."
        });
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
                Text =
                    "Ready는 두 경로의 첫 로컬 NIC가 같다는 뜻이고, Diverged는 서로 다르다는 뜻입니다. 두 상태 모두 이후 사내망·프록시·인터넷 경로의 정상 여부를 자동 확정하지 않습니다."
            }
        });
        content.Children.Add(new Border
        {
            Margin = new Thickness(0, 12, 0, 0),
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
                    "결과에는 프록시 호스트와 전체 인터페이스 GUID 대신 짧은 비가역 지문만 표시합니다. 정확한 NIC 판정은 현재 실행 중 메모리의 전체 GUID로만 수행합니다."
            }
        });
        content.Children.Add(new Border
        {
            Margin = new Thickness(0, 16, 0, 0),
            Padding = new Thickness(16),
            CornerRadius = new CornerRadius(8),
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(
                Color.FromRgb(216, 221, 227)),
            BorderThickness = new Thickness(1),
            Child = form
        });
        content.Children.Add(new TextBlock
        {
            Margin = new Thickness(0, 18, 0, 8),
            FontSize = 17,
            FontWeight = FontWeights.SemiBold,
            Text = "비교 결과"
        });
        content.Children.Add(_internalProxyRouteComparisonResultTextBox);

        return new TabItem
        {
            Header = "경로 비교",
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

    private static TextBlock CreateRouteComparisonLabel(
        string text,
        double topMargin = 0) =>
        new()
        {
            Margin = new Thickness(0, topMargin, 0, 6),
            FontWeight = FontWeights.SemiBold,
            Text = text
        };

    private static TextBlock CreateRouteComparisonHint(
        string text) =>
        new()
        {
            Margin = new Thickness(0, 5, 0, 0),
            Foreground = new SolidColorBrush(
                Color.FromRgb(86, 101, 115)),
            TextWrapping = TextWrapping.Wrap,
            Text = text
        };

    private async void OnStartInternalProxyRouteComparisonClick(
        object sender,
        RoutedEventArgs e)
    {
        if (_internalProxyRouteComparisonCancellation is not null)
        {
            return;
        }

        if (_measurementRunning || _observationCancellation is not null)
        {
            SetInternalProxyRouteComparisonResult(
                "내부·외부 다운로드 측정 또는 브라우저 관찰이 진행 중입니다. 완료하거나 중지한 뒤 경로 비교를 실행하십시오.",
                Brushes.DarkOrange);
            return;
        }

        string internalTarget =
            _internalRouteTargetTextBox?.Text.Trim()
            ?? string.Empty;
        string proxyDirective =
            _proxyDirectiveTextBox?.Text.Trim()
            ?? string.Empty;
        if (internalTarget.Length == 0)
        {
            SetInternalProxyRouteComparisonResult(
                "승인된 내부 DIRECT URL, 호스트 또는 IP를 입력하십시오.",
                Brushes.DarkOrange);
            return;
        }

        if (proxyDirective.Length == 0)
        {
            SetInternalProxyRouteComparisonResult(
                "현재 외부 대상에 적용된 프록시 지시문 또는 PAC/WPAD 판정 결과를 입력하십시오.",
                Brushes.DarkOrange);
            return;
        }

        CancellationTokenSource activeCancellation = new();
        _internalProxyRouteComparisonCancellation =
            activeCancellation;
        SetInternalProxyRouteComparisonRunningState(isRunning: true);
        SetInternalProxyRouteComparisonResult(
            "현재 WLAN identity와 내부·프록시 후보의 Windows 로컬 경로를 확인하고 있습니다. HTTP 다운로드나 프록시 로그인은 수행하지 않습니다.",
            Brushes.DarkSlateGray);

        try
        {
            string? expectedWlanInterfaceId = await Task.Run(
                ReadCurrentWlanInterfaceId,
                activeCancellation.Token);

            DestinationRouteEvidence internalRoute =
                await LocalRouteEvidenceReader.ReadAsync(
                    internalTarget,
                    "내부 DIRECT 기준 대상",
                    RouteProbePurpose.InternalDirectTarget,
                    dnsTimeoutSeconds: 5,
                    activeCancellation.Token);
            activeCancellation.Token.ThrowIfCancellationRequested();
            if (internalRoute.Status
                == DestinationRouteEvidenceStatus.Canceled)
            {
                throw new OperationCanceledException(
                    activeCancellation.Token);
            }

            ProxyEndpointRouteAnalysisResult proxyAnalysis =
                await _proxyEndpointRouteAnalyzer.AnalyzeAsync(
                    proxyDirective,
                    expectedWlanInterfaceId,
                    dnsTimeoutSeconds: 5,
                    endpointLimit:
                        ProxyEndpointRouteAnalyzer.DefaultEndpointLimit,
                    activeCancellation.Token);
            if (proxyAnalysis.Status
                == ProxyEndpointRouteAnalysisStatus.Canceled)
            {
                throw new OperationCanceledException(
                    activeCancellation.Token);
            }

            InternalProxyRouteComparisonResult comparison =
                InternalProxyRouteComparisonEvaluator.Evaluate(
                    internalRoute,
                    proxyAnalysis);
            ReportFinding finding =
                InternalProxyRouteComparisonFindingMapper.FromResult(
                    comparison);
            string rendered =
                InternalProxyRouteComparisonTextRenderer.Render(
                    comparison,
                    proxyAnalysis);

            _lastInternalDirectRouteEvidence = internalRoute;
            _lastProxyEndpointRouteAnalysis = proxyAnalysis;
            _lastInternalProxyRouteComparison = comparison;
            _lastInternalProxyRouteComparisonFinding = finding;

            StringBuilder builder = new(rendered);
            builder.AppendLine();
            builder.AppendLine();
            builder.AppendLine("[보고서 판정]");
            builder.AppendLine(
                $"{finding.Severity} · {finding.Code}");
            builder.AppendLine(finding.Title);
            builder.AppendLine($"근거: {finding.Evidence}");
            SetInternalProxyRouteComparisonResult(
                builder.ToString(),
                GetInternalProxyRouteComparisonBrush(
                    comparison.Status));
        }
        catch (OperationCanceledException)
        {
            SetInternalProxyRouteComparisonResult(
                "사용자 요청으로 내부↔프록시 경로 비교를 중지했습니다. 완료되지 않은 후보는 조회하지 않았습니다.",
                Brushes.DarkOrange);
        }
        catch (Exception exception)
        {
            SetInternalProxyRouteComparisonResult(
                $"로컬 경로 비교 중 오류가 발생했습니다. 오류 유형: {exception.GetType().Name}. 입력 원문과 예외 메시지는 결과에 표시하지 않았습니다.",
                Brushes.DarkRed);
        }
        finally
        {
            if (ReferenceEquals(
                    _internalProxyRouteComparisonCancellation,
                    activeCancellation))
            {
                _internalProxyRouteComparisonCancellation = null;
            }

            activeCancellation.Dispose();
            SetInternalProxyRouteComparisonRunningState(
                isRunning: false);
        }
    }

    private void OnCancelInternalProxyRouteComparisonClick(
        object sender,
        RoutedEventArgs e)
    {
        CancellationTokenSource? active =
            _internalProxyRouteComparisonCancellation;
        if (active is null)
        {
            return;
        }

        try
        {
            active.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The comparison completed while the cancel click was handled.
        }

        SetInternalProxyRouteComparisonResult(
            "경로 비교 중지 요청을 처리하고 있습니다.",
            Brushes.DarkOrange);
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

    private void SetInternalProxyRouteComparisonRunningState(
        bool isRunning)
    {
        if (isRunning)
        {
            _previousInternalMeasurementButtonEnabled =
                StartInternalMeasurementButton.IsEnabled;
            _previousExternalMeasurementButtonEnabled =
                StartExternalMeasurementButton.IsEnabled;
            _previousObservationButtonEnabled =
                _startObservationButton?.IsEnabled;
        }

        if (_startInternalProxyRouteComparisonButton is not null)
        {
            _startInternalProxyRouteComparisonButton.IsEnabled =
                !isRunning;
        }

        if (_cancelInternalProxyRouteComparisonButton is not null)
        {
            _cancelInternalProxyRouteComparisonButton.IsEnabled =
                isRunning;
        }

        if (_internalRouteTargetTextBox is not null)
        {
            _internalRouteTargetTextBox.IsEnabled = !isRunning;
        }

        if (_proxyDirectiveTextBox is not null)
        {
            _proxyDirectiveTextBox.IsEnabled = !isRunning;
        }

        if (isRunning)
        {
            StartInternalMeasurementButton.IsEnabled = false;
            StartExternalMeasurementButton.IsEnabled = false;
            if (_startObservationButton is not null)
            {
                _startObservationButton.IsEnabled = false;
            }
        }
        else
        {
            StartInternalMeasurementButton.IsEnabled =
                _previousInternalMeasurementButtonEnabled;
            StartExternalMeasurementButton.IsEnabled =
                _previousExternalMeasurementButtonEnabled;
            if (_startObservationButton is not null
                && _previousObservationButtonEnabled.HasValue)
            {
                _startObservationButton.IsEnabled =
                    _previousObservationButtonEnabled.Value;
            }
        }
    }

    private void SetInternalProxyRouteComparisonResult(
        string text,
        Brush brush)
    {
        if (_internalProxyRouteComparisonResultTextBox is null)
        {
            return;
        }

        _internalProxyRouteComparisonResultTextBox.Text = text;
        _internalProxyRouteComparisonResultTextBox.Foreground = brush;
        _internalProxyRouteComparisonResultTextBox.ScrollToHome();
    }

    private static Brush GetInternalProxyRouteComparisonBrush(
        InternalProxyRouteComparisonStatus status) =>
        status switch
        {
            InternalProxyRouteComparisonStatus.Ready =>
                Brushes.DarkGreen,
            InternalProxyRouteComparisonStatus.Diverged =>
                Brushes.DarkBlue,
            InternalProxyRouteComparisonStatus.Ambiguous =>
                Brushes.DarkOrange,
            _ => Brushes.DarkRed
        };

    private void OnInternalProxyRouteComparisonWindowClosed(
        object? sender,
        EventArgs e)
    {
        try
        {
            _internalProxyRouteComparisonCancellation?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The comparison completed while the window was closing.
        }

        if (_internalProxyRouteComparisonClosedHooked)
        {
            Closed -= OnInternalProxyRouteComparisonWindowClosed;
            _internalProxyRouteComparisonClosedHooked = false;
        }
    }
}
