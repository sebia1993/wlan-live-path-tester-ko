namespace WlanLivePathTester.Core.Observation;

public enum ObservationPowerTransition
{
    Suspend,
    Resume,
    PowerStatusChanged
}

public sealed record ObservationPowerTransitionDecision(
    bool ShouldCancelObservation,
    bool ShouldReevaluateAdapters,
    bool ObservationWasActive,
    string Message);

public sealed class ObservationPowerTransitionState
{
    private readonly object _sync = new();
    private bool _observationActive;
    private bool _interruptedBySuspend;
    private bool _adapterReevaluationRequired;

    public bool ObservationActive
    {
        get
        {
            lock (_sync)
            {
                return _observationActive;
            }
        }
    }

    public bool InterruptedBySuspend
    {
        get
        {
            lock (_sync)
            {
                return _interruptedBySuspend;
            }
        }
    }

    public bool AdapterReevaluationRequired
    {
        get
        {
            lock (_sync)
            {
                return _adapterReevaluationRequired;
            }
        }
    }

    public void BeginObservation()
    {
        lock (_sync)
        {
            _observationActive = true;
            _interruptedBySuspend = false;
        }
    }

    public bool CompleteObservation()
    {
        lock (_sync)
        {
            bool interrupted = _interruptedBySuspend;
            _observationActive = false;
            _interruptedBySuspend = false;
            return interrupted;
        }
    }

    public ObservationPowerTransitionDecision Handle(
        ObservationPowerTransition transition)
    {
        lock (_sync)
        {
            return transition switch
            {
                ObservationPowerTransition.Suspend => HandleSuspend(),
                ObservationPowerTransition.Resume => HandleResume(),
                _ => HandlePowerStatusChanged()
            };
        }
    }

    public void MarkAdaptersReevaluated()
    {
        lock (_sync)
        {
            _adapterReevaluationRequired = false;
        }
    }

    private ObservationPowerTransitionDecision HandleSuspend()
    {
        bool wasActive = _observationActive;
        _adapterReevaluationRequired = true;
        if (wasActive)
        {
            _interruptedBySuspend = true;
        }

        return new ObservationPowerTransitionDecision(
            ShouldCancelObservation: wasActive,
            ShouldReevaluateAdapters: false,
            ObservationWasActive: wasActive,
            Message: wasActive
                ? "시스템 절전 전환을 감지해 활성 브라우저 관찰을 중단해야 합니다."
                : "시스템 절전 전환을 감지했습니다. 복귀 후 어댑터를 다시 평가해야 합니다.");
    }

    private ObservationPowerTransitionDecision HandleResume()
    {
        _adapterReevaluationRequired = true;
        return new ObservationPowerTransitionDecision(
            ShouldCancelObservation: false,
            ShouldReevaluateAdapters: true,
            ObservationWasActive: _observationActive,
            Message: "시스템 절전 복귀를 감지했습니다. Wi-Fi·VPN·가상 어댑터 상태를 다시 평가해야 합니다.");
    }

    private ObservationPowerTransitionDecision HandlePowerStatusChanged()
    {
        return new ObservationPowerTransitionDecision(
            ShouldCancelObservation: false,
            ShouldReevaluateAdapters: false,
            ObservationWasActive: _observationActive,
            Message: "전원 상태가 변경됐지만 절전·복귀 이벤트는 아니므로 관찰 상태를 변경하지 않습니다.");
    }
}
