using System.Runtime.Versioning;
using WlanLivePathTester.Core.Proxy;
using WlanLivePathTester.Core.Reporting;
using WlanLivePathTester.Core.Routing;

namespace WlanLivePathTester.Windows.Routing;

public interface IProxyEndpointRouteEvidenceReader
{
    Task<DestinationRouteEvidence> ReadAsync(
        string host,
        string safeLabel,
        int dnsTimeoutSeconds,
        CancellationToken cancellationToken);
}

[SupportedOSPlatform("windows")]
internal sealed class WindowsProxyEndpointRouteEvidenceReader
    : IProxyEndpointRouteEvidenceReader
{
    public Task<DestinationRouteEvidence> ReadAsync(
        string host,
        string safeLabel,
        int dnsTimeoutSeconds,
        CancellationToken cancellationToken) =>
        LocalRouteEvidenceReader.ReadAsync(
            host,
            safeLabel,
            RouteProbePurpose.ProxyEndpoint,
            dnsTimeoutSeconds,
            cancellationToken);
}

[SupportedOSPlatform("windows")]
public sealed class ProxyEndpointRouteAnalyzer
{
    private const string AnalysisLimitation =
        "이 결과는 현재 PC에서 프록시 엔드포인트까지 선택되는 Windows 로컬 인터페이스만 보여 줍니다. 프록시 서버 내부 상태, 프록시 이후 외부 경로, 인증·정책·캐시·클러스터 상태와 실제 연결 성공 여부는 확인하지 않습니다.";

    private readonly IProxyEndpointRouteEvidenceReader _reader;

    public ProxyEndpointRouteAnalyzer()
        : this(new WindowsProxyEndpointRouteEvidenceReader())
    {
    }

    public ProxyEndpointRouteAnalyzer(
        IProxyEndpointRouteEvidenceReader reader)
    {
        _reader = reader
            ?? throw new ArgumentNullException(nameof(reader));
    }

    public async Task<ProxyEndpointRouteAnalysisResult> AnalyzeAsync(
        ProxyEndpointParseResult parsed,
        string? expectedWlanInterfaceId,
        int dnsTimeoutSeconds = 5,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parsed);
        if (dnsTimeoutSeconds is < 1 or > 30)
        {
            throw new ArgumentOutOfRangeException(
                nameof(dnsTimeoutSeconds),
                "DNS 제한 시간은 1~30초 범위여야 합니다.");
        }

        DateTimeOffset capturedAt = DateTimeOffset.UtcNow;
        IReadOnlyList<string> parseWarnings = parsed.Warnings
            .Select(SanitizeGeneralText)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (parsed.Errors.Count > 0)
        {
            return CreateWithoutEndpoints(
                capturedAt,
                ProxyEndpointRouteAnalysisStatus.InvalidInput,
                parsed,
                parseWarnings,
                "프록시 엔드포인트 입력 오류가 있어 로컬 경로를 확인하지 않았습니다.");
        }

        int? firstDirectSequence = parsed.DirectSequences.Count == 0
            ? null
            : parsed.DirectSequences.Min();
        bool directIsPrimary = firstDirectSequence.HasValue
            && (parsed.Endpoints.Count == 0
                || firstDirectSequence.Value
                    < parsed.Endpoints.Min(endpoint => endpoint.Sequence));

        if (parsed.Decision is ProxyEndpointDecision.Direct
            or ProxyEndpointDecision.DirectWithProxyAlternatives)
        {
            return new ProxyEndpointRouteAnalysisResult(
                CapturedAt: capturedAt,
                Status:
                    ProxyEndpointRouteAnalysisStatus.DirectPathSelected,
                SourceKind: parsed.SourceKind,
                ProxyDecision: parsed.Decision,
                TargetScheme: parsed.TargetScheme,
                DirectPresent: parsed.DirectPresent,
                DirectIsPrimary: true,
                DirectFallback: false,
                DirectSequence: firstDirectSequence,
                ParsedEndpointCount: parsed.ParsedEndpointCount,
                ApplicableEndpointCount: parsed.Endpoints.Count,
                AnalyzedEndpointCount: 0,
                SkippedAfterDirectCount: parsed.Endpoints.Count,
                SuccessfulEndpointCount: 0,
                DistinctInterfaceCount: 0,
                Endpoints:
                    Array.Empty<ProxyEndpointRouteEvidenceItem>(),
                Warnings: parseWarnings,
                Message:
                    "DIRECT가 첫 적용 경로이므로 프록시 후보에 대한 DNS 또는 로컬 라우팅 조회를 수행하지 않았습니다.",
                Limitation: AnalysisLimitation);
        }

        ProxyEndpointCandidate[] effectiveEndpoints = parsed.Endpoints
            .Where(endpoint =>
                !firstDirectSequence.HasValue
                || endpoint.Sequence < firstDirectSequence.Value)
            .OrderBy(endpoint => endpoint.Sequence)
            .ToArray();
        int skippedAfterDirectCount = parsed.Endpoints.Count
            - effectiveEndpoints.Length;

        if (effectiveEndpoints.Length == 0)
        {
            return CreateWithoutEndpoints(
                capturedAt,
                ProxyEndpointRouteAnalysisStatus.NoApplicableEndpoint,
                parsed,
                parseWarnings,
                "현재 대상 URL에 적용되는 프록시 엔드포인트가 없어 로컬 경로를 확인하지 않았습니다.",
                directIsPrimary,
                firstDirectSequence,
                skippedAfterDirectCount);
        }

        List<ProxyEndpointRouteEvidenceItem> evidenceItems = [];
        List<string> analysisWarnings = [.. parseWarnings];
        bool canceled = false;

        foreach (ProxyEndpointCandidate endpoint in effectiveEndpoints)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                canceled = true;
                break;
            }

            DestinationRouteEvidence routeEvidence;
            try
            {
                routeEvidence = await _reader.ReadAsync(
                        endpoint.Host,
                        endpoint.SafeLabel,
                        dnsTimeoutSeconds,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                canceled = true;
                break;
            }
            catch (Exception)
            {
                routeEvidence = new DestinationRouteEvidence(
                    CapturedAt: DateTimeOffset.UtcNow,
                    TargetLabel: endpoint.SafeLabel,
                    Purpose: RouteProbePurpose.ProxyEndpoint,
                    DnsWasUsed: false,
                    ResolvedAddressCount: 0,
                    Status: DestinationRouteEvidenceStatus.Failed,
                    SelectedInterface: null,
                    AddressEvidence:
                        Array.Empty<RouteAddressEvidence>(),
                    Warnings: Array.Empty<string>(),
                    Message:
                        "로컬 라우팅 판정 중 예외가 발생했습니다. 예외 원문은 결과에 포함하지 않았습니다.");
            }

            DestinationRouteEvidence correlated =
                RouteWlanCorrelationEvaluator.Apply(
                    routeEvidence,
                    expectedWlanInterfaceId);
            ProxyEndpointRouteEvidenceItem mapped = MapEvidence(
                endpoint,
                correlated);
            evidenceItems.Add(mapped);
            foreach (string warning in mapped.Warnings)
            {
                if (!analysisWarnings.Contains(
                        warning,
                        StringComparer.Ordinal))
                {
                    analysisWarnings.Add(warning);
                }
            }

            if (correlated.Status
                == DestinationRouteEvidenceStatus.Canceled)
            {
                canceled = true;
                break;
            }
        }

        int successfulEndpointCount = evidenceItems.Count(endpoint =>
            endpoint.IsRouteSuccess);
        int distinctInterfaceCount = evidenceItems
            .Where(endpoint => endpoint.IsRouteSuccess)
            .Select(endpoint => endpoint.SelectedInterfaceFingerprint)
            .Where(value =>
                !string.IsNullOrWhiteSpace(value)
                && !string.Equals(
                    value,
                    "없음",
                    StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        ProxyEndpointRouteAnalysisStatus status;
        string message;
        if (canceled)
        {
            status = ProxyEndpointRouteAnalysisStatus.Canceled;
            message = evidenceItems.Count == 0
                ? "사용자 요청으로 프록시 엔드포인트 로컬 경로 확인을 중단했습니다."
                : $"사용자 요청으로 로컬 경로 확인을 중단했습니다. 완료된 프록시 후보 {evidenceItems.Count}개의 결과만 유지합니다.";
        }
        else if (successfulEndpointCount == evidenceItems.Count)
        {
            if (distinctInterfaceCount > 1)
            {
                status =
                    ProxyEndpointRouteAnalysisStatus.MultipleInterfaces;
                message =
                    $"프록시 후보 {evidenceItems.Count}개의 Windows 로컬 경로를 확인했으며 서로 다른 로컬 인터페이스 {distinctInterfaceCount}개가 선택됐습니다.";
            }
            else
            {
                status = ProxyEndpointRouteAnalysisStatus.Success;
                message =
                    $"프록시 후보 {evidenceItems.Count}개의 Windows 로컬 경로를 확인했습니다.";
            }
        }
        else if (successfulEndpointCount > 0)
        {
            status = ProxyEndpointRouteAnalysisStatus.PartialSuccess;
            message =
                $"프록시 후보 {evidenceItems.Count}개 중 {successfulEndpointCount}개의 Windows 로컬 경로만 확인했습니다.";
        }
        else
        {
            status = ProxyEndpointRouteAnalysisStatus.Failed;
            message =
                $"프록시 후보 {evidenceItems.Count}개의 Windows 로컬 경로를 확인하지 못했습니다.";
        }

        if (parsed.DirectFallback)
        {
            analysisWarnings.Add(
                "프록시 후보 뒤에 DIRECT fallback이 있습니다. 로컬 인터페이스 판정은 실제 프록시 연결 성공 여부를 시험하지 않으므로 DIRECT 전환 발생 여부를 확정하지 않습니다.");
        }

        return new ProxyEndpointRouteAnalysisResult(
            CapturedAt: capturedAt,
            Status: status,
            SourceKind: parsed.SourceKind,
            ProxyDecision: parsed.Decision,
            TargetScheme: parsed.TargetScheme,
            DirectPresent: parsed.DirectPresent,
            DirectIsPrimary: directIsPrimary,
            DirectFallback: parsed.DirectFallback,
            DirectSequence: firstDirectSequence,
            ParsedEndpointCount: parsed.ParsedEndpointCount,
            ApplicableEndpointCount: parsed.Endpoints.Count,
            AnalyzedEndpointCount: evidenceItems.Count,
            SkippedAfterDirectCount: skippedAfterDirectCount,
            SuccessfulEndpointCount: successfulEndpointCount,
            DistinctInterfaceCount: distinctInterfaceCount,
            Endpoints: evidenceItems.ToArray(),
            Warnings: analysisWarnings
                .Distinct(StringComparer.Ordinal)
                .ToArray(),
            Message: message,
            Limitation: AnalysisLimitation);
    }

    private static ProxyEndpointRouteAnalysisResult
        CreateWithoutEndpoints(
            DateTimeOffset capturedAt,
            ProxyEndpointRouteAnalysisStatus status,
            ProxyEndpointParseResult parsed,
            IReadOnlyList<string> warnings,
            string message,
            bool directIsPrimary = false,
            int? directSequence = null,
            int skippedAfterDirectCount = 0) =>
        new(
            CapturedAt: capturedAt,
            Status: status,
            SourceKind: parsed.SourceKind,
            ProxyDecision: parsed.Decision,
            TargetScheme: parsed.TargetScheme,
            DirectPresent: parsed.DirectPresent,
            DirectIsPrimary: directIsPrimary,
            DirectFallback: parsed.DirectFallback,
            DirectSequence: directSequence,
            ParsedEndpointCount: parsed.ParsedEndpointCount,
            ApplicableEndpointCount: parsed.Endpoints.Count,
            AnalyzedEndpointCount: 0,
            SkippedAfterDirectCount: skippedAfterDirectCount,
            SuccessfulEndpointCount: 0,
            DistinctInterfaceCount: 0,
            Endpoints:
                Array.Empty<ProxyEndpointRouteEvidenceItem>(),
            Warnings: warnings,
            Message: message,
            Limitation: AnalysisLimitation);

    private static ProxyEndpointRouteEvidenceItem MapEvidence(
        ProxyEndpointCandidate endpoint,
        DestinationRouteEvidence evidence)
    {
        RouteInterfaceDescriptor? selected =
            evidence.SelectedInterface;
        int successfulAddressCount = evidence.AddressEvidence.Count(item =>
            item.Status == RouteAddressEvidenceStatus.Success);
        int failedAddressCount = evidence.AddressEvidence.Count
            - successfulAddressCount;
        string[] sensitiveValues =
        [
            endpoint.Host,
            selected?.InterfaceIdentity ?? string.Empty,
            selected?.DisplayName ?? string.Empty,
            selected?.Description ?? string.Empty
        ];

        return new ProxyEndpointRouteEvidenceItem(
            Sequence: endpoint.Sequence,
            EndpointLabel: endpoint.SafeLabel,
            HostFingerprint: endpoint.HostFingerprint,
            AppliesToScheme: endpoint.AppliesToScheme,
            Transport: endpoint.Transport,
            Port: endpoint.Port,
            RouteStatus: evidence.Status,
            WlanCorrelationStatus: evidence.WlanCorrelationStatus,
            SelectedInterfaceFingerprint:
                selected?.IdentityFingerprint,
            SelectedInterfaceCategory: selected?.Category,
            SelectedInterfaceIsVirtual: selected?.IsVirtual,
            SelectedInterfaceIsVpn: selected?.IsVpn,
            SelectedInterfaceIsUp: selected?.IsUp,
            SelectedInterfaceHasDefaultGateway:
                selected?.HasDefaultGateway,
            ResolvedAddressCount: Math.Max(
                0,
                evidence.ResolvedAddressCount),
            SuccessfulAddressCount: successfulAddressCount,
            FailedAddressCount: failedAddressCount,
            Message: SanitizeRouteText(
                evidence.Message,
                sensitiveValues),
            Warnings: evidence.Warnings
                .Select(value => SanitizeRouteText(
                    value,
                    sensitiveValues))
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.Ordinal)
                .ToArray());
    }

    private static string SanitizeRouteText(
        string? value,
        IEnumerable<string> sensitiveValues)
    {
        string sanitized = value ?? string.Empty;
        foreach (string sensitiveValue in sensitiveValues)
        {
            string candidate = sensitiveValue.Trim();
            if (candidate.Length < 3)
            {
                continue;
            }

            sanitized = sanitized.Replace(
                candidate,
                "[로컬 식별값 마스킹됨]",
                StringComparison.OrdinalIgnoreCase);
        }

        return SensitiveDataRedactor.RedactText(sanitized)
            ?? string.Empty;
    }

    private static string SanitizeGeneralText(string? value) =>
        SensitiveDataRedactor.RedactText(value)
        ?? string.Empty;
}
