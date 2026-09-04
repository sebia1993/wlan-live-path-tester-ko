using System.Runtime.CompilerServices;
using WlanLivePathTester.Core.Adapters;

namespace WlanLivePathTester.SelfTest;

internal static class AdapterIdentityStabilityTrackerTests
{
#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Run()
    {
        VerifiesGuidNormalization();
        AllowsTransientUnavailableIdentity();
        ResetsMismatchCountWhenIdentityReturns();
        ConfirmsChangeAtThreshold();
        KeepsConfirmedChangeSticky();
        Console.WriteLine("PASS Wi-Fi adapter identity stability tracker tests");
    }

    private static void VerifiesGuidNormalization()
    {
        string normalized = NetworkAdapterIdentity.Normalize(
            "{12345678-1234-1234-1234-1234567890AB}");
        Ensure(
            normalized == "12345678-1234-1234-1234-1234567890ab",
            "인터페이스 GUID를 표준 D 형식과 소문자로 정규화해야 합니다.");
    }

    private static void AllowsTransientUnavailableIdentity()
    {
        AdapterIdentityStabilityTracker tracker = new(
            "11111111-1111-1111-1111-111111111111",
            mismatchThreshold: 3);

        AdapterIdentityStabilityObservation first = tracker.Observe(null);
        AdapterIdentityStabilityObservation second = tracker.Observe(string.Empty);

        Ensure(
            first.Status == AdapterIdentityStabilityStatus.TransientMismatch,
            "첫 번째 ID 미확인은 일시 불일치여야 합니다.");
        Ensure(
            second.Status == AdapterIdentityStabilityStatus.TransientMismatch,
            "임계값 전 두 번째 ID 미확인도 일시 불일치여야 합니다.");
        Ensure(!tracker.HasChanged,
            "임계값 전에는 인터페이스 변경을 확정하면 안 됩니다.");
    }

    private static void ResetsMismatchCountWhenIdentityReturns()
    {
        const string expected =
            "22222222-2222-2222-2222-222222222222";
        AdapterIdentityStabilityTracker tracker = new(
            expected,
            mismatchThreshold: 3);

        _ = tracker.Observe(
            "33333333-3333-3333-3333-333333333333");
        AdapterIdentityStabilityObservation stable = tracker.Observe(
            "{22222222-2222-2222-2222-222222222222}");

        Ensure(stable.Status == AdapterIdentityStabilityStatus.Stable,
            "예상 ID가 돌아오면 Stable이어야 합니다.");
        Ensure(tracker.ConsecutiveMismatchCount == 0,
            "정상 ID가 돌아오면 연속 불일치 횟수를 초기화해야 합니다.");
    }

    private static void ConfirmsChangeAtThreshold()
    {
        AdapterIdentityStabilityTracker tracker = new(
            "44444444-4444-4444-4444-444444444444",
            mismatchThreshold: 3);
        const string changed =
            "55555555-5555-5555-5555-555555555555";

        _ = tracker.Observe(changed);
        _ = tracker.Observe(changed);
        AdapterIdentityStabilityObservation third = tracker.Observe(changed);

        Ensure(third.Status == AdapterIdentityStabilityStatus.Changed,
            "세 번째 연속 불일치에서 변경을 확정해야 합니다.");
        Ensure(third.CurrentIdentityAvailable,
            "다른 ID가 확인된 변경과 ID 미확인을 구분해야 합니다.");
        Ensure(tracker.HasChanged,
            "변경 확정 상태를 보관해야 합니다.");
    }

    private static void KeepsConfirmedChangeSticky()
    {
        const string expected =
            "66666666-6666-6666-6666-666666666666";
        AdapterIdentityStabilityTracker tracker = new(
            expected,
            mismatchThreshold: 1);

        _ = tracker.Observe(
            "77777777-7777-7777-7777-777777777777");
        AdapterIdentityStabilityObservation afterReturn =
            tracker.Observe(expected);

        Ensure(afterReturn.Status == AdapterIdentityStabilityStatus.Changed,
            "변경 확정 뒤 동일 ID가 다시 보여도 같은 관찰 세션에서는 변경 상태를 유지해야 합니다.");
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
