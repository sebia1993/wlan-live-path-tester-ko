using System.Runtime.CompilerServices;
using WlanLivePathTester.Core.NetworkEnvironment;
using WlanLivePathTester.Core.Routing;

namespace WlanLivePathTester.SelfTest;

internal static class RouteWlanCorrelationEvaluatorTests
{
#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Run()
    {
        MatchesNormalizedInterfaceGuid();
        DetectsDifferentWirelessInterface();
        IdentifiesEthernetMismatch();
        HandlesMissingWlanIdentity();
        HandlesMultipleRouteInterfaces();
        DoesNotTreatFallbackIdentityAsExact();
        Console.WriteLine("PASS route to Native WLAN correlation tests");
    }

    private static void MatchesNormalizedInterfaceGuid()
    {
        const string id =
            "A1B2C3D4-E5F6-47A8-9123-1234567890AB";
        DestinationRouteEvidence result =
            RouteWlanCorrelationEvaluator.Apply(
                Route(Interface(
                    "{" + id.ToLowerInvariant() + "}",
                    NetworkAdapterCategory.Wireless)),
                id);

        Ensure(result.WlanCorrelationStatus
               == RouteWlanCorrelationStatus.Matched,
            "표기만 다른 같은 GUID는 WLAN 일치여야 합니다.");
        Ensure(result.ExpectedWlanInterfaceFingerprint
               == result.SelectedInterface?.IdentityFingerprint,
            "같은 GUID는 같은 짧은 지문이어야 합니다.");
        Ensure(result.Warnings.Count == 0,
            "정상 일치는 경고를 추가하면 안 됩니다.");
        Ensure(result.Message.Contains(
                "WLAN NIC 비교",
                StringComparison.Ordinal),
            "화면과 기존 보고서에서도 상관 결과를 볼 수 있어야 합니다.");
    }

    private static void DetectsDifferentWirelessInterface()
    {
        DestinationRouteEvidence result =
            RouteWlanCorrelationEvaluator.Apply(
                Route(Interface(
                    "11111111-1111-1111-1111-111111111111",
                    NetworkAdapterCategory.Wireless)),
                "22222222-2222-2222-2222-222222222222");

        Ensure(result.WlanCorrelationStatus
               == RouteWlanCorrelationStatus.DifferentInterface,
            "다른 물리 Wi-Fi GUID는 불일치여야 합니다.");
        Ensure(result.Warnings.Any(warning => warning.Contains(
                "다른 Wi-Fi 인터페이스",
                StringComparison.Ordinal)),
            "다른 USB·내장 Wi-Fi 선택 경고가 필요합니다.");
    }

    private static void IdentifiesEthernetMismatch()
    {
        DestinationRouteEvidence result =
            RouteWlanCorrelationEvaluator.Apply(
                Route(Interface(
                    "33333333-3333-3333-3333-333333333333",
                    NetworkAdapterCategory.Ethernet)),
                "44444444-4444-4444-4444-444444444444");

        Ensure(result.WlanCorrelationStatus
               == RouteWlanCorrelationStatus.DifferentInterface,
            "유선 경로는 현재 WLAN과 불일치여야 합니다.");
        Ensure(result.Warnings.Any(warning => warning.Contains(
                "유선 인터페이스",
                StringComparison.Ordinal)),
            "유선 선택을 명확히 설명해야 합니다.");
    }

    private static void HandlesMissingWlanIdentity()
    {
        DestinationRouteEvidence result =
            RouteWlanCorrelationEvaluator.Apply(
                Route(Interface(
                    "55555555-5555-5555-5555-555555555555",
                    NetworkAdapterCategory.Wireless)),
                expectedWlanInterfaceId: null);

        Ensure(result.WlanCorrelationStatus
               == RouteWlanCorrelationStatus.WlanIdentityUnavailable,
            "Native WLAN ID가 없으면 판단 불가여야 합니다.");
        Ensure(result.ExpectedWlanInterfaceFingerprint is null,
            "없는 WLAN ID의 가짜 지문을 만들면 안 됩니다.");
    }

    private static void HandlesMultipleRouteInterfaces()
    {
        DestinationRouteEvidence original = Route(
            selectedInterface: null) with
        {
            Status = DestinationRouteEvidenceStatus.MultipleInterfaces
        };
        DestinationRouteEvidence result =
            RouteWlanCorrelationEvaluator.Apply(
                original,
                "66666666-6666-6666-6666-666666666666");

        Ensure(result.WlanCorrelationStatus
               == RouteWlanCorrelationStatus.RouteInterfaceUnavailable,
            "단일 라우팅 인터페이스가 없으면 WLAN 비교 불가여야 합니다.");
        Ensure(result.ExpectedWlanInterfaceFingerprint?.Length
               == RouteInterfaceFingerprint.DisplayLength,
            "확인된 WLAN ID는 짧은 지문으로 유지해야 합니다.");
    }

    private static void DoesNotTreatFallbackIdentityAsExact()
    {
        DestinationRouteEvidence result =
            RouteWlanCorrelationEvaluator.Apply(
                Route(Interface(
                    "local-0123456789abcdef",
                    NetworkAdapterCategory.Wireless)),
                "77777777-7777-7777-7777-777777777777");

        Ensure(result.WlanCorrelationStatus
               == RouteWlanCorrelationStatus.RouteInterfaceUnavailable,
            "로컬 보조 식별자를 Native WLAN GUID와 정확 일치로 취급하면 안 됩니다.");
    }

    private static DestinationRouteEvidence Route(
        RouteInterfaceDescriptor? selectedInterface) =>
        new(
            CapturedAt: DateTimeOffset.UnixEpoch,
            TargetLabel: "합성 대상",
            Purpose: RouteProbePurpose.InternalDirectTarget,
            DnsWasUsed: false,
            ResolvedAddressCount: selectedInterface is null ? 0 : 1,
            Status: selectedInterface is null
                ? DestinationRouteEvidenceStatus.RouteNotFound
                : DestinationRouteEvidenceStatus.Success,
            SelectedInterface: selectedInterface,
            AddressEvidence: Array.Empty<RouteAddressEvidence>(),
            Warnings: Array.Empty<string>(),
            Message: "합성 라우팅 결과");

    private static RouteInterfaceDescriptor Interface(
        string id,
        NetworkAdapterCategory category) =>
        new(
            InterfaceIdentity: id,
            DisplayName: "합성 인터페이스",
            Description: "합성 설명",
            NativeInterfaceType: category.ToString(),
            Category: category,
            OperationalState: NetworkAdapterOperationalState.Up,
            HasDefaultGateway: true,
            IsVirtual: category == NetworkAdapterCategory.Tunnel,
            IsVpn: category == NetworkAdapterCategory.Tunnel);

    private static void Ensure(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
