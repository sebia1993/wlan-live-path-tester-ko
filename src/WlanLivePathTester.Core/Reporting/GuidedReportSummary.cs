using System.Text;

namespace WlanLivePathTester.Core.Reporting;

/// <summary>Explains already captured evidence; never starts collection or infers a healthy OSI layer.</summary>
public static class GuidedReportSummary
{
    public static string Render(LocalDiagnosticReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        StringBuilder text = new();
        text.AppendLine($"보고서 시각: {report.Metadata.GeneratedAt.ToLocalTime():yyyy-MM-dd HH:mm:ss}");
        text.AppendLine("① 무선 연결: " + (report.Wlan.IsConnected
            ? "연결 정보 확인됨 (무선망 전체 정상 판정 아님)" : "판단 불가 — 무선 연결 정보를 확보하지 못함"));
        text.AppendLine("② 실제 경로: " + (report.InternalProxyRouteComparison is null
            ? "미실행 — 별도 대상별 경로 비교 결과 없음"
            : "별도 경로 비교 결과 있음 — 다운로드와 수집 시각을 대조하세요"));
        IReadOnlyList<ReportMeasurementSection> measurements = report.StructuredMeasurements ?? [];
        text.AppendLine("③ 서비스 연결: " + (measurements.Count == 0
            ? "미실행 — HTTP 측정 결과 없음" : "HTTP·인증 결과는 아래 측정별 상태와 근거를 확인하세요"));
        text.AppendLine("④ 성능 비교: " + (measurements.Count == 0
            ? "미실행 — 내부·외부 속도를 판단하지 않음" : $"측정 기록 {measurements.Count}개 (시각과 대상 조건을 대조하세요)"));
        foreach (ReportMeasurementSection result in measurements)
        {
            string state = result.Status switch
            {
                "Success" => "확인됨",
                "PartialSuccess" => "주의 · 일부 성공",
                "Canceled" => "미완료 · 사용자 중지",
                "Failed" or "TimedOut" or "ProxyAuthenticationRequired" => "실패",
                "NotRun" => "미실행",
                "Blocked" => "주의 · 정책에 의해 차단됨",
                "PathMismatch" => "주의 · 예상 경로 불일치",
                _ => "판단 불가"
            };
            text.AppendLine($"- {result.StartedAt.ToLocalTime():HH:mm:ss} · {(result.PathKind switch { "Internal" => "내부", "External" => "외부", _ => "경로 판단 불가" })} · {state} · HTTP {result.HttpStatusCode?.ToString() ?? "확인 불가"} · {result.AverageMbps?.ToString("F1") ?? "측정값 없음"} Mbps");
        }
        text.AppendLine("⑤ 다음 점검 (각 항목의 관찰 범위에 한함)");
        foreach (ReportFinding finding in report.Findings)
        {
            text.AppendLine(Safe(finding.Title));
            text.AppendLine("근거: " + Safe(finding.Evidence));
            text.AppendLine("의미: " + Safe(finding.Interpretation));
            text.AppendLine("한계: " + Safe(finding.Limitation));
            text.AppendLine("다음 점검: " + Safe(finding.NextStep));
        }
        return text.ToString().TrimEnd();
    }

    private static string Safe(string? value) => SensitiveDataRedactor.RedactText(value) ?? "확인 불가";
}
