using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WlanLivePathTester.Core.Guidance;
using WlanLivePathTester.Core.Models;
using WlanLivePathTester.Core.Operations;
using WlanLivePathTester.Windows.Measurements;

namespace WlanLivePathTester.App;

public partial class MainWindow
{
    private TextBlock? _connectionBasicsText;
    private TextBlock? _beginnerSummaryText;
    private string _connectionBasicsSnapshot = "미실행 — IP·게이트웨이·DNS 설정을 아직 확인하지 않았습니다.";
    private bool _changingSymptom;
    private int _selectedSymptom;
    internal Func<CancellationToken, Task<string>> CollectConnectionBasics { get; set; } = token => Task.Run(() =>
    {
        List<LocalConnectionEvidence> evidence = [];
        foreach (NetworkInterface adapter in NetworkInterface.GetAllNetworkInterfaces())
        {
            token.ThrowIfCancellationRequested();
            if (adapter.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
            string kind = adapter.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 ? "Wi-Fi"
                : adapter.NetworkInterfaceType == NetworkInterfaceType.Ethernet ? "Ethernet / 가상 어댑터 가능" : "VPN·터널·기타";
            bool up = adapter.OperationalStatus == OperationalStatus.Up;
            try
            {
                IPInterfaceProperties properties = adapter.GetIPProperties();
                IPAddress[] addresses = properties.UnicastAddresses.Select(a => a.Address).ToArray();
                evidence.Add(new(kind, up, true, addresses.Any(IsUsableAddress), addresses.Any(IsLinkLocalAddress),
                    properties.GatewayAddresses.Any(a => !a.Address.Equals(IPAddress.Any) && !a.Address.Equals(IPAddress.IPv6Any)),
                    properties.DnsAddresses.Count > 0));
            }
            catch (NetworkInformationException) { evidence.Add(new(kind, up, false, false, false, false, false)); }
        }
        return $"수집 시각: {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss}\n" + BeginnerConnectionSummary.Render(evidence);
    }, token);

    private static bool IsLinkLocalAddress(IPAddress address) => address.IsIPv6LinkLocal
        || (address.AddressFamily == AddressFamily.InterNetwork && address.GetAddressBytes()[0] == 169 && address.GetAddressBytes()[1] == 254);
    private static bool IsUsableAddress(IPAddress address) => !IPAddress.IsLoopback(address)
        && !address.Equals(IPAddress.Any) && !address.Equals(IPAddress.IPv6Any)
        && !address.IsIPv6Multicast && !IsLinkLocalAddress(address);

    private void InitializeBeginnerGuidance()
    {
        _changingSymptom = true;
        GuidedSymptom.SelectedIndex = 0;
        _changingSymptom = false;
    }

    private void OnGuidedSymptomChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_changingSymptom || GuidedNextAction is null) return;
        if (!CanNavigateGuided())
        {
            _changingSymptom = true;
            GuidedSymptom.SelectedIndex = _selectedSymptom;
            _changingSymptom = false;
            return;
        }
        _selectedSymptom = GuidedSymptom.SelectedIndex;
        NavigateGuidedStep(_selectedSymptom == 2 ? 1 : 0);
    }

    private string SymptomNextAction() => _selectedSymptom switch
    {
        1 => "지금 할 일: 무선 연결 상태 확인 → IP 설정 확인. 연결 실패의 인증 원인은 Windows 오류와 AP·인증 서버 로그를 대조하세요.",
        2 => "지금 할 일: IP·게이트웨이·DNS 설정 확인 → 실제 경로 → 프록시 확인 → 승인된 대상 측정.",
        3 => "지금 할 일: 무선 연결과 실제 경로 확인 → 같은 조건의 내부·외부 측정 → 반복 측정.",
        4 => "지금 할 일: 무선 상태 확인 → 성능 비교의 브라우저 관찰. 끊긴 시각과 BSSID·신호 변화를 대조하세요.",
        _ => "왼쪽에서 증상을 선택하세요. 각 단계의 시작 버튼을 눌러야 점검이 실행됩니다."
    };

    private void RefreshBeginnerGuidance()
    {
        GuidedNextAction.Text = SymptomNextAction();
        if (_guidedStep == 2)
        {
            Button measure = new() { Content = "서비스 응답 확인 → 승인 대상 측정", Padding = new Thickness(9, 5, 9, 5) };
            measure.Click += (_, _) => NavigateGuidedStep(3);
            GuidedChoices.Children.Add(measure);
        }
        if (_guidedStep == 4 && _beginnerSummaryText is not null)
            _beginnerSummaryText.Text = RenderBeginnerSummary();
    }

    private void AddConnectionCheckChoice()
    {
        EnsureConnectionBasicsTab();
        AddGuidedChoice("IP·게이트웨이·DNS 설정 점검", "기본 연결 점검");
        SelectGuidedTool("기본 연결 점검");
    }

    private void EnsureConnectionBasicsTab()
    {
        if (_connectionBasicsText is not null) return;
        _connectionBasicsText = new() { Text = _connectionBasicsSnapshot, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 14, 0, 0) };
        Button run = new() { Content = "기본 연결 설정 확인", Padding = new Thickness(12, 8, 12, 8), HorizontalAlignment = HorizontalAlignment.Left };
        run.Click += async (_, _) => await RunConnectionBasicsAsync(run);
        StackPanel panel = new() { Margin = new Thickness(20) };
        panel.Children.Add(new TextBlock { Text = "IP → 게이트웨이 → DNS → 서비스", FontSize = 20, FontWeight = FontWeights.SemiBold });
        panel.Children.Add(new TextBlock { Text = "Windows의 로컬 설정을 확인합니다. 설정 유무와 실제 통신 성공을 구분합니다. 네트워크 요청은 보내지 않습니다.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 14) });
        panel.Children.Add(run);
        panel.Children.Add(_connectionBasicsText);
        DiagnosticTabs.Items.Add(new TabItem { Header = "기본 연결 점검", Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } });
    }

    internal Task<bool> RunConnectionBasicsAsync(Button? button = null) => RunLocalDiagnosticAsync(
        ApplicationOperationKind.NetworkEnvironmentCapture, CollectConnectionBasics,
        text => { _connectionBasicsSnapshot = text; if (_connectionBasicsText is not null) _connectionBasicsText.Text = text; },
        busy => { if (button is not null) button.IsEnabled = !busy; },
        (text, _) => { if (_connectionBasicsText is not null) _connectionBasicsText.Text = text; });

    internal string RenderBeginnerSummary()
    {
        StringBuilder text = new();
        text.AppendLine("관찰된 결과");
        text.AppendLine(_connectionBasicsSnapshot);
        var results = MeasurementResultHistory.Snapshot();
        var latest = results.GroupBy(r => r.PathKind).Select(g => g.OrderByDescending(r => r.CompletedAt).First()).ToArray();
        text.AppendLine("\n서비스 응답 · 내부/외부 각각의 최근 측정");
        if (latest.Length == 0) text.AppendLine("미실행 — 승인된 대상의 HTTP 응답과 성능을 아직 측정하지 않았습니다.");
        foreach (var result in latest)
        {
            text.AppendLine($"{result.CompletedAt.ToLocalTime():yyyy-MM-dd HH:mm:ss} · {(result.PathKind == NetworkPathKind.Internal ? "내부" : "외부")} · {FormatMeasurementStatus(result.Status)} · HTTP {result.HttpStatusCode?.ToString() ?? "확인 불가"} · {FormatMbps(result.AverageMbps)}");
            text.AppendLine(result.HttpStatusCode == 407 ? "지금 할 일: 프록시 인증 요구가 관찰됐습니다. 현재 사용자 인증과 회사 프록시 정책을 담당자와 확인하세요."
                : result.Status == MeasurementStatus.Success ? "해당 시각·대상의 요청은 완료됐습니다. 다른 대상과 다른 시각의 정상까지 의미하지 않습니다."
                : "지금 할 일: 측정 상세의 상태·오류를 확인하세요. DNS·TCP·TLS·서버 중 원인을 이 결과만으로 확정할 수 없습니다.");
        }
        text.AppendLine("\n아직 확인하지 못한 부분: AP 부하·간섭·재전송·인증 서버 원인. DNS/TCP/TLS 개별 성공 여부를 분리 측정하지 않았습니다.");
        text.AppendLine("\n" + SymptomNextAction());
        text.Append("여러 시각의 기록을 동일 조건으로 해석하지 마세요. 상세 근거와 공유용 파일은 아래 보고서 생성으로 저장하세요.");
        return text.ToString();
    }

    private UIElement CreateBeginnerSummaryPanel()
    {
        _beginnerSummaryText = new() { Text = RenderBeginnerSummary(), TextWrapping = TextWrapping.Wrap };
        return new Border { Padding = new Thickness(14), Margin = new Thickness(0, 0, 0, 16), Background = Brushes.AliceBlue, Child = _beginnerSummaryText };
    }
}
