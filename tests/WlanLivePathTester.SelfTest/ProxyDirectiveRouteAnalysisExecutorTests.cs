using System.Runtime.CompilerServices;
using System.Text.Json;
using WlanLivePathTester.Core.Proxy;

namespace WlanLivePathTester.SelfTest;

internal static class ProxyDirectiveRouteAnalysisExecutorTests
{
#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Run()
    {
        InvokesAnalyzerExactlyOnceForSelectedProxy();
        InvokesAnalyzerForPartialProxySelectionWithWarnings();
        DoesNotInvokeAnalyzerForDirectInvalidOrUnavailable();
        PreCanceledTokenDoesNotInvokeAnalyzer();
        ConvertsAnalyzerCancellationWithoutLeakingInput();
        ConvertsAnalyzerFailureWithoutLeakingException();
        RejectsNullAnalyzerResult();
        DoesNotSerializeRawDirectiveOrAnalysisPayload();
        Console.WriteLine(
            "PASS proxy directive route analysis executor tests");
    }

    private static void
        InvokesAnalyzerExactlyOnceForSelectedProxy()
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
            "선택된 프록시 계획은 분석 콜백을 정확히 한 번 호출해야 합니다.");
        Ensure(receivedDirective
               == selection.SelectedDirectiveText,
            "분석 콜백에는 선택된 대상별 지시문만 전달해야 합니다.");
        Ensure(receivedToken == source.Token,
            "사용자 취소 토큰을 분석 콜백에 그대로 전달해야 합니다.");
        Ensure(result.Status
               == ProxyDirectiveRouteAnalysisExecutionStatus.Completed,
            "정상 분석 콜백은 Completed여야 합니다.");
        Ensure(result.HasCompletedAnalysis
               && result.Analysis == "analysis-ok",
            "완료된 메모리 분석 결과를 호출자에게 유지해야 합니다.");
        Ensure(result.PlanCode
               == ProxyDirectiveRouteAnalysisPlanCode
                   .TargetSpecificProxySelected,
            "실행 결과에서 대상별 계획 코드를 유지해야 합니다.");
    }

    private static void
        InvokesAnalyzerForPartialProxySelectionWithWarnings()
    {
        ProxyDirectiveSourceSelectionResult selection =
            ProxyDirectiveSourceSelectionPolicy.Select(
                targetDecisionWasEvaluated: false,
                targetDecisionIsDirect: false,
                targetSpecificDirective: null,
                manualProxyConfigured: true,
                manualProxyDirective:
                    "PROXY valid-executor.example.invalid:8080; UNKNOWN invalid; DIRECT");
        int calls = 0;

        ProxyDirectiveRouteAnalysisExecutionResult<int> result =
            ProxyDirectiveRouteAnalysisExecutor.ExecuteAsync(
                    selection,
                    (_, _) =>
                    {
                        calls++;
                        return Task.FromResult(42);
                    })
                .GetAwaiter()
                .GetResult();

        Ensure(selection.Status
               == ProxyDirectiveSourceSelectionStatus.SelectedWithWarnings,
            "합성 선택은 SelectedWithWarnings여야 합니다.");
        Ensure(calls == 1
               && result.Status
                   == ProxyDirectiveRouteAnalysisExecutionStatus.Completed,
            "유효한 프록시 후보가 있으면 경고 상태에서도 명시 실행 콜백은 한 번 수행할 수 있습니다.");
        Ensure(result.HasParseErrors,
            "실행 결과에 제외된 파싱 구간이 있다는 위험을 유지해야 합니다.");
        Ensure(result.Analysis == 42,
            "분석 결과를 메모리에서 유지해야 합니다.");
    }

    private static void
        DoesNotInvokeAnalyzerForDirectInvalidOrUnavailable()
    {
        ProxyDirectiveSourceSelectionResult[] selections =
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

        for (int index = 0; index < selections.Length; index++)
        {
            ProxyDirectiveRouteAnalysisExecutionResult<string> result =
                ProxyDirectiveRouteAnalysisExecutor.ExecuteAsync(
                        selections[index],
                        (_, _) =>
                        {
                            calls++;
                            return Task.FromResult("must-not-run");
                        })
                    .GetAwaiter()
                    .GetResult();

            Ensure(result.Status == expected[index],
                $"비프록시 실행 상태가 잘못됐습니다: {index}");
            Ensure(!result.HasCompletedAnalysis
                   && result.Analysis is null,
                "콜백 미실행 상태에는 분석 결과가 없어야 합니다.");
        }

        Ensure(calls == 0,
            "DIRECT·Invalid·Unavailable 상태에서는 분석 콜백을 한 번도 호출하면 안 됩니다.");
    }

    private static void PreCanceledTokenDoesNotInvokeAnalyzer()
    {
        ProxyDirectiveSourceSelectionResult selection =
            CreateTargetProxySelection();
        using CancellationTokenSource source = new();
        source.Cancel();
        int calls = 0;

        ProxyDirectiveRouteAnalysisExecutionResult<string> result =
            ProxyDirectiveRouteAnalysisExecutor.ExecuteAsync(
                    selection,
                    (_, _) =>
                    {
                        calls++;
                        return Task.FromResult("must-not-run");
                    },
                    source.Token)
                .GetAwaiter()
                .GetResult();

        Ensure(result.Status
               == ProxyDirectiveRouteAnalysisExecutionStatus.Canceled,
            "사전 취소 토큰은 Canceled 실행 결과여야 합니다.");
        Ensure(calls == 0,
            "사전 취소에서는 분석 콜백을 호출하면 안 됩니다.");
        Ensure(result.Message.Contains(
                "이미 요청",
                StringComparison.Ordinal),
            "사전 취소와 콜백 내부 취소를 구분하는 설명이 필요합니다.");
    }

    private static void
        ConvertsAnalyzerCancellationWithoutLeakingInput()
    {
        const string secretHost =
            "cancel-private-proxy.example.invalid";
        ProxyDirectiveSourceSelectionResult selection =
            ProxyDirectiveSourceSelectionPolicy.Select(
                targetDecisionWasEvaluated: true,
                targetDecisionIsDirect: false,
                targetSpecificDirective:
                    $"PROXY {secretHost}:8080",
                manualProxyConfigured: false,
                manualProxyDirective: null);

        ProxyDirectiveRouteAnalysisExecutionResult<string> result =
            ProxyDirectiveRouteAnalysisExecutor.ExecuteAsync<string>(
                    selection,
                    (_, _) => throw new OperationCanceledException(
                        $"canceled at {secretHost}"))
                .GetAwaiter()
                .GetResult();

        Ensure(result.Status
               == ProxyDirectiveRouteAnalysisExecutionStatus.Canceled,
            "콜백의 OperationCanceledException은 Canceled로 변환해야 합니다.");
        Ensure(result.Analysis is null,
            "취소된 실행에는 분석 결과가 없어야 합니다.");
        Ensure(!result.Message.Contains(
                secretHost,
                StringComparison.OrdinalIgnoreCase),
            "취소 예외의 프록시 호스트를 결과 메시지에 반사하면 안 됩니다.");
    }

    private static void
        ConvertsAnalyzerFailureWithoutLeakingException()
    {
        const string secretHost =
            "failure-private-proxy.example.invalid";
        const string secretToken = "super-secret-token";
        ProxyDirectiveSourceSelectionResult selection =
            ProxyDirectiveSourceSelectionPolicy.Select(
                targetDecisionWasEvaluated: true,
                targetDecisionIsDirect: false,
                targetSpecificDirective:
                    $"PROXY {secretHost}:8080",
                manualProxyConfigured: false,
                manualProxyDirective: null);

        ProxyDirectiveRouteAnalysisExecutionResult<string> result =
            ProxyDirectiveRouteAnalysisExecutor.ExecuteAsync<string>(
                    selection,
                    (_, _) => throw new InvalidOperationException(
                        $"{secretHost} {secretToken}"))
                .GetAwaiter()
                .GetResult();

        Ensure(result.Status
               == ProxyDirectiveRouteAnalysisExecutionStatus.Failed,
            "일반 콜백 예외는 Failed로 변환해야 합니다.");
        Ensure(result.Analysis is null,
            "실패 실행에는 분석 결과가 없어야 합니다.");
        Ensure(!result.Message.Contains(
                secretHost,
                StringComparison.OrdinalIgnoreCase)
               && !result.Message.Contains(
                   secretToken,
                   StringComparison.Ordinal),
            "예외 메시지와 선택 원문을 실행 결과에 반사하면 안 됩니다.");
        Ensure(result.Message.Contains(
                "예외 원문과 프록시 지시문은 결과에 포함하지 않았습니다",
                StringComparison.Ordinal),
            "오류 비반사 경계를 설명해야 합니다.");
    }

    private static void RejectsNullAnalyzerResult()
    {
        ProxyDirectiveSourceSelectionResult selection =
            CreateTargetProxySelection();

        ProxyDirectiveRouteAnalysisExecutionResult<string> result =
            ProxyDirectiveRouteAnalysisExecutor.ExecuteAsync<string>(
                    selection,
                    (_, _) => Task.FromResult<string>(null!))
                .GetAwaiter()
                .GetResult();

        Ensure(result.Status
               == ProxyDirectiveRouteAnalysisExecutionStatus.Failed,
            "null 분석 결과는 Completed가 될 수 없습니다.");
        Ensure(!result.HasCompletedAnalysis
               && result.Analysis is null,
            "null 분석 결과를 완료 상태로 보존하면 안 됩니다.");
        Ensure(result.Message.Contains(
                "결과를 반환하지 않았습니다",
                StringComparison.Ordinal),
            "null 분석 결과의 고정 설명이 필요합니다.");
    }

    private static void
        DoesNotSerializeRawDirectiveOrAnalysisPayload()
    {
        const string secretHost =
            "serialize-private-proxy.example.invalid";
        const string secretAnalysis =
            "raw-route-analysis-containing-private-data";
        ProxyDirectiveSourceSelectionResult selection =
            ProxyDirectiveSourceSelectionPolicy.Select(
                targetDecisionWasEvaluated: true,
                targetDecisionIsDirect: false,
                targetSpecificDirective:
                    $"PROXY {secretHost}:8080; DIRECT",
                manualProxyConfigured: false,
                manualProxyDirective: null);
        ProxyDirectiveRouteAnalysisExecutionResult<string> result =
            ProxyDirectiveRouteAnalysisExecutor.ExecuteAsync(
                    selection,
                    (_, _) => Task.FromResult(secretAnalysis))
                .GetAwaiter()
                .GetResult();

        string json = JsonSerializer.Serialize(result);
        string text = result.ToString();
        string[] forbidden =
        [
            secretHost,
            secretAnalysis,
            selection.SelectedDirectiveText!
        ];
        foreach (string value in forbidden)
        {
            Ensure(!json.Contains(
                    value,
                    StringComparison.OrdinalIgnoreCase),
                $"실행 결과 JSON에 메모리 전용 값이 남았습니다: {value}");
            Ensure(!text.Contains(
                    value,
                    StringComparison.OrdinalIgnoreCase),
                $"실행 결과 표시에 메모리 전용 값이 남았습니다: {value}");
        }

        Ensure(json.Contains(
                "TargetSpecificProxySelected",
                StringComparison.Ordinal)
               && json.Contains(
                   "\"proxyEndpointCount\":1",
                   StringComparison.Ordinal),
            "안전한 실행 결과 JSON에는 고정 계획 코드와 개수가 필요합니다.");
    }

    private static ProxyDirectiveSourceSelectionResult
        CreateTargetProxySelection() =>
        ProxyDirectiveSourceSelectionPolicy.Select(
            targetDecisionWasEvaluated: true,
            targetDecisionIsDirect: false,
            targetSpecificDirective:
                "PROXY target-executor.example.invalid:8080; DIRECT",
            manualProxyConfigured: true,
            manualProxyDirective:
                "PROXY manual-executor.example.invalid:3128");

    private static void Ensure(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
