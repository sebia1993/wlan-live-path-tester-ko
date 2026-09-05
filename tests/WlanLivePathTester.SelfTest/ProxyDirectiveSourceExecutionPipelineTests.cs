using System.Runtime.CompilerServices;
using System.Text.Json;
using WlanLivePathTester.Core.Proxy;

namespace WlanLivePathTester.SelfTest;

internal static class ProxyDirectiveSourceExecutionPipelineTests
{
    private static readonly DateTimeOffset CapturedAt =
        DateTimeOffset.UnixEpoch.AddDays(9);

#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Run()
    {
        ExecutesOnlyTheSuccessfulTargetSpecificProxy();
        DoesNotFallBackAfterTargetDecisionFailure();
        DoesNotExecuteForTargetSpecificDirect();
        UsesManualProxyOnlyWhenTargetDecisionWasNotAttempted();
        DoesNotExecuteAfterManualReadFailure();
        HonorsCancellationBeforeTheAnalyzer();
        SafeExecutionResultDoesNotExposeEitherSource();
        Console.WriteLine(
            "PASS end-to-end proxy source execution pipeline tests");
    }

    private static void ExecutesOnlyTheSuccessfulTargetSpecificProxy()
    {
        const string targetHost =
            "pipeline-target.example.invalid";
        const string manualHost =
            "pipeline-manual.example.invalid";
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
        int calls = 0;
        string? received = null;

        ProxyDirectiveRouteAnalysisExecutionResult<string> result =
            ProxyDirectiveSourceExecutionPipeline.ExecuteAsync(
                    snapshot,
                    (directive, _) =>
                    {
                        calls++;
                        received = directive;
                        return Task.FromResult("target-analysis");
                    })
                .GetAwaiter()
                .GetResult();

        Ensure(calls == 1,
            "성공한 대상별 프록시에서는 분석 콜백을 정확히 한 번 호출해야 합니다.");
        Ensure(received?.Contains(
                targetHost,
                StringComparison.OrdinalIgnoreCase) == true,
            "분석 콜백에는 대상별 지시문이 전달돼야 합니다.");
        Ensure(received?.Contains(
                manualHost,
                StringComparison.OrdinalIgnoreCase) == false,
            "수동 프록시가 대상별 판정을 덮어쓰면 안 됩니다.");
        Ensure(result.Status
               == ProxyDirectiveRouteAnalysisExecutionStatus.Completed
               && result.PlanCode
                   == ProxyDirectiveRouteAnalysisPlanCode
                       .TargetSpecificProxySelected
               && result.Analysis == "target-analysis",
            "대상별 프록시의 완료 상태·계획 코드·메모리 분석 결과를 유지해야 합니다.");
    }

    private static void DoesNotFallBackAfterTargetDecisionFailure()
    {
        const string manualHost =
            "valid-but-blocked-manual.example.invalid";
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
        int calls = 0;

        ProxyDirectiveRouteAnalysisExecutionResult<string> result =
            ProxyDirectiveSourceExecutionPipeline.ExecuteAsync(
                    snapshot,
                    (_, _) =>
                    {
                        calls++;
                        return Task.FromResult("must-not-run");
                    })
                .GetAwaiter()
                .GetResult();

        Ensure(calls == 0,
            "PAC/WPAD 판정을 시도했지만 실패했으면 유효한 수동 프록시가 있어도 분석 콜백을 호출하면 안 됩니다.");
        Ensure(result.Status
               == ProxyDirectiveRouteAnalysisExecutionStatus.Blocked
               && result.SourceKind
                   == ProxyDirectiveSourceKind.TargetSpecificAutoProxy
               && result.PlanCode
                   == ProxyDirectiveRouteAnalysisPlanCode
                       .InvalidSourceDecision,
            "대상별 판정 실패를 수동 프록시 성공으로 바꾸지 말고 차단해야 합니다.");
        Ensure(result.ProxyEndpointCount == 0
               && result.Analysis is null,
            "차단 결과에 선택된 수동 프록시나 분석 결과가 남으면 안 됩니다.");
    }

    private static void DoesNotExecuteForTargetSpecificDirect()
    {
        ProxyDirectiveSourceSnapshot snapshot = new(
            CapturedAt,
            ProxyDirectiveSourceReadStatus.Success,
            targetDecisionIsDirect: true,
            targetSpecificDirective: null,
            ProxyDirectiveSourceReadStatus.Success,
            manualProxyConfigured: true,
            manualProxyDirective:
                "PROXY ignored-direct-manual.example.invalid:8080",
            autoDetectEnabled: true,
            pacConfigured: true);
        int calls = 0;

        ProxyDirectiveRouteAnalysisExecutionResult<string> result =
            ProxyDirectiveSourceExecutionPipeline.ExecuteAsync(
                    snapshot,
                    (_, _) =>
                    {
                        calls++;
                        return Task.FromResult("must-not-run");
                    })
                .GetAwaiter()
                .GetResult();

        Ensure(calls == 0,
            "대상별 DIRECT에서는 DNS·프록시 경로 분석 콜백을 호출하면 안 됩니다.");
        Ensure(result.Status
               == ProxyDirectiveRouteAnalysisExecutionStatus.DirectOnly
               && result.PlanCode
                   == ProxyDirectiveRouteAnalysisPlanCode
                       .TargetSpecificDirect
               && result.DirectDirectiveCount == 1,
            "대상별 DIRECT의 실행 상태와 계획 코드를 유지해야 합니다.");
    }

    private static void
        UsesManualProxyOnlyWhenTargetDecisionWasNotAttempted()
    {
        const string manualHost =
            "pipeline-selected-manual.example.invalid";
        ProxyDirectiveSourceSnapshot snapshot = new(
            CapturedAt,
            ProxyDirectiveSourceReadStatus.NotAttempted,
            targetDecisionIsDirect: false,
            targetSpecificDirective: null,
            ProxyDirectiveSourceReadStatus.Success,
            manualProxyConfigured: true,
            manualProxyDirective:
                $"PROXY {manualHost}:3128; DIRECT",
            autoDetectEnabled: false,
            pacConfigured: false);
        int calls = 0;
        string? received = null;

        ProxyDirectiveRouteAnalysisExecutionResult<int> result =
            ProxyDirectiveSourceExecutionPipeline.ExecuteAsync(
                    snapshot,
                    (directive, _) =>
                    {
                        calls++;
                        received = directive;
                        return Task.FromResult(7);
                    })
                .GetAwaiter()
                .GetResult();

        Ensure(calls == 1
               && received?.Contains(
                   manualHost,
                   StringComparison.OrdinalIgnoreCase) == true,
            "대상별 판정을 수행하지 않았을 때만 수동 프록시를 분석해야 합니다.");
        Ensure(result.Status
               == ProxyDirectiveRouteAnalysisExecutionStatus.Completed
               && result.SourceKind
                   == ProxyDirectiveSourceKind.ManualProxyConfiguration
               && result.PlanCode
                   == ProxyDirectiveRouteAnalysisPlanCode
                       .ManualProxySelected
               && result.Analysis == 7,
            "수동 프록시 실행의 출처·계획 코드·결과를 유지해야 합니다.");
    }

    private static void DoesNotExecuteAfterManualReadFailure()
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
        int calls = 0;

        ProxyDirectiveRouteAnalysisExecutionResult<string> result =
            ProxyDirectiveSourceExecutionPipeline.ExecuteAsync(
                    snapshot,
                    (_, _) =>
                    {
                        calls++;
                        return Task.FromResult("must-not-run");
                    })
                .GetAwaiter()
                .GetResult();

        Ensure(calls == 0,
            "수동 설정 읽기 실패에서는 분석 콜백을 호출하면 안 됩니다.");
        Ensure(result.Status
               == ProxyDirectiveRouteAnalysisExecutionStatus.Blocked
               && result.SourceKind
                   == ProxyDirectiveSourceKind.ManualProxyConfiguration,
            "수동 읽기 실패를 DIRECT나 출처 없음으로 바꾸지 말고 차단해야 합니다.");
    }

    private static void HonorsCancellationBeforeTheAnalyzer()
    {
        ProxyDirectiveSourceSnapshot snapshot = new(
            CapturedAt,
            ProxyDirectiveSourceReadStatus.Success,
            targetDecisionIsDirect: false,
            targetSpecificDirective:
                "PROXY cancel-before-analysis.example.invalid:8080",
            ProxyDirectiveSourceReadStatus.NotAttempted,
            manualProxyConfigured: false,
            manualProxyDirective: null,
            autoDetectEnabled: true,
            pacConfigured: true);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        int calls = 0;

        ProxyDirectiveRouteAnalysisExecutionResult<string> result =
            ProxyDirectiveSourceExecutionPipeline.ExecuteAsync(
                    snapshot,
                    (_, _) =>
                    {
                        calls++;
                        return Task.FromResult("must-not-run");
                    },
                    cancellation.Token)
                .GetAwaiter()
                .GetResult();

        Ensure(calls == 0,
            "사전 취소에서는 분석 콜백을 호출하면 안 됩니다.");
        Ensure(result.Status
               == ProxyDirectiveRouteAnalysisExecutionStatus.Canceled
               && result.Analysis is null,
            "사전 취소 상태와 빈 분석 결과를 유지해야 합니다.");
    }

    private static void SafeExecutionResultDoesNotExposeEitherSource()
    {
        const string targetHost =
            "pipeline-private-target.example.invalid";
        const string manualHost =
            "pipeline-private-manual.example.invalid";
        const string analysisPayload =
            "private-route-analysis-payload";
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

        ProxyDirectiveRouteAnalysisExecutionResult<string> result =
            ProxyDirectiveSourceExecutionPipeline.ExecuteAsync(
                    snapshot,
                    (_, _) => Task.FromResult(analysisPayload))
                .GetAwaiter()
                .GetResult();
        string json = JsonSerializer.Serialize(result);
        string text = result.ToString();

        foreach (string secret in new[]
                 {
                     targetHost,
                     manualHost,
                     analysisPayload,
                     $"PROXY {targetHost}:8080; DIRECT"
                 })
        {
            Ensure(!json.Contains(
                    secret,
                    StringComparison.OrdinalIgnoreCase),
                $"파이프라인 실행 JSON에 메모리 전용 값이 남았습니다: {secret}");
            Ensure(!text.Contains(
                    secret,
                    StringComparison.OrdinalIgnoreCase),
                $"파이프라인 실행 표시에 메모리 전용 값이 남았습니다: {secret}");
        }

        Ensure(json.Contains(
                "TargetSpecificProxySelected",
                StringComparison.Ordinal)
               && json.Contains(
                   "\"proxyEndpointCount\":1",
                   StringComparison.Ordinal),
            "안전한 실행 결과에는 고정 계획 코드와 후보 수가 필요합니다.");
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
