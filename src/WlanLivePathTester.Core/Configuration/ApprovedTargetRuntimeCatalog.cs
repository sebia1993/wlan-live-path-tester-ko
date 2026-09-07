using WlanLivePathTester.Core.Models;
using WlanLivePathTester.Core.Security;

namespace WlanLivePathTester.Core.Configuration;

public sealed record ApprovedTargetRuntimePolicyStatus(
    bool IsActive, bool IsEnforced, bool IsBlocked, int TargetCount,
    string SourceDescription, string? BlockReason);

public static class ApprovedTargetRuntimeCatalog
{
    private static readonly object Sync = new();
    private static IReadOnlyDictionary<string, MeasurementTargetDefinition> _targets = EmptyTargets();
    private static bool _isEnforced;
    private static string _sourceDescription = "설정 없음";
    private static string? _blockReason;

    public static bool IsActive
    {
        get { lock (Sync) { return _targets.Count > 0 || _isEnforced || _blockReason is not null; } }
    }
    public static bool IsEnforced { get { lock (Sync) { return _isEnforced; } } }
    public static bool IsBlocked { get { lock (Sync) { return _blockReason is not null; } } }

    public static void Replace(IEnumerable<MeasurementTargetDefinition> targets) =>
        Configure(targets, enforceApprovedTargets: false, sourceDescription: "로컬 승인 대상");

    public static void Configure(IEnumerable<MeasurementTargetDefinition> targets,
        bool enforceApprovedTargets, string sourceDescription)
    {
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceDescription);
        // Scheme/host are canonicalized by CreateKey. URL paths and query values
        // remain case-sensitive so a different resource cannot match approval.
        Dictionary<string, MeasurementTargetDefinition> replacement = new(StringComparer.Ordinal);
        foreach (MeasurementTargetDefinition? target in targets)
        {
            if (target is null || replacement.Count >= TargetConfigurationLoader.MaximumTargetCount)
                throw new InvalidDataException("승인 대상 항목 또는 개수 제한이 올바르지 않습니다.");
            // Definitions are checked without consulting the old runtime policy.
            if (TargetValidator.ValidateDefinition(target).Count > 0
                || !TryCreateKey(target.PathKind, target.Url, out string key))
                throw new InvalidDataException("승인 대상 원문 또는 실행 제한이 올바르지 않습니다.");
            MeasurementTargetDefinition snapshot = target with
            {
                AllowedRedirectHosts = target.AllowedRedirectHosts is null ? null
                    : Array.AsReadOnly(target.AllowedRedirectHosts.ToArray())
            };
            if (!replacement.TryAdd(key, snapshot))
                throw new InvalidDataException("승인 대상 설정에 같은 경로 유형과 URL이 중복되었습니다.");
        }
        if (replacement.Count == 0)
            throw new InvalidDataException("승인 대상 실행 정책에는 측정 대상이 하나 이상 필요합니다.");
        lock (Sync)
        {
            _targets = replacement;
            _isEnforced = enforceApprovedTargets;
            _sourceDescription = sourceDescription;
            _blockReason = null;
        }
    }

    public static void BlockEnforcedPolicy(string sourceDescription, string reason)
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

    public static bool TryResolve(MeasurementTargetDefinition requested, out MeasurementTargetDefinition effective)
    {
        ArgumentNullException.ThrowIfNull(requested);
        effective = requested;
        if (!TryCreateKey(requested.PathKind, requested.Url, out string key)) return false;
        lock (Sync)
        {
            if (_blockReason is not null) return false;
            if (_targets.Count == 0) return !_isEnforced;
            if (_targets.TryGetValue(key, out MeasurementTargetDefinition? approved))
            {
                effective = approved;
                return true;
            }
            return false;
        }
    }

    public static MeasurementTargetDefinition Apply(MeasurementTargetDefinition requested)
    {
        _ = TryResolve(requested, out MeasurementTargetDefinition effective);
        return effective;
    }
    public static IReadOnlyList<MeasurementTargetDefinition> Snapshot()
    {
        lock (Sync) { return _targets.Values.ToArray(); }
    }
    public static ApprovedTargetRuntimePolicyStatus GetStatus()
    {
        lock (Sync)
        {
            return new(_targets.Count > 0 || _isEnforced || _blockReason is not null,
                _isEnforced, _blockReason is not null, _targets.Count, _sourceDescription, _blockReason);
        }
    }
    private static IReadOnlyDictionary<string, MeasurementTargetDefinition> EmptyTargets() =>
        new Dictionary<string, MeasurementTargetDefinition>(StringComparer.Ordinal);

    private static bool TryCreateKey(NetworkPathKind pathKind, string? rawUrl, out string key)
    {
        key = string.Empty;
        if (!Enum.IsDefined(pathKind) || !NetworkInputBoundary.TryHttpUri(rawUrl, out Uri? uri, out _)) return false;
        key = $"{pathKind}|{NetworkInputBoundary.CreateHttpKey(uri)}";
        return true;
    }
}
