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
                    snapshot.TargetDecisionIsDirect,
                    snapshot.TargetSpecificDirective,
                    snapshot.ManualProxyConfigured,
                    snapshot.ManualProxyDirective);
            case ProxyDirectiveSourceReadStatus.Failed:
                return ProxyDirectiveSourceSelectionPolicy.Select(
                    targetDecisionWasEvaluated: true,
                    targetDecisionIsDirect: false,
                    targetSpecificDirective: null,
                    snapshot.ManualProxyConfigured,
                    snapshot.ManualProxyDirective);
            case ProxyDirectiveSourceReadStatus.NotAttempted:
                break;
            default:
                return CreateInvalidTargetReadResult();
        }

        switch (snapshot.ManualConfigurationStatus)
        {
            case ProxyDirectiveSourceReadStatus.Success:
                return ProxyDirectiveSourceSelectionPolicy.Select(
                    targetDecisionWasEvaluated: false,
                    targetDecisionIsDirect: false,
                    targetSpecificDirective: null,
                    snapshot.ManualProxyConfigured,
                    snapshot.ManualProxyDirective);
            case ProxyDirectiveSourceReadStatus.Failed:
                return ProxyDirectiveSourceSelectionPolicy.Select(
                    targetDecisionWasEvaluated: false,
                    targetDecisionIsDirect: false,
                    targetSpecificDirective: null,
                    manualProxyConfigured: true,
                    manualProxyDirective: null);
            case ProxyDirectiveSourceReadStatus.NotAttempted:
                return ProxyDirectiveSourceSelectionPolicy.Select(
                    targetDecisionWasEvaluated: false,
                    targetDecisionIsDirect: false,
                    targetSpecificDirective: null,
                    manualProxyConfigured: false,
                    manualProxyDirective: null);
            default:
                return CreateInvalidManualReadResult();
        }
    }

    private static ProxyDirectiveSourceSelectionResult
        CreateInvalidTargetReadResult() =>
        new(
            ProxyDirectiveSourceSelectionStatus.Invalid,
            ProxyDirectiveSourceKind.TargetSpecificAutoProxy,
            ProxyDirectiveSourceSelectionCode.TargetDecisionInvalid,
            selectedDirectiveText: null,
            parseResult: null,
            "대상별 프록시 판정 상태를 안전하게 해석하지 못해 수동 설정으로 fallback하지 않았습니다.");

    private static ProxyDirectiveSourceSelectionResult
        CreateInvalidManualReadResult() =>
        new(
            ProxyDirectiveSourceSelectionStatus.Invalid,
            ProxyDirectiveSourceKind.ManualProxyConfiguration,
            ProxyDirectiveSourceSelectionCode
                .ManualConfigurationInvalid,
            selectedDirectiveText: null,
            parseResult: null,
            "수동 프록시 설정 읽기 상태를 안전하게 해석하지 못해 DIRECT 또는 임의 프록시로 추정하지 않았습니다.");
}
