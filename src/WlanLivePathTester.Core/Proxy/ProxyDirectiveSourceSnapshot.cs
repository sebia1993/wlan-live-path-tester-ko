using System.Diagnostics;
using System.Text.Json.Serialization;

namespace WlanLivePathTester.Core.Proxy;

public enum ProxyDirectiveSourceReadStatus
{
    NotAttempted,
    Success,
    Failed
}

[DebuggerDisplay("{RedactedDisplay,nq}")]
public sealed class ProxyDirectiveSourceSnapshot
{
    public ProxyDirectiveSourceSnapshot(
        DateTimeOffset capturedAt,
        ProxyDirectiveSourceReadStatus targetDecisionStatus,
        bool targetDecisionIsDirect,
        string? targetSpecificDirective,
        ProxyDirectiveSourceReadStatus manualConfigurationStatus,
        bool manualProxyConfigured,
        string? manualProxyDirective,
        bool autoDetectEnabled,
        bool pacConfigured)
    {
        CapturedAt = capturedAt;
        TargetDecisionStatus = targetDecisionStatus;
        TargetDecisionIsDirect = targetDecisionIsDirect;
        TargetSpecificDirective = targetSpecificDirective;
        ManualConfigurationStatus = manualConfigurationStatus;
        ManualProxyConfigured = manualProxyConfigured;
        ManualProxyDirective = manualProxyDirective;
        AutoDetectEnabled = autoDetectEnabled;
        PacConfigured = pacConfigured;
        RedactedDisplay =
            $"대상 판정 {TargetDecisionStatus} · 수동 설정 {ManualConfigurationStatus} · 수동 프록시 {(ManualProxyConfigured ? "있음" : "없음")} · 자동 검색 {(AutoDetectEnabled ? "사용" : "미사용")} · PAC {(PacConfigured ? "설정" : "미설정")}";
    }

    public DateTimeOffset CapturedAt { get; }

    public ProxyDirectiveSourceReadStatus TargetDecisionStatus { get; }

    public bool TargetDecisionIsDirect { get; }

    [JsonIgnore]
    public string? TargetSpecificDirective { get; }

    public ProxyDirectiveSourceReadStatus ManualConfigurationStatus
    {
        get;
    }

    public bool ManualProxyConfigured { get; }

    [JsonIgnore]
    public string? ManualProxyDirective { get; }

    public bool AutoDetectEnabled { get; }

    public bool PacConfigured { get; }

    public string RedactedDisplay { get; }

    public override string ToString() => RedactedDisplay;
}

public static class ProxyDirectiveSourceSnapshotSelectionPolicy
{
    public static ProxyDirectiveSourceSelectionResult Select(
        ProxyDirectiveSourceSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        switch (snapshot.TargetDecisionStatus)
        {
            case ProxyDirectiveSourceReadStatus.Success:
                return ProxyDirectiveSourceSelectionPolicy.Select(
                    targetDecisionWasEvaluated: true,
                    targetDecisionIsDirect:
                        snapshot.TargetDecisionIsDirect,
                    targetSpecificDirective:
                        snapshot.TargetSpecificDirective,
                    manualProxyConfigured:
                        snapshot.ManualProxyConfigured,
                    manualProxyDirective:
                        snapshot.ManualProxyDirective);
            case ProxyDirectiveSourceReadStatus.Failed:
                return ProxyDirectiveSourceSelectionPolicy
                    .InvalidTargetRead(
                        "대상별 PAC/WPAD 판정을 시도했지만 결과를 얻지 못했습니다.");
            case ProxyDirectiveSourceReadStatus.NotAttempted:
                break;
            default:
                return ProxyDirectiveSourceSelectionPolicy
                    .InvalidTargetRead(
                        "대상별 프록시 판정 읽기 상태가 정의되지 않은 값입니다.");
        }

        return snapshot.ManualConfigurationStatus switch
        {
            ProxyDirectiveSourceReadStatus.Success =>
                ProxyDirectiveSourceSelectionPolicy.Select(
                    targetDecisionWasEvaluated: false,
                    targetDecisionIsDirect: false,
                    targetSpecificDirective: null,
                    manualProxyConfigured:
                        snapshot.ManualProxyConfigured,
                    manualProxyDirective:
                        snapshot.ManualProxyDirective),
            ProxyDirectiveSourceReadStatus.Failed =>
                ProxyDirectiveSourceSelectionPolicy.InvalidManualRead(
                    "현재 사용자 수동 프록시 설정을 읽지 못했습니다."),
            ProxyDirectiveSourceReadStatus.NotAttempted =>
                ProxyDirectiveSourceSelectionPolicy.Select(
                    targetDecisionWasEvaluated: false,
                    targetDecisionIsDirect: false,
                    targetSpecificDirective: null,
                    manualProxyConfigured: false,
                    manualProxyDirective: null),
            _ => ProxyDirectiveSourceSelectionPolicy.InvalidManualRead(
                "수동 프록시 설정 읽기 상태가 정의되지 않은 값입니다.")
        };
    }
}
