using System.Windows;
using WlanLivePathTester.Windows.Proxy;

namespace WlanLivePathTester.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
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
}
