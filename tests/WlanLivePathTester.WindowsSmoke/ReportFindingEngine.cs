using WlanLivePathTester.Core.Reporting;

namespace WlanLivePathTester.WindowsSmoke;

internal static class ReportFindingEngine
{
    public static IReadOnlyList<ReportFinding> Evaluate(
        ReportWlanSection wlan,
        ReportProxySection proxy,
        IReadOnlyList<ReportTextSection> measurements,
        ReportObservationSection? observation,
        IReadOnlyList<ReportMeasurementSection>? structuredMeasurements = null) =>
        ReportFindingPipeline.Evaluate(
            wlan,
            proxy,
            measurements,
            observation,
            structuredMeasurements);

    public static IReadOnlyList<string> DefaultLimitations() =>
        ReportFindingPipeline.DefaultLimitations();
}
