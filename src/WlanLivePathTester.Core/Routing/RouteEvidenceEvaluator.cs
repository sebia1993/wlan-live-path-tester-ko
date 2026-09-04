using WlanLivePathTester.Core.NetworkEnvironment;

namespace WlanLivePathTester.Core.Routing;

public static class RouteEvidenceEvaluator
{
    public static DestinationRouteEvidence Evaluate(
        DateTimeOffset capturedAt,
        string targetLabel,
        RouteProbePurpose purpose,
        bool dnsWasUsed,
        int resolvedAddressCount,
        IReadOnlyList<RouteAddressEvidence> addressEvidence)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetLabel);
        ArgumentNullException.ThrowIfNull(addressEvidence);

        RouteAddressEvidence[] successful = addressEvidence
            .Where(item =>
                item.Status == RouteAddressEvidenceStatus.Success
                && item.Interface is not null)
            .ToArray();
        List<string> warnings = BuildPurposeWarnings(purpose);

        if (successful.Length == 0)
        {
            DestinationRouteEvidenceStatus status = addressEvidence.Any(item =>
                item.Status == RouteAddressEvidenceStatus.InterfaceAmbiguous)
                ? DestinationRouteEvidenceStatus.MultipleInterfaces
                : DestinationRouteEvidenceStatus.RouteNotFound;

            return new DestinationRouteEvidence(
                CapturedAt: capturedAt,
                TargetLabel: targetLabel,
                Purpose: purpose,
                DnsWasUsed: dnsWasUsed,
                ResolvedAddressCount: resolvedAddressCount,
                Status: status,
                SelectedInterface: null,
                AddressEvidence: addressEvidence,
                Warnings: warnings,
                Message: status == DestinationRouteEvidenceStatus.MultipleInterfaces
                    ? "라우팅 결과에 중복 인터페이스 후보가 있어 하나를 확정하지 않았습니다."
                    : "해석 가능한 Windows 최적 인터페이스 경로를 확인하지 못했습니다.");
        }

        RouteInterfaceDescriptor[] distinctInterfaces = successful
            .Select(item => item.Interface!)
            .GroupBy(
                item => RouteInterfaceFingerprint.Normalize(
                    item.InterfaceIdentity),
                StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();

        if (distinctInterfaces.Length > 1)
        {
            warnings.Add(
                "같은 호스트의 IPv4·IPv6 또는 복수 DNS 응답이 서로 다른 로컬 인터페이스를 선택합니다.");
            warnings.Add(
                "실제 연결 주소가 정해지기 전에는 단일 Wi-Fi·유선·VPN 경로로 단정하지 마십시오.");
            return new DestinationRouteEvidence(
                CapturedAt: capturedAt,
                TargetLabel: targetLabel,
                Purpose: purpose,
                DnsWasUsed: dnsWasUsed,
                ResolvedAddressCount: resolvedAddressCount,
                Status: DestinationRouteEvidenceStatus.MultipleInterfaces,
                SelectedInterface: null,
                AddressEvidence: addressEvidence,
                Warnings: warnings,
                Message: $"성공한 라우팅 결과가 서로 다른 로컬 인터페이스 {distinctInterfaces.Length}개로 나뉩니다.");
        }

        RouteInterfaceDescriptor selected = distinctInterfaces[0];
        AddInterfaceWarnings(selected, purpose, warnings);
        bool hasFailure = addressEvidence.Any(item =>
            item.Status != RouteAddressEvidenceStatus.Success);
        DestinationRouteEvidenceStatus aggregateStatus = hasFailure
            ? DestinationRouteEvidenceStatus.PartialSuccess
            : DestinationRouteEvidenceStatus.Success;

        return new DestinationRouteEvidence(
            CapturedAt: capturedAt,
            TargetLabel: targetLabel,
            Purpose: purpose,
            DnsWasUsed: dnsWasUsed,
            ResolvedAddressCount: resolvedAddressCount,
            Status: aggregateStatus,
            SelectedInterface: selected,
            AddressEvidence: addressEvidence,
            Warnings: warnings,
            Message: aggregateStatus == DestinationRouteEvidenceStatus.Success
                ? "확인한 모든 주소 계열이 같은 Windows 최적 인터페이스를 선택합니다."
                : "일부 주소의 경로는 확인하지 못했지만 성공한 결과는 같은 인터페이스를 선택합니다.");
    }

    public static DestinationRouteEvidence Invalid(
        string targetLabel,
        RouteProbePurpose purpose,
        string message) =>
        new(
            CapturedAt: DateTimeOffset.UtcNow,
            TargetLabel: targetLabel,
            Purpose: purpose,
            DnsWasUsed: false,
            ResolvedAddressCount: 0,
            Status: DestinationRouteEvidenceStatus.InvalidTarget,
            SelectedInterface: null,
            AddressEvidence: Array.Empty<RouteAddressEvidence>(),
            Warnings: BuildPurposeWarnings(purpose),
            Message: message);

    public static DestinationRouteEvidence ResolutionFailed(
        string targetLabel,
        RouteProbePurpose purpose,
        bool dnsWasUsed,
        string message) =>
        new(
            CapturedAt: DateTimeOffset.UtcNow,
            TargetLabel: targetLabel,
            Purpose: purpose,
            DnsWasUsed: dnsWasUsed,
            ResolvedAddressCount: 0,
            Status: DestinationRouteEvidenceStatus.ResolutionFailed,
            SelectedInterface: null,
            AddressEvidence: Array.Empty<RouteAddressEvidence>(),
            Warnings: BuildPurposeWarnings(purpose),
            Message: message);

    public static DestinationRouteEvidence Canceled(
        string targetLabel,
        RouteProbePurpose purpose,
        bool dnsWasUsed) =>
        new(
            CapturedAt: DateTimeOffset.UtcNow,
            TargetLabel: targetLabel,
            Purpose: purpose,
            DnsWasUsed: dnsWasUsed,
            ResolvedAddressCount: 0,
            Status: DestinationRouteEvidenceStatus.Canceled,
            SelectedInterface: null,
            AddressEvidence: Array.Empty<RouteAddressEvidence>(),
            Warnings: BuildPurposeWarnings(purpose),
            Message: "사용자 요청으로 라우팅 근거 확인을 중단했습니다.");

    private static List<string> BuildPurposeWarnings(
        RouteProbePurpose purpose)
    {
        List<string> warnings = [];
        if (purpose == RouteProbePurpose.ExternalTargetReference)
        {
            warnings.Add(
                "회사 프록시 환경에서 외부 사이트 주소의 최적 인터페이스는 실제 HTTP 연결 경로를 나타내지 않을 수 있습니다.");
            warnings.Add(
                "실제 외부 측정의 로컬 연결 대상은 PAC·WPAD 또는 수동 설정이 선택한 프록시 엔드포인트일 수 있습니다.");
        }
        else if (purpose == RouteProbePurpose.ProxyEndpoint)
        {
            warnings.Add(
                "이 결과는 PC에서 프록시 엔드포인트까지의 로컬 인터페이스 선택 근거이며 프록시 이후 인터넷 구간은 포함하지 않습니다.");
        }

        return warnings;
    }

    private static void AddInterfaceWarnings(
        RouteInterfaceDescriptor selected,
        RouteProbePurpose purpose,
        ICollection<string> warnings)
    {
        if (!selected.IsUp)
        {
            warnings.Add(
                "Windows가 선택한 인터페이스가 현재 Up 상태로 확인되지 않았습니다.");
        }

        if (selected.IsVpn)
        {
            warnings.Add(
                "Windows 최적 경로가 VPN 또는 터널 후보 인터페이스를 선택합니다.");
        }

        if (selected.IsVirtual)
        {
            warnings.Add(
                "Windows 최적 경로가 가상 인터페이스로 분류된 항목을 선택합니다.");
        }

        if (purpose == RouteProbePurpose.InternalDirectTarget
            && selected.Category != NetworkAdapterCategory.Wireless)
        {
            warnings.Add(
                "내부 DIRECT 대상의 Windows 최적 경로가 물리 Wi-Fi 범주가 아닙니다. 무선 성능 측정과 실제 요청 경로가 다를 수 있습니다.");
        }

        if (!selected.HasDefaultGateway
            && purpose is RouteProbePurpose.ProxyEndpoint
                or RouteProbePurpose.ExternalTargetReference)
        {
            warnings.Add(
                "선택된 인터페이스에서 기본 게이트웨이가 확인되지 않았습니다. 더 구체적인 정적 경로나 VPN 정책이 사용될 수 있습니다.");
        }
    }
}
