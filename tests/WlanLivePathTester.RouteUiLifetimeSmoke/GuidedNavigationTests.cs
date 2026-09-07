using System.Windows;
using System.Windows.Controls;
using WlanLivePathTester.App;
using static WlanLivePathTester.RouteUiLifetimeSmoke.TestContext;

namespace WlanLivePathTester.RouteUiLifetimeSmoke;

internal static class GuidedNavigationTests
{
    internal static readonly (string Name, Func<Task> Run)[] Cases =
    [
        ("beginner connection evidence distinguishes settings from reachability", ConnectionEvidenceAsync),
        ("symptom navigation and local checks respect active work and preserve summary", BeginnerFlowAsync),
        ("approved profile import preserves valid input after invalid file", ProfileAsync),
        ("guided report distinguishes absent measurement evidence from health", ReportSummaryAsync),
        ("guided production layouts render at supported window sizes", RenderAsync),
        ("guided stages reuse production tools without starting collectors", StagesAsync),
        ("guided navigation cannot escape a running diagnostic lease", BusyAsync),
        ("guided and advanced views preserve inputs and separate wireless from proxy", AdvancedAsync)
    ];

    private static Task ConnectionEvidenceAsync()
    {
        Func<IReadOnlyList<WlanLivePathTester.Core.Guidance.LocalConnectionEvidence>, string> render = WlanLivePathTester.Core.Guidance.BeginnerConnectionSummary.Render;
        string missing = render([]);
        Ensure(missing.Contains("판단 불가"), "Empty capture must not be healthy.");
        string linkLocal = render([new("Wi-Fi", true, true, false, true, false, false)]);
        Ensure(linkLocal.Contains("링크 로컬 주소만") && linkLocal.Contains("DHCP 장애로 확정하지"), "Link-local evidence must not prove DHCP cause.");
        string configured = render([new("Wi-Fi", true, true, true, true, true, true)]);
        Ensure(!configured.Contains("링크 로컬 주소만") && configured.Contains("도달 여부 미검사") && configured.Contains("이름 해석 성공 여부 미검사"), "Dual-stack/configured addresses must not imply reachability.");
        string inactive = render([new("Ethernet", false, true, false, false, false, false), new("Wi-Fi", true, false, false, false, false, false)]);
        Ensure(inactive.Contains("사용하지 않는 어댑터") && inactive.Contains("읽지 못했습니다"), "Inactive and failed captures must remain distinct.");
        return Task.CompletedTask;
    }

    private static async Task BeginnerFlowAsync()
    {
        MainWindow window = Prepare();
        var release = Pending<string>();
        try
        {
            window.GuidedSymptom.SelectedIndex = 2;
            Ensure((Tabs(window).SelectedItem as TabItem)?.Header?.ToString() == "기본 연결 점검", "No-internet symptom should start at local IP settings.");
            Ensure(!window.MeasurementAdvancedSettings.IsExpanded, "Advanced measurement settings should start collapsed.");
            window.CollectConnectionBasics = _ => release.Task;
            Task<bool> run = window.RunConnectionBasicsAsync();
            await UntilAsync(() => window.CurrentApplicationOperation.IsBusy);
            window.GuidedSymptom.SelectedIndex = 4;
            Ensure(window.GuidedSymptom.SelectedIndex == 2 && !window.NavigateGuidedStep(4), "Symptom selection must not escape an active collection.");
            release.SetResult("fixture: gateway configured; reachability untested");
            await run;
            Ensure(window.NavigateGuidedStep(4), "Results should be available without saving.");
            Ensure(Field<TextBlock>(window, "_beginnerConnectionDetails").Text.Contains("fixture: gateway"), "Local evidence must be retained in the preview.");
            Ensure(Optional(window, "_lastReportDirectory") is null, "Preview must not save a report.");
        }
        finally { release.TrySetResult("released"); window.Close(); }
    }

    private static Task ProfileAsync()
    {
        MainWindow window = Prepare();
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try
        {
            window.NavigateGuidedStep(3);
            File.WriteAllText(path, "{invalid json");
            window.InternalTargetUrlTextBox.Text = "https://internal.example.invalid/approved.bin";
            window.LoadApprovedTargetConfiguration(path);
            Ensure(window.InternalTargetUrlTextBox.Text.EndsWith("approved.bin", StringComparison.Ordinal), "Invalid profile must not destroy input.");
            string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
            window.LoadApprovedTargetConfiguration(Path.Combine(root, "config", "targets.example.json"));
            Ensure(window.InternalTargetUrlTextBox.IsReadOnly, "Valid profile must enforce approved target mode.");
            string approved = window.InternalTargetUrlTextBox.Text;
            window.LoadApprovedTargetConfiguration(path);
            Ensure(window.InternalTargetUrlTextBox.Text == approved && window.InternalTargetUrlTextBox.IsReadOnly, "Invalid replacement must preserve approved mode.");
        }
        finally { File.Delete(path); window.Close(); WlanLivePathTester.Core.Configuration.ApprovedTargetRuntimeCatalog.Clear(); }
        return Task.CompletedTask;
    }

    private static Task ReportSummaryAsync()
    {
        var report = WlanLivePathTester.UnifiedReportSmoke.Fixtures.Report() with { StructuredMeasurements = [] };
        string summary = WlanLivePathTester.Core.Reporting.GuidedReportSummary.Render(report);
        Ensure(summary.Contains("미실행 — HTTP 측정 결과 없음", StringComparison.Ordinal)
            && summary.Contains("미실행 — 내부·외부 속도를 판단하지 않음", StringComparison.Ordinal),
            "Absent tests must never become a healthy service or throughput verdict.");
        report = report with { Wlan = report.Wlan with { IsConnected = false } };
        summary = WlanLivePathTester.Core.Reporting.GuidedReportSummary.Render(report);
        Ensure(summary.Contains("판단 불가 — 무선 연결 정보를 확보하지 못함", StringComparison.Ordinal),
            "Unavailable WLAN information must not assert a proven disconnection cause.");
        return Task.CompletedTask;
    }

    private static Task RenderAsync()
    {
        string? directory = Environment.GetEnvironmentVariable("WLAN_GUIDED_RENDER_DIRECTORY");
        if (string.IsNullOrWhiteSpace(directory)) return Task.CompletedTask;
        Directory.CreateDirectory(directory);
        MainWindow window = Prepare();
        try
        {
            foreach (int width in new[] { 1080, 1280 })
            {
                for (int step = 0; step < 5; step++)
                {
                    Ensure(window.NavigateGuidedStep(step), "Cannot render a missing stage.");
                    if (step == 3) Ensure(Optional(window, "_approvedTargetStatusText") is TextBlock,
                        "The rendered measurement page must contain the approved-profile picker.");
                    FrameworkElement root = (FrameworkElement)window.Content;
                    root.Measure(new Size(width, 800));
                    root.Arrange(new Rect(0, 0, width, 800));
                    root.UpdateLayout();
                    var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(
                        width, 800, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                    bitmap.Render(root);
                    var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                    encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                    using var output = File.Create(Path.Combine(directory, $"stage-{step + 1}-{width}.png"));
                    encoder.Save(output);
                }
            }
        }
        finally { window.Close(); }
        return Task.CompletedTask;
    }

    private static Task StagesAsync()
    {
        MainWindow window = Prepare();
        try
        {
            int calls = 0;
            window.CollectBasicWlan = _ => { calls++; return Task.FromResult("unexpected"); };
            window.CollectBasicProxySettings = window.CollectBasicWlan;
            string[] expected = ["WLAN · 프록시", "기본 연결 점검", "WLAN · 프록시", "내부 · 외부 다운로드 측정", "로컬 보고서"];
            for (int step = 0; step < expected.Length; step++)
            {
                Ensure(window.NavigateGuidedStep(step), "Every guided step must have a production destination.");
                Ensure((Tabs(window).SelectedItem as TabItem)?.Header?.ToString() == expected[step], "Wrong guided destination.");
                Ensure(!window.CurrentApplicationOperation.IsBusy, "Navigation must not begin work.");
            }
            Ensure(calls == 0, "Navigation must not perform collection or external requests.");
            Ensure(!window.NavigateGuidedStep(-1) && !window.NavigateGuidedStep(5), "Invalid steps must be rejected.");
        }
        finally { window.Close(); }
        return Task.CompletedTask;
    }

    private static async Task BusyAsync()
    {
        MainWindow window = Prepare("WLAN · 프록시");
        var release = Pending<string>();
        window.CollectBasicWlan = _ => release.Task;
        Task<bool> active = window.RunBasicDiagnosticAsync(false);
        try
        {
            object selected = Tabs(window).SelectedItem;
            Ensure(!window.NavigateGuidedStep(4), "Busy navigation must be rejected.");
            Ensure(ReferenceEquals(selected, Tabs(window).SelectedItem), "Busy navigation changed the owning tab.");
            release.SetResult("synthetic-wlan");
            await active;
            Ensure(window.NavigateGuidedStep(4), "Navigation must recover after real completion.");
            Ensure(window.WlanResultText.Text == "synthetic-wlan", "Navigation must preserve collected evidence.");
        }
        finally { release.TrySetResult("cleanup"); await active; window.Close(); }
    }

    private static Task AdvancedAsync()
    {
        MainWindow window = Prepare();
        try
        {
            window.InternalTargetUrlTextBox.Text = "https://internal.example.invalid/test.bin";
            Ensure(window.NavigateGuidedStep(0), "Wireless stage missing.");
            Ensure(window.GuidedWlanPanel.Visibility == Visibility.Visible
                && window.GuidedProxyPanel.Visibility == Visibility.Collapsed, "Wireless stage is cluttered with proxy controls.");
            Ensure(window.NavigateGuidedStep(2), "Service stage missing.");
            Ensure(window.GuidedWlanPanel.Visibility == Visibility.Collapsed
                && window.GuidedProxyPanel.Visibility == Visibility.Visible, "Service controls must be separated.");
            window.GuidedAdvanced.IsChecked = true;
            window.GuidedAdvancedTools.SelectedItem = "경로 비교";
            Ensure((Tabs(window).SelectedItem as TabItem)?.Header?.ToString() == "경로 비교", "Advanced tool selection did not navigate.");
            window.GuidedAdvanced.IsChecked = false;
            Ensure((Tabs(window).SelectedItem as TabItem)?.Header?.ToString() == "WLAN · 프록시", "Basic mode must return to its current stage.");
            Ensure(window.InternalTargetUrlTextBox.Text == "https://internal.example.invalid/test.bin", "Mode switching lost user input.");
        }
        finally { window.Close(); }
        return Task.CompletedTask;
    }
}
