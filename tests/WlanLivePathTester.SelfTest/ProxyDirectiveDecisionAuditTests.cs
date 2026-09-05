using System.Runtime.CompilerServices;
using System.Text.Json;
using WlanLivePathTester.Core.Proxy;

namespace WlanLivePathTester.SelfTest;

internal static class ProxyDirectiveDecisionAuditTests
{
    private static readonly DateTimeOffset CapturedAt =
        DateTimeOffset.UnixEpoch.AddDays(10);

#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Run()
    {
        RecordsPlannedTargetProxyWithoutRawValues();
        RecordsBlockedTargetFailureInsteadOfManualFallback();
        RecordsDirectOnlyWithoutNetworkPermission();
        RecordsCompletedCanceledAndFailedExecutionPhases();
        RecordsManualPartialSelectionRisk();
        NormalizesInvalidReadStatesFailClosed();
        Console.WriteLine(
            "PASS redacted proxy decision audit snapshot tests");
    }

    private static void RecordsPlannedTargetProxyWithoutRawValues()
    {
        const string targetHost =
            "audit-target-private.example.invalid";
        const string manualHost =
            "audit-manual-private.example.invalid";
        ProxyDirectiveSourceSnapshot snapshot = CreateSnapshot(
            ProxyDirectiveSourceReadStatus.Success,
            targetDecisionIsDirect: false,
            $"PROXY {targetHost}:8080; DIRECT",
            ProxyDirectiveSourceReadStatus.Success,
            manualProxyConfigured: true,
            $"PROXY {manualHost}:3128",
            autoDetectEnabled: true,
            pacConfigured: true);

        ProxyDirectiveDecisionAudit audit =
            ProxyDirectiveDecisionAuditFactory.Create(snapshot);

        Ensure(audit.Phase == ProxyDirectiveDecisionAuditPhase.Planned,
            "승인된 프록시 지시문은 실행 전 Planned 감사 상태여야 합니다.");
        Ensure(audit.SourceKind
               == ProxyDirectiveSourceKind.TargetSpecificAutoProxy
               && audit.SelectionCode
                   == ProxyDirectiveSourceSelectionCode.TargetSpecificProxy
               && audit.PlanCode
                   == ProxyDirectiveRouteAnalysisPlanCode
                       .TargetSpecificProxySelected,
            "대상별 출처·선택 코드·계획 코드를 유지해야 합니다.");
        Ensure(audit.NetworkLookupAllowed
               && audit.ProxyEndpointCount == 1
               && audit.DirectDirectiveCount == 1
               && audit.HasDirectFallback,
            "프록시 후보·DIRECT fallback과 사용자 실행 조회 가능 상태를 유지해야 합니다.");
        Ensure(audit.ParseErrorCount == 0
               && audit.ParseWarningCount == 0,
            "정상 지시문에 파싱 오류·경고가 없어야 합니다.");
        AssertNoRawValues(
            audit,
            targetHost,
            manualHost,
            snapshot.TargetSpecificDirective!,
            snapshot.ManualProxyDirective!);
    }

    private static void
        RecordsBlockedTargetFailureInsteadOfManualFallback()
    {
        const string manualHost =
            "audit-valid-but-blocked.example.invalid";
        ProxyDirectiveSourceSnapshot snapshot = CreateSnapshot(
            ProxyDirectiveSourceReadStatus.Failed,
            targetDecisionIsDirect: false,
            targetSpecificDirective: null,
            ProxyDirectiveSourceReadStatus.Success,
            manualProxyConfigured: true,
            $"PROXY {manualHost}:8080",
            autoDetectEnabled: true,
            pacConfigured: true);

        ProxyDirectiveDecisionAudit audit =
            ProxyDirectiveDecisionAuditFactory.Create(snapshot);

        Ensure(audit.Phase == ProxyDirectiveDecisionAuditPhase.Blocked,
            "시도한 대상별 판정 실패는 Blocked 감사 상태여야 합니다.");
        Ensure(audit.SourceKind
               == ProxyDirectiveSourceKind.TargetSpecificAutoProxy
               && audit.SelectionStatus
                   == ProxyDirectiveSourceSelectionStatus.Invalid
               && audit.PlanStatus
                   == ProxyDirectiveRouteAnalysisPlanStatus.Blocked,
            "수동 프록시로 출처를 변경하지 말고 대상별 오류 상태를 유지해야 합니다.");
        Ensure(!audit.NetworkLookupAllowed
               && audit.ProxyEndpointCount == 0
               && audit.DirectDirectiveCount == 0,
            "차단 상태에서 프록시·DIRECT나 DNS 조회를 추정하면 안 됩니다.");
        AssertNoRawValues(audit, manualHost);
    }

    private static void RecordsDirectOnlyWithoutNetworkPermission()
    {
        ProxyDirectiveSourceSnapshot snapshot = CreateSnapshot(
            ProxyDirectiveSourceReadStatus.Success,
            targetDecisionIsDirect: true,
            targetSpecificDirective: null,
            ProxyDirectiveSourceReadStatus.Success,
            manualProxyConfigured: true,
            "PROXY ignored-audit-manual.example.invalid:8080",
            autoDetectEnabled: true,
            pacConfigured: true);

        ProxyDirectiveDecisionAudit audit =
            ProxyDirectiveDecisionAuditFactory.Create(snapshot);

        Ensure(audit.Phase
               == ProxyDirectiveDecisionAuditPhase.DirectOnly
               && audit.SelectionStatus
                   == ProxyDirectiveSourceSelectionStatus.Direct
               && audit.PlanStatus
                   == ProxyDirectiveRouteAnalysisPlanStatus.DirectOnly,
            "대상별 DIRECT를 DirectOnly로 유지해야 합니다.");
        Ensure(!audit.NetworkLookupAllowed
               && audit.ProxyEndpointCount == 0
               && audit.DirectDirectiveCount == 1,
            "DIRECT-only에서 프록시 DNS·경로 조회를 허용하면 안 됩니다.");
        Ensure(audit.Message.Contains(
                "DNS·경로 분석을 수행하지 않습니다",
                StringComparison.Ordinal),
            "감사 메시지에 no-network 경계를 설명해야 합니다.");
    }

    private static void
        RecordsCompletedCanceledAndFailedExecutionPhases()
    {
        ProxyDirectiveSourceSnapshot snapshot = CreateSnapshot(
            ProxyDirectiveSourceReadStatus.NotAttempted,
            targetDecisionIsDirect: false,
            targetSpecificDirective: null,
            ProxyDirectiveSourceReadStatus.Success,
            manualProxyConfigured: true,
            "PROXY audit-manual.example.invalid:3128",
            autoDetectEnabled: false,
            pacConfigured: false);

        ProxyDirectiveDecisionAudit completed =
            ProxyDirectiveDecisionAuditFactory.Create(
                snapshot,
                ProxyDirectiveRouteAnalysisExecutionStatus.Completed);
        ProxyDirectiveDecisionAudit canceled =
            ProxyDirectiveDecisionAuditFactory.Create(
                snapshot,
                ProxyDirectiveRouteAnalysisExecutionStatus.Canceled);
        ProxyDirectiveDecisionAudit failed =
            ProxyDirectiveDecisionAuditFactory.Create(
                snapshot,
                ProxyDirectiveRouteAnalysisExecutionStatus.Failed);

        Ensure(completed.Phase
               == ProxyDirectiveDecisionAuditPhase.Completed,
            "완료 실행 상태를 Completed 감사 단계로 유지해야 합니다.");
        Ensure(canceled.Phase
               == ProxyDirectiveDecisionAuditPhase.Canceled,
            "취소 실행 상태를 Canceled 감사 단계로 유지해야 합니다.");
        Ensure(failed.Phase
               == ProxyDirectiveDecisionAuditPhase.Failed,
            "실패 실행 상태를 Failed 감사 단계로 유지해야 합니다.");
        Ensure(completed.NetworkLookupAllowed
               && canceled.NetworkLookupAllowed
               && failed.NetworkLookupAllowed,
            "실행 결과가 달라도 원래 계획의 조회 허용 여부는 감사 근거로 유지해야 합니다.");
        Ensure(canceled.Message.Contains(
                "사용자 취소",
                StringComparison.Ordinal)
               && failed.Message.Contains(
                   "원문 지시문과 예외 메시지는",
                   StringComparison.Ordinal),
            "취소·실패의 안전한 고정 설명이 필요합니다.");
    }

    private static void RecordsManualPartialSelectionRisk()
    {
        ProxyDirectiveSourceSnapshot snapshot = CreateSnapshot(
            ProxyDirectiveSourceReadStatus.NotAttempted,
            targetDecisionIsDirect: false,
            targetSpecificDirective: null,
            ProxyDirectiveSourceReadStatus.Success,
            manualProxyConfigured: true,
            "PROXY valid-audit.example.invalid:8080; UNKNOWN invalid; DIRECT",
            autoDetectEnabled: false,
            pacConfigured: false);

        ProxyDirectiveDecisionAudit audit =
            ProxyDirectiveDecisionAuditFactory.Create(snapshot);

        Ensure(audit.Phase == ProxyDirectiveDecisionAuditPhase.Planned,
            "유효 프록시 후보가 있는 부분 파싱은 사용자 실행 가능한 Planned 상태여야 합니다.");
        Ensure(audit.SelectionStatus
               == ProxyDirectiveSourceSelectionStatus
                   .SelectedWithWarnings
               && audit.ParseErrorCount == 1,
            "제외된 구간의 파싱 오류를 감사 스냅샷에 집계해야 합니다.");
        Ensure(audit.ProxyEndpointCount == 1
               && audit.DirectDirectiveCount == 1,
            "유효 프록시와 DIRECT fallback 개수를 유지해야 합니다.");
        Ensure(audit.Message.Contains(
                "파싱 오류 1개",
                StringComparison.Ordinal),
            "감사 메시지에 오류 개수만 안전하게 표시해야 합니다.");
        Ensure(!audit.Message.Contains(
                "UNKNOWN invalid",
                StringComparison.OrdinalIgnoreCase),
            "감사 메시지에 해석 실패 원문을 반사하면 안 됩니다.");
    }

    private static void NormalizesInvalidReadStatesFailClosed()
    {
        ProxyDirectiveSourceSnapshot snapshot = CreateSnapshot(
            (ProxyDirectiveSourceReadStatus)999,
            targetDecisionIsDirect: false,
            "PROXY invalid-audit-target.example.invalid:8080",
            ProxyDirectiveSourceReadStatus.Success,
            manualProxyConfigured: true,
            "PROXY valid-audit-manual.example.invalid:3128",
            autoDetectEnabled: true,
            pacConfigured: true);

        ProxyDirectiveDecisionAudit audit =
            ProxyDirectiveDecisionAuditFactory.Create(snapshot);

        Ensure(audit.TargetDecisionReadStatus
               == ProxyDirectiveSourceReadStatus.Failed,
            "정의되지 않은 대상 판정 읽기 상태는 감사 모델에서 Failed로 정규화해야 합니다.");
        Ensure(audit.Phase == ProxyDirectiveDecisionAuditPhase.Blocked
               && !audit.NetworkLookupAllowed,
            "정의되지 않은 읽기 상태를 수동 프록시로 fallback하면 안 됩니다.");
        Ensure(audit.SelectionCode
               == ProxyDirectiveSourceSelectionCode.TargetDecisionInvalid,
            "fail-closed 대상 판정 오류 코드를 유지해야 합니다.");
    }

    private static ProxyDirectiveSourceSnapshot CreateSnapshot(
        ProxyDirectiveSourceReadStatus targetStatus,
        bool targetDecisionIsDirect,
        string? targetSpecificDirective,
        ProxyDirectiveSourceReadStatus manualStatus,
        bool manualProxyConfigured,
        string? manualProxyDirective,
        bool autoDetectEnabled,
        bool pacConfigured) =>
        new(
            CapturedAt,
            targetStatus,
            targetDecisionIsDirect,
            targetSpecificDirective,
            manualStatus,
            manualProxyConfigured,
            manualProxyDirective,
            autoDetectEnabled,
            pacConfigured);

    private static void AssertNoRawValues(
        ProxyDirectiveDecisionAudit audit,
        params string[] forbidden)
    {
        string json = JsonSerializer.Serialize(audit);
        string display = audit.RedactedDisplay;
        string message = audit.Message;
        foreach (string value in forbidden)
        {
            Ensure(!json.Contains(
                    value,
                    StringComparison.OrdinalIgnoreCase),
                $"감사 JSON에 원문이 남았습니다: {value}");
            Ensure(!display.Contains(
                    value,
                    StringComparison.OrdinalIgnoreCase),
                $"감사 표시에 원문이 남았습니다: {value}");
            Ensure(!message.Contains(
                    value,
                    StringComparison.OrdinalIgnoreCase),
                $"감사 메시지에 원문이 남았습니다: {value}");
        }
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
