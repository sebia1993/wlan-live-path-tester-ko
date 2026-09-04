using System.Runtime.CompilerServices;
using WlanLivePathTester.Core.NetworkEnvironment;
using WlanLivePathTester.Core.Routing;

namespace WlanLivePathTester.SelfTest;

internal static class RouteEvidenceEvaluatorTests
{
#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Run()
    {
        CombinesIpv4AndIpv6OnSameInterface();
        DetectsDifferentInterfaces();
        MarksPartialSuccess();
        WarnsWhenInternalRouteIsNotWireless();
        AddsExternalProxyCaveat();
        ProducesStableNormalizedFingerprint();
        Console.WriteLine("PASS destination route evidence aggregation tests");
    }

    private static void CombinesIpv4AndIpv6OnSameInterface()
    {
        RouteInterfaceDescriptor wifi = Interface(
            id: "A1B2C3D4-E5F6-47A8-9123-1234567890AB",
            category: NetworkAdapterCategory.Wireless,
            isVirtual: false,
            isVpn: false,
            gateway: true);
        DestinationRouteEvidence result = RouteEvidenceEvaluator.Evaluate(
            DateTimeOffset.UnixEpoch,
            "내부 대상",
            RouteProbePurpose.InternalDirectTarget,
            dnsWasUsed: true,
            resolvedAddressCount: 2,
            addressEvidence:
            [
                Success(RouteAddressFamilyKind.IPv4, wifi),
                Success(RouteAddressFamilyKind.IPv6, wifi with
                {
                    InterfaceIdentity =
                        "{a1b2c3d4-e5f6-47a8-9123-1234567890ab}"
                })
            ]);

        Ensure(result.Status == DestinationRouteEvidenceStatus.Success,
            "같은 GUID의 IPv4·IPv6 경로는 단일 성공이어야 합니다.");
        Ensure(result.SelectedInterface?.IdentityFingerprint
               == wifi.IdentityFingerprint,
            "정규화된 같은 GUID는 같은 지문과 선택 인터페이스여야 합니다.");
    }

    private static void DetectsDifferentInterfaces()
    {
        DestinationRouteEvidence result = RouteEvidenceEvaluator.Evaluate(
            DateTimeOffset.UnixEpoch,
            "복수 경로",
            RouteProbePurpose.ManualDestination,
            dnsWasUsed: true,
            resolvedAddressCount: 2,
            addressEvidence:
            [
                Success(
                    RouteAddressFamilyKind.IPv4,
                    Interface(
                        "11111111-1111-1111-1111-111111111111",
                        NetworkAdapterCategory.Wireless,
                        false,
                        false,
                        true)),
                Success(
                    RouteAddressFamilyKind.IPv6,
                    Interface(
                        "22222222-2222-2222-2222-222222222222",
                        NetworkAdapterCategory.Tunnel,
                        true,
                        true,
                        false))
            ]);

        Ensure(result.Status
               == DestinationRouteEvidenceStatus.MultipleInterfaces,
            "서로 다른 인터페이스의 주소 결과는 복수 경로여야 합니다.");
        Ensure(result.SelectedInterface is null,
            "복수 경로에서 임의 인터페이스를 선택하면 안 됩니다.");
        Ensure(result.Warnings.Any(warning => warning.Contains(
                "서로 다른",
                StringComparison.Ordinal)),
            "주소 계열별 인터페이스 차이 경고가 필요합니다.");
    }

    private static void MarksPartialSuccess()
    {
        RouteInterfaceDescriptor wifi = Interface(
            "33333333-3333-3333-3333-333333333333",
            NetworkAdapterCategory.Wireless,
            false,
            false,
            true);
        DestinationRouteEvidence result = RouteEvidenceEvaluator.Evaluate(
            DateTimeOffset.UnixEpoch,
            "부분 경로",
            RouteProbePurpose.InternalDirectTarget,
            dnsWasUsed: true,
            resolvedAddressCount: 2,
            addressEvidence:
            [
                Success(RouteAddressFamilyKind.IPv4, wifi),
                new RouteAddressEvidence(
                    RouteAddressFamilyKind.IPv6,
                    RouteAddressEvidenceStatus.RouteNotFound,
                    Interface: null,
                    NativeErrorCode: 123,
                    Message: "합성 경로 없음")
            ]);

        Ensure(result.Status
               == DestinationRouteEvidenceStatus.PartialSuccess,
            "일부 주소만 성공하면 PartialSuccess여야 합니다.");
        Ensure(result.SelectedInterface?.InterfaceIdentity
               == wifi.InterfaceIdentity,
            "성공한 주소들이 같은 인터페이스이면 해당 인터페이스를 유지해야 합니다.");
    }

    private static void WarnsWhenInternalRouteIsNotWireless()
    {
        RouteInterfaceDescriptor ethernet = Interface(
            "44444444-4444-4444-4444-444444444444",
            NetworkAdapterCategory.Ethernet,
            false,
            false,
            true);
        DestinationRouteEvidence result = RouteEvidenceEvaluator.Evaluate(
            DateTimeOffset.UnixEpoch,
            "내부 대상",
            RouteProbePurpose.InternalDirectTarget,
            dnsWasUsed: false,
            resolvedAddressCount: 1,
            addressEvidence:
            [
                Success(RouteAddressFamilyKind.IPv4, ethernet)
            ]);

        Ensure(result.Warnings.Any(warning => warning.Contains(
                "물리 Wi-Fi 범주가 아닙니다",
                StringComparison.Ordinal)),
            "내부 DIRECT 경로가 유선이면 WLAN 측정 불일치 경고가 필요합니다.");
    }

    private static void AddsExternalProxyCaveat()
    {
        RouteInterfaceDescriptor wifi = Interface(
            "55555555-5555-5555-5555-555555555555",
            NetworkAdapterCategory.Wireless,
            false,
            false,
            true);
        DestinationRouteEvidence result = RouteEvidenceEvaluator.Evaluate(
            DateTimeOffset.UnixEpoch,
            "외부 참고",
            RouteProbePurpose.ExternalTargetReference,
            dnsWasUsed: true,
            resolvedAddressCount: 1,
            addressEvidence:
            [
                Success(RouteAddressFamilyKind.IPv4, wifi)
            ]);

        Ensure(result.Warnings.Any(warning => warning.Contains(
                "실제 HTTP 연결 경로",
                StringComparison.Ordinal)),
            "외부 사이트 직접 경로와 프록시 실제 경로가 다를 수 있음을 표시해야 합니다.");
    }

    private static void ProducesStableNormalizedFingerprint()
    {
        string first = RouteInterfaceFingerprint.Create(
            "{ABCDEF12-3456-7890-ABCD-EF1234567890}");
        string second = RouteInterfaceFingerprint.Create(
            "abcdef12-3456-7890-abcd-ef1234567890");

        Ensure(first == second,
            "같은 GUID의 표기 차이는 같은 지문이어야 합니다.");
        Ensure(first.Length == RouteInterfaceFingerprint.DisplayLength,
            "인터페이스 지문은 고정된 짧은 길이여야 합니다.");
        Ensure(RouteInterfaceFingerprint.Create(null) == "없음",
            "빈 인터페이스 ID는 해시처럼 오해되지 않게 표시해야 합니다.");
    }

    private static RouteAddressEvidence Success(
        RouteAddressFamilyKind family,
        RouteInterfaceDescriptor descriptor) =>
        new(
            family,
            RouteAddressEvidenceStatus.Success,
            descriptor,
            NativeErrorCode: null,
            Message: "합성 성공");

    private static RouteInterfaceDescriptor Interface(
        string id,
        NetworkAdapterCategory category,
        bool isVirtual,
        bool isVpn,
        bool gateway) =>
        new(
            InterfaceIdentity: id,
            DisplayName: "합성 인터페이스",
            Description: "합성 설명",
            NativeInterfaceType: category.ToString(),
            Category: category,
            OperationalState: NetworkAdapterOperationalState.Up,
            HasDefaultGateway: gateway,
            IsVirtual: isVirtual,
            IsVpn: isVpn);

    private static void Ensure(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
