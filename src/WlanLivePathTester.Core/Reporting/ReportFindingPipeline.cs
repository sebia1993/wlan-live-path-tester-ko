namespace WlanLivePathTester.Core.Reporting;

public static class ReportFindingPipeline
{
    private const string NoClearFailurePatternCode =
        "NO_CLEAR_FAILURE_PATTERN";
    private const string WlanIdentityUnavailableCode =
        "BROWSER_OBSERVATION_WLAN_IDENTITY_UNAVAILABLE";

    public static IReadOnlyList<ReportFinding> Evaluate(
        ReportWlanSection wlan,
        ReportProxySection proxy,
        IReadOnlyList<ReportTextSection> measurements,
        ReportObservationSection? observation,
        IReadOnlyList<ReportMeasurementSection>? structuredMeasurements = null)
    {
        IReadOnlyList<ReportFinding> existing =
            ReportFindingEngine.Evaluate(
                wlan,
                proxy,
                measurements,
                observation,
                structuredMeasurements);
        List<ReportFinding> findings = [.. existing];

        if (observation?.TerminationReason?.Equals(
                "WlanIdentityUnavailable",
                StringComparison.OrdinalIgnoreCase) == true)
        {
            RemoveByCode(findings, NoClearFailurePatternCode);
            AddUnique(findings, CreateWlanIdentityUnavailableFinding(
                observation));
        }

        return findings;
    }

    public static IReadOnlyList<string> DefaultLimitations() =>
        ReportFindingEngine.DefaultLimitations();

    private static ReportFinding
        CreateWlanIdentityUnavailableFinding(
            ReportObservationSection observation) =>
        new(
            Code: WlanIdentityUnavailableCode,
            Severity: "Warning",
            Title: "WLAN 연결 ID 연속 미확인",
            Evidence:
                $"브라우저 관찰 상태는 {observation.Status}, 보존된 샘플은 {observation.Samples.Count}개, WLAN 미확인 샘플은 {observation.WlanDisconnectedSampleCount ?? 0}개이며 종료 원인은 WlanIdentityUnavailable입니다.",
            Interpretation:
                "시작 시 고정한 물리 Wi-Fi 카운터는 다른 NIC로 전환하지 않았지만 Native WLAN 연결 또는 인터페이스 ID를 연속 임계 횟수 확인하지 못해 WLAN 메타데이터와 카운터의 상관을 더 이상 보장하지 않았습니다.",
            Limitation:
                "WLAN AutoConfig 일시 지연, 드라이버 재연결, 장치 절전, 권한·EDR 제한 또는 실제 WLAN 분리 중 어느 원인인지 이 결과만으로 확정할 수 없습니다.",
            NextStep:
                "Windows WLAN 보고서, WLAN AutoConfig·무선 드라이버·시스템 이벤트와 장치 전원 관리 상태를 확인하고 연결이 안정된 상태에서 관찰을 다시 실행하십시오.");

    private static void RemoveByCode(
        ICollection<ReportFinding> findings,
        string code)
    {
        ReportFinding[] matches = findings
            .Where(finding => finding.Code.Equals(
                code,
                StringComparison.OrdinalIgnoreCase))
            .ToArray();
        foreach (ReportFinding match in matches)
        {
            findings.Remove(match);
        }
    }

    private static void AddUnique(
        ICollection<ReportFinding> findings,
        ReportFinding finding)
    {
        if (!findings.Any(existing => existing.Code.Equals(
                finding.Code,
                StringComparison.OrdinalIgnoreCase)))
        {
            findings.Add(finding);
        }
    }
}
