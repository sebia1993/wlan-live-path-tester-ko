using System.Runtime.CompilerServices;
using System.Text.Json;
using WlanLivePathTester.Core.Proxy;

namespace WlanLivePathTester.SelfTest;

internal static class ProxyDirectiveSourceSnapshotSelectionTests
{
    private static readonly DateTimeOffset CapturedAt =
        DateTimeOffset.UnixEpoch.AddDays(8);

#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Run()
    {
        SelectsSuccessfulTargetDecision();
        FailedTargetDecisionDoesNotFallBackToValidManualProxy();
        UsesManualOnlyWhenTargetDecisionWasNotAttempted();
        FailedManualReadIsInvalidNotDirect();
        NoReadsRemainUnavailable();
        RawSourceStringsAreNotSerializedOrDisplayed();
        InvalidReadEnumValuesFailClosed();
        Console.WriteLine(
            "PASS proxy directive source snapshot selection tests");
    }

    private static void SelectsSuccessfulTargetDecision()
    {
        ProxyDirectiveSourceSnapshot snapshot = new(
            CapturedAt,
            ProxyDirectiveSourceReadStatus.Success,
            targetDecisionIsDirect: false,
            targetSpecificDirective:
                "PROXY target-snapshot.example.invalid:8080; DIRECT",
            ProxyDirectiveSourceReadStatus.Success,
            manualProxyConfigured: true,
            manualProxyDirective:
                "PROXY manual-snapshot.example.invalid:3128",
            autoDetectEnabled: true,
            pacConfigured: true);

        ProxyDirectiveSourceSelectionResult result =
            ProxyDirectiveSourceSnapshotSelectionPolicy.Select(snapshot);

        Ensure(result.Status
               == ProxyDirectiveSourceSelectionStatus.Selected,
            "성공한 대상별 프록시 판정은 Selected여야 합니다.");
        Ensure(result.SourceKind
               == ProxyDirectiveSourceKind.TargetSpecificAutoProxy,
            "대상별 판정이 수동 설정보다 우선해야 합니다.");
        Ensure(result.ProxyEndpointCount == 1
               && result.DirectDirectiveCount == 1,
            "대상별 프록시와 DIRECT fallback을 유지해야 합니다.");
    }

    private static void
        FailedTargetDecisionDoesNotFallBackToValidManualProxy()
    {
        const string manualHost =
            "valid-but-not-selected.example.invalid";
        ProxyDirectiveSourceSnapshot snapshot = new(
            CapturedAt,
            ProxyDirectiveSourceReadStatus.Failed,
            targetDecisionIsDirect: false,
            targetSpecificDirective: null,
            ProxyDirectiveSourceReadStatus.Success,
            manualProxyConfigured: true,
            manualProxyDirective:
                $"PROXY {manualHost}:8080",
            autoDetectEnabled: true,
            pacConfigured: true);

        ProxyDirectiveSourceSelectionResult result =
            ProxyDirectiveSourceSnapshotSelectionPolicy.Select(snapshot);

        Ensure(result.Status
               == ProxyDirectiveSourceSelectionStatus.Invalid,
            "시도한 PAC/WPAD 판정 실패는 Invalid여야 합니다.");
        Ensure(result.SourceKind
               == ProxyDirectiveSourceKind.TargetSpecificAutoProxy,
            "실패 출처를 수동 프록시로 바꾸면 안 됩니다.");
        Ensure(result.Code
               == ProxyDirectiveSourceSelectionCode.TargetDecisionInvalid,
            "대상별 판정 오류 코드를 유지해야 합니다.");
        Ensure(result.SelectedDirectiveText is null
               && !result.HasUsableSelection,
            "유효한 수동 프록시가 있어도 자동 fallback하면 안 됩니다.");
        Ensure(!JsonSerializer.Serialize(result).Contains(
                manualHost,
                StringComparison.OrdinalIgnoreCase),
            "선택하지 않은 수동 프록시 원문이 결과에 남으면 안 됩니다.");
    }

    private static void
        UsesManualOnlyWhenTargetDecisionWasNotAttempted()
    {
        ProxyDirectiveSourceSnapshot snapshot = new(
            CapturedAt,
            ProxyDirectiveSourceReadStatus.NotAttempted,
            targetDecisionIsDirect: false,
            targetSpecificDirective: null,
            ProxyDirectiveSourceReadStatus.Success,
            manualProxyConfigured: true,
            manualProxyDirective:
                "http=manual-http.example.invalid:8080;https=manual-https.example.invalid:8443",
            autoDetectEnabled: false,
            pacConfigured: false);

        ProxyDirectiveSourceSelectionResult result =
            ProxyDirectiveSourceSnapshotSelectionPolicy.Select(snapshot);

        Ensure(result.Status
               == ProxyDirectiveSourceSelectionStatus.Selected,
            "대상별 판정이 없을 때 유효한 수동 설정을 선택해야 합니다.");
        Ensure(result.SourceKind
               == ProxyDirectiveSourceKind.ManualProxyConfiguration,
            "수동 설정 출처를 유지해야 합니다.");
        Ensure(result.ProxyEndpointCount == 2,
            "수동 프로토콜별 후보 두 개를 유지해야 합니다.");
    }

    private static void FailedManualReadIsInvalidNotDirect()
    {
        ProxyDirectiveSourceSnapshot snapshot = new(
            CapturedAt,
            ProxyDirectiveSourceReadStatus.NotAttempted,
            targetDecisionIsDirect: false,
            targetSpecificDirective: null,
            ProxyDirectiveSourceReadStatus.Failed,
            manualProxyConfigured: false,
            manualProxyDirective: null,
            autoDetectEnabled: false,
            pacConfigured: false);

        ProxyDirectiveSourceSelectionResult result =
            ProxyDirectiveSourceSnapshotSelectionPolicy.Select(snapshot);

        Ensure(result.Status
               == ProxyDirectiveSourceSelectionStatus.Invalid,
            "수동 설정 읽기 실패는 Unavailable이나 Direct가 아니라 Invalid여야 합니다.");
        Ensure(result.SourceKind
               == ProxyDirectiveSourceKind.ManualProxyConfiguration,
            "수동 읽기 실패 출처를 유지해야 합니다.");
        Ensure(result.Code
               == ProxyDirectiveSourceSelectionCode
                   .ManualConfigurationInvalid,
            "수동 설정 읽기 오류 코드를 사용해야 합니다.");
        Ensure(!result.HasUsableSelection,
            "읽기 실패 상태에서 DIRECT나 프록시를 추정하면 안 됩니다.");
    }

    private static void NoReadsRemainUnavailable()
    {
        ProxyDirectiveSourceSnapshot snapshot = new(
            CapturedAt,
            ProxyDirectiveSourceReadStatus.NotAttempted,
            targetDecisionIsDirect: false,
            targetSpecificDirective: null,
            ProxyDirectiveSourceReadStatus.NotAttempted,
            manualProxyConfigured: false,
            manualProxyDirective: null,
            autoDetectEnabled: false,
            pacConfigured: false);

        ProxyDirectiveSourceSelectionResult result =
            ProxyDirectiveSourceSnapshotSelectionPolicy.Select(snapshot);

        Ensure(result.Status
               == ProxyDirectiveSourceSelectionStatus.Unavailable,
            "아무 읽기도 수행하지 않은 상태는 Unavailable이어야 합니다.");
        Ensure(result.Code
               == ProxyDirectiveSourceSelectionCode.NoAvailableDirective,
            "출처 없음 코드를 유지해야 합니다.");
        Ensure(result.ProxyEndpointCount == 0
               && result.DirectDirectiveCount == 0,
            "프록시나 DIRECT를 임의 추정하면 안 됩니다.");
    }

    private static void
        RawSourceStringsAreNotSerializedOrDisplayed()
    {
        const string targetHost =
            "snapshot-private-target.example.invalid";
        const string manualHost =
            "snapshot-private-manual.example.invalid";
        ProxyDirectiveSourceSnapshot snapshot = new(
            CapturedAt,
            ProxyDirectiveSourceReadStatus.Success,
            targetDecisionIsDirect: false,
            targetSpecificDirective:
                $"PROXY {targetHost}:8080; DIRECT",
            ProxyDirectiveSourceReadStatus.Success,
            manualProxyConfigured: true,
            manualProxyDirective:
                $"PROXY {manualHost}:3128",
            autoDetectEnabled: true,
            pacConfigured: true);

        string json = JsonSerializer.Serialize(snapshot);
        string text = snapshot.ToString();
        foreach (string secret in new[]
                 {
                     targetHost,
                     manualHost,
                     $"PROXY {targetHost}:8080; DIRECT"
                 })
        {
            Ensure(!json.Contains(
                    secret,
                    StringComparison.OrdinalIgnoreCase),
                $"스냅샷 JSON에 프록시 원문이 남았습니다: {secret}");
            Ensure(!text.Contains(
                    secret,
                    StringComparison.OrdinalIgnoreCase),
                $"스냅샷 표시에 프록시 원문이 남았습니다: {secret}");
        }

        Ensure(json.Contains(
                "TargetDecisionStatus",
                StringComparison.Ordinal)
               && text.Contains(
                   "자동 검색 사용",
                   StringComparison.Ordinal)
               && text.Contains(
                   "PAC 설정",
                   StringComparison.Ordinal),
            "안전한 상태와 설정 플래그는 유지해야 합니다.");
    }

    private static void InvalidReadEnumValuesFailClosed()
    {
        ProxyDirectiveSourceSnapshot targetInvalid = new(
            CapturedAt,
            (ProxyDirectiveSourceReadStatus)999,
            targetDecisionIsDirect: false,
            targetSpecificDirective:
                "PROXY target-invalid.example.invalid:8080",
            ProxyDirectiveSourceReadStatus.Success,
            manualProxyConfigured: true,
            manualProxyDirective:
                "PROXY manual-valid.example.invalid:3128",
            autoDetectEnabled: true,
            pacConfigured: true);
        ProxyDirectiveSourceSnapshot manualInvalid = new(
            CapturedAt,
            ProxyDirectiveSourceReadStatus.NotAttempted,
            targetDecisionIsDirect: false,
            targetSpecificDirective: null,
            (ProxyDirectiveSourceReadStatus)999,
            manualProxyConfigured: true,
            manualProxyDirective:
                "PROXY manual-invalid.example.invalid:8080",
            autoDetectEnabled: false,
            pacConfigured: false);

        ProxyDirectiveSourceSelectionResult targetResult =
            ProxyDirectiveSourceSnapshotSelectionPolicy.Select(
                targetInvalid);
        ProxyDirectiveSourceSelectionResult manualResult =
            ProxyDirectiveSourceSnapshotSelectionPolicy.Select(
                manualInvalid);

        Ensure(targetResult.Status
               == ProxyDirectiveSourceSelectionStatus.Invalid
               && targetResult.Code
                   == ProxyDirectiveSourceSelectionCode
                       .TargetDecisionInvalid,
            "알 수 없는 대상 읽기 상태는 수동 fallback 없이 차단해야 합니다.");
        Ensure(manualResult.Status
               == ProxyDirectiveSourceSelectionStatus.Invalid
               && manualResult.Code
                   == ProxyDirectiveSourceSelectionCode
                       .ManualConfigurationInvalid,
            "알 수 없는 수동 읽기 상태는 DIRECT 추정 없이 차단해야 합니다.");
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
