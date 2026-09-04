using System.Runtime.CompilerServices;
using WlanLivePathTester.Core.NetworkEnvironment;
using WlanLivePathTester.Core.Routing;

namespace WlanLivePathTester.SelfTest;

internal static class RoutePathComparisonEvaluatorTests
{
#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Run()
    {
        ReportsReadyWhenInternalAndProxyShareConnectedWlan();
        ReportsDivergedWhenProxyUsesVpn();
        ReportsIncompleteWhenProxyEvidenceIsMissing();
        ReportsAmbiguousForMultipleInterfaces();
        UsesMostRecentPurposeResult();
        DoesNotExposeRawInterfaceIdentityInComparisonPoints();
        Console.WriteLine("PASS internal and proxy route comparison tests");
    }

    private static void ReportsReadyWhenInternalAndProxyShareConnectedWlan()
    {
        const string id =
            "A1B2C3D4-E5F6-47A8-9123-1234567890AB";
        RoutePathComparisonResult result =
            RoutePathComparisonEvaluator.Evaluate(
            [
                Route(
                    RouteProbePurpose.InternalDirectTarget,
                    id,
                    NetworkAdapterCategory.Wireless,
                    RouteWlanCorrelationStatus.Matched,
                    DateTimeOffset.UnixEpoch.AddSeconds(1)),
                Route(
                    RouteProbePurpose.ProxyEndpoint,
                    id,
                    NetworkAdapterCategory.Wireless,
                    RouteWlanCorrelationStatus.Matched,
                    DateTimeOffset.UnixEpoch.AddSeconds(2))
            ],
            DateTimeOffset.UnixEpoch.AddSeconds(3));

        Ensure(result.Status == RoutePathComparisonStatus.Ready,
            "내부·프록시 경로가 같은 연결 WLAN이면 Ready여야 합니다.");
        Ensure(result.Findings.Any(finding =>
                finding.Code == "INTERNAL_AND_PROXY_SHARE_INTERFACE"),
            "같은 로컬 인터페이스 사용 Finding이 필요합니다.");
        Ensure(result.InternalDirect?.InterfaceFingerprint
               == result.ProxyEndpoint?.InterfaceFingerprint,
            "같은 ID의 내부·프록시 지문이 같아야 합니다.");
    }

    private static void ReportsDivergedWhenProxyUsesVpn()
    {
        RoutePathComparisonResult result =
            RoutePathComparisonEvaluator.Evaluate(
            [
                Route(
                    RouteProbePurpose.InternalDirectTarget,
                    "11111111-1111-1111-1111-111111111111",
                    NetworkAdapterCategory.Wireless,
                    RouteWlanCorrelationStatus.Matched,
                    DateTimeOffset.UnixEpoch),
                Route(
                    RouteProbePurpose.ProxyEndpoint,
                    "22222222-2222-2222-2222-222222222222",
                    NetworkAdapterCategory.Tunnel,
                    RouteWlanCorrelationStatus.DifferentInterface,
                    DateTimeOffset.UnixEpoch.AddSeconds(1),
                    isVpn: true,
                    isVirtual: true)
            ]);

        Ensure(result.Status == RoutePathComparisonStatus.Diverged,
            "프록시가 다른 VPN 인터페이스를 사용하면 Diverged여야 합니다.");
        Ensure(result.Findings.Any(finding =>
                finding.Code == "PROXY_DIFFERS_FROM_CONNECTED_WLAN"),
            "프록시-WLAN 불일치 Finding이 필요합니다.");
        Ensure(result.Findings.Any(finding =>
                finding.Code == "PROXY_USES_VPN_OR_TUNNEL"),
            "VPN·터널 사용 Finding이 필요합니다.");
        Ensure(result.Findings.Any(finding =>
                finding.Code
                    == "INTERNAL_AND_PROXY_USE_DIFFERENT_INTERFACES"),
            "내부·프록시 인터페이스 분리 Finding이 필요합니다.");
    }

    private static void ReportsIncompleteWhenProxyEvidenceIsMissing()
    {
        RoutePathComparisonResult result =
            RoutePathComparisonEvaluator.Evaluate(
            [
                Route(
                    RouteProbePurpose.InternalDirectTarget,
                    "33333333-3333-3333-3333-333333333333",
                    NetworkAdapterCategory.Wireless,
                    RouteWlanCorrelationStatus.Matched,
                    DateTimeOffset.UnixEpoch),
                Route(
                    RouteProbePurpose.ExternalTargetReference,
                    "33333333-3333-3333-3333-333333333333",
                    NetworkAdapterCategory.Wireless,
                    RouteWlanCorrelationStatus.Matched,
                    DateTimeOffset.UnixEpoch.AddSeconds(1))
            ]);

        Ensure(result.Status == RoutePathComparisonStatus.Incomplete,
            "프록시 엔드포인트 근거가 없으면 Incomplete여야 합니다.");
        Ensure(result.Findings.Any(finding =>
                finding.Code == "PROXY_ROUTE_NOT_MEASURED"),
            "프록시 경로 미측정 Finding이 필요합니다.");
        Ensure(result.Findings.Any(finding =>
                finding.Code == "EXTERNAL_REFERENCE_IS_NOT_PROXY_PATH"),
            "외부 참고 경로가 실제 프록시 경로가 아니라는 Finding이 필요합니다.");
    }

    private static void ReportsAmbiguousForMultipleInterfaces()
    {
        DestinationRouteEvidence ambiguous = new(
            CapturedAt: DateTimeOffset.UnixEpoch,
            TargetLabel: "내부 복수 경로",
            Purpose: RouteProbePurpose.InternalDirectTarget,
            DnsWasUsed: true,
            ResolvedAddressCount: 2,
            Status: DestinationRouteEvidenceStatus.MultipleInterfaces,
            SelectedInterface: null,
            AddressEvidence: Array.Empty<RouteAddressEvidence>(),
            Warnings: Array.Empty<string>(),
            Message: "합성 복수 경로",
            WlanCorrelationStatus:
                RouteWlanCorrelationStatus.RouteInterfaceUnavailable);

        RoutePathComparisonResult result =
            RoutePathComparisonEvaluator.Evaluate([ambiguous]);

        Ensure(result.Status == RoutePathComparisonStatus.Ambiguous,
            "복수 인터페이스 내부 경로는 Ambiguous여야 합니다.");
        Ensure(result.Findings.Any(finding =>
                finding.Code == "INTERNAL_ROUTE_AMBIGUOUS"),
            "내부 경로 미확정 Finding이 필요합니다.");
    }

    private static void UsesMostRecentPurposeResult()
    {
        DestinationRouteEvidence oldResult = Route(
            RouteProbePurpose.InternalDirectTarget,
            "44444444-4444-4444-4444-444444444444",
            NetworkAdapterCategory.Ethernet,
            RouteWlanCorrelationStatus.DifferentInterface,
            DateTimeOffset.UnixEpoch);
        DestinationRouteEvidence recentResult = Route(
            RouteProbePurpose.InternalDirectTarget,
            "55555555-5555-5555-5555-555555555555",
            NetworkAdapterCategory.Wireless,
            RouteWlanCorrelationStatus.Matched,
            DateTimeOffset.UnixEpoch.AddMinutes(1));
        DestinationRouteEvidence proxy = Route(
            RouteProbePurpose.ProxyEndpoint,
            "55555555-5555-5555-5555-555555555555",
            NetworkAdapterCategory.Wireless,
            RouteWlanCorrelationStatus.Matched,
            DateTimeOffset.UnixEpoch.AddMinutes(2));

        RoutePathComparisonResult result =
            RoutePathComparisonEvaluator.Evaluate(
                [recentResult, proxy, oldResult]);

        Ensure(result.InternalDirect?.InterfaceFingerprint
               == recentResult.SelectedInterface?.IdentityFingerprint,
            "같은 목적의 가장 최근 결과를 사용해야 합니다.");
        Ensure(result.Status == RoutePathComparisonStatus.Ready,
            "오래된 불일치보다 최근 정상 결과를 사용해야 합니다.");
    }

    private static void DoesNotExposeRawInterfaceIdentityInComparisonPoints()
    {
        const string secret =
            "66666666-6666-6666-6666-666666666666";
        RoutePathComparisonResult result =
            RoutePathComparisonEvaluator.Evaluate(
            [
                Route(
                    RouteProbePurpose.InternalDirectTarget,
                    secret,
                    NetworkAdapterCategory.Wireless,
                    RouteWlanCorrelationStatus.Matched,
                    DateTimeOffset.UnixEpoch)
            ]);

        string pointText = result.InternalDirect?.ToString() ?? string.Empty;
        Ensure(!pointText.Contains(secret, StringComparison.OrdinalIgnoreCase),
            "비교 Point에는 전체 인터페이스 ID가 없어야 합니다.");
        Ensure(result.InternalDirect?.InterfaceFingerprint?.Length
               == RouteInterfaceFingerprint.DisplayLength,
            "비교 Point에는 짧은 ID 지문만 있어야 합니다.");
    }

    private static DestinationRouteEvidence Route(
        RouteProbePurpose purpose,
        string interfaceId,
        NetworkAdapterCategory category,
        RouteWlanCorrelationStatus correlation,
        DateTimeOffset capturedAt,
        bool isVpn = false,
        bool isVirtual = false)
    {
        RouteInterfaceDescriptor selected = new(
            InterfaceIdentity: interfaceId,
            DisplayName: "합성 인터페이스",
            Description: "합성 설명",
            NativeInterfaceType: category.ToString(),
            Category: category,
            OperationalState: NetworkAdapterOperationalState.Up,
            HasDefaultGateway: true,
            IsVirtual: isVirtual,
            IsVpn: isVpn);
        string message = correlation == RouteWlanCorrelationStatus.Matched
            ? "Windows 최적 라우팅 인터페이스가 현재 연결된 Native WLAN 인터페이스와 일치합니다."
            : "Windows 최적 경로가 현재 연결된 Wi-Fi가 아닌 다른 인터페이스를 선택합니다.";

        return new DestinationRouteEvidence(
            CapturedAt: capturedAt,
            TargetLabel: purpose.ToString(),
            Purpose: purpose,
            DnsWasUsed: false,
            ResolvedAddressCount: 1,
            Status: DestinationRouteEvidenceStatus.Success,
            SelectedInterface: selected,
            AddressEvidence: Array.Empty<RouteAddressEvidence>(),
            Warnings: Array.Empty<string>(),
            Message: "합성 성공",
            WlanCorrelationStatus: correlation,
            ExpectedWlanInterfaceFingerprint:
                RouteInterfaceFingerprint.Create(
                    correlation == RouteWlanCorrelationStatus.Matched
                        ? interfaceId
                        : "77777777-7777-7777-7777-777777777777"),
            WlanCorrelationMessage: message);
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
