using System.Runtime.CompilerServices;
using WlanLivePathTester.Core.NetworkEnvironment;

namespace WlanLivePathTester.SelfTest;

internal static class NetworkEnvironmentEvaluatorTests
{
#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Run()
    {
        ClassifiesCommonAdapterNames();
        DetectsWiredWirelessVpnAmbiguity();
        RecognizesSimpleWirelessEnvironment();
        DetectsMultipleWirelessAdapters();
        Console.WriteLine("PASS local network interface classification tests");
    }

    private static void ClassifiesCommonAdapterNames()
    {
        NetworkAdapterClassification wifi =
            NetworkAdapterClassifier.Classify(
                "Wireless80211",
                "Wi-Fi",
                "Intel(R) Wi-Fi 6E AX211");
        Ensure(wifi.Category == NetworkAdapterCategory.Wireless,
            "Wireless80211은 Wi-Fi로 분류해야 합니다.");
        Ensure(!wifi.IsVirtual && !wifi.IsVpn,
            "일반 Intel 무선 NIC는 물리 후보여야 합니다.");

        NetworkAdapterClassification hyperV =
            NetworkAdapterClassifier.Classify(
                "Ethernet",
                "vEthernet (Default Switch)",
                "Hyper-V Virtual Ethernet Adapter");
        Ensure(hyperV.Category == NetworkAdapterCategory.Ethernet,
            "Hyper-V 어댑터의 전송 유형은 Ethernet으로 유지해야 합니다.");
        Ensure(hyperV.IsVirtual && !hyperV.IsVpn,
            "Hyper-V 어댑터는 가상 인터페이스여야 합니다.");

        NetworkAdapterClassification vpn =
            NetworkAdapterClassifier.Classify(
                "Tunnel",
                "Company VPN",
                "Cisco AnyConnect Secure Mobility Client");
        Ensure(vpn.Category == NetworkAdapterCategory.Tunnel,
            "Tunnel 유형은 터널로 분류해야 합니다.");
        Ensure(vpn.IsVpn && vpn.IsVirtual,
            "AnyConnect 터널은 VPN·가상 인터페이스여야 합니다.");
    }

    private static void DetectsWiredWirelessVpnAmbiguity()
    {
        LocalNetworkAdapterSnapshot[] adapters =
        [
            Adapter(
                "Wi-Fi",
                NetworkAdapterCategory.Wireless,
                gateway: true),
            Adapter(
                "Ethernet",
                NetworkAdapterCategory.Ethernet,
                gateway: true),
            Adapter(
                "Company VPN",
                NetworkAdapterCategory.Tunnel,
                gateway: true,
                isVirtual: true,
                isVpn: true),
            Adapter(
                "vEthernet",
                NetworkAdapterCategory.Ethernet,
                gateway: false,
                isVirtual: true)
        ];

        NetworkEnvironmentAssessment assessment =
            NetworkEnvironmentEvaluator.Evaluate(adapters);

        Ensure(assessment.RouteSelectionMayBeAmbiguous,
            "유선·무선·VPN 기본 경로가 함께 있으면 혼재 가능성이 있어야 합니다.");
        Ensure(assessment.ActiveDefaultGatewayCount == 3,
            "활성 기본 게이트웨이 인터페이스 수를 계산해야 합니다.");
        Ensure(assessment.Findings.Any(finding =>
                finding.Code == "MULTIPLE_ACTIVE_DEFAULT_GATEWAYS"),
            "다중 기본 게이트웨이 Finding이 필요합니다.");
        Ensure(assessment.Findings.Any(finding =>
                finding.Code == "WIRED_AND_WIRELESS_GATEWAYS_ACTIVE"),
            "유선·무선 동시 기본 경로 Finding이 필요합니다.");
        Ensure(assessment.Findings.Any(finding =>
                finding.Code == "VPN_OR_TUNNEL_ACTIVE"),
            "VPN 활성 Finding이 필요합니다.");
        Ensure(assessment.Findings.Any(finding =>
                finding.Code == "VIRTUAL_ADAPTERS_ACTIVE"),
            "가상 어댑터 Finding이 필요합니다.");
    }

    private static void RecognizesSimpleWirelessEnvironment()
    {
        NetworkEnvironmentAssessment assessment =
            NetworkEnvironmentEvaluator.Evaluate(
            [
                Adapter(
                    "Wi-Fi",
                    NetworkAdapterCategory.Wireless,
                    gateway: true),
                Adapter(
                    "Loopback",
                    NetworkAdapterCategory.Loopback,
                    gateway: false)
            ]);

        Ensure(!assessment.RouteSelectionMayBeAmbiguous,
            "단일 물리 Wi-Fi 환경은 혼재 가능성이 낮아야 합니다.");
        Ensure(assessment.PreferredWirelessDisplayName == "Wi-Fi",
            "단일 물리 Wi-Fi 후보 이름을 반환해야 합니다.");
        Ensure(assessment.Findings.Count == 1
               && assessment.Findings[0].Code
                   == "SIMPLE_WIRELESS_ENVIRONMENT",
            "단순 환경 정보 Finding 하나가 필요합니다.");
    }

    private static void DetectsMultipleWirelessAdapters()
    {
        NetworkEnvironmentAssessment assessment =
            NetworkEnvironmentEvaluator.Evaluate(
            [
                Adapter(
                    "내장 Wi-Fi",
                    NetworkAdapterCategory.Wireless,
                    gateway: true),
                Adapter(
                    "USB Wi-Fi",
                    NetworkAdapterCategory.Wireless,
                    gateway: false)
            ]);

        Ensure(assessment.RouteSelectionMayBeAmbiguous,
            "물리 무선 NIC가 여러 개면 관찰 대상 혼재 가능성이 있어야 합니다.");
        Ensure(assessment.PreferredWirelessDisplayName is null,
            "여러 물리 Wi-Fi 중 하나를 임의로 선호 후보로 고르면 안 됩니다.");
        Ensure(assessment.Findings.Any(finding =>
                finding.Code == "MULTIPLE_ACTIVE_WIRELESS"),
            "다중 물리 Wi-Fi Finding이 필요합니다.");
    }

    private static LocalNetworkAdapterSnapshot Adapter(
        string name,
        NetworkAdapterCategory category,
        bool gateway,
        bool isVirtual = false,
        bool isVpn = false) =>
        new(
            DisplayName: name,
            Description: "합성 어댑터",
            NativeInterfaceType: category.ToString(),
            Category: category,
            OperationalState: NetworkAdapterOperationalState.Up,
            SpeedBitsPerSecond: 1_000_000_000,
            HasDefaultGateway: gateway,
            GatewayCount: gateway ? 1 : 0,
            HasIpv4: true,
            HasIpv6: true,
            UnicastAddressCount: 2,
            SupportsMulticast: true,
            IsVirtual: isVirtual,
            IsVpn: isVpn,
            ReadError: null);

    private static void Ensure(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
