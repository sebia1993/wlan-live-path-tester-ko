using WlanLivePathTester.Core.Measurements;

namespace WlanLivePathTester.Windows.Measurements;

public static class MeasurementResultHistory
{
    private const int MaximumResults = 16;
    private static readonly object Sync = new();
    private static readonly List<DownloadMeasurementResult> Results = [];

    public static void Add(DownloadMeasurementResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        lock (Sync)
        {
            Results.Add(result);
            if (Results.Count > MaximumResults)
            {
                Results.RemoveRange(0, Results.Count - MaximumResults);
            }
        }
    }

    public static IReadOnlyList<DownloadMeasurementResult> Snapshot()
    {
        lock (Sync)
        {
            return Results.ToArray();
        }
    }

    public static void Clear()
    {
        lock (Sync)
        {
            Results.Clear();
        }
    }
}
