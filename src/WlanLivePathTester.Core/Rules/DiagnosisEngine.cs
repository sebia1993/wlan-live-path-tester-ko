using WlanLivePathTester.Core.Models;

namespace WlanLivePathTester.Core.Rules;

public sealed class DiagnosisEngine
{
    private readonly DiagnosisThresholds _thresholds;

    public DiagnosisEngine(DiagnosisThresholds? thresholds = null)
    {
        _thresholds = thresholds ?? DiagnosisThresholds.Default;
    }

    public IReadOnlyList<DiagnosisFinding> Evaluate(
        WlanSnapshot? wlan,
        DownloadMeasurement? internalMeasurement,
        IEnumerable<DownloadMeasurement> externalMeasurements)
    {
        ArgumentNullException.ThrowIfNull(externalMeasurements);

        List<DiagnosisFinding> findings = [];
        DownloadMeasurement[] external = externalMeasurements.ToArray();

        AddWlanFindings(findings, wlan);
        AddProtocolAndPathFindings(findings, internalMeasurement, external);
        AddPerformanceFindings(findings, internalMeasurement, external);

        if (findings.Count == 0)
        {
            findings.Add(new DiagnosisFinding(
                Code: "NO_ACTIONABLE_FAULT",
                Severity: FindingSeverity.Information,
                Title: "현재 측정에서 뚜렷한 성능 저하가 확인되지 않았습니다.",
                Explanation: "수집된 WLAN, 내부망 및 외부망 결과에서 고정 규칙에 해당하는 문제가 관찰되지 않았습니다.",
                NextStep: "사용자가 문제를 느낀 시각과 동일한 조건에서 다시 측정하고 애플리케이션 또는 서버 응답도 함께 확인하십시오."));
        }

        return findings;
    }

    private void AddWlanFindings(ICollection<DiagnosisFinding> findings, WlanSnapshot? wlan)
    {
        if (wlan is null || !wlan.IsConnected)
        {
            findings.Add(new DiagnosisFinding(
                Code: "WLAN_NOT_CONNECTED",
                Severity: FindingSeverity.Critical,
                Title: "무선랜 연결이 확인되지 않습니다.",
                Explanation: "현재 측정 결과를 무선 경로 문제와 비교할 수 없습니다.",
                NextStep: "무선 어댑터 상태와 SSID 연결 여부를 먼저 확인하십시오."));
            return;
        }

        if (wlan.RssiDbm is int rssi && rssi <= _thresholds.WeakRssiDbm)
        {
            findings.Add(new DiagnosisFinding(
                Code: "WLAN_WEAK_SIGNAL",
                Severity: FindingSeverity.Warning,
                Title: "무선 신호가 약합니다.",
                Explanation: $"측정 RSSI는 {rssi} dBm이며 기준 {_thresholds.WeakRssiDbm} dBm 이하입니다.",
                NextStep: "AP와의 거리, 차폐물, 동일 채널 간섭 및 BSSID 변경 여부를 확인하십시오."));
        }
    }

    private static void AddProtocolAndPathFindings(
        ICollection<DiagnosisFinding> findings,
        DownloadMeasurement? internalMeasurement,
        IEnumerable<DownloadMeasurement> external)
    {
        if (internalMeasurement?.Status == MeasurementStatus.PathMismatch)
        {
            findings.Add(new DiagnosisFinding(
                Code: "INTERNAL_PATH_USED_PROXY",
                Severity: FindingSeverity.Warning,
                Title: "내부망 측정이 프록시 경로를 사용했습니다.",
                Explanation: "이 결과는 순수한 WLAN·사내망 기준 성능으로 사용할 수 없습니다.",
                NextStep: "내부 측정 URL의 프록시 바이패스 정책 또는 DIRECT 경로를 확인하십시오."));
        }

        if (external.Any(item => item.Status == MeasurementStatus.ProxyAuthenticationRequired))
        {
            findings.Add(new DiagnosisFinding(
                Code: "PROXY_AUTHENTICATION_REQUIRED",
                Severity: FindingSeverity.Warning,
                Title: "회사 프록시 인증을 완료하지 못했습니다.",
                Explanation: "HTTP 407 또는 동등한 프록시 인증 실패가 관찰되었습니다. 속도 저하로 판정하지 않습니다.",
                NextStep: "현재 로그인 사용자 컨텍스트, PAC/WPAD 결과와 Negotiate/NTLM 지원 여부를 확인하십시오."));
        }

        if (external.Any(item => item.Status == MeasurementStatus.PathMismatch))
        {
            findings.Add(new DiagnosisFinding(
                Code: "EXTERNAL_PATH_DID_NOT_USE_EXPECTED_PROXY",
                Severity: FindingSeverity.Warning,
                Title: "외부 측정의 예상 프록시 경로가 확인되지 않았습니다.",
                Explanation: "프로그램 경로가 실제 브라우저 경로와 다를 수 있어 외부 성능 결과의 신뢰도가 낮습니다.",
                NextStep: "대상 URL별 PAC/WPAD 결과를 확인하거나 브라우저 다운로드 관찰 모드를 사용하십시오."));
        }
    }

    private void AddPerformanceFindings(
        ICollection<DiagnosisFinding> findings,
        DownloadMeasurement? internalMeasurement,
        IReadOnlyCollection<DownloadMeasurement> external)
    {
        bool internalSucceeded =
            internalMeasurement is { Status: MeasurementStatus.Success, AverageMbps: not null };

        if (internalSucceeded
            && internalMeasurement!.AverageMbps!.Value < _thresholds.MinimumInternalMbps)
        {
            findings.Add(new DiagnosisFinding(
                Code: "INTERNAL_PATH_LOW_THROUGHPUT",
                Severity: FindingSeverity.Warning,
                Title: "내부망 다운로드 처리량이 기준보다 낮습니다.",
                Explanation: $"내부망 평균은 {internalMeasurement.AverageMbps.Value:F1} Mbps입니다.",
                NextStep: "RSSI, PHY 링크 속도, 채널 혼잡, WLAN 터널과 내부 유선 서버 경로를 우선 확인하십시오."));
        }

        DownloadMeasurement[] successfulExternal = external
            .Where(item => item is { Status: MeasurementStatus.Success, AverageMbps: not null })
            .ToArray();

        if (!internalSucceeded || successfulExternal.Length < 2)
        {
            return;
        }

        double internalMbps = internalMeasurement!.AverageMbps!.Value;
        double averageExternal = successfulExternal.Average(item => item.AverageMbps!.Value);
        bool allExternallyLow = successfulExternal.All(
            item => item.AverageMbps!.Value < _thresholds.MinimumExternalMbps);
        bool commonRatioLow =
            internalMbps > 0 && averageExternal / internalMbps < _thresholds.CommonExternalPathRatio;

        if (internalMbps >= _thresholds.MinimumInternalMbps && (allExternallyLow || commonRatioLow))
        {
            findings.Add(new DiagnosisFinding(
                Code: "COMMON_EXTERNAL_PATH_DEGRADED",
                Severity: FindingSeverity.Warning,
                Title: "복수 외부 대상에서 공통 경로 성능 저하가 관찰됩니다.",
                Explanation: $"내부망 {internalMbps:F1} Mbps에 비해 외부 대상 평균은 {averageExternal:F1} Mbps입니다. 회사 프록시, 인터넷 경계 또는 공통 외부 구간의 영향 가능성이 있습니다.",
                NextStep: "프록시 운영팀 또는 외부망 담당자에게 측정 시각과 복수 대상 결과를 전달하십시오. 프록시 내부 상태는 이 도구로 확정할 수 없습니다."));
            return;
        }

        double minimum = successfulExternal.Min(item => item.AverageMbps!.Value);
        double maximum = successfulExternal.Max(item => item.AverageMbps!.Value);

        if (minimum > 0 && maximum / minimum >= _thresholds.SiteVariationRatio)
        {
            findings.Add(new DiagnosisFinding(
                Code: "EXTERNAL_SITE_VARIATION",
                Severity: FindingSeverity.Information,
                Title: "외부 사이트별 다운로드 성능 편차가 큽니다.",
                Explanation: $"성공한 외부 대상의 최소·최대 속도는 {minimum:F1}~{maximum:F1} Mbps입니다.",
                NextStep: "특정 사이트 또는 CDN 경로의 제한인지 확인하고 공통 외부망 장애로 단정하지 마십시오."));
        }
    }
}
