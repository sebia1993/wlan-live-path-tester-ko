using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WlanLivePathTester.Core.Adapters;
using WlanLivePathTester.Core.Models;
using WlanLivePathTester.Windows.Adapters;
using WlanLivePathTester.Windows.Wlan;

namespace WlanLivePathTester.App;

public partial class MainWindow
{
    private Button? _refreshNetworkAdapterDiagnosticsButton;
    private TextBlock? _networkAdapterSelectionText;
    private TextBlock? _networkAdapterWarningText;
    private TextBlock? _networkAdapterInventoryText;
    private bool _networkAdapterDiagnosticsTabAdded;
    private string? _recommendedWirelessAdapterId;

    internal void EnsureNetworkAdapterDiagnosticsTab()
    {
        if (_networkAdapterDiagnosticsTabAdded)
        {
            return;
        }

        TabControl? tabControl = FindVisualDescendant<TabControl>(this);
        if (tabControl is null)
        {
            return;
        }

        tabControl.Items.Add(CreateNetworkAdapterDiagnosticsTab());
        _networkAdapterDiagnosticsTabAdded = true;
        RefreshNetworkAdapterDiagnostics();
    }

    private TabItem CreateNetworkAdapterDiagnosticsTab()
    {
        _refreshNetworkAdapterDiagnosticsButton = new Button
        {
            Content = "어댑터 목록 새로고침",
            MinWidth = 170,
            Padding = new Thickness(12, 8, 12, 8),
            HorizontalAlignment = HorizontalAlignment.Left
        };
        _refreshNetworkAdapterDiagnosticsButton.Click +=
            OnRefreshNetworkAdapterDiagnosticsClick;

        _networkAdapterSelectionText = new TextBlock
        {
            FontFamily = new FontFamily("Consolas"),
            TextWrapping = TextWrapping.Wrap,
            Text = "아직 어댑터 상태를 확인하지 않았습니다."
        };
        _networkAdapterWarningText = new TextBlock
        {
            FontFamily = new FontFamily("Consolas"),
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brushes.DarkOrange,
            Text = "추가 경고 없음"
        };
        _networkAdapterInventoryText = new TextBlock
        {
            FontFamily = new FontFamily("Consolas"),
            TextWrapping = TextWrapping.NoWrap,
            Text = "어댑터 목록 없음"
        };

        StackPanel content = new();
        content.Children.Add(new TextBlock
        {
            FontSize = 22,
            FontWeight = FontWeights.SemiBold,
            Text = "다중 NIC · VPN · 가상 어댑터 진단"
        });
        content.Children.Add(new TextBlock
        {
            Margin = new Thickness(0, 8, 0, 0),
            Foreground = new SolidColorBrush(Color.FromRgb(86, 101, 115)),
            TextWrapping = TextWrapping.Wrap,
            Text = "Windows의 로컬 인터페이스 목록과 현재 Native WLAN 연결 GUID를 비교해 물리 Wi-Fi, 유선, VPN·터널, Hyper-V·VMware·WSL, Wi-Fi Direct 후보를 분류합니다. 이 기능은 DNS·HTTP·PAC·WPAD 요청을 만들지 않습니다."
        });
        content.Children.Add(new Border
        {
            Margin = new Thickness(0, 16, 0, 0),
            Padding = new Thickness(14),
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(Color.FromRgb(255, 248, 231)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(232, 206, 138)),
            BorderThickness = new Thickness(1),
            Child = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Text = "동일 우선순위의 활성 물리 Wi-Fi가 여러 개면 임의로 첫 번째 어댑터를 선택하지 않습니다. Native WLAN GUID를 직접 읽지 못하면 연결 identity의 설명 완전 일치로 한 번 보완하며, 중복 후보는 선택하지 않습니다."
            }
        });
        content.Children.Add(_refreshNetworkAdapterDiagnosticsButton);
        content.Children.Add(CreateAdapterResultCard(
            "권장 Wi-Fi 선택",
            _networkAdapterSelectionText,
            marginTop: 18));
        content.Children.Add(CreateAdapterResultCard(
            "경고",
            _networkAdapterWarningText,
            marginTop: 12));

        StackPanel inventoryPanel = new();
        inventoryPanel.Children.Add(new TextBlock
        {
            FontSize = 17,
            FontWeight = FontWeights.SemiBold,
            Text = "로컬 어댑터 인벤토리"
        });
        inventoryPanel.Children.Add(new TextBlock
        {
            Margin = new Thickness(0, 5, 0, 10),
            Foreground = new SolidColorBrush(Color.FromRgb(86, 101, 115)),
            TextWrapping = TextWrapping.Wrap,
            Text = "IP·MAC·게이트웨이 주소 원문은 표시하지 않습니다. ID는 SHA-256 앞 10자리 지문으로만 표시합니다."
        });
        inventoryPanel.Children.Add(new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            MaxHeight = 480,
            Content = _networkAdapterInventoryText
        });

        content.Children.Add(new Border
        {
            Margin = new Thickness(0, 12, 0, 0),
            Padding = new Thickness(16),
            CornerRadius = new CornerRadius(8),
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(216, 221, 227)),
            BorderThickness = new Thickness(1),
            Child = inventoryPanel
        });

        return new TabItem
        {
            Header = "어댑터 진단",
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

    private static Border CreateAdapterResultCard(
        string title,
        TextBlock content,
        double marginTop)
    {
        StackPanel panel = new();
        panel.Children.Add(new TextBlock
        {
            FontSize = 17,
            FontWeight = FontWeights.SemiBold,
            Text = title
        });
        content.Margin = new Thickness(0, 8, 0, 0);
        panel.Children.Add(content);

        return new Border
        {
            Margin = new Thickness(0, marginTop, 0, 0),
            Padding = new Thickness(16),
            CornerRadius = new CornerRadius(8),
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(216, 221, 227)),
            BorderThickness = new Thickness(1),
            Child = panel
        };
    }

    private void OnRefreshNetworkAdapterDiagnosticsClick(
        object sender,
        RoutedEventArgs e)
    {
        if (_measurementRunning || _observationCancellation is not null)
        {
            SetNetworkAdapterSelectionText(
                "측정 또는 브라우저 관찰이 진행 중입니다. 현재 인터페이스 기준이 바뀌지 않도록 작업이 끝난 뒤 새로고침하십시오.",
                Brushes.DarkOrange);
            return;
        }

        RefreshNetworkAdapterDiagnostics();
    }

    private void RefreshNetworkAdapterDiagnostics()
    {
        if (_networkAdapterSelectionText is null
            || _networkAdapterWarningText is null
            || _networkAdapterInventoryText is null)
        {
            return;
        }

        WlanReadResult wlanRead = NativeWlanReader.ReadCurrent();
        WlanInterfaceIdentityReadResult identityRead =
            WlanInterfaceIdentityReader.ReadCurrent();
        WlanSnapshot? connectedWlan =
            WlanInterfaceIdentityReader.AttachIdentity(
                wlanRead.FirstConnectedInterface,
                identityRead);
        NetworkAdapterInventoryReadResult inventoryRead =
            NetworkAdapterInventoryReader.Read(
                connectedWlan?.InterfaceId);
        WirelessAdapterSelectionResult selection =
            NetworkAdapterSelector.Select(inventoryRead.Adapters);

        _recommendedWirelessAdapterId =
            selection.Selected?.Candidate.Id;
        SetNetworkAdapterSelectionText(
            FormatAdapterSelection(
                selection,
                connectedWlan,
                identityRead),
            selection.Status == WirelessAdapterSelectionStatus.Selected
                ? Brushes.DarkGreen
                : selection.Status == WirelessAdapterSelectionStatus.Ambiguous
                    ? Brushes.DarkOrange
                    : Brushes.DarkRed);

        List<string> warnings =
        [
            .. inventoryRead.Warnings,
            .. selection.Warnings
        ];
        if (!identityRead.IsSuccess)
        {
            warnings.Add(
                "WLAN identity 목록을 읽지 못해 Native WLAN GUID 우선순위를 적용하지 못했습니다. 설명·상태 근거만 사용했습니다.");
        }
        else if (connectedWlan is not null
                 && string.IsNullOrWhiteSpace(connectedWlan.InterfaceId))
        {
            warnings.Add(
                "연결된 Native WLAN과 정확히 하나의 identity를 대응시키지 못했습니다. 다중 Wi-Fi 환경에서는 선택 결과를 직접 확인하십시오.");
        }

        _networkAdapterWarningText.Text = warnings.Count == 0
            ? "추가 경고 없음"
            : string.Join(
                Environment.NewLine,
                warnings.Select((warning, index) =>
                    $"{index + 1}. {warning}"));
        _networkAdapterWarningText.Foreground = warnings.Count == 0
            ? Brushes.DarkGreen
            : Brushes.DarkOrange;
        _networkAdapterInventoryText.Text =
            FormatAdapterInventory(selection.Inventory);
    }

    private static string FormatAdapterSelection(
        WirelessAdapterSelectionResult selection,
        WlanSnapshot? connectedWlan,
        WlanInterfaceIdentityReadResult identityRead)
    {
        StringBuilder builder = new();
        builder.AppendLine($"상태: {FormatSelectionStatus(selection.Status)}");
        builder.AppendLine(selection.Message);

        if (selection.Selected is ClassifiedNetworkAdapter selected)
        {
            builder.AppendLine($"권장 어댑터: {SafeAdapterName(selected.Candidate)}");
            builder.AppendLine($"로컬 ID 지문: {Fingerprint(selected.Candidate.Id)}");
            builder.AppendLine($"점수: {selected.WirelessSelectionScore}");
            builder.AppendLine($"Native WLAN 현재 연결 일치: {(selected.Candidate.IsNativeWlanConnected ? "예" : "아니요")}");
        }
        else if (selection.Candidates.Count > 0)
        {
            builder.AppendLine("후보:");
            foreach (ClassifiedNetworkAdapter candidate in selection.Candidates)
            {
                builder.AppendLine(
                    $"- {SafeAdapterName(candidate.Candidate)} · {Fingerprint(candidate.Candidate.Id)} · 점수 {candidate.WirelessSelectionScore}");
            }
        }

        builder.AppendLine($"WLAN identity 조회: {(identityRead.IsSuccess ? "성공" : "제한")} · 항목 {identityRead.Interfaces.Count}개");
        if (connectedWlan is not null)
        {
            builder.AppendLine($"Native WLAN 연결 상태: {(connectedWlan.IsConnected ? "연결됨" : "연결 안 됨")}");
            builder.AppendLine($"Native WLAN ID 지문: {Fingerprint(connectedWlan.InterfaceId)}");
        }
        else
        {
            builder.AppendLine("Native WLAN 현재 연결: 확인되지 않음");
        }

        return builder.ToString().TrimEnd();
    }

    private static string FormatAdapterInventory(
        IReadOnlyList<ClassifiedNetworkAdapter> inventory)
    {
        if (inventory.Count == 0)
        {
            return "Windows에서 읽은 어댑터가 없습니다.";
        }

        StringBuilder builder = new();
        builder.AppendLine("No  Role                 State       Type                 Score  GW  IP  WLAN  ID         Name");
        builder.AppendLine(new string('-', 116));

        for (int index = 0; index < inventory.Count; index++)
        {
            ClassifiedNetworkAdapter item = inventory[index];
            NetworkAdapterCandidate adapter = item.Candidate;
            string score = item.IsEligiblePhysicalWireless
                ? item.WirelessSelectionScore.ToString()
                : "-";
            builder.AppendLine(string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "{0,-3} {1,-20} {2,-11} {3,-20} {4,5}  {5,-2}  {6,-2}  {7,-4}  {8,-10} {9}",
                index + 1,
                FormatAdapterRole(item.Role),
                adapter.OperationalStatus,
                adapter.InterfaceType,
                score,
                adapter.HasDefaultGateway ? "Y" : "-",
                adapter.HasUnicastAddress ? "Y" : "-",
                adapter.IsNativeWlanConnected ? "Y" : "-",
                Fingerprint(adapter.Id),
                SafeAdapterName(adapter)));
        }

        return builder.ToString().TrimEnd();
    }

    private static string FormatSelectionStatus(
        WirelessAdapterSelectionStatus status) =>
        status switch
        {
            WirelessAdapterSelectionStatus.Selected => "선택됨",
            WirelessAdapterSelectionStatus.Ambiguous => "모호함",
            WirelessAdapterSelectionStatus.NoConnectedPhysicalWireless =>
                "연결된 물리 Wi-Fi 없음",
            _ => "물리 Wi-Fi 후보 없음"
        };

    private static string FormatAdapterRole(NetworkAdapterRole role) =>
        role switch
        {
            NetworkAdapterRole.PhysicalWireless => "Physical Wi-Fi",
            NetworkAdapterRole.PhysicalEthernet => "Physical Ethernet",
            NetworkAdapterRole.WiFiDirectOrHosted => "Wi-Fi Direct/SoftAP",
            NetworkAdapterRole.VpnOrTunnel => "VPN/Tunnel",
            NetworkAdapterRole.VirtualSwitch => "Virtual Switch",
            NetworkAdapterRole.Bluetooth => "Bluetooth",
            NetworkAdapterRole.Loopback => "Loopback",
            NetworkAdapterRole.OtherVirtual => "Other Virtual",
            _ => "Unknown"
        };

    private static string SafeAdapterName(NetworkAdapterCandidate adapter)
    {
        string name = string.IsNullOrWhiteSpace(adapter.Name)
            ? adapter.Description
            : adapter.Name;
        return string.IsNullOrWhiteSpace(name)
            ? "이름 없는 어댑터"
            : name.Replace('\r', ' ').Replace('\n', ' ').Trim();
    }

    private static string Fingerprint(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "없음";
        }

        byte[] hash = SHA256.HashData(
            Encoding.UTF8.GetBytes(value.Trim()));
        return Convert.ToHexString(hash)[..10].ToLowerInvariant();
    }

    private void SetNetworkAdapterSelectionText(
        string text,
        Brush brush)
    {
        if (_networkAdapterSelectionText is null)
        {
            return;
        }

        _networkAdapterSelectionText.Text = text;
        _networkAdapterSelectionText.Foreground = brush;
    }
}
