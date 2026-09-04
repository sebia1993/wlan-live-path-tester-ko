namespace WlanLivePathTester.Core.Routing;

public static class RouteWlanCorrelationEvaluator
{
    public static DestinationRouteEvidence Apply(
        DestinationRouteEvidence routeEvidence,
        string? expectedWlanInterfaceId)
    {
        ArgumentNullException.ThrowIfNull(routeEvidence);

        string expectedIdentity = RouteInterfaceFingerprint.Normalize(
            expectedWlanInterfaceId);
        if (!IsValidIdentity(expectedIdentity))
        {
            return routeEvidence with
            {
                WlanCorrelationStatus =
                    RouteWlanCorrelationStatus.WlanIdentityUnavailable,
                ExpectedWlanInterfaceFingerprint = null,
                WlanCorrelationMessage =
                    "연결된 Native WLAN 인터페이스 ID를 확인하지 못해 라우팅 인터페이스와 정확히 비교하지 않았습니다."
            };
        }

        string expectedFingerprint =
            RouteInterfaceFingerprint.Create(expectedIdentity);
        if (routeEvidence.SelectedInterface is not RouteInterfaceDescriptor selected)
        {
            return routeEvidence with
            {
                WlanCorrelationStatus =
                    RouteWlanCorrelationStatus.RouteInterfaceUnavailable,
                ExpectedWlanInterfaceFingerprint = expectedFingerprint,
                WlanCorrelationMessage =
                    "Windows 라우팅 결과가 단일 인터페이스로 확정되지 않아 연결된 WLAN NIC와 비교하지 않았습니다."
            };
        }

        string routeIdentity = RouteInterfaceFingerprint.Normalize(
            selected.InterfaceIdentity);
        if (!IsValidIdentity(routeIdentity))
        {
            return routeEvidence with
            {
                WlanCorrelationStatus =
                    RouteWlanCorrelationStatus.RouteInterfaceUnavailable,
                ExpectedWlanInterfaceFingerprint = expectedFingerprint,
                WlanCorrelationMessage =
                    "선택된 라우팅 인터페이스의 유효한 ID를 확인하지 못해 연결된 WLAN NIC와 비교하지 않았습니다."
            };
        }

        if (routeIdentity.Equals(
                expectedIdentity,
                StringComparison.OrdinalIgnoreCase))
        {
            return routeEvidence with
            {
                WlanCorrelationStatus = RouteWlanCorrelationStatus.Matched,
                ExpectedWlanInterfaceFingerprint = expectedFingerprint,
                WlanCorrelationMessage =
                    "Windows 최적 라우팅 인터페이스가 현재 연결된 Native WLAN 인터페이스와 일치합니다."
            };
        }

        string warning = selected.Category switch
        {
            NetworkEnvironment.NetworkAdapterCategory.Ethernet =>
                "Windows 최적 경로가 현재 연결된 Wi-Fi가 아닌 유선 인터페이스를 선택합니다.",
            NetworkEnvironment.NetworkAdapterCategory.Tunnel =>
                "Windows 최적 경로가 현재 연결된 Wi-Fi가 아닌 VPN·터널 인터페이스를 선택합니다.",
            NetworkEnvironment.NetworkAdapterCategory.Wireless =>
                "Windows 최적 경로가 현재 연결된 Native WLAN과 다른 Wi-Fi 인터페이스를 선택합니다.",
            _ =>
                "Windows 최적 경로가 현재 연결된 Native WLAN과 다른 로컬 인터페이스를 선택합니다."
        };

        return routeEvidence with
        {
            WlanCorrelationStatus =
                RouteWlanCorrelationStatus.DifferentInterface,
            ExpectedWlanInterfaceFingerprint = expectedFingerprint,
            WlanCorrelationMessage = warning,
            Warnings = routeEvidence.Warnings
                .Append(warning)
                .Distinct(StringComparer.Ordinal)
                .ToArray()
        };
    }

    private static bool IsValidIdentity(string value) =>
        !string.IsNullOrWhiteSpace(value)
        && !value.Equals(
            "interface-id-unavailable",
            StringComparison.OrdinalIgnoreCase)
        && !value.StartsWith(
            "local-",
            StringComparison.OrdinalIgnoreCase)
        && !value.StartsWith(
            "duplicate-",
            StringComparison.OrdinalIgnoreCase);
}
