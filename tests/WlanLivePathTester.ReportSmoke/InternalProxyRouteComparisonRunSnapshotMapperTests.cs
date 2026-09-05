using System.Runtime.CompilerServices;
using System.Text.Json;
using WlanLivePathTester.Core.NetworkEnvironment;
using WlanLivePathTester.Core.Proxy;
using WlanLivePathTester.Core.Reporting;
using WlanLivePathTester.Core.Routing;

namespace WlanLivePathTester.ReportSmoke;

internal static class InternalProxyRouteComparisonRunSnapshotMapperTests
{
    private const string InternalFingerprint = "0123456789";
    private const string ProxyFingerprint = "abcdef0123";
    private const string SecretGuid =
        "D3B2C3D4-E5F6-47A8-9123-1234567890AB";
    private const string SecretUrl =
        "https://internal-secret.example.invalid/private.bin";
    private const string SecretHost =
        "proxy-secret.example.invalid";
    private const string SecretEmail =
        "snapshot-user@example.invalid";
    private static readonly DateTimeOffset FixedNow =
        DateTimeOffset.UnixEpoch.AddDays(8);

#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Run()
    {
        MapsCompletedReadyResult();
        MapsDirectRunWithoutComparison();
        RejectsUnknownEnumsInvalidFingerprintsAndNarratives();
        ClampsNegativeCounts();
        Console.WriteLine(
            "PASS strict coordinated route comparison snapshot tests");
    }

    private static void MapsCompletedReadyResult()
    {
        InternalProxyRouteComparisonRunSnapshot snapshot =
            InternalProxyRouteComparisonRunSnapshotMapper.FromResult(
                CreateRun(
                    InternalProxyRouteComparisonRunStatus.Completed,
                    CreateComparison(
                        InternalProxyRouteComparisonStatus.Ready)));

        Ensure(snapshot.SchemaVersion == "1.0"
               && !snapshot.SensitiveValuesIncluded,
            "안전 스냅샷의 스키마와 민감값 미포함 선언이 필요합니다.");
        Ensure(snapshot.RunStatus == "Completed"
               && snapshot.ProxySourceKind == "AutoProxyResult"
               && snapshot.ProxyDecision
                   == "ProxyWithDirectFallback",
            "실행·출처·프록시 결정을 구조화 문자열로 유지해야 합니다.");
        Ensure(snapshot.TargetScheme == "https"
               && snapshot.InternalRouteStatus == "Success"
               && snapshot.ProxyRouteStatus == "Success"
               && snapshot.ComparisonStatus == "Ready",
            "대상 스킴과 내부·프록시·비교 상태를 유지해야 합니다.");
        Ensure(snapshot.InternalInterface?.InterfaceFingerprint
               == InternalFingerprint
               && snapshot.InternalInterface.Category == "Wireless",
            "내부 인터페이스의 검증된 지문과 범주를 유지해야 합니다.");
        Ensure(snapshot.ProxyInterface?.InterfaceFingerprint
               == InternalFingerprint
               && snapshot.ProxyInterface.Category == "Wireless",
            "Ready 프록시 인터페이스의 검증된 지문과 범주를 유지해야 합니다.");
        Ensure(snapshot.SameLocalInterface == true
               && snapshot.ProxyDistinctInterfaceCount == 1,
            "같은 로컬 인터페이스와 distinct 집계를 유지해야 합니다.");
        Ensure(snapshot.Finding.Code
               == "INTERNAL_PROXY_ROUTE_COMPARISON_READY",
            "안전 스냅샷에 같은 고정 Finding 계약을 사용해야 합니다.");
    }

    private static void MapsDirectRunWithoutComparison()
    {
        InternalProxyRouteComparisonRunSnapshot snapshot =
            InternalProxyRouteComparisonRunSnapshotMapper.FromResult(
                CreateRun(
                    InternalProxyRouteComparisonRunStatus
                        .DirectPathSelected,
                    comparison: null) with
                {
                    ProxyDecision =
                        ProxyEndpointDecision
                            .DirectWithProxyAlternatives,
                    InternalRouteStatus = null,
                    ProxyRouteStatus =
                        ProxyEndpointRouteAnalysisStatus
                            .DirectPathSelected,
                    InternalRouteReadPerformed = false,
                    ProxyRouteAnalysisPerformed = false,
                    DirectPresent = true,
                    DirectFallback = false
                });

        Ensure(snapshot.ComparisonStatus is null
               && snapshot.InternalInterface is null
               && snapshot.ProxyInterface is null,
            "DIRECT 우선 실행에는 비교·인터페이스 객체가 없어야 합니다.");
        Ensure(snapshot.RunStatus == "DirectPathSelected"
               && snapshot.DirectPresent
               && !snapshot.DirectFallback,
            "DIRECT 우선 구조화 상태를 유지해야 합니다.");
        Ensure(snapshot.Finding.Code
               == "INTERNAL_PROXY_ROUTE_COMPARISON_DIRECT_PRIMARY",
            "DIRECT 우선 Finding을 사용해야 합니다.");
    }

    private static void
        RejectsUnknownEnumsInvalidFingerprintsAndNarratives()
    {
        InternalProxyRouteComparisonResult maliciousComparison =
            CreateComparison(
                (InternalProxyRouteComparisonStatus)999) with
            {
                InternalInterface = new LocalRouteComparisonInterface(
                    InterfaceFingerprint: SecretGuid,
                    Category: (NetworkAdapterCategory)999,
                    IsVirtual: false,
                    IsVpn: false,
                    IsUp: true,
                    HasDefaultGateway: true,
                    MatchesExpectedWlan: true),
                ProxyInterface = new LocalRouteComparisonInterface(
                    InterfaceFingerprint: SecretHost,
                    Category: NetworkAdapterCategory.Tunnel,
                    IsVirtual: true,
                    IsVpn: true,
                    IsUp: true,
                    HasDefaultGateway: true,
                    MatchesExpectedWlan: false),
                Message:
                    $"{SecretUrl} {SecretHost} {SecretGuid}",
                Limitation:
                    $"{SecretEmail} {InternalFingerprint} {ProxyFingerprint}",
                Warnings:
                [
                    $"{SecretUrl} {SecretHost} {SecretGuid}"
                ]
            };
        InternalProxyRouteComparisonRunResult maliciousRun =
            CreateRun(
                (InternalProxyRouteComparisonRunStatus)999,
                maliciousComparison) with
            {
                ProxySourceKind =
                    (ProxyEndpointSourceKind)999,
                ProxyDecision = (ProxyEndpointDecision)999,
                TargetScheme = SecretUrl,
                InternalRouteStatus =
                    (DestinationRouteEvidenceStatus)999,
                ProxyRouteStatus =
                    (ProxyEndpointRouteAnalysisStatus)999,
                Message:
                    $"{SecretUrl} {SecretHost} {SecretGuid}",
                Limitation:
                    $"{SecretEmail} {InternalFingerprint} {ProxyFingerprint}"
            };

        InternalProxyRouteComparisonRunSnapshot snapshot =
            InternalProxyRouteComparisonRunSnapshotMapper.FromResult(
                maliciousRun);
        string json = JsonSerializer.Serialize(snapshot);

        Ensure(snapshot.RunStatus == "Failed"
               && snapshot.ProxySourceKind == "Unknown"
               && snapshot.ProxyDecision == "Unknown",
            "정의되지 않은 실행·출처·결정 enum은 안전한 fallback으로 바꿔야 합니다.");
        Ensure(snapshot.InternalRouteStatus == "Unknown"
               && snapshot.ProxyRouteStatus == "Unknown"
               && snapshot.ComparisonStatus == "Incomplete",
            "정의되지 않은 상태 enum을 알려진 정상 상태로 추정하면 안 됩니다.");
        Ensure(snapshot.TargetScheme is null,
            "URL 전체를 대상 스킴으로 보고서에 유지하면 안 됩니다.");
        Ensure(snapshot.InternalInterface is null
               && snapshot.ProxyInterface is null,
            "전체 GUID·호스트 또는 정의되지 않은 범주를 안전 인터페이스로 받아들이면 안 됩니다.");
        Ensure(snapshot.Finding.Code
               == "INTERNAL_PROXY_ROUTE_COMPARISON_UNKNOWN",
            "정의되지 않은 실행 상태는 unknown Finding이어야 합니다.");

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
                $"안전 스냅샷 JSON에 자유형 원문·잘못된 지문이 남았습니다: {secret}");
        }
    }

    private static void ClampsNegativeCounts()
    {
        InternalProxyRouteComparisonRunResult run = CreateRun(
            InternalProxyRouteComparisonRunStatus.InvalidInput,
            comparison: null) with
        {
            ParsedProxyEndpointCount = -10,
            AnalyzedProxyEndpointCount = -20,
            SuccessfulProxyEndpointCount = -30
        };
        InternalProxyRouteComparisonRunSnapshot snapshot =
            InternalProxyRouteComparisonRunSnapshotMapper.FromResult(
                run);

        Ensure(snapshot.ParsedProxyEndpointCount == 0
               && snapshot.AnalyzedProxyEndpointCount == 0
               && snapshot.SuccessfulProxyEndpointCount == 0,
            "음수 후보 집계를 안전 스냅샷에 유지하면 안 됩니다.");
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
            ProxyRouteStatus:
                ProxyEndpointRouteAnalysisStatus.Success,
            InternalInterface: internalInterface,
            ProxyInterface: proxyInterface,
            ExpectedWlanInterfaceFingerprint:
                InternalFingerprint,
            SameLocalInterface: same,
            InternalEvidencePartial: false,
            ProxyEvidencePartial: false,
            ProxyDirectPathSelected: false,
            ProxyDirectFallbackPresent: true,
            ProxyCandidateCount: 2,
            ProxySuccessfulCandidateCount: 2,
            ProxyDistinctInterfaceCount: 1,
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
