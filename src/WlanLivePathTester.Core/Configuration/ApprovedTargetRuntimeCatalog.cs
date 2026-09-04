using WlanLivePathTester.Core.Models;

namespace WlanLivePathTester.Core.Configuration;

public sealed record ApprovedTargetRuntimePolicyStatus(
    bool IsActive,
    bool IsEnforced,
    bool IsBlocked,
    int TargetCount,
    string SourceDescription,
    string? BlockReason);

public static class ApprovedTargetRuntimeCatalog
{
    private static readonly object Sync = new();
    private static IReadOnlyDictionary<string, MeasurementTargetDefinition> _targets =
        EmptyTargets();
    private static bool _isEnforced;
    private static string _sourceDescription = "설정 없음";
    private static string? _blockReason;

    public static bool IsActive
    {
        get
        {
            lock (Sync)
            {
                return _targets.Count > 0
                    || _isEnforced
                    || _blockReason is not null;
            }
        }
    }

    public static bool IsEnforced
    {
        get
        {
            lock (Sync)
            {
                return _isEnforced;
            }
        }
    }

    public static bool IsBlocked
    {
        get
        {
            lock (Sync)
            {
                return _blockReason is not null;
            }
        }
    }

    public static void Replace(
        IEnumerable<MeasurementTargetDefinition> targets) =>
        Configure(
            targets,
            enforceApprovedTargets: false,
            sourceDescription: "로컬 승인 대상");

    public static void Configure(
        IEnumerable<MeasurementTargetDefinition> targets,
        bool enforceApprovedTargets,
        string sourceDescription)
    {
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceDescription);

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

        if (replacement.Count == 0)
        {
            throw new InvalidDataException(
                "승인 대상 실행 정책에는 측정 대상이 하나 이상 필요합니다.");
        }

        lock (Sync)
        {
            _targets = replacement;
            _isEnforced = enforceApprovedTargets;
            _sourceDescription = sourceDescription;
            _blockReason = null;
        }
    }

    public static void BlockEnforcedPolicy(
        string sourceDescription,
        string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceDescription);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        lock (Sync)
        {
            _targets = EmptyTargets();
            _isEnforced = true;
            _sourceDescription = sourceDescription;
            _blockReason = reason;
        }
    }

    public static void Clear()
    {
        lock (Sync)
        {
            _targets = EmptyTargets();
            _isEnforced = false;
            _sourceDescription = "설정 없음";
            _blockReason = null;
        }
    }

    public static bool TryResolve(
        MeasurementTargetDefinition requested,
        out MeasurementTargetDefinition effective)
    {
        ArgumentNullException.ThrowIfNull(requested);

        lock (Sync)
        {
            if (_blockReason is not null)
            {
                effective = requested;
                return false;
            }

            if (_targets.Count == 0)
            {
                effective = requested;
                return !_isEnforced;
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

    public static ApprovedTargetRuntimePolicyStatus GetStatus()
    {
        lock (Sync)
        {
            return new ApprovedTargetRuntimePolicyStatus(
                IsActive: _targets.Count > 0
                    || _isEnforced
                    || _blockReason is not null,
                IsEnforced: _isEnforced,
                IsBlocked: _blockReason is not null,
                TargetCount: _targets.Count,
                SourceDescription: _sourceDescription,
                BlockReason: _blockReason);
        }
    }

    private static IReadOnlyDictionary<string, MeasurementTargetDefinition>
        EmptyTargets() =>
        new Dictionary<string, MeasurementTargetDefinition>(
            StringComparer.OrdinalIgnoreCase);

    private static string CreateKey(
        NetworkPathKind pathKind,
        string url)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        Uri uri = new(url, UriKind.Absolute);
        return $"{pathKind}|{uri.AbsoluteUri}";
    }
}
