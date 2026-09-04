using System.Runtime.CompilerServices;
using WlanLivePathTester.Core.Observation;
using WlanLivePathTester.Core.Reporting;

namespace WlanLivePathTester.SelfTest;

internal static class Alpha9FeatureIntegrationTests
{
#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Run()
    {
        RequireStructuredTerminationReasons();
        RequireTimingContinuityPolicy();
        RequirePowerTransitionState();
        RequireStrictCounterSelectionPolicy();
        RequireObservationReportWriter();
        Console.WriteLine("PASS alpha.9 strict observation feature integration");
    }

    private static void RequireStructuredTerminationReasons()
    {
        BrowserObservationTerminationReason[] required =
        [
            BrowserObservationTerminationReason.Completed,
            BrowserObservationTerminationReason.CanceledByUser,
            BrowserObservationTerminationReason.AdapterChanged,
            BrowserObservationTerminationReason.AdapterUnavailable,
            BrowserObservationTerminationReason.CounterProviderMismatch,
            BrowserObservationTerminationReason.SystemSuspend,
            BrowserObservationTerminationReason.TimingDiscontinuity
        ];

        Ensure(required.Distinct().Count() == required.Length,
            "관찰 종료 원인은 서로 다른 enum 값이어야 합니다.");
    }

    private static void RequireTimingContinuityPolicy()
    {
        DateTimeOffset start = DateTimeOffset.UnixEpoch;
        ObservationTimingContinuityDecision valid =
            ObservationTimingContinuityPolicy.Evaluate(
                start,
                start.AddSeconds(1),
                1000);
        ObservationTimingContinuityDecision invalid =
            ObservationTimingContinuityPolicy.Evaluate(
                start,
                start.AddSeconds(6),
                1000);

        Ensure(valid.ShouldContinue,
            "정상 1초 카운터 간격은 계속 처리해야 합니다.");
        Ensure(!invalid.ShouldContinue
               && invalid.Status
                   == ObservationTimingContinuityStatus.ExcessiveInterval,
            "6초 카운터 간격은 시간 연속성 중단이어야 합니다.");
    }

    private static void RequirePowerTransitionState()
    {
        ObservationPowerTransitionState state = new();
        state.BeginObservation();
        ObservationPowerTransitionDecision suspend = state.Handle(
            ObservationPowerTransition.Suspend);
        ObservationPowerTransitionDecision resume = state.Handle(
            ObservationPowerTransition.Resume);

        Ensure(suspend.ShouldCancelObservation,
            "활성 관찰 중 Suspend는 취소를 요구해야 합니다.");
        Ensure(resume.ShouldReevaluateAdapters,
            "Resume는 어댑터 재평가를 요구해야 합니다.");
    }

    private static void RequireStrictCounterSelectionPolicy()
    {
        const string expectedId =
            "A1B2C3D4-E5F6-47A8-9123-1234567890AB";
        InterfaceCounterSelectionDecision missing =
            InterfaceCounterSelectionPolicy.Select(
            [
                new InterfaceCounterCandidate(
                    CandidateIndex: 0,
                    InterfaceId:
                        "B1B2C3D4-E5F6-47A8-9123-1234567890AB",
                    Description: "Same description",
                    IsWireless: true,
                    IsOperational: true)
            ],
            preferredInterfaceId: expectedId,
            preferredInterfaceDescription: "Same description");

        Ensure(!missing.IsSelected
               && missing.Status
                   == InterfaceCounterSelectionStatus.PreferredInterfaceNotFound,
            "유효한 고정 GUID가 없으면 같은 설명의 다른 Wi-Fi로 우회하면 안 됩니다.");
    }

    private static void RequireObservationReportWriter()
    {
        BrowserObservationResult result = new(
            BrowserObservationStatus.Canceled,
            null,
            null,
            "integration",
            BrowserObservationTerminationReason.SystemSuspend);
        BrowserObservationSessionReportDocument report =
            BrowserObservationSessionReportWriter.CreateDocument(
                result,
                "0.1.0-integration",
                DateTimeOffset.UnixEpoch);

        Ensure(report.TerminationReason == "SystemSuspend",
            "관찰 전용 보고서가 구조화 종료 원인을 유지해야 합니다.");
        Ensure(report.SensitiveValuesIncluded == false,
            "관찰 전용 보고서는 민감값 미포함을 선언해야 합니다.");
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
