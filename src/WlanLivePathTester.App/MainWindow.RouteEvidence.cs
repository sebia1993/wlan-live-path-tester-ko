using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WlanLivePathTester.Core.NetworkEnvironment;
using WlanLivePathTester.Core.Routing;
using WlanLivePathTester.Windows.Routing;

namespace WlanLivePathTester.App;

public partial class MainWindow
{
    private TextBox? _routeEvidenceTargetTextBox;
    private ComboBox? _routeEvidencePurposeComboBox;
    private Button? _analyzeRouteEvidenceButton;
    private Button? _cancelRouteEvidenceButton;
    private TextBlock? _routeEvidenceResultText;
    private CancellationTokenSource? _routeEvidenceCancellation;
    private bool _routeEvidenceTabAdded;

    internal void EnsureRouteEvidenceTab()
    {
        if (_routeEvidenceTabAdded)
        {
            return;
        }

        TabControl? tabControl = FindVisualDescendant<TabControl>(this);
        if (tabControl is null)
        {
            return;
        }

        tabControl.Items.Insert(
            Math.Min(3, tabControl.Items.Count),
            CreateRouteEvidenceTab());
        _routeEvidenceTabAdded = true;
        Closed += OnRouteEvidenceWindowClosed;
    }

    private TabItem CreateRouteEvidenceTab()
    {
        _routeEvidenceTargetTextBox = new TextBox
        {
            MinWidth = 420,
            Padding = new Thickness(8, 6, 8, 6),
            VerticalContentAlignment = VerticalAlignment.Center,
            ToolTip = "HTTP/HTTPS URL, 호스트 이름, IP 주소 또는 host:port"
        };

        _routeEvidencePurposeComboBox = new ComboBox
        {
            MinWidth = 250,
            Padding = new Thickness(8, 5, 8, 5),
            SelectedIndex = 0
        };
        _routeEvidencePurposeComboBox.Items.Add(
            CreatePurposeItem(
                "내부 DIRECT 측정 대상",
                RouteProbePurpose.InternalDirectTarget));
        _routeEvidencePurposeComboBox.Items.Add(
            CreatePurposeItem(
                "프록시 엔드포인트",
                RouteProbePurpose.ProxyEndpoint));
        _routeEvidencePurposeComboBox.Items.Add(
            CreatePurposeItem(
                "외부 사이트 참고 경로",
                RouteProbePurpose.ExternalTargetReference));
        _routeEvidencePurposeComboBox.Items.Add(
            CreatePurposeItem(
                "수동 목적지",
                RouteProbePurpose.ManualDestination));

        Button useInternalButton = new()
        {
            Content = "현재 내부 URL 가져오기",
            MinWidth = 170,
            Padding = new Thickness(10, 7, 10, 7)
        };
        useInternalButton.Click += OnUseInternalRouteTargetClick;

        Button useExternalButton = new()
        {
            Content = "첫 외부 URL 참고",
            MinWidth = 150,
            Padding = new Thickness(10, 7, 10, 7)
        };
        useExternalButton.Click += OnUseExternalRouteTargetClick;

        _analyzeRouteEvidenceButton = new Button
        {
            Content = "Windows 최적 인터페이스 확인",
            MinWidth = 210,
            Padding = new Thickness(12, 8, 12, 8)
        };
        _analyzeRouteEvidenceButton.Click += OnAnalyzeRouteEvidenceClick;

        _cancelRouteEvidenceButton = new Button
        {
            Content = "확인 취소",
            MinWidth = 100,
            Padding = new Thickness(12, 8, 12, 8),
            IsEnabled = false
        };
        _cancelRouteEvidenceButton.Click += OnCancelRouteEvidenceClick;

        _routeEvidenceResultText = new TextBlock
        {
            FontFamily = new FontFamily("Consolas"),
            TextWrapping = TextWrapping.Wrap,
            Text = "아직 목적지별 Windows 최적 인터페이스를 확인하지 않았습니다."
        };

        Grid inputGrid = new()
        {
            Margin = new Thickness(0, 16, 0, 0)
        };
        inputGrid.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(1, GridUnitType.Star)
        });
        inputGrid.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(14)
        });
        inputGrid.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = GridLength.Auto
        });

        StackPanel targetPanel = new();
        targetPanel.Children.Add(new TextBlock
        {
            FontWeight = FontWeights.SemiBold,
            Text = "분석 대상"
        });
        _routeEvidenceTargetTextBox.Margin = new Thickness(0, 6, 0, 0);
        targetPanel.Children.Add(_routeEvidenceTargetTextBox);
        Grid.SetColumn(targetPanel, 0);
        inputGrid.Children.Add(targetPanel);

        StackPanel purposePanel = new();
        purposePanel.Children.Add(new TextBlock
        {
            FontWeight = FontWeights.SemiBold,
            Text = "해석 목적"
        });
        _routeEvidencePurposeComboBox.Margin = new Thickness(0, 6, 0, 0);
        purposePanel.Children.Add(_routeEvidencePurposeComboBox);
        Grid.SetColumn(purposePanel, 2);
        inputGrid.Children.Add(purposePanel);

        StackPanel sourceButtons = new()
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 12, 0, 0)
        };
        sourceButtons.Children.Add(useInternalButton);
        sourceButtons.Children.Add(new Border { Width = 10 });
        sourceButtons.Children.Add(useExternalButton);

        StackPanel actionButtons = new()
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 16, 0, 0)
        };
        actionButtons.Children.Add(_analyzeRouteEvidenceButton);
        actionButtons.Children.Add(new Border { Width = 10 });
        actionButtons.Children.Add(_cancelRouteEvidenceButton);

        StackPanel content = new();
        content.Children.Add(new TextBlock
        {
            FontSize = 22,
            FontWeight = FontWeights.SemiBold,
            Text = "목적지별 Windows 라우팅 근거"
        });
        content.Children.Add(new TextBlock
        {
            Margin = new Thickness(0, 8, 0, 0),
            Foreground = new SolidColorBrush(Color.FromRgb(86, 101, 115)),
            TextWrapping = TextWrapping.Wrap,
            Text = "대상 주소별로 Windows GetBestInterfaceEx가 선택하는 로컬 인터페이스를 확인합니다. 내부 DIRECT 요청이 실제 Wi-Fi 대신 유선·VPN·가상 NIC를 선택하는지 판단하는 보조 근거입니다."
        });
        content.Children.Add(new Border
        {
            Margin = new Thickness(0, 16, 0, 0),
            Padding = new Thickness(14),
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(Color.FromRgb(232, 246, 243)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(115, 198, 182)),
            BorderThickness = new Thickness(1),
            Child = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Text = "IP 주소를 입력하면 네트워크 요청 없이 로컬 라우팅 테이블만 확인합니다. 호스트 이름 또는 URL을 입력하면 사용자가 버튼을 누른 시점에 DNS 확인이 발생하지만 HTTP 요청·외부 API 호출·업로드는 하지 않습니다. 결과에는 해석한 IP·게이트웨이·DNS 주소 원문을 표시하지 않습니다."
            }
        });
        content.Children.Add(new Border
        {
            Margin = new Thickness(0, 12, 0, 0),
            Padding = new Thickness(14),
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(Color.FromRgb(255, 248, 231)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(232, 206, 138)),
            BorderThickness = new Thickness(1),
            Child = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Text = "회사 프록시 환경에서 외부 사이트 자체의 라우팅 결과는 실제 HTTP 연결 경로가 아닐 수 있습니다. 외부 측정의 정확한 로컬 경로는 PAC·WPAD 또는 수동 설정이 선택한 프록시 엔드포인트를 기준으로 봐야 합니다."
            }
        });
        content.Children.Add(inputGrid);
        content.Children.Add(sourceButtons);
        content.Children.Add(actionButtons);
        content.Children.Add(new Border
        {
            Margin = new Thickness(0, 18, 0, 0),
            Padding = new Thickness(16),
            CornerRadius = new CornerRadius(8),
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(216, 221, 227)),
            BorderThickness = new Thickness(1),
            Child = _routeEvidenceResultText
        });

        return new TabItem
        {
            Header = "라우팅 근거",
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

    private static ComboBoxItem CreatePurposeItem(
        string label,
        RouteProbePurpose purpose) =>
        new()
        {
            Content = label,
            Tag = purpose
        };

    private void OnUseInternalRouteTargetClick(
        object sender,
        RoutedEventArgs e)
    {
        if (_routeEvidenceTargetTextBox is null
            || _routeEvidencePurposeComboBox is null)
        {
            return;
        }

        _routeEvidenceTargetTextBox.Text =
            InternalTargetUrlTextBox.Text.Trim();
        SelectRoutePurpose(RouteProbePurpose.InternalDirectTarget);
    }

    private void OnUseExternalRouteTargetClick(
        object sender,
        RoutedEventArgs e)
    {
        if (_routeEvidenceTargetTextBox is null
            || _routeEvidencePurposeComboBox is null)
        {
            return;
        }

        string? firstExternal = ExternalTargetUrlsTextBox.Text
            .Split(
                ['\r', '\n'],
                StringSplitOptions.RemoveEmptyEntries
                | StringSplitOptions.TrimEntries)
            .FirstOrDefault();
        _routeEvidenceTargetTextBox.Text = firstExternal ?? string.Empty;
        SelectRoutePurpose(RouteProbePurpose.ExternalTargetReference);
    }

    private async void OnAnalyzeRouteEvidenceClick(
        object sender,
        RoutedEventArgs e)
    {
        if (_routeEvidenceTargetTextBox is null
            || _routeEvidencePurposeComboBox is null
            || _analyzeRouteEvidenceButton is null
            || _cancelRouteEvidenceButton is null
            || _routeEvidenceCancellation is not null)
        {
            return;
        }

        if (_measurementRunning || _observationCancellation is not null)
        {
            SetRouteEvidenceResult(
                "다운로드 측정 또는 브라우저 관찰이 진행 중입니다. 불필요한 DNS·경로 변수를 추가하지 않도록 완료 후 확인하십시오.",
                Brushes.DarkOrange);
            return;
        }

        RouteProbePurpose purpose = GetSelectedRoutePurpose();
        string target = _routeEvidenceTargetTextBox.Text.Trim();
        string label = FormatRoutePurpose(purpose);
        _routeEvidenceCancellation = new CancellationTokenSource();
        _analyzeRouteEvidenceButton.IsEnabled = false;
        _cancelRouteEvidenceButton.IsEnabled = true;
        SetRouteEvidenceResult(
            "대상을 확인하고 Windows 최적 인터페이스를 조회하고 있습니다.",
            Brushes.DarkSlateGray);

        try
        {
            DestinationRouteEvidence result =
                await LocalRouteEvidenceReader.ReadAsync(
                    target,
                    label,
                    purpose,
                    dnsTimeoutSeconds: 5,
                    _routeEvidenceCancellation.Token);
            SetRouteEvidenceResult(
                FormatRouteEvidence(result),
                result.Status switch
                {
                    DestinationRouteEvidenceStatus.Success
                        when result.Warnings.Count == 0 => Brushes.DarkGreen,
                    DestinationRouteEvidenceStatus.Success
                        or DestinationRouteEvidenceStatus.PartialSuccess
                        or DestinationRouteEvidenceStatus.MultipleInterfaces
                        => Brushes.DarkOrange,
                    DestinationRouteEvidenceStatus.Canceled
                        => Brushes.DarkSlateGray,
                    _ => Brushes.DarkRed
                });
        }
        catch (OperationCanceledException)
        {
            SetRouteEvidenceResult(
                "사용자 요청으로 라우팅 근거 확인을 중단했습니다.",
                Brushes.DarkSlateGray);
        }
        catch (Exception exception)
        {
            SetRouteEvidenceResult(
                $"라우팅 근거 확인 중 오류가 발생했습니다: {exception.Message}",
                Brushes.DarkRed);
        }
        finally
        {
            _routeEvidenceCancellation.Dispose();
            _routeEvidenceCancellation = null;
            _analyzeRouteEvidenceButton.IsEnabled = true;
            _cancelRouteEvidenceButton.IsEnabled = false;
        }
    }

    private void OnCancelRouteEvidenceClick(
        object sender,
        RoutedEventArgs e)
    {
        try
        {
            _routeEvidenceCancellation?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The route check completed while the user pressed cancel.
        }
    }

    private void SelectRoutePurpose(RouteProbePurpose purpose)
    {
        if (_routeEvidencePurposeComboBox is null)
        {
            return;
        }

        foreach (ComboBoxItem item in
                 _routeEvidencePurposeComboBox.Items.OfType<ComboBoxItem>())
        {
            if (item.Tag is RouteProbePurpose itemPurpose
                && itemPurpose == purpose)
            {
                _routeEvidencePurposeComboBox.SelectedItem = item;
                return;
            }
        }
    }

    private RouteProbePurpose GetSelectedRoutePurpose() =>
        _routeEvidencePurposeComboBox?.SelectedItem is ComboBoxItem
        {
            Tag: RouteProbePurpose purpose
        }
            ? purpose
            : RouteProbePurpose.ManualDestination;

    private static string FormatRouteEvidence(
        DestinationRouteEvidence result)
    {
        StringBuilder builder = new();
        builder.AppendLine($"대상 구분: {FormatRoutePurpose(result.Purpose)}");
        builder.AppendLine($"상태: {FormatRouteStatus(result.Status)}");
        builder.AppendLine($"확인 방식: {(result.DnsWasUsed ? "DNS 확인 후 로컬 라우팅 조회" : "IP 리터럴의 로컬 라우팅 조회")}");
        builder.AppendLine($"확인한 주소 수: {result.ResolvedAddressCount}");
        builder.AppendLine(result.Message);

        if (result.SelectedInterface is RouteInterfaceDescriptor selected)
        {
            builder.AppendLine();
            builder.AppendLine("[선택된 로컬 인터페이스]");
            builder.AppendLine($"이름: {selected.DisplayName}");
            builder.AppendLine($"설명: {selected.Description}");
            builder.AppendLine($"ID 지문: {selected.IdentityFingerprint}");
            builder.AppendLine($"범주: {FormatRouteCategory(selected.Category)} / Native: {selected.NativeInterfaceType}");
            builder.AppendLine($"상태: {selected.OperationalState} / 기본 게이트웨이: {(selected.HasDefaultGateway ? "있음" : "확인되지 않음")}");
            builder.AppendLine($"VPN·터널 분류: {(selected.IsVpn ? "예" : "아니요")} / 가상 분류: {(selected.IsVirtual ? "예" : "아니요")}");
        }

        if (result.AddressEvidence.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("[주소 계열별 근거]");
            foreach (RouteAddressEvidence item in result.AddressEvidence)
            {
                string interfaceText = item.Interface is null
                    ? "인터페이스 확인 안 됨"
                    : $"{item.Interface.DisplayName} / {item.Interface.IdentityFingerprint}";
                builder.AppendLine($"- {item.AddressFamily}: {item.Status} · {interfaceText}");
                if (!string.IsNullOrWhiteSpace(item.Message))
                {
                    builder.AppendLine($"  {item.Message}");
                }
            }
        }

        if (result.Warnings.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("[주의]");
            foreach (string warning in result.Warnings)
            {
                builder.AppendLine($"- {warning}");
            }
        }

        builder.AppendLine();
        builder.AppendLine("IP·게이트웨이·DNS 주소 원문은 표시하지 않았습니다.");
        return builder.ToString().TrimEnd();
    }

    private static string FormatRoutePurpose(RouteProbePurpose purpose) =>
        purpose switch
        {
            RouteProbePurpose.InternalDirectTarget => "내부 DIRECT 측정 대상",
            RouteProbePurpose.ProxyEndpoint => "프록시 엔드포인트",
            RouteProbePurpose.ExternalTargetReference => "외부 사이트 참고 경로",
            _ => "수동 목적지"
        };

    private static string FormatRouteStatus(
        DestinationRouteEvidenceStatus status) =>
        status switch
        {
            DestinationRouteEvidenceStatus.Success => "확인 성공",
            DestinationRouteEvidenceStatus.PartialSuccess => "일부 확인",
            DestinationRouteEvidenceStatus.MultipleInterfaces => "복수 인터페이스",
            DestinationRouteEvidenceStatus.InvalidTarget => "입력 오류",
            DestinationRouteEvidenceStatus.ResolutionFailed => "주소 확인 실패",
            DestinationRouteEvidenceStatus.RouteNotFound => "경로 확인 실패",
            DestinationRouteEvidenceStatus.Canceled => "취소",
            _ => "오류"
        };

    private static string FormatRouteCategory(
        NetworkAdapterCategory category) =>
        category switch
        {
            NetworkAdapterCategory.Wireless => "Wi-Fi",
            NetworkAdapterCategory.Ethernet => "유선",
            NetworkAdapterCategory.Tunnel => "VPN·터널",
            NetworkAdapterCategory.Loopback => "루프백",
            _ => "기타"
        };

    private void SetRouteEvidenceResult(string text, Brush brush)
    {
        if (_routeEvidenceResultText is null)
        {
            return;
        }

        _routeEvidenceResultText.Text = text;
        _routeEvidenceResultText.Foreground = brush;
    }

    private void OnRouteEvidenceWindowClosed(
        object? sender,
        EventArgs e)
    {
        try
        {
            _routeEvidenceCancellation?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Already completed.
        }
        finally
        {
            _routeEvidenceCancellation?.Dispose();
            _routeEvidenceCancellation = null;
        }
    }
}
