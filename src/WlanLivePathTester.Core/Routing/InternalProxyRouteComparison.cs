using WlanLivePathTester.Core.NetworkEnvironment;

namespace WlanLivePathTester.Core.Routing;

public enum InternalProxyRouteComparisonStatus
{
    Ready,
    Incomplete,
    Ambiguous,
    Diverged
}

public sealed record LocalRouteComparisonInterface(
    string InterfaceFingerprint,
    NetworkAdapterCategory Category,
    bool? IsVirtual,
    bool? IsVpn,
    bool? IsUp,
    bool? HasDefaultGateway,
    bool? MatchesExpectedWlan);

public sealed record InternalProxyRouteComparisonResult(
    DateTimeOffset EvaluatedAt,
    InternalProxyRouteComparisonStatus Status,
    DestinationRouteEvidenceStatus InternalRouteStatus,
    ProxyEndpointRouteAnalysisStatus ProxyRouteStatus,
    LocalRouteComparisonInterface? InternalInterface,
    LocalRouteComparisonInterface? ProxyInterface,
    string? ExpectedWlanInterfaceFingerprint,
    bool? SameLocalInterface,
    bool InternalEvidencePartial,
    bool ProxyEvidencePartial,
    bool ProxyDirectPathSelected,
    bool ProxyDirectFallbackPresent,
    int ProxyCandidateCount,
    int ProxySuccessfulCandidateCount,
    int ProxyDistinctInterfaceCount,
    bool AnyVirtualInterface,
    bool AnyVpnOrTunnelInterface,
    IReadOnlyList<string> Warnings,
    string Message,
    string Limitation)
{
    public bool IsComparable =>
        Status is InternalProxyRouteComparisonStatus.Ready
            or InternalProxyRouteComparisonStatus.Diverged;
}

public static class InternalProxyRouteComparison
{
    private const string ComparisonLimitation =
        "이 비교는 현재 PC에서 내부 DIRECT 대상과 프록시 엔드포인트까지 선택되는 Windows 로컬 인터페이스만 비교합니다. 실제 패킷 전달 성공, 프록시 인증·정책·캐시·서버 상태, 프록시 이후 외부 경로와 인터넷 회선 품질은 확인하지 않습니다.";

    public static InternalProxyRouteComparisonResult Compare(
        DestinationRouteEvidence internalRoute,
        ProxyEndpointRouteAnalysisResult proxyRoute,
        string? expectedWlanInterfaceId = null,
        DateTimeOffset? evaluatedAt = null)
    {
        ArgumentNullException.ThrowIfNull(internalRoute);
        ArgumentNullException.ThrowIfNull(proxyRoute);

        string? expectedWlanFingerprint =
            CreateExactGuidFingerprint(expectedWlanInterfaceId);
        LocalRouteComparisonInterface? internalInterface =
            MapInternalInterface(
                internalRoute.SelectedInterface,
                expectedWlanFingerprint);
        ProxyInterfaceResolution proxyResolution =
            ResolveProxyInterface(
                proxyRoute,
                expectedWlanFingerprint);
        LocalRouteComparisonInterface? proxyInterface =
            proxyResolution.Interface;

        bool internalPartial = internalRoute.Status
            == DestinationRouteEvidenceStatus.PartialSuccess;
        bool proxyPartial = proxyRoute.Status
            == ProxyEndpointRouteAnalysisStatus.PartialSuccess
            || proxyRoute.Endpoints.Any(endpoint =>
                endpoint.RouteStatus
                    == DestinationRouteEvidenceStatus.PartialSuccess);
        bool anyVirtual = internalInterface?.IsVirtual == true
            || proxyInterface?.IsVirtual == true
            || proxyRoute.Endpoints.Any(endpoint =>
                endpoint.SelectedInterfaceIsVirtual == true);
        bool anyVpnOrTunnel =
            IsVpnOrTunnel(internalInterface)
            || IsVpnOrTunnel(proxyInterface)
            || proxyRoute.Endpoints.Any(endpoint =>
                endpoint.SelectedInterfaceIsVpn == true
                || endpoint.SelectedInterfaceCategory
                    == NetworkAdapterCategory.Tunnel);

        InternalProxyRouteComparisonStatus status;
        bool? sameInterface = null;
        string message;

        if (IsInternalAmbiguous(internalRoute)
            || IsProxyAmbiguous(proxyRoute, proxyResolution))
        {
            status = InternalProxyRouteComparisonStatus.Ambiguous;
            message = BuildAmbiguousMessage(
                internalRoute,
                proxyRoute,
                proxyResolution);
        }
        else if (!IsInternalUsable(
                     internalRoute,
                     internalInterface)
                 || !IsProxyUsable(
                     proxyRoute,
                     proxyResolution))
        {
            status = InternalProxyRouteComparisonStatus.Incomplete;
            message = BuildIncompleteMessage(
                internalRoute,
                proxyRoute,
                internalInterface,
                proxyResolution);
        }
        else
        {
            sameInterface = string.Equals(
                internalInterface!.InterfaceFingerprint,
                proxyInterface!.InterfaceFingerprint,
                StringComparison.OrdinalIgnoreCase);
            status = sameInterface.Value
                ? InternalProxyRouteComparisonStatus.Ready
                : InternalProxyRouteComparisonStatus.Diverged;
            message = sameInterface.Value
                ? "내부 DIRECT 대상과 분석된 모든 프록시 후보가 같은 Windows 로컬 인터페이스 지문을 사용합니다."
                : "내부 DIRECT 대상과 프록시 엔드포인트가 서로 다른 Windows 로컬 인터페이스 지문을 사용합니다.";
        }

        IReadOnlyList<string> warnings = BuildWarnings(
            internalRoute,
            proxyRoute,
            internalInterface,
            proxyInterface,
            expectedWlanFingerprint,
            internalPartial,
            proxyPartial,
            anyVirtual,
            anyVpnOrTunnel,
            status,
            proxyResolution);

        return new InternalProxyRouteComparisonResult(
            EvaluatedAt: evaluatedAt ?? DateTimeOffset.UtcNow,
            Status: status,
            InternalRouteStatus: internalRoute.Status,
            ProxyRouteStatus: proxyRoute.Status,
            InternalInterface: internalInterface,
            ProxyInterface: proxyInterface,
            ExpectedWlanInterfaceFingerprint:
                expectedWlanFingerprint,
            SameLocalInterface: sameInterface,
            InternalEvidencePartial: internalPartial,
            ProxyEvidencePartial: proxyPartial,
            ProxyDirectPathSelected: proxyRoute.Status
                == ProxyEndpointRouteAnalysisStatus.DirectPathSelected,
            ProxyDirectFallbackPresent:
                proxyRoute.DirectFallback,
            ProxyCandidateCount:
                Math.Max(0, proxyRoute.AnalyzedEndpointCount),
            ProxySuccessfulCandidateCount:
                Math.Max(0, proxyRoute.SuccessfulEndpointCount),
            ProxyDistinctInterfaceCount:
                Math.Max(0, proxyRoute.DistinctInterfaceCount),
            AnyVirtualInterface: anyVirtual,
            AnyVpnOrTunnelInterface: anyVpnOrTunnel,
            Warnings: warnings,
            Message: message,
            Limitation: ComparisonLimitation);
    }

    private static LocalRouteComparisonInterface?
        MapInternalInterface(
            RouteInterfaceDescriptor? descriptor,
            string? expectedWlanFingerprint)
    {
        if (descriptor is null
            || string.Equals(
                descriptor.IdentityFingerprint,
                "없음",
                StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return new LocalRouteComparisonInterface(
            InterfaceFingerprint: descriptor.IdentityFingerprint,
            Category: descriptor.Category,
            IsVirtual: descriptor.IsVirtual,
            IsVpn: descriptor.IsVpn,
            IsUp: descriptor.IsUp,
            HasDefaultGateway: descriptor.HasDefaultGateway,
            MatchesExpectedWlan: expectedWlanFingerprint is null
                ? null
                : string.Equals(
                    descriptor.IdentityFingerprint,
                    expectedWlanFingerprint,
                    StringComparison.OrdinalIgnoreCase));
    }

    private static ProxyInterfaceResolution ResolveProxyInterface(
        ProxyEndpointRouteAnalysisResult proxyRoute,
        string? expectedWlanFingerprint)
    {
        ProxyEndpointRouteEvidenceItem[] successful =
            proxyRoute.Endpoints
                .Where(endpoint => endpoint.IsRouteSuccess)
                .ToArray();
        ProxyEndpointRouteEvidenceItem[] identified = successful
            .Where(endpoint =>
                !string.IsNullOrWhiteSpace(
                    endpoint.SelectedInterfaceFingerprint)
                && !string.Equals(
                    endpoint.SelectedInterfaceFingerprint,
                    "없음",
                    StringComparison.OrdinalIgnoreCase))
            .ToArray();
        string[] fingerprints = identified
            .Select(endpoint =>
                endpoint.SelectedInterfaceFingerprint!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (successful.Length == 0)
        {
            return new ProxyInterfaceResolution(
                Interface: null,
                SuccessfulEndpointCount: 0,
                IdentifiedEndpointCount: 0,
                DistinctFingerprintCount: 0,
                MetadataConflict: false);
        }

        if (fingerprints.Length != 1
            || identified.Length != successful.Length)
        {
            return new ProxyInterfaceResolution(
                Interface: null,
                SuccessfulEndpointCount: successful.Length,
                IdentifiedEndpointCount: identified.Length,
                DistinctFingerprintCount: fingerprints.Length,
                MetadataConflict: false);
        }

        NetworkAdapterCategory?[] categories = identified
            .Select(endpoint => endpoint.SelectedInterfaceCategory)
            .Distinct()
            .ToArray();
        bool metadataConflict = categories.Length != 1
            || !categories[0].HasValue
            || HasBooleanConflict(
                identified.Select(endpoint =>
                    endpoint.SelectedInterfaceIsVirtual))
            || HasBooleanConflict(
                identified.Select(endpoint =>
                    endpoint.SelectedInterfaceIsVpn))
            || HasBooleanConflict(
                identified.Select(endpoint =>
                    endpoint.SelectedInterfaceIsUp))
            || HasBooleanConflict(
                identified.Select(endpoint =>
                    endpoint.SelectedInterfaceHasDefaultGateway));
        if (metadataConflict)
        {
            return new ProxyInterfaceResolution(
                Interface: null,
                SuccessfulEndpointCount: successful.Length,
                IdentifiedEndpointCount: identified.Length,
                DistinctFingerprintCount: fingerprints.Length,
                MetadataConflict: true);
        }

        ProxyEndpointRouteEvidenceItem first = identified[0];
        string fingerprint = fingerprints[0];
        return new ProxyInterfaceResolution(
            Interface: new LocalRouteComparisonInterface(
                InterfaceFingerprint: fingerprint,
                Category: categories[0]!.Value,
                IsVirtual: first.SelectedInterfaceIsVirtual,
                IsVpn: first.SelectedInterfaceIsVpn,
                IsUp: first.SelectedInterfaceIsUp,
                HasDefaultGateway:
                    first.SelectedInterfaceHasDefaultGateway,
                MatchesExpectedWlan: expectedWlanFingerprint is null
                    ? null
                    : string.Equals(
                        fingerprint,
                        expectedWlanFingerprint,
                        StringComparison.OrdinalIgnoreCase)),
            SuccessfulEndpointCount: successful.Length,
            IdentifiedEndpointCount: identified.Length,
            DistinctFingerprintCount: 1,
            MetadataConflict: false);
    }

    private static bool IsInternalAmbiguous(
        DestinationRouteEvidence internalRoute) =>
        internalRoute.Status
            == DestinationRouteEvidenceStatus.MultipleInterfaces;

    private static bool IsProxyAmbiguous(
        ProxyEndpointRouteAnalysisResult proxyRoute,
        ProxyInterfaceResolution proxyResolution) =>
        proxyRoute.Status
            == ProxyEndpointRouteAnalysisStatus.MultipleInterfaces
        || proxyRoute.DistinctInterfaceCount > 1
        || proxyResolution.DistinctFingerprintCount > 1
        || proxyResolution.MetadataConflict;

    private static bool IsInternalUsable(
        DestinationRouteEvidence internalRoute,
        LocalRouteComparisonInterface? internalInterface) =>
        internalRoute.IsSuccess
        && internalInterface is not null;

    private static bool IsProxyUsable(
        ProxyEndpointRouteAnalysisResult proxyRoute,
        ProxyInterfaceResolution proxyResolution) =>
        proxyRoute.Status == ProxyEndpointRouteAnalysisStatus.Success
        && proxyRoute.AnalyzedEndpointCount > 0
        && proxyRoute.SuccessfulEndpointCount
            == proxyRoute.AnalyzedEndpointCount
        && proxyResolution.Interface is not null
        && proxyResolution.SuccessfulEndpointCount
            == proxyRoute.SuccessfulEndpointCount
        && proxyResolution.IdentifiedEndpointCount
            == proxyResolution.SuccessfulEndpointCount;

    private static string BuildAmbiguousMessage(
        DestinationRouteEvidence internalRoute,
        ProxyEndpointRouteAnalysisResult proxyRoute,
        ProxyInterfaceResolution proxyResolution)
    {
        if (internalRoute.Status
            == DestinationRouteEvidenceStatus.MultipleInterfaces)
        {
            return "내부 DIRECT 대상의 IPv4·IPv6 또는 주소별 Windows 최적 경로가 여러 로컬 인터페이스로 나뉘어 단일 비교 기준을 정할 수 없습니다.";
        }

        if (proxyResolution.MetadataConflict)
        {
            return "같은 프록시 인터페이스 지문에 서로 다른 범주 또는 VPN·가상 상태가 기록돼 비교 근거가 모호합니다.";
        }

        return proxyRoute.DistinctInterfaceCount > 1
               || proxyResolution.DistinctFingerprintCount > 1
            ? "프록시 후보들이 서로 다른 Windows 로컬 인터페이스를 사용해 내부 DIRECT 경로와 하나의 기준으로 비교할 수 없습니다."
            : "내부 DIRECT 또는 프록시 경로가 여러 인터페이스로 해석돼 비교 근거가 모호합니다.";
    }

    private static string BuildIncompleteMessage(
        DestinationRouteEvidence internalRoute,
        ProxyEndpointRouteAnalysisResult proxyRoute,
        LocalRouteComparisonInterface? internalInterface,
        ProxyInterfaceResolution proxyResolution)
    {
        if (!internalRoute.IsSuccess
            || internalInterface is null)
        {
            return "내부 DIRECT 대상의 단일 Windows 로컬 인터페이스를 확인하지 못해 프록시 경로와 비교하지 않았습니다.";
        }

        if (proxyRoute.Status
            == ProxyEndpointRouteAnalysisStatus.DirectPathSelected)
        {
            return "외부 대상에서 DIRECT가 첫 경로이므로 비교할 프록시 엔드포인트 로컬 경로가 없습니다.";
        }

        if (proxyRoute.Status
            == ProxyEndpointRouteAnalysisStatus.NoApplicableEndpoint)
        {
            return "현재 외부 대상에 적용되는 프록시 엔드포인트가 없어 내부 DIRECT 경로와 비교하지 않았습니다.";
        }

        if (proxyRoute.Status
            == ProxyEndpointRouteAnalysisStatus.PartialSuccess)
        {
            return "일부 프록시 후보의 로컬 경로를 확인하지 못해 fallback 전체 경로를 확정할 수 없습니다.";
        }

        if (proxyResolution.SuccessfulEndpointCount > 0
            && proxyResolution.IdentifiedEndpointCount
                < proxyResolution.SuccessfulEndpointCount)
        {
            return "성공한 프록시 후보 중 선택 로컬 인터페이스 지문이 없는 결과가 있어 비교를 완료하지 않았습니다.";
        }

        return "프록시 엔드포인트의 단일 Windows 로컬 인터페이스를 확인하지 못해 내부 DIRECT 경로와 비교하지 않았습니다.";
    }

    private static IReadOnlyList<string> BuildWarnings(
        DestinationRouteEvidence internalRoute,
        ProxyEndpointRouteAnalysisResult proxyRoute,
        LocalRouteComparisonInterface? internalInterface,
        LocalRouteComparisonInterface? proxyInterface,
        string? expectedWlanFingerprint,
        bool internalPartial,
        bool proxyPartial,
        bool anyVirtual,
        bool anyVpnOrTunnel,
        InternalProxyRouteComparisonStatus status,
        ProxyInterfaceResolution proxyResolution)
    {
        List<string> warnings = [];
        if (internalPartial)
        {
            warnings.Add(
                "내부 DIRECT 대상의 일부 주소군만 경로 확인에 성공했습니다. 단일 인터페이스는 확인됐지만 주소군 전체 성공은 아닙니다.");
        }

        if (proxyPartial)
        {
            warnings.Add(
                "프록시 엔드포인트 중 일부 주소 또는 후보의 경로 근거가 부분 성공입니다.");
        }

        if (proxyRoute.DirectFallback)
        {
            warnings.Add(
                "프록시 후보 뒤에 DIRECT fallback이 있습니다. 이 비교는 프록시 연결 실패와 실제 DIRECT 전환을 시험하지 않습니다.");
        }

        if (anyVpnOrTunnel)
        {
            warnings.Add(
                "비교 경로 중 VPN 또는 터널 인터페이스가 포함돼 Windows 라우팅·보안 에이전트 정책의 영향을 받을 수 있습니다.");
        }

        if (anyVirtual)
        {
            warnings.Add(
                "비교 경로 중 가상 인터페이스가 포함돼 물리 Wi-Fi 경로와 직접 동일시하면 안 됩니다.");
        }

        if (expectedWlanFingerprint is null)
        {
            warnings.Add(
                "현재 Native WLAN 인터페이스 GUID를 확인하지 못해 내부·프록시 경로의 WLAN 일치 여부를 판정하지 않았습니다.");
        }
        else
        {
            if (internalInterface?.MatchesExpectedWlan == false)
            {
                warnings.Add(
                    "내부 DIRECT 대상의 Windows 최적 경로가 현재 연결된 Native WLAN과 다른 인터페이스입니다.");
            }

            if (proxyInterface?.MatchesExpectedWlan == false)
            {
                warnings.Add(
                    "프록시 엔드포인트의 Windows 최적 경로가 현재 연결된 Native WLAN과 다른 인터페이스입니다.");
            }
        }

        if (status == InternalProxyRouteComparisonStatus.Diverged)
        {
            warnings.Add(
                "내부 DIRECT와 프록시 로컬 경로가 다릅니다. 유선·무선 우선순위, VPN·터널, 정적 경로와 인터페이스 메트릭을 확인해야 합니다.");
        }
        else if (status
                 == InternalProxyRouteComparisonStatus.Ambiguous)
        {
            warnings.Add(
                "여러 로컬 인터페이스 또는 충돌하는 인터페이스 메타데이터 때문에 단일 경로 비교 결론을 내리지 않았습니다.");
        }
        else if (status
                 == InternalProxyRouteComparisonStatus.Incomplete)
        {
            warnings.Add(
                "필수 내부 또는 프록시 경로 근거가 부족해 두 경로가 같거나 다르다고 결론 내리지 않았습니다.");
        }

        if (proxyResolution.MetadataConflict)
        {
            warnings.Add(
                "같은 프록시 인터페이스 지문에서 범주·VPN·가상·Up·게이트웨이 메타데이터가 일치하지 않습니다.");
        }

        return warnings.Distinct(StringComparer.Ordinal).ToArray();
    }

    private static bool IsVpnOrTunnel(
        LocalRouteComparisonInterface? routeInterface) =>
        routeInterface?.IsVpn == true
        || routeInterface?.Category
            == NetworkAdapterCategory.Tunnel;

    private static bool HasBooleanConflict(
        IEnumerable<bool?> values) =>
        values.Distinct().Count() > 1;

    private static string? CreateExactGuidFingerprint(
        string? interfaceId)
    {
        string candidate = (interfaceId ?? string.Empty)
            .Trim()
            .Trim('{', '}');
        return Guid.TryParse(candidate, out Guid parsed)
            ? RouteInterfaceFingerprint.Create(
                parsed.ToString("D"))
            : null;
    }

    private sealed record ProxyInterfaceResolution(
        LocalRouteComparisonInterface? Interface,
        int SuccessfulEndpointCount,
        int IdentifiedEndpointCount,
        int DistinctFingerprintCount,
        bool MetadataConflict);
}
