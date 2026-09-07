using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace WlanLivePathTester.App;

public partial class MainWindow
{
    private int _guidedStep;
    private bool _updatingGuidedTools;

    private static readonly string[] GuidedTitles =
    [
        "① 무선 연결 · L1/L2", "② 실제 통신 경로 · L3", "③ 서비스 연결 · L4~L7",
        "④ 내부·외부 성능 비교", "⑤ 결과와 다음 점검"
    ];
    private static readonly string[] GuidedQuestions =
    [
        "어떤 AP에 연결됐나요? 무선 진단 시작을 눌러 신호·채널·링크 정보를 확인하세요.",
        "Wi-Fi, 유선, VPN 중 어느 경로를 사용하나요? 인터페이스를 확인한 뒤 대상별 경로를 비교하세요.",
        "회사 프록시 설정과 대상별 경로를 확인하세요. 실제 HTTP 응답·인증 오류는 다음 단계의 다운로드 측정에서 확인합니다.",
        "승인된 내부 대상부터 측정하고 외부 대상과 비교하세요. 재현성을 확인하려면 반복 측정을 사용하세요.",
        "측정 결과를 보고서로 모아 근거·의미·다음 점검을 확인하세요. 미실행 항목은 정상으로 판단하지 않습니다."
    ];
    private static readonly string[] GuidedLimits =
    [
        "RSSI와 PHY 링크 속도만으로 무선망 전체를 정상으로 판정할 수 없습니다. PHY는 실제 다운로드 속도가 아닙니다.",
        "유선·VPN이 함께 활성화되면 Wi-Fi를 사용한다고 단정할 수 없습니다. OS 경로 근거는 실제 패킷 경로 전체의 증명이 아닙니다.",
        "TLS를 OSI의 특정 계층 하나로 단정하지 않습니다. 연결 오류는 실제로 확보한 근거만 해석합니다.",
        "내부 서버도 병목일 수 있습니다. 브라우저 관찰은 해당 Wi-Fi 전체 트래픽이며 다른 프로그램이 섞일 수 있습니다.",
        "프록시·AP 내부 상태는 직접 확인하지 않습니다. 보고서 공유 전 민감정보를 다시 검토하세요."
    ];

    private void InitializeGuidedNavigation()
    {
        NavigateGuidedStep(0);
    }

    private void OnGuidedStepClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string text } && int.TryParse(text, out int step))
            NavigateGuidedStep(step);
    }

    internal bool NavigateGuidedStep(int step)
    {
        Dispatcher.VerifyAccess();
        if (step is < 0 or > 4 || !CanNavigateGuided()) return false;
        string header;
        switch (step)
        {
            case 1:
                EnsureNetworkEnvironmentTab();
                EnsureRouteComparisonTabV3();
                EnsureRouteProxyImportControls();
                header = "인터페이스 환경";
                break;
            case 3:
                EnsureRepeatedMeasurementTab();
                header = "내부 · 외부 다운로드 측정";
                break;
            case 4:
                EnsureLocalReportTab();
                header = "로컬 보고서";
                break;
            default: header = "WLAN · 프록시"; break;
        }
        if (!SelectGuidedTool(header)) return false;
        _updatingGuidedTools = true;
        try
        {
            GuidedAdvanced.IsChecked = false;
            GuidedAdvancedTools.Visibility = Visibility.Collapsed;
        }
        finally { _updatingGuidedTools = false; }
        _guidedStep = step;
        GuidedTitle.Text = GuidedTitles[step];
        GuidedQuestion.Text = GuidedQuestions[step];
        GuidedLimitation.Text = GuidedLimits[step];
        GuidedChoices.Children.Clear();
        if (step == 1)
        {
            AddGuidedChoice("인터페이스 확인", "인터페이스 환경");
            AddGuidedChoice("내부·프록시 경로 비교", "경로 비교");
        }
        if (step == 3)
        {
            AddGuidedChoice("단일 측정·대상 설정", "내부 · 외부 다운로드 측정");
            AddGuidedChoice("반복 측정", "반복 측정");
            AddGuidedChoice("브라우저 관찰", "브라우저 관찰");
        }
        foreach (Button button in GuidedSteps.Children.OfType<Button>())
        {
            bool selected = button.Tag?.ToString() == step.ToString(System.Globalization.CultureInfo.InvariantCulture);
            button.FontWeight = selected ? FontWeights.Bold : FontWeights.Normal;
            button.Background = selected ? Brushes.LightSteelBlue : Brushes.White;
        }
        ApplyGuidedPanelVisibility();
        return true;
    }

    private bool CanNavigateGuided()
    {
        if (_applicationOperationWindowClosed || _applicationOperationClosePending
            || _localDiagnosticSystemSuspended || CurrentApplicationOperation.IsBusy)
        {
            if (!_applicationOperationWindowClosed)
                ShowApplicationOperationBlocked("실행 중인 작업을 완료하거나 중지한 뒤 단계를 이동하세요. 실제 작업 종료까지 기다립니다.");
            return false;
        }
        return true;
    }

    private bool SelectGuidedTool(string header)
    {
        if (!CanNavigateGuided()) return false;
        TabItem? tab = DiagnosticTabs.Items.OfType<TabItem>()
            .FirstOrDefault(item => string.Equals(item.Header?.ToString(), header, StringComparison.Ordinal));
        if (tab is null || !tab.IsEnabled)
        {
            ShowApplicationOperationBlocked("화면을 준비하지 못했습니다. 창이 열린 뒤 다시 선택하세요.");
            return false;
        }
        DiagnosticTabs.SelectedItem = tab;
        return true;
    }

    private void AddGuidedChoice(string label, string header)
    {
        Button button = new() { Content = label, Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(9, 5, 9, 5) };
        button.Click += (_, _) => SelectGuidedTool(header);
        GuidedChoices.Children.Add(button);
    }

    private void OnGuidedAdvancedChanged(object sender, RoutedEventArgs e)
    {
        if (_updatingGuidedTools || DiagnosticTabs is null || GuidedAdvancedTools is null) return;
        if (!CanNavigateGuided())
        {
            // Return the checkbox to the current view without changing active controls.
            GuidedAdvanced.Checked -= OnGuidedAdvancedChanged;
            GuidedAdvanced.Unchecked -= OnGuidedAdvancedChanged;
            GuidedAdvanced.IsChecked = GuidedAdvancedTools.Visibility == Visibility.Visible;
            GuidedAdvanced.Checked += OnGuidedAdvancedChanged;
            GuidedAdvanced.Unchecked += OnGuidedAdvancedChanged;
            return;
        }
        bool advanced = GuidedAdvanced.IsChecked == true;
        GuidedAdvancedTools.Visibility = advanced ? Visibility.Visible : Visibility.Collapsed;
        if (advanced)
        {
            _updatingGuidedTools = true;
            try
            {
                GuidedAdvancedTools.ItemsSource = DiagnosticTabs.Items.OfType<TabItem>()
                    .Select(tab => tab.Header?.ToString() ?? string.Empty).ToArray();
                GuidedAdvancedTools.SelectedItem = (DiagnosticTabs.SelectedItem as TabItem)?.Header?.ToString();
            }
            finally { _updatingGuidedTools = false; }
        }
        else NavigateGuidedStep(_guidedStep);
        ApplyGuidedPanelVisibility();
    }

    private void OnGuidedToolChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingGuidedTools || GuidedAdvancedTools.SelectedItem is not string header) return;
        if (SelectGuidedTool(header))
        {
            GuidedTitle.Text = "고급 도구 · " + header;
            GuidedQuestion.Text = "개별 기능을 직접 실행합니다. 기본 진단으로 돌아가려면 왼쪽 단계를 선택하세요.";
            GuidedLimitation.Text = "개별 기능의 수집 시각과 한계를 확인하세요. 서로 다른 시각의 결과를 동일 조건의 측정으로 해석하지 마세요.";
            GuidedChoices.Children.Clear();
        }
        else
        {
            _updatingGuidedTools = true;
            try { GuidedAdvancedTools.SelectedItem = (DiagnosticTabs.SelectedItem as TabItem)?.Header?.ToString(); }
            finally { _updatingGuidedTools = false; }
        }
    }

    private void ApplyGuidedPanelVisibility()
    {
        bool advanced = GuidedAdvanced.IsChecked == true;
        GuidedWlanPanel.Visibility = advanced || _guidedStep == 0 ? Visibility.Visible : Visibility.Collapsed;
        GuidedProxyPanel.Visibility = advanced || _guidedStep == 2 ? Visibility.Visible : Visibility.Collapsed;
        GuidedProxyRoutePanel.Visibility = advanced || _guidedStep == 2 ? Visibility.Visible : Visibility.Collapsed;
        Grid.SetColumn(GuidedProxyPanel, advanced ? 2 : 0);
        Grid.SetColumnSpan(GuidedWlanPanel, advanced ? 1 : 3);
        Grid.SetColumnSpan(GuidedProxyPanel, advanced ? 1 : 3);
    }
}
