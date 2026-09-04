using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WlanLivePathTester.Core.NetworkEnvironment;
using WlanLivePathTester.Windows.NetworkEnvironment;

namespace WlanLivePathTester.App;

public partial class MainWindow
{
    private Button? _readNetworkEnvironmentButton;
    private TextBlock? _networkEnvironmentResultText;
    private bool _networkEnvironmentTabAdded;

    internal void EnsureNetworkEnvironmentTab()
    {
        if (_networkEnvironmentTabAdded)
        {
            return;
        }

        TabControl? tabControl = FindVisualDescendant<TabControl>(this);
        if (tabControl is null)
        {
            return;
        }

        tabControl.Items.Insert(
            Math.Min(1, tabControl.Items.Count),
            CreateNetworkEnvironmentTab());
        _networkEnvironmentTabAdded = true;
    }

    private TabItem CreateNetworkEnvironmentTab()
    {
        _readNetworkEnvironmentButton = new Button
        {
            Content = "로컬 인터페이스 환경 확인",
            MinWidth = 200,
            Padding = new Thickness(12, 8, 12, 8),
            HorizontalAlignment = HorizontalAlignment.Left
        };
        _readNetworkEnvironmentButton.Click +=
            OnReadNetworkEnvironmentClick;

        _networkEnvironmentResultText = new TextBlock
        {
            FontFamily = new FontFamily("Consolas"),
            TextWrapping = TextWrapping.Wrap,
            Text = "아직 로컬 인터페이스 환경을 확인하지 않았습니다."
        };

        StackPanel content = new();
        content.Children.Add(new TextBlock
        {
            FontSize = 22,
            FontWeight = FontWeights.SemiBold,
            Text = "로컬 인터페이스 환경"
        });
        content.Children.Add(new TextBlock
        {
            Margin = new Thickness(0, 8, 0, 0),
            Foreground = new SolidColorBrush(Color.FromRgb(86, 101, 115)),
            TextWrapping = TextWrapping.Wrap,
            Text = "활성 Wi-Fi·유선·VPN·터널·Hyper-V·VMware·WSL 등 로컬 인터페이스를 구분하고, WLAN 관찰과 실제 다운로드 경로가 엇갈릴 가능성을 확인합니다."
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
                Text = "이 기능은 Windows의 로컬 NetworkInterface 정보만 읽습니다. DNS·HTTP·프록시 요청을 만들지 않고 IP·게이트웨이·MAC 주소 원문도 표시하지 않습니다."
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
                Text = "기본 게이트웨이가 여러 개이거나 VPN이 활성화됐다는 사실만으로 실제 경로를 확정할 수는 없습니다. 필요하면 route print 또는 Get-NetRoute 결과와 비교하십시오."
            }
        });
        content.Children.Add(new Border
        {
            Height = 14
        });
        content.Children.Add(_readNetworkEnvironmentButton);
        content.Children.Add(new Border
        {
            Margin = new Thickness(0, 18, 0, 0),
            Padding = new Thickness(16),
            CornerRadius = new CornerRadius(8),
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(216, 221, 227)),
            BorderThickness = new Thickness(1),
            Child = _networkEnvironmentResultText
        });

        return new TabItem
        {
            Header = "인터페이스 환경",
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

    private async void OnReadNetworkEnvironmentClick(
        object sender,
        RoutedEventArgs e)
    {
        if (_readNetworkEnvironmentButton is null
            || !_readNetworkEnvironmentButton.IsEnabled)
        {
            return;
        }

        _readNetworkEnvironmentButton.IsEnabled = false;
        SetNetworkEnvironmentResult(
            "로컬 인터페이스 정보를 읽고 있습니다.",
            Brushes.DarkSlateGray);

        try
        {
            LocalNetworkEnvironmentSnapshot snapshot = await Task.Run(
                LocalNetworkEnvironmentReader.ReadCurrent);
            NetworkEnvironmentSeverity highestSeverity =
                snapshot.Assessment.Findings.Any(finding =>
                    finding.Severity == NetworkEnvironmentSeverity.Warning)
                    ? NetworkEnvironmentSeverity.Warning
                    : NetworkEnvironmentSeverity.Information;
            SetNetworkEnvironmentResult(
                FormatNetworkEnvironment(snapshot),
                highestSeverity == NetworkEnvironmentSeverity.Warning
                    ? Brushes.DarkOrange
                    : Brushes.DarkGreen);
        }
        catch (Exception exception)
        {
            SetNetworkEnvironmentResult(
                $"로컬 인터페이스 환경 확인 중 오류가 발생했습니다: {exception.Message}",
                Brushes.DarkRed);
        }
        finally
        {
            _readNetworkEnvironmentButton.IsEnabled = true;
        }
    }

    private static string FormatNetworkEnvironment(
        LocalNetworkEnvironmentSnapshot snapshot)
    {
        NetworkEnvironmentAssessment assessment = snapshot.Assessment;
        StringBuilder builder = new();
        builder.AppendLine(snapshot.Message);
        builder.AppendLine($"수집 시각: {snapshot.CapturedAt.ToLocalTime():yyyy-MM-dd HH:mm:ss}");
        builder.AppendLine();
        builder.AppendLine("[요약]");
        builder.AppendLine($"전체/활성 인터페이스: {assessment.TotalAdapterCount}/{assessment.ActiveAdapterCount}");
        builder.AppendLine($"활성 Wi-Fi: {assessment.ActiveWirelessCount} · 유선: {assessment.ActiveEthernetCount} · VPN/터널: {assessment.ActiveVpnCount} · 가상: {assessment.ActiveVirtualCount}");
        builder.AppendLine($"활성 기본 게이트웨이 보유 인터페이스: {assessment.ActiveDefaultGatewayCount}");
        builder.AppendLine($"경로 선택 혼재 가능성: {(assessment.RouteSelectionMayBeAmbiguous ? "있음" : "낮음")}");
        builder.AppendLine($"단일 물리 Wi-Fi 후보: {assessment.PreferredWirelessDisplayName ?? "확정하지 못함"}");

        builder.AppendLine();
        builder.AppendLine("[판정]");
        foreach (NetworkEnvironmentFinding finding in assessment.Findings)
        {
            builder.AppendLine($"- {FormatNetworkSeverity(finding.Severity)} {finding.Title} ({finding.Code})");
            builder.AppendLine($"  근거: {finding.Evidence}");
            builder.AppendLine($"  해석: {finding.Interpretation}");
            builder.AppendLine($"  다음 확인: {finding.NextStep}");
        }

        builder.AppendLine();
        builder.AppendLine("[인터페이스 목록]");
        LocalNetworkAdapterSnapshot[] ordered = snapshot.Adapters
            .OrderByDescending(adapter => adapter.IsUp)
            .ThenByDescending(adapter => adapter.HasDefaultGateway)
            .ThenBy(adapter => adapter.Category)
            .ThenBy(adapter => adapter.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        for (int index = 0; index < ordered.Length; index++)
        {
            LocalNetworkAdapterSnapshot adapter = ordered[index];
            builder.AppendLine($"{index + 1}. {adapter.DisplayName}");
            builder.AppendLine($"   유형: {FormatNetworkCategory(adapter.Category)} / Native: {adapter.NativeInterfaceType} / 상태: {FormatNetworkState(adapter.OperationalState)}");
            builder.AppendLine($"   설명: {adapter.Description}");
            builder.AppendLine($"   링크 속도: {FormatNetworkSpeed(adapter.SpeedBitsPerSecond)} / 기본 게이트웨이: {(adapter.HasDefaultGateway ? $"있음({adapter.GatewayCount})" : "없음")}");
            builder.AppendLine($"   주소 계열: IPv4 {(adapter.HasIpv4 ? "있음" : "없음")}, IPv6 {(adapter.HasIpv6 ? "있음" : "없음")} / 주소 개수: {adapter.UnicastAddressCount}");
            builder.AppendLine($"   분류: {(adapter.IsVpn ? "VPN/터널 " : string.Empty)}{(adapter.IsVirtual ? "가상" : "물리 후보")}");
            if (!string.IsNullOrWhiteSpace(adapter.ReadError))
            {
                builder.AppendLine($"   부분 제한: {adapter.ReadError}");
            }
        }

        return builder.ToString().TrimEnd();
    }

    private static string FormatNetworkSeverity(
        NetworkEnvironmentSeverity severity) =>
        severity == NetworkEnvironmentSeverity.Warning
            ? "[주의]"
            : "[정보]";

    private static string FormatNetworkCategory(
        NetworkAdapterCategory category) =>
        category switch
        {
            NetworkAdapterCategory.Wireless => "Wi-Fi",
            NetworkAdapterCategory.Ethernet => "유선",
            NetworkAdapterCategory.Tunnel => "터널",
            NetworkAdapterCategory.Loopback => "루프백",
            _ => "기타"
        };

    private static string FormatNetworkState(
        NetworkAdapterOperationalState state) =>
        state switch
        {
            NetworkAdapterOperationalState.Up => "Up",
            NetworkAdapterOperationalState.Down => "Down",
            NetworkAdapterOperationalState.Dormant => "Dormant",
            NetworkAdapterOperationalState.LowerLayerDown => "LowerLayerDown",
            NetworkAdapterOperationalState.Testing => "Testing",
            _ => "Unknown"
        };

    private static string FormatNetworkSpeed(long? bitsPerSecond)
    {
        if (!bitsPerSecond.HasValue || bitsPerSecond.Value <= 0)
        {
            return "확인 불가";
        }

        double mbps = bitsPerSecond.Value / 1_000_000d;
        return mbps >= 1000
            ? $"{mbps / 1000:F1} Gbps"
            : $"{mbps:F0} Mbps";
    }

    private void SetNetworkEnvironmentResult(
        string text,
        Brush brush)
    {
        if (_networkEnvironmentResultText is null)
        {
            return;
        }

        _networkEnvironmentResultText.Text = text;
        _networkEnvironmentResultText.Foreground = brush;
    }
}
