using System.Runtime.CompilerServices;
using WlanLivePathTester.Core.Observation;
using WlanLivePathTester.Core.Reporting;

namespace WlanLivePathTester.ReportSmoke;

internal static class ObservationTerminationStatusTests
{
#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Run()
    {
        VerifyStatus(BrowserObservationStatus.AdapterChanged);
        VerifyStatus(BrowserObservationStatus.AdapterUnavailable);
        VerifyStatus(BrowserObservationStatus.CounterProviderMismatch);
        Console.WriteLine("PASS  관찰 인터페이스 종료 상태 보고서 매핑");
    }

    private static void VerifyStatus(BrowserObservationStatus status)
    {
        BrowserObservationResult result = new(
            Status: status,
            Summary: null,
            InitialWlan: null,
            Message: $"합성 종료 상태: {status}");
        ReportObservationSection? mapped =
            ReportObservationMapper.FromResult(result);

        Ensure(mapped is not null,
            "관찰 종료 결과를 보고서 섹션으로 매핑해야 합니다.");
        Ensure(mapped.Status.Equals(
                status.ToString(),
                StringComparison.Ordinal),
            $"보고서에 구조화된 종료 상태를 보존해야 합니다: {status}");
        Ensure(mapped.Confidence == "Unknown",
            "요약이 없는 중단 결과는 신뢰도를 임의 추정하면 안 됩니다.");
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
