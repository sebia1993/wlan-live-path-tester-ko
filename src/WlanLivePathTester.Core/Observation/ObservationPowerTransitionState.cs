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
    private bool _resumeObservedForPendingTransition;

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

    public bool ResumeObservedForPendingTransition
    {
        get
        {
            lock (_sync)
            {
                return _resumeObservedForPendingTransition;
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

    public bool TryMarkAdaptersReevaluated()
    {
        lock (_sync)
        {
            if (!_adapterReevaluationRequired
                || !_resumeObservedForPendingTransition
                || _observationActive)
            {
                return false;
            }

            _adapterReevaluationRequired = false;
            _resumeObservedForPendingTransition = false;
            return true;
        }
    }

    private ObservationPowerTransitionDecision HandleSuspend()
    {
        bool wasActive = _observationActive;
        _adapterReevaluationRequired = true;
        _resumeObservedForPendingTransition = false;
        if (wasActive)
        {
            _interruptedBySuspend = true;
        }

        return new ObservationPowerTransitionDecision(
            ShouldCancelObservation: wasActive,
            ShouldReevaluateAdapters: false,
            ObservationWasActive: wasActive,
            Message: wasActive
                ? "시스템 절전 전환을 감지해 활성 브라우저 관찰을 중단합니다."
                : "시스템 절전 전환을 감지했습니다. 복귀 후 어댑터 상태를 다시 평가합니다.");
    }

    private ObservationPowerTransitionDecision HandleResume()
    {
        _adapterReevaluationRequired = true;
        _resumeObservedForPendingTransition = true;
        bool canReevaluateNow = !_observationActive;
        return new ObservationPowerTransitionDecision(
            ShouldCancelObservation: false,
            ShouldReevaluateAdapters: canReevaluateNow,
            ObservationWasActive: _observationActive,
            Message: canReevaluateNow
                ? "시스템 절전 복귀를 감지했습니다. Wi-Fi·VPN·가상 어댑터 상태를 다시 평가합니다."
                : "시스템 절전 복귀를 감지했습니다. 관찰 정리가 끝난 뒤 어댑터 상태를 다시 평가합니다.");
    }

    private ObservationPowerTransitionDecision HandlePowerStatusChanged() =>
        new(
            ShouldCancelObservation: false,
            ShouldReevaluateAdapters: false,
            ObservationWasActive: _observationActive,
            Message: "AC·배터리 전원 상태만 변경됐으므로 브라우저 관찰을 계속합니다.");
}
