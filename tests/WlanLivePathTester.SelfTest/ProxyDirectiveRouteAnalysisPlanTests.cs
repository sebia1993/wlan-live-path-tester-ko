using System.Runtime.CompilerServices;
using System.Text.Json;
using WlanLivePathTester.Core.Proxy;

namespace WlanLivePathTester.SelfTest;

internal static class ProxyDirectiveRouteAnalysisPlanTests
{
#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Run()
    {
        AllowsOnlySelectedProxyEndpoints();
        AllowsSelectedProxyWithWarningsButPreservesRisk();
        BlocksNetworkLookupForTargetAndManualDirect();
        BlocksInvalidSourceDecisions();
        KeepsUnavailableSourceDistinctFromInvalid();
        DoesNotSerializeOrDisplayRawDirectiveText();
        Console.WriteLine(
            "PASS proxy directive route analysis plan tests");
    }

    private static void AllowsOnlySelectedProxyEndpoints()
    {
        ProxyDirectiveSourceSelectionResult selection =
            ProxyDirectiveSourceSelectionPolicy.Select(
                targetDecisionWasEvaluated: true,
                targetDecisionIsDirect: false,
                targetSpecificDirective:
                    "PROXY target-plan.example.invalid:8080; DIRECT",
                manualProxyConfigured: true,
                manualProxyDirective:
                    "PROXY manual-plan.example.invalid:3128");
        ProxyDirectiveRouteAnalysisPlan plan =
            ProxyDirectiveRouteAnalysisPlanPolicy.Create(selection);

        Ensure(plan.Status
               == ProxyDirectiveRouteAnalysisPlanStatus
                   .AnalyzeProxyEndpoints,
            "선택된 대상별 프록시는 분석 가능한 계획이어야 합니다.");
        Ensure(plan.Code
               == ProxyDirectiveRouteAnalysisPlanCode
                   .TargetSpecificProxySelected,
            "대상별 프록시 계획 코드를 유지해야 합니다.");
        Ensure(plan.ShouldAnalyzeProxyEndpoints
               && plan.NetworkLookupAllowed,
            "프록시 후보가 선택된 상태에서만 네트워크 조회를 허용해야 합니다.");
        Ensure(plan.ProxyEndpointCount == 1
               && plan.DirectDirectiveCount == 1,
            "계획에 프록시·DIRECT 개수를 유지해야 합니다.");
        Ensure(plan.DirectiveText == selection.SelectedDirectiveText,
            "분석기에 전달할 원문은 메모리에서만 유지해야 합니다.");
    }

    private static void
        AllowsSelectedProxyWithWarningsButPreservesRisk()
    {
        ProxyDirectiveSourceSelectionResult selection =
            ProxyDirectiveSourceSelectionPolicy.Select(
                targetDecisionWasEvaluated: false,
                targetDecisionIsDirect: false,
                targetSpecificDirective: null,
                manualProxyConfigured: true,
                manualProxyDirective:
                    "PROXY valid-plan.example.invalid:8080; UNKNOWN invalid; DIRECT");
        ProxyDirectiveRouteAnalysisPlan plan =
            ProxyDirectiveRouteAnalysisPlanPolicy.Create(selection);

        Ensure(selection.Status
               == ProxyDirectiveSourceSelectionStatus.SelectedWithWarnings,
            "합성 입력은 경고 포함 선택이어야 합니다.");
        Ensure(plan.Status
               == ProxyDirectiveRouteAnalysisPlanStatus
                   .AnalyzeProxyEndpoints,
            "유효한 프록시 후보가 있으면 명시 실행을 허용할 수 있습니다.");
        Ensure(plan.Code
               == ProxyDirectiveRouteAnalysisPlanCode.ManualProxySelected,
            "수동 프록시 계획 코드를 유지해야 합니다.");
        Ensure(plan.HasParseErrors,
            "제외된 구간의 파싱 오류를 계획에서 유지해야 합니다.");
        Ensure(plan.Message.Contains(
                "전체 경로 비교는 불완전할 수 있습니다",
                StringComparison.Ordinal),
            "부분 파싱의 비교 한계를 명시해야 합니다.");
    }

    private static void
        BlocksNetworkLookupForTargetAndManualDirect()
    {
        ProxyDirectiveRouteAnalysisPlan target =
            ProxyDirectiveRouteAnalysisPlanPolicy.Create(
                ProxyDirectiveSourceSelectionPolicy.Select(
                    targetDecisionWasEvaluated: true,
                    targetDecisionIsDirect: true,
                    targetSpecificDirective: null,
                    manualProxyConfigured: true,
                    manualProxyDirective:
                        "PROXY ignored.example.invalid:8080"));
        ProxyDirectiveRouteAnalysisPlan manual =
            ProxyDirectiveRouteAnalysisPlanPolicy.Create(
                ProxyDirectiveSourceSelectionPolicy.Select(
                    targetDecisionWasEvaluated: false,
                    targetDecisionIsDirect: false,
                    targetSpecificDirective: null,
                    manualProxyConfigured: true,
                    manualProxyDirective: "ftp=DIRECT"));

        Ensure(target.Status
               == ProxyDirectiveRouteAnalysisPlanStatus.DirectOnly
               && target.Code
                   == ProxyDirectiveRouteAnalysisPlanCode
                       .TargetSpecificDirect,
            "대상별 DIRECT는 DirectOnly 계획이어야 합니다.");
        Ensure(manual.Status
               == ProxyDirectiveRouteAnalysisPlanStatus.DirectOnly
               && manual.Code
                   == ProxyDirectiveRouteAnalysisPlanCode.ManualDirect,
            "수동 scoped DIRECT도 DirectOnly 계획이어야 합니다.");
        Ensure(!target.NetworkLookupAllowed
               && !manual.NetworkLookupAllowed,
            "DIRECT-only에서는 DNS·프록시 경로 조회를 허용하면 안 됩니다.");
        Ensure(manual.DirectiveText == "ftp=DIRECT",
            "수동 DIRECT의 범위를 계획에서도 유지해야 합니다.");
    }

    private static void BlocksInvalidSourceDecisions()
    {
        ProxyDirectiveSourceSelectionResult selection =
            ProxyDirectiveSourceSelectionPolicy.Select(
                targetDecisionWasEvaluated: true,
                targetDecisionIsDirect: false,
                targetSpecificDirective: "DIRECT",
                manualProxyConfigured: true,
                manualProxyDirective:
                    "PROXY valid-manual.example.invalid:8080");
        ProxyDirectiveRouteAnalysisPlan plan =
            ProxyDirectiveRouteAnalysisPlanPolicy.Create(selection);

        Ensure(plan.Status
               == ProxyDirectiveRouteAnalysisPlanStatus.Blocked,
            "Invalid source decision must produce a blocked plan.");
        Ensure(plan.Code
               == ProxyDirectiveRouteAnalysisPlanCode
                   .InvalidSourceDecision,
            "Invalid source decision plan code is required.");
        Ensure(!plan.NetworkLookupAllowed
               && plan.DirectiveText is null,
            "Blocked plan must not retain executable directive text.");
        Ensure(plan.Message.Contains(
                "경로 조회를 시작하지 않습니다",
                StringComparison.Ordinal),
            "차단된 네트워크 경계를 설명해야 합니다.");
    }

    private static void
        KeepsUnavailableSourceDistinctFromInvalid()
    {
        ProxyDirectiveSourceSelectionResult selection =
            ProxyDirectiveSourceSelectionPolicy.Select(
                targetDecisionWasEvaluated: false,
                targetDecisionIsDirect: false,
                targetSpecificDirective: null,
                manualProxyConfigured: false,
                manualProxyDirective: null);
        ProxyDirectiveRouteAnalysisPlan plan =
            ProxyDirectiveRouteAnalysisPlanPolicy.Create(selection);

        Ensure(plan.Status
               == ProxyDirectiveRouteAnalysisPlanStatus.Unavailable,
            "입력 없음은 Blocked가 아니라 Unavailable이어야 합니다.");
        Ensure(plan.Code
               == ProxyDirectiveRouteAnalysisPlanCode
                   .MissingSourceDecision,
            "사용 가능한 출처 없음 코드를 유지해야 합니다.");
        Ensure(!plan.NetworkLookupAllowed
               && plan.ProxyEndpointCount == 0
               && plan.DirectDirectiveCount == 0,
            "Unavailable 상태에서 프록시·DIRECT·네트워크 조회를 추정하면 안 됩니다.");
    }

    private static void
        DoesNotSerializeOrDisplayRawDirectiveText()
    {
        const string secretHost =
            "plan-private-proxy.example.invalid";
        ProxyDirectiveSourceSelectionResult selection =
            ProxyDirectiveSourceSelectionPolicy.Select(
                targetDecisionWasEvaluated: true,
                targetDecisionIsDirect: false,
                targetSpecificDirective:
                    $"PROXY {secretHost}:8080; DIRECT",
                manualProxyConfigured: false,
                manualProxyDirective: null);
        ProxyDirectiveRouteAnalysisPlan plan =
            ProxyDirectiveRouteAnalysisPlanPolicy.Create(selection);

        string json = JsonSerializer.Serialize(plan);
        string text = plan.ToString();
        Ensure(!json.Contains(
                secretHost,
                StringComparison.OrdinalIgnoreCase)
               && !text.Contains(
                   secretHost,
                   StringComparison.OrdinalIgnoreCase),
            "계획 JSON·표시에 원문 프록시 호스트가 남으면 안 됩니다.");
        Ensure(!json.Contains(
                plan.DirectiveText!,
                StringComparison.Ordinal),
            "분석용 원문 전체 문자열은 기본 JSON에서 제외해야 합니다.");
        Ensure(json.Contains(
                plan.ParseResult!.Directives[0].HostFingerprint,
                StringComparison.Ordinal),
            "안전한 계획 JSON에는 비가역 호스트 지문을 유지할 수 있습니다.");
        Ensure(text.Contains(
                "AnalyzeProxyEndpoints",
                StringComparison.Ordinal)
               && text.Contains(
                   "프록시 후보 1개",
                   StringComparison.Ordinal),
            "안전한 표시에는 계획 상태와 개수만 있어야 합니다.");
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
