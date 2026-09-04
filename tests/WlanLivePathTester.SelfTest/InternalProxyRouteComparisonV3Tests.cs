using System.Runtime.CompilerServices;
using System.Text.Json;
using WlanLivePathTester.Core.NetworkEnvironment;
using WlanLivePathTester.Core.Proxy;
using WlanLivePathTester.Core.Routing;

namespace WlanLivePathTester.SelfTest;

internal static class InternalProxyRouteComparisonV3Tests
{
    private const string WlanId =
        "91B2C3D4-E5F6-47A8-9123-1234567890AB";
    private const string EthernetId =
        "A2B2C3D4-E5F6-47A8-9123-1234567890AB";
    private const string TunnelId =
        "B2B2C3D4-E5F6-47A8-9123-1234567890AB";
    private static readonly DateTimeOffset EvaluationTime =
        DateTimeOffset.UnixEpoch.AddDays(8);

#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Run()
    {
        SameWlanInterfaceIsReady();
        DifferentProxyTunnelIsDiverged();
        MultipleProxyInterfacesAreAmbiguous();
        InternalMultipleInterfacesAreAmbiguous();
        DirectAndPartialProxyEvidenceAreIncomplete();
        InternalPartialEvidenceCanRemainComparable();
        ConflictingProxyMetadataIsAmbiguous();
        MissingWlanIdentityDoesNotBlockInterfaceComparison();
        ResultNeverContainsRawInterfaceIdentityOrNames();
        Console.WriteLine(
            "PASS internal DIRECT and proxy route comparison v3 tests");
    }

    private static void SameWlanInterfaceIsReady()
    {
        DestinationRouteEvidence internalRoute = InternalEvidence(
            DestinationRouteEvidenceStatus.Success,
            WlanId,
            NetworkAdapterCategory.Wireless);
        ProxyEndpointRouteAnalysisResult proxyRoute = ProxyEvidence(
            ProxyEndpointRouteAnalysisStatus.Success,
            [
                ProxyItem(
                    1,
                    WlanId,
                    NetworkAdapterCategory.Wireless,
                    isVirtual: false,
                    isVpn: false)
            ]);

        InternalProxyRouteComparisonResult result =
            InternalProxyRouteComparison.Compare(
                internalRoute,
                proxyRoute,
                WlanId,
                EvaluationTime);

        Ensure(result.Status
               == InternalProxyRouteComparisonStatus.Ready,
            "같은 로컬 인터페이스 지문은 Ready여야 합니다.");
        Ensure(result.IsComparable
               && result.SameLocalInterface == true,
            "Ready 결과는 비교 가능하고 같은 인터페이스여야 합니다.");
        Ensure(result.InternalInterface?.MatchesExpectedWlan == true
               && result.ProxyInterface?.MatchesExpectedWlan == true,
            "내부·프록시 경로 모두 현재 WLAN과 일치해야 합니다.");
        Ensure(result.ExpectedWlanInterfaceFingerprint
               == RouteInterfaceFingerprint.Create(WlanId),
            "원문 GUID 대신 현재 WLAN 지문을 유지해야 합니다.");
        Ensure(!result.AnyVirtualInterface
               && !result.AnyVpnOrTunnelInterface,
            "합성 물리 Wi-Fi 경로를 가상·VPN으로 표시하면 안 됩니다.");
        Ensure(result.EvaluatedAt == EvaluationTime,
            "결정론적 평가 시각을 유지해야 합니다.");
    }

    private static void DifferentProxyTunnelIsDiverged()
    {
        DestinationRouteEvidence internalRoute = InternalEvidence(
            DestinationRouteEvidenceStatus.Success,
            WlanId,
            NetworkAdapterCategory.Wireless);
        ProxyEndpointRouteAnalysisResult proxyRoute = ProxyEvidence(
            ProxyEndpointRouteAnalysisStatus.Success,
            [
                ProxyItem(
                    1,
                    TunnelId,
                    NetworkAdapterCategory.Tunnel,
                    isVirtual: true,
                    isVpn: true)
            ]);

        InternalProxyRouteComparisonResult result =
            InternalProxyRouteComparison.Compare(
                internalRoute,
                proxyRoute,
                WlanId,
                EvaluationTime);

        Ensure(result.Status
               == InternalProxyRouteComparisonStatus.Diverged,
            "Wi-Fi 내부 경로와 VPN 프록시 경로는 Diverged여야 합니다.");
        Ensure(result.IsComparable
               && result.SameLocalInterface == false,
            "Diverged도 근거가 충분한 비교 결과여야 합니다.");
        Ensure(result.InternalInterface?.MatchesExpectedWlan == true
               && result.ProxyInterface?.MatchesExpectedWlan == false,
            "내부는 현재 WLAN, 프록시는 다른 인터페이스여야 합니다.");
        Ensure(result.AnyVirtualInterface
               && result.AnyVpnOrTunnelInterface,
            "VPN 터널 경로의 가상·VPN 표시가 필요합니다.");
        Ensure(result.Warnings.Any(message => message.Contains(
                "VPN 또는 터널",
                StringComparison.Ordinal)),
            "VPN·터널 경로 경고가 필요합니다.");
        Ensure(result.Warnings.Any(message => message.Contains(
                "서로 다른",
                StringComparison.Ordinal)),
            "경로 분기 경고가 필요합니다.");
    }

    private static void MultipleProxyInterfacesAreAmbiguous()
    {
        DestinationRouteEvidence internalRoute = InternalEvidence(
            DestinationRouteEvidenceStatus.Success,
            WlanId,
            NetworkAdapterCategory.Wireless);
        ProxyEndpointRouteAnalysisResult proxyRoute = ProxyEvidence(
            ProxyEndpointRouteAnalysisStatus.MultipleInterfaces,
            [
                ProxyItem(
                    1,
                    WlanId,
                    NetworkAdapterCategory.Wireless,
                    false,
                    false),
                ProxyItem(
                    2,
                    EthernetId,
                    NetworkAdapterCategory.Ethernet,
                    false,
                    false)
            ],
            distinctInterfaceCount: 2);

        InternalProxyRouteComparisonResult result =
            InternalProxyRouteComparison.Compare(
                internalRoute,
                proxyRoute,
                WlanId,
                EvaluationTime);

        Ensure(result.Status
               == InternalProxyRouteComparisonStatus.Ambiguous,
            "프록시 후보가 여러 로컬 NIC로 나뉘면 Ambiguous여야 합니다.");
        Ensure(!result.IsComparable
               && result.SameLocalInterface is null,
            "Ambiguous 결과에 같은 경로 결론을 만들면 안 됩니다.");
        Ensure(result.ProxyInterface is null
               && result.ProxyDistinctInterfaceCount == 2,
            "단일 프록시 인터페이스를 선택하지 않고 실제 수를 유지해야 합니다.");
    }

    private static void InternalMultipleInterfacesAreAmbiguous()
    {
        DestinationRouteEvidence internalRoute = InternalEvidence(
            DestinationRouteEvidenceStatus.MultipleInterfaces,
            interfaceId: null,
            NetworkAdapterCategory.Unknown);
        ProxyEndpointRouteAnalysisResult proxyRoute = ProxyEvidence(
            ProxyEndpointRouteAnalysisStatus.Success,
            [
                ProxyItem(
                    1,
                    WlanId,
                    NetworkAdapterCategory.Wireless,
                    false,
                    false)
            ]);

        InternalProxyRouteComparisonResult result =
            InternalProxyRouteComparison.Compare(
                internalRoute,
                proxyRoute,
                WlanId,
                EvaluationTime);

        Ensure(result.Status
               == InternalProxyRouteComparisonStatus.Ambiguous,
            "내부 대상이 여러 로컬 인터페이스로 나뉘면 Ambiguous여야 합니다.");
        Ensure(result.InternalInterface is null,
            "내부 단일 인터페이스를 임의 선택하면 안 됩니다.");
        Ensure(result.Message.Contains(
                "내부 DIRECT",
                StringComparison.Ordinal),
            "어느 쪽의 근거가 모호한지 설명해야 합니다.");
    }

    private static void DirectAndPartialProxyEvidenceAreIncomplete()
    {
        DestinationRouteEvidence internalRoute = InternalEvidence(
            DestinationRouteEvidenceStatus.Success,
            WlanId,
            NetworkAdapterCategory.Wireless);
        ProxyEndpointRouteAnalysisResult direct = ProxyEvidence(
            ProxyEndpointRouteAnalysisStatus.DirectPathSelected,
            Array.Empty<ProxyEndpointRouteEvidenceItem>(),
            directIsPrimary: true);
        InternalProxyRouteComparisonResult directResult =
            InternalProxyRouteComparison.Compare(
                internalRoute,
                direct,
                WlanId,
                EvaluationTime);

        Ensure(directResult.Status
               == InternalProxyRouteComparisonStatus.Incomplete
               && directResult.ProxyDirectPathSelected
               && directResult.ProxyInterface is null,
            "외부 DIRECT 경로에는 비교할 프록시 엔드포인트가 없어야 합니다.");

        ProxyEndpointRouteAnalysisResult partial = ProxyEvidence(
            ProxyEndpointRouteAnalysisStatus.PartialSuccess,
            [
                ProxyItem(
                    1,
                    WlanId,
                    NetworkAdapterCategory.Wireless,
                    false,
                    false),
                FailedProxyItem(2)
            ]);
        InternalProxyRouteComparisonResult partialResult =
            InternalProxyRouteComparison.Compare(
                internalRoute,
                partial,
                WlanId,
                EvaluationTime);
        Ensure(partialResult.Status
               == InternalProxyRouteComparisonStatus.Incomplete
               && partialResult.ProxyEvidencePartial,
            "일부 fallback 후보가 실패하면 전체 비교는 Incomplete여야 합니다.");
        Ensure(partialResult.SameLocalInterface is null,
            "부분 프록시 근거로 같은 인터페이스 결론을 내리면 안 됩니다.");

        DestinationRouteEvidence failedInternal = InternalEvidence(
            DestinationRouteEvidenceStatus.RouteNotFound,
            interfaceId: null,
            NetworkAdapterCategory.Unknown);
        InternalProxyRouteComparisonResult internalFailure =
            InternalProxyRouteComparison.Compare(
                failedInternal,
                ProxyEvidence(
                    ProxyEndpointRouteAnalysisStatus.Success,
                    [
                        ProxyItem(
                            1,
                            WlanId,
                            NetworkAdapterCategory.Wireless,
                            false,
                            false)
                    ]),
                WlanId,
                EvaluationTime);
        Ensure(internalFailure.Status
               == InternalProxyRouteComparisonStatus.Incomplete,
            "내부 경로가 없으면 비교를 완료하면 안 됩니다.");
    }

    private static void InternalPartialEvidenceCanRemainComparable()
    {
        DestinationRouteEvidence internalRoute = InternalEvidence(
            DestinationRouteEvidenceStatus.PartialSuccess,
            WlanId,
            NetworkAdapterCategory.Wireless);
        ProxyEndpointRouteAnalysisResult proxyRoute = ProxyEvidence(
            ProxyEndpointRouteAnalysisStatus.Success,
            [
                ProxyItem(
                    1,
                    WlanId,
                    NetworkAdapterCategory.Wireless,
                    false,
                    false)
            ]);

        InternalProxyRouteComparisonResult result =
            InternalProxyRouteComparison.Compare(
                internalRoute,
                proxyRoute,
                WlanId,
                EvaluationTime);

        Ensure(result.Status
               == InternalProxyRouteComparisonStatus.Ready
               && result.InternalEvidencePartial,
            "내부 일부 주소군이 실패해도 단일 인터페이스가 확정되면 비교는 가능해야 합니다.");
        Ensure(result.Warnings.Any(message => message.Contains(
                "일부 주소군",
                StringComparison.Ordinal)),
            "내부 부분 성공의 한계를 별도 경고해야 합니다.");
    }

    private static void ConflictingProxyMetadataIsAmbiguous()
    {
        string fingerprint = RouteInterfaceFingerprint.Create(WlanId);
        ProxyEndpointRouteEvidenceItem first = ProxyItem(
            1,
            WlanId,
            NetworkAdapterCategory.Wireless,
            false,
            false);
        ProxyEndpointRouteEvidenceItem conflicting = first with
        {
            Sequence = 2,
            SelectedInterfaceFingerprint = fingerprint,
            SelectedInterfaceCategory =
                NetworkAdapterCategory.Ethernet
        };
        ProxyEndpointRouteAnalysisResult proxyRoute = ProxyEvidence(
            ProxyEndpointRouteAnalysisStatus.Success,
            [first, conflicting],
            distinctInterfaceCount: 1);

        InternalProxyRouteComparisonResult result =
            InternalProxyRouteComparison.Compare(
                InternalEvidence(
                    DestinationRouteEvidenceStatus.Success,
                    WlanId,
                    NetworkAdapterCategory.Wireless),
                proxyRoute,
                WlanId,
                EvaluationTime);

        Ensure(result.Status
               == InternalProxyRouteComparisonStatus.Ambiguous,
            "같은 지문에 충돌하는 범주가 있으면 Ambiguous여야 합니다.");
        Ensure(result.ProxyInterface is null,
            "충돌한 프록시 인터페이스 메타데이터를 하나로 축약하면 안 됩니다.");
        Ensure(result.Warnings.Any(message => message.Contains(
                "메타데이터",
                StringComparison.Ordinal)),
            "메타데이터 충돌 경고가 필요합니다.");
    }

    private static void
        MissingWlanIdentityDoesNotBlockInterfaceComparison()
    {
        InternalProxyRouteComparisonResult result =
            InternalProxyRouteComparison.Compare(
                InternalEvidence(
                    DestinationRouteEvidenceStatus.Success,
                    EthernetId,
                    NetworkAdapterCategory.Ethernet),
                ProxyEvidence(
                    ProxyEndpointRouteAnalysisStatus.Success,
                    [
                        ProxyItem(
                            1,
                            EthernetId,
                            NetworkAdapterCategory.Ethernet,
                            false,
                            false)
                    ]),
                expectedWlanInterfaceId: null,
                EvaluationTime);

        Ensure(result.Status
               == InternalProxyRouteComparisonStatus.Ready
               && result.SameLocalInterface == true,
            "WLAN ID가 없어도 두 안전한 인터페이스 지문은 비교할 수 있어야 합니다.");
        Ensure(result.ExpectedWlanInterfaceFingerprint is null
               && result.InternalInterface?.MatchesExpectedWlan is null
               && result.ProxyInterface?.MatchesExpectedWlan is null,
            "WLAN 일치 여부를 false로 추정하면 안 됩니다.");
        Ensure(result.Warnings.Any(message => message.Contains(
                "GUID를 확인하지 못해",
                StringComparison.Ordinal)),
            "WLAN 상관 미평가 경고가 필요합니다.");
    }

    private static void ResultNeverContainsRawInterfaceIdentityOrNames()
    {
        const string internalName = "Corporate Internal Adapter";
        const string proxyName = "Corporate Proxy Tunnel";
        DestinationRouteEvidence internalRoute = InternalEvidence(
            DestinationRouteEvidenceStatus.Success,
            WlanId,
            NetworkAdapterCategory.Wireless,
            internalName,
            "Private internal interface description");
        ProxyEndpointRouteAnalysisResult proxyRoute = ProxyEvidence(
            ProxyEndpointRouteAnalysisStatus.Success,
            [
                ProxyItem(
                    1,
                    TunnelId,
                    NetworkAdapterCategory.Tunnel,
                    true,
                    true,
                    message: proxyName)
            ]);

        InternalProxyRouteComparisonResult result =
            InternalProxyRouteComparison.Compare(
                internalRoute,
                proxyRoute,
                WlanId,
                EvaluationTime);
        string json = JsonSerializer.Serialize(result);

        foreach (string secret in new[]
                 {
                     WlanId,
                     TunnelId,
                     internalName,
                     "Private internal interface description",
                     proxyName
                 })
        {
            Ensure(!json.Contains(
                    secret,
                    StringComparison.OrdinalIgnoreCase),
                $"비교 결과에 원문 인터페이스 식별값이 남았습니다: {secret}");
        }
        Ensure(json.Contains(
                RouteInterfaceFingerprint.Create(WlanId),
                StringComparison.Ordinal),
            "원문 대신 짧은 인터페이스 지문은 유지해야 합니다.");
    }

    private static DestinationRouteEvidence InternalEvidence(
        DestinationRouteEvidenceStatus status,
        string? interfaceId,
        NetworkAdapterCategory category,
        string displayName = "Synthetic internal adapter",
        string description = "Synthetic internal description")
    {
        RouteInterfaceDescriptor? descriptor = interfaceId is null
            ? null
            : new RouteInterfaceDescriptor(
                InterfaceIdentity: interfaceId,
                DisplayName: displayName,
                Description: description,
                NativeInterfaceType: category.ToString(),
                Category: category,
                OperationalState: NetworkAdapterOperationalState.Up,
                HasDefaultGateway: true,
                IsVirtual: category == NetworkAdapterCategory.Tunnel,
                IsVpn: category == NetworkAdapterCategory.Tunnel);
        RouteAddressEvidenceStatus addressStatus = status switch
        {
            DestinationRouteEvidenceStatus.Success
                or DestinationRouteEvidenceStatus.PartialSuccess =>
                RouteAddressEvidenceStatus.Success,
            DestinationRouteEvidenceStatus.RouteNotFound =>
                RouteAddressEvidenceStatus.RouteNotFound,
            _ => RouteAddressEvidenceStatus.Failed
        };

        return new DestinationRouteEvidence(
            CapturedAt: EvaluationTime,
            TargetLabel: "내부 DIRECT 대상",
            Purpose: RouteProbePurpose.InternalDirectTarget,
            DnsWasUsed: false,
            ResolvedAddressCount: 1,
            Status: status,
            SelectedInterface: descriptor,
            AddressEvidence:
            [
                new RouteAddressEvidence(
                    RouteAddressFamilyKind.IPv4,
                    addressStatus,
                    descriptor,
                    NativeErrorCode: null,
                    Message: "합성 내부 경로 근거")
            ],
            Warnings: Array.Empty<string>(),
            Message: "합성 내부 경로 근거");
    }

    private static ProxyEndpointRouteAnalysisResult ProxyEvidence(
        ProxyEndpointRouteAnalysisStatus status,
        IReadOnlyList<ProxyEndpointRouteEvidenceItem> endpoints,
        int? distinctInterfaceCount = null,
        bool directIsPrimary = false)
    {
        int successful = endpoints.Count(item =>
            item.IsRouteSuccess);
        int distinct = distinctInterfaceCount
            ?? endpoints
                .Where(item => item.IsRouteSuccess)
                .Select(item => item.SelectedInterfaceFingerprint)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();
        return new ProxyEndpointRouteAnalysisResult(
            CapturedAt: EvaluationTime,
            Status: status,
            SourceKind: ProxyEndpointSourceKind.AutoProxyResult,
            ProxyDecision: directIsPrimary
                ? ProxyEndpointDecision.DirectWithProxyAlternatives
                : ProxyEndpointDecision.Proxy,
            TargetScheme: "https",
            DirectPresent: directIsPrimary,
            DirectIsPrimary: directIsPrimary,
            DirectFallback: false,
            DirectSequence: directIsPrimary ? 1 : null,
            ParsedEndpointCount: endpoints.Count,
            ApplicableEndpointCount: endpoints.Count,
            AnalyzedEndpointCount: endpoints.Count,
            SkippedAfterDirectCount: directIsPrimary
                ? endpoints.Count
                : 0,
            SuccessfulEndpointCount: successful,
            DistinctInterfaceCount: distinct,
            Endpoints: endpoints,
            Warnings: Array.Empty<string>(),
            Message: "합성 프록시 경로 근거",
            Limitation: "합성 한계");
    }

    private static ProxyEndpointRouteEvidenceItem ProxyItem(
        int sequence,
        string interfaceId,
        NetworkAdapterCategory category,
        bool isVirtual,
        bool isVpn,
        string message = "합성 프록시 경로") =>
        new(
            Sequence: sequence,
            EndpointLabel:
                $"프록시 후보 {sequence} · host#0123456789 · port 8080",
            HostFingerprint: "0123456789",
            AppliesToScheme: "https",
            Transport: ProxyEndpointTransport.Http,
            Port: 8080,
            RouteStatus: DestinationRouteEvidenceStatus.Success,
            WlanCorrelationStatus:
                interfaceId == WlanId
                    ? RouteWlanCorrelationStatus.Matched
                    : RouteWlanCorrelationStatus.DifferentInterface,
            SelectedInterfaceFingerprint:
                RouteInterfaceFingerprint.Create(interfaceId),
            SelectedInterfaceCategory: category,
            SelectedInterfaceIsVirtual: isVirtual,
            SelectedInterfaceIsVpn: isVpn,
            SelectedInterfaceIsUp: true,
            SelectedInterfaceHasDefaultGateway: true,
            ResolvedAddressCount: 1,
            SuccessfulAddressCount: 1,
            FailedAddressCount: 0,
            Message: message,
            Warnings: Array.Empty<string>());

    private static ProxyEndpointRouteEvidenceItem FailedProxyItem(
        int sequence) =>
        new(
            Sequence: sequence,
            EndpointLabel:
                $"프록시 후보 {sequence} · host#abcdef0123 · port 8080",
            HostFingerprint: "abcdef0123",
            AppliesToScheme: "https",
            Transport: ProxyEndpointTransport.Http,
            Port: 8080,
            RouteStatus: DestinationRouteEvidenceStatus.RouteNotFound,
            WlanCorrelationStatus:
                RouteWlanCorrelationStatus.RouteInterfaceUnavailable,
            SelectedInterfaceFingerprint: null,
            SelectedInterfaceCategory: null,
            SelectedInterfaceIsVirtual: null,
            SelectedInterfaceIsVpn: null,
            SelectedInterfaceIsUp: null,
            SelectedInterfaceHasDefaultGateway: null,
            ResolvedAddressCount: 1,
            SuccessfulAddressCount: 0,
            FailedAddressCount: 1,
            Message: "합성 프록시 경로 실패",
            Warnings: Array.Empty<string>());

    private static void Ensure(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
