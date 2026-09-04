using System.Runtime.CompilerServices;
using WlanLivePathTester.Core.Observation;

namespace WlanLivePathTester.SelfTest;

internal static class PinnedInterfaceContinuityPolicyTests
{
#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Run()
    {
        ContinuesForSameGuid();
        StopsForDifferentGuidEvenWhenDescriptionsMatch();
        UsesExactDescriptionWhenCurrentGuidIsUnavailable();
        StopsForDifferentDescriptionWhenGuidIsUnavailable();
        ContinuesPinnedCounterDuringTemporaryIdentityLoss();
        RejectsMissingPinnedIdentity();
        Console.WriteLine("PASS pinned Wi-Fi continuity policy tests");
    }

    private static void ContinuesForSameGuid()
    {
        const string id =
            "A1B2C3D4-E5F6-47A8-9123-1234567890AB";
        PinnedInterfaceContinuityDecision decision =
            PinnedInterfaceContinuityPolicy.Evaluate(
                pinnedInterfaceId: "{" + id + "}",
                pinnedInterfaceDescription: "Intel Wi-Fi",
                currentInterfaceId: id.ToLowerInvariant(),
                currentInterfaceDescription: "Different driver text");

        Ensure(decision.ShouldContinue,
            "같은 유효 GUID이면 설명이 달라도 관찰을 계속해야 합니다.");
        Ensure(decision.Status
               == PinnedInterfaceContinuityStatus.StableByInterfaceId,
            "같은 GUID는 GUID 연속성 상태여야 합니다.");
    }

    private static void StopsForDifferentGuidEvenWhenDescriptionsMatch()
    {
        PinnedInterfaceContinuityDecision decision =
            PinnedInterfaceContinuityPolicy.Evaluate(
                pinnedInterfaceId:
                    "A1B2C3D4-E5F6-47A8-9123-1234567890AB",
                pinnedInterfaceDescription: "Intel Wi-Fi",
                currentInterfaceId:
                    "B1B2C3D4-E5F6-47A8-9123-1234567890AB",
                currentInterfaceDescription: "Intel Wi-Fi");

        Ensure(!decision.ShouldContinue,
            "서로 다른 유효 GUID이면 같은 설명이어도 중단해야 합니다.");
        Ensure(decision.Status
               == PinnedInterfaceContinuityStatus.InterfaceChanged,
            "GUID 변경은 물리 인터페이스 변경 상태여야 합니다.");
    }

    private static void UsesExactDescriptionWhenCurrentGuidIsUnavailable()
    {
        PinnedInterfaceContinuityDecision decision =
            PinnedInterfaceContinuityPolicy.Evaluate(
                pinnedInterfaceId:
                    "A1B2C3D4-E5F6-47A8-9123-1234567890AB",
                pinnedInterfaceDescription:
                    " Intel(R)   Wi-Fi 6E AX211 ",
                currentInterfaceId: null,
                currentInterfaceDescription:
                    "Intel(R) Wi-Fi 6E AX211");

        Ensure(decision.ShouldContinue,
            "현재 GUID가 없어도 설명 완전 일치이면 고정 카운터를 유지해야 합니다.");
        Ensure(decision.Status
               == PinnedInterfaceContinuityStatus.StableByDescription,
            "설명 보조 연속성 상태가 필요합니다.");
    }

    private static void StopsForDifferentDescriptionWhenGuidIsUnavailable()
    {
        PinnedInterfaceContinuityDecision decision =
            PinnedInterfaceContinuityPolicy.Evaluate(
                pinnedInterfaceId:
                    "A1B2C3D4-E5F6-47A8-9123-1234567890AB",
                pinnedInterfaceDescription: "Internal Wi-Fi",
                currentInterfaceId: null,
                currentInterfaceDescription: "USB Wi-Fi");

        Ensure(!decision.ShouldContinue,
            "현재 GUID를 읽지 못해도 설명이 다른 NIC이면 중단해야 합니다.");
        Ensure(decision.Status
               == PinnedInterfaceContinuityStatus.InterfaceChanged,
            "설명 변경은 인터페이스 변경 상태여야 합니다.");
    }

    private static void ContinuesPinnedCounterDuringTemporaryIdentityLoss()
    {
        PinnedInterfaceContinuityDecision decision =
            PinnedInterfaceContinuityPolicy.Evaluate(
                pinnedInterfaceId:
                    "A1B2C3D4-E5F6-47A8-9123-1234567890AB",
                pinnedInterfaceDescription: "Intel Wi-Fi",
                currentInterfaceId: null,
                currentInterfaceDescription: null);

        Ensure(decision.ShouldContinue,
            "현재 WLAN을 일시적으로 읽지 못해도 시작 시 고정한 카운터는 유지해야 합니다.");
        Ensure(decision.Status
               == PinnedInterfaceContinuityStatus.CurrentIdentityUnavailable,
            "현재 identity 일시 미확인 상태가 필요합니다.");
    }

    private static void RejectsMissingPinnedIdentity()
    {
        PinnedInterfaceContinuityDecision decision =
            PinnedInterfaceContinuityPolicy.Evaluate(
                pinnedInterfaceId: null,
                pinnedInterfaceDescription: null,
                currentInterfaceId: null,
                currentInterfaceDescription: null);

        Ensure(!decision.ShouldContinue,
            "시작 시 고정할 identity가 전혀 없으면 연속성을 보장하면 안 됩니다.");
        Ensure(decision.Status
               == PinnedInterfaceContinuityStatus.PinnedIdentityUnavailable,
            "고정 identity 없음 상태가 필요합니다.");
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
