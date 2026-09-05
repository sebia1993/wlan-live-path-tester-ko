using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WlanLivePathTester.Core.Models;
using WlanLivePathTester.Core.Reporting;
using WlanLivePathTester.Core.Routing;
using WlanLivePathTester.Windows.Routing;
using WlanLivePathTester.Windows.Wlan;

namespace WlanLivePathTester.App;

public partial class MainWindow
{
    private readonly InternalProxyRouteComparisonCoordinator
        _routeComparisonCoordinatorV1 = new();
    private CancellationTokenSource? _routeComparisonCancellationV1;
    private TabItem? _routeComparisonTabV1;
    private TextBox? _routeInternalTargetTextBoxV1;
    private TextBox? _routeExternalTargetTextBoxV1;
    private TextBox? _routeProxyDirectiveTextBoxV1;
    private TextBox? _routeComparisonResultTextBoxV1;
    private TextBlock? _routeComparisonReportStatusTextV1;
    private Button? _routeComparisonStartButtonV1;
    private Button? _routeComparisonCancelButtonV1;
    private Button? _routeComparisonReportButtonV1;
    private Button? _routeComparisonOpenFolderButtonV1;
    private Button? _routeComparisonOpenHtmlButtonV1;
    private InternalProxyRouteComparisonRunResult?
        _lastRouteComparisonRunV1;
    private string? _lastRouteComparisonReportDirectoryV1;
    private string? _lastRouteComparisonReportHtmlPathV1;
    private Dictionary<TabItem, bool>?
        _routeComparisonPeerTabStatesV1;
    private bool _routeComparisonTabAddedV1;
    private bool _routeComparisonClosedHookedV1;

    internal void EnsureRouteComparisonCoordinatorTabV1()
    {
        if (_routeComparisonTabAddedV1)
        {
            return;
        }

        TabControl? tabControl = FindRouteComparisonDescendantV1<
            TabControl>(this);
        if (tabControl is null)
        {
            return;
        }

        _routeComparisonTabV1 = CreateRouteComparisonTabV1();
        tabControl.Items.Add(_routeComparisonTabV1);
        _routeComparisonTabAddedV1 = true;

        if (!_routeComparisonClosedHookedV1)
        {
            Closed += OnRouteComparisonWindowClosedV1;
            _routeComparisonClosedHookedV1 = true;
        }
    }

    private TabItem CreateRouteComparisonTabV1()
    {
        _routeInternalTargetTextBoxV1 = CreateRouteInputTextBoxV1(
            acceptsReturn: false,
            minimumHeight: 38,
            "회사 정책상 DIRECT로 접근하는 승인된 내부 URL, 호스트 또는 IP를 입력하십시오.");
        _routeExternalTargetTextBoxV1 = CreateRouteInputTextBoxV1(
            acceptsReturn: false,
            minimumHeight: 38,
            "현재 프록시 지시문이 적용되는 절대 HTTP 또는 HTTPS URL을 입력하십시오.");
        _routeProxyDirectiveTextBoxV1 = CreateRouteInputTextBoxV1(
            acceptsReturn: true,
            minimumHeight: 118,
            "예: PROXY proxy.example:8080; DIRECT 또는 http=host:port;https=host:port");

        _routeComparisonStartButtonV1 = new Button
        {
            Content = "내부↔프록시 경로 비교",
            MinWidth = 200,
            Padding = new Thickness(13, 8, 13, 8)
        };
        _routeComparisonStartButtonV1.Click +=
            OnStartRouteComparisonV1;

        _routeComparisonCancelButtonV1 = new Button
        {
            Content = "경로 확인 중지",
            MinWidth = 125,
            Padding = new Thickness(13, 8, 13, 8),
            IsEnabled = false
        };
        _routeComparisonCancelButtonV1.Click +=
            OnCancelRouteComparisonV1;

        _routeComparisonReportButtonV1 = new Button
        {
            Content = "비교 보고서 생성",
            MinWidth = 150,
            Padding = new Thickness(13, 8, 13, 8),
            IsEnabled = false
        };
        _routeComparisonReportButtonV1.Click +=
            OnCreateRouteComparisonReportV1;

        _routeComparisonOpenFolderButtonV1 = new Button
        {
            Content = "보고서 폴더 열기",
            MinWidth = 135,
            Padding = new Thickness(13, 8, 13, 8),
            IsEnabled = false
        };
        _routeComparisonOpenFolderButtonV1.Click +=
            OnOpenRouteComparisonReportFolderV1;

        _routeComparisonOpenHtmlButtonV1 = new Button
        {
            Content = "최신 HTML 열기",
            MinWidth = 125,
            Padding = new Thickness(13, 8, 13, 8),
            IsEnabled = false
        };
        _routeComparisonOpenHtmlButtonV1.Click +=
            OnOpenRouteComparisonReportHtmlV1;

        _routeComparisonResultTextBoxV1 = new TextBox
        {
            MinHeight = 420,
            Padding = new Thickness(12),
            FontFamily = new FontFamily("Consolas"),
            FontSize = 13,
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility =
                ScrollBarVisibility.Disabled,
            Background = Brushes.White,
            Text =
                "아직 비교하지 않았습니다. 승인된 내부 DIRECT 대상, 외부 HTTP(S) URL과 해당 URL에 적용되는 프록시 지시문을 입력하십시오."
        };
        _routeComparisonReportStatusTextV1 = new TextBlock
        {
            Margin = new Thickness(0, 10, 0, 0),
            Foreground = Brushes.DarkSlateGray,
            TextWrapping = TextWrapping.Wrap,
            Text =
                "보고서는 비교 실행 후 사용자가 버튼을 눌렀을 때만 로컬로 생성됩니다."
        };

        StackPanel form = new();
        form.Children.Add(CreateRouteLabelV1(
            "내부 DIRECT 기준 대상"));
        form.Children.Add(_routeInternalTargetTextBoxV1);
        form.Children.Add(CreateRouteHintV1(
            "URL·호스트·IPv4·IPv6를 지원합니다. 회사 정책상 프록시를 우회하는 승인된 내부 대상만 사용하십시오."));
        form.Children.Add(CreateRouteLabelV1(
            "외부 프록시 판정 대상 URL",
            topMargin: 16));
        form.Children.Add(_routeExternalTargetTextBoxV1);
        form.Children.Add(CreateRouteHintV1(
            "프록시 매핑의 http=·https= 적용 범위를 정확히 선택하기 위해 필요합니다. 절대 HTTP(S) URL만 허용합니다."));
        form.Children.Add(CreateRouteLabelV1(
            "프록시 지시문 또는 PAC/WPAD 판정 결과",
            topMargin: 16));
        form.Children.Add(_routeProxyDirectiveTextBoxV1);
        form.Children.Add(CreateRouteHintV1(
            "PROXY·HTTPS·SOCKS·DIRECT fallback과 Windows의 프로토콜별 수동 프록시 형식을 지원합니다. 입력 원문은 자동 저장하거나 업로드하지 않습니다."));

        WrapPanel runButtons = new()
        {
            Margin = new Thickness(0, 16, 0, 0)
        };
        runButtons.Children.Add(_routeComparisonStartButtonV1);
        runButtons.Children.Add(CreateButtonSpacerV1());
        runButtons.Children.Add(_routeComparisonCancelButtonV1);
        form.Children.Add(runButtons);

        WrapPanel reportButtons = new()
        {
            Margin = new Thickness(0, 12, 0, 0)
        };
        reportButtons.Children.Add(_routeComparisonReportButtonV1);
        reportButtons.Children.Add(CreateButtonSpacerV1());
        reportButtons.Children.Add(
            _routeComparisonOpenFolderButtonV1);
        reportButtons.Children.Add(CreateButtonSpacerV1());
        reportButtons.Children.Add(_routeComparisonOpenHtmlButtonV1);
        form.Children.Add(reportButtons);
        form.Children.Add(_routeComparisonReportStatusTextV1);

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
                "비교 버튼을 누르면 입력을 먼저 검증한 뒤 필요한 경우에만 운영체제 DNS와 Windows 최적 로컬 인터페이스를 확인합니다. HTTP 다운로드·프록시 로그인·외부 API·결과 업로드는 수행하지 않습니다."
        });
        content.Children.Add(CreateRouteNoticeV1(
            "Ready는 내부와 프록시까지의 첫 로컬 NIC가 같다는 뜻입니다. Diverged는 서로 다르지만 VPN·터널·유선 우선순위 또는 의도된 분할 라우팅일 수 있으므로 자동 장애로 확정하지 않습니다.",
            Color.FromRgb(255, 248, 231),
            Color.FromRgb(232, 206, 138)));
        content.Children.Add(CreateRouteNoticeV1(
            "결과와 보고서에는 내부 URL·프록시 호스트·전체 인터페이스 GUID·이름·설명 대신 검증된 상태, 개수와 짧은 비가역 인터페이스 지문만 사용합니다.",
            Color.FromRgb(232, 246, 243),
            Color.FromRgb(115, 198, 182)));
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
            Text = "안전한 비교 결과"
        });
        content.Children.Add(_routeComparisonResultTextBoxV1);

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

    private async void OnStartRouteComparisonV1(
        object sender,
        RoutedEventArgs e)
    {
        if (_routeComparisonCancellationV1 is not null)
        {
            return;
        }

        if (IsExistingNetworkOperationActiveV1())
        {
            SetRouteComparisonResultV1(
                "내부·외부 다운로드 측정 또는 브라우저 관찰이 진행 중입니다. 완료하거나 중지한 뒤 경로 비교를 실행하십시오.",
                Brushes.DarkOrange);
            return;
        }

        string internalTarget =
            _routeInternalTargetTextBoxV1?.Text.Trim()
            ?? string.Empty;
        string externalTargetText =
            _routeExternalTargetTextBoxV1?.Text.Trim()
            ?? string.Empty;
        string proxyDirective =
            _routeProxyDirectiveTextBoxV1?.Text.Trim()
            ?? string.Empty;

        Uri? externalTarget = null;
        if (!Uri.TryCreate(
                externalTargetText,
                UriKind.Absolute,
                out externalTarget))
        {
            SetRouteComparisonResultV1(
                "외부 프록시 판정 대상은 절대 HTTP 또는 HTTPS URL이어야 합니다.",
                Brushes.DarkOrange);
            return;
        }

        CancellationTokenSource active = new();
        _routeComparisonCancellationV1 = active;
        SetRouteComparisonRunningStateV1(isRunning: true);
        SetRouteComparisonResultV1(
            "입력을 검증하고 필요한 경우에만 내부 대상과 프록시 후보의 Windows 로컬 경로를 확인하고 있습니다.",
            Brushes.DarkSlateGray);
        SetRouteComparisonReportStatusV1(
            "비교가 완료될 때까지 보고서 생성을 사용할 수 없습니다.",
            Brushes.DarkSlateGray);

        try
        {
            string? expectedWlanInterfaceId = await Task.Run(
                ReadCurrentWlanInterfaceIdV1,
                active.Token);
            InternalProxyRouteComparisonRunResult run =
                await _routeComparisonCoordinatorV1.RunAsync(
                    internalTarget,
                    proxyDirective,
                    externalTarget,
                    expectedWlanInterfaceId,
                    dnsTimeoutSeconds: 5,
                    active.Token);

            _lastRouteComparisonRunV1 = run;
            string rendered =
                InternalProxyRouteComparisonRunTextRenderer.Render(
                    run);
            SetRouteComparisonResultV1(
                rendered,
                GetRouteComparisonBrushV1(run));
            SetRouteComparisonReportStatusV1(
                "구조화 비교 결과가 메모리에 준비됐습니다. 보고서는 사용자가 생성 버튼을 누를 때만 로컬에 저장됩니다.",
                Brushes.DarkGreen);
            if (_routeComparisonReportButtonV1 is not null)
            {
                _routeComparisonReportButtonV1.IsEnabled = true;
            }
        }
        catch (OperationCanceledException)
        {
            SetRouteComparisonResultV1(
                "사용자 요청으로 경로 비교를 중지했습니다. 완료되지 않은 DNS·라우팅 단계는 결과로 저장하지 않았습니다.",
                Brushes.DarkOrange);
            SetRouteComparisonReportStatusV1(
                "새 구조화 실행 결과가 생성되지 않았습니다.",
                Brushes.DarkOrange);
        }
        catch (Exception exception)
        {
            SetRouteComparisonResultV1(
                $"경로 비교 UI 처리 중 오류가 발생했습니다. 오류 유형: {exception.GetType().Name}. 입력 원문과 예외 메시지는 결과에 표시하지 않았습니다.",
                Brushes.DarkRed);
            SetRouteComparisonReportStatusV1(
                "새 구조화 실행 결과가 생성되지 않았습니다.",
                Brushes.DarkRed);
        }
        finally
        {
            if (ReferenceEquals(
                    _routeComparisonCancellationV1,
                    active))
            {
                _routeComparisonCancellationV1 = null;
            }

            active.Dispose();
            SetRouteComparisonRunningStateV1(isRunning: false);
        }
    }

    private void OnCancelRouteComparisonV1(
        object sender,
        RoutedEventArgs e)
    {
        CancellationTokenSource? active =
            _routeComparisonCancellationV1;
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
            // The operation completed while the click was handled.
        }

        SetRouteComparisonResultV1(
            "경로 비교 중지 요청을 처리하고 있습니다.",
            Brushes.DarkOrange);
    }

    private async void OnCreateRouteComparisonReportV1(
        object sender,
        RoutedEventArgs e)
    {
        InternalProxyRouteComparisonRunResult? run =
            _lastRouteComparisonRunV1;
        if (run is null)
        {
            SetRouteComparisonReportStatusV1(
                "저장할 구조화 경로 비교 결과가 없습니다.",
                Brushes.DarkOrange);
            return;
        }

        if (_routeComparisonCancellationV1 is not null)
        {
            SetRouteComparisonReportStatusV1(
                "경로 비교가 진행 중입니다. 완료하거나 중지한 뒤 보고서를 생성하십시오.",
                Brushes.DarkOrange);
            return;
        }

        if (_routeComparisonReportButtonV1 is not null)
        {
            _routeComparisonReportButtonV1.IsEnabled = false;
        }
        SetRouteComparisonReportStatusV1(
            "안전 스냅샷으로 JSON·CSV·HTML과 SHA-256을 로컬에 생성하고 있습니다.",
            Brushes.DarkSlateGray);

        try
        {
            string version = Assembly.GetExecutingAssembly()
                .GetName()
                .Version?
                .ToString()
                ?? "개발 빌드";
            InternalProxyRouteComparisonRunReportDocument document =
                InternalProxyRouteComparisonRunReportWriter
                    .CreateDocument(run, version);
            InternalProxyRouteComparisonRunReportExportResult export =
                await Task.Run(() =>
                    InternalProxyRouteComparisonRunReportWriter
                        .WriteAll(
                            document,
                            GetRouteComparisonReportDirectoryV1()));

            _lastRouteComparisonReportDirectoryV1 =
                export.OutputDirectory;
            _lastRouteComparisonReportHtmlPathV1 =
                export.HtmlPath;
            if (_routeComparisonOpenFolderButtonV1 is not null)
            {
                _routeComparisonOpenFolderButtonV1.IsEnabled = true;
            }

            if (_routeComparisonOpenHtmlButtonV1 is not null)
            {
                _routeComparisonOpenHtmlButtonV1.IsEnabled = true;
            }

            StringBuilder message = new();
            message.AppendLine("경로 비교 보고서 생성 완료");
            message.AppendLine(
                $"실행 상태: {document.RouteComparison.RunStatus}");
            message.AppendLine(
                $"비교 상태: {document.RouteComparison.ComparisonStatus ?? "없음"}");
            message.AppendLine(
                $"판정: {document.RouteComparison.Finding.Severity} · {document.RouteComparison.Finding.Code}");
            message.AppendLine(
                $"JSON: {Path.GetFileName(export.JsonPath)}");
            message.AppendLine(
                $"CSV: {Path.GetFileName(export.CsvPath)}");
            message.AppendLine(
                $"HTML: {Path.GetFileName(export.HtmlPath)}");
            message.AppendLine(
                $"무결성: {Path.GetFileName(export.Sha256Path)}");
            message.Append(
                "추가 DNS·HTTP 요청과 외부 전송은 수행하지 않았습니다.");
            SetRouteComparisonReportStatusV1(
                message.ToString(),
                Brushes.DarkGreen);
        }
        catch (Exception exception)
        {
            SetRouteComparisonReportStatusV1(
                $"경로 비교 보고서 생성 중 로컬 파일 오류가 발생했습니다. 오류 유형: {exception.GetType().Name}. 예외 원문은 표시하지 않았습니다.",
                Brushes.DarkRed);
        }
        finally
        {
            if (_routeComparisonReportButtonV1 is not null)
            {
                _routeComparisonReportButtonV1.IsEnabled = true;
            }
        }
    }

    private void OnOpenRouteComparisonReportFolderV1(
        object sender,
        RoutedEventArgs e) =>
        OpenRouteComparisonPathV1(
            _lastRouteComparisonReportDirectoryV1,
            "경로 비교 보고서 폴더를 찾을 수 없습니다.");

    private void OnOpenRouteComparisonReportHtmlV1(
        object sender,
        RoutedEventArgs e) =>
        OpenRouteComparisonPathV1(
            _lastRouteComparisonReportHtmlPathV1,
            "최신 경로 비교 HTML 보고서를 찾을 수 없습니다.");

    private void OpenRouteComparisonPathV1(
        string? path,
        string missingMessage)
    {
        if (string.IsNullOrWhiteSpace(path)
            || (!Directory.Exists(path) && !File.Exists(path)))
        {
            SetRouteComparisonReportStatusV1(
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
            SetRouteComparisonReportStatusV1(
                $"로컬 보고서 경로를 열지 못했습니다. 오류 유형: {exception.GetType().Name}.",
                Brushes.DarkRed);
        }
    }

    private static TextBox CreateRouteInputTextBoxV1(
        bool acceptsReturn,
        double minimumHeight,
        string toolTip) =>
        new()
        {
            MinWidth = 520,
            MinHeight = minimumHeight,
            Padding = new Thickness(8, 6, 8, 6),
            AcceptsReturn = acceptsReturn,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = acceptsReturn
                ? ScrollBarVisibility.Auto
                : ScrollBarVisibility.Hidden,
            ToolTip = toolTip
        };

    private static TextBlock CreateRouteLabelV1(
        string text,
        double topMargin = 0) =>
        new()
        {
            Margin = new Thickness(0, topMargin, 0, 6),
            FontWeight = FontWeights.SemiBold,
            Text = text
        };

    private static TextBlock CreateRouteHintV1(string text) =>
        new()
        {
            Margin = new Thickness(0, 5, 0, 0),
            Foreground = new SolidColorBrush(
                Color.FromRgb(86, 101, 115)),
            TextWrapping = TextWrapping.Wrap,
            Text = text
        };

    private static Border CreateRouteNoticeV1(
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

    private static Border CreateButtonSpacerV1() =>
        new() { Width = 10 };

    private static string? ReadCurrentWlanInterfaceIdV1()
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

    private bool IsExistingNetworkOperationActiveV1()
    {
        Type type = GetType();
        FieldInfo? measurementRunning = type.GetField(
            "_measurementRunning",
            BindingFlags.Instance | BindingFlags.NonPublic);
        if (measurementRunning?.FieldType == typeof(bool)
            && measurementRunning.GetValue(this) is true)
        {
            return true;
        }

        FieldInfo? observationCancellation = type.GetField(
            "_observationCancellation",
            BindingFlags.Instance | BindingFlags.NonPublic);
        return observationCancellation?.GetValue(this) is not null;
    }

    private void SetRouteComparisonRunningStateV1(bool isRunning)
    {
        if (_routeComparisonStartButtonV1 is not null)
        {
            _routeComparisonStartButtonV1.IsEnabled = !isRunning;
        }

        if (_routeComparisonCancelButtonV1 is not null)
        {
            _routeComparisonCancelButtonV1.IsEnabled = isRunning;
        }

        if (_routeInternalTargetTextBoxV1 is not null)
        {
            _routeInternalTargetTextBoxV1.IsEnabled = !isRunning;
        }

        if (_routeExternalTargetTextBoxV1 is not null)
        {
            _routeExternalTargetTextBoxV1.IsEnabled = !isRunning;
        }

        if (_routeProxyDirectiveTextBoxV1 is not null)
        {
            _routeProxyDirectiveTextBoxV1.IsEnabled = !isRunning;
        }

        if (_routeComparisonReportButtonV1 is not null)
        {
            _routeComparisonReportButtonV1.IsEnabled =
                !isRunning && _lastRouteComparisonRunV1 is not null;
        }

        SetPeerTabsEnabledV1(!isRunning);
    }

    private void SetPeerTabsEnabledV1(bool enabled)
    {
        TabControl? tabControl = FindRouteComparisonDescendantV1<
            TabControl>(this);
        if (tabControl is null || _routeComparisonTabV1 is null)
        {
            return;
        }

        if (!enabled)
        {
            _routeComparisonPeerTabStatesV1 = new Dictionary<
                TabItem,
                bool>();
            foreach (object item in tabControl.Items)
            {
                if (item is not TabItem tab
                    || ReferenceEquals(tab, _routeComparisonTabV1))
                {
                    continue;
                }

                _routeComparisonPeerTabStatesV1[tab] = tab.IsEnabled;
                tab.IsEnabled = false;
            }

            return;
        }

        if (_routeComparisonPeerTabStatesV1 is null)
        {
            return;
        }

        foreach ((TabItem tab, bool wasEnabled)
                 in _routeComparisonPeerTabStatesV1)
        {
            tab.IsEnabled = wasEnabled;
        }

        _routeComparisonPeerTabStatesV1 = null;
    }

    private void SetRouteComparisonResultV1(
        string text,
        Brush brush)
    {
        if (_routeComparisonResultTextBoxV1 is null)
        {
            return;
        }

        _routeComparisonResultTextBoxV1.Text = text;
        _routeComparisonResultTextBoxV1.Foreground = brush;
        _routeComparisonResultTextBoxV1.ScrollToHome();
    }

    private void SetRouteComparisonReportStatusV1(
        string text,
        Brush brush)
    {
        if (_routeComparisonReportStatusTextV1 is null)
        {
            return;
        }

        _routeComparisonReportStatusTextV1.Text = text;
        _routeComparisonReportStatusTextV1.Foreground = brush;
    }

    private static Brush GetRouteComparisonBrushV1(
        InternalProxyRouteComparisonRunResult run)
    {
        if (run.Status
            == InternalProxyRouteComparisonRunStatus.Completed)
        {
            return run.Comparison?.Status switch
            {
                InternalProxyRouteComparisonStatus.Ready =>
                    Brushes.DarkGreen,
                InternalProxyRouteComparisonStatus.Diverged =>
                    Brushes.DarkBlue,
                InternalProxyRouteComparisonStatus.Ambiguous =>
                    Brushes.DarkOrange,
                _ => Brushes.DarkRed
            };
        }

        return run.Status switch
        {
            InternalProxyRouteComparisonRunStatus
                .DirectPathSelected => Brushes.DarkBlue,
            InternalProxyRouteComparisonRunStatus.Canceled =>
                Brushes.DarkOrange,
            InternalProxyRouteComparisonRunStatus.InvalidInput
                or InternalProxyRouteComparisonRunStatus
                    .InternalRouteUnavailable => Brushes.DarkOrange,
            _ => Brushes.DarkRed
        };
    }

    private static string GetRouteComparisonReportDirectoryV1()
    {
        string root = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(
            root,
            "WlanLivePathTesterKO",
            "Reports",
            "InternalProxyRouteComparison");
    }

    private static T? FindRouteComparisonDescendantV1<T>(
        DependencyObject root)
        where T : DependencyObject
    {
        int childCount = VisualTreeHelper.GetChildrenCount(root);
        for (int index = 0; index < childCount; index++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(
                root,
                index);
            if (child is T match)
            {
                return match;
            }

            T? nested = FindRouteComparisonDescendantV1<T>(child);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }

    private void OnRouteComparisonWindowClosedV1(
        object? sender,
        EventArgs e)
    {
        try
        {
            _routeComparisonCancellationV1?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The operation completed while the window was closing.
        }

        SetPeerTabsEnabledV1(enabled: true);
        if (_routeComparisonClosedHookedV1)
        {
            Closed -= OnRouteComparisonWindowClosedV1;
            _routeComparisonClosedHookedV1 = false;
        }
    }
}
