using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WlanLivePathTester.Core.Observation;
using WlanLivePathTester.Windows.Observation;

namespace WlanLivePathTester.App;

public partial class MainWindow
{
    private readonly BrowserObservationRunner _browserObservationRunner = new();
    private CancellationTokenSource? _observationCancellation;
    private BrowserObservationResult? _lastBrowserObservationResult;
    private TextBox? _observationDurationTextBox;
    private Button? _startObservationButton;
    private Button? _stopObservationButton;
    private TextBlock? _observationResultText;
    private bool _observationTabAdded;

    protected override void OnInitialized(EventArgs e)
    {
        base.OnInitialized(e);
        Loaded += OnObservationHostLoaded;
        Closed += OnObservationHostClosed;
    }

    private void OnObservationHostLoaded(object sender, RoutedEventArgs e)
    {
        if (_observationTabAdded)
        {
            return;
        }

        TabControl? tabControl = FindVisualDescendant<TabControl>(this);
        if (tabControl is null)
        {
            return;
        }

        tabControl.Items.Add(CreateObservationTab());
        _observationTabAdded = true;
    }

    private void OnObservationHostClosed(object? sender, EventArgs e)
    {
        _observationCancellation?.Cancel();
        _observationCancellation?.Dispose();
        _observationCancellation = null;
    }

    private TabItem CreateObservationTab()
    {
        _observationDurationTextBox = new TextBox
        {
            Width = 80,
            Text = "30",
            HorizontalContentAlignment = HorizontalAlignment.Right,
            VerticalContentAlignment = VerticalAlignment.Center
        };

        _startObservationButton = new Button
        {
            Content = "브라우저 관찰 시작",
            MinWidth = 160,
            Padding = new Thickness(12, 8, 12, 8)
        };
        _startObservationButton.Click += OnStartObservationClick;

        _stopObservationButton = new Button
        {
            Content = "관찰 중지",
            MinWidth = 100,
            Padding = new Thickness(12, 8, 12, 8),
            IsEnabled = false
        };
        _stopObservationButton.Click += OnStopObservationClick;

        _observationResultText = new TextBlock
        {
            Margin = new Thickness(0, 16, 0, 0),
            FontFamily = new FontFamily("Consolas"),
            TextWrapping = TextWrapping.Wrap,
            Text = "아직 관찰하지 않았습니다."
        };

        StackPanel durationRow = new()
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 16, 0, 0)
        };
        durationRow.Children.Add(new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            Text = "실제 관찰 시간(초)"
        });
        durationRow.Children.Add(new Border
        {
            Width = 12
        });
        durationRow.Children.Add(_observationDurationTextBox);
        durationRow.Children.Add(new TextBlock
        {
            Margin = new Thickness(10, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = new SolidColorBrush(Color.FromRgb(86, 101, 115)),
            Text = "5~600초 · 시작 전 백그라운드 기준 수집 3초가 추가됩니다."
        });

        StackPanel buttonRow = new()
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 16, 0, 0)
        };
        buttonRow.Children.Add(_startObservationButton);
        buttonRow.Children.Add(new Border { Width = 10 });
        buttonRow.Children.Add(_stopObservationButton);

        StackPanel content = new();
        content.Children.Add(new TextBlock
        {
            FontSize = 22,
            FontWeight = FontWeights.SemiBold,
            Text = "브라우저 다운로드 관찰"
        });
        content.Children.Add(new TextBlock
        {
            Margin = new Thickness(0, 8, 0, 0),
            Foreground = new SolidColorBrush(Color.FromRgb(86, 101, 115)),
            TextWrapping = TextWrapping.Wrap,
            Text = "이 모드는 프로그램이 외부 요청을 만들지 않습니다. Edge·Chrome 등에서 직접 다운로드하는 동안 Wi-Fi 인터페이스 전체 수신량과 WLAN 상태 변화를 관찰합니다."
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
                Text = "사용 순서: 관찰 시작 → 3초 기준 수집 완료 안내 확인 → 브라우저에서 다운로드 시작. 다른 프로그램의 트래픽도 합산되므로 프로세스별 속도라고 해석하면 안 됩니다."
            }
        });
        content.Children.Add(durationRow);
        content.Children.Add(buttonRow);
        content.Children.Add(new Border
        {
            Margin = new Thickness(0, 20, 0, 0),
            Padding = new Thickness(16),
            CornerRadius = new CornerRadius(8),
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(216, 221, 227)),
            BorderThickness = new Thickness(1),
            Child = _observationResultText
        });

        return new TabItem
        {
            Header = "브라우저 관찰",
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

    private async void OnStartObservationClick(object sender, RoutedEventArgs e)
    {
        if (_observationCancellation is not null)
        {
            return;
        }

        if (_measurementCancellation is not null)
        {
            SetObservationResult("내부·외부 다운로드 측정이 진행 중입니다. 해당 측정을 중지한 뒤 브라우저 관찰을 시작하십시오.");
            return;
        }

        if (_observationDurationTextBox is null
            || !int.TryParse(_observationDurationTextBox.Text.Trim(), out int durationSeconds)
            || durationSeconds is < 5 or > 600)
        {
            SetObservationResult("관찰 시간은 5~600초 범위의 정수로 입력하십시오.");
            return;
        }

        BrowserObservationOptions options = new(
            BaselineSeconds: 3,
            ObservationSeconds: durationSeconds,
            SampleIntervalMilliseconds: 1000);

        _observationCancellation = new CancellationTokenSource();
        SetObservationRunningState(isRunning: true);
        SetObservationResult(
            "관찰 준비 중입니다. 먼저 3초 동안 백그라운드 트래픽 기준치를 수집합니다. 기준 수집 완료 안내 후 브라우저 다운로드를 시작하십시오.");

        try
        {
            Progress<BrowserObservationProgress> progress = new(OnObservationProgress);
            BrowserObservationResult result = await _browserObservationRunner.RunAsync(
                options,
                progress,
                _observationCancellation.Token);
            _lastBrowserObservationResult = result;
            SetObservationResult(FormatObservationResult(result));
        }
        catch (Exception exception)
        {
            SetObservationResult($"브라우저 관찰 중 오류가 발생했습니다: {exception.Message}");
        }
        finally
        {
            _observationCancellation.Dispose();
            _observationCancellation = null;
            SetObservationRunningState(isRunning: false);
        }
    }

    private void OnStopObservationClick(object sender, RoutedEventArgs e)
    {
        _observationCancellation?.Cancel();
        SetObservationResult("관찰 중지 요청을 처리하고 있습니다.");
    }

    private void OnObservationProgress(BrowserObservationProgress progress)
    {
        if (_observationResultText is null)
        {
            return;
        }

        BrowserObservationSample? sample = progress.LatestSample;
        StringBuilder builder = new();
        builder.AppendLine(progress.Message);
        builder.AppendLine($"경과: {progress.Elapsed.TotalSeconds:F0}초 · 남음: {Math.Max(0, progress.Remaining.TotalSeconds):F0}초");

        if (sample is not null)
        {
            builder.AppendLine($"현재 Wi-Fi 수신: {FormatNullableMbps(sample.RawReceiveMbps)}");
            if (!sample.IsBaseline)
            {
                builder.AppendLine($"기준치 제외 수신: {FormatNullableMbps(sample.AdjustedReceiveMbps)}");
            }

            builder.AppendLine($"RSSI: {(sample.RssiDbm is int rssi ? $"{rssi} dBm" : "확인 불가")} · BSSID 변경: {(sample.BssidChanged ? "있음" : "없음")}");
            if (!string.IsNullOrWhiteSpace(sample.Note))
            {
                builder.AppendLine($"관찰 메모: {sample.Note}");
            }
        }

        _observationResultText.Text = builder.ToString().TrimEnd();
    }

    private static string FormatObservationResult(BrowserObservationResult result)
    {
        StringBuilder builder = new();
        builder.AppendLine($"상태: {FormatObservationStatus(result.Status)}");
        builder.AppendLine(result.Message);

        BrowserObservationSummary? summary = result.Summary;
        if (summary is null)
        {
            return builder.ToString().TrimEnd();
        }

        builder.AppendLine($"실제 관찰 구간: {summary.ObservedDuration.TotalSeconds:F1}초 · 활성 샘플: {summary.ActiveSampleCount}개");
        builder.AppendLine($"백그라운드 기준: {summary.BaselineReceiveMbps:F1} Mbps");
        builder.AppendLine($"기준치 제외 평균: {FormatNullableMbps(summary.AverageAdjustedReceiveMbps)} · 최고: {FormatNullableMbps(summary.PeakAdjustedReceiveMbps)}");
        builder.AppendLine($"관찰 수신량: {FormatObservationBytes(summary.TotalReceiveBytes)}");
        builder.AppendLine($"일시 정지: {summary.PauseCount}회 · 급락: {summary.SuddenDropCount}회 · BSSID 변경: {summary.BssidChangeCount}회");
        builder.AppendLine($"인터페이스 변경: {summary.AdapterChangeCount}회 · 카운터 재설정: {summary.CounterResetCount}회 · WLAN 미연결 샘플: {summary.WlanDisconnectedSampleCount}개");
        builder.AppendLine($"신뢰도: {(summary.Confidence == ObservationConfidence.Medium ? "중간" : "낮음")}");
        builder.AppendLine($"한계: {summary.Limitation}");
        return builder.ToString().TrimEnd();
    }

    private void SetObservationRunningState(bool isRunning)
    {
        if (_startObservationButton is not null)
        {
            _startObservationButton.IsEnabled = !isRunning;
        }

        if (_stopObservationButton is not null)
        {
            _stopObservationButton.IsEnabled = isRunning;
        }

        if (_observationDurationTextBox is not null)
        {
            _observationDurationTextBox.IsEnabled = !isRunning;
        }

        InternalMeasureButton.IsEnabled = !isRunning;
        ExternalMeasureButton.IsEnabled = !isRunning;
    }

    private void SetObservationResult(string text)
    {
        if (_observationResultText is not null)
        {
            _observationResultText.Text = text;
        }
    }

    private static string FormatObservationStatus(BrowserObservationStatus status) =>
        status switch
        {
            BrowserObservationStatus.Success => "완료",
            BrowserObservationStatus.PartialSuccess => "일부 완료",
            BrowserObservationStatus.Canceled => "사용자 중지",
            BrowserObservationStatus.UnsupportedPlatform => "지원하지 않는 운영체제",
            BrowserObservationStatus.NoWirelessConnection => "무선 연결 없음",
            BrowserObservationStatus.InterfaceUnavailable => "인터페이스 통계 확인 불가",
            BrowserObservationStatus.InvalidOptions => "설정 오류",
            _ => "실패"
        };

    private static string FormatNullableMbps(double? value) =>
        value is double mbps ? $"{mbps:F1} Mbps" : "계산 불가";

    private static string FormatObservationBytes(long bytes) =>
        bytes >= 1024L * 1024 * 1024
            ? $"{bytes / 1024d / 1024 / 1024:F2} GiB"
            : $"{bytes / 1024d / 1024:F2} MiB";

    private static T? FindVisualDescendant<T>(DependencyObject parent)
        where T : DependencyObject
    {
        int childCount = VisualTreeHelper.GetChildrenCount(parent);
        for (int index = 0; index < childCount; index++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match)
            {
                return match;
            }

            T? nested = FindVisualDescendant<T>(child);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }
}
