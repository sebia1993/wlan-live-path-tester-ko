using System.Runtime.CompilerServices;
using System.Text.Json;
using WlanLivePathTester.Core.Proxy;

namespace WlanLivePathTester.SelfTest;

internal static class ProxyDirectiveSourceSelectionPolicyTests
{
#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Run()
    {
        TargetSpecificProxyOverridesManualConfiguration();
        TargetSpecificDirectIgnoresManualProxy();
        ContradictoryTargetDirectDecisionFailsClosed();
        TargetProxyDecisionWithDirectOnlyFailsClosed();
        InvalidTargetDecisionNeverFallsBackToManual();
        ManualProxyIsUsedOnlyWhenTargetDecisionWasNotEvaluated();
        ManualScopedDirectPreservesItsScope();
        PartialProxyParseIsSelectedWithWarnings();
        MissingSourcesRemainUnavailableWithoutDirectInference();
        RawDirectiveAndHostsAreNotSerializedOrDisplayed();
        Console.WriteLine(
            "PASS proxy directive source selection policy tests");
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
            "대상별 PAC/WPAD 판정이 수동 프록시보다 우선해야 합니다.");
        Ensure(result.Code
               == ProxyDirectiveSourceSelectionCode.TargetSpecificProxy,
            "대상별 프록시 선택 코드를 유지해야 합니다.");
        Ensure(result.ProxyEndpointCount == 1
               && result.DirectDirectiveCount == 1
               && result.HasDirectFallback,
            "대상별 프록시 후보와 DIRECT fallback 수를 유지해야 합니다.");
        Ensure(result.SelectedDirectiveText?.Contains(
                targetHost,
                StringComparison.OrdinalIgnoreCase) == true,
            "후속 로컬 분석을 위해 선택한 대상별 원문은 메모리에서 유지해야 합니다.");
        Ensure(result.SelectedDirectiveText?.Contains(
                manualHost,
                StringComparison.OrdinalIgnoreCase) == false,
            "수동 프록시 문자열로 대상별 판정을 덮어쓰면 안 됩니다.");
    }

    private static void TargetSpecificDirectIgnoresManualProxy()
    {
        const string manualHost = "manual-secret.example.invalid";
        ProxyDirectiveSourceSelectionResult result =
            ProxyDirectiveSourceSelectionPolicy.Select(
                targetDecisionWasEvaluated: true,
                targetDecisionIsDirect: true,
                targetSpecificDirective: null,
                manualProxyConfigured: true,
                manualProxyDirective:
                    $"PROXY {manualHost}:8080");

        Ensure(result.Status
               == ProxyDirectiveSourceSelectionStatus.Direct,
            "대상별 DIRECT 판정은 Direct여야 합니다.");
        Ensure(result.SourceKind
               == ProxyDirectiveSourceKind.TargetSpecificAutoProxy,
            "DIRECT도 대상별 판정 출처를 유지해야 합니다.");
        Ensure(result.Code
               == ProxyDirectiveSourceSelectionCode.TargetSpecificDirect,
            "대상별 DIRECT 코드를 유지해야 합니다.");
        Ensure(result.SelectedDirectiveText == "DIRECT",
            "빈 대상별 DIRECT 판정은 canonical DIRECT 지시문을 제공해야 합니다.");
        Ensure(result.ProxyEndpointCount == 0
               && result.DirectDirectiveCount == 1
               && !result.HasDirectFallback,
            "DIRECT-only는 프록시 후보나 fallback으로 표시하면 안 됩니다.");
        Ensure(!JsonSerializer.Serialize(result).Contains(
                manualHost,
                StringComparison.OrdinalIgnoreCase),
            "무시한 수동 프록시 호스트가 직렬화 결과에 남으면 안 됩니다.");
    }

    private static void
        ContradictoryTargetDirectDecisionFailsClosed()
    {
        ProxyDirectiveSourceSelectionResult result =
            ProxyDirectiveSourceSelectionPolicy.Select(
                targetDecisionWasEvaluated: true,
                targetDecisionIsDirect: true,
                targetSpecificDirective:
                    "PROXY contradictory.example.invalid:8080; DIRECT",
                manualProxyConfigured: true,
                manualProxyDirective:
                    "PROXY manual.example.invalid:3128");

        Ensure(result.Status
               == ProxyDirectiveSourceSelectionStatus.Invalid,
            "DIRECT boolean과 프록시 후보가 모순되면 Invalid여야 합니다.");
        Ensure(result.Code
               == ProxyDirectiveSourceSelectionCode.TargetDecisionInvalid,
            "대상별 판정 모순 코드를 사용해야 합니다.");
        Ensure(!result.HasUsableSelection
               && result.SelectedDirectiveText is null,
            "모순된 대상별 판정을 실행 가능한 문자열로 반환하면 안 됩니다.");
        Ensure(result.Message.Contains(
                "자동 fallback을 수행하지 않았습니다",
                StringComparison.Ordinal),
            "수동 프록시로 대체하지 않았다는 설명이 필요합니다.");
    }

    private static void
        TargetProxyDecisionWithDirectOnlyFailsClosed()
    {
        ProxyDirectiveSourceSelectionResult result =
            ProxyDirectiveSourceSelectionPolicy.Select(
                targetDecisionWasEvaluated: true,
                targetDecisionIsDirect: false,
                targetSpecificDirective: "DIRECT",
                manualProxyConfigured: true,
                manualProxyDirective:
                    "PROXY manual.example.invalid:8080");

        Ensure(result.Status
               == ProxyDirectiveSourceSelectionStatus.Invalid,
            "프록시 boolean과 DIRECT-only 지시문은 모순입니다.");
        Ensure(result.SourceKind
               == ProxyDirectiveSourceKind.TargetSpecificAutoProxy,
            "모순 상태에서도 원인 출처는 대상별 판정이어야 합니다.");
        Ensure(result.ProxyEndpointCount == 0
               && result.DirectDirectiveCount == 1,
            "모순 진단용 파싱 결과는 안전한 개수만 유지할 수 있습니다.");
        Ensure(result.SelectedDirectiveText is null,
            "모순된 DIRECT를 실행 가능한 선택으로 반환하면 안 됩니다.");
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
               == ProxyDirectiveSourceSelectionStatus.Invalid,
            "잘못된 대상별 판정은 Invalid여야 합니다.");
        Ensure(result.SourceKind
               == ProxyDirectiveSourceKind.TargetSpecificAutoProxy,
            "수동 프록시가 유효해도 대상별 판정 오류를 숨기면 안 됩니다.");
        Ensure(result.Code
               == ProxyDirectiveSourceSelectionCode.TargetDecisionInvalid,
            "대상별 판정 오류 코드를 유지해야 합니다.");
        Ensure(result.SelectedDirectiveText is null,
            "수동 프록시를 자동 선택하면 안 됩니다.");
        Ensure(!JsonSerializer.Serialize(result).Contains(
                manualHost,
                StringComparison.OrdinalIgnoreCase),
            "선택되지 않은 수동 프록시 호스트가 결과에 남으면 안 됩니다.");
    }

    private static void
        ManualProxyIsUsedOnlyWhenTargetDecisionWasNotEvaluated()
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
            "대상별 판정이 없으면 유효한 수동 프록시를 선택해야 합니다.");
        Ensure(result.SourceKind
               == ProxyDirectiveSourceKind.ManualProxyConfiguration,
            "수동 프록시 출처를 표시해야 합니다.");
        Ensure(result.Code
               == ProxyDirectiveSourceSelectionCode.ManualProxy,
            "수동 프록시 선택 코드를 사용해야 합니다.");
        Ensure(result.ProxyEndpointCount == 2,
            "프로토콜별 수동 프록시 후보 두 개를 유지해야 합니다.");
        Ensure(result.SelectedDirectiveText?.StartsWith(
                "http=manual-http",
                StringComparison.Ordinal) == true,
            "선택된 수동 지시문을 후속 로컬 분석용으로 유지해야 합니다.");
    }

    private static void ManualScopedDirectPreservesItsScope()
    {
        const string manual = "ftp=DIRECT";
        ProxyDirectiveSourceSelectionResult result =
            ProxyDirectiveSourceSelectionPolicy.Select(
                targetDecisionWasEvaluated: false,
                targetDecisionIsDirect: false,
                targetSpecificDirective: null,
                manualProxyConfigured: true,
                manualProxyDirective: manual);

        Ensure(result.Status
               == ProxyDirectiveSourceSelectionStatus.Direct,
            "수동 DIRECT-only 설정은 Direct 상태여야 합니다.");
        Ensure(result.Code
               == ProxyDirectiveSourceSelectionCode.ManualDirect,
            "수동 DIRECT 코드를 사용해야 합니다.");
        Ensure(result.SelectedDirectiveText == manual,
            "scoped DIRECT 원문을 canonical DIRECT로 축소하면 안 됩니다.");
        ProxyRouteDirective directive =
            result.ParseResult?.Directives.Single()
            ?? throw new InvalidOperationException(
                "수동 scoped DIRECT 파싱 결과가 필요합니다.");
        Ensure(directive.IsDirect && directive.Scope == "ftp",
            "DIRECT의 ftp 범위를 보존해야 합니다.");
    }

    private static void PartialProxyParseIsSelectedWithWarnings()
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

        Ensure(target.Status
               == ProxyDirectiveSourceSelectionStatus.SelectedWithWarnings,
            "유효 대상 프록시와 잘못된 구간이 함께 있으면 경고 포함 선택이어야 합니다.");
        Ensure(target.ProxyEndpointCount == 1
               && target.DirectDirectiveCount == 1,
            "유효 후보와 DIRECT fallback을 유지해야 합니다.");
        Ensure(manual.Status
               == ProxyDirectiveSourceSelectionStatus.SelectedWithWarnings,
            "수동 프록시 부분 성공도 경고 포함 선택이어야 합니다.");
        Ensure(target.ParseResult?.Issues.Any(issue =>
                issue.Severity
                    == ProxyDirectiveIssueSeverity.Error) == true
               && manual.ParseResult?.Issues.Any(issue =>
                   issue.Severity
                       == ProxyDirectiveIssueSeverity.Error) == true,
            "제외된 구간의 구조화 오류를 유지해야 합니다.");
    }

    private static void
        MissingSourcesRemainUnavailableWithoutDirectInference()
    {
        ProxyDirectiveSourceSelectionResult result =
            ProxyDirectiveSourceSelectionPolicy.Select(
                targetDecisionWasEvaluated: false,
                targetDecisionIsDirect: false,
                targetSpecificDirective: null,
                manualProxyConfigured: false,
                manualProxyDirective: null);

        Ensure(result.Status
               == ProxyDirectiveSourceSelectionStatus.Unavailable,
            "대상별 판정과 수동 설정이 모두 없으면 Unavailable이어야 합니다.");
        Ensure(result.SourceKind == ProxyDirectiveSourceKind.None
               && result.Code
                   == ProxyDirectiveSourceSelectionCode.NoAvailableDirective,
            "사용 가능한 출처가 없음을 구조화해야 합니다.");
        Ensure(!result.HasUsableSelection
               && result.ProxyEndpointCount == 0
               && result.DirectDirectiveCount == 0,
            "DIRECT나 프록시를 임의 추정하면 안 됩니다.");
        Ensure(result.Message.Contains(
                "DIRECT로 추정하지 않습니다",
                StringComparison.Ordinal),
            "fail-closed 설명이 필요합니다.");
    }

    private static void
        RawDirectiveAndHostsAreNotSerializedOrDisplayed()
    {
        const string secretTargetHost =
            "target-private-proxy.example.invalid";
        const string secretManualHost =
            "manual-private-proxy.example.invalid";
        ProxyDirectiveSourceSelectionResult result =
            ProxyDirectiveSourceSelectionPolicy.Select(
                targetDecisionWasEvaluated: true,
                targetDecisionIsDirect: false,
                targetSpecificDirective:
                    $"PROXY {secretTargetHost}:8080; DIRECT",
                manualProxyConfigured: true,
                manualProxyDirective:
                    $"PROXY {secretManualHost}:3128");

        string text = result.ToString();
        string json = JsonSerializer.Serialize(result);
        foreach (string secret in new[]
                 {
                     secretTargetHost,
                     secretManualHost,
                     $"PROXY {secretTargetHost}:8080; DIRECT"
                 })
        {
            Ensure(!text.Contains(
                    secret,
                    StringComparison.OrdinalIgnoreCase),
                $"ToString에 선택 원문이 남았습니다: {secret}");
            Ensure(!json.Contains(
                    secret,
                    StringComparison.OrdinalIgnoreCase),
                $"기본 JSON에 선택·미선택 프록시 원문이 남았습니다: {secret}");
        }

        Ensure(json.Contains(
                result.ParseResult!.Directives[0].HostFingerprint,
                StringComparison.Ordinal),
            "기본 JSON에는 원문 대신 비가역 호스트 지문을 유지할 수 있습니다.");
        Ensure(text.Contains(
                "프록시 후보 1개",
                StringComparison.Ordinal)
               && text.Contains(
                   "DIRECT 1개",
                   StringComparison.Ordinal),
            "안전한 표시에는 출처·상태와 개수만 포함해야 합니다.");
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
