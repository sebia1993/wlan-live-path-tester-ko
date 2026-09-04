using WlanLivePathTester.Core.Routing;

namespace WlanLivePathTester.Windows.Routing;

public static class RouteEvidenceResultHistory
{
    private const int MaximumResults = 12;
    private static readonly object Sync = new();
    private static readonly List<DestinationRouteEvidence> Results = [];

    public static void Add(DestinationRouteEvidence result)
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

    public static IReadOnlyList<DestinationRouteEvidence> Snapshot()
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
