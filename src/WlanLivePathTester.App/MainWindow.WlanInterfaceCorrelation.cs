using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WlanLivePathTester.Core.Models;
using WlanLivePathTester.Core.NetworkEnvironment;
using WlanLivePathTester.Windows.NetworkEnvironment;
using WlanLivePathTester.Windows.Wlan;

namespace WlanLivePathTester.App;

public partial class MainWindow
{
    private Button? _correlateWlanInterfaceButton;
    private TextBlock? _wlanInterfaceCorrelationResultText;
    private bool _wlanInterfaceCorrelationTabAdded;

    internal void EnsureWlanInterfaceCorrelationTab()
    {
        if (_wlanInterfaceCorrelationTabAdded)
        {
            return;
        }

        TabControl? tabControl = FindVisualDescendant<TabControl>(this);
        if (tabControl is null)
        {
            return;
        }

        tabControl.Items.Insert(
            Math.Min(2, tabControl.Items.Count),
            CreateWlanInterfaceCorrelationTab());
        _wlanInterfaceCorrelationTabAdded = true;
    }

    private TabItem CreateWlanInterfaceCorrelationTab()
    {
        _correlateWlanInterfaceButton = new Button
        {
            Content = "WLAN과 로컬 NIC 대응 확인",
            MinWidth = 210,
            Padding = new Thickness(12, 8, 12, 8),
            HorizontalAlignment = HorizontalAlignment.Left
        };
        _correlateWlanInterfaceButton.Click +=
            OnCorrelateWlanInterfaceClick;

        _wlanInterfaceCorrelationResultText = new TextBlock
        {
            FontFamily = new FontFamily("Consolas"),
            TextWrapping = TextWrapping.Wrap,
            Text = "아직 Native WLAN과 로컬 NIC의 대응을 확인하지 않았습니다."
        };

        StackPanel content = new();
        content.Children.Add(new TextBlock
        {
            FontSize = 22,
            FontWeight = FontWeights.SemiBold,
            Text = "WLAN 인터페이스 대응"
        });
        content.Children.Add(new TextBlock
        {
            Margin = new Thickness(0, 8, 0, 0),
            Foreground = new SolidColorBrush(Color.FromRgb(86, 101, 115)),
            TextWrapping = TextWrapping.Wrap,
            Text = "Native WLAN API가 반환한 연결 인터페이스와 Windows NetworkInterface 목록의 Wi-Fi NIC가 같은 장치인지 확인합니다. 다중 내장·USB 무선 NIC 환경에서 관찰 대상을 잘못 해석하는 문제를 줄입니다."
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
                Text = "먼저 인터페이스 GUID를 정확히 비교하고, GUID를 사용할 수 없을 때만 무선 NIC 설명의 완전 일치를 보조 기준으로 사용합니다. GUID 원문은 화면에 표시하거나 보고서에 저장하지 않습니다."
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
                Text = "NIC 대응 성공은 외부 다운로드가 반드시 Wi-Fi로 나간다는 뜻이 아닙니다. 유선·VPN·다중 기본 게이트웨이가 있으면 목적지별 Windows 라우팅을 별도로 확인해야 합니다."
            }
        });
        content.Children.Add(new Border { Height = 14 });
        content.Children.Add(_correlateWlanInterfaceButton);
        content.Children.Add(new Border
        {
            Margin = new Thickness(0, 18, 0, 0),
            Padding = new Thickness(16),
            CornerRadius = new CornerRadius(8),
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(216, 221, 227)),
            BorderThickness = new Thickness(1),
            Child = _wlanInterfaceCorrelationResultText
        });

        return new TabItem
        {
            Header = "WLAN NIC 대응",
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

    private async void OnCorrelateWlanInterfaceClick(
        object sender,
        RoutedEventArgs e)
    {
        if (_correlateWlanInterfaceButton is null
            || !_correlateWlanInterfaceButton.IsEnabled)
        {
            return;
        }

        _correlateWlanInterfaceButton.IsEnabled = false;
        SetWlanInterfaceCorrelationResult(
            "Native WLAN ID와 로컬 인터페이스 정보를 읽고 있습니다.",
            Brushes.DarkSlateGray);

        try
        {
            (
                WlanReadResult Wlan,
                WlanInterfaceIdentityReadResult Identities,
                LocalNetworkEnvironmentSnapshot Network
            ) data = await Task.Run(() => (
                NativeWlanReader.ReadCurrent(),
                WlanInterfaceIdentityReader.ReadCurrent(),
                LocalNetworkEnvironmentReader.ReadCurrent()));

            WlanSnapshot? connected =
                WlanInterfaceIdentityReader.AttachIdentity(
                    data.Wlan.FirstConnectedInterface,
                    data.Identities);
            WlanInterfaceCorrelationResult correlation =
                WlanInterfaceCorrelator.Correlate(
                    connected,
                    data.Network.Adapters);

            SetWlanInterfaceCorrelationResult(
                FormatWlanInterfaceCorrelation(
                    data.Wlan,
                    data.Identities,
                    data.Network,
                    correlation),
                correlation.IsMatched && correlation.Warnings.Count == 0
                    ? Brushes.DarkGreen
                    : correlation.IsMatched
                        ? Brushes.DarkOrange
                        : Brushes.DarkRed);
        }
        catch (Exception exception)
        {
            SetWlanInterfaceCorrelationResult(
                $"WLAN 인터페이스 대응 확인 중 오류가 발생했습니다: {exception.Message}",
                Brushes.DarkRed);
        }
        finally
        {
            _correlateWlanInterfaceButton.IsEnabled = true;
        }
    }

    private static string FormatWlanInterfaceCorrelation(
        WlanReadResult wlanRead,
        WlanInterfaceIdentityReadResult identityRead,
        LocalNetworkEnvironmentSnapshot network,
        WlanInterfaceCorrelationResult correlation)
    {
        StringBuilder builder = new();
        builder.AppendLine($"Native WLAN 상태: {wlanRead.Status}");
        builder.AppendLine($"WLAN ID 목록: {(identityRead.IsSuccess ? "확인 성공" : "확인 실패")} · 항목 {identityRead.Interfaces.Count}개");
        builder.AppendLine($"로컬 인터페이스: {network.Adapters.Count}개 / 활성 Wi-Fi: {network.Assessment.ActiveWirelessCount}개");
        builder.AppendLine($"대응 상태: {FormatCorrelationStatus(correlation.Status)}");
        builder.AppendLine(correlation.Message);

        if (!identityRead.IsSuccess)
        {
            builder.AppendLine($"GUID 조회 제한: {identityRead.Message}");
        }

        if (correlation.IsMatched)
        {
            builder.AppendLine();
            builder.AppendLine("[대응된 로컬 NIC]");
            builder.AppendLine($"이름: {correlation.MatchedDisplayName ?? "확인 불가"}");
            builder.AppendLine($"상태: {FormatNullableBoolean(correlation.MatchedAdapterIsUp, "Up", "Up 아님")}");
            builder.AppendLine($"기본 게이트웨이: {FormatNullableBoolean(correlation.MatchedAdapterHasDefaultGateway, "있음", "없음")}");
            builder.AppendLine($"가상 분류: {FormatNullableBoolean(correlation.MatchedAdapterIsVirtual, "예", "아니요")}");
            builder.AppendLine($"VPN/터널 분류: {FormatNullableBoolean(correlation.MatchedAdapterIsVpn, "예", "아니요")}");
        }

        if (correlation.Warnings.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("[주의]");
            foreach (string warning in correlation.Warnings)
            {
                builder.AppendLine($"- {warning}");
            }
        }

        if (network.Assessment.RouteSelectionMayBeAmbiguous)
        {
            builder.AppendLine();
            builder.AppendLine("[라우팅 참고]");
            builder.AppendLine("활성 유선·VPN·다중 기본 게이트웨이 또는 다중 Wi-Fi 때문에 실제 요청 경로가 혼재할 수 있습니다.");
            builder.AppendLine("route print 또는 Get-NetRoute로 목적지별 경로를 추가 확인하십시오.");
        }

        return builder.ToString().TrimEnd();
    }

    private static string FormatCorrelationStatus(
        WlanInterfaceCorrelationStatus status) =>
        status switch
        {
            WlanInterfaceCorrelationStatus.MatchedByInterfaceId =>
                "GUID 정확 일치",
            WlanInterfaceCorrelationStatus.MatchedByDescription =>
                "설명 완전 일치",
            WlanInterfaceCorrelationStatus.NoConnectedWlan =>
                "연결 WLAN 없음",
            WlanInterfaceCorrelationStatus.MultipleMatches =>
                "중복 후보",
            _ => "일치 항목 없음"
        };

    private static string FormatNullableBoolean(
        bool? value,
        string trueText,
        string falseText) =>
        value switch
        {
            true => trueText,
            false => falseText,
            null => "확인 불가"
        };

    private void SetWlanInterfaceCorrelationResult(
        string text,
        Brush brush)
    {
        if (_wlanInterfaceCorrelationResultText is null)
        {
            return;
        }

        _wlanInterfaceCorrelationResultText.Text = text;
        _wlanInterfaceCorrelationResultText.Foreground = brush;
    }
}
