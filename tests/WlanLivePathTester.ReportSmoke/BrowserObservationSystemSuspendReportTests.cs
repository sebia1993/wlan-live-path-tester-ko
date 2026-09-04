using System.Runtime.CompilerServices;
using WlanLivePathTester.Core.Observation;
using WlanLivePathTester.Core.Reporting;

namespace WlanLivePathTester.ReportSmoke;

internal static class BrowserObservationSystemSuspendReportTests
{
#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Run()
    {
        BrowserObservationResult result = new(
            BrowserObservationStatus.Canceled,
            null,
            null,
            "시스템 절전 전환으로 관찰을 중단했습니다.",
            BrowserObservationTerminationReason.SystemSuspend);
        BrowserObservationSessionReportDocument report =
            BrowserObservationSessionReportWriter.CreateDocument(
                result,
                "0.1.0-test",
                DateTimeOffset.UnixEpoch.AddHours(9));

        Ensure(report.TerminationReason == "SystemSuspend",
            "관찰 전용 보고서에 SystemSuspend 종료 원인을 기록해야 합니다.");
        Ensure(report.Status == "Canceled",
            "절전 중단 결과 상태를 Canceled로 유지해야 합니다.");
        Ensure(report.Summary is null,
            "샘플이 없는 절전 중단 보고서는 빈 요약을 허용해야 합니다.");

        string json = BrowserObservationSessionReportWriter.RenderJson(
            report);
        string csv = BrowserObservationSessionReportWriter.RenderCsv(
            report);
        string html = BrowserObservationSessionReportWriter.RenderHtml(
            report);

        Ensure(json.Contains("\"terminationReason\": \"SystemSuspend\"",
                StringComparison.Ordinal),
            "JSON에 SystemSuspend가 필요합니다.");
        Ensure(csv.Contains(
                "\"observation\",\"terminationReason\",\"SystemSuspend\"",
                StringComparison.Ordinal),
            "CSV에 SystemSuspend 행이 필요합니다.");
        Ensure(html.Contains("SystemSuspend", StringComparison.Ordinal),
            "HTML에 SystemSuspend가 필요합니다.");
        Ensure(html.Contains("저장된 관찰 샘플이 없습니다.",
                StringComparison.Ordinal),
            "샘플 없는 절전 중단을 HTML에서 명확히 표시해야 합니다.");

        Console.WriteLine("PASS browser observation SystemSuspend report tests");
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
