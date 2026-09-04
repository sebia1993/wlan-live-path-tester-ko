using WlanLivePathTester.Core.Measurements;

namespace WlanLivePathTester.Windows.Measurements;

public static class RepeatedMeasurementResultHistory
{
    private const int MaximumResults = 8;
    private static readonly object Sync = new();
    private static readonly List<RepeatedMeasurementResult> Results = [];

    public static void Add(RepeatedMeasurementResult result)
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

    public static IReadOnlyList<RepeatedMeasurementResult> Snapshot()
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
