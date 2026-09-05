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
public sealed class InternalProxyRouteComparisonCoordinator
{
    public const int MaximumInternalTargetLength = 2048;

    private const string ResultLimitation =
        "이 실행은 현재 PC에서 내부 DIRECT 대상과 적용 프록시 엔드포인트까지 선택되는 Windows 로컬 인터페이스만 비교합니다. HTTP 연결, 프록시 인증, 프록시 서버 내부 상태, 프록시 이후 인터넷 경로와 실제 서비스 품질은 확인하지 않습니다.";

    private readonly IInternalDirectRouteEvidenceReader
        _internalRouteReader;
    private readonly ProxyDirectiveRouteAnalysisCoordinator
        _proxyCoordinator;
    private readonly TimeProvider _timeProvider;

    public InternalProxyRouteComparisonCoordinator()
        : this(
            new WindowsInternalDirectRouteEvidenceReader(),
            new ProxyDirectiveRouteAnalysisCoordinator(),
            TimeProvider.System)
    {
    }

    public InternalProxyRouteComparisonCoordinator(
        IInternalDirectRouteEvidenceReader internalRouteReader,
        ProxyDirectiveRouteAnalysisCoordinator proxyCoordinator,
        TimeProvider? timeProvider = null)
    {
        _internalRouteReader = internalRouteReader
            ?? throw new ArgumentNullException(
                nameof(internalRouteReader));
        _proxyCoordinator = proxyCoordinator
            ?? throw new ArgumentNullException(nameof(proxyCoordinator));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public Task<InternalProxyRouteComparisonRunResult>
        RunManualDirectiveAsync(
            string? internalTarget,
            string? proxyDirectiveText,
            Uri? externalTarget,
            string? expectedWlanInterfaceId,
            int dnsTimeoutSeconds = 5,
            CancellationToken cancellationToken = default)
    {
        ProxyDirectiveSourceSelectionResult selection =
            ProxyDirectiveSourceSelectionPolicy.Select(
                targetDecisionWasEvaluated: false,
                targetDecisionIsDirect: false,
                targetSpecificDirective: null,
                manualProxyConfigured: true,
                manualProxyDirective: proxyDirectiveText);
        return RunAsync(
            internalTarget,
            selection,
            externalTarget,
            expectedWlanInterfaceId,
            dnsTimeoutSeconds,
            cancellationToken);
    }

    public async Task<InternalProxyRouteComparisonRunResult> RunAsync(
        string? internalTarget,
        ProxyDirectiveSourceSelectionResult selection,
        Uri? externalTarget,
        string? expectedWlanInterfaceId,
        int dnsTimeoutSeconds = 5,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(selection);
        if (dnsTimeoutSeconds is < 1 or > 30)
        {
            throw new ArgumentOutOfRangeException(
                nameof(dnsTimeoutSeconds),
                "DNS 제한 시간은 1~30초 범위여야 합니다.");
        }

        string normalizedInternalTarget =
            (internalTarget ?? string.Empty).Trim();
        bool expectedWlanIdentityAvailable =
            HasValidInterfaceGuid(expectedWlanInterfaceId);
        ProxyDirectiveRouteAnalysisPlan plan =
            ProxyDirectiveRouteAnalysisPlanPolicy.Create(selection);

        string? inputError = ValidateInput(
            normalizedInternalTarget,
            externalTarget);
        if (inputError is not null)
        {
            return CreateResult(
                InternalProxyRouteComparisonRunStatus.InvalidInput,
                plan,
                parsed: null,
                execution: null,
                comparison: null,
                internalRoute: null,
                expectedWlanIdentityAvailable,
                internalRouteReadPerformed: false,
                proxyRouteAnalysisPerformed: false,
                inputError,
                externalTarget);
        }

        switch (plan.Status)
        {
            case ProxyDirectiveRouteAnalysisPlanStatus.Blocked:
                return CreateResult(
                    InternalProxyRouteComparisonRunStatus
                        .ProxySourceBlocked,
                    plan,
                    parsed: null,
                    execution: null,
                    comparison: null,
                    internalRoute: null,
                    expectedWlanIdentityAvailable,
                    internalRouteReadPerformed: false,
                    proxyRouteAnalysisPerformed: false,
                    "프록시 출처 선택 또는 실행 계획이 유효하지 않아 내부·프록시 DNS와 라우팅 조회를 시작하지 않았습니다.",
                    externalTarget);
            case ProxyDirectiveRouteAnalysisPlanStatus.Unavailable:
                return CreateResult(
                    InternalProxyRouteComparisonRunStatus
                        .ProxySourceUnavailable,
                    plan,
                    parsed: null,
                    execution: null,
                    comparison: null,
                    internalRoute: null,
                    expectedWlanIdentityAvailable,
                    internalRouteReadPerformed: false,
                    proxyRouteAnalysisPerformed: false,
                    "사용할 수 있는 대상별 또는 수동 프록시 지시문이 없어 내부·프록시 DNS와 라우팅 조회를 시작하지 않았습니다.",
                    externalTarget);
            case ProxyDirectiveRouteAnalysisPlanStatus.DirectOnly:
                return CreateResult(
                    InternalProxyRouteComparisonRunStatus
                        .DirectPathSelected,
                    plan,
                    parsed: null,
                    execution: null,
                    comparison: null,
                    internalRoute: null,
                    expectedWlanIdentityAvailable,
                    internalRouteReadPerformed: false,
                    proxyRouteAnalysisPerformed: false,
                    "선택된 프록시 출처가 DIRECT-only이므로 비교할 프록시 엔드포인트가 없습니다. 내부·프록시 DNS와 라우팅 조회를 모두 생략했습니다.",
                    externalTarget,
                    forceDirectPrimary: true);
            case ProxyDirectiveRouteAnalysisPlanStatus
                .AnalyzeProxyEndpoints:
                break;
            default:
                return CreateResult(
                    InternalProxyRouteComparisonRunStatus
                        .ProxySourceBlocked,
                    plan,
                    parsed: null,
                    execution: null,
                    comparison: null,
                    internalRoute: null,
                    expectedWlanIdentityAvailable,
                    internalRouteReadPerformed: false,
                    proxyRouteAnalysisPerformed: false,
                    "정의되지 않은 프록시 실행 계획 상태이므로 DNS와 라우팅 조회를 시작하지 않았습니다.",
                    externalTarget);
        }

        string? directiveText = plan.DirectiveText;
        if (string.IsNullOrWhiteSpace(directiveText))
        {
            return CreateResult(
                InternalProxyRouteComparisonRunStatus
                    .ProxySourceBlocked,
                plan,
                parsed: null,
                execution: null,
                comparison: null,
                internalRoute: null,
                expectedWlanIdentityAvailable,
                internalRouteReadPerformed: false,
                proxyRouteAnalysisPerformed: false,
                "분석 가능한 프록시 계획에 메모리 전용 지시문이 없어 DNS와 라우팅 조회를 차단했습니다.",
                externalTarget);
        }

        ProxyEndpointParseResult parsed =
            ProxyEndpointParser.Parse(
                directiveText,
                externalTarget);
        if (!parsed.IsUsable
            || parsed.Errors.Count > 0
            || parsed.Decision == ProxyEndpointDecision.Unknown)
        {
            return CreateResult(
                InternalProxyRouteComparisonRunStatus.InvalidInput,
                plan,
                parsed,
                execution: null,
                comparison: null,
                internalRoute: null,
                expectedWlanIdentityAvailable,
                internalRouteReadPerformed: false,
                proxyRouteAnalysisPerformed: false,
                "프록시 지시문에서 현재 외부 대상에 적용되는 안전한 경로를 결정하지 못해 DNS와 라우팅 조회를 시작하지 않았습니다.",
                externalTarget);
        }

        if (parsed.Decision is ProxyEndpointDecision.Direct
            or ProxyEndpointDecision.DirectWithProxyAlternatives)
        {
            return CreateResult(
                InternalProxyRouteComparisonRunStatus
                    .DirectPathSelected,
                plan,
                parsed,
                execution: null,
                comparison: null,
                internalRoute: null,
                expectedWlanIdentityAvailable,
                internalRouteReadPerformed: false,
                proxyRouteAnalysisPerformed: false,
                "DIRECT가 첫 적용 경로이므로 비교할 프록시 엔드포인트가 없습니다. 내부·프록시 DNS와 라우팅 조회를 모두 생략했습니다.",
                externalTarget,
                forceDirectPrimary: true);
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return CreateCanceled(
                plan,
                parsed,
                execution: null,
                internalRoute: null,
                comparison: null,
                expectedWlanIdentityAvailable,
                internalRouteReadPerformed: false,
                proxyRouteAnalysisPerformed: false,
                externalTarget);
        }

        DestinationRouteEvidence internalRoute;
        try
        {
            internalRoute = await _internalRouteReader.ReadAsync(
                    normalizedInternalTarget,
                    "내부 DIRECT 기준 대상",
                    dnsTimeoutSeconds,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return CreateCanceled(
                plan,
                parsed,
                execution: null,
                internalRoute: null,
                comparison: null,
                expectedWlanIdentityAvailable,
                internalRouteReadPerformed: true,
                proxyRouteAnalysisPerformed: false,
                externalTarget);
        }
        catch (Exception)
        {
            return CreateResult(
                InternalProxyRouteComparisonRunStatus.Failed,
                plan,
                parsed,
                execution: null,
                comparison: null,
                internalRoute: null,
                expectedWlanIdentityAvailable,
                internalRouteReadPerformed: true,
                proxyRouteAnalysisPerformed: false,
                "내부 DIRECT 대상의 로컬 경로 확인 중 오류가 발생했습니다. 입력 원문과 예외 메시지는 결과에 포함하지 않았습니다.",
                externalTarget);
        }

        if (internalRoute.Status
            == DestinationRouteEvidenceStatus.Canceled)
        {
            return CreateCanceled(
                plan,
                parsed,
                execution: null,
                internalRoute,
                comparison: null,
                expectedWlanIdentityAvailable,
                internalRouteReadPerformed: true,
                proxyRouteAnalysisPerformed: false,
                externalTarget);
        }

        if (!CanContinueWithInternalRoute(internalRoute))
        {
            InternalProxyRouteComparisonResult? terminalComparison =
                internalRoute.Status
                    == DestinationRouteEvidenceStatus.MultipleInterfaces
                    ? InternalProxyRouteComparisonEvaluator.Evaluate(
                        internalRoute,
                        proxyExecution: null,
                        _timeProvider.GetUtcNow())
                    : null;
            return CreateResult(
                InternalProxyRouteComparisonRunStatus
                    .InternalRouteUnavailable,
                plan,
                parsed,
                execution: null,
                terminalComparison,
                internalRoute,
                expectedWlanIdentityAvailable,
                internalRouteReadPerformed: true,
                proxyRouteAnalysisPerformed: false,
                "내부 DIRECT 대상의 정확하고 단일한 로컬 인터페이스 근거를 확인하지 못해 프록시 후보 DNS와 라우팅 조회를 시작하지 않았습니다.",
                externalTarget);
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return CreateCanceled(
                plan,
                parsed,
                execution: null,
                internalRoute,
                comparison: null,
                expectedWlanIdentityAvailable,
                internalRouteReadPerformed: true,
                proxyRouteAnalysisPerformed: false,
                externalTarget);
        }

        ProxyDirectiveRouteAnalysisExecutionResult<
            ProxyEndpointRouteAnalysisResult> execution;
        try
        {
            execution = await _proxyCoordinator.ExecuteAsync(
                    selection,
                    externalTarget!,
                    expectedWlanInterfaceId,
                    dnsTimeoutSeconds,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return CreateCanceled(
                plan,
                parsed,
                execution: null,
                internalRoute,
                comparison: null,
                expectedWlanIdentityAvailable,
                internalRouteReadPerformed: true,
                proxyRouteAnalysisPerformed: true,
                externalTarget);
        }
        catch (Exception)
        {
            return CreateResult(
                InternalProxyRouteComparisonRunStatus.Failed,
                plan,
                parsed,
                execution: null,
                comparison: null,
                internalRoute,
                expectedWlanIdentityAvailable,
                internalRouteReadPerformed: true,
                proxyRouteAnalysisPerformed: true,
                "프록시 엔드포인트의 로컬 경로 확인 중 오류가 발생했습니다. 프록시 원문과 예외 메시지는 결과에 포함하지 않았습니다.",
                externalTarget);
        }

        InternalProxyRouteComparisonResult comparison =
            InternalProxyRouteComparisonEvaluator.Evaluate(
                internalRoute,
                execution,
                _timeProvider.GetUtcNow());
        InternalProxyRouteComparisonRunStatus runStatus =
            execution.Status switch
            {
                ProxyDirectiveRouteAnalysisExecutionStatus.Completed =>
                    InternalProxyRouteComparisonRunStatus.Completed,
                ProxyDirectiveRouteAnalysisExecutionStatus.Canceled =>
                    InternalProxyRouteComparisonRunStatus.Canceled,
                ProxyDirectiveRouteAnalysisExecutionStatus.Blocked =>
                    InternalProxyRouteComparisonRunStatus
                        .ProxySourceBlocked,
                ProxyDirectiveRouteAnalysisExecutionStatus.Unavailable =>
                    InternalProxyRouteComparisonRunStatus
                        .ProxySourceUnavailable,
                ProxyDirectiveRouteAnalysisExecutionStatus.DirectOnly =>
                    InternalProxyRouteComparisonRunStatus
                        .DirectPathSelected,
                _ => InternalProxyRouteComparisonRunStatus.Failed
            };
        string message = runStatus switch
        {
            InternalProxyRouteComparisonRunStatus.Completed =>
                "내부 DIRECT 대상과 적용 프록시 엔드포인트의 Windows 로컬 인터페이스 비교를 완료했습니다.",
            InternalProxyRouteComparisonRunStatus.Canceled =>
                "사용자 요청으로 프록시 경로 분석을 중단했습니다. 완료되지 않은 후보를 전체 비교 근거로 사용하지 않았습니다.",
            InternalProxyRouteComparisonRunStatus.ProxySourceBlocked =>
                "프록시 출처 또는 실행 계획이 분석 단계에서 차단돼 비교를 완료하지 않았습니다.",
            InternalProxyRouteComparisonRunStatus.ProxySourceUnavailable =>
                "사용 가능한 프록시 출처가 없어 비교를 완료하지 않았습니다.",
            InternalProxyRouteComparisonRunStatus.DirectPathSelected =>
                "분석 단계에서 DIRECT-only 결과가 확인돼 비교할 프록시 엔드포인트가 없습니다.",
            _ =>
                "프록시 엔드포인트 경로 분석을 완료하지 못했습니다. 원문 입력과 예외 메시지는 결과에 포함하지 않았습니다."
        };

        return CreateResult(
            runStatus,
            plan,
            parsed,
            execution,
            comparison,
            internalRoute,
            expectedWlanIdentityAvailable,
            internalRouteReadPerformed: true,
            proxyRouteAnalysisPerformed: true,
            message,
            externalTarget);
    }

    private static string? ValidateInput(
        string internalTarget,
        Uri? externalTarget)
    {
        if (internalTarget.Length == 0)
        {
            return "내부 DIRECT 기준 대상이 비어 있습니다.";
        }

        if (internalTarget.Length > MaximumInternalTargetLength
            || internalTarget.Any(char.IsControl))
        {
            return $"내부 DIRECT 기준 대상은 제어 문자 없이 {MaximumInternalTargetLength}자 이하여야 합니다.";
        }

        if (externalTarget is null
            || !externalTarget.IsAbsoluteUri
            || (!externalTarget.Scheme.Equals(
                    Uri.UriSchemeHttp,
                    StringComparison.OrdinalIgnoreCase)
                && !externalTarget.Scheme.Equals(
                    Uri.UriSchemeHttps,
                    StringComparison.OrdinalIgnoreCase)))
        {
            return "외부 대상은 절대 HTTP 또는 HTTPS URL이어야 합니다.";
        }

        return null;
    }

    private static bool CanContinueWithInternalRoute(
        DestinationRouteEvidence evidence) =>
        evidence.Status == DestinationRouteEvidenceStatus.Success
        && evidence.SelectedInterface is not null
        && HasValidInterfaceGuid(
            evidence.SelectedInterface.InterfaceIdentity);

    private static bool HasValidInterfaceGuid(string? value) =>
        Guid.TryParse(
            (value ?? string.Empty).Trim().Trim('{', '}'),
            out _);

    private InternalProxyRouteComparisonRunResult CreateCanceled(
        ProxyDirectiveRouteAnalysisPlan plan,
        ProxyEndpointParseResult? parsed,
        ProxyDirectiveRouteAnalysisExecutionResult<
            ProxyEndpointRouteAnalysisResult>? execution,
        DestinationRouteEvidence? internalRoute,
        InternalProxyRouteComparisonResult? comparison,
        bool expectedWlanIdentityAvailable,
        bool internalRouteReadPerformed,
        bool proxyRouteAnalysisPerformed,
        Uri? externalTarget) =>
        CreateResult(
            InternalProxyRouteComparisonRunStatus.Canceled,
            plan,
            parsed,
            execution,
            comparison,
            internalRoute,
            expectedWlanIdentityAvailable,
            internalRouteReadPerformed,
            proxyRouteAnalysisPerformed,
            "사용자 요청으로 로컬 경로 비교를 중단했습니다. 이후 DNS와 라우팅 조회를 시작하지 않았습니다.",
            externalTarget);

    private InternalProxyRouteComparisonRunResult CreateResult(
        InternalProxyRouteComparisonRunStatus status,
        ProxyDirectiveRouteAnalysisPlan plan,
        ProxyEndpointParseResult? parsed,
        ProxyDirectiveRouteAnalysisExecutionResult<
            ProxyEndpointRouteAnalysisResult>? execution,
        InternalProxyRouteComparisonResult? comparison,
        DestinationRouteEvidence? internalRoute,
        bool expectedWlanIdentityAvailable,
        bool internalRouteReadPerformed,
        bool proxyRouteAnalysisPerformed,
        string message,
        Uri? externalTarget,
        bool forceDirectPrimary = false)
    {
        ProxyEndpointRouteAnalysisResult? analysis =
            execution?.Analysis;
        bool directPresent = forceDirectPrimary
            || parsed?.DirectPresent == true
            || analysis?.DirectPresent == true
            || plan.DirectiveText?.Trim().Equals(
                "DIRECT",
                StringComparison.OrdinalIgnoreCase) == true;
        bool directIsPrimary = forceDirectPrimary
            || analysis?.DirectIsPrimary == true
            || parsed?.Decision is ProxyEndpointDecision.Direct
                or ProxyEndpointDecision.DirectWithProxyAlternatives;
        bool directFallback = !directIsPrimary
            && (analysis?.DirectFallback
                ?? parsed?.DirectFallback
                ?? false);

        return new InternalProxyRouteComparisonRunResult(
            CompletedAt: _timeProvider.GetUtcNow(),
            Status: status,
            ProxySourceKind: plan.SourceKind,
            ProxySelectionStatus: plan.SelectionStatus,
            ProxyPlanStatus: plan.Status,
            ProxyPlanCode: plan.Code,
            ProxyExecutionStatus: execution?.Status,
            ProxyEndpointSourceKind:
                parsed?.SourceKind
                ?? analysis?.SourceKind
                ?? ProxyEndpointSourceKind.Unknown,
            ProxyDecision:
                analysis?.ProxyDecision
                ?? parsed?.Decision
                ?? (directIsPrimary
                    ? ProxyEndpointDecision.Direct
                    : ProxyEndpointDecision.Unknown),
            TargetScheme:
                analysis?.TargetScheme
                ?? parsed?.TargetScheme
                ?? externalTarget?.Scheme.ToLowerInvariant(),
            InternalRouteStatus: internalRoute?.Status,
            ProxyRouteStatus: analysis?.Status,
            Comparison: comparison,
            ParsedProxyEndpointCount: Math.Max(
                0,
                parsed?.ParsedEndpointCount
                ?? analysis?.ParsedEndpointCount
                ?? 0),
            ApplicableProxyEndpointCount: Math.Max(
                0,
                analysis?.ApplicableEndpointCount
                ?? parsed?.Endpoints.Count
                ?? 0),
            AnalyzedProxyEndpointCount: Math.Max(
                0,
                analysis?.AnalyzedEndpointCount ?? 0),
            SuccessfulProxyEndpointCount: Math.Max(
                0,
                analysis?.SuccessfulEndpointCount ?? 0),
            DistinctProxyInterfaceCount: Math.Max(
                0,
                analysis?.DistinctInterfaceCount ?? 0),
            DirectPresent: directPresent,
            DirectIsPrimary: directIsPrimary,
            DirectFallback: directFallback,
            ProxyParseErrorsPresent:
                plan.HasParseErrors
                || parsed?.Errors.Count > 0,
            ExpectedWlanIdentityAvailable:
                expectedWlanIdentityAvailable,
            InternalRouteReadPerformed:
                internalRouteReadPerformed,
            ProxyRouteAnalysisPerformed:
                proxyRouteAnalysisPerformed,
            Message: message,
            Limitation: ResultLimitation,
            InternalRouteEvidence: internalRoute,
            ProxyExecution: execution);
    }
}
