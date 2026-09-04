namespace WlanLivePathTester.Core.Reporting;

public static class ReportFindingEngine
{
    public static IReadOnlyList<ReportFinding> Evaluate(
        ReportWlanSection wlan,
        ReportProxySection proxy,
        IReadOnlyList<ReportTextSection> measurements,
        ReportObservationSection? observation)
    {
        ArgumentNullException.ThrowIfNull(wlan);
        ArgumentNullException.ThrowIfNull(proxy);
        ArgumentNullException.ThrowIfNull(measurements);

        List<ReportFinding> findings = [];

        if (!wlan.IsConnected)
        {
            findings.Add(new ReportFinding(
                Code: "WLAN_NOT_CONNECTED",
                Severity: "Warning",
                Title: "WLAN 연결 정보 없음",
                Evidence: "보고서 생성 시점에 연결된 WLAN 인터페이스를 확인하지 못했습니다.",
                Interpretation: "무선 링크 상태와 다운로드 결과를 같은 시점의 정보로 비교할 수 없습니다.",
                Limitation: "어댑터 비활성화, WLAN AutoConfig 상태 또는 권한 제한도 같은 결과를 만들 수 있습니다.",
                NextStep: "무선 연결과 WlanSvc 상태를 확인한 뒤 WLAN 상태를 다시 수집하십시오."));
        }
        else if (wlan.RssiDbm is <= -75)
        {
            findings.Add(new ReportFinding(
                Code: "WLAN_WEAK_RSSI",
                Severity: "Warning",
                Title: "무선 신호 약함",
                Evidence: $"보고서 생성 시점 RSSI가 {wlan.RssiDbm} dBm입니다.",
                Interpretation: "다운로드 처리량 저하에 무선 링크 품질이 영향을 주었을 가능성이 있습니다.",
                Limitation: "RSSI만으로 채널 혼잡, 재전송, AP 부하 또는 유선 경로 문제를 확정할 수 없습니다.",
                NextStep: "같은 위치에서 RSSI, PHY 링크 속도, BSSID 변화와 내부망 측정을 함께 비교하십시오."));
        }

        if (!proxy.ReadSucceeded)
        {
            findings.Add(new ReportFinding(
                Code: "PROXY_SETTINGS_UNAVAILABLE",
                Severity: "Warning",
                Title: "Windows 프록시 설정 확인 실패",
                Evidence: proxy.Win32Error.HasValue
                    ? $"프록시 설정 읽기에서 Win32 오류 {proxy.Win32Error.Value}가 반환됐습니다."
                    : "현재 사용자 프록시 설정을 읽지 못했습니다.",
                Interpretation: "외부 측정 결과의 프록시 경로를 충분히 확인하지 못했을 수 있습니다.",
                Limitation: "회사 GPO, 권한 또는 사용자 프로필 상태에 따라 같은 오류가 발생할 수 있습니다.",
                NextStep: "Windows 인터넷 옵션과 현재 사용자 프록시 정책을 확인하십시오."));
        }

        string combinedMeasurementText = string.Join(
            Environment.NewLine,
            measurements.Select(section => section.Content));
        AddMeasurementFinding(
            findings,
            combinedMeasurementText,
            new[] { "407", "ProxyAuthentication", "프록시 인증" },
            new ReportFinding(
                Code: "PROXY_AUTHENTICATION_FAILURE",
                Severity: "Warning",
                Title: "프록시 인증 실패 또는 호환 제한",
                Evidence: "저장된 측정 결과에 HTTP 407 또는 프록시 인증 관련 문구가 있습니다.",
                Interpretation: "낮은 속도가 아니라 프로그램과 프록시 인증 경로의 실패로 구분해야 합니다.",
                Limitation: "브라우저는 별도 SSO·인증 캐시·정책을 사용할 수 있어 같은 URL에 성공할 수 있습니다.",
                NextStep: "브라우저 관찰 모드와 Windows 통합 인증 환경을 비교하십시오."));
        AddMeasurementFinding(
            findings,
            combinedMeasurementText,
            new[] { "경로 불일치", "PathMismatch", "PATH_MISMATCH" },
            new ReportFinding(
                Code: "NETWORK_PATH_MISMATCH",
                Severity: "Warning",
                Title: "예상 네트워크 경로 불일치",
                Evidence: "저장된 측정 결과에 DIRECT 또는 PROXY 기대 경로 불일치가 있습니다.",
                Interpretation: "해당 결과를 순수 내부망 또는 회사 프록시 경유 외부망 기준값으로 사용하면 안 됩니다.",
                Limitation: "운영체제의 DIRECT 판정만으로 투명 프록시가 없다고 증명할 수 없습니다.",
                NextStep: "대상 URL의 PAC/WPAD·바이패스 정책을 확인한 뒤 다시 측정하십시오."));
        AddMeasurementFinding(
            findings,
            combinedMeasurementText,
            new[] { "시간 초과", "TimedOut", "TIMEOUT" },
            new ReportFinding(
                Code: "MEASUREMENT_TIMEOUT",
                Severity: "Warning",
                Title: "측정 시간 초과",
                Evidence: "저장된 측정 결과에 제한 시간 초과가 있습니다.",
                Interpretation: "시간 초과 결과를 낮은 Mbps의 성공 측정으로 해석하지 않습니다.",
                Limitation: "DNS, TCP, TLS, 프록시 인증, 대상 서버와 정책 차단 중 어느 단계인지는 추가 근거가 필요합니다.",
                NextStep: "HTTP 상태·오류 코드·프록시 경로와 다른 대상의 결과를 함께 확인하십시오."));
        AddMeasurementFinding(
            findings,
            combinedMeasurementText,
            new[] { "HTTP 403", "상태: 403", "403 Forbidden" },
            new ReportFinding(
                Code: "TARGET_OR_POLICY_BLOCKED",
                Severity: "Information",
                Title: "대상 또는 정책 접근 거부",
                Evidence: "저장된 측정 결과에 HTTP 403 관련 문구가 있습니다.",
                Interpretation: "접근 정책 실패이며 다운로드 속도 결과로 사용하지 않습니다.",
                Limitation: "사이트 정책, 회사 프록시 정책, 보안 게이트웨이 중 어느 지점의 거부인지는 단독으로 확정할 수 없습니다.",
                NextStep: "브라우저 접근 여부와 승인된 측정 URL 정책을 확인하십시오."));

        if (observation is not null)
        {
            if (observation.Confidence.Equals("Low", StringComparison.OrdinalIgnoreCase))
            {
                findings.Add(new ReportFinding(
                    Code: "BROWSER_OBSERVATION_LOW_CONFIDENCE",
                    Severity: "Information",
                    Title: "브라우저 관찰 신뢰도 낮음",
                    Evidence: "백그라운드 트래픽, 샘플 부족 또는 인터페이스·카운터 변경 조건이 확인됐습니다.",
                    Interpretation: "표시된 처리량에 브라우저 외 다른 프로그램의 통신이 의미 있게 섞였을 수 있습니다.",
                    Limitation: "이 모드는 프로세스별 전송량을 측정하지 않습니다.",
                    NextStep: "백그라운드 통신을 줄이고 같은 다운로드를 다시 관찰하십시오."));
            }

            if (observation.BssidChangeCount is > 0
                && (observation.PauseCount is > 0 || observation.SuddenDropCount is > 0))
            {
                findings.Add(new ReportFinding(
                    Code: "BSSID_CHANGE_WITH_THROUGHPUT_DROP",
                    Severity: "Warning",
                    Title: "BSSID 변경 시점의 처리량 저하 가능성",
                    Evidence: $"관찰 중 BSSID 변경 {observation.BssidChangeCount}회와 일시 정지 {observation.PauseCount ?? 0}회 또는 급락 {observation.SuddenDropCount ?? 0}회가 기록됐습니다.",
                    Interpretation: "로밍 시점과 다운로드 정지·급락이 연관됐을 가능성을 확인할 가치가 있습니다.",
                    Limitation: "시간축 상 동시 발생만으로 로밍 실패나 특정 AP 장애를 확정할 수 없습니다.",
                    NextStep: "해당 시점의 RSSI, AP 전환 로그, 재인증 및 컨트롤러 사용자 로그를 비교하십시오."));
            }
            else if (observation.PauseCount is > 0 || observation.SuddenDropCount is > 0)
            {
                findings.Add(new ReportFinding(
                    Code: "BROWSER_THROUGHPUT_INTERRUPTION",
                    Severity: "Information",
                    Title: "브라우저 다운로드 처리량 정지·급락",
                    Evidence: $"관찰 중 일시 정지 {observation.PauseCount ?? 0}회, 급락 {observation.SuddenDropCount ?? 0}회가 기록됐습니다.",
                    Interpretation: "무선, 사내 경로, 프록시, 외부 사이트 또는 다른 트래픽의 영향을 비교해야 합니다.",
                    Limitation: "인터페이스 전체 카운터만으로 정지 원인을 특정할 수 없습니다.",
                    NextStep: "내부망과 복수 외부 대상의 자체 측정 및 WLAN 상태를 같은 위치에서 비교하십시오."));
            }
        }

        if (findings.Count == 0)
        {
            findings.Add(new ReportFinding(
                Code: "NO_CLEAR_FAILURE_PATTERN",
                Severity: "Information",
                Title: "명확한 실패 패턴 없음",
                Evidence: "현재 보고서에 규칙으로 식별할 수 있는 연결·인증·경로·관찰 경고가 없습니다.",
                Interpretation: "수집된 범위에서는 뚜렷한 공통 장애 패턴을 확인하지 못했습니다.",
                Limitation: "정상 판정이나 서비스 품질 보증을 뜻하지 않으며 실제 환경의 모든 구간을 관찰하지 않습니다.",
                NextStep: "사용자 증상이 지속되면 발생 시점의 측정을 반복하고 장비 로그와 비교하십시오."));
        }

        return findings;
    }

    public static IReadOnlyList<string> DefaultLimitations() =>
    [
        "프록시 서버의 CPU, 세션, 큐, 캐시, 정책 로그와 클러스터 상태에는 접근하지 않습니다.",
        "PHY Rx/Tx 링크 속도는 실제 애플리케이션 다운로드 처리량이 아닙니다.",
        "외부 사이트 한 곳의 결과만으로 인터넷 회선 또는 프록시 장애를 확정하지 않습니다.",
        "운영체제의 DIRECT 판정은 투명 프록시가 없다는 증거가 아닙니다.",
        "브라우저 관찰값에는 Wi-Fi 인터페이스를 사용하는 다른 프로그램의 트래픽이 포함될 수 있습니다.",
        "보고서 생성 시점의 WLAN 상태는 과거 측정 시점의 상태와 다를 수 있습니다.",
        "민감정보 마스킹은 보조 수단이며 공개 전에는 사용자가 내용을 다시 검토해야 합니다."
    ];

    private static void AddMeasurementFinding(
        ICollection<ReportFinding> findings,
        string source,
        IEnumerable<string> markers,
        ReportFinding finding)
    {
        if (markers.Any(marker => source.Contains(marker, StringComparison.OrdinalIgnoreCase)))
        {
            findings.Add(finding);
        }
    }
}
