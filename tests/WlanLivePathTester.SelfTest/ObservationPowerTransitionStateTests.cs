using System.Runtime.CompilerServices;
using WlanLivePathTester.Core.Observation;

namespace WlanLivePathTester.SelfTest;

internal static class ObservationPowerTransitionStateTests
{
#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Run()
    {
        SuspendsActiveObservation();
        DoesNotCancelWhenIdle();
        ResumeRequiresAdapterReevaluation();
        CompletionReturnsAndClearsSuspendReason();
        PowerStatusChangeDoesNotCancel();
        Console.WriteLine("PASS observation system power transition state tests");
    }

    private static void SuspendsActiveObservation()
    {
        ObservationPowerTransitionState state = new();
        state.BeginObservation();

        ObservationPowerTransitionDecision decision = state.Handle(
            ObservationPowerTransition.Suspend);

        Ensure(decision.ShouldCancelObservation,
            "활성 관찰 중 Suspend는 취소를 요구해야 합니다.");
        Ensure(decision.ObservationWasActive,
            "Suspend 결정에 활성 관찰 상태가 기록돼야 합니다.");
        Ensure(state.InterruptedBySuspend,
            "절전 중단 상태를 세션 완료 전까지 보관해야 합니다.");
        Ensure(state.AdapterReevaluationRequired,
            "절전 뒤 어댑터 재평가가 필요해야 합니다.");
    }

    private static void DoesNotCancelWhenIdle()
    {
        ObservationPowerTransitionState state = new();

        ObservationPowerTransitionDecision decision = state.Handle(
            ObservationPowerTransition.Suspend);

        Ensure(!decision.ShouldCancelObservation,
            "관찰이 없을 때 Suspend가 취소를 요구하면 안 됩니다.");
        Ensure(!state.InterruptedBySuspend,
            "유휴 Suspend를 관찰 중단으로 기록하면 안 됩니다.");
        Ensure(state.AdapterReevaluationRequired,
            "유휴 절전 뒤에도 복귀 시 어댑터 재평가는 필요합니다.");
    }

    private static void ResumeRequiresAdapterReevaluation()
    {
        ObservationPowerTransitionState state = new();

        ObservationPowerTransitionDecision decision = state.Handle(
            ObservationPowerTransition.Resume);

        Ensure(decision.ShouldReevaluateAdapters,
            "Resume는 어댑터 재평가를 요구해야 합니다.");
        Ensure(state.AdapterReevaluationRequired,
            "재평가 완료 전까지 요구 상태가 유지돼야 합니다.");

        state.MarkAdaptersReevaluated();
        Ensure(!state.AdapterReevaluationRequired,
            "어댑터 재평가 완료 후 요구 상태를 해제해야 합니다.");
    }

    private static void CompletionReturnsAndClearsSuspendReason()
    {
        ObservationPowerTransitionState state = new();
        state.BeginObservation();
        _ = state.Handle(ObservationPowerTransition.Suspend);

        bool interrupted = state.CompleteObservation();
        bool secondCompletion = state.CompleteObservation();

        Ensure(interrupted,
            "첫 완료에서 절전 중단 여부를 반환해야 합니다.");
        Ensure(!secondCompletion,
            "절전 중단 상태는 한 번 소비한 뒤 지워야 합니다.");
        Ensure(!state.ObservationActive,
            "완료 후 관찰 활성 상태를 해제해야 합니다.");
    }

    private static void PowerStatusChangeDoesNotCancel()
    {
        ObservationPowerTransitionState state = new();
        state.BeginObservation();

        ObservationPowerTransitionDecision decision = state.Handle(
            ObservationPowerTransition.PowerStatusChanged);

        Ensure(!decision.ShouldCancelObservation,
            "배터리·전원 상태 변화만으로 관찰을 취소하면 안 됩니다.");
        Ensure(!decision.ShouldReevaluateAdapters,
            "일반 전원 상태 변화는 어댑터 재평가를 요구하지 않아야 합니다.");
        Ensure(!state.InterruptedBySuspend,
            "일반 전원 상태 변화를 절전 중단으로 기록하면 안 됩니다.");
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
