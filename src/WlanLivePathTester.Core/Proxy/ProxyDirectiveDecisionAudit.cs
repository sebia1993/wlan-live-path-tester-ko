using System.Diagnostics;

namespace WlanLivePathTester.Core.Proxy;

public enum ProxyDirectiveDecisionAuditPhase
{
    Planned,
    Completed,
    DirectOnly,
    Blocked,
    Unavailable,
    Canceled,
    Failed
}

[DebuggerDisplay("{RedactedDisplay,nq}")]
public sealed record ProxyDirectiveDecisionAudit(
    DateTimeOffset CapturedAt,
    ProxyDirectiveSourceReadStatus TargetDecisionReadStatus,
    ProxyDirectiveSourceReadStatus ManualConfigurationReadStatus,
    bool AutoDetectEnabled,
    bool PacConfigured,
    bool ManualProxyConfigured,
    ProxyDirectiveSourceSelectionStatus SelectionStatus,
    ProxyDirectiveSourceKind SourceKind,
    ProxyDirectiveSourceSelectionCode SelectionCode,
    ProxyDirectiveRouteAnalysisPlanStatus PlanStatus,
    ProxyDirectiveRouteAnalysisPlanCode PlanCode,
    ProxyDirectiveDecisionAuditPhase Phase,
    bool NetworkLookupAllowed,
    int ProxyEndpointCount,
    int DirectDirectiveCount,
    int ParseErrorCount,
    int ParseWarningCount,
    bool HasDirectFallback,
    string Message,
    string RedactedDisplay);

public static class ProxyDirectiveDecisionAuditFactory
{
    public static ProxyDirectiveDecisionAudit Create(
        ProxyDirectiveSourceSnapshot snapshot,
        ProxyDirectiveRouteAnalysisExecutionStatus? executionStatus = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        ProxyDirectiveSourceSelectionResult selection =
            ProxyDirectiveSourceSnapshotSelectionPolicy.Select(snapshot);
        ProxyDirectiveRouteAnalysisPlan plan =
            ProxyDirectiveRouteAnalysisPlanPolicy.Create(selection);
        ProxyDirectiveDecisionAuditPhase phase = ResolvePhase(
            plan,
            executionStatus);
        int parseErrors = selection.ParseResult?.Issues.Count(issue =>
            issue.Severity == ProxyDirectiveIssueSeverity.Error) ?? 0;
        int parseWarnings = selection.ParseResult?.Issues.Count(issue =>
            issue.Severity == ProxyDirectiveIssueSeverity.Warning) ?? 0;
        string message = CreateMessage(
            phase,
            selection,
            plan,
            parseErrors,
            parseWarnings);
        string display = string.Join(
            " · ",
            phase,
            selection.SourceKind,
            selection.Code,
            plan.Code,
            $"프록시 후보 {Math.Max(0, plan.ProxyEndpointCount)}개",
            $"DIRECT {Math.Max(0, plan.DirectDirectiveCount)}개",
            $"네트워크 조회 {(plan.NetworkLookupAllowed ? "허용" : "차단")}");

        return new ProxyDirectiveDecisionAudit(
            CapturedAt: snapshot.CapturedAt,
            TargetDecisionReadStatus:
                NormalizeReadStatus(snapshot.TargetDecisionStatus),
            ManualConfigurationReadStatus:
                NormalizeReadStatus(snapshot.ManualConfigurationStatus),
            AutoDetectEnabled: snapshot.AutoDetectEnabled,
            PacConfigured: snapshot.PacConfigured,
            ManualProxyConfigured: snapshot.ManualProxyConfigured,
            SelectionStatus: NormalizeSelectionStatus(
                selection.Status),
            SourceKind: NormalizeSourceKind(selection.SourceKind),
            SelectionCode: NormalizeSelectionCode(selection.Code),
            PlanStatus: NormalizePlanStatus(plan.Status),
            PlanCode: NormalizePlanCode(plan.Code),
            Phase: phase,
            NetworkLookupAllowed: plan.NetworkLookupAllowed,
            ProxyEndpointCount: Math.Max(
                0,
                plan.ProxyEndpointCount),
            DirectDirectiveCount: Math.Max(
                0,
                plan.DirectDirectiveCount),
            ParseErrorCount: Math.Max(0, parseErrors),
            ParseWarningCount: Math.Max(0, parseWarnings),
            HasDirectFallback: selection.HasDirectFallback,
            Message: message,
            RedactedDisplay: display);
    }

    private static ProxyDirectiveDecisionAuditPhase ResolvePhase(
        ProxyDirectiveRouteAnalysisPlan plan,
        ProxyDirectiveRouteAnalysisExecutionStatus? executionStatus)
    {
        if (executionStatus.HasValue)
        {
            return executionStatus.Value switch
            {
                ProxyDirectiveRouteAnalysisExecutionStatus.Completed =>
                    ProxyDirectiveDecisionAuditPhase.Completed,
                ProxyDirectiveRouteAnalysisExecutionStatus.DirectOnly =>
                    ProxyDirectiveDecisionAuditPhase.DirectOnly,
                ProxyDirectiveRouteAnalysisExecutionStatus.Blocked =>
                    ProxyDirectiveDecisionAuditPhase.Blocked,
                ProxyDirectiveRouteAnalysisExecutionStatus.Unavailable =>
                    ProxyDirectiveDecisionAuditPhase.Unavailable,
                ProxyDirectiveRouteAnalysisExecutionStatus.Canceled =>
                    ProxyDirectiveDecisionAuditPhase.Canceled,
                ProxyDirectiveRouteAnalysisExecutionStatus.Failed =>
                    ProxyDirectiveDecisionAuditPhase.Failed,
                _ => ProxyDirectiveDecisionAuditPhase.Failed
            };
        }

        return plan.Status switch
        {
            ProxyDirectiveRouteAnalysisPlanStatus
                .AnalyzeProxyEndpoints =>
                ProxyDirectiveDecisionAuditPhase.Planned,
            ProxyDirectiveRouteAnalysisPlanStatus.DirectOnly =>
                ProxyDirectiveDecisionAuditPhase.DirectOnly,
            ProxyDirectiveRouteAnalysisPlanStatus.Blocked =>
                ProxyDirectiveDecisionAuditPhase.Blocked,
            ProxyDirectiveRouteAnalysisPlanStatus.Unavailable =>
                ProxyDirectiveDecisionAuditPhase.Unavailable,
            _ => ProxyDirectiveDecisionAuditPhase.Failed
        };
    }

    private static string CreateMessage(
        ProxyDirectiveDecisionAuditPhase phase,
        ProxyDirectiveSourceSelectionResult selection,
        ProxyDirectiveRouteAnalysisPlan plan,
        int parseErrors,
        int parseWarnings)
    {
        string counts =
            $"프록시 후보 {Math.Max(0, plan.ProxyEndpointCount)}개, DIRECT {Math.Max(0, plan.DirectDirectiveCount)}개, 파싱 오류 {Math.Max(0, parseErrors)}개, 경고 {Math.Max(0, parseWarnings)}개입니다.";
        string decision = phase switch
        {
            ProxyDirectiveDecisionAuditPhase.Planned =>
                "승인된 프록시 후보가 있으며 사용자 실행 시에만 DNS·Windows 로컬 경로 분석을 시작할 수 있습니다.",
            ProxyDirectiveDecisionAuditPhase.Completed =>
                "승인된 프록시 지시문을 사용한 분석 실행이 완료됐습니다.",
            ProxyDirectiveDecisionAuditPhase.DirectOnly =>
                "DIRECT-only 판정이므로 프록시 엔드포인트 DNS·경로 분석을 수행하지 않습니다.",
            ProxyDirectiveDecisionAuditPhase.Blocked =>
                "출처 판정 또는 실행 계획이 유효하지 않아 프록시 엔드포인트 분석을 차단했습니다.",
            ProxyDirectiveDecisionAuditPhase.Unavailable =>
                "사용할 수 있는 대상별 또는 수동 프록시 지시문이 없어 분석을 시작하지 않습니다.",
            ProxyDirectiveDecisionAuditPhase.Canceled =>
                "사용자 취소로 프록시 엔드포인트 분석을 완료하지 않았습니다.",
            _ =>
                "프록시 엔드포인트 분석이 완료되지 않았습니다. 원문 지시문과 예외 메시지는 감사 스냅샷에 포함하지 않습니다."
        };
        return string.Join(
            " ",
            decision,
            counts,
            $"선택 출처는 {NormalizeSourceKind(selection.SourceKind)}, 선택 코드는 {NormalizeSelectionCode(selection.Code)}, 계획 코드는 {NormalizePlanCode(plan.Code)}입니다.");
    }

    private static ProxyDirectiveSourceReadStatus NormalizeReadStatus(
        ProxyDirectiveSourceReadStatus value) =>
        Enum.IsDefined(value)
            ? value
            : ProxyDirectiveSourceReadStatus.Failed;

    private static ProxyDirectiveSourceSelectionStatus
        NormalizeSelectionStatus(
            ProxyDirectiveSourceSelectionStatus value) =>
        Enum.IsDefined(value)
            ? value
            : ProxyDirectiveSourceSelectionStatus.Invalid;

    private static ProxyDirectiveSourceKind NormalizeSourceKind(
        ProxyDirectiveSourceKind value) =>
        Enum.IsDefined(value)
            ? value
            : ProxyDirectiveSourceKind.None;

    private static ProxyDirectiveSourceSelectionCode NormalizeSelectionCode(
        ProxyDirectiveSourceSelectionCode value) =>
        Enum.IsDefined(value)
            ? value
            : ProxyDirectiveSourceSelectionCode.TargetDecisionInvalid;

    private static ProxyDirectiveRouteAnalysisPlanStatus NormalizePlanStatus(
        ProxyDirectiveRouteAnalysisPlanStatus value) =>
        Enum.IsDefined(value)
            ? value
            : ProxyDirectiveRouteAnalysisPlanStatus.Blocked;

    private static ProxyDirectiveRouteAnalysisPlanCode NormalizePlanCode(
        ProxyDirectiveRouteAnalysisPlanCode value) =>
        Enum.IsDefined(value)
            ? value
            : ProxyDirectiveRouteAnalysisPlanCode
                .InconsistentSelectionResult;
}
