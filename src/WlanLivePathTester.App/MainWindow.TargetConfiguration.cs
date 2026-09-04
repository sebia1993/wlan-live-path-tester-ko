using System.Globalization;
using System.IO;
using System.Text;
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

    private void OnApprovedTargetConfigurationLoaded(
        object sender,
        RoutedEventArgs e)
    {
        if (!_approvedTargetPanelAdded)
        {
            AddApprovedTargetPanel();
            _approvedTargetPanelAdded = true;
        }

        LoadApprovedTargetConfiguration();
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
            || scrollViewer.Content is not StackPanel stackPanel)
        {
            return;
        }

        TextBlock statusText = new()
        {
            Foreground = new SolidColorBrush(Color.FromRgb(86, 101, 115)),
            TextWrapping = TextWrapping.Wrap,
            Text = "로컬 승인 대상 설정을 확인하고 있습니다."
        };
        _approvedTargetStatusText = statusText;

        Button reloadButton = new()
        {
            Content = "승인 대상 다시 불러오기",
            MinWidth = 170,
            Padding = new Thickness(10, 6, 10, 6),
            HorizontalAlignment = HorizontalAlignment.Left
        };
        reloadButton.Click += OnReloadApprovedTargetsClick;
        _reloadApprovedTargetsButton = reloadButton;

        CheckBox manualEntryCheckBox = new()
        {
            Margin = new Thickness(14, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Content = "승인 목록을 무시하고 임의 URL 직접 입력(고급)"
        };
        manualEntryCheckBox.Checked += OnManualTargetEntryChanged;
        manualEntryCheckBox.Unchecked += OnManualTargetEntryChanged;
        _manualTargetEntryCheckBox = manualEntryCheckBox;

        StackPanel actions = new()
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 11, 0, 0)
        };
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
        panelContent.Children.Add(actions);

        Border panel = new()
        {
            Margin = new Thickness(0, 14, 0, 0),
            Padding = new Thickness(14),
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(Color.FromRgb(234, 242, 248)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(93, 173, 226)),
            BorderThickness = new Thickness(1),
            Child = panelContent
        };

        int insertionIndex = Math.Min(1, stackPanel.Children.Count);
        stackPanel.Children.Insert(insertionIndex, panel);
    }

    private void OnReloadApprovedTargetsClick(
        object sender,
        RoutedEventArgs e)
    {
        if (_measurementRunning || _observationCancellation is not null)
        {
            SetApprovedTargetStatus(
                "측정 또는 브라우저 관찰이 진행 중입니다. 완료하거나 중지한 뒤 다시 불러오십시오.",
                isError: true);
            return;
        }

        LoadApprovedTargetConfiguration();
    }

    private void OnManualTargetEntryChanged(
        object sender,
        RoutedEventArgs e)
    {
        if (_manualTargetEntryCheckBox?.IsChecked != true
            && _approvedTargets.Count == 0)
        {
            _manualTargetEntryCheckBox!.IsChecked = true;
            SetApprovedTargetStatus(
                "사용할 수 있는 승인 대상 설정이 없어 직접 입력 모드를 유지합니다.",
                isError: true);
            return;
        }

        ApplyTargetEntryMode();
    }

    private void LoadApprovedTargetConfiguration()
    {
        _approvedTargets.Clear();
        ApprovedTargetRuntimeCatalog.Clear();
        _approvedTargetConfigurationPath = FindApprovedTargetConfiguration();

        if (_approvedTargetConfigurationPath is null)
        {
            if (_manualTargetEntryCheckBox is not null)
            {
                _manualTargetEntryCheckBox.IsChecked = true;
            }

            ApplyTargetEntryMode();
            SetApprovedTargetStatus(
                "targets.local.json이 없습니다. 현재는 임의 URL 직접 입력 모드입니다. 실제 사내 주소가 포함된 설정 파일은 Git에 커밋하지 마십시오.",
                isError: false);
            return;
        }

        try
        {
            string json = File.ReadAllText(
                _approvedTargetConfigurationPath,
                new UTF8Encoding(
                    encoderShouldEmitUTF8Identifier: false,
                    throwOnInvalidBytes: true));
            IReadOnlyList<MeasurementTargetDefinition> targets =
                TargetConfigurationLoader.LoadFromJson(json);

            MeasurementTargetDefinition[] internalTargets = targets
                .Where(target => target.PathKind == NetworkPathKind.Internal)
                .ToArray();
            MeasurementTargetDefinition[] externalTargets = targets
                .Where(target => target.PathKind == NetworkPathKind.External)
                .ToArray();

            if (internalTargets.Length != 1)
            {
                throw new InvalidDataException(
                    "현재 화면은 승인된 내부망 대상을 정확히 1개 요구합니다.");
            }

            if (externalTargets.Length is < 1 or > 4)
            {
                throw new InvalidDataException(
                    "현재 화면은 승인된 외부망 대상을 1~4개 요구합니다.");
            }

            MeasurementTargetDefinition first = targets[0];
            bool settingsAreUniform = targets.All(target =>
                target.MaxBytes == first.MaxBytes
                && target.TimeoutSeconds == first.TimeoutSeconds
                && target.Streams == first.Streams
                && target.MaxRedirects == first.MaxRedirects);
            if (!settingsAreUniform)
            {
                throw new InvalidDataException(
                    "현재 화면에서는 모든 승인 대상이 동일한 maxBytes, timeoutSeconds, streams, maxRedirects 값을 사용해야 합니다.");
            }

            _approvedTargets.AddRange(targets);
            InternalTargetUrlTextBox.Text = internalTargets[0].Url;
            ExternalTargetUrlsTextBox.Text = string.Join(
                Environment.NewLine,
                externalTargets.Select(target => target.Url));
            MeasurementMaxMegabytesTextBox.Text = Math.Max(
                    1,
                    first.MaxBytes / 1024 / 1024)
                .ToString(CultureInfo.InvariantCulture);
            MeasurementTimeoutSecondsTextBox.Text =
                first.TimeoutSeconds.ToString(CultureInfo.InvariantCulture);
            MeasurementStreamsComboBox.SelectedIndex = first.Streams - 1;
            MeasurementMaxRedirectsTextBox.Text =
                first.MaxRedirects.ToString(CultureInfo.InvariantCulture);

            if (_manualTargetEntryCheckBox is not null)
            {
                _manualTargetEntryCheckBox.IsChecked = false;
            }

            ApplyTargetEntryMode();
            SetApprovedTargetStatus(
                $"승인 대상 {targets.Count}개를 로컬 설정에서 불러왔습니다. 내부 {internalTargets.Length}개, 외부 {externalTargets.Length}개이며 실제 주소는 이 상태 문구에 표시하지 않습니다.",
                isError: false);
        }
        catch (Exception exception)
        {
            _approvedTargets.Clear();
            ApprovedTargetRuntimeCatalog.Clear();
            if (_manualTargetEntryCheckBox is not null)
            {
                _manualTargetEntryCheckBox.IsChecked = true;
            }

            ApplyTargetEntryMode();
            SetApprovedTargetStatus(
                $"승인 대상 설정을 사용할 수 없어 직접 입력 모드로 전환했습니다: {exception.Message}",
                isError: true);
        }
    }

    private void ApplyTargetEntryMode()
    {
        bool manualMode = _manualTargetEntryCheckBox?.IsChecked == true
            || _approvedTargets.Count == 0;

        if (manualMode)
        {
            ApprovedTargetRuntimeCatalog.Clear();
        }
        else
        {
            ApprovedTargetRuntimeCatalog.Replace(_approvedTargets);
        }

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
        string localApplicationData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);
        string userPath = Path.Combine(
            localApplicationData,
            "WLAN Live Path Tester KO",
            "targets.local.json");
        string portablePath = Path.Combine(
            AppContext.BaseDirectory,
            "config",
            "targets.local.json");

        if (!string.IsNullOrWhiteSpace(localApplicationData)
            && File.Exists(userPath))
        {
            return userPath;
        }

        return File.Exists(portablePath) ? portablePath : null;
    }

    private void SetApprovedTargetStatus(string message, bool isError)
    {
        if (_approvedTargetStatusText is null)
        {
            return;
        }

        _approvedTargetStatusText.Margin = new Thickness(0, 6, 0, 0);
        _approvedTargetStatusText.Foreground = isError
            ? Brushes.DarkRed
            : new SolidColorBrush(Color.FromRgb(86, 101, 115));
        _approvedTargetStatusText.Text = message;
    }
}
