using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WlanLivePathTester.Core.Models;
using WlanLivePathTester.Core.Operations;
using WlanLivePathTester.Core.Wlan;
using WlanLivePathTester.Windows.Proxy;
using WlanLivePathTester.Windows.Wlan;

namespace WlanLivePathTester.App;

public partial class MainWindow
{
    internal Func<CancellationToken, Task<string>> CollectBasicWlan { get; set; } = token =>
        Task.Run(() => FormatBasicWlan(NativeWlanReader.ReadCurrent()), token);
    internal Func<CancellationToken, Task<string>> CollectBasicProxySettings { get; set; } = token =>
        Task.Run(() =>
        {
            CurrentUserProxySettings settings = CurrentUserProxySettingsReader.Read();
            return settings.ReadSucceeded
                ? $"읽기 성공 · 방식: {settings.Mode} · 자동 감지: {(settings.AutoDetectEnabled ? "사용" : "미사용")} · PAC: {(settings.AutoConfigUrl is null ? "없음" : "설정됨")} · 수동 프록시: {(settings.ManualProxy is null ? "없음" : "설정됨")}"
                : $"읽기 실패 · Win32 오류: {settings.Win32Error}";
        }, token);

    private async void OnReadWlanStatusClick(object sender, RoutedEventArgs e) =>
        await RunBasicDiagnosticAsync(false, sender as Button);
    private async void OnReadProxySettingsClick(object sender, RoutedEventArgs e) =>
        await RunBasicDiagnosticAsync(true, sender as Button);

    internal Task<bool> RunBasicDiagnosticAsync(bool proxySettings, Button? button = null)
    {
        Dispatcher.VerifyAccess();
        Func<CancellationToken, Task<string>> collect = proxySettings ? CollectBasicProxySettings : CollectBasicWlan;
        TextBlock output = proxySettings ? ProxyResultText : WlanResultText;
        bool wasEnabled = button?.IsEnabled == true;
        return RunLocalDiagnosticAsync(
            proxySettings ? ApplicationOperationKind.ProxyRouteResolution : ApplicationOperationKind.NetworkAdapterDiagnostics,
            collect, text => { output.Text = text; output.Foreground = Brushes.DarkSlateGray; },
            busy => { if (button is not null) button.IsEnabled = !busy && wasEnabled; },
            (text, brush) => { output.Text = text; output.Foreground = brush; });
    }

    private static string FormatBasicWlan(WlanReadResult result)
    {
        WlanSnapshot? connected = result.FirstConnectedInterface;
        if (connected is null)
        {
            string states = result.Interfaces.Count == 0 ? "무선 인터페이스 정보 없음"
                : string.Join(Environment.NewLine, result.Interfaces.Select(item =>
                    $"- {item.InterfaceDescription ?? "이름 없음"}: {item.InterfaceState ?? "상태 불명"}"));
            return $"{result.Message}{Environment.NewLine}{states}";
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
        if (connected.ReadError is not null) builder.AppendLine($"부분 제한: {connected.ReadError}");
        return builder.ToString().TrimEnd();
    }
}
