using System.Text;
using System.Windows;
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
