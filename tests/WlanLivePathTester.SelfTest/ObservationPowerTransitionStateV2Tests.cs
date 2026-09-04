using System.Runtime.CompilerServices;
using WlanLivePathTester.Core.Observation;

namespace WlanLivePathTester.SelfTest;

internal static class ObservationPowerTransitionStateV2Tests
{
#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Run()
    {
        ActiveSuspendCancelsAndDefersRefresh();
        IdleSuspendDoesNotCancelOrRefreshBeforeResume();
        IdleResumeAllowsOneRefresh();
        ActiveResumeDefersRefreshUntilCompletion();
        PowerStatusChangeDoesNotAffectObservation();
        NewObservationClearsOnlyItsSuspendMarker();
        Console.WriteLine(
            "PASS observation suspend resume state v2 tests");
    }

    private static void ActiveSuspendCancelsAndDefersRefresh()
    {
        ObservationPowerTransitionState state = new();
        state.BeginObservation();

        ObservationPowerTransitionDecision suspend = state.Handle(
            ObservationPowerTransition.Suspend);

        Ensure(suspend.ShouldCancelObservation,
            "활성 관찰 중 Suspend는 현재 관찰 취소를 요구해야 합니다.");
        Ensure(!suspend.ShouldReevaluateAdapters,
            "Suspend 처리 중에는 어댑터를 즉시 다시 평가하면 안 됩니다.");
        Ensure(suspend.ObservationWasActive,
            "Suspend 결정에 활성 관찰 상태를 기록해야 합니다.");
        Ensure(state.InterruptedBySuspend,
            "관찰 완료 전까지 절전 중단 표시를 유지해야 합니다.");
        Ensure(state.AdapterReevaluationRequired,
            "절전 이후 어댑터 재평가가 필요해야 합니다.");
        Ensure(!state.ResumeObservedForPendingTransition,
            "Suspend 직후에는 Resume를 관측한 것으로 처리하면 안 됩니다.");
        Ensure(!state.TryMarkAdaptersReevaluated(),
            "관찰이 활성 상태이거나 Resume 전에는 재평가를 완료 처리하면 안 됩니다.");
    }

    private static void IdleSuspendDoesNotCancelOrRefreshBeforeResume()
    {
        ObservationPowerTransitionState state = new();

        ObservationPowerTransitionDecision suspend = state.Handle(
            ObservationPowerTransition.Suspend);

        Ensure(!suspend.ShouldCancelObservation,
            "유휴 상태의 Suspend는 존재하지 않는 관찰을 취소하면 안 됩니다.");
        Ensure(!suspend.ObservationWasActive,
            "유휴 Suspend를 활성 관찰로 기록하면 안 됩니다.");
        Ensure(!state.InterruptedBySuspend,
            "유휴 Suspend는 관찰 중단 표시를 만들면 안 됩니다.");
        Ensure(state.AdapterReevaluationRequired,
            "유휴 절전 뒤에도 복귀 후 어댑터 재평가가 필요합니다.");
        Ensure(!state.ResumeObservedForPendingTransition,
            "Suspend만으로 Resume를 관측했다고 처리하면 안 됩니다.");
        Ensure(!state.TryMarkAdaptersReevaluated(),
            "실제 Resume 전에 어댑터 재평가 요구를 소비하면 안 됩니다.");
    }

    private static void IdleResumeAllowsOneRefresh()
    {
        ObservationPowerTransitionState state = new();
        _ = state.Handle(ObservationPowerTransition.Suspend);

        ObservationPowerTransitionDecision resume = state.Handle(
            ObservationPowerTransition.Resume);

        Ensure(resume.ShouldReevaluateAdapters,
            "유휴 Resume는 어댑터 재평가를 요청해야 합니다.");
        Ensure(state.ResumeObservedForPendingTransition,
            "Resume 관측 상태를 pending 전환에 기록해야 합니다.");
        Ensure(state.TryMarkAdaptersReevaluated(),
            "첫 유휴 재평가 완료 처리는 성공해야 합니다.");
        Ensure(!state.TryMarkAdaptersReevaluated(),
            "같은 Resume에 대해 재평가 완료를 두 번 소비하면 안 됩니다.");
        Ensure(!state.AdapterReevaluationRequired
               && !state.ResumeObservedForPendingTransition,
            "재평가 완료 뒤 pending과 Resume 관측 상태를 함께 해제해야 합니다.");
    }

    private static void ActiveResumeDefersRefreshUntilCompletion()
    {
        ObservationPowerTransitionState state = new();
        state.BeginObservation();
        _ = state.Handle(ObservationPowerTransition.Suspend);

        ObservationPowerTransitionDecision resume = state.Handle(
            ObservationPowerTransition.Resume);

        Ensure(!resume.ShouldReevaluateAdapters,
            "관찰 정리가 끝나기 전 Resume는 즉시 재평가를 요청하면 안 됩니다.");
        Ensure(state.ResumeObservedForPendingTransition,
            "관찰 중이라도 Resume 관측 사실은 보존해야 합니다.");
        Ensure(!state.TryMarkAdaptersReevaluated(),
            "활성 관찰 중 pending 재평가를 소비하면 안 됩니다.");
        Ensure(state.CompleteObservation(),
            "완료 시 절전 중단 여부를 한 번 반환해야 합니다.");
        Ensure(!state.ObservationActive,
            "완료 후 관찰 활성 상태를 해제해야 합니다.");
        Ensure(state.TryMarkAdaptersReevaluated(),
            "관찰 완료 뒤 pending 어댑터 재평가를 수행할 수 있어야 합니다.");
        Ensure(!state.CompleteObservation(),
            "절전 중단 표시는 첫 완료 뒤 지워져야 합니다.");
    }

    private static void PowerStatusChangeDoesNotAffectObservation()
    {
        ObservationPowerTransitionState state = new();
        state.BeginObservation();

        ObservationPowerTransitionDecision power = state.Handle(
            ObservationPowerTransition.PowerStatusChanged);

        Ensure(!power.ShouldCancelObservation,
            "AC·배터리 상태 변경만으로 관찰을 취소하면 안 됩니다.");
        Ensure(!power.ShouldReevaluateAdapters,
            "일반 전원 상태 변경은 어댑터 재평가를 요구하지 않아야 합니다.");
        Ensure(state.ObservationActive,
            "일반 전원 상태 변경 뒤 관찰은 계속 활성 상태여야 합니다.");
        Ensure(!state.AdapterReevaluationRequired
               && !state.ResumeObservedForPendingTransition,
            "일반 전원 상태 변경을 Suspend·Resume으로 오인하면 안 됩니다.");
    }

    private static void NewObservationClearsOnlyItsSuspendMarker()
    {
        ObservationPowerTransitionState state = new();
        state.BeginObservation();
        _ = state.Handle(ObservationPowerTransition.Suspend);
        _ = state.CompleteObservation();
        Ensure(state.AdapterReevaluationRequired,
            "첫 관찰 완료만으로 pending 어댑터 재평가를 지우면 안 됩니다.");

        state.BeginObservation();

        Ensure(!state.InterruptedBySuspend,
            "새 관찰은 이전 세션의 절전 중단 표시를 상속하면 안 됩니다.");
        Ensure(state.AdapterReevaluationRequired,
            "새 관찰 시작만으로 미수행 어댑터 재평가 요구를 지우면 안 됩니다.");
        Ensure(!state.TryMarkAdaptersReevaluated(),
            "Resume가 없고 새 관찰이 활성인 상태에서는 재평가를 소비하면 안 됩니다.");
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
