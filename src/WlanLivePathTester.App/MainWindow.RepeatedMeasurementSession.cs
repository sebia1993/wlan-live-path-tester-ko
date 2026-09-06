using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WlanLivePathTester.Core.Measurements;
using WlanLivePathTester.Core.Models;
using WlanLivePathTester.Core.Operations;

namespace WlanLivePathTester.App;

internal delegate Task<RepeatedMeasurementResult> RepeatedTargetRunner(
    MeasurementTargetDefinition target,
    RepeatedMeasurementPlan plan,
    bool performHeadPreflight,
    IProgress<RepeatedMeasurementProgress>? progress,
    CancellationToken cancellationToken);

public partial class MainWindow
{
    private Button? _repeatCancelButton;
    private ApplicationOperationUiLease? _repeatedOperationLease;
    private object? _repeatedTargetSession;

    // The same production path is exercised with an injected target runner by
    // WPF smoke tests; no alternative coordinator or timer watches completion.
    internal async Task RunRepeatedMeasurementSessionAsync(
        IReadOnlyList<MeasurementTargetDefinition> targets,
        RepeatedMeasurementPlan plan,
        RepeatedTargetRunner runner)
    {
        Dispatcher.VerifyAccess();
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(runner);
        if (_applicationOperationWindowClosed || _repeatedOperationLease is not null) return;

        MeasurementTargetDefinition[] snapshot = targets.ToArray();
        long plannedBytes = 0;
        try
        {
            if (snapshot.Length is < 1 or > 4 || plan.Validate().Count > 0
                || snapshot.Any(target => target is null || target.MaxBytes <= 0))
            {
                SetRepeatedResult("입력 오류: 반복 계획 또는 대상 수·수신량을 확인하십시오.", Brushes.DarkRed);
                return;
            }
            foreach (MeasurementTargetDefinition target in snapshot)
            {
                plannedBytes = checked(plannedBytes + plan.GetPlannedMaximumBytes(target));
            }
            if (plannedBytes > MaximumRepeatedOperationBytes)
            {
                SetRepeatedResult("입력 오류: 반복 작업 전체의 최대 예상 수신량은 2GiB 이하여야 합니다.", Brushes.DarkRed);
                return;
            }
        }
        catch (Exception exception) when (exception is ArgumentException or OverflowException)
        {
            SetRepeatedResult("입력 오류: 반복 계획 또는 최대 예상 수신량을 확인하십시오.", Brushes.DarkRed);
            return;
        }

        string plannedText = FormatRepeatedBytes(plannedBytes);
        bool performHeadPreflight = MeasurementHeadPreflightCheckBox.IsChecked == true;
        bool started = await RunCoordinatedMeasurementAsync(
            ApplicationOperationKind.RepeatedMeasurement,
            async (token, lease) =>
            {
                _repeatedOperationLease = lease;
                List<RepeatedMeasurementResult> results = [];
                int startedTargetCount = 0;
                try
                {
                    UpdateRepeatedMeasurementButtons();
                    for (int index = 0; index < snapshot.Length; index++)
                    {
                        if (token.IsCancellationRequested) break;
                        int targetOrdinal = index + 1;
                        object targetSession = new();
                        _repeatedTargetSession = targetSession;
                        Progress<RepeatedMeasurementProgress> progress = new(update =>
                        {
                            // A posted callback can outlive its target AND its
                            // run. Neither mutable results.Count nor a shared
                            // counter is an acceptable identity for that update.
                            if (lease.IsCurrent
                                && ReferenceEquals(_repeatedOperationLease, lease)
                                && ReferenceEquals(_repeatedTargetSession, targetSession)
                                && !token.IsCancellationRequested
                                && !_applicationOperationWindowClosed)
                            {
                                OnRepeatedMeasurementProgress(
                                    update, snapshot.Length, targetOrdinal - 1, plannedText);
                            }
                        });

                        SetRepeatedResult(
                            $"대상 {targetOrdinal}/{snapshot.Length} 반복 측정 중 · 최대 예상 수신량 {plannedText}",
                            Brushes.DarkSlateGray);
                        startedTargetCount++;
                        RepeatedMeasurementResult result;
                        try
                        {
                            result = await runner(snapshot[index], plan,
                                performHeadPreflight, progress, token);
                        }
                        finally
                        {
                            // Invalidate queued callbacks before displaying a
                            // result or moving to the next target.
                            if (ReferenceEquals(_repeatedTargetSession, targetSession))
                                _repeatedTargetSession = null;
                        }

                        if (result is null) throw new InvalidOperationException("REPEATED_RESULT_MISSING");
                        results.Add(result);
                        if (result.WasCanceled)
                        {
                            lease.RequestCancellation();
                            break;
                        }
                    }

                    SetRepeatedCompletion(results, snapshot.Length, startedTargetCount,
                        plannedBytes, token.IsCancellationRequested, errorType: null);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    SetRepeatedCompletion(results, snapshot.Length, startedTargetCount,
                        plannedBytes, canceled: true, errorType: null);
                }
                catch (Exception exception)
                {
                    SetRepeatedCompletion(results, snapshot.Length, startedTargetCount,
                        plannedBytes, canceled: false, exception.GetType().Name);
                    throw;
                }
                finally
                {
                    _repeatedTargetSession = null;
                    if (ReferenceEquals(_repeatedOperationLease, lease))
                        _repeatedOperationLease = null;
                    UpdateRepeatedMeasurementButtons();
                    // The common runner restores measurement controls first,
                    // then releases this same UI/Core lease.
                }
            },
            $"반복 측정 준비 중 · 대상 {snapshot.Length}개 · 최대 예상 수신량 {plannedText}");

        if (!started && !_applicationOperationWindowClosed)
            SetRepeatedResult(MeasurementStatusText.Text, Brushes.DarkOrange);
    }

    private void SetRepeatedCompletion(
        IReadOnlyList<RepeatedMeasurementResult> results,
        int plannedTargetCount,
        int startedTargetCount,
        long plannedBytes,
        bool canceled,
        string? errorType)
    {
        string outcome = errorType is not null
            ? $"반복 작업 오류: {errorType} · 확보한 결과 보존 · 예외 원문 미표시"
            : canceled ? "반복 작업 취소됨 · 확보한 결과 보존" : "반복 작업 종료";
        string text = $"{outcome}\n결과 확보 대상: {results.Count}/{plannedTargetCount} · 미시작 대상: {plannedTargetCount - startedTargetCount}\n"
            + FormatRepeatedResults(results, plannedBytes);
        Brush brush = errorType is not null ? Brushes.DarkRed
            : canceled ? Brushes.DarkOrange
            : results.Count == plannedTargetCount && results.All(result =>
                result.Summary.Confidence is RepeatedMeasurementConfidence.High
                    or RepeatedMeasurementConfidence.Medium)
                ? Brushes.DarkGreen
                : results.Any(result => result.Summary.MedianMbps.HasValue)
                    ? Brushes.DarkOrange : Brushes.DarkRed;
        SetRepeatedResult(text, brush);
    }

    private void OnCancelRepeatedMeasurementClick(object sender, RoutedEventArgs e)
    {
        ApplicationOperationUiLease? lease = _repeatedOperationLease;
        if (lease is null || !lease.IsCurrent || _applicationOperationWindowClosed) return;
        ApplicationOperationCancellationStatus status = lease.RequestCancellation();
        string failure = FormatApplicationCancellationFailure(status);
        SetRepeatedResult(string.IsNullOrEmpty(failure)
                ? "반복 측정 취소 요청됨 · 현재 호출의 실제 반환을 기다리고 남은 대상을 시작하지 않습니다."
                : failure,
            status == ApplicationOperationCancellationStatus.CallbackFailed
                ? Brushes.DarkRed : Brushes.DarkOrange);
        UpdateRepeatedMeasurementButtons();
    }

    private void OnRepeatedMeasurementWindowClosed(object? sender, EventArgs e)
    {
        _repeatedTargetSession = null;
        _repeatedOperationLease?.RequestCancellation();
        StartInternalMeasurementButton.IsEnabledChanged -= OnBaseMeasurementAvailabilityChanged;
        if (_repeatInternalButton is not null) _repeatInternalButton.Click -= OnStartRepeatedInternalClick;
        if (_repeatExternalButton is not null) _repeatExternalButton.Click -= OnStartRepeatedExternalClick;
        if (_repeatCancelButton is not null) _repeatCancelButton.Click -= OnCancelRepeatedMeasurementClick;
        Closed -= OnRepeatedMeasurementWindowClosed;
    }
}
