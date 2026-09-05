using System.Diagnostics;
using System.Text.Json.Serialization;

namespace WlanLivePathTester.Core.Proxy;

public enum ProxyDirectiveRouteAnalysisExecutionStatus
{
    Completed,
    DirectOnly,
    Blocked,
    Unavailable,
    Canceled,
    Failed
}

[DebuggerDisplay("{RedactedDisplay,nq}")]
public sealed class ProxyDirectiveRouteAnalysisExecutionResult<TAnalysis>
{
    internal ProxyDirectiveRouteAnalysisExecutionResult(
        ProxyDirectiveRouteAnalysisExecutionStatus status,
        ProxyDirectiveRouteAnalysisPlan plan,
        TAnalysis? analysis,
        string message)
    {
        Status = status;
        PlanStatus = plan.Status;
        PlanCode = plan.Code;
        SourceKind = plan.SourceKind;
        SelectionStatus = plan.SelectionStatus;
        ProxyEndpointCount = plan.ProxyEndpointCount;
        DirectDirectiveCount = plan.DirectDirectiveCount;
        HasParseErrors = plan.HasParseErrors;
        Analysis = analysis;
        Message = message;
        RedactedDisplay =
            $"{Status} · {PlanStatus} · {PlanCode} · {SourceKind} · 프록시 후보 {ProxyEndpointCount}개 · DIRECT {DirectDirectiveCount}개";
    }

    [JsonPropertyName("status")]
    public ProxyDirectiveRouteAnalysisExecutionStatus Status { get; }

    [JsonPropertyName("planStatus")]
    public ProxyDirectiveRouteAnalysisPlanStatus PlanStatus { get; }

    [JsonPropertyName("planCode")]
    public ProxyDirectiveRouteAnalysisPlanCode PlanCode { get; }

    [JsonPropertyName("sourceKind")]
    public ProxyDirectiveSourceKind SourceKind { get; }

    [JsonPropertyName("selectionStatus")]
    public ProxyDirectiveSourceSelectionStatus SelectionStatus { get; }

    [JsonPropertyName("proxyEndpointCount")]
    public int ProxyEndpointCount { get; }

    [JsonPropertyName("directDirectiveCount")]
    public int DirectDirectiveCount { get; }

    [JsonPropertyName("hasParseErrors")]
    public bool HasParseErrors { get; }

    [JsonIgnore]
    public TAnalysis? Analysis { get; }

    [JsonPropertyName("message")]
    public string Message { get; }

    [JsonPropertyName("redactedDisplay")]
    public string RedactedDisplay { get; }

    [JsonPropertyName("hasCompletedAnalysis")]
    public bool HasCompletedAnalysis =>
        Status == ProxyDirectiveRouteAnalysisExecutionStatus.Completed
        && Analysis is not null;

    public override string ToString() => RedactedDisplay;
}

public static class ProxyDirectiveRouteAnalysisExecutor
{
    public static async Task<
        ProxyDirectiveRouteAnalysisExecutionResult<TAnalysis>>
        ExecuteAsync<TAnalysis>(
            ProxyDirectiveSourceSelectionResult selection,
            Func<string, CancellationToken, Task<TAnalysis>> analyzer,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(analyzer);

        ProxyDirectiveRouteAnalysisPlan plan =
            ProxyDirectiveRouteAnalysisPlanPolicy.Create(selection);
        switch (plan.Status)
        {
            case ProxyDirectiveRouteAnalysisPlanStatus.DirectOnly:
                return CreateResult<TAnalysis>(
                    ProxyDirectiveRouteAnalysisExecutionStatus.DirectOnly,
                    plan,
                    analysis: default,
                    "DIRECT-only 계획이므로 프록시 엔드포인트 분석 콜백을 호출하지 않았습니다.");
            case ProxyDirectiveRouteAnalysisPlanStatus.Blocked:
                return CreateResult<TAnalysis>(
                    ProxyDirectiveRouteAnalysisExecutionStatus.Blocked,
                    plan,
                    analysis: default,
                    "프록시 출처 또는 실행 계획이 유효하지 않아 분석 콜백을 호출하지 않았습니다.");
            case ProxyDirectiveRouteAnalysisPlanStatus.Unavailable:
                return CreateResult<TAnalysis>(
                    ProxyDirectiveRouteAnalysisExecutionStatus.Unavailable,
                    plan,
                    analysis: default,
                    "사용할 수 있는 프록시 지시문이 없어 분석 콜백을 호출하지 않았습니다.");
            case ProxyDirectiveRouteAnalysisPlanStatus
                .AnalyzeProxyEndpoints:
                break;
            default:
                return CreateResult<TAnalysis>(
                    ProxyDirectiveRouteAnalysisExecutionStatus.Blocked,
                    plan,
                    analysis: default,
                    "알 수 없는 실행 계획 상태이므로 분석 콜백을 호출하지 않았습니다.");
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return CreateResult<TAnalysis>(
                ProxyDirectiveRouteAnalysisExecutionStatus.Canceled,
                plan,
                analysis: default,
                "사용자 취소가 이미 요청돼 프록시 엔드포인트 분석 콜백을 호출하지 않았습니다.");
        }

        string? directiveText = plan.DirectiveText;
        if (string.IsNullOrWhiteSpace(directiveText))
        {
            return CreateResult<TAnalysis>(
                ProxyDirectiveRouteAnalysisExecutionStatus.Blocked,
                plan,
                analysis: default,
                "분석 계획의 메모리 전용 지시문이 없어 분석 콜백을 호출하지 않았습니다.");
        }

        try
        {
            TAnalysis analysis = await analyzer(
                    directiveText,
                    cancellationToken)
                .ConfigureAwait(false);

            // A cooperative/native adapter can return normally after observing
            // cancellation. Do not publish that late value as a completed run.
            // Await the actual callback completion; do not abandon native work.
            cancellationToken.ThrowIfCancellationRequested();
            if (analysis is null)
            {
                return CreateResult<TAnalysis>(
                    ProxyDirectiveRouteAnalysisExecutionStatus.Failed,
                    plan,
                    analysis: default,
                    "프록시 엔드포인트 분석 콜백이 결과를 반환하지 않았습니다. 원문 지시문은 결과에 포함하지 않았습니다.");
            }

            return CreateResult(
                ProxyDirectiveRouteAnalysisExecutionStatus.Completed,
                plan,
                analysis,
                "선택된 프록시 지시문으로 사용자 실행 분석 콜백을 정확히 한 번 완료했습니다.");
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            return CreateResult<TAnalysis>(
                ProxyDirectiveRouteAnalysisExecutionStatus.Canceled,
                plan,
                analysis: default,
                "사용자 요청으로 프록시 엔드포인트 분석을 취소했습니다. 이후 후보 분석은 호출자가 중단해야 합니다.");
        }
        catch (OperationCanceledException)
        {
            return CreateResult<TAnalysis>(
                ProxyDirectiveRouteAnalysisExecutionStatus.Canceled,
                plan,
                analysis: default,
                "분석 콜백이 취소 상태로 종료됐습니다. 예외 원문과 프록시 지시문은 결과에 포함하지 않았습니다.");
        }
        catch (Exception)
        {
            return CreateResult<TAnalysis>(
                ProxyDirectiveRouteAnalysisExecutionStatus.Failed,
                plan,
                analysis: default,
                "프록시 엔드포인트 분석 콜백에서 오류가 발생했습니다. 예외 원문과 프록시 지시문은 결과에 포함하지 않았습니다.");
        }
    }

    private static ProxyDirectiveRouteAnalysisExecutionResult<TAnalysis>
        CreateResult<TAnalysis>(
            ProxyDirectiveRouteAnalysisExecutionStatus status,
            ProxyDirectiveRouteAnalysisPlan plan,
            TAnalysis? analysis,
            string message) =>
        new(status, plan, analysis, message);
}
