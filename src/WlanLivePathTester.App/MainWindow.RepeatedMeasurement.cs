using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WlanLivePathTester.Core.Measurements;
using WlanLivePathTester.Core.Models;
using WlanLivePathTester.Windows.Measurements;

namespace WlanLivePathTester.App;

public partial class MainWindow
{
    private const long MaximumRepeatedOperationBytes =
        2L * 1024 * 1024 * 1024;

    private ComboBox? _repeatCountComboBox;
    private CheckBox? _repeatWarmupCheckBox;
    private TextBox? _repeatDelayTextBox;
    private Button? _repeatInternalButton;
    private Button? _repeatExternalButton;
    private TextBlock? _repeatResultText;
    private bool _repeatedMeasurementTabAdded;

    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
        EnsureRepeatedMeasurementTab();
    }

    private void EnsureRepeatedMeasurementTab()
    {
        if (_repeatedMeasurementTabAdded)
        {
            return;
        }

        TabControl? tabControl = FindVisualDescendant<TabControl>(this);
        if (tabControl is null)
        {
            return;
        }

        tabControl.Items.Add(CreateRepeatedMeasurementTab());
        StartInternalMeasurementButton.IsEnabledChanged +=
            OnBaseMeasurementAvailabilityChanged;
        _repeatedMeasurementTabAdded = true;
        UpdateRepeatedMeasurementButtons();
    }

    private TabItem CreateRepeatedMeasurementTab()
    {
        _repeatCountComboBox = new ComboBox
        {
            Width = 90,
            SelectedIndex = 2,
            Padding = new Thickness(7, 4, 7, 4)
        };
        for (int count = 1; count <= 5; count++)
        {
            _repeatCountComboBox.Items.Add(new ComboBoxItem
            {
                Content = count.ToString()
            });
        }

        _repeatWarmupCheckBox = new CheckBox
        {
            IsChecked = true,
            VerticalAlignment = VerticalAlignment.Center,
            Content = "예열 1회 사용"
        };
        _repeatDelayTextBox = new TextBox
        {
            Width = 100,
            Text = "500",
            HorizontalContentAlignment = HorizontalAlignment.Right,
            VerticalContentAlignment = VerticalAlignment.Center
        };

        _repeatInternalButton = new Button
        {
            Content = "현재 내부 URL 반복 측정",
            MinWidth = 210,
            Padding = new Thickness(12, 8, 12, 8)
        };
        _repeatInternalButton.Click += OnStartRepeatedInternalClick;

        _repeatExternalButton = new Button
        {
            Content = "현재 외부 URL 반복 측정",
            MinWidth = 210,
            Padding = new Thickness(12, 8, 12, 8)
        };
        _repeatExternalButton.Click += OnStartRepeatedExternalClick;

        _repeatResultText = new TextBlock
        {
            FontFamily = new FontFamily("Consolas"),
            TextWrapping = TextWrapping.Wrap,
            Text = "아직 반복 측정하지 않았습니다."
        };

        Grid settingsGrid = new()
        {
            Margin = new Thickness(0, 14, 0, 0)
        };
        settingsGrid.ColumnDefinitions.Add(new ColumnDefinition());
        settingsGrid.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(18)
        });
        settingsGrid.ColumnDefinitions.Add(new ColumnDefinition());
        settingsGrid.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(18)
        });
        settingsGrid.ColumnDefinitions.Add(new ColumnDefinition());

        FrameworkElement repeatCountPanel = CreateRepeatSettingPanel(
            "본 측정 횟수",
            _repeatCountComboBox,
            "1~5회 · 대표값은 중앙값");
        Grid.SetColumn(repeatCountPanel, 0);
        settingsGrid.Children.Add(repeatCountPanel);

        FrameworkElement warmupPanel = CreateRepeatSettingPanel(
            "예열 측정",
            _repeatWarmupCheckBox,
            "대표값과 편차 계산에서 제외");
        Grid.SetColumn(warmupPanel, 2);
        settingsGrid.Children.Add(warmupPanel);

        FrameworkElement delayPanel = CreateRepeatSettingPanel(
            "측정 간 대기(ms)",
            _repeatDelayTextBox,
            "0~10000ms");
        Grid.SetColumn(delayPanel, 4);
        settingsGrid.Children.Add(delayPanel);

        StackPanel buttons = new()
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 16, 0, 0)
        };
        buttons.Children.Add(_repeatInternalButton);
        buttons.Children.Add(new Border { Width = 12 });
        buttons.Children.Add(_repeatExternalButton);

        StackPanel content = new();
        content.Children.Add(new TextBlock
        {
            FontSize = 22,
            FontWeight = FontWeights.SemiBold,
            Text = "반복 다운로드 측정"
        });
        content.Children.Add(new TextBlock
        {
            Margin = new Thickness(0, 8, 0, 0),
            Foreground = new SolidColorBrush(Color.FromRgb(86, 101, 115)),
            TextWrapping = TextWrapping.Wrap,
            Text = "측정 화면에 입력되거나 승인 목록에서 적용한 URL과 공통 제한값을 사용합니다. 선택적 예열 뒤 본 측정을 반복하고 중앙값·최소·최대·변동계수로 대표값의 안정성을 판정합니다."
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
                Text = "반복 횟수만큼 실제 HEAD/GET 다운로드가 발생합니다. 대상 하나와 작업 전체의 최대 예상 수신량은 각각 2GiB로 제한하며, 외부 URL은 최대 4개를 순차 측정합니다."
            }
        });
        content.Children.Add(settingsGrid);
        content.Children.Add(buttons);
        content.Children.Add(new Border
        {
            Margin = new Thickness(0, 18, 0, 0),
            Padding = new Thickness(16),
            CornerRadius = new CornerRadius(8),
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(216, 221, 227)),
            BorderThickness = new Thickness(1),
            Child = _repeatResultText
        });

        return new TabItem
        {
            Header = "반복 측정",
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

    private static FrameworkElement CreateRepeatSettingPanel(
        string title,
        Control control,
        string description)
    {
        StackPanel panel = new();
        panel.Children.Add(new TextBlock
        {
            FontWeight = FontWeights.SemiBold,
            Text = title
        });
        control.Margin = new Thickness(0, 6, 0, 0);
        panel.Children.Add(control);
        panel.Children.Add(new TextBlock
        {
            Margin = new Thickness(0, 5, 0, 0),
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(112, 123, 124)),
            Text = description
        });
        return panel;
    }

    private async void OnStartRepeatedInternalClick(
        object sender,
        RoutedEventArgs e)
    {
        if (!TryCreateRepeatedTargets(
                NetworkPathKind.Internal,
                out MeasurementTargetDefinition[] targets,
                out RepeatedMeasurementPlan plan,
                out string error))
        {
            SetRepeatedResult(error, Brushes.DarkRed);
            return;
        }

        await RunRepeatedOperationAsync(targets, plan);
    }

    private async void OnStartRepeatedExternalClick(
        object sender,
        RoutedEventArgs e)
    {
        if (!TryCreateRepeatedTargets(
                NetworkPathKind.External,
                out MeasurementTargetDefinition[] targets,
                out RepeatedMeasurementPlan plan,
                out string error))
        {
            SetRepeatedResult(error, Brushes.DarkRed);
            return;
        }

        await RunRepeatedOperationAsync(targets, plan);
    }

    private async Task RunRepeatedOperationAsync(
        IReadOnlyList<MeasurementTargetDefinition> targets,
        RepeatedMeasurementPlan plan)
    {
        long plannedBytes = targets.Sum(target =>
            plan.GetPlannedMaximumBytes(target));
        string plannedText = FormatRepeatedBytes(plannedBytes);

        await RunMeasurementOperationAsync(
            async cancellationToken =>
            {
                List<RepeatedMeasurementResult> results = [];
                Progress<RepeatedMeasurementProgress> progress = new(
                    update => OnRepeatedMeasurementProgress(
                        update,
                        targets.Count,
                        results.Count,
                        plannedText));

                for (int index = 0; index < targets.Count; index++)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }

                    MeasurementTargetDefinition target = targets[index];
                    SetRepeatedResult(
                        $"대상 {index + 1}/{targets.Count} 반복 측정 중 · 최대 예상 수신량 {plannedText}",
                        Brushes.DarkSlateGray);
                    RepeatedMeasurementResult result =
                        await RepeatedMeasurementRunner.RunAsync(
                            target,
                            plan,
                            MeasurementHeadPreflightCheckBox.IsChecked == true,
                            progress,
                            cancellationToken);
                    results.Add(result);
                }

                SetRepeatedResult(
                    FormatRepeatedResults(results, plannedBytes),
                    results.Count > 0
                        && results.All(result =>
                            result.Summary.Confidence
                                is RepeatedMeasurementConfidence.High
                                    or RepeatedMeasurementConfidence.Medium)
                        ? Brushes.DarkGreen
                        : results.Any(result =>
                            result.Summary.MedianMbps.HasValue)
                            ? Brushes.DarkOrange
                            : Brushes.DarkRed);
            },
            $"반복 측정 준비 중 · 대상 {targets.Count}개 · 최대 예상 수신량 {plannedText}");
    }

    private void OnRepeatedMeasurementProgress(
        RepeatedMeasurementProgress progress,
        int targetCount,
        int completedTargetCount,
        string plannedText)
    {
        StringBuilder builder = new();
        builder.AppendLine($"대상: {completedTargetCount + 1}/{targetCount} · 반복 단계: {progress.CompletedRunCount}/{progress.TotalRunCount}");
        builder.AppendLine(progress.Message);
        builder.AppendLine($"작업 최대 예상 수신량: {plannedText}");
        if (progress.LatestResult is DownloadMeasurementResult latest)
        {
            builder.AppendLine($"최근 상태: {FormatMeasurementStatus(latest.Status)} · 평균 {FormatMbps(latest.AverageMbps)} · 수신 {FormatBytes(latest.BytesReceived)}");
        }

        SetRepeatedResult(builder.ToString().TrimEnd(), Brushes.DarkSlateGray);
    }

    private bool TryCreateRepeatedTargets(
        NetworkPathKind pathKind,
        out MeasurementTargetDefinition[] targets,
        out RepeatedMeasurementPlan plan,
        out string error)
    {
        targets = [];
        plan = RepeatedMeasurementPlan.Recommended;
        error = string.Empty;

        if (!TryReadMeasurementSettings(
                out long maxBytes,
                out int timeoutSeconds,
                out int streams,
                out int maxRedirects,
                out string settingsError))
        {
            error = $"입력 오류: {settingsError}";
            return false;
        }

        if (_repeatCountComboBox is null
            || _repeatDelayTextBox is null
            || _repeatWarmupCheckBox is null)
        {
            error = "반복 측정 화면이 아직 준비되지 않았습니다.";
            return false;
        }

        int repeatCount = _repeatCountComboBox.SelectedIndex + 1;
        if (!int.TryParse(_repeatDelayTextBox.Text.Trim(), out int delayMilliseconds))
        {
            error = "측정 간 대기 시간은 0~10000ms 범위의 정수여야 합니다.";
            return false;
        }

        plan = new RepeatedMeasurementPlan(
            RepeatCount: repeatCount,
            IncludeWarmup: _repeatWarmupCheckBox.IsChecked == true,
            DelayMilliseconds: delayMilliseconds);
        IReadOnlyList<string> planErrors = plan.Validate();
        if (planErrors.Count > 0)
        {
            error = string.Join(" ", planErrors);
            return false;
        }

        string[] urls = pathKind == NetworkPathKind.Internal
            ? [InternalTargetUrlTextBox.Text.Trim()]
            : ExternalTargetUrlsTextBox.Text
                .Split(
                    ['\r', '\n'],
                    StringSplitOptions.RemoveEmptyEntries
                    | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

        if (pathKind == NetworkPathKind.Internal
            && string.IsNullOrWhiteSpace(urls[0]))
        {
            error = "내부 측정 화면에 기준 URL을 입력하거나 승인 대상에서 적용하십시오.";
            return false;
        }

        if (pathKind == NetworkPathKind.External
            && urls.Length is < 1 or > 4)
        {
            error = "외부 측정 화면에 URL을 한 줄에 하나씩 1~4개 입력하거나 승인 대상에서 적용하십시오.";
            return false;
        }

        targets = urls.Select((url, index) =>
            new MeasurementTargetDefinition(
                Name: pathKind == NetworkPathKind.Internal
                    ? "내부망 반복 대상"
                    : $"외부 반복 대상 {index + 1}",
                Url: url,
                PathKind: pathKind,
                RequireProxy: pathKind == NetworkPathKind.External,
                RequireDirect: pathKind == NetworkPathKind.Internal,
                MaxBytes: maxBytes,
                TimeoutSeconds: timeoutSeconds,
                Streams: streams,
                MaxRedirects: maxRedirects))
            .ToArray();

        List<string> targetErrors = [];
        long totalPlannedBytes = 0;
        foreach (MeasurementTargetDefinition target in targets)
        {
            IReadOnlyList<string> errors = plan.ValidateForTarget(target);
            if (errors.Count > 0)
            {
                targetErrors.AddRange(errors);
                continue;
            }

            totalPlannedBytes = checked(
                totalPlannedBytes + plan.GetPlannedMaximumBytes(target));
        }

        if (targetErrors.Count > 0)
        {
            error = string.Join(" ", targetErrors.Distinct());
            return false;
        }

        if (totalPlannedBytes > MaximumRepeatedOperationBytes)
        {
            error = $"이번 반복 작업의 최대 예상 수신량은 {FormatRepeatedBytes(totalPlannedBytes)}입니다. 작업 전체 제한 2GiB 이하가 되도록 대상 수, 반복 횟수 또는 최대 수신량을 줄이십시오.";
            return false;
        }

        return true;
    }

    private static string FormatRepeatedResults(
        IReadOnlyList<RepeatedMeasurementResult> results,
        long plannedBytes)
    {
        if (results.Count == 0)
        {
            return "완료된 반복 측정 결과가 없습니다.";
        }

        StringBuilder builder = new();
        builder.AppendLine($"반복 측정 대상: {results.Count}개 · 최대 예상 수신량: {FormatRepeatedBytes(plannedBytes)}");
        builder.AppendLine($"실제 총 수신량: {FormatRepeatedBytes(results.Sum(result => result.TotalBytesReceived))}");

        for (int index = 0; index < results.Count; index++)
        {
            RepeatedMeasurementResult result = results[index];
            RepeatedMeasurementSummary summary = result.Summary;
            if (index > 0)
            {
                builder.AppendLine();
                builder.AppendLine(new string('-', 58));
            }

            builder.AppendLine($"대상 {index + 1}: {result.TargetName}");
            builder.AppendLine($"경로: {FormatExpectedPath(result.PathKind)}");
            builder.AppendLine($"계획/완료/성공: {summary.PlannedMeasurementCount}/{summary.CompletedMeasurementCount}/{summary.SuccessfulMeasurementCount} · 실패 {summary.FailedMeasurementCount} · 미완료 {summary.NotCompletedMeasurementCount}");
            builder.AppendLine($"대표 중앙값: {FormatMbps(summary.MedianMbps)} · 평균: {FormatMbps(summary.MeanMbps)}");
            builder.AppendLine($"최소~최대: {FormatMbps(summary.MinimumMbps)} ~ {FormatMbps(summary.MaximumMbps)}");
            builder.AppendLine($"표준편차: {FormatMbps(summary.StandardDeviationMbps)} · 변동계수: {FormatVariation(summary.CoefficientOfVariation)}");
            builder.AppendLine($"대표 본 측정: {(summary.RepresentativeSequence.HasValue ? $"{summary.RepresentativeSequence}회차" : "없음")} · 캐시 적중 가능성: {(summary.CacheHitPossible ? "있음" : "확인되지 않음")}");
            builder.AppendLine($"신뢰도: {FormatRepeatedConfidence(summary.Confidence)}");
            builder.AppendLine($"판정 근거: {string.Join(" ", summary.ConfidenceReasons)}");
        }

        return builder.ToString().TrimEnd();
    }

    private static string FormatRepeatedConfidence(
        RepeatedMeasurementConfidence confidence) =>
        confidence switch
        {
            RepeatedMeasurementConfidence.High => "높음",
            RepeatedMeasurementConfidence.Medium => "중간",
            RepeatedMeasurementConfidence.Low => "낮음",
            _ => "평가 안 함"
        };

    private static string FormatVariation(double? value) =>
        value.HasValue ? value.Value.ToString("P1") : "계산 안 함";

    private static string FormatRepeatedBytes(long bytes) =>
        bytes >= 1024L * 1024 * 1024
            ? $"{bytes / 1024d / 1024 / 1024:F2} GiB"
            : $"{bytes / 1024d / 1024:F2} MiB";

    private void SetRepeatedResult(string text, Brush brush)
    {
        if (_repeatResultText is null)
        {
            return;
        }

        _repeatResultText.Text = text;
        _repeatResultText.Foreground = brush;
    }

    private void OnBaseMeasurementAvailabilityChanged(
        object sender,
        DependencyPropertyChangedEventArgs e) =>
        UpdateRepeatedMeasurementButtons();

    private void UpdateRepeatedMeasurementButtons()
    {
        bool enabled = StartInternalMeasurementButton.IsEnabled
            && !_measurementRunning
            && _observationCancellation is null;
        if (_repeatInternalButton is not null)
        {
            _repeatInternalButton.IsEnabled = enabled;
        }

        if (_repeatExternalButton is not null)
        {
            _repeatExternalButton.IsEnabled = enabled;
        }

        if (_repeatCountComboBox is not null)
        {
            _repeatCountComboBox.IsEnabled = enabled;
        }

        if (_repeatWarmupCheckBox is not null)
        {
            _repeatWarmupCheckBox.IsEnabled = enabled;
        }

        if (_repeatDelayTextBox is not null)
        {
            _repeatDelayTextBox.IsEnabled = enabled;
        }
    }
}
