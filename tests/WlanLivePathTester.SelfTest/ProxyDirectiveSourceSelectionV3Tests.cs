using System.Runtime.CompilerServices;
using System.Text.Json;
using WlanLivePathTester.Core.Proxy;

namespace WlanLivePathTester.SelfTest;

internal static class ProxyDirectiveSourceSelectionV3Tests
{
    private static readonly DateTimeOffset CapturedAt =
        DateTimeOffset.UnixEpoch.AddDays(8);

#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Run()
    {
        TargetSpecificProxyOverridesManualConfiguration();
        TargetSpecificDirectOverridesManualConfiguration();
        ContradictoryTargetDecisionsFailClosed();
        InvalidTargetDecisionNeverFallsBackToManual();
        ManualConfigurationIsUsedOnlyWithoutTargetEvaluation();
        ManualDirectPreservesScopeAndRejectsAmbiguity();
        PartialProxyInputIsSelectedWithWarnings();
        SnapshotDistinguishesFailedFromNotAttempted();
        FailedManualReadIsInvalidAndNoReadsAreUnavailable();
        UnknownReadStatesFailClosed();
        RawDirectiveTextAndHostsAreNotSerializedOrDisplayed();
        Console.WriteLine(
            "PASS authoritative proxy directive source selection v3 tests");
    }

    private static void
        TargetSpecificProxyOverridesManualConfiguration()
    {
        const string targetHost = "target-proxy.example.invalid";
        const string manualHost = "manual-proxy.example.invalid";
        ProxyDirectiveSourceSelectionResult result =
            ProxyDirectiveSourceSelectionPolicy.Select(
                targetDecisionWasEvaluated: true,
                targetDecisionIsDirect: false,
                targetSpecificDirective:
                    $"PROXY {targetHost}:8080; DIRECT",
                manualProxyConfigured: true,
                manualProxyDirective:
                    $"PROXY {manualHost}:3128");

        Ensure(result.Status
               == ProxyDirectiveSourceSelectionStatus.Selected,
            "정상 대상별 프록시 판정은 Selected여야 합니다.");
        Ensure(result.SourceKind
               == ProxyDirectiveSourceKind.TargetSpecificAutoProxy,
            "대상별 PAC/WPAD 판정이 수동 설정보다 우선해야 합니다.");
        Ensure(result.Code
               == ProxyDirectiveSourceSelectionCode.TargetSpecificProxy,
            "대상별 프록시 선택 코드를 유지해야 합니다.");
        Ensure(result.ProxyEndpointCount == 1
               && result.DirectDirectiveCount == 1
               && result.HasDirectFallback,
            "프록시 후보와 DIRECT fallback 수를 유지해야 합니다.");
        Ensure(result.SelectedDirectiveText?.Contains(
                targetHost,
                StringComparison.OrdinalIgnoreCase) == true,
            "선택된 대상별 원문은 후속 로컬 분석을 위해 메모리에 유지해야 합니다.");
        Ensure(result.SelectedDirectiveText?.Contains(
                manualHost,
                StringComparison.OrdinalIgnoreCase) == false,
            "수동 프록시로 대상별 판정을 덮어쓰면 안 됩니다.");
    }

    private static void
        TargetSpecificDirectOverridesManualConfiguration()
    {
        const string manualHost = "manual-secret.example.invalid";
        ProxyDirectiveSourceSelectionResult emptyDirective =
            ProxyDirectiveSourceSelectionPolicy.Select(
                targetDecisionWasEvaluated: true,
                targetDecisionIsDirect: true,
                targetSpecificDirective: null,
                manualProxyConfigured: true,
                manualProxyDirective:
                    $"PROXY {manualHost}:8080");
        ProxyDirectiveSourceSelectionResult explicitDirect =
            ProxyDirectiveSourceSelectionPolicy.Select(
                targetDecisionWasEvaluated: true,
                targetDecisionIsDirect: true,
                targetSpecificDirective: "https=DIRECT",
                manualProxyConfigured: true,
                manualProxyDirective:
                    $"PROXY {manualHost}:8080");

        Ensure(emptyDirective.Status
               == ProxyDirectiveSourceSelectionStatus.Direct
               && emptyDirective.Code
                   == ProxyDirectiveSourceSelectionCode
                       .TargetSpecificDirect,
            "대상별 DIRECT 판정은 Direct 상태와 고정 코드를 가져야 합니다.");
        Ensure(emptyDirective.SelectedDirectiveText == "DIRECT",
            "원문이 없는 대상별 DIRECT는 canonical DIRECT를 만들어야 합니다.");
        Ensure(explicitDirect.Status
               == ProxyDirectiveSourceSelectionStatus.Direct
               && explicitDirect.SelectedDirectiveText
                   == "https=DIRECT",
            "명시된 대상 범위의 DIRECT는 원문 범위를 유지해야 합니다.");
        Ensure(emptyDirective.ProxyEndpointCount == 0
               && explicitDirect.ProxyEndpointCount == 0,
            "DIRECT 판정에서 프록시 후보를 추정하면 안 됩니다.");
        Ensure(!JsonSerializer.Serialize(emptyDirective).Contains(
                manualHost,
                StringComparison.OrdinalIgnoreCase),
            "무시한 수동 프록시 호스트가 결과 JSON에 남으면 안 됩니다.");
    }

    private static void ContradictoryTargetDecisionsFailClosed()
    {
        ProxyDirectiveSourceSelectionResult directWithProxy =
            ProxyDirectiveSourceSelectionPolicy.Select(
                targetDecisionWasEvaluated: true,
                targetDecisionIsDirect: true,
                targetSpecificDirective:
                    "PROXY contradictory.example.invalid:8080; DIRECT",
                manualProxyConfigured: true,
                manualProxyDirective:
                    "PROXY manual.example.invalid:3128");
        ProxyDirectiveSourceSelectionResult proxyWithDirectOnly =
            ProxyDirectiveSourceSelectionPolicy.Select(
                targetDecisionWasEvaluated: true,
                targetDecisionIsDirect: false,
                targetSpecificDirective: "DIRECT",
                manualProxyConfigured: true,
                manualProxyDirective:
                    "PROXY manual.example.invalid:3128");

        foreach (ProxyDirectiveSourceSelectionResult result
                 in new[] { directWithProxy, proxyWithDirectOnly })
        {
            Ensure(result.Status
                   == ProxyDirectiveSourceSelectionStatus.Invalid,
                "boolean 판정과 지시문이 모순되면 Invalid여야 합니다.");
            Ensure(result.SourceKind
                   == ProxyDirectiveSourceKind.TargetSpecificAutoProxy,
                "모순 상태의 원인 출처는 대상별 판정이어야 합니다.");
            Ensure(result.Code
                   == ProxyDirectiveSourceSelectionCode
                       .TargetDecisionInvalid,
                "대상별 판정 오류 코드를 사용해야 합니다.");
            Ensure(!result.HasUsableSelection
                   && result.SelectedDirectiveText is null,
                "모순된 판정을 실행 가능한 지시문으로 반환하면 안 됩니다.");
            Ensure(result.Message.Contains(
                    "자동 fallback을 수행하지 않았습니다",
                    StringComparison.Ordinal),
                "수동 설정으로 대체하지 않았다는 설명이 필요합니다.");
        }
    }

    private static void
        InvalidTargetDecisionNeverFallsBackToManual()
    {
        const string manualHost = "valid-manual.example.invalid";
        ProxyDirectiveSourceSelectionResult result =
            ProxyDirectiveSourceSelectionPolicy.Select(
                targetDecisionWasEvaluated: true,
                targetDecisionIsDirect: false,
                targetSpecificDirective:
                    "PROXY invalid-target.example.invalid:not-a-port",
                manualProxyConfigured: true,
                manualProxyDirective:
                    $"PROXY {manualHost}:8080");

        Ensure(result.Status
               == ProxyDirectiveSourceSelectionStatus.Invalid
               && result.Code
                   == ProxyDirectiveSourceSelectionCode
                       .TargetDecisionInvalid,
            "잘못된 대상별 판정은 수동 설정으로 숨기지 않고 Invalid여야 합니다.");
        Ensure(result.SelectedDirectiveText is null,
            "유효한 수동 프록시가 있어도 자동 선택하면 안 됩니다.");
        Ensure(!JsonSerializer.Serialize(result).Contains(
                manualHost,
                StringComparison.OrdinalIgnoreCase),
            "선택되지 않은 수동 프록시 호스트가 결과에 남으면 안 됩니다.");
    }

    private static void
        ManualConfigurationIsUsedOnlyWithoutTargetEvaluation()
    {
        ProxyDirectiveSourceSelectionResult result =
            ProxyDirectiveSourceSelectionPolicy.Select(
                targetDecisionWasEvaluated: false,
                targetDecisionIsDirect: false,
                targetSpecificDirective:
                    "PROXY ignored-target.example.invalid:8080",
                manualProxyConfigured: true,
                manualProxyDirective:
                    "http=manual-http.example.invalid:8080;https=manual-https.example.invalid:8443");

        Ensure(result.Status
               == ProxyDirectiveSourceSelectionStatus.Selected,
            "대상별 판정이 없으면 정상 수동 프록시를 선택해야 합니다.");
        Ensure(result.SourceKind
               == ProxyDirectiveSourceKind.ManualProxyConfiguration
               && result.Code
                   == ProxyDirectiveSourceSelectionCode.ManualProxy,
            "수동 프록시 출처와 고정 코드를 유지해야 합니다.");
        Ensure(result.ProxyEndpointCount == 2
               && result.DirectDirectiveCount == 0,
            "프로토콜별 수동 후보 두 개를 유지해야 합니다.");
        Ensure(result.SelectedDirectiveText?.StartsWith(
                "http=manual-http",
                StringComparison.Ordinal) == true,
            "선택된 수동 문자열을 메모리에서 유지해야 합니다.");
    }

    private static void ManualDirectPreservesScopeAndRejectsAmbiguity()
    {
        const string scopedDirect = "ftp=DIRECT";
        ProxyDirectiveSourceSelectionResult valid =
            ProxyDirectiveSourceSelectionPolicy.Select(
                targetDecisionWasEvaluated: false,
                targetDecisionIsDirect: false,
                targetSpecificDirective: null,
                manualProxyConfigured: true,
                manualProxyDirective: scopedDirect);
        ProxyDirectiveSourceSelectionResult ambiguous =
            ProxyDirectiveSourceSelectionPolicy.Select(
                targetDecisionWasEvaluated: false,
                targetDecisionIsDirect: false,
                targetSpecificDirective: null,
                manualProxyConfigured: true,
                manualProxyDirective:
                    "DIRECT; UNKNOWN possibly-proxy.example.invalid:8080");

        Ensure(valid.Status
               == ProxyDirectiveSourceSelectionStatus.Direct
               && valid.Code
                   == ProxyDirectiveSourceSelectionCode.ManualDirect,
            "명확한 수동 DIRECT-only 설정은 Direct여야 합니다.");
        Ensure(valid.SelectedDirectiveText == scopedDirect,
            "scoped DIRECT를 일반 DIRECT로 축소하면 안 됩니다.");
        ProxyRouteDirective directive =
            valid.ParseResult?.Directives.Single()
            ?? throw new InvalidOperationException(
                "수동 DIRECT 파싱 결과가 필요합니다.");
        Ensure(directive.IsDirect && directive.Scope == "ftp",
            "DIRECT의 FTP 범위를 유지해야 합니다.");

        Ensure(ambiguous.Status
               == ProxyDirectiveSourceSelectionStatus.Invalid
               && ambiguous.Code
                   == ProxyDirectiveSourceSelectionCode
                       .ManualConfigurationInvalid,
            "DIRECT와 해석 불가 구간이 섞이면 Invalid여야 합니다.");
        Ensure(!ambiguous.HasUsableSelection
               && ambiguous.SelectedDirectiveText is null,
            "모호한 수동 설정을 DIRECT-only로 축소하면 안 됩니다.");
        Ensure(ambiguous.Message.Contains(
                "DIRECT-only 정책으로 축소하지 않았습니다",
                StringComparison.Ordinal),
            "숨은 프록시 후보를 놓치지 않는 fail-closed 설명이 필요합니다.");
    }

    private static void PartialProxyInputIsSelectedWithWarnings()
    {
        ProxyDirectiveSourceSelectionResult target =
            ProxyDirectiveSourceSelectionPolicy.Select(
                targetDecisionWasEvaluated: true,
                targetDecisionIsDirect: false,
                targetSpecificDirective:
                    "PROXY valid-target.example.invalid:8080; UNKNOWN invalid; DIRECT",
                manualProxyConfigured: false,
                manualProxyDirective: null);
        ProxyDirectiveSourceSelectionResult manual =
            ProxyDirectiveSourceSelectionPolicy.Select(
                targetDecisionWasEvaluated: false,
                targetDecisionIsDirect: false,
                targetSpecificDirective: null,
                manualProxyConfigured: true,
                manualProxyDirective:
                    "PROXY valid-manual.example.invalid:3128; UNKNOWN invalid");

        foreach (ProxyDirectiveSourceSelectionResult result
                 in new[] { target, manual })
        {
            Ensure(result.Status
                   == ProxyDirectiveSourceSelectionStatus
                       .SelectedWithWarnings,
                "유효 프록시와 잘못된 구간이 함께 있으면 경고 포함 선택이어야 합니다.");
            Ensure(result.ProxyEndpointCount == 1
                   && result.HasParseErrors,
                "유효 후보 수와 파싱 오류 상태를 유지해야 합니다.");
            Ensure(result.HasUsableSelection,
                "유효 프록시 후보는 사용자가 명시 실행할 수 있어야 합니다.");
        }
    }

    private static void SnapshotDistinguishesFailedFromNotAttempted()
    {
        const string manualHost =
            "valid-but-not-selected.example.invalid";
        ProxyDirectiveSourceSnapshot failedTarget = new(
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
        ProxyDirectiveSourceSnapshot notAttemptedTarget = new(
            CapturedAt,
            ProxyDirectiveSourceReadStatus.NotAttempted,
            targetDecisionIsDirect: false,
            targetSpecificDirective: null,
            ProxyDirectiveSourceReadStatus.Success,
            manualProxyConfigured: true,
            manualProxyDirective:
                $"PROXY {manualHost}:8080",
            autoDetectEnabled: false,
            pacConfigured: false);

        ProxyDirectiveSourceSelectionResult failedResult =
            ProxyDirectiveSourceSnapshotSelectionPolicy.Select(
                failedTarget);
        ProxyDirectiveSourceSelectionResult manualResult =
            ProxyDirectiveSourceSnapshotSelectionPolicy.Select(
                notAttemptedTarget);

        Ensure(failedResult.Status
               == ProxyDirectiveSourceSelectionStatus.Invalid
               && failedResult.SourceKind
                   == ProxyDirectiveSourceKind.TargetSpecificAutoProxy,
            "시도한 대상별 판정 실패는 수동 fallback 없이 Invalid여야 합니다.");
        Ensure(failedResult.SelectedDirectiveText is null,
            "대상별 판정 실패에서 수동 프록시를 선택하면 안 됩니다.");
        Ensure(manualResult.Status
               == ProxyDirectiveSourceSelectionStatus.Selected
               && manualResult.SourceKind
                   == ProxyDirectiveSourceKind
                       .ManualProxyConfiguration,
            "대상별 판정을 시도하지 않은 경우에만 수동 설정을 선택해야 합니다.");
    }

    private static void FailedManualReadIsInvalidAndNoReadsAreUnavailable()
    {
        ProxyDirectiveSourceSnapshot failedManual = new(
            CapturedAt,
            ProxyDirectiveSourceReadStatus.NotAttempted,
            targetDecisionIsDirect: false,
            targetSpecificDirective: null,
            ProxyDirectiveSourceReadStatus.Failed,
            manualProxyConfigured: false,
            manualProxyDirective: null,
            autoDetectEnabled: false,
            pacConfigured: false);
        ProxyDirectiveSourceSnapshot noReads = new(
            CapturedAt,
            ProxyDirectiveSourceReadStatus.NotAttempted,
            targetDecisionIsDirect: false,
            targetSpecificDirective: null,
            ProxyDirectiveSourceReadStatus.NotAttempted,
            manualProxyConfigured: false,
            manualProxyDirective: null,
            autoDetectEnabled: false,
            pacConfigured: false);

        ProxyDirectiveSourceSelectionResult failedResult =
            ProxyDirectiveSourceSnapshotSelectionPolicy.Select(
                failedManual);
        ProxyDirectiveSourceSelectionResult unavailableResult =
            ProxyDirectiveSourceSnapshotSelectionPolicy.Select(noReads);

        Ensure(failedResult.Status
               == ProxyDirectiveSourceSelectionStatus.Invalid
               && failedResult.Code
                   == ProxyDirectiveSourceSelectionCode
                       .ManualConfigurationInvalid,
            "수동 설정 읽기 실패는 Invalid여야 합니다.");
        Ensure(unavailableResult.Status
               == ProxyDirectiveSourceSelectionStatus.Unavailable
               && unavailableResult.Code
                   == ProxyDirectiveSourceSelectionCode
                       .NoAvailableDirective,
            "아무 출처도 읽지 않은 상태는 Unavailable이어야 합니다.");
        Ensure(unavailableResult.ProxyEndpointCount == 0
               && unavailableResult.DirectDirectiveCount == 0,
            "출처 없음 상태에서 DIRECT나 프록시를 추정하면 안 됩니다.");
    }

    private static void UnknownReadStatesFailClosed()
    {
        ProxyDirectiveSourceSnapshot invalidTarget = new(
            CapturedAt,
            (ProxyDirectiveSourceReadStatus)999,
            targetDecisionIsDirect: false,
            targetSpecificDirective:
                "PROXY invalid-target.example.invalid:8080",
            ProxyDirectiveSourceReadStatus.Success,
            manualProxyConfigured: true,
            manualProxyDirective:
                "PROXY valid-manual.example.invalid:3128",
            autoDetectEnabled: true,
            pacConfigured: true);
        ProxyDirectiveSourceSnapshot invalidManual = new(
            CapturedAt,
            ProxyDirectiveSourceReadStatus.NotAttempted,
            targetDecisionIsDirect: false,
            targetSpecificDirective: null,
            (ProxyDirectiveSourceReadStatus)999,
            manualProxyConfigured: true,
            manualProxyDirective:
                "PROXY invalid-manual.example.invalid:8080",
            autoDetectEnabled: false,
            pacConfigured: false);

        ProxyDirectiveSourceSelectionResult targetResult =
            ProxyDirectiveSourceSnapshotSelectionPolicy.Select(
                invalidTarget);
        ProxyDirectiveSourceSelectionResult manualResult =
            ProxyDirectiveSourceSnapshotSelectionPolicy.Select(
                invalidManual);

        Ensure(targetResult.Status
               == ProxyDirectiveSourceSelectionStatus.Invalid
               && targetResult.Code
                   == ProxyDirectiveSourceSelectionCode
                       .TargetDecisionInvalid,
            "정의되지 않은 대상 읽기 상태는 수동 fallback 없이 차단해야 합니다.");
        Ensure(manualResult.Status
               == ProxyDirectiveSourceSelectionStatus.Invalid
               && manualResult.Code
                   == ProxyDirectiveSourceSelectionCode
                       .ManualConfigurationInvalid,
            "정의되지 않은 수동 읽기 상태는 DIRECT 추정 없이 차단해야 합니다.");
    }

    private static void
        RawDirectiveTextAndHostsAreNotSerializedOrDisplayed()
    {
        const string targetHost =
            "snapshot-private-target.example.invalid";
        const string manualHost =
            "snapshot-private-manual.example.invalid";
        string targetDirective = $"PROXY {targetHost}:8080; DIRECT";
        ProxyDirectiveSourceSnapshot snapshot = new(
            CapturedAt,
            ProxyDirectiveSourceReadStatus.Success,
            targetDecisionIsDirect: false,
            targetSpecificDirective: targetDirective,
            ProxyDirectiveSourceReadStatus.Success,
            manualProxyConfigured: true,
            manualProxyDirective:
                $"PROXY {manualHost}:3128",
            autoDetectEnabled: true,
            pacConfigured: true);
        ProxyDirectiveSourceSelectionResult selection =
            ProxyDirectiveSourceSnapshotSelectionPolicy.Select(snapshot);

        string snapshotJson = JsonSerializer.Serialize(snapshot);
        string selectionJson = JsonSerializer.Serialize(selection);
        string combinedText = snapshot + Environment.NewLine + selection;
        foreach (string secret in new[]
                 {
                     targetHost,
                     manualHost,
                     targetDirective
                 })
        {
            Ensure(!snapshotJson.Contains(
                    secret,
                    StringComparison.OrdinalIgnoreCase),
                $"스냅샷 JSON에 프록시 원문이 남았습니다: {secret}");
            Ensure(!selectionJson.Contains(
                    secret,
                    StringComparison.OrdinalIgnoreCase),
                $"선택 결과 JSON에 프록시 원문이 남았습니다: {secret}");
            Ensure(!combinedText.Contains(
                    secret,
                    StringComparison.OrdinalIgnoreCase),
                $"안전 표시 문자열에 프록시 원문이 남았습니다: {secret}");
        }

        Ensure(selectionJson.Contains(
                selection.ParseResult!.Directives[0]
                    .HostFingerprint,
                StringComparison.Ordinal),
            "기본 JSON에는 원문 대신 비가역 호스트 지문을 유지할 수 있습니다.");
        Ensure(selection.RedactedDisplay.Contains(
                "프록시 후보 1개",
                StringComparison.Ordinal)
               && selection.RedactedDisplay.Contains(
                   "DIRECT 1개",
                   StringComparison.Ordinal),
            "안전 표시에는 출처·상태·개수만 포함해야 합니다.");
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
