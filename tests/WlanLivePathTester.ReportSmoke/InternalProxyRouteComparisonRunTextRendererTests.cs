using System.Runtime.CompilerServices;
using WlanLivePathTester.Core.NetworkEnvironment;
using WlanLivePathTester.Core.Proxy;
using WlanLivePathTester.Core.Reporting;
using WlanLivePathTester.Core.Routing;

namespace WlanLivePathTester.ReportSmoke;

internal static class InternalProxyRouteComparisonRunTextRendererTests
{
    private const string InternalFingerprint = "0123456789";
    private const string ProxyFingerprint = "abcdef0123";
    private const string SecretGuid =
        "F3B2C3D4-E5F6-47A8-9123-1234567890AB";
    private const string SecretUrl =
        "https://internal-secret.example.invalid/private.bin";
    private const string SecretHost =
        "proxy-secret.example.invalid";
    private const string SecretEmail =
        "route-renderer@example.invalid";
    private const string SecretIp = "10.88.77.66";
    private static readonly DateTimeOffset FixedNow =
        DateTimeOffset.UnixEpoch.AddDays(8);

#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Run()
    {
        RendersCompletedDivergedResult();
        RendersDirectWithoutInterfaceSections();
        DoesNotReflectFreeFormOrRawEvidence();
        Console.WriteLine(
            "PASS coordinated route comparison safe text renderer tests");
    }

    private static void RendersCompletedDivergedResult()
    {
        string text = InternalProxyRouteComparisonRunTextRenderer.Render(
            CreateRun(
                InternalProxyRouteComparisonRunStatus.Completed,
                CreateComparison(
                    InternalProxyRouteComparisonStatus.Diverged)));

        Ensure(text.Contains(
                "실행 상태: Completed",
                StringComparison.Ordinal)
               && text.Contains(
                   "상태: Diverged",
                   StringComparison.Ordinal),
            "실행과 비교 상태를 표시해야 합니다.");
        Ensure(text.Contains(
                "프록시 후보(파싱 / 분석 / 성공): 2 / 2 / 2",
                StringComparison.Ordinal),
            "후보 집계를 표시해야 합니다.");
        Ensure(text.Contains(
                $"Wireless / {InternalFingerprint}",
                StringComparison.Ordinal)
               && text.Contains(
                   $"Tunnel / {ProxyFingerprint}",
                   StringComparison.Ordinal),
            "검증된 인터페이스 범주와 지문을 표시해야 합니다.");
        Ensure(text.Contains(
                "Warning · INTERNAL_PROXY_ROUTE_COMPARISON_DIVERGED",
                StringComparison.Ordinal),
            "고정 Finding 심각도와 코드를 표시해야 합니다.");
        Ensure(text.Contains(
                "현재 WLAN 일치: 예",
                StringComparison.Ordinal)
               && text.Contains(
                   "현재 WLAN 일치: 아니요",
                   StringComparison.Ordinal),
            "내부·프록시의 현재 WLAN 일치 여부를 표시해야 합니다.");
    }

    private static void RendersDirectWithoutInterfaceSections()
    {
        InternalProxyRouteComparisonRunResult run = CreateRun(
            InternalProxyRouteComparisonRunStatus.DirectPathSelected,
            comparison: null) with
        {
            ProxyDecision =
                ProxyEndpointDecision.DirectWithProxyAlternatives,
            InternalRouteStatus = null,
            ProxyRouteStatus =
                ProxyEndpointRouteAnalysisStatus.DirectPathSelected,
            InternalRouteReadPerformed = false,
            ProxyRouteAnalysisPerformed = false,
            DirectPresent = true,
            DirectFallback = false
        };
        string text =
            InternalProxyRouteComparisonRunTextRenderer.Render(run);

        Ensure(text.Contains(
                "실행 상태: DirectPathSelected",
                StringComparison.Ordinal)
               && text.Contains(
                   "Information · INTERNAL_PROXY_ROUTE_COMPARISON_DIRECT_PRIMARY",
                   StringComparison.Ordinal),
            "DIRECT 우선 상태와 Finding을 표시해야 합니다.");
        Ensure(text.Contains(
                "단일 안전 인터페이스 근거 없음",
                StringComparison.Ordinal),
            "조회하지 않은 인터페이스를 임의 표시하면 안 됩니다.");
        Ensure(!text.Contains(
                "[비교 결과]",
                StringComparison.Ordinal),
            "비교 객체가 없는 DIRECT 실행에 비교 상태를 만들면 안 됩니다.");
    }

    private static void DoesNotReflectFreeFormOrRawEvidence()
    {
        InternalProxyRouteComparisonResult comparison =
            CreateComparison(
                InternalProxyRouteComparisonStatus.Diverged) with
            {
                Message =
                    $"{SecretUrl} {SecretHost} {SecretGuid}",
                Limitation =
                    $"{SecretEmail} {SecretIp} {InternalFingerprint}",
                Warnings =
                [
                    $"{SecretUrl} {SecretHost} {SecretGuid}"
                ]
            };
        InternalProxyRouteComparisonRunResult run = CreateRun(
            InternalProxyRouteComparisonRunStatus.Completed,
            comparison) with
        {
            Message =
                $"{SecretUrl} {SecretHost} {SecretGuid}",
            Limitation =
                $"{SecretEmail} {SecretIp} {ProxyFingerprint}",
            InternalRouteEvidence = CreateRawInternalEvidence(),
            ProxyRouteAnalysis = CreateRawProxyAnalysis()
        };

        string text =
            InternalProxyRouteComparisonRunTextRenderer.Render(run);

        foreach (string secret in new[]
                 {
                     SecretUrl,
                     "internal-secret.example.invalid",
                     SecretHost,
                     SecretGuid,
                     SecretEmail,
                     SecretIp,
                     "Corporate Secret Adapter"
                 })
        {
            Ensure(!text.Contains(
                    secret,
                    StringComparison.OrdinalIgnoreCase),
                $"안전 렌더러에 자유형 원문 또는 원본 근거가 남았습니다: {secret}");
        }

        Ensure(text.Contains(
                InternalFingerprint,
                StringComparison.Ordinal)
               && text.Contains(
                   ProxyFingerprint,
                   StringComparison.Ordinal),
            "허용된 짧은 인터페이스 지문은 유지해야 합니다.");
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

    private static DestinationRouteEvidence CreateRawInternalEvidence()
    {
        RouteInterfaceDescriptor descriptor = new(
            InterfaceIdentity: SecretGuid,
            DisplayName: "Corporate Secret Adapter",
            Description: "Corporate Secret Adapter",
            NativeInterfaceType: "Wireless80211",
            Category: NetworkAdapterCategory.Wireless,
            OperationalState: NetworkAdapterOperationalState.Up,
            HasDefaultGateway: true,
            IsVirtual: false,
            IsVpn: false);
        return new DestinationRouteEvidence(
            CapturedAt: FixedNow,
            TargetLabel: SecretUrl,
            Purpose: RouteProbePurpose.InternalDirectTarget,
            DnsWasUsed: true,
            ResolvedAddressCount: 1,
            Status: DestinationRouteEvidenceStatus.Success,
            SelectedInterface: descriptor,
            AddressEvidence: Array.Empty<RouteAddressEvidence>(),
            Warnings: [SecretEmail, SecretIp],
            Message: SecretUrl);
    }

    private static ProxyEndpointRouteAnalysisResult
        CreateRawProxyAnalysis() =>
        new(
            CapturedAt: FixedNow,
            Status: ProxyEndpointRouteAnalysisStatus.Success,
            SourceKind: ProxyEndpointSourceKind.AutoProxyResult,
            ProxyDecision:
                ProxyEndpointDecision.ProxyWithDirectFallback,
            TargetScheme: "https",
            DirectPresent: true,
            DirectIsPrimary: false,
            DirectFallback: true,
            DirectSequence: 2,
            ParsedEndpointCount: 1,
            ApplicableEndpointCount: 1,
            AnalyzedEndpointCount: 1,
            SkippedAfterDirectCount: 0,
            SuccessfulEndpointCount: 1,
            DistinctInterfaceCount: 1,
            Endpoints: Array.Empty<ProxyEndpointRouteEvidenceItem>(),
            Warnings: [SecretHost, SecretGuid],
            Message: SecretUrl,
            Limitation: SecretEmail);

    private static void Ensure(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
