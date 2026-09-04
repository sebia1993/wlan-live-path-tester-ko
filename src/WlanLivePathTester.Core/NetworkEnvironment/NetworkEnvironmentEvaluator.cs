namespace WlanLivePathTester.Core.NetworkEnvironment;

public static class NetworkEnvironmentEvaluator
{
    public static NetworkEnvironmentAssessment Evaluate(
        IReadOnlyList<LocalNetworkAdapterSnapshot> adapters)
    {
        ArgumentNullException.ThrowIfNull(adapters);

        LocalNetworkAdapterSnapshot[] active = adapters
            .Where(adapter => adapter.IsUp)
            .Where(adapter =>
                adapter.Category != NetworkAdapterCategory.Loopback)
            .ToArray();
        LocalNetworkAdapterSnapshot[] activeWireless = active
            .Where(adapter =>
                adapter.Category == NetworkAdapterCategory.Wireless)
            .ToArray();
        LocalNetworkAdapterSnapshot[] activeEthernet = active
            .Where(adapter =>
                adapter.Category == NetworkAdapterCategory.Ethernet)
            .ToArray();
        LocalNetworkAdapterSnapshot[] activeVpn = active
            .Where(adapter => adapter.IsVpn)
            .ToArray();
        LocalNetworkAdapterSnapshot[] activeVirtual = active
            .Where(adapter => adapter.IsVirtual)
            .ToArray();
        LocalNetworkAdapterSnapshot[] gatewayAdapters = active
            .Where(adapter => adapter.HasDefaultGateway)
            .ToArray();
        LocalNetworkAdapterSnapshot[] physicalWireless = activeWireless
            .Where(adapter => !adapter.IsVirtual && !adapter.IsVpn)
            .ToArray();

        List<NetworkEnvironmentFinding> findings = [];

        if (physicalWireless.Length == 0)
        {
            findings.Add(new NetworkEnvironmentFinding(
                Code: "NO_ACTIVE_PHYSICAL_WIRELESS",
                Severity: NetworkEnvironmentSeverity.Warning,
                Title: "활성 물리 무선 어댑터 없음",
                Evidence: "현재 Up 상태인 비가상 Wireless80211 인터페이스를 확인하지 못했습니다.",
                Interpretation: "WLAN 상태 또는 브라우저 관찰 결과를 실제 무선 경로와 연결하기 어렵습니다.",
                NextStep: "Wi-Fi가 켜져 있고 실제 무선랜에 연결됐는지 확인한 뒤 다시 수집하십시오."));
        }
        else if (physicalWireless.Length > 1)
        {
            findings.Add(new NetworkEnvironmentFinding(
                Code: "MULTIPLE_ACTIVE_WIRELESS",
                Severity: NetworkEnvironmentSeverity.Warning,
                Title: "활성 물리 무선 어댑터 여러 개",
                Evidence: $"Up 상태인 비가상 무선 어댑터가 {physicalWireless.Length}개입니다.",
                Interpretation: "WLAN API 결과와 인터페이스 처리량 관찰이 서로 다른 무선 NIC를 가리킬 수 있습니다.",
                NextStep: "사용하지 않는 USB·내장 무선 NIC를 비활성화하거나 실제 연결 인터페이스 이름을 비교하십시오."));
        }

        if (gatewayAdapters.Length > 1)
        {
            findings.Add(new NetworkEnvironmentFinding(
                Code: "MULTIPLE_ACTIVE_DEFAULT_GATEWAYS",
                Severity: NetworkEnvironmentSeverity.Warning,
                Title: "활성 기본 게이트웨이 여러 개",
                Evidence: $"Up 상태이며 기본 게이트웨이가 설정된 인터페이스가 {gatewayAdapters.Length}개입니다.",
                Interpretation: "실제 요청 경로는 인터페이스 이름만으로 확정할 수 없으며 Windows 라우팅 메트릭의 영향을 받습니다.",
                NextStep: "route print 또는 Get-NetRoute로 목적지별 최적 경로와 인터페이스 메트릭을 확인하십시오."));
        }

        bool wirelessGateway = activeWireless.Any(adapter =>
            adapter.HasDefaultGateway && !adapter.IsVirtual);
        bool ethernetGateway = activeEthernet.Any(adapter =>
            adapter.HasDefaultGateway && !adapter.IsVirtual);
        if (wirelessGateway && ethernetGateway)
        {
            findings.Add(new NetworkEnvironmentFinding(
                Code: "WIRED_AND_WIRELESS_GATEWAYS_ACTIVE",
                Severity: NetworkEnvironmentSeverity.Warning,
                Title: "유선·무선 기본 경로 동시 활성",
                Evidence: "물리 유선과 물리 무선 인터페이스에 기본 게이트웨이가 모두 있습니다.",
                Interpretation: "다운로드 트래픽이 Wi-Fi가 아닌 유선으로 나가면 WLAN 처리량 측정과 실제 요청 경로가 달라질 수 있습니다.",
                NextStep: "무선 성능 검증 중에는 유선을 분리하거나 목적지 경로가 Wi-Fi 인터페이스를 사용하는지 확인하십시오."));
        }

        if (activeVpn.Length > 0)
        {
            findings.Add(new NetworkEnvironmentFinding(
                Code: "VPN_OR_TUNNEL_ACTIVE",
                Severity: NetworkEnvironmentSeverity.Warning,
                Title: "VPN 또는 터널 인터페이스 활성",
                Evidence: $"활성 VPN·터널 후보가 {activeVpn.Length}개입니다.",
                Interpretation: "내부·외부 다운로드가 VPN 터널, 보안 에이전트 또는 별도 가상 경로를 통과할 수 있습니다.",
                NextStep: "회사 정책을 따르면서 VPN 연결 상태와 목적지별 라우팅을 확인하고 결과에 VPN 사용 여부를 기록하십시오."));
        }

        if (activeVirtual.Length > 0)
        {
            findings.Add(new NetworkEnvironmentFinding(
                Code: "VIRTUAL_ADAPTERS_ACTIVE",
                Severity: NetworkEnvironmentSeverity.Information,
                Title: "가상 네트워크 어댑터 활성",
                Evidence: $"Hyper-V·VMware·WSL·가상 VPN 등으로 분류된 활성 인터페이스가 {activeVirtual.Length}개입니다.",
                Interpretation: "대부분 직접적인 장애는 아니지만 인터페이스 자동 선택과 카운터 해석을 복잡하게 만들 수 있습니다.",
                NextStep: "진단 시 실제 WLAN NIC와 가상 인터페이스를 이름·유형·게이트웨이 유무로 구분하십시오."));
        }

        int partialReadCount = adapters.Count(adapter =>
            !string.IsNullOrWhiteSpace(adapter.ReadError));
        if (partialReadCount > 0)
        {
            findings.Add(new NetworkEnvironmentFinding(
                Code: "ADAPTER_PROPERTIES_PARTIAL",
                Severity: NetworkEnvironmentSeverity.Information,
                Title: "일부 인터페이스 속성 조회 제한",
                Evidence: $"{partialReadCount}개 인터페이스에서 IP 속성 일부를 읽지 못했습니다.",
                Interpretation: "게이트웨이 또는 주소 계열 개수가 실제보다 적게 표시될 수 있습니다.",
                NextStep: "권한·어댑터 상태·드라이버를 확인하고 로컬 명령 결과와 비교하십시오."));
        }

        bool routeSelectionMayBeAmbiguous =
            gatewayAdapters.Length > 1
            || activeVpn.Length > 0
            || (wirelessGateway && ethernetGateway)
            || physicalWireless.Length > 1;

        if (findings.Count == 0)
        {
            findings.Add(new NetworkEnvironmentFinding(
                Code: "SIMPLE_WIRELESS_ENVIRONMENT",
                Severity: NetworkEnvironmentSeverity.Information,
                Title: "단순한 무선 인터페이스 환경",
                Evidence: "활성 물리 무선 NIC가 한 개이며 다중 기본 게이트웨이·VPN·가상 어댑터 경고가 없습니다.",
                Interpretation: "현재 수집 범위에서는 WLAN 관찰 대상과 요청 경로가 엇갈릴 가능성이 상대적으로 낮습니다.",
                NextStep: "실제 목적지 경로는 프록시 판정과 다운로드 결과로 계속 확인하십시오."));
        }

        return new NetworkEnvironmentAssessment(
            TotalAdapterCount: adapters.Count,
            ActiveAdapterCount: active.Length,
            ActiveWirelessCount: activeWireless.Length,
            ActiveEthernetCount: activeEthernet.Length,
            ActiveVpnCount: activeVpn.Length,
            ActiveVirtualCount: activeVirtual.Length,
            ActiveDefaultGatewayCount: gatewayAdapters.Length,
            RouteSelectionMayBeAmbiguous: routeSelectionMayBeAmbiguous,
            PreferredWirelessDisplayName: physicalWireless.Length == 1
                ? physicalWireless[0].DisplayName
                : null,
            Findings: findings);
    }
}
