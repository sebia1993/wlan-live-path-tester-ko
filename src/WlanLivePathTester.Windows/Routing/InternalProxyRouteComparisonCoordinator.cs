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

public interface IProxyEndpointRouteAnalysisService
{
    Task<ProxyEndpointRouteAnalysisResult> AnalyzeAsync(
        ProxyEndpointParseResult parsed,
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
internal sealed class WindowsProxyEndpointRouteAnalysisService
    : IProxyEndpointRouteAnalysisService
{
    private readonly ProxyEndpointRouteAnalyzer _analyzer = new();

    public Task<ProxyEndpointRouteAnalysisResult> AnalyzeAsync(
        ProxyEndpointParseResult parsed,
        string? expectedWlanInterfaceId,
        int dnsTimeoutSeconds,
        CancellationToken cancellationToken) =>
        _analyzer.AnalyzeAsync(
            parsed,
            expectedWlanInterfaceId,
            dnsTimeoutSeconds,
            cancellationToken);
}

[SupportedOSPlatform("windows")]
public sealed class InternalProxyRouteComparisonCoordinator
{
    public const int MaximumInternalTargetLength = 2048;

    private const string ResultLimitation =
        "이 실행은 현재 PC에서 내부 DIRECT 대상과 프록시 엔드포인트까지 선택되는 Windows 로컬 인터페이스만 비교합니다. HTTP 연결, 프록시 인증, 프록시 서버 내부 상태, 프록시 이후 인터넷 경로와 실제 서비스 품질은 확인하지 않습니다.";

    private readonly IInternalDirectRouteEvidenceReader
        _internalRouteReader;
    private readonly IProxyEndpointRouteAnalysisService
        _proxyRouteAnalysisService;
    private readonly TimeProvider _timeProvider;

    public InternalProxyRouteComparisonCoordinator()
        : this(
            new WindowsInternalDirectRouteEvidenceReader(),
            new WindowsProxyEndpointRouteAnalysisService(),
            TimeProvider.System)
    {
    }

    public InternalProxyRouteComparisonCoordinator(
        IInternalDirectRouteEvidenceReader internalRouteReader,
        IProxyEndpointRouteAnalysisService proxyRouteAnalysisService,
        TimeProvider? timeProvider = null)
    {
        _internalRouteReader = internalRouteReader
            ?? throw new ArgumentNullException(
                nameof(internalRouteReader));
        _proxyRouteAnalysisService = proxyRouteAnalysisService
            ?? throw new ArgumentNullException(
                nameof(proxyRouteAnalysisService));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<InternalProxyRouteComparisonRunResult> RunAsync(
        string? internalTarget,
        string? proxyDirectiveText,
        Uri? externalTarget,
        string? expectedWlanInterfaceId,
        int dnsTimeoutSeconds = 5,
        CancellationToken cancellationToken = default)
    {
        if (dnsTimeoutSeconds is < 1 or > 30)
        {
            throw new ArgumentOutOfRangeException(
                nameof(dnsTimeoutSeconds),
                "DNS 제한 시간은 1~30초 범위여야 합니다.");
        }

        string normalizedInternalTarget =
            (internalTarget ?? string.Empty).Trim();
        string normalizedProxyDirective =
            (proxyDirectiveText ?? string.Empty).Trim();
        bool expectedWlanIdentityAvailable =
            HasValidInterfaceGuid(expectedWlanInterfaceId);

        string? inputError = ValidateInput(
            normalizedInternalTarget,
            normalizedProxyDirective,
            externalTarget);
        if (inputError is not null)
        {
            return CreateResult(
                InternalProxyRouteComparisonRunStatus.InvalidInput,
                ProxyEndpointSourceKind.Unknown,
                ProxyEndpointDecision.Unknown,
                targetScheme: null,
                internalRouteStatus: null,
                proxyRouteStatus: null,
                comparison: null,
                parsedProxyEndpointCount: 0,
                analyzedProxyEndpointCount: 0,
                successfulProxyEndpointCount: 0,
                directPresent: false,
                directFallback: false,
                expectedWlanIdentityAvailable,
                internalRouteReadPerformed: false,
                proxyRouteAnalysisPerformed: false,
                inputError,
                internalRouteEvidence: null,
                proxyRouteAnalysis: null);
        }

        ProxyEndpointParseResult parsed =
            ProxyEndpointParser.Parse(
                normalizedProxyDirective,
                externalTarget);
        if (!parsed.IsUsable
            || parsed.Errors.Count > 0
            || parsed.Decision == ProxyEndpointDecision.Unknown)
        {
            return CreateResult(
                InternalProxyRouteComparisonRunStatus.InvalidInput,
                parsed.SourceKind,
                parsed.Decision,
                parsed.TargetScheme,
                internalRouteStatus: null,
                proxyRouteStatus: null,
                comparison: null,
                parsed.ParsedEndpointCount,
                analyzedProxyEndpointCount: 0,
                successfulProxyEndpointCount: 0,
                parsed.DirectPresent,
                parsed.DirectFallback,
                expectedWlanIdentityAvailable,
                internalRouteReadPerformed: false,
                proxyRouteAnalysisPerformed: false,
                "프록시 지시문에서 현재 외부 대상에 적용되는 안전한 경로를 결정하지 못해 DNS·라우팅 조회를 시작하지 않았습니다.",
                internalRouteEvidence: null,
                proxyRouteAnalysis: null);
        }

        if (parsed.Decision is ProxyEndpointDecision.Direct
            or ProxyEndpointDecision.DirectWithProxyAlternatives)
        {
            return CreateResult(
                InternalProxyRouteComparisonRunStatus.DirectPathSelected,
                parsed.SourceKind,
                parsed.Decision,
                parsed.TargetScheme,
                internalRouteStatus: null,
                proxyRouteStatus:
                    ProxyEndpointRouteAnalysisStatus.DirectPathSelected,
                comparison: null,
                parsed.ParsedEndpointCount,
                analyzedProxyEndpointCount: 0,
                successfulProxyEndpointCount: 0,
                parsed.DirectPresent,
                directFallback: false,
                expectedWlanIdentityAvailable,
                internalRouteReadPerformed: false,
                proxyRouteAnalysisPerformed: false,
                "DIRECT가 첫 적용 경로이므로 비교할 프록시 엔드포인트가 없습니다. 내부 대상과 프록시 후보에 대한 DNS·라우팅 조회를 모두 생략했습니다.",
                internalRouteEvidence: null,
                proxyRouteAnalysis: null);
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return CreateCanceled(
                parsed,
                expectedWlanIdentityAvailable,
                internalRouteReadPerformed: false,
                proxyRouteAnalysisPerformed: false,
                internalRouteEvidence: null,
                proxyRouteAnalysis: null);
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
                parsed,
                expectedWlanIdentityAvailable,
                internalRouteReadPerformed: true,
                proxyRouteAnalysisPerformed: false,
                internalRouteEvidence: null,
                proxyRouteAnalysis: null);
        }
        catch (Exception)
        {
            return CreateResult(
                InternalProxyRouteComparisonRunStatus.Failed,
                parsed.SourceKind,
                parsed.Decision,
                parsed.TargetScheme,
                internalRouteStatus: null,
                proxyRouteStatus: null,
                comparison: null,
                parsed.ParsedEndpointCount,
                analyzedProxyEndpointCount: 0,
                successfulProxyEndpointCount: 0,
                parsed.DirectPresent,
                parsed.DirectFallback,
                expectedWlanIdentityAvailable,
                internalRouteReadPerformed: true,
                proxyRouteAnalysisPerformed: false,
                "내부 DIRECT 대상의 로컬 경로 확인 중 오류가 발생했습니다. 입력 원문과 예외 메시지는 결과에 포함하지 않았습니다.",
                internalRouteEvidence: null,
                proxyRouteAnalysis: null);
        }

        if (internalRoute.Status
            == DestinationRouteEvidenceStatus.Canceled)
        {
            return CreateCanceled(
                parsed,
                expectedWlanIdentityAvailable,
                internalRouteReadPerformed: true,
                proxyRouteAnalysisPerformed: false,
                internalRoute,
                proxyRouteAnalysis: null);
        }

        if (!CanContinueWithInternalRoute(internalRoute))
        {
            return CreateResult(
                InternalProxyRouteComparisonRunStatus
                    .InternalRouteUnavailable,
                parsed.SourceKind,
                parsed.Decision,
                parsed.TargetScheme,
                internalRoute.Status,
                proxyRouteStatus: null,
                comparison: null,
                parsed.ParsedEndpointCount,
                analyzedProxyEndpointCount: 0,
                successfulProxyEndpointCount: 0,
                parsed.DirectPresent,
                parsed.DirectFallback,
                expectedWlanIdentityAvailable,
                internalRouteReadPerformed: true,
                proxyRouteAnalysisPerformed: false,
                "내부 DIRECT 대상의 비교 가능한 로컬 인터페이스 근거를 확인하지 못해 프록시 후보 DNS·라우팅 조회를 시작하지 않았습니다.",
                internalRoute,
                proxyRouteAnalysis: null);
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return CreateCanceled(
                parsed,
                expectedWlanIdentityAvailable,
                internalRouteReadPerformed: true,
                proxyRouteAnalysisPerformed: false,
                internalRoute,
                proxyRouteAnalysis: null);
        }

        ProxyEndpointRouteAnalysisResult proxyAnalysis;
        try
        {
            proxyAnalysis = await _proxyRouteAnalysisService
                .AnalyzeAsync(
                    parsed,
                    expectedWlanInterfaceId,
                    dnsTimeoutSeconds,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return CreateCanceled(
                parsed,
                expectedWlanIdentityAvailable,
                internalRouteReadPerformed: true,
                proxyRouteAnalysisPerformed: true,
                internalRoute,
                proxyRouteAnalysis: null);
        }
        catch (Exception)
        {
            return CreateResult(
                InternalProxyRouteComparisonRunStatus.Failed,
                parsed.SourceKind,
                parsed.Decision,
                parsed.TargetScheme,
                internalRoute.Status,
                proxyRouteStatus: null,
                comparison: null,
                parsed.ParsedEndpointCount,
                analyzedProxyEndpointCount: 0,
                successfulProxyEndpointCount: 0,
                parsed.DirectPresent,
                parsed.DirectFallback,
                expectedWlanIdentityAvailable,
                internalRouteReadPerformed: true,
                proxyRouteAnalysisPerformed: true,
                "프록시 엔드포인트의 로컬 경로 확인 중 오류가 발생했습니다. 프록시 원문과 예외 메시지는 결과에 포함하지 않았습니다.",
                internalRoute,
                proxyRouteAnalysis: null);
        }

        if (proxyAnalysis.Status
            == ProxyEndpointRouteAnalysisStatus.Canceled)
        {
            return CreateCanceled(
                parsed,
                expectedWlanIdentityAvailable,
                internalRouteReadPerformed: true,
                proxyRouteAnalysisPerformed: true,
                internalRoute,
                proxyAnalysis);
        }

        InternalProxyRouteComparisonResult comparison =
            InternalProxyRouteComparison.Compare(
                internalRoute,
                proxyAnalysis,
                expectedWlanInterfaceId,
                _timeProvider.GetUtcNow());

        return CreateResult(
            InternalProxyRouteComparisonRunStatus.Completed,
            parsed.SourceKind,
            parsed.Decision,
            parsed.TargetScheme,
            internalRoute.Status,
            proxyAnalysis.Status,
            comparison,
            parsed.ParsedEndpointCount,
            proxyAnalysis.AnalyzedEndpointCount,
            proxyAnalysis.SuccessfulEndpointCount,
            parsed.DirectPresent,
            proxyAnalysis.DirectFallback,
            expectedWlanIdentityAvailable,
            internalRouteReadPerformed: true,
            proxyRouteAnalysisPerformed: true,
            "내부 DIRECT 대상과 적용 가능한 프록시 엔드포인트의 Windows 로컬 인터페이스 비교를 완료했습니다.",
            internalRoute,
            proxyAnalysis);
    }

    private static string? ValidateInput(
        string internalTarget,
        string proxyDirectiveText,
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

        if (proxyDirectiveText.Length == 0)
        {
            return "프록시 지시문이 비어 있습니다.";
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
        evidence.Status is DestinationRouteEvidenceStatus.Success
            or DestinationRouteEvidenceStatus.PartialSuccess
            or DestinationRouteEvidenceStatus.MultipleInterfaces;

    private static bool HasValidInterfaceGuid(string? value) =>
        Guid.TryParse(
            (value ?? string.Empty).Trim().Trim('{', '}'),
            out _);

    private InternalProxyRouteComparisonRunResult CreateCanceled(
        ProxyEndpointParseResult parsed,
        bool expectedWlanIdentityAvailable,
        bool internalRouteReadPerformed,
        bool proxyRouteAnalysisPerformed,
        DestinationRouteEvidence? internalRouteEvidence,
        ProxyEndpointRouteAnalysisResult? proxyRouteAnalysis) =>
        CreateResult(
            InternalProxyRouteComparisonRunStatus.Canceled,
            parsed.SourceKind,
            parsed.Decision,
            parsed.TargetScheme,
            internalRouteEvidence?.Status,
            proxyRouteAnalysis?.Status,
            comparison: null,
            parsed.ParsedEndpointCount,
            proxyRouteAnalysis?.AnalyzedEndpointCount ?? 0,
            proxyRouteAnalysis?.SuccessfulEndpointCount ?? 0,
            parsed.DirectPresent,
            proxyRouteAnalysis?.DirectFallback
                ?? parsed.DirectFallback,
            expectedWlanIdentityAvailable,
            internalRouteReadPerformed,
            proxyRouteAnalysisPerformed,
            "사용자 요청으로 로컬 경로 비교를 중단했습니다. 이후 DNS·라우팅 조회는 시작하지 않았습니다.",
            internalRouteEvidence,
            proxyRouteAnalysis);

    private InternalProxyRouteComparisonRunResult CreateResult(
        InternalProxyRouteComparisonRunStatus status,
        ProxyEndpointSourceKind proxySourceKind,
        ProxyEndpointDecision proxyDecision,
        string? targetScheme,
        DestinationRouteEvidenceStatus? internalRouteStatus,
        ProxyEndpointRouteAnalysisStatus? proxyRouteStatus,
        InternalProxyRouteComparisonResult? comparison,
        int parsedProxyEndpointCount,
        int analyzedProxyEndpointCount,
        int successfulProxyEndpointCount,
        bool directPresent,
        bool directFallback,
        bool expectedWlanIdentityAvailable,
        bool internalRouteReadPerformed,
        bool proxyRouteAnalysisPerformed,
        string message,
        DestinationRouteEvidence? internalRouteEvidence,
        ProxyEndpointRouteAnalysisResult? proxyRouteAnalysis) =>
        new(
            CompletedAt: _timeProvider.GetUtcNow(),
            Status: status,
            ProxySourceKind: proxySourceKind,
            ProxyDecision: proxyDecision,
            TargetScheme: targetScheme,
            InternalRouteStatus: internalRouteStatus,
            ProxyRouteStatus: proxyRouteStatus,
            Comparison: comparison,
            ParsedProxyEndpointCount: Math.Max(
                0,
                parsedProxyEndpointCount),
            AnalyzedProxyEndpointCount: Math.Max(
                0,
                analyzedProxyEndpointCount),
            SuccessfulProxyEndpointCount: Math.Max(
                0,
                successfulProxyEndpointCount),
            DirectPresent: directPresent,
            DirectFallback: directFallback,
            ExpectedWlanIdentityAvailable:
                expectedWlanIdentityAvailable,
            InternalRouteReadPerformed:
                internalRouteReadPerformed,
            ProxyRouteAnalysisPerformed:
                proxyRouteAnalysisPerformed,
            Message: message,
            Limitation: ResultLimitation,
            InternalRouteEvidence: internalRouteEvidence,
            ProxyRouteAnalysis: proxyRouteAnalysis);
}
