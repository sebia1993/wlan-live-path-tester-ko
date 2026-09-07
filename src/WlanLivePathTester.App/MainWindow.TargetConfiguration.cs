using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WlanLivePathTester.Core.Configuration;
using WlanLivePathTester.Core.Models;

namespace WlanLivePathTester.App;

public partial class MainWindow
{
    private readonly List<MeasurementTargetDefinition> _approvedTargets = [];
    private CheckBox? _manualTargetEntryCheckBox;
    private TextBlock? _approvedTargetStatusText;
    private Button? _reloadApprovedTargetsButton;
    private bool _approvedTargetPanelAdded;
    private string? _approvedTargetConfigurationPath;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        Loaded += OnApprovedTargetConfigurationLoaded;
    }

    private void OnApprovedTargetConfigurationLoaded(object sender, RoutedEventArgs e)
    {
        EnsureApprovedTargetPanel();
        LoadApprovedTargetConfiguration();
    }

    internal void EnsureApprovedTargetPanel()
    {
        if (_approvedTargetPanelAdded) return;
        AddApprovedTargetPanel();
        _approvedTargetPanelAdded = _approvedTargetStatusText is not null;
    }

    private void AddApprovedTargetPanel()
    {
        TabControl? tabControl = FindVisualDescendant<TabControl>(this);
        TabItem? measurementTab = tabControl?.Items
            .OfType<TabItem>()
            .FirstOrDefault(item => string.Equals(
                item.Header?.ToString(),
                "내부 · 외부 다운로드 측정",
                StringComparison.Ordinal));
        if (measurementTab?.Content is not ScrollViewer scrollViewer
            || scrollViewer.Content is not StackPanel stackPanel) return;

        TextBlock statusText = new()
        {
            Foreground = new SolidColorBrush(Color.FromRgb(86, 101, 115)),
            TextWrapping = TextWrapping.Wrap,
            Text = "승인 대상 파일을 선택하거나 아래에 승인된 고정 파일 URL을 입력하세요."
        };
        _approvedTargetStatusText = statusText;
        Button reloadButton = new()
        {
            Content = "승인 대상 다시 불러오기", MinWidth = 170,
            Padding = new Thickness(10, 6, 10, 6), HorizontalAlignment = HorizontalAlignment.Left
        };
        reloadButton.Click += OnReloadApprovedTargetsClick;
        _reloadApprovedTargetsButton = reloadButton;
        CheckBox manualEntryCheckBox = new()
        {
            Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
            Content = "승인 목록을 무시하고 임의 URL 직접 입력(고급)"
        };
        manualEntryCheckBox.Checked += OnManualTargetEntryChanged;
        manualEntryCheckBox.Unchecked += OnManualTargetEntryChanged;
        _manualTargetEntryCheckBox = manualEntryCheckBox;
        WrapPanel actions = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 11, 0, 0) };
        Button importButton = new() { Content = "승인 대상 파일 선택", Padding = new Thickness(10, 6, 10, 6), Margin = new Thickness(0, 0, 8, 0) };
        importButton.Click += (_, _) =>
        {
            if (!CanNavigateGuided()) return;
            Microsoft.Win32.OpenFileDialog dialog = new() { Title = "담당자가 제공한 승인 측정 대상 선택", Filter = "측정 대상 설정 (*.json)|*.json", CheckFileExists = true };
            if (dialog.ShowDialog(this) == true) LoadApprovedTargetConfiguration(dialog.FileName);
        };
        actions.Children.Add(importButton);
        actions.Children.Add(reloadButton);
        actions.Children.Add(manualEntryCheckBox);
        StackPanel panelContent = new();
        panelContent.Children.Add(new TextBlock
        {
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(31, 97, 141)),
            Text = "로컬 승인 측정 대상"
        });
        panelContent.Children.Add(statusText);
        panelContent.Children.Add(new TextBlock { Text = "파일 선택은 이 실행에만 적용됩니다. 자동 적용하려면 앱의 config/targets.local.json으로 준비하세요. 실제 주소가 담긴 파일은 외부로 공유하지 마세요.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) });
        panelContent.Children.Add(actions);
        Border panel = new()
        {
            Margin = new Thickness(0, 14, 0, 0), Padding = new Thickness(14),
            CornerRadius = new CornerRadius(8), Background = new SolidColorBrush(Color.FromRgb(234, 242, 248)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(93, 173, 226)), BorderThickness = new Thickness(1),
            Child = panelContent
        };
        stackPanel.Children.Insert(Math.Min(1, stackPanel.Children.Count), panel);
    }

    private void OnReloadApprovedTargetsClick(object sender, RoutedEventArgs e)
    {
        if (_measurementRunning || _observationCancellation is not null)
        {
            SetApprovedTargetStatus(
                "측정 또는 브라우저 관찰이 진행 중입니다. 완료하거나 중지한 뒤 다시 불러오십시오.", true);
            return;
        }
        if (!CanNavigateGuided()) return;
        LoadApprovedTargetConfiguration(_approvedTargetConfigurationPath);
    }

    private void OnManualTargetEntryChanged(object sender, RoutedEventArgs e)
    {
        if (_manualTargetEntryCheckBox?.IsChecked != true && _approvedTargets.Count == 0)
        {
            _manualTargetEntryCheckBox!.IsChecked = true;
            SetApprovedTargetStatus("사용할 수 있는 승인 대상 설정이 없어 직접 입력 모드를 유지합니다.", true);
            return;
        }
        ApplyTargetEntryMode();
    }

    internal void LoadApprovedTargetConfiguration(string? selectedPath = null)
    {
        if (!CanNavigateGuided()) return;
        EnsureApprovedTargetPanel();
        string? configurationPath = selectedPath ?? FindApprovedTargetConfiguration();
        if (configurationPath is null)
        {
            if (_manualTargetEntryCheckBox is not null) _manualTargetEntryCheckBox.IsChecked = true;
            ApplyTargetEntryMode();
            SetApprovedTargetStatus(
                "준비 필요: 담당자가 제공한 승인 대상 JSON 파일을 선택하세요. 파일이 없다면 승인된 고정 다운로드 파일 URL을 아래에 입력하세요. 일반 홈페이지 주소는 성능 비교에 적합하지 않습니다.", false);
            return;
        }

        try
        {
            string json = TargetConfigurationFileReader.ReadStrictUtf8(configurationPath);
            IReadOnlyList<MeasurementTargetDefinition> targets = TargetConfigurationLoader.LoadFromJson(json);
            MeasurementTargetDefinition[] internalTargets = targets
                .Where(target => target.PathKind == NetworkPathKind.Internal).ToArray();
            MeasurementTargetDefinition[] externalTargets = targets
                .Where(target => target.PathKind == NetworkPathKind.External).ToArray();
            if (internalTargets.Length != 1)
                throw new InvalidDataException("현재 화면은 승인된 내부망 대상을 정확히 1개 요구합니다.");
            if (externalTargets.Length is < 1 or > 4)
                throw new InvalidDataException("현재 화면은 승인된 외부망 대상을 1~4개 요구합니다.");

            MeasurementTargetDefinition first = targets[0];
            bool settingsAreUniform = targets.All(target =>
                target.MaxBytes == first.MaxBytes
                && target.TimeoutSeconds == first.TimeoutSeconds
                && target.Streams == first.Streams
                && target.MaxRedirects == first.MaxRedirects);
            if (!settingsAreUniform)
                throw new InvalidDataException(
                    "현재 화면에서는 모든 승인 대상이 동일한 maxBytes, timeoutSeconds, streams, maxRedirects 값을 사용해야 합니다.");

            _approvedTargetConfigurationPath = configurationPath;
            _approvedTargets.Clear();
            _approvedTargets.AddRange(targets);
            InternalTargetUrlTextBox.Text = internalTargets[0].Url;
            ExternalTargetUrlsTextBox.Text = string.Join(Environment.NewLine, externalTargets.Select(target => target.Url));
            MeasurementMaxMegabytesTextBox.Text = Math.Max(1, first.MaxBytes / 1024 / 1024)
                .ToString(CultureInfo.InvariantCulture);
            MeasurementTimeoutSecondsTextBox.Text = first.TimeoutSeconds.ToString(CultureInfo.InvariantCulture);
            MeasurementStreamsComboBox.SelectedIndex = first.Streams - 1;
            MeasurementMaxRedirectsTextBox.Text = first.MaxRedirects.ToString(CultureInfo.InvariantCulture);
            if (_manualTargetEntryCheckBox is not null) _manualTargetEntryCheckBox.IsChecked = false;
            ApplyTargetEntryMode();
            SetApprovedTargetStatus(
                $"승인 대상 {targets.Count}개를 로컬 설정에서 불러왔습니다. 내부 {internalTargets.Length}개, 외부 {externalTargets.Length}개이며 실제 주소는 이 상태 문구에 표시하지 않습니다.",
                false);
        }
        catch (Exception exception)
        {
            SetApprovedTargetStatus(
                $"파일을 불러오지 못했습니다. 기존 대상과 입력은 유지했습니다. 오류 유형: {exception.GetType().Name}. 설정 경로와 원문은 표시하지 않았습니다.",
                true);
        }
    }

    private void ApplyTargetEntryMode()
    {
        bool manualMode = _manualTargetEntryCheckBox?.IsChecked == true || _approvedTargets.Count == 0;
        if (manualMode) ApprovedTargetRuntimeCatalog.Clear();
        else ApprovedTargetRuntimeCatalog.Replace(_approvedTargets);

        InternalTargetUrlTextBox.IsReadOnly = !manualMode;
        ExternalTargetUrlsTextBox.IsReadOnly = !manualMode;
        MeasurementMaxMegabytesTextBox.IsReadOnly = !manualMode;
        MeasurementTimeoutSecondsTextBox.IsReadOnly = !manualMode;
        MeasurementMaxRedirectsTextBox.IsReadOnly = !manualMode;
        MeasurementStreamsComboBox.IsHitTestVisible = manualMode;
        MeasurementStreamsComboBox.IsTabStop = manualMode;
        double opacity = manualMode ? 1.0 : 0.82;
        InternalTargetUrlTextBox.Opacity = opacity;
        ExternalTargetUrlsTextBox.Opacity = opacity;
        MeasurementMaxMegabytesTextBox.Opacity = opacity;
        MeasurementTimeoutSecondsTextBox.Opacity = opacity;
        MeasurementMaxRedirectsTextBox.Opacity = opacity;
        MeasurementStreamsComboBox.Opacity = opacity;
    }

    private static string? FindApprovedTargetConfiguration()
    {
        string localApplicationData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string userPath = Path.Combine(localApplicationData, "WLAN Live Path Tester KO", "targets.local.json");
        string portablePath = Path.Combine(AppContext.BaseDirectory, "config", "targets.local.json");
        if (!string.IsNullOrWhiteSpace(localApplicationData) && File.Exists(userPath)) return userPath;
        return File.Exists(portablePath) ? portablePath : null;
    }

    private void SetApprovedTargetStatus(string message, bool isError)
    {
        if (_approvedTargetStatusText is null) return;
        _approvedTargetStatusText.Margin = new Thickness(0, 6, 0, 0);
        _approvedTargetStatusText.Foreground = isError
            ? Brushes.DarkRed : new SolidColorBrush(Color.FromRgb(86, 101, 115));
        _approvedTargetStatusText.Text = message;
    }
}
