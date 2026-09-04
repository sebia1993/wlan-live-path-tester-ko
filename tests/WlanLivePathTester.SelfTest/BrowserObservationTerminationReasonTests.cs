using System.Runtime.CompilerServices;
using WlanLivePathTester.Core.Observation;

namespace WlanLivePathTester.SelfTest;

internal static class BrowserObservationTerminationReasonTests
{
#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Run()
    {
        PreservesLegacyConstructorAndDeconstruction();
        StoresStructuredTerminationReason();
        Console.WriteLine("PASS structured browser observation termination tests");
    }

    private static void PreservesLegacyConstructorAndDeconstruction()
    {
        BrowserObservationResult result = new(
            BrowserObservationStatus.Canceled,
            null,
            null,
            "legacy result");

        (
            BrowserObservationStatus status,
            BrowserObservationSummary? summary,
            _,
            string message
        ) = result;

        Ensure(status == BrowserObservationStatus.Canceled,
            "기존 네 필드 deconstruction 상태를 유지해야 합니다.");
        Ensure(summary is null && message == "legacy result",
            "기존 네 필드 생성자와 deconstruction 값이 유지되어야 합니다.");
        Ensure(result.TerminationReason
               == BrowserObservationTerminationReason.None,
            "기존 호출은 명시하지 않은 종료 원인을 None으로 유지해야 합니다.");
    }

    private static void StoresStructuredTerminationReason()
    {
        BrowserObservationResult result = new(
            BrowserObservationStatus.PartialSuccess,
            null,
            null,
            "adapter changed",
            BrowserObservationTerminationReason.AdapterChanged);

        Ensure(result.TerminationReason
               == BrowserObservationTerminationReason.AdapterChanged,
            "다섯 번째 생성자 인수로 구조화 종료 원인을 저장해야 합니다.");
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
