using System.Text;
using System.Windows;
using System.Windows.Media;
using WlanLivePathTester.Core.Models;
using WlanLivePathTester.Core.Wlan;
using WlanLivePathTester.Windows.Proxy;
using WlanLivePathTester.Windows.Wlan;

namespace WlanLivePathTester.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void OnReadWlanStatusClick(object sender, RoutedEventArgs e)
    {
        try
        {
            WlanReadResult result = NativeWlanReader.ReadCurrent();
            WlanSnapshot? connected = result.FirstConnectedInterface;

            if (connected is null)
            {
                string interfaceStates = result.Interfaces.Count == 0
                    ? "무선 인터페이스 정보 없음"
                    : string.Join(
                        Environment.NewLine,
                        result.Interfaces.Select(item =>
                            $"- {item.InterfaceDescription ?? "이름 없음"}: {item.InterfaceState ?? "상태 불명"}"));

                WlanResultText.Text = $"{result.Message}{Environment.NewLine}{interfaceStates}";
                return;
            }

            StringBuilder builder = new();
            builder.AppendLine(result.Message);
            builder.AppendLine($"인터페이스: {connected.InterfaceDescription ?? "확인 불가"}");
            builder.AppendLine($"SSID: {connected.Ssid ?? "확인 불가"}");
            builder.AppendLine($"BSSID: {connected.Bssid ?? "확인 불가"}");
            builder.AppendLine($"RSSI: {FormatDbm(connected.RssiDbm)} / 신호 품질: {FormatPercent(connected.SignalQualityPercent)}");
            builder.AppendLine($"밴드: {WlanChannelCalculator.GetBandName(connected.CenterFrequencyMhz)} / 채널: {FormatNumber(connected.Channel)} / 주파수: {FormatFrequency(connected.CenterFrequencyMhz)}");
            builder.AppendLine($"PHY: {connected.PhyType ?? "확인 불가"}");
            builder.AppendLine($"Rx 링크: {FormatLinkSpeed(connected.ReceiveLinkSpeedBps)} / Tx 링크: {FormatLinkSpeed(connected.TransmitLinkSpeedBps)}");
            builder.AppendLine($"인증: {connected.Authentication ?? "확인 불가"} / 암호화: {connected.Cipher ?? "확인 불가"}");

            if (connected.ReadError is not null)
            {
                builder.AppendLine($"부분 제한: {connected.ReadError}");
            }

            WlanResultText.Text = builder.ToString().TrimEnd();
        }
        catch (Exception exception)
        {
            WlanResultText.Text = $"WLAN 확인 중 오류가 발생했습니다: {exception.Message}";
        }
    }

    private void OnReadProxySettingsClick(object sender, RoutedEventArgs e)
    {
        try
        {
            CurrentUserProxySettings settings = CurrentUserProxySettingsReader.Read();

            ProxyResultText.Text = settings.ReadSucceeded
                ? $"읽기 성공 · 방식: {settings.Mode} · 자동 감지: {(settings.AutoDetectEnabled ? "사용" : "미사용")} · PAC: {(settings.AutoConfigUrl is null ? "없음" : "설정됨")} · 수동 프록시: {(settings.ManualProxy is null ? "없음" : "설정됨")}"
                : $"읽기 실패 · Win32 오류: {settings.Win32Error}";
        }
        catch (Exception exception)
        {
            ProxyResultText.Text = $"확인 중 오류가 발생했습니다: {exception.Message}";
        }
    }

    private async void OnResolveProxyRouteClick(object sender, RoutedEventArgs e)
    {
        string url = ProxyTargetUrlTextBox.Text.Trim();
        NetworkPathKind expectedPath = ProxyExpectedPathComboBox.SelectedIndex == 1
            ? NetworkPathKind.Internal
            : NetworkPathKind.External;

        object? previousContent = ResolveProxyRouteButton.Content;
        ResolveProxyRouteButton.IsEnabled = false;
        ResolveProxyRouteButton.Content = "확인 중...";
        ProxyRouteResultText.Foreground = Brushes.DarkSlateGray;
        ProxyRouteResultText.Text = "현재 사용자 프록시 정책을 확인하고 있습니다.";

        try
        {
            ProxyRouteResolution result = await Task.Run(
                () => ProxyRouteResolver.Resolve(url, expectedPath));

            ProxyRouteResultText.Foreground = result switch
            {
                { IsSuccess: true, Expectation: ProxyPathExpectation.Match } => Brushes.DarkGreen,
                { IsSuccess: true, Expectation: ProxyPathExpectation.Mismatch } => Brushes.DarkOrange,
                { IsSuccess: true } => Brushes.DarkSlateGray,
                _ => Brushes.DarkRed
            };

            StringBuilder builder = new();
            builder.AppendLine($"상태: {FormatProxyStatus(result.Status)}");
            builder.AppendLine($"설정 출처: {FormatProxySource(result.Source)}");
            builder.AppendLine($"판정 경로: {result.SafeRouteSummary}");
            builder.AppendLine($"예상 경로: {FormatExpectedPath(expectedPath)}");
            builder.AppendLine($"기대 경로 일치: {FormatExpectation(result.Expectation)}");
            builder.AppendLine($"PAC/WPAD 네트워크 조회: {(result.NetworkLookupPerformed ? "수행됨" : "수행 안 함")}");
            builder.AppendLine($"자동 로그온 재시도: {(result.AutoLogonRetried ? "수행됨" : "수행 안 함")}");

            if (result.InvalidDirectiveCount > 0)
            {
                builder.AppendLine($"제외한 지시문: {result.InvalidDirectiveCount}개");
            }

            if (result.Win32ErrorCode is int errorCode)
            {
                builder.AppendLine($"Win32 오류 코드: {errorCode}");
            }

            builder.AppendLine($"설명: {result.Message}");
            ProxyRouteResultText.Text = builder.ToString().TrimEnd();
        }
        catch (Exception exception)
        {
            ProxyRouteResultText.Foreground = Brushes.DarkRed;
            ProxyRouteResultText.Text = $"프록시 경로 확인 중 오류가 발생했습니다: {exception.Message}";
        }
        finally
        {
            ResolveProxyRouteButton.Content = previousContent;
            ResolveProxyRouteButton.IsEnabled = true;
        }
    }

    private static string FormatProxyStatus(ProxyResolutionStatus status) =>
        status switch
        {
            ProxyResolutionStatus.Success => "성공",
            ProxyResolutionStatus.InvalidUrl => "URL 오류",
            ProxyResolutionStatus.UnsupportedPlatform => "지원하지 않는 운영체제",
            ProxyResolutionStatus.ConfigurationReadFailed => "프록시 설정 읽기 실패",
            ProxyResolutionStatus.ConfigurationInvalid => "프록시 설정 해석 실패",
            ProxyResolutionStatus.AutoProxyAuthenticationFailed => "PAC/WPAD 인증 실패",
            ProxyResolutionStatus.AutoProxyFailed => "PAC/WPAD 판정 실패",
            ProxyResolutionStatus.TimedOut => "시간 초과",
            _ => "Windows API 오류"
        };

    private static string FormatProxySource(ProxyConfigurationSource source) =>
        source switch
        {
            ProxyConfigurationSource.None => "설정 없음",
            ProxyConfigurationSource.Manual => "수동 프록시 또는 바이패스",
            ProxyConfigurationSource.Wpad => "WPAD 자동 검색",
            ProxyConfigurationSource.Pac => "명시적 PAC",
            ProxyConfigurationSource.WpadThenPac => "WPAD 실패 후 명시적 PAC",
            ProxyConfigurationSource.ManualFallback => "PAC/WPAD 실패 후 수동 설정",
            _ => "확인 불가"
        };

    private static string FormatExpectation(ProxyPathExpectation expectation) =>
        expectation switch
        {
            ProxyPathExpectation.Match => "일치",
            ProxyPathExpectation.Mismatch => "불일치",
            _ => "판단 불가"
        };

    private static string FormatExpectedPath(NetworkPathKind pathKind) =>
        pathKind == NetworkPathKind.Internal
            ? "내부망 — DIRECT 예상"
            : "외부망 — PROXY 예상";

    private static string FormatDbm(int? value) =>
        value is int rssi ? $"{rssi} dBm" : "확인 불가";

    private static string FormatPercent(int? value) =>
        value is int percent ? $"{percent}%" : "확인 불가";

    private static string FormatNumber(uint? value) =>
        value?.ToString() ?? "확인 불가";

    private static string FormatFrequency(uint? value) =>
        value is uint frequency ? $"{frequency} MHz" : "확인 불가";

    private static string FormatLinkSpeed(ulong? value) =>
        value is ulong bitsPerSecond
            ? $"{bitsPerSecond / 1_000_000d:F1} Mbps"
            : "확인 불가";
}
