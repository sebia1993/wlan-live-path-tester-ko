using WlanLivePathTester.Core.Models;

namespace WlanLivePathTester.Core.Configuration;

public static class ApprovedTargetRuntimeCatalog
{
    private static readonly object Sync = new();
    private static IReadOnlyDictionary<string, MeasurementTargetDefinition> _targets =
        new Dictionary<string, MeasurementTargetDefinition>(
            StringComparer.OrdinalIgnoreCase);

    public static bool IsActive
    {
        get
        {
            lock (Sync)
            {
                return _targets.Count > 0;
            }
        }
    }

    public static void Replace(
        IEnumerable<MeasurementTargetDefinition> targets)
    {
        ArgumentNullException.ThrowIfNull(targets);

        Dictionary<string, MeasurementTargetDefinition> replacement =
            new(StringComparer.OrdinalIgnoreCase);
        foreach (MeasurementTargetDefinition target in targets)
        {
            string key = CreateKey(target.PathKind, target.Url);
            if (!replacement.TryAdd(key, target))
            {
                throw new InvalidDataException(
                    $"승인 대상 설정에 같은 경로 유형과 URL이 중복되었습니다: {target.Name}");
            }
        }

        lock (Sync)
        {
            _targets = replacement;
        }
    }

    public static void Clear()
    {
        lock (Sync)
        {
            _targets = new Dictionary<string, MeasurementTargetDefinition>(
                StringComparer.OrdinalIgnoreCase);
        }
    }

    public static bool TryResolve(
        MeasurementTargetDefinition requested,
        out MeasurementTargetDefinition effective)
    {
        ArgumentNullException.ThrowIfNull(requested);

        lock (Sync)
        {
            if (_targets.Count == 0)
            {
                effective = requested;
                return true;
            }

            if (_targets.TryGetValue(
                    CreateKey(requested.PathKind, requested.Url),
                    out MeasurementTargetDefinition? approved))
            {
                effective = approved;
                return true;
            }

            effective = requested;
            return false;
        }
    }

    public static MeasurementTargetDefinition Apply(
        MeasurementTargetDefinition requested)
    {
        _ = TryResolve(requested, out MeasurementTargetDefinition effective);
        return effective;
    }

    public static IReadOnlyList<MeasurementTargetDefinition> Snapshot()
    {
        lock (Sync)
        {
            return _targets.Values.ToArray();
        }
    }

    private static string CreateKey(NetworkPathKind pathKind, string url)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        Uri uri = new(url, UriKind.Absolute);
        return $"{pathKind}|{uri.AbsoluteUri}";
    }
}
