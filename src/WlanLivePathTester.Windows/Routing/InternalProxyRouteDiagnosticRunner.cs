using System.Runtime.Versioning;
using WlanLivePathTester.Core.Proxy;
using WlanLivePathTester.Core.Routing;

namespace WlanLivePathTester.Windows.Routing;

public interface IInternalDirectRouteEvidenceReader
{
    Task<DestinationRouteEvidence> ReadAsync(
        string target,
        string safeLabel,
        int dnsTimeoutSeconds,
        CancellationToken cancellationToken);
}

public interface IProxyDirectiveRouteBridgeExecutor
{
    Task<
        ProxyDirectiveRouteAnalysisExecutionResult<
            ProxyEndpointRouteAnalysisResult>> ExecuteAsync(
        ProxyDirectiveSourceSelectionResult selection,
        Uri targetUri,
        string? expectedWlanInterfaceId,
        int dnsTimeoutSeconds,
        CancellationToken cancellationToken);
}

[SupportedOSPlatform("windows")]
internal sealed class WindowsInternalDirectRouteEvidenceReader
    : IInternalDirectRouteEvidenceReader
{
    public Task<DestinationRouteEvidence> ReadAsync(
        string target,
        string safeLabel,
        int dnsTimeoutSeconds,
        CancellationToken cancellationToken) =>
        LocalRouteEvidenceReader.ReadAsync(
            target,
            safeLabel,
            RouteProbePurpose.InternalDirectTarget,
            dnsTimeoutSeconds,
            cancellationToken);
}

[SupportedOSPlatform("windows")]
internal sealed class WindowsProxyDirectiveRouteBridgeExecutor
    : IProxyDirectiveRouteBridgeExecutor
{
    private readonly ProxyDirectiveRouteBridge _bridge = new();

    public Task<
        ProxyDirectiveRouteAnalysisExecutionResult<
            ProxyEndpointRouteAnalysisResult>> ExecuteAsync(
        ProxyDirectiveSourceSelectionResult selection,
        Uri targetUri,
        string? expectedWlanInterfaceId,
        int dnsTimeoutSeconds,
        CancellationToken cancellationToken) =>
        _bridge.ExecuteAsync(
            selection,
            targetUri,
            expectedWlanInterfaceId,
            dnsTimeoutSeconds,
            cancellationToken);
}

[SupportedOSPlatform("windows")]
public sealed class InternalProxyRouteDiagnosticRunner
{
    private const string InternalTargetSafeLabel =
        "내부 DIRECT 기준 대상";

    private readonly IInternalDirectRouteEvidenceReader
        _internalRouteReader;
    private readonly IProxyDirectiveRouteBridgeExecutor
        _proxyRouteBridge;

    public InternalProxyRouteDiagnosticRunner()
        : this(
            new WindowsInternalDirectRouteEvidenceReader(),
            new WindowsProxyDirectiveRouteBridgeExecutor())
    {
    }

    public InternalProxyRouteDiagnosticRunner(
        IInternalDirectRouteEvidenceReader internalRouteReader,
        IProxyDirectiveRouteBridgeExecutor proxyRouteBridge)
    {
        _internalRouteReader = internalRouteReader
            ?? throw new ArgumentNullException(
                nameof(internalRouteReader));
        _proxyRouteBridge = proxyRouteBridge
            ?? throw new ArgumentNullException(
                nameof(proxyRouteBridge));
    }

    public async Task<InternalProxyRouteDiagnosticRunResult> RunAsync(
        string internalDirectTarget,
        Uri externalTargetUri,
        ProxyDirectiveSourceSnapshot sourceSnapshot,
        string? expectedWlanInterfaceId,
        int dnsTimeoutSeconds = 5,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            internalDirectTarget);
        ArgumentNullException.ThrowIfNull(externalTargetUri);
        ArgumentNullException.ThrowIfNull(sourceSnapshot);
        if (dnsTimeoutSeconds is < 1 or > 30)
        {
            throw new ArgumentOutOfRangeException(
                nameof(dnsTimeoutSeconds),
                "DNS 제한 시간은 1~30초 범위여야 합니다.");
        }

        ProxyDirectiveSourceSelectionResult selection =
            ProxyDirectiveSourceSnapshotSelectionPolicy.Select(
                sourceSnapshot);
        ProxyDirectiveRouteAnalysisPlan plan =
            ProxyDirectiveRouteAnalysisPlanPolicy.Create(selection);

        InternalProxyRouteDiagnosticRunResult? terminal =
            CreateNonProxyTerminalResult(selection, plan);
        if (terminal is not null)
        {
            return terminal;
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return CreateCanceled(
                selection,
                plan,
                internalStatus: "NotStarted",
                proxyStatus: "NotStarted",
                internalRoute: null,
                proxyAnalysis: null,
                "사용자 취소가 이미 요청돼 내부 DNS와 프록시 경로 분석을 시작하지 않았습니다.");
        }

        DestinationRouteEvidence internalRoute;
        try
        {
            internalRoute = await _internalRouteReader.ReadAsync(
                    internalDirectTarget,
                    InternalTargetSafeLabel,
                    dnsTimeoutSeconds,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return CreateCanceled(
                selection,
                plan,
                internalStatus: "Canceled",
                proxyStatus: "NotStarted",
                internalRoute: null,
                proxyAnalysis: null,
                "사용자 요청으로 내부 DIRECT 경로 확인을 취소했습니다. 프록시 후보는 조회하지 않았습니다.");
        }
        catch (Exception)
        {
            return CreateFailed(
                selection,
                plan,
                internalStatus: "Failed",
                proxyStatus: "NotStarted",
                internalRoute: null,
                proxyAnalysis: null,
                "내부 DIRECT 경로 확인 중 오류가 발생했습니다. 대상 원문과 예외 메시지는 결과에 포함하지 않았고 프록시 후보도 조회하지 않았습니다.");
        }

        if (internalRoute.Status
            == DestinationRouteEvidenceStatus.Canceled)
        {
            return CreateCanceled(
                selection,
                plan,
                internalRoute.Status.ToString(),
                proxyStatus: "NotStarted",
                internalRoute,
                proxyAnalysis: null,
                "내부 DIRECT 경로 확인이 취소 상태로 끝나 프록시 후보를 조회하지 않았습니다.");
        }

        ProxyDirectiveRouteAnalysisExecutionResult<
            ProxyEndpointRouteAnalysisResult> execution;
        try
        {
            execution = await _proxyRouteBridge.ExecuteAsync(
                    selection,
                    externalTargetUri,
                    expectedWlanInterfaceId,
                    dnsTimeoutSeconds,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return CreateCanceled(
                selection,
                plan,
                internalRoute.Status.ToString(),
                proxyStatus: "Canceled",
                internalRoute,
                proxyAnalysis: null,
                "사용자 요청으로 프록시 엔드포인트 경로 분석을 취소했습니다. 내부 경로 근거만 메모리에 유지합니다.");
        }
        catch (Exception)
        {
            return CreateFailed(
                selection,
                plan,
                internalRoute.Status.ToString(),
                proxyStatus: "Failed",
                internalRoute,
                proxyAnalysis: null,
                "프록시 엔드포인트 경로 분석 중 오류가 발생했습니다. 지시문·대상·예외 원문은 결과에 포함하지 않았습니다.");
        }

        if (execution.Status
            == ProxyDirectiveRouteAnalysisExecutionStatus.Canceled)
        {
            return CreateCanceled(
                selection,
                plan,
                internalRoute.Status.ToString(),
                proxyStatus: "Canceled",
                internalRoute,
                proxyAnalysis: null,
                "프록시 엔드포인트 경로 분석이 취소됐습니다. 내부 경로와 완료 전 상태만 유지합니다.");
        }

        if (execution.Status
                != ProxyDirectiveRouteAnalysisExecutionStatus.Completed
            || execution.Analysis is null)
        {
            return CreateFailed(
                selection,
                plan,
                internalRoute.Status.ToString(),
                execution.Status.ToString(),
                internalRoute,
                proxyAnalysis: null,
                "승인된 프록시 실행 계획이 완료된 경로 분석 결과를 반환하지 않았습니다. 내부 경로만 유지합니다.");
        }

        ProxyEndpointRouteAnalysisResult proxyAnalysis =
            execution.Analysis;
        if (proxyAnalysis.Status
            == ProxyEndpointRouteAnalysisStatus.Canceled)
        {
            return CreateCanceled(
                selection,
                plan,
                internalRoute.Status.ToString(),
                proxyAnalysis.Status.ToString(),
                internalRoute,
                proxyAnalysis,
                "프록시 후보 경로 분석이 취소 상태로 끝나 내부·프록시 비교를 수행하지 않았습니다.");
        }

        InternalProxyRouteComparisonResult comparison;
        try
        {
            comparison = InternalProxyRouteComparison.Compare(
                internalRoute,
                proxyAnalysis,
                expectedWlanInterfaceId);
        }
        catch (Exception)
        {
            return CreateFailed(
                selection,
                plan,
                internalRoute.Status.ToString(),
                proxyAnalysis.Status.ToString(),
                internalRoute,
                proxyAnalysis,
                "내부 DIRECT·프록시 로컬 인터페이스 비교 중 오류가 발생했습니다. 입력과 예외 원문은 결과에 포함하지 않았습니다.");
        }

        return new InternalProxyRouteDiagnosticRunResult(
            Status: InternalProxyRouteDiagnosticRunStatus.Completed,
            SelectionStatus: selection.Status,
            SourceKind: selection.SourceKind,
            PlanCode: plan.Code,
            InternalRouteStatus: internalRoute.Status.ToString(),
            ProxyRouteStatus: proxyAnalysis.Status.ToString(),
            ComparisonStatus: comparison.Status.ToString(),
            SameLocalInterface: comparison.SameLocalInterface,
            ProxyEndpointCount: proxyAnalysis.AnalyzedEndpointCount,
            SuccessfulProxyRouteCount:
                proxyAnalysis.SuccessfulEndpointCount,
            DirectDirectiveCount: execution.DirectDirectiveCount,
            ProxyAnalysisWasTruncated: false,
            Message:
                $"내부 DIRECT와 프록시 로컬 경로 진단을 완료했습니다. 비교 상태는 {comparison.Status}입니다.",
            InternalRouteEvidence: internalRoute,
            ProxyRouteAnalysis: proxyAnalysis,
            Comparison: comparison);
    }

    private static InternalProxyRouteDiagnosticRunResult?
        CreateNonProxyTerminalResult(
            ProxyDirectiveSourceSelectionResult selection,
            ProxyDirectiveRouteAnalysisPlan plan) =>
        plan.Status switch
        {
            ProxyDirectiveRouteAnalysisPlanStatus.DirectOnly =>
                new InternalProxyRouteDiagnosticRunResult(
                    InternalProxyRouteDiagnosticRunStatus.DirectOnly,
                    selection.Status,
                    selection.SourceKind,
                    plan.Code,
                    internalRouteStatus: "NotStarted",
                    proxyRouteStatus: "DirectOnly",
                    comparisonStatus: "NotPerformed",
                    sameLocalInterface: null,
                    proxyEndpointCount: 0,
                    successfulProxyRouteCount: 0,
                    directDirectiveCount:
                        plan.DirectDirectiveCount,
                    proxyAnalysisWasTruncated: false,
                    "현재 대상은 DIRECT-only이므로 비교할 프록시 엔드포인트가 없습니다. 내부 DNS와 프록시 경로 조회를 시작하지 않았습니다."),
            ProxyDirectiveRouteAnalysisPlanStatus.Blocked =>
                new InternalProxyRouteDiagnosticRunResult(
                    InternalProxyRouteDiagnosticRunStatus.Blocked,
                    selection.Status,
                    selection.SourceKind,
                    plan.Code,
                    internalRouteStatus: "NotStarted",
                    proxyRouteStatus: "Blocked",
                    comparisonStatus: "NotPerformed",
                    sameLocalInterface: null,
                    proxyEndpointCount: plan.ProxyEndpointCount,
                    successfulProxyRouteCount: 0,
                    directDirectiveCount:
                        plan.DirectDirectiveCount,
                    proxyAnalysisWasTruncated: false,
                    "프록시 출처 판정 또는 실행 계획이 유효하지 않아 내부 DNS와 프록시 경로 조회를 차단했습니다."),
            ProxyDirectiveRouteAnalysisPlanStatus.Unavailable =>
                new InternalProxyRouteDiagnosticRunResult(
                    InternalProxyRouteDiagnosticRunStatus.Unavailable,
                    selection.Status,
                    selection.SourceKind,
                    plan.Code,
                    internalRouteStatus: "NotStarted",
                    proxyRouteStatus: "Unavailable",
                    comparisonStatus: "NotPerformed",
                    sameLocalInterface: null,
                    proxyEndpointCount: 0,
                    successfulProxyRouteCount: 0,
                    directDirectiveCount: 0,
                    proxyAnalysisWasTruncated: false,
                    "사용할 수 있는 대상별 또는 수동 프록시 출처가 없어 내부 DNS와 프록시 경로 조회를 시작하지 않았습니다. DIRECT로 추정하지 않습니다."),
            ProxyDirectiveRouteAnalysisPlanStatus
                .AnalyzeProxyEndpoints => null,
            _ =>
                new InternalProxyRouteDiagnosticRunResult(
                    InternalProxyRouteDiagnosticRunStatus.Blocked,
                    selection.Status,
                    selection.SourceKind,
                    ProxyDirectiveRouteAnalysisPlanCode
                        .InconsistentSelectionResult,
                    internalRouteStatus: "NotStarted",
                    proxyRouteStatus: "Blocked",
                    comparisonStatus: "NotPerformed",
                    sameLocalInterface: null,
                    proxyEndpointCount: 0,
                    successfulProxyRouteCount: 0,
                    directDirectiveCount: 0,
                    proxyAnalysisWasTruncated: false,
                    "알 수 없는 실행 계획 상태여서 모든 DNS·경로 조회를 차단했습니다.")
        };

    private static InternalProxyRouteDiagnosticRunResult CreateCanceled(
        ProxyDirectiveSourceSelectionResult selection,
        ProxyDirectiveRouteAnalysisPlan plan,
        string internalStatus,
        string proxyStatus,
        DestinationRouteEvidence? internalRoute,
        ProxyEndpointRouteAnalysisResult? proxyAnalysis,
        string message) =>
        new(
            InternalProxyRouteDiagnosticRunStatus.Canceled,
            selection.Status,
            selection.SourceKind,
            plan.Code,
            internalStatus,
            proxyStatus,
            comparisonStatus: "NotPerformed",
            sameLocalInterface: null,
            proxyEndpointCount:
                proxyAnalysis?.AnalyzedEndpointCount ?? 0,
            successfulProxyRouteCount:
                proxyAnalysis?.SuccessfulEndpointCount ?? 0,
            directDirectiveCount:
                plan.DirectDirectiveCount,
            proxyAnalysisWasTruncated: false,
            message,
            internalRoute,
            proxyAnalysis,
            comparison: null);

    private static InternalProxyRouteDiagnosticRunResult CreateFailed(
        ProxyDirectiveSourceSelectionResult selection,
        ProxyDirectiveRouteAnalysisPlan plan,
        string internalStatus,
        string proxyStatus,
        DestinationRouteEvidence? internalRoute,
        ProxyEndpointRouteAnalysisResult? proxyAnalysis,
        string message) =>
        new(
            InternalProxyRouteDiagnosticRunStatus.Failed,
            selection.Status,
            selection.SourceKind,
            plan.Code,
            internalStatus,
            proxyStatus,
            comparisonStatus: "NotPerformed",
            sameLocalInterface: null,
            proxyEndpointCount:
                proxyAnalysis?.AnalyzedEndpointCount ?? 0,
            successfulProxyRouteCount:
                proxyAnalysis?.SuccessfulEndpointCount ?? 0,
            directDirectiveCount:
                plan.DirectDirectiveCount,
            proxyAnalysisWasTruncated: false,
            message,
            internalRoute,
            proxyAnalysis,
            comparison: null);
}
