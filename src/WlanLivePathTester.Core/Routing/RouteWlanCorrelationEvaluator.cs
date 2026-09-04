using WlanLivePathTester.Core.NetworkEnvironment;

namespace WlanLivePathTester.Core.Routing;

public static class RouteWlanCorrelationEvaluator
{
    public static DestinationRouteEvidence Apply(
        DestinationRouteEvidence routeEvidence,
        string? expectedWlanInterfaceId)
    {
        ArgumentNullException.ThrowIfNull(routeEvidence);

        string? expectedIdentity = NormalizeExactGuid(
            expectedWlanInterfaceId);
        if (expectedIdentity is null)
        {
            const string message =
                "연결된 Native WLAN 인터페이스 ID를 확인하지 못해 라우팅 인터페이스와 정확히 비교하지 않았습니다.";
            return ApplyCorrelation(
                routeEvidence,
                RouteWlanCorrelationStatus.WlanIdentityUnavailable,
                expectedFingerprint: null,
                message,
                addWarning: false);
        }

        string expectedFingerprint =
            RouteInterfaceFingerprint.Create(expectedIdentity);
        if (routeEvidence.SelectedInterface is not RouteInterfaceDescriptor selected)
        {
            const string message =
                "Windows 라우팅 결과가 단일 인터페이스로 확정되지 않아 연결된 WLAN NIC와 비교하지 않았습니다.";
            return ApplyCorrelation(
                routeEvidence,
                RouteWlanCorrelationStatus.RouteInterfaceUnavailable,
                expectedFingerprint,
                message,
                addWarning: false);
        }

        string? routeIdentity = NormalizeExactGuid(
            selected.InterfaceIdentity);
        if (routeIdentity is null)
        {
            const string message =
                "선택된 라우팅 인터페이스의 유효한 GUID를 확인하지 못해 연결된 WLAN NIC와 비교하지 않았습니다.";
            return ApplyCorrelation(
                routeEvidence,
                RouteWlanCorrelationStatus.RouteInterfaceUnavailable,
                expectedFingerprint,
                message,
                addWarning: false);
        }

        if (routeIdentity.Equals(
                expectedIdentity,
                StringComparison.OrdinalIgnoreCase))
        {
            const string message =
                "Windows 최적 라우팅 인터페이스가 현재 연결된 Native WLAN 인터페이스와 일치합니다.";
            return ApplyCorrelation(
                routeEvidence,
                RouteWlanCorrelationStatus.Matched,
                expectedFingerprint,
                message,
                addWarning: false);
        }

        string warning = selected.Category switch
        {
            NetworkAdapterCategory.Ethernet =>
                "Windows 최적 경로가 현재 연결된 Wi-Fi가 아닌 유선 인터페이스를 선택합니다.",
            NetworkAdapterCategory.Tunnel =>
                "Windows 최적 경로가 현재 연결된 Wi-Fi가 아닌 VPN·터널 인터페이스를 선택합니다.",
            NetworkAdapterCategory.Wireless =>
                "Windows 최적 경로가 현재 연결된 Native WLAN과 다른 Wi-Fi 인터페이스를 선택합니다.",
            _ =>
                "Windows 최적 경로가 현재 연결된 Native WLAN과 다른 로컬 인터페이스를 선택합니다."
        };

        return ApplyCorrelation(
            routeEvidence,
            RouteWlanCorrelationStatus.DifferentInterface,
            expectedFingerprint,
            warning,
            addWarning: true);
    }

    private static DestinationRouteEvidence ApplyCorrelation(
        DestinationRouteEvidence routeEvidence,
        RouteWlanCorrelationStatus status,
        string? expectedFingerprint,
        string message,
        bool addWarning)
    {
        IReadOnlyList<string> warnings = addWarning
            ? routeEvidence.Warnings
                .Append(message)
                .Distinct(StringComparer.Ordinal)
                .ToArray()
            : routeEvidence.Warnings;
        string combinedMessage = string.IsNullOrWhiteSpace(
            routeEvidence.Message)
            ? "WLAN NIC 비교: " + message
            : routeEvidence.Message.TrimEnd()
                + " WLAN NIC 비교: "
                + message;

        return routeEvidence with
        {
            WlanCorrelationStatus = status,
            ExpectedWlanInterfaceFingerprint = expectedFingerprint,
            WlanCorrelationMessage = message,
            Warnings = warnings,
            Message = combinedMessage
        };
    }

    private static string? NormalizeExactGuid(string? value)
    {
        string trimmed = (value ?? string.Empty)
            .Trim()
            .Trim('{', '}');
        return Guid.TryParse(trimmed, out Guid parsed)
            ? parsed.ToString("D")
            : null;
    }
}
