using WlanLivePathTester.Core.Models;

namespace WlanLivePathTester.Core.Observation;

public enum ObservationWlanIdentityContinuityStatus
{
    Stable,
    TransientlyUnavailable,
    UnavailableThresholdExceeded,
    Changed
}

public sealed record ObservationWlanIdentityContinuityObservation(
    ObservationWlanIdentityContinuityStatus Status,
    bool ShouldContinue,
    int ConsecutiveUnavailableCount,
    int UnavailableThreshold,
    bool CurrentIdentityAvailable,
    string Message);

public sealed class ObservationWlanIdentityContinuityTracker
{
    public const int DefaultUnavailableThreshold = 3;

    private readonly PinnedObservationInterface _binding;
    private readonly int _unavailableThreshold;
    private int _consecutiveUnavailableCount;
    private ObservationWlanIdentityContinuityStatus? _terminalStatus;

    public ObservationWlanIdentityContinuityTracker(
        PinnedObservationInterface binding,
        int unavailableThreshold = DefaultUnavailableThreshold)
    {
        _binding = binding
            ?? throw new ArgumentNullException(nameof(binding));
        if (unavailableThreshold is < 1 or > 20)
        {
            throw new ArgumentOutOfRangeException(
                nameof(unavailableThreshold),
                "WLAN ID 미확인 임계값은 1~20회여야 합니다.");
        }

        _unavailableThreshold = unavailableThreshold;
    }

    public int UnavailableThreshold => _unavailableThreshold;

    public int ConsecutiveUnavailableCount =>
        _consecutiveUnavailableCount;

    public bool IsTerminal => _terminalStatus.HasValue;

    public ObservationWlanIdentityContinuityObservation Observe(
        WlanSnapshot? currentWlan)
    {
        if (_terminalStatus.HasValue)
        {
            return CreateTerminalObservation(
                _terminalStatus.Value,
                currentWlan);
        }

        ObservationInterfaceContinuityResult evaluation =
            ObservationInterfaceBindingPolicy.EvaluateWlan(
                _binding,
                currentWlan);

        if (evaluation.Status
            == ObservationInterfaceContinuityStatus.Stable)
        {
            _consecutiveUnavailableCount = 0;
            return new ObservationWlanIdentityContinuityObservation(
                ObservationWlanIdentityContinuityStatus.Stable,
                ShouldContinue: true,
                ConsecutiveUnavailableCount: 0,
                UnavailableThreshold: _unavailableThreshold,
                CurrentIdentityAvailable: true,
                Message: evaluation.Message);
        }

        if (evaluation.Status
            == ObservationInterfaceContinuityStatus.WlanInterfaceChanged)
        {
            _terminalStatus =
                ObservationWlanIdentityContinuityStatus.Changed;
            return new ObservationWlanIdentityContinuityObservation(
                _terminalStatus.Value,
                ShouldContinue: false,
                ConsecutiveUnavailableCount:
                    _consecutiveUnavailableCount,
                UnavailableThreshold: _unavailableThreshold,
                CurrentIdentityAvailable: HasIdentity(currentWlan),
                Message: evaluation.Message
                    + " 실제 다른 ID가 확인됐으므로 임계값을 기다리지 않고 즉시 중단합니다.");
        }

        if (evaluation.Status
            != ObservationInterfaceContinuityStatus
                .WlanTemporarilyUnavailable)
        {
            _terminalStatus =
                ObservationWlanIdentityContinuityStatus.Changed;
            return new ObservationWlanIdentityContinuityObservation(
                _terminalStatus.Value,
                ShouldContinue: false,
                ConsecutiveUnavailableCount:
                    _consecutiveUnavailableCount,
                UnavailableThreshold: _unavailableThreshold,
                CurrentIdentityAvailable: HasIdentity(currentWlan),
                Message:
                    "WLAN 연속성 상태를 안전하게 해석하지 못해 고정 카운터 관찰을 중단합니다.");
        }

        _consecutiveUnavailableCount++;
        bool identityAvailable = HasIdentity(currentWlan);
        if (_consecutiveUnavailableCount < _unavailableThreshold)
        {
            return new ObservationWlanIdentityContinuityObservation(
                ObservationWlanIdentityContinuityStatus
                    .TransientlyUnavailable,
                ShouldContinue: true,
                ConsecutiveUnavailableCount:
                    _consecutiveUnavailableCount,
                UnavailableThreshold: _unavailableThreshold,
                CurrentIdentityAvailable: identityAvailable,
                Message:
                    $"{evaluation.Message} 연속 {_consecutiveUnavailableCount}/{_unavailableThreshold}회이며 임계값 전까지 시작 시 고정한 카운터로 재확인합니다.");
        }

        _terminalStatus = ObservationWlanIdentityContinuityStatus
            .UnavailableThresholdExceeded;
        return new ObservationWlanIdentityContinuityObservation(
            _terminalStatus.Value,
            ShouldContinue: false,
            ConsecutiveUnavailableCount:
                _consecutiveUnavailableCount,
            UnavailableThreshold: _unavailableThreshold,
            CurrentIdentityAvailable: identityAvailable,
            Message:
                $"Native WLAN 연결 또는 인터페이스 ID를 연속 {_consecutiveUnavailableCount}회 확인하지 못했습니다. 시작 시 고정한 카운터는 유지됐지만 WLAN 상태의 연속성을 더 이상 보장하지 않아 관찰을 중단합니다.");
    }

    private ObservationWlanIdentityContinuityObservation
        CreateTerminalObservation(
            ObservationWlanIdentityContinuityStatus status,
            WlanSnapshot? currentWlan)
    {
        string message = status switch
        {
            ObservationWlanIdentityContinuityStatus.Changed =>
                "WLAN 물리 인터페이스 변경이 이미 확정돼 관찰을 계속하지 않습니다.",
            ObservationWlanIdentityContinuityStatus
                .UnavailableThresholdExceeded =>
                "WLAN 연결 또는 인터페이스 ID의 연속 미확인 임계값을 이미 초과해 관찰을 계속하지 않습니다.",
            _ =>
                "WLAN 연속성 추적기가 종료 상태이므로 관찰을 계속하지 않습니다."
        };
        return new ObservationWlanIdentityContinuityObservation(
            status,
            ShouldContinue: false,
            ConsecutiveUnavailableCount:
                _consecutiveUnavailableCount,
            UnavailableThreshold: _unavailableThreshold,
            CurrentIdentityAvailable: HasIdentity(currentWlan),
            Message: message);
    }

    private static bool HasIdentity(WlanSnapshot? currentWlan) =>
        currentWlan is { IsConnected: true }
        && !string.IsNullOrWhiteSpace(
            ObservationInterfaceBindingPolicy.NormalizeInterfaceId(
                currentWlan.InterfaceId));
}
