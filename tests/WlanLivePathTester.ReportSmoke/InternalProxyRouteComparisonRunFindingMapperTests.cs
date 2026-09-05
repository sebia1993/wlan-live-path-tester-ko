using System.Runtime.CompilerServices;
using System.Text.Json;
using WlanLivePathTester.Core.NetworkEnvironment;
using WlanLivePathTester.Core.Proxy;
using WlanLivePathTester.Core.Reporting;
using WlanLivePathTester.Core.Routing;

namespace WlanLivePathTester.ReportSmoke;

internal static class InternalProxyRouteComparisonRunFindingMapperTests
{
    private const string SecretGuid =
        "C3B2C3D4-E5F6-47A8-9123-1234567890AB";
    private const string SecretHost =
        "proxy-secret.example.invalid";
    private const string SecretUrl =
        "https://internal-secret.example.invalid/private.bin";
    private const string SecretEmail =
        "route-finding@example.invalid";
    private const string InternalFingerprint = "0123456789";
    private const string ProxyFingerprint = "abcdef0123";
    private static readonly DateTimeOffset FixedNow =
        DateTimeOffset.UnixEpoch.AddDays(8);

#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Run()
    {
        VerifyRunStatusMatrix();
        VerifyCompletedComparisonMatrix();
        VerifyMissingAndUnknownResultContracts();
        VerifyFindingDoesNotReflectNarrativesOrFingerprints();
        Console.WriteLine(
            "PASS coordinated route comparison finding matrix tests");
    }

    private static void VerifyRunStatusMatrix()
    {
        (InternalProxyRouteComparisonRunStatus Status,
            string Code,
            string Severity)[] cases =
        [
            (InternalProxyRouteComparisonRunStatus.InvalidInput,
                "INTERNAL_PROXY_ROUTE_COMPARISON_INVALID_INPUT",
                "Warning"),
            (InternalProxyRouteComparisonRunStatus.DirectPathSelected,
                "INTERNAL_PROXY_ROUTE_COMPARISON_DIRECT_PRIMARY",
                "Information"),
            (InternalProxyRouteComparisonRunStatus
                    .InternalRouteUnavailable,
                "INTERNAL_PROXY_ROUTE_COMPARISON_INTERNAL_UNAVAILABLE",
                "Warning"),
            (InternalProxyRouteComparisonRunStatus.Canceled,
                "INTERNAL_PROXY_ROUTE_COMPARISON_CANCELED",
                "Information"),
            (InternalProxyRouteComparisonRunStatus.Failed,
                "INTERNAL_PROXY_ROUTE_COMPARISON_FAILED",
                "Warning")
        ];

        foreach ((InternalProxyRouteComparisonRunStatus status,
                  string code,
                  string severity) in cases)
        {
            InternalProxyRouteComparisonRunResult run = CreateRun(
                status,
                comparison: null);
            ReportFinding finding =
                InternalProxyRouteComparisonRunFindingMapper
                    .FromResult(run);

            Ensure(finding.Code == code,
                $"실행 상태 {status}의 Finding 코드가 잘못됐습니다.");
            Ensure(finding.Severity == severity,
                $"실행 상태 {status}의 Finding 심각도가 잘못됐습니다.");
            Ensure(finding.Evidence.Contains(
                    $"실행 상태는 {status}",
                    StringComparison.Ordinal),
                $"실행 상태 {status}가 근거에 필요합니다.");
            Ensure(finding.Evidence.Contains(
                    "파싱 후보 2개",
                    StringComparison.Ordinal)
                   && finding.Evidence.Contains(
                       "내부 경로 조회는 수행",
                       StringComparison.Ordinal),
                "Finding 근거는 구조화 개수와 실행 단계를 유지해야 합니다.");
        }
    }

    private static void VerifyCompletedComparisonMatrix()
    {
        (InternalProxyRouteComparisonStatus Status,
            string Code,
            string Severity)[] cases =
        [
            (InternalProxyRouteComparisonStatus.Ready,
                "INTERNAL_PROXY_ROUTE_COMPARISON_READY",
                "Information"),
            (InternalProxyRouteComparisonStatus.Diverged,
                "INTERNAL_PROXY_ROUTE_COMPARISON_DIVERGED",
                "Warning"),
            (InternalProxyRouteComparisonStatus.Ambiguous,
                "INTERNAL_PROXY_ROUTE_COMPARISON_AMBIGUOUS",
                "Warning"),
            (InternalProxyRouteComparisonStatus.Incomplete,
                "INTERNAL_PROXY_ROUTE_COMPARISON_INCOMPLETE",
                "Information")
        ];

        foreach ((InternalProxyRouteComparisonStatus status,
                  string code,
                  string severity) in cases)
        {
            InternalProxyRouteComparisonResult comparison =
                CreateComparison(status);
            InternalProxyRouteComparisonRunResult run = CreateRun(
                InternalProxyRouteComparisonRunStatus.Completed,
                comparison);
            ReportFinding finding =
                InternalProxyRouteComparisonRunFindingMapper
                    .FromResult(run);

            Ensure(finding.Code == code,
                $"비교 상태 {status}의 Finding 코드가 잘못됐습니다.");
            Ensure(finding.Severity == severity,
                $"비교 상태 {status}의 Finding 심각도가 잘못됐습니다.");
            Ensure(finding.Evidence.Contains(
                    $"비교 상태는 {status}",
                    StringComparison.Ordinal),
                $"비교 상태 {status}가 근거에 필요합니다.");
            Ensure(finding.Evidence.Contains(
                    "서로 다른 인터페이스 수는 1개",
                    StringComparison.Ordinal),
                "프록시 인터페이스 집계가 근거에 필요합니다.");
        }
    }

    private static void VerifyMissingAndUnknownResultContracts()
    {
        InternalProxyRouteComparisonRunResult missing = CreateRun(
            InternalProxyRouteComparisonRunStatus.Completed,
            comparison: null);
        InternalProxyRouteComparisonRunResult unknownRun = CreateRun(
            (InternalProxyRouteComparisonRunStatus)999,
            comparison: null);
        InternalProxyRouteComparisonRunResult unknownComparison =
            CreateRun(
                InternalProxyRouteComparisonRunStatus.Completed,
                CreateComparison(
                    (InternalProxyRouteComparisonStatus)999));

        ReportFinding missingFinding =
            InternalProxyRouteComparisonRunFindingMapper.FromResult(
                missing);
        ReportFinding unknownRunFinding =
            InternalProxyRouteComparisonRunFindingMapper.FromResult(
                unknownRun);
        ReportFinding unknownComparisonFinding =
            InternalProxyRouteComparisonRunFindingMapper.FromResult(
                unknownComparison);

        Ensure(missingFinding.Code
               == "INTERNAL_PROXY_ROUTE_COMPARISON_RESULT_MISSING"
               && missingFinding.Severity == "Warning",
            "Completed인데 비교 결과가 없으면 fail-closed Warning이어야 합니다.");
        Ensure(unknownRunFinding.Code
               == "INTERNAL_PROXY_ROUTE_COMPARISON_UNKNOWN",
            "알 수 없는 실행 상태를 고정 코드로 처리해야 합니다.");
        Ensure(unknownComparisonFinding.Code
               == "INTERNAL_PROXY_ROUTE_COMPARISON_RESULT_UNKNOWN",
            "알 수 없는 비교 상태를 고정 코드로 처리해야 합니다.");
    }

    private static void
        VerifyFindingDoesNotReflectNarrativesOrFingerprints()
    {
        InternalProxyRouteComparisonResult comparison =
            CreateComparison(
                InternalProxyRouteComparisonStatus.Diverged) with
            {
                Message =
                    $"{SecretUrl} {SecretHost} {SecretGuid}",
                Limitation =
                    $"{SecretEmail} {InternalFingerprint} {ProxyFingerprint}",
                Warnings =
                [
                    $"{SecretHost} {SecretGuid} {SecretEmail}"
                ]
            };
        InternalProxyRouteComparisonRunResult run = CreateRun(
            InternalProxyRouteComparisonRunStatus.Completed,
            comparison) with
        {
            Message =
                $"{SecretUrl} {SecretHost} {SecretGuid}",
            Limitation =
                $"{SecretEmail} {InternalFingerprint} {ProxyFingerprint}"
        };

        ReportFinding finding =
            InternalProxyRouteComparisonRunFindingMapper.FromResult(run);
        string json = JsonSerializer.Serialize(finding);

        foreach (string secret in new[]
                 {
                     SecretUrl,
                     "internal-secret.example.invalid",
                     SecretHost,
                     SecretGuid,
                     SecretEmail,
                     InternalFingerprint,
                     ProxyFingerprint
                 })
        {
            Ensure(!json.Contains(
                    secret,
                    StringComparison.OrdinalIgnoreCase),
                $"Finding에 자유형 원문 또는 지문이 반사됐습니다: {secret}");
        }

        Ensure(json.Contains(
                "INTERNAL_PROXY_ROUTE_COMPARISON_DIVERGED",
                StringComparison.Ordinal)
               && json.Contains(
                   "프록시 경로의 서로 다른 인터페이스 수는 1개",
                   StringComparison.Ordinal),
            "Finding에는 고정 코드와 구조화 집계만 유지해야 합니다.");
    }

    private static InternalProxyRouteComparisonRunResult CreateRun(
        InternalProxyRouteComparisonRunStatus status,
        InternalProxyRouteComparisonResult? comparison) =>
        new(
            CompletedAt: FixedNow,
            Status: status,
            ProxySourceKind:
                ProxyEndpointSourceKind.AutoProxyResult,
            ProxyDecision:
                ProxyEndpointDecision.ProxyWithDirectFallback,
            TargetScheme: "https",
            InternalRouteStatus:
                DestinationRouteEvidenceStatus.Success,
            ProxyRouteStatus:
                ProxyEndpointRouteAnalysisStatus.Success,
            Comparison: comparison,
            ParsedProxyEndpointCount: 2,
            AnalyzedProxyEndpointCount: 2,
            SuccessfulProxyEndpointCount: 2,
            DirectPresent: true,
            DirectFallback: true,
            ExpectedWlanIdentityAvailable: true,
            InternalRouteReadPerformed: true,
            ProxyRouteAnalysisPerformed: true,
            Message: "합성 실행 메시지",
            Limitation: "합성 실행 한계",
            InternalRouteEvidence: null,
            ProxyRouteAnalysis: null);

    private static InternalProxyRouteComparisonResult
        CreateComparison(InternalProxyRouteComparisonStatus status)
    {
        bool? same = status switch
        {
            InternalProxyRouteComparisonStatus.Ready => true,
            InternalProxyRouteComparisonStatus.Diverged => false,
            _ => null
        };
        LocalRouteComparisonInterface internalInterface = new(
            InterfaceFingerprint: InternalFingerprint,
            Category: NetworkAdapterCategory.Wireless,
            IsVirtual: false,
            IsVpn: false,
            IsUp: true,
            HasDefaultGateway: true,
            MatchesExpectedWlan: true);
        LocalRouteComparisonInterface proxyInterface = new(
            InterfaceFingerprint: status
                == InternalProxyRouteComparisonStatus.Diverged
                    ? ProxyFingerprint
                    : InternalFingerprint,
            Category: status
                == InternalProxyRouteComparisonStatus.Diverged
                    ? NetworkAdapterCategory.Tunnel
                    : NetworkAdapterCategory.Wireless,
            IsVirtual: status
                == InternalProxyRouteComparisonStatus.Diverged,
            IsVpn: status
                == InternalProxyRouteComparisonStatus.Diverged,
            IsUp: true,
            HasDefaultGateway: true,
            MatchesExpectedWlan: same);

        return new InternalProxyRouteComparisonResult(
            EvaluatedAt: FixedNow,
            Status: status,
            InternalRouteStatus:
                DestinationRouteEvidenceStatus.Success,
            ProxyRouteStatus: status
                == InternalProxyRouteComparisonStatus.Ambiguous
                    ? ProxyEndpointRouteAnalysisStatus
                        .MultipleInterfaces
                    : status
                        == InternalProxyRouteComparisonStatus.Incomplete
                            ? ProxyEndpointRouteAnalysisStatus
                                .PartialSuccess
                            : ProxyEndpointRouteAnalysisStatus.Success,
            InternalInterface: internalInterface,
            ProxyInterface: status is
                InternalProxyRouteComparisonStatus.Ambiguous
                or InternalProxyRouteComparisonStatus.Incomplete
                    ? null
                    : proxyInterface,
            ExpectedWlanInterfaceFingerprint:
                InternalFingerprint,
            SameLocalInterface: same,
            InternalEvidencePartial: false,
            ProxyEvidencePartial: status
                == InternalProxyRouteComparisonStatus.Incomplete,
            ProxyDirectPathSelected: false,
            ProxyDirectFallbackPresent: true,
            ProxyCandidateCount: 2,
            ProxySuccessfulCandidateCount: status
                == InternalProxyRouteComparisonStatus.Incomplete
                    ? 1
                    : 2,
            ProxyDistinctInterfaceCount: status
                == InternalProxyRouteComparisonStatus.Ambiguous
                    ? 2
                    : 1,
            AnyVirtualInterface: status
                == InternalProxyRouteComparisonStatus.Diverged,
            AnyVpnOrTunnelInterface: status
                == InternalProxyRouteComparisonStatus.Diverged,
            Warnings: Array.Empty<string>(),
            Message: "합성 비교 메시지",
            Limitation: "합성 비교 한계");
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
