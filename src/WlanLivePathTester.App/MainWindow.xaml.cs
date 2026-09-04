using System.Text;
using System.Windows;
using System.Windows.Media;
using WlanLivePathTester.Core.Measurements;
using WlanLivePathTester.Core.Models;
using WlanLivePathTester.Core.Wlan;
using WlanLivePathTester.Windows.Measurements;
using WlanLivePathTester.Windows.Proxy;
using WlanLivePathTester.Windows.Wlan;

namespace WlanLivePathTester.App;

public partial class MainWindow : Window
{
    private Action? _cancelMeasurement;
    private bool _measurementRunning;

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
            ResolveProxyRouteButton.IsEnabled = !_measurementRunning;
        }
    }

    private async void OnStartInternalMeasurementClick(object sender, RoutedEventArgs e)
    {
        string url = InternalTargetUrlTextBox.Text.Trim();
        if (!TryReadMeasurementSettings(
                out long maxBytes,
                out int timeoutSeconds,
                out int streams,
                out int maxRedirects,
                out string settingsError))
        {
            ShowMeasurementInputError(InternalMeasurementResultText, settingsError);
            return;
        }

        MeasurementTargetDefinition target = new(
            Name: "내부망 기준 대상",
            Url: url,
            PathKind: NetworkPathKind.Internal,
            RequireProxy: false,
            RequireDirect: true,
            MaxBytes: maxBytes,
            TimeoutSeconds: timeoutSeconds,
            Streams: streams,
            MaxRedirects: maxRedirects);

        await RunMeasurementOperationAsync(
            async cancellationToken =>
            {
                InternalMeasurementResultText.Foreground = Brushes.DarkSlateGray;
                InternalMeasurementResultText.Text = "내부망 DIRECT 경로를 확인하고 다운로드를 측정하고 있습니다.";

                DownloadMeasurementResult result = await DownloadMeasurementRunner.RunAsync(
                    target,
                    MeasurementHeadPreflightCheckBox.IsChecked == true,
                    cancellationToken);

                InternalMeasurementResultText.Foreground = GetMeasurementBrush(result.Status);
                InternalMeasurementResultText.Text = FormatMeasurementResult(result);
            },
            "내부망 측정 중");
    }

    private async void OnStartExternalMeasurementClick(object sender, RoutedEventArgs e)
    {
        if (!TryReadMeasurementSettings(
                out long maxBytes,
                out int timeoutSeconds,
                out int streams,
                out int maxRedirects,
                out string settingsError))
        {
            ShowMeasurementInputError(ExternalMeasurementResultText, settingsError);
            return;
        }

        string[] urls = ExternalTargetUrlsTextBox.Text
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (urls.Length is < 1 or > 4)
        {
            ShowMeasurementInputError(
                ExternalMeasurementResultText,
                "외부 URL을 한 줄에 하나씩 1~4개 입력하십시오.");
            return;
        }

        MeasurementTargetDefinition[] targets = urls
            .Select((url, index) => new MeasurementTargetDefinition(
                Name: $"외부 대상 {index + 1}",
                Url: url,
                PathKind: NetworkPathKind.External,
                RequireProxy: true,
                RequireDirect: false,
                MaxBytes: maxBytes,
                TimeoutSeconds: timeoutSeconds,
                Streams: streams,
                MaxRedirects: maxRedirects))
            .ToArray();

        await RunMeasurementOperationAsync(
            async cancellationToken =>
            {
                ExternalMeasurementResultText.Foreground = Brushes.DarkSlateGray;
                ExternalMeasurementResultText.Text = $"외부 대상 {targets.Length}개를 입력 순서대로 측정하고 있습니다.";

                IReadOnlyList<DownloadMeasurementResult> results =
                    await DownloadMeasurementRunner.RunManyAsync(
                        targets,
                        MeasurementHeadPreflightCheckBox.IsChecked == true,
                        cancellationToken);

                ExternalMeasurementResultText.Foreground = results.All(result => result.IsSuccess)
                    ? Brushes.DarkGreen
                    : results.Any(result => result.IsSuccess)
                        ? Brushes.DarkOrange
                        : Brushes.DarkRed;
                ExternalMeasurementResultText.Text = FormatMeasurementResults(results);
            },
            $"외부망 대상 {targets.Length}개 측정 중");
    }

    private void OnCancelMeasurementClick(object sender, RoutedEventArgs e)
    {
        if (!_measurementRunning || _cancelMeasurement is null)
        {
            return;
        }

        _cancelMeasurement();
        CancelMeasurementButton.IsEnabled = false;
        MeasurementStatusText.Foreground = Brushes.DarkOrange;
        MeasurementStatusText.Text = "취소 요청됨 · 현재 WinHTTP 호출이 반환된 뒤 다음 단계와 남은 대상을 중단합니다.";
    }

    private async Task RunMeasurementOperationAsync(
        Func<CancellationToken, Task> operation,
        string runningMessage)
    {
        if (_measurementRunning)
        {
            MeasurementStatusText.Foreground = Brushes.DarkOrange;
            MeasurementStatusText.Text = "다른 측정이 진행 중입니다. 완료하거나 취소한 뒤 다시 실행하십시오.";
            return;
        }

        using CancellationTokenSource cancellation = new();
        _measurementRunning = true;
        _cancelMeasurement = cancellation.Cancel;
        SetMeasurementBusy(true);
        MeasurementStatusText.Foreground = Brushes.DarkSlateGray;
        MeasurementStatusText.Text = runningMessage;

        try
        {
            await operation(cancellation.Token);
            MeasurementStatusText.Foreground = cancellation.IsCancellationRequested
                ? Brushes.DarkOrange
                : Brushes.DarkGreen;
            MeasurementStatusText.Text = cancellation.IsCancellationRequested
                ? "측정 취소 처리가 완료되었습니다."
                : "측정이 완료되었습니다.";
        }
        catch (Exception exception)
        {
            MeasurementStatusText.Foreground = Brushes.DarkRed;
            MeasurementStatusText.Text = $"측정 처리 중 오류가 발생했습니다: {exception.Message}";
        }
        finally
        {
            _cancelMeasurement = null;
            _measurementRunning = false;
            SetMeasurementBusy(false);
        }
    }

    private void SetMeasurementBusy(bool busy)
    {
        StartInternalMeasurementButton.IsEnabled = !busy;
        StartExternalMeasurementButton.IsEnabled = !busy;
        ResolveProxyRouteButton.IsEnabled = !busy;
        CancelMeasurementButton.IsEnabled = busy;
        MeasurementMaxMegabytesTextBox.IsEnabled = !busy;
        MeasurementTimeoutSecondsTextBox.IsEnabled = !busy;
        MeasurementStreamsComboBox.IsEnabled = !busy;
        MeasurementMaxRedirectsTextBox.IsEnabled = !busy;
        MeasurementHeadPreflightCheckBox.IsEnabled = !busy;
        InternalTargetUrlTextBox.IsEnabled = !busy;
        ExternalTargetUrlsTextBox.IsEnabled = !busy;
    }

    private bool TryReadMeasurementSettings(
        out long maxBytes,
        out int timeoutSeconds,
        out int streams,
        out int maxRedirects,
        out string error)
    {
        maxBytes = 0;
        timeoutSeconds = 0;
        streams = MeasurementStreamsComboBox.SelectedIndex + 1;
        maxRedirects = 0;
        error = string.Empty;

        if (!int.TryParse(MeasurementMaxMegabytesTextBox.Text.Trim(), out int maxMegabytes)
            || maxMegabytes is < 1 or > 1024)
        {
            error = "최대 수신량은 1~1024MB 범위의 정수여야 합니다.";
            return false;
        }

        if (!int.TryParse(MeasurementTimeoutSecondsTextBox.Text.Trim(), out timeoutSeconds)
            || timeoutSeconds is < 5 or > 300)
        {
            error = "제한 시간은 5~300초 범위의 정수여야 합니다.";
            return false;
        }

        if (streams is < 1 or > 4)
        {
            error = "스트림 수는 1~4 범위여야 합니다.";
            return false;
        }

        if (!int.TryParse(MeasurementMaxRedirectsTextBox.Text.Trim(), out maxRedirects)
            || maxRedirects is < 0 or > 10)
        {
            error = "최대 리다이렉트 수는 0~10 범위의 정수여야 합니다.";
            return false;
        }

        maxBytes = checked((long)maxMegabytes * 1024 * 1024);
        return true;
    }

    private static void ShowMeasurementInputError(TextBlock target, string message)
    {
        target.Foreground = Brushes.DarkRed;
        target.Text = $"입력 오류: {message}";
    }

    private static string FormatMeasurementResults(
        IReadOnlyList<DownloadMeasurementResult> results)
    {
        if (results.Count == 0)
        {
            return "측정 결과가 없습니다.";
        }

        StringBuilder builder = new();
        for (int index = 0; index < results.Count; index++)
        {
            if (index > 0)
            {
                builder.AppendLine();
                builder.AppendLine(new string('-', 52));
            }

            builder.Append(FormatMeasurementResult(results[index]));
        }

        return builder.ToString().TrimEnd();
    }

    private static string FormatMeasurementResult(DownloadMeasurementResult result)
    {
        StringBuilder builder = new();
        builder.AppendLine($"대상: {result.TargetName}");
        builder.AppendLine($"상태: {FormatMeasurementStatus(result.Status)}");
        builder.AppendLine($"경로: {FormatExpectedPath(result.PathKind)} / 실제 프록시: {FormatProxyUsage(result.ProxyWasUsed)}");
        builder.AppendLine($"HTTP: {result.HttpStatusCode?.ToString() ?? "없음"} / 리다이렉트: {result.RedirectCount}회");
        builder.AppendLine($"수신량: {FormatBytes(result.BytesReceived)} / 소요시간: {result.Duration.TotalSeconds:F2}초");
        builder.AppendLine($"평균 처리량: {FormatMbps(result.AverageMbps)} / 최고 구간: {FormatMbps(result.PeakMbps)}");
        builder.AppendLine($"TTFB: {FormatMilliseconds(result.TimeToFirstByte)} / 스트림: {result.StreamsCompleted}/{result.StreamsRequested}");
        builder.AppendLine($"최종 URL: {result.FinalUrl}");
        builder.AppendLine($"응답 메타데이터: {FormatResponseMetadata(result.ResponseHeaders)}");

        if (result.Samples.Count > 0)
        {
            double minimum = result.Samples.Min(sample => sample.Mbps);
            double maximum = result.Samples.Max(sample => sample.Mbps);
            builder.AppendLine($"구간 샘플: {result.Samples.Count}개 / {minimum:F1}~{maximum:F1} Mbps");
        }

        if (!string.IsNullOrWhiteSpace(result.ErrorCode))
        {
            builder.AppendLine($"오류 코드: {result.ErrorCode}");
        }

        builder.Append($"설명: {result.Message}");
        return builder.ToString();
    }

    private static string FormatResponseMetadata(
        IReadOnlyDictionary<string, string> headers)
    {
        List<string> values = [];
        AddHeader(values, headers, "Age");
        AddHeader(values, headers, "Cache-Status");
        AddHeader(values, headers, "X-Cache");
        AddHeader(values, headers, "Content-Length");
        AddHeader(values, headers, "Content-Range");

        if (headers.ContainsKey("Via"))
        {
            values.Add("Via=[설정됨]");
        }

        return values.Count == 0
            ? "선택 헤더 없음"
            : string.Join(" · ", values);
    }

    private static void AddHeader(
        ICollection<string> values,
        IReadOnlyDictionary<string, string> headers,
        string name)
    {
        if (headers.TryGetValue(name, out string? value)
            && !string.IsNullOrWhiteSpace(value))
        {
            values.Add($"{name}={value}");
        }
    }

    private static Brush GetMeasurementBrush(MeasurementStatus status) =>
        status switch
        {
            MeasurementStatus.Success => Brushes.DarkGreen,
            MeasurementStatus.PartialSuccess => Brushes.DarkOrange,
            MeasurementStatus.Canceled => Brushes.DarkOrange,
            _ => Brushes.DarkRed
        };

    private static string FormatMeasurementStatus(MeasurementStatus status) =>
        status switch
        {
            MeasurementStatus.NotRun => "미실행",
            MeasurementStatus.Success => "성공",
            MeasurementStatus.PartialSuccess => "부분 성공",
            MeasurementStatus.Failed => "실패",
            MeasurementStatus.TimedOut => "시간 초과",
            MeasurementStatus.Canceled => "취소",
            MeasurementStatus.Blocked => "정책 차단",
            MeasurementStatus.ProxyAuthenticationRequired => "프록시 인증 실패",
            MeasurementStatus.PathMismatch => "기대 경로 불일치",
            _ => status.ToString()
        };

    private static string FormatProxyUsage(bool? proxyWasUsed) =>
        proxyWasUsed switch
        {
            true => "사용",
            false => "미사용",
            null => "확인 불가"
        };

    private static string FormatBytes(long bytes) =>
        $"{bytes / 1024d / 1024d:F2} MiB";

    private static string FormatMbps(double? value) =>
        value is double mbps ? $"{mbps:F1} Mbps" : "계산 안 함";

    private static string FormatMilliseconds(TimeSpan? value) =>
        value is TimeSpan duration ? $"{duration.TotalMilliseconds:F0} ms" : "확인 불가";

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
