using System.Runtime.CompilerServices;
using WlanLivePathTester.Core.Observation;
using WlanLivePathTester.Core.Reporting;

namespace WlanLivePathTester.ReportSmoke;

internal static class BrowserObservationTimingDiscontinuityReportTests
{
#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Run()
    {
        BrowserObservationResult result = new(
            BrowserObservationStatus.PartialSuccess,
            null,
            null,
            "샘플 간격이 비정상적으로 벌어져 관찰을 중단했습니다.",
            BrowserObservationTerminationReason.TimingDiscontinuity);
        BrowserObservationSessionReportDocument report =
            BrowserObservationSessionReportWriter.CreateDocument(
                result,
                "0.1.0-test",
                DateTimeOffset.UnixEpoch.AddHours(9));

        Ensure(report.TerminationReason == "TimingDiscontinuity",
            "관찰 보고서에 TimingDiscontinuity를 기록해야 합니다.");
        Ensure(report.Status == "PartialSuccess",
            "이전 샘플이 있을 수 있는 시간 연속성 중단은 PartialSuccess 상태를 허용해야 합니다.");

        string json = BrowserObservationSessionReportWriter.RenderJson(
            report);
        string csv = BrowserObservationSessionReportWriter.RenderCsv(
            report);
        string html = BrowserObservationSessionReportWriter.RenderHtml(
            report);

        Ensure(json.Contains(
                "\"terminationReason\": \"TimingDiscontinuity\"",
                StringComparison.Ordinal),
            "JSON에 TimingDiscontinuity가 필요합니다.");
        Ensure(csv.Contains(
                "\"observation\",\"terminationReason\",\"TimingDiscontinuity\"",
                StringComparison.Ordinal),
            "CSV에 TimingDiscontinuity 행이 필요합니다.");
        Ensure(html.Contains(
                "TimingDiscontinuity",
                StringComparison.Ordinal),
            "HTML에 TimingDiscontinuity가 필요합니다.");

        Console.WriteLine("PASS browser observation TimingDiscontinuity report tests");
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
