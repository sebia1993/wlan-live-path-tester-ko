using System.Runtime.CompilerServices;
using WlanLivePathTester.Core.Observation;

namespace WlanLivePathTester.SelfTest;

internal static class BrowserObservationCancellationContextTests
{
#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Run()
    {
        DefaultsUnannotatedCancellationToUserStop();
        RecordsUserCancellationOnce();
        SystemSuspendOverridesUserCancellation();
        UserCancellationCannotDowngradeSuspend();
        ResetClearsPreviousReason();
        ConcurrentRequestsResolveToSystemSuspend();
        Console.WriteLine(
            "PASS browser observation cancellation context priority tests");
    }

    private static void DefaultsUnannotatedCancellationToUserStop()
    {
        BrowserObservationCancellationContext context = new();

        Ensure(context.RequestedReason
               == BrowserObservationTerminationReason.None,
            "새 취소 컨텍스트에는 명시 요청이 없어야 합니다.");
        Ensure(context.ResolveCancellationReason()
               == BrowserObservationTerminationReason.CanceledByUser,
            "기존 호출처럼 원인 없는 토큰 취소는 사용자 중지로 안전하게 처리해야 합니다.");
    }

    private static void RecordsUserCancellationOnce()
    {
        BrowserObservationCancellationContext context = new();

        Ensure(context.RequestUserCancellation(),
            "첫 사용자 중지 요청은 상태를 변경해야 합니다.");
        Ensure(!context.RequestUserCancellation(),
            "같은 사용자 중지 요청을 중복 상태 변경으로 처리하면 안 됩니다.");
        Ensure(context.RequestedReason
               == BrowserObservationTerminationReason.CanceledByUser,
            "사용자 중지 요청 원인을 유지해야 합니다.");
        Ensure(context.ResolveCancellationReason()
               == BrowserObservationTerminationReason.CanceledByUser,
            "사용자 중지는 CanceledByUser로 해석해야 합니다.");
    }

    private static void SystemSuspendOverridesUserCancellation()
    {
        BrowserObservationCancellationContext context = new();
        _ = context.RequestUserCancellation();

        Ensure(context.RequestSystemSuspend(),
            "사용자 중지 요청 뒤 시스템 절전이 오면 더 높은 우선순위로 갱신해야 합니다.");
        Ensure(context.RequestedReason
               == BrowserObservationTerminationReason.SystemSuspend,
            "시스템 절전이 사용자 중지보다 우선해야 합니다.");
        Ensure(context.ResolveCancellationReason()
               == BrowserObservationTerminationReason.SystemSuspend,
            "경합 후 종료 원인은 SystemSuspend여야 합니다.");
    }

    private static void UserCancellationCannotDowngradeSuspend()
    {
        BrowserObservationCancellationContext context = new();
        _ = context.RequestSystemSuspend();

        Ensure(!context.RequestUserCancellation(),
            "시스템 절전 뒤 사용자 중지 요청이 원인을 낮추면 안 됩니다.");
        Ensure(context.ResolveCancellationReason()
               == BrowserObservationTerminationReason.SystemSuspend,
            "SystemSuspend를 CanceledByUser로 덮어쓰면 안 됩니다.");
    }

    private static void ResetClearsPreviousReason()
    {
        BrowserObservationCancellationContext context = new();
        _ = context.RequestSystemSuspend();

        context.Reset();

        Ensure(context.RequestedReason
               == BrowserObservationTerminationReason.None,
            "새 관찰 전에 이전 절전 원인을 지워야 합니다.");
        Ensure(context.RequestUserCancellation(),
            "Reset 뒤 새 사용자 중지 요청을 기록할 수 있어야 합니다.");
    }

    private static void ConcurrentRequestsResolveToSystemSuspend()
    {
        BrowserObservationCancellationContext context = new();
        Parallel.For(
            0,
            128,
            index =>
            {
                if (index % 3 == 0)
                {
                    _ = context.RequestSystemSuspend();
                }
                else
                {
                    _ = context.RequestUserCancellation();
                }
            });

        Ensure(context.ResolveCancellationReason()
               == BrowserObservationTerminationReason.SystemSuspend,
            "동시 사용자·절전 요청에서도 최종 원인은 SystemSuspend여야 합니다.");
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
