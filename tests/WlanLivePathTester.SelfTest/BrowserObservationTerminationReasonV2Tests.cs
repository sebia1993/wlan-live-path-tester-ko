using System.Runtime.CompilerServices;
using WlanLivePathTester.Core.Observation;

namespace WlanLivePathTester.SelfTest;

internal static class BrowserObservationTerminationReasonV2Tests
{
#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Run()
    {
        PreservesFourValueConstructionAndDeconstruction();
        StoresExplicitTerminationReason();
        MapsExistingStatusesDeterministically();
        PrefersExplicitReasonOverStatusFallback();
        ProvidesStableKoreanLabels();
        Console.WriteLine("PASS structured browser observation termination v2 tests");
    }

    private static void PreservesFourValueConstructionAndDeconstruction()
    {
        BrowserObservationResult result = new(
            BrowserObservationStatus.Canceled,
            null,
            null,
            "legacy result");

        var (status, summary, initialWlan, message) = result;

        Ensure(status == BrowserObservationStatus.Canceled,
            "기존 네 값 deconstruction 상태를 유지해야 합니다.");
        Ensure(summary is null
               && initialWlan is null
               && message == "legacy result",
            "기존 네 값 생성자와 deconstruction 결과가 유지돼야 합니다.");
        Ensure(result.TerminationReason
               == BrowserObservationTerminationReason.None,
            "기존 생성자는 명시 종료 원인을 추가하지 않아야 합니다.");
        Ensure(result.EffectiveTerminationReason
               == BrowserObservationTerminationReason.CanceledByUser,
            "기존 Canceled 상태도 구조화된 사용자 중지로 해석해야 합니다.");
    }

    private static void StoresExplicitTerminationReason()
    {
        BrowserObservationResult result = new(
            BrowserObservationStatus.Canceled,
            null,
            null,
            "suspend",
            BrowserObservationTerminationReason.SystemSuspend);

        Ensure(result.TerminationReason
               == BrowserObservationTerminationReason.SystemSuspend,
            "다섯 번째 생성자 인수로 명시 종료 원인을 저장해야 합니다.");
        Ensure(result.EffectiveTerminationReason
               == BrowserObservationTerminationReason.SystemSuspend,
            "명시 종료 원인이 상태 기반 추론보다 우선해야 합니다.");
    }

    private static void MapsExistingStatusesDeterministically()
    {
        Dictionary<BrowserObservationStatus,
            BrowserObservationTerminationReason> expected = new()
        {
            [BrowserObservationStatus.Success] =
                BrowserObservationTerminationReason.Completed,
            [BrowserObservationStatus.Canceled] =
                BrowserObservationTerminationReason.CanceledByUser,
            [BrowserObservationStatus.AdapterChanged] =
                BrowserObservationTerminationReason.AdapterChanged,
            [BrowserObservationStatus.AdapterUnavailable] =
                BrowserObservationTerminationReason.AdapterUnavailable,
            [BrowserObservationStatus.CounterProviderMismatch] =
                BrowserObservationTerminationReason.CounterProviderMismatch,
            [BrowserObservationStatus.InterfaceUnavailable] =
                BrowserObservationTerminationReason.AdapterUnavailable,
            [BrowserObservationStatus.InvalidOptions] =
                BrowserObservationTerminationReason.InvalidOptions,
            [BrowserObservationStatus.UnsupportedPlatform] =
                BrowserObservationTerminationReason.UnsupportedPlatform,
            [BrowserObservationStatus.NoWirelessConnection] =
                BrowserObservationTerminationReason.NoWirelessConnection,
            [BrowserObservationStatus.PartialSuccess] =
                BrowserObservationTerminationReason.Failed,
            [BrowserObservationStatus.Failed] =
                BrowserObservationTerminationReason.Failed
        };

        foreach ((BrowserObservationStatus status,
                  BrowserObservationTerminationReason reason) in expected)
        {
            Ensure(BrowserObservationTerminationPolicy.FromStatus(status)
                   == reason,
                $"종료 원인 매핑이 잘못됐습니다: {status}");
        }
    }

    private static void PrefersExplicitReasonOverStatusFallback()
    {
        BrowserObservationResult result = new(
            BrowserObservationStatus.PartialSuccess,
            null,
            null,
            "timing",
            BrowserObservationTerminationReason.TimingDiscontinuity);

        Ensure(BrowserObservationTerminationPolicy.Resolve(result)
               == BrowserObservationTerminationReason.TimingDiscontinuity,
            "명시한 TimingDiscontinuity를 PartialSuccess의 기본 Failed로 덮어쓰면 안 됩니다.");
    }

    private static void ProvidesStableKoreanLabels()
    {
        Ensure(BrowserObservationTerminationPolicy.ToDisplayText(
                   BrowserObservationTerminationReason.Completed)
               == "정상 완료",
            "Completed 한국어 표시가 필요합니다.");
        Ensure(BrowserObservationTerminationPolicy.ToDisplayText(
                   BrowserObservationTerminationReason.CounterProviderMismatch)
               .Contains("카운터 공급자", StringComparison.Ordinal),
            "카운터 공급자 불일치 표시가 필요합니다.");
        Ensure(BrowserObservationTerminationPolicy.ToDisplayText(
                   BrowserObservationTerminationReason.TimingDiscontinuity)
               .Contains("시간 연속성", StringComparison.Ordinal),
            "시간 연속성 중단 표시가 필요합니다.");
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
