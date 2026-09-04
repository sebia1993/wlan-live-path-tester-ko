using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WlanLivePathTester.Core.Routing;
using WlanLivePathTester.Windows.Routing;

namespace WlanLivePathTester.App;

public partial class MainWindow
{
    private Button? _compareRoutePathsButton;
    private TextBlock? _routePathComparisonResultText;
    private bool _routePathComparisonTabAdded;

    internal void EnsureRoutePathComparisonTab()
    {
        if (_routePathComparisonTabAdded)
        {
            return;
        }

        TabControl? tabControl = FindVisualDescendant<TabControl>(this);
        if (tabControl is null)
        {
            return;
        }

        tabControl.Items.Add(CreateRoutePathComparisonTab());
        _routePathComparisonTabAdded = true;
    }

    private TabItem CreateRoutePathComparisonTab()
    {
        _compareRoutePathsButton = new Button
        {
            Content = "현재 라우팅 이력 비교",
            MinWidth = 180,
            Padding = new Thickness(12, 8, 12, 8),
            HorizontalAlignment = HorizontalAlignment.Left
        };
        _compareRoutePathsButton.Click +=
            OnCompareRoutePathsClick;

        _routePathComparisonResultText = new TextBlock
        {
            FontFamily = new FontFamily("Consolas"),
            TextWrapping = TextWrapping.Wrap,
            Text = "아직 내부 DIRECT와 프록시 엔드포인트 경로를 비교하지 않았습니다."
        };

        StackPanel content = new();
        content.Children.Add(new TextBlock
        {
            FontSize = 22,
            FontWeight = FontWeights.SemiBold,
            Text = "내부 · 프록시 로컬 경로 비교"
        });
        content.Children.Add(new TextBlock
        {
            Margin = new Thickness(0, 8, 0, 0),
            Foreground = new SolidColorBrush(Color.FromRgb(86, 101, 115)),
            TextWrapping = TextWrapping.Wrap,
            Text = "현재 앱 메모리에 저장된 가장 최근 내부 DIRECT·프록시 엔드포인트·외부 사이트 참고 경로를 비교합니다. 내부와 외부 프록시 경로가 같은 Wi-Fi를 쓰는지, 유선·VPN·다른 Wi-Fi로 분리되는지 고정 규칙으로 판정합니다."
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
                Text = "이 비교는 이미 수집된 메모리 이력만 사용합니다. 버튼을 눌러도 DNS·HTTP·PAC/WPAD·외부 API 요청이나 업로드가 새로 발생하지 않습니다. 인터페이스는 짧은 SHA-256 지문과 범주로만 비교합니다."
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
                Text = "외부 사이트 참고 경로는 회사 프록시 환경의 실제 HTTP 연결 경로가 아닐 수 있습니다. 정확한 외부 로컬 경로 비교에는 프록시 엔드포인트 목적의 라우팅 근거가 필요합니다."
            }
        });
        content.Children.Add(new Border { Height = 14 });
        content.Children.Add(_compareRoutePathsButton);
        content.Children.Add(new Border
        {
            Margin = new Thickness(0, 18, 0, 0),
            Padding = new Thickness(16),
            CornerRadius = new CornerRadius(8),
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(216, 221, 227)),
            BorderThickness = new Thickness(1),
            Child = _routePathComparisonResultText
        });

        return new TabItem
        {
            Header = "경로 비교",
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

    private void OnCompareRoutePathsClick(
        object sender,
        RoutedEventArgs e)
    {
        if (_compareRoutePathsButton is null
            || !_compareRoutePathsButton.IsEnabled)
        {
            return;
        }

        if (_measurementRunning
            || _observationCancellation is not null
            || _routeEvidenceCancellation is not null)
        {
            SetRoutePathComparisonResult(
                "측정·브라우저 관찰 또는 라우팅 확인이 진행 중입니다. 완료하거나 중지한 뒤 현재 이력을 비교하십시오.",
                Brushes.DarkOrange);
            return;
        }

        IReadOnlyList<DestinationRouteEvidence> history =
            RouteEvidenceResultHistory.Snapshot();
        RoutePathComparisonResult result =
            RoutePathComparisonEvaluator.Evaluate(history);
        SetRoutePathComparisonResult(
            FormatRoutePathComparison(result),
            result.Status switch
            {
                RoutePathComparisonStatus.Ready => Brushes.DarkGreen,
                RoutePathComparisonStatus.Incomplete
                    or RoutePathComparisonStatus.Ambiguous
                    => Brushes.DarkOrange,
                _ => Brushes.DarkRed
            });
    }

    private static string FormatRoutePathComparison(
        RoutePathComparisonResult result)
    {
        StringBuilder builder = new();
        builder.AppendLine($"비교 시각: {result.EvaluatedAt.ToLocalTime():yyyy-MM-dd HH:mm:ss}");
        builder.AppendLine($"상태: {FormatRouteComparisonStatus(result.Status)}");
        builder.AppendLine(result.Message);
        builder.AppendLine();
        builder.AppendLine("[최근 경로 근거]");
        AppendRouteComparisonPoint(
            builder,
            "내부 DIRECT",
            result.InternalDirect);
        AppendRouteComparisonPoint(
            builder,
            "프록시 엔드포인트",
            result.ProxyEndpoint);
        AppendRouteComparisonPoint(
            builder,
            "외부 사이트 참고",
            result.ExternalReference);

        builder.AppendLine();
        builder.AppendLine("[판정]");
        if (result.Findings.Count == 0)
        {
            builder.AppendLine("- 별도 판정 없음");
        }
        else
        {
            foreach (RoutePathComparisonFinding finding in result.Findings)
            {
                builder.AppendLine($"- {FormatRouteComparisonSeverity(finding.Severity)} {finding.Title} ({finding.Code})");
                builder.AppendLine($"  근거: {finding.Evidence}");
                builder.AppendLine($"  해석: {finding.Interpretation}");
                builder.AppendLine($"  다음 확인: {finding.NextStep}");
            }
        }

        builder.AppendLine();
        builder.AppendLine("이 비교는 현재 메모리 이력만 사용했으며 새 네트워크 요청을 수행하지 않았습니다.");
        return builder.ToString().TrimEnd();
    }

    private static void AppendRouteComparisonPoint(
        StringBuilder builder,
        string label,
        RoutePathComparisonPoint? point)
    {
        if (point is null)
        {
            builder.AppendLine($"- {label}: 근거 없음");
            return;
        }

        builder.AppendLine(
            $"- {label}: Route={point.RouteStatus}, WLAN={point.WlanCorrelationStatus}, Category={point.InterfaceCategory ?? "없음"}, ID={point.InterfaceFingerprint ?? "없음"}, VPN={FormatNullableFlag(point.IsVpn)}, Virtual={FormatNullableFlag(point.IsVirtual)}, Warnings={point.WarningCount}, Captured={point.CapturedAt.ToLocalTime():HH:mm:ss}");
    }

    private static string FormatNullableFlag(bool? value) =>
        value switch
        {
            true => "Y",
            false => "N",
            null => "?"
        };

    private static string FormatRouteComparisonStatus(
        RoutePathComparisonStatus status) =>
        status switch
        {
            RoutePathComparisonStatus.Ready => "비교 가능",
            RoutePathComparisonStatus.Incomplete => "근거 부족",
            RoutePathComparisonStatus.Ambiguous => "경로 미확정",
            RoutePathComparisonStatus.Diverged => "인터페이스 분리",
            _ => status.ToString()
        };

    private static string FormatRouteComparisonSeverity(
        RoutePathComparisonSeverity severity) =>
        severity == RoutePathComparisonSeverity.Warning
            ? "[주의]"
            : "[정보]";

    private void SetRoutePathComparisonResult(
        string text,
        Brush brush)
    {
        if (_routePathComparisonResultText is null)
        {
            return;
        }

        _routePathComparisonResultText.Text = text;
        _routePathComparisonResultText.Foreground = brush;
    }
}
