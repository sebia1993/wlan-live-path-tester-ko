using System.Runtime.CompilerServices;
using System.Text.Json;
using WlanLivePathTester.Core.Proxy;

namespace WlanLivePathTester.SelfTest;

internal static class ProxyDirectiveRouteAnalysisPlanV3Tests
{
#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Run()
    {
        SelectedTargetProxyCreatesAllowedPlan();
        PartialManualProxyPreservesParseRisk();
        DirectPlansNeverAllowNetworkLookup();
        InvalidAndUnavailableSelectionsAreNotExecutable();
        ExecutorInvokesAllowedAnalyzerExactlyOnce();
        ExecutorNeverInvokesDisallowedOrPreCanceledPlans();
        ExecutorConvertsCancellationFailureAndNullSafely();
        PlanAndExecutionJsonExcludeRawDirectiveAndPayload();
        Console.WriteLine(
            "PASS proxy route analysis plan and executor v3 tests");
    }

    private static void SelectedTargetProxyCreatesAllowedPlan()
    {
        const string targetHost = "target-plan.example.invalid";
        ProxyDirectiveSourceSelectionResult selection =
            ProxyDirectiveSourceSelectionPolicy.Select(
                targetDecisionWasEvaluated: true,
                targetDecisionIsDirect: false,
                targetSpecificDirective:
                    $"PROXY {targetHost}:8080; DIRECT",
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
        Ensure(plan.SourceKind
               == ProxyDirectiveSourceKind.TargetSpecificAutoProxy
               && plan.SelectionStatus
                   == ProxyDirectiveSourceSelectionStatus.Selected,
            "계획에 선택 출처와 상태를 유지해야 합니다.");
        Ensure(plan.ShouldAnalyzeProxyEndpoints
               && plan.NetworkLookupAllowed,
            "프록시 후보가 선택된 경우에만 네트워크 조회를 허용해야 합니다.");
        Ensure(plan.ProxyEndpointCount == 1
               && plan.DirectDirectiveCount == 1
               && !plan.HasParseErrors,
            "프록시·DIRECT 개수와 파싱 상태가 정확해야 합니다.");
        Ensure(plan.DirectiveText == selection.SelectedDirectiveText
               && plan.DirectiveText?.Contains(
                   targetHost,
                   StringComparison.OrdinalIgnoreCase) == true,
            "승인된 원문은 후속 분석용으로 메모리에만 유지해야 합니다.");
    }

    private static void PartialManualProxyPreservesParseRisk()
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
            "합성 수동 설정은 경고 포함 선택이어야 합니다.");
        Ensure(plan.Status
               == ProxyDirectiveRouteAnalysisPlanStatus
                   .AnalyzeProxyEndpoints
               && plan.Code
                   == ProxyDirectiveRouteAnalysisPlanCode
                       .ManualProxySelected,
            "유효한 수동 프록시 후보는 분석 계획을 만들 수 있어야 합니다.");
        Ensure(plan.HasParseErrors,
            "제외된 세그먼트의 파싱 오류를 계획에서 유지해야 합니다.");
        Ensure(plan.Message.Contains(
                "전체 경로 비교는 불완전할 수 있습니다",
                StringComparison.Ordinal),
            "부분 파싱이 전체 fallback 비교를 제한한다는 설명이 필요합니다.");
    }

    private static void DirectPlansNeverAllowNetworkLookup()
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
            "DIRECT-only 계획에서는 DNS·프록시 경로 조회를 허용하면 안 됩니다.");
        Ensure(manual.DirectiveText == "ftp=DIRECT",
            "수동 DIRECT의 범위를 계획에서도 유지해야 합니다.");
    }

    private static void InvalidAndUnavailableSelectionsAreNotExecutable()
    {
        ProxyDirectiveSourceSelectionResult invalidSelection =
            ProxyDirectiveSourceSelectionPolicy.Select(
                targetDecisionWasEvaluated: true,
                targetDecisionIsDirect: false,
                targetSpecificDirective: "DIRECT",
                manualProxyConfigured: true,
                manualProxyDirective:
                    "PROXY valid-manual.example.invalid:8080");
        ProxyDirectiveSourceSelectionResult unavailableSelection =
            ProxyDirectiveSourceSelectionPolicy.Select(
                targetDecisionWasEvaluated: false,
                targetDecisionIsDirect: false,
                targetSpecificDirective: null,
                manualProxyConfigured: false,
                manualProxyDirective: null);
        ProxyDirectiveRouteAnalysisPlan invalid =
            ProxyDirectiveRouteAnalysisPlanPolicy.Create(
                invalidSelection);
        ProxyDirectiveRouteAnalysisPlan unavailable =
            ProxyDirectiveRouteAnalysisPlanPolicy.Create(
                unavailableSelection);

        Ensure(invalid.Status
               == ProxyDirectiveRouteAnalysisPlanStatus.Blocked
               && invalid.Code
                   == ProxyDirectiveRouteAnalysisPlanCode
                       .InvalidSourceDecision,
            "Invalid 선택은 Blocked 계획이어야 합니다.");
        Ensure(unavailable.Status
               == ProxyDirectiveRouteAnalysisPlanStatus.Unavailable
               && unavailable.Code
                   == ProxyDirectiveRouteAnalysisPlanCode
                       .MissingSourceDecision,
            "출처 없음은 Blocked가 아니라 Unavailable이어야 합니다.");
        Ensure(!invalid.NetworkLookupAllowed
               && !unavailable.NetworkLookupAllowed
               && invalid.DirectiveText is null
               && unavailable.DirectiveText is null,
            "Blocked·Unavailable 계획은 실행 가능한 원문이나 네트워크 권한을 가지면 안 됩니다.");
    }

    private static void ExecutorInvokesAllowedAnalyzerExactlyOnce()
    {
        ProxyDirectiveSourceSelectionResult selection =
            CreateTargetProxySelection();
        int calls = 0;
        string? receivedDirective = null;
        CancellationToken receivedToken = default;
        using CancellationTokenSource source = new();

        ProxyDirectiveRouteAnalysisExecutionResult<string> result =
            ProxyDirectiveRouteAnalysisExecutor.ExecuteAsync(
                    selection,
                    (directive, token) =>
                    {
                        calls++;
                        receivedDirective = directive;
                        receivedToken = token;
                        return Task.FromResult("analysis-ok");
                    },
                    source.Token)
                .GetAwaiter()
                .GetResult();

        Ensure(calls == 1,
            "승인된 프록시 계획은 분석 콜백을 정확히 한 번 호출해야 합니다.");
        Ensure(receivedDirective
               == selection.SelectedDirectiveText,
            "콜백에는 승인된 지시문만 전달해야 합니다.");
        Ensure(receivedToken == source.Token,
            "사용자 취소 토큰을 같은 값으로 전달해야 합니다.");
        Ensure(result.Status
               == ProxyDirectiveRouteAnalysisExecutionStatus.Completed
               && result.HasCompletedAnalysis
               && result.Analysis == "analysis-ok",
            "정상 콜백 결과는 Completed로 메모리에 유지해야 합니다.");
        Ensure(result.PlanCode
               == ProxyDirectiveRouteAnalysisPlanCode
                   .TargetSpecificProxySelected,
            "실행 결과에 승인 계획 코드를 유지해야 합니다.");
    }

    private static void
        ExecutorNeverInvokesDisallowedOrPreCanceledPlans()
    {
        ProxyDirectiveSourceSelectionResult[] disallowed =
        [
            ProxyDirectiveSourceSelectionPolicy.Select(
                targetDecisionWasEvaluated: true,
                targetDecisionIsDirect: true,
                targetSpecificDirective: null,
                manualProxyConfigured: true,
                manualProxyDirective:
                    "PROXY ignored-direct.example.invalid:8080"),
            ProxyDirectiveSourceSelectionPolicy.Select(
                targetDecisionWasEvaluated: true,
                targetDecisionIsDirect: false,
                targetSpecificDirective: "DIRECT",
                manualProxyConfigured: true,
                manualProxyDirective:
                    "PROXY ignored-invalid.example.invalid:8080"),
            ProxyDirectiveSourceSelectionPolicy.Select(
                targetDecisionWasEvaluated: false,
                targetDecisionIsDirect: false,
                targetSpecificDirective: null,
                manualProxyConfigured: false,
                manualProxyDirective: null)
        ];
        ProxyDirectiveRouteAnalysisExecutionStatus[] expected =
        [
            ProxyDirectiveRouteAnalysisExecutionStatus.DirectOnly,
            ProxyDirectiveRouteAnalysisExecutionStatus.Blocked,
            ProxyDirectiveRouteAnalysisExecutionStatus.Unavailable
        ];
        int calls = 0;

        for (int index = 0; index < disallowed.Length; index++)
        {
            ProxyDirectiveRouteAnalysisExecutionResult<string> result =
                ProxyDirectiveRouteAnalysisExecutor.ExecuteAsync(
                        disallowed[index],
                        (_, _) =>
                        {
                            calls++;
                            return Task.FromResult("must-not-run");
                        })
                    .GetAwaiter()
                    .GetResult();
            Ensure(result.Status == expected[index]
                   && result.Analysis is null,
                $"비실행 계획의 결과 상태가 잘못됐습니다: {index}");
        }

        using CancellationTokenSource canceledSource = new();
        canceledSource.Cancel();
        ProxyDirectiveRouteAnalysisExecutionResult<string> canceled =
            ProxyDirectiveRouteAnalysisExecutor.ExecuteAsync(
                    CreateTargetProxySelection(),
                    (_, _) =>
                    {
                        calls++;
                        return Task.FromResult("must-not-run");
                    },
                    canceledSource.Token)
                .GetAwaiter()
                .GetResult();

        Ensure(canceled.Status
               == ProxyDirectiveRouteAnalysisExecutionStatus.Canceled,
            "사전 취소는 Canceled여야 합니다.");
        Ensure(calls == 0,
            "DIRECT·Invalid·Unavailable·사전 취소에서는 콜백을 한 번도 호출하면 안 됩니다.");
        Ensure(canceled.Message.Contains(
                "이미 요청",
                StringComparison.Ordinal),
            "사전 취소와 콜백 내부 취소를 구분해야 합니다.");
    }

    private static void
        ExecutorConvertsCancellationFailureAndNullSafely()
    {
        const string cancelHost =
            "cancel-private-proxy.example.invalid";
        const string failureHost =
            "failure-private-proxy.example.invalid";
        const string secretToken = "super-secret-token";

        ProxyDirectiveRouteAnalysisExecutionResult<string> canceled =
            ProxyDirectiveRouteAnalysisExecutor.ExecuteAsync<string>(
                    CreateSelectionForHost(cancelHost),
                    (_, _) => throw new OperationCanceledException(
                        $"canceled at {cancelHost}"))
                .GetAwaiter()
                .GetResult();
        ProxyDirectiveRouteAnalysisExecutionResult<string> failed =
            ProxyDirectiveRouteAnalysisExecutor.ExecuteAsync<string>(
                    CreateSelectionForHost(failureHost),
                    (_, _) => throw new InvalidOperationException(
                        $"{failureHost} {secretToken}"))
                .GetAwaiter()
                .GetResult();
        ProxyDirectiveRouteAnalysisExecutionResult<string> nullResult =
            ProxyDirectiveRouteAnalysisExecutor.ExecuteAsync<string>(
                    CreateTargetProxySelection(),
                    (_, _) => Task.FromResult<string>(null!))
                .GetAwaiter()
                .GetResult();

        Ensure(canceled.Status
               == ProxyDirectiveRouteAnalysisExecutionStatus.Canceled
               && canceled.Analysis is null,
            "콜백 취소를 Canceled로 변환해야 합니다.");
        Ensure(failed.Status
               == ProxyDirectiveRouteAnalysisExecutionStatus.Failed
               && failed.Analysis is null,
            "일반 콜백 예외를 Failed로 변환해야 합니다.");
        Ensure(nullResult.Status
               == ProxyDirectiveRouteAnalysisExecutionStatus.Failed
               && !nullResult.HasCompletedAnalysis,
            "null 결과는 Completed가 될 수 없습니다.");
        string combined = canceled.Message
            + Environment.NewLine
            + failed.Message
            + Environment.NewLine
            + nullResult.Message;
        foreach (string secret in new[]
                 {
                     cancelHost,
                     failureHost,
                     secretToken
                 })
        {
            Ensure(!combined.Contains(
                    secret,
                    StringComparison.OrdinalIgnoreCase),
                $"취소·예외 원문을 실행 결과에 반사하면 안 됩니다: {secret}");
        }
    }

    private static void PlanAndExecutionJsonExcludeRawDirectiveAndPayload()
    {
        const string secretHost =
            "serialize-private-proxy.example.invalid";
        const string secretPayload =
            "raw-route-analysis-containing-private-data";
        ProxyDirectiveSourceSelectionResult selection =
            CreateSelectionForHost(secretHost);
        ProxyDirectiveRouteAnalysisPlan plan =
            ProxyDirectiveRouteAnalysisPlanPolicy.Create(selection);
        ProxyDirectiveRouteAnalysisExecutionResult<string> execution =
            ProxyDirectiveRouteAnalysisExecutor.ExecuteAsync(
                    selection,
                    (_, _) => Task.FromResult(secretPayload))
                .GetAwaiter()
                .GetResult();

        string planJson = JsonSerializer.Serialize(plan);
        string executionJson = JsonSerializer.Serialize(execution);
        string text = plan + Environment.NewLine + execution;
        foreach (string secret in new[]
                 {
                     secretHost,
                     secretPayload,
                     selection.SelectedDirectiveText!
                 })
        {
            Ensure(!planJson.Contains(
                    secret,
                    StringComparison.OrdinalIgnoreCase),
                $"계획 JSON에 메모리 전용 값이 남았습니다: {secret}");
            Ensure(!executionJson.Contains(
                    secret,
                    StringComparison.OrdinalIgnoreCase),
                $"실행 JSON에 메모리 전용 값이 남았습니다: {secret}");
            Ensure(!text.Contains(
                    secret,
                    StringComparison.OrdinalIgnoreCase),
                $"안전 표시 문자열에 메모리 전용 값이 남았습니다: {secret}");
        }

        Ensure(planJson.Contains(
                selection.ParseResult!.Directives[0]
                    .HostFingerprint,
                StringComparison.Ordinal),
            "계획 JSON에는 원문 대신 비가역 호스트 지문을 유지할 수 있습니다.");
        Ensure(executionJson.Contains(
                "\"planCode\":0",
                StringComparison.Ordinal)
               && executionJson.Contains(
                   "\"proxyEndpointCount\":1",
                   StringComparison.Ordinal),
            "실행 JSON에는 안전한 고정 상태와 후보 수가 필요합니다.");
        Ensure(!executionJson.Contains(
                "Analysis",
                StringComparison.Ordinal),
            "분석 payload 속성 자체를 기본 JSON에 포함하면 안 됩니다.");
    }

    private static ProxyDirectiveSourceSelectionResult
        CreateTargetProxySelection() =>
        CreateSelectionForHost(
            "target-executor.example.invalid");

    private static ProxyDirectiveSourceSelectionResult
        CreateSelectionForHost(string host) =>
        ProxyDirectiveSourceSelectionPolicy.Select(
            targetDecisionWasEvaluated: true,
            targetDecisionIsDirect: false,
            targetSpecificDirective:
                $"PROXY {host}:8080; DIRECT",
            manualProxyConfigured: true,
            manualProxyDirective:
                "PROXY ignored-manual.example.invalid:3128");

    private static void Ensure(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
