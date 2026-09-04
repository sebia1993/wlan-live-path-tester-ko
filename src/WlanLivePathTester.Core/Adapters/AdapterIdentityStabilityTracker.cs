namespace WlanLivePathTester.Core.Adapters;

public enum AdapterIdentityStabilityStatus
{
    Stable,
    TransientMismatch,
    Changed
}

public sealed record AdapterIdentityStabilityObservation(
    AdapterIdentityStabilityStatus Status,
    int ConsecutiveMismatchCount,
    bool CurrentIdentityAvailable,
    string Message);

public sealed class AdapterIdentityStabilityTracker
{
    private readonly string _expectedIdentity;
    private readonly int _mismatchThreshold;
    private int _consecutiveMismatchCount;
    private bool _changed;

    public AdapterIdentityStabilityTracker(
        string expectedIdentity,
        int mismatchThreshold = 3)
    {
        _expectedIdentity = Normalize(expectedIdentity);
        if (string.IsNullOrWhiteSpace(_expectedIdentity))
        {
            throw new ArgumentException(
                "예상 어댑터 ID가 비어 있습니다.",
                nameof(expectedIdentity));
        }

        if (mismatchThreshold is < 1 or > 20)
        {
            throw new ArgumentOutOfRangeException(
                nameof(mismatchThreshold),
                "불일치 임계값은 1~20회여야 합니다.");
        }

        _mismatchThreshold = mismatchThreshold;
    }

    public int MismatchThreshold => _mismatchThreshold;

    public int ConsecutiveMismatchCount => _consecutiveMismatchCount;

    public bool HasChanged => _changed;

    public AdapterIdentityStabilityObservation Observe(
        string? currentIdentity)
    {
        if (_changed)
        {
            return new AdapterIdentityStabilityObservation(
                AdapterIdentityStabilityStatus.Changed,
                _consecutiveMismatchCount,
                !string.IsNullOrWhiteSpace(currentIdentity),
                "Wi-Fi 인터페이스 ID 변경이 이미 확정되었습니다.");
        }

        string normalizedCurrent = Normalize(currentIdentity);
        if (normalizedCurrent.Equals(
                _expectedIdentity,
                StringComparison.OrdinalIgnoreCase))
        {
            _consecutiveMismatchCount = 0;
            return new AdapterIdentityStabilityObservation(
                AdapterIdentityStabilityStatus.Stable,
                ConsecutiveMismatchCount: 0,
                CurrentIdentityAvailable: true,
                Message: "시작 시점과 같은 Wi-Fi 인터페이스 ID입니다.");
        }

        _consecutiveMismatchCount++;
        bool available = !string.IsNullOrWhiteSpace(normalizedCurrent);
        if (_consecutiveMismatchCount < _mismatchThreshold)
        {
            return new AdapterIdentityStabilityObservation(
                AdapterIdentityStabilityStatus.TransientMismatch,
                _consecutiveMismatchCount,
                available,
                available
                    ? "Wi-Fi 인터페이스 ID 불일치를 일시적으로 관찰했습니다. 임계값 전까지 재확인합니다."
                    : "Wi-Fi 인터페이스 ID를 일시적으로 확인하지 못했습니다. 임계값 전까지 재확인합니다.");
        }

        _changed = true;
        return new AdapterIdentityStabilityObservation(
            AdapterIdentityStabilityStatus.Changed,
            _consecutiveMismatchCount,
            available,
            available
                ? "Wi-Fi 인터페이스 ID가 연속해서 달라 변경으로 확정했습니다."
                : "Wi-Fi 인터페이스 ID를 연속해서 확인하지 못해 연결 변경으로 처리했습니다.");
    }

    public static string Normalize(string? value)
    {
        string trimmed = (value ?? string.Empty).Trim();
        if (Guid.TryParse(trimmed, out Guid guid))
        {
            return guid.ToString("D");
        }

        return trimmed.Trim('{', '}').ToLowerInvariant();
    }
}
