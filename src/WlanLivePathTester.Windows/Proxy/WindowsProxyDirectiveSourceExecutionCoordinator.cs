using System.Runtime.Versioning;
using System.Text.Json.Serialization;
using WlanLivePathTester.Core.Proxy;

namespace WlanLivePathTester.Windows.Proxy;

public enum WindowsProxyDirectiveSourceExecutionStatus
{
    Completed,
    DirectOnly,
    Blocked,
    Unavailable,
    Canceled,
    Failed
}

public sealed class WindowsProxyDirectiveSourceExecutionResult<TAnalysis>
{
    internal WindowsProxyDirectiveSourceExecutionResult(
        WindowsProxyDirectiveSourceExecutionStatus status,
        ProxyDirectiveDecisionAudit? audit,
        ProxyDirectiveSourceSnapshot? snapshot,
        ProxyDirectiveRouteAnalysisExecutionResult<TAnalysis>? execution,
        string message)
    {
        Status = status;
        Audit = audit;
        Snapshot = snapshot;
        Execution = execution;
        Message = message;
    }

    public WindowsProxyDirectiveSourceExecutionStatus Status { get; }

    public ProxyDirectiveDecisionAudit? Audit { get; }

    [JsonIgnore]
    public ProxyDirectiveSourceSnapshot? Snapshot { get; }

    [JsonIgnore]
    public ProxyDirectiveRouteAnalysisExecutionResult<TAnalysis>? Execution
    {
        get;
    }

    [JsonIgnore]
    public TAnalysis? Analysis => Execution is null
        ? default
        : Execution.Analysis;

    public bool HasCompletedAnalysis =>
        Status == WindowsProxyDirectiveSourceExecutionStatus.Completed
        && Analysis is not null;

    public string Message { get; }
}

[SupportedOSPlatform("windows")]
public sealed class WindowsProxyDirectiveSourceExecutionCoordinator
{
    private readonly WindowsProxyDirectiveSourceSnapshotReader _reader;

    public WindowsProxyDirectiveSourceExecutionCoordinator(
        WindowsProxyDirectiveSourceSnapshotReader reader)
    {
        _reader = reader
            ?? throw new ArgumentNullException(nameof(reader));
    }

    public async Task<
        WindowsProxyDirectiveSourceExecutionResult<TAnalysis>>
        ReadAndExecuteAsync<TAnalysis>(
            Uri targetUri,
            Func<string, CancellationToken, Task<TAnalysis>> analyzer,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(targetUri);
        ArgumentNullException.ThrowIfNull(analyzer);

        if (cancellationToken.IsCancellationRequested)
        {
            return CreateWithoutSnapshot<TAnalysis>(
                WindowsProxyDirectiveSourceExecutionStatus.Canceled,
                "사용자 취소가 이미 요청돼 Windows 프록시 설정과 대상별 판정을 읽지 않았습니다.");
        }

        ProxyDirectiveSourceSnapshot snapshot;
        try
        {
            snapshot = await _reader.ReadAsync(
                    targetUri,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return CreateWithoutSnapshot<TAnalysis>(
                WindowsProxyDirectiveSourceExecutionStatus.Canceled,
                "Windows 프록시 출처를 읽는 중 사용자 요청으로 취소했습니다. 원문 설정과 예외 메시지는 결과에 포함하지 않았습니다.");
        }
        catch (Exception)
        {
            return CreateWithoutSnapshot<TAnalysis>(
                WindowsProxyDirectiveSourceExecutionStatus.Failed,
                "Windows 프록시 출처를 읽는 중 오류가 발생했습니다. 원문 설정과 예외 메시지는 결과에 포함하지 않았습니다.");
        }

        ProxyDirectiveRouteAnalysisExecutionResult<TAnalysis> execution =
            await ProxyDirectiveSourceExecutionPipeline.ExecuteAsync(
                    snapshot,
                    analyzer,
                    cancellationToken)
                .ConfigureAwait(false);
        ProxyDirectiveDecisionAudit audit =
            ProxyDirectiveDecisionAuditFactory.Create(
                snapshot,
                execution.Status);
        WindowsProxyDirectiveSourceExecutionStatus status =
            MapExecutionStatus(execution.Status);
        string message = status switch
        {
            WindowsProxyDirectiveSourceExecutionStatus.Completed =>
                "Windows 프록시 출처 선택과 승인된 로컬 경로 분석을 완료했습니다.",
            WindowsProxyDirectiveSourceExecutionStatus.DirectOnly =>
                "대상별 또는 수동 판정이 DIRECT-only이므로 프록시 엔드포인트 분석 콜백을 호출하지 않았습니다.",
            WindowsProxyDirectiveSourceExecutionStatus.Blocked =>
                "Windows 프록시 출처 판정이 유효하지 않아 프록시 엔드포인트 분석을 차단했습니다.",
            WindowsProxyDirectiveSourceExecutionStatus.Unavailable =>
                "사용할 수 있는 대상별 또는 수동 프록시 지시문이 없어 분석을 시작하지 않았습니다.",
            WindowsProxyDirectiveSourceExecutionStatus.Canceled =>
                "사용자 요청으로 프록시 출처 선택 또는 엔드포인트 분석을 완료하지 않았습니다.",
            _ =>
                "프록시 엔드포인트 분석을 완료하지 못했습니다. 원문 설정·지시문·예외 메시지는 결과에 포함하지 않았습니다."
        };

        return new WindowsProxyDirectiveSourceExecutionResult<TAnalysis>(
            status,
            audit,
            snapshot,
            execution,
            message);
    }

    private static WindowsProxyDirectiveSourceExecutionStatus
        MapExecutionStatus(
            ProxyDirectiveRouteAnalysisExecutionStatus status) =>
        status switch
        {
            ProxyDirectiveRouteAnalysisExecutionStatus.Completed =>
                WindowsProxyDirectiveSourceExecutionStatus.Completed,
            ProxyDirectiveRouteAnalysisExecutionStatus.DirectOnly =>
                WindowsProxyDirectiveSourceExecutionStatus.DirectOnly,
            ProxyDirectiveRouteAnalysisExecutionStatus.Blocked =>
                WindowsProxyDirectiveSourceExecutionStatus.Blocked,
            ProxyDirectiveRouteAnalysisExecutionStatus.Unavailable =>
                WindowsProxyDirectiveSourceExecutionStatus.Unavailable,
            ProxyDirectiveRouteAnalysisExecutionStatus.Canceled =>
                WindowsProxyDirectiveSourceExecutionStatus.Canceled,
            ProxyDirectiveRouteAnalysisExecutionStatus.Failed =>
                WindowsProxyDirectiveSourceExecutionStatus.Failed,
            _ => WindowsProxyDirectiveSourceExecutionStatus.Failed
        };

    private static WindowsProxyDirectiveSourceExecutionResult<TAnalysis>
        CreateWithoutSnapshot<TAnalysis>(
            WindowsProxyDirectiveSourceExecutionStatus status,
            string message) =>
        new(
            status,
            audit: null,
            snapshot: null,
            execution: null,
            message);
}
