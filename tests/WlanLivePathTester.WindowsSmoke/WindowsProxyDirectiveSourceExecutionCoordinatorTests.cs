using System.Runtime.CompilerServices;
using System.Text.Json;
using WlanLivePathTester.Core.Proxy;
using WlanLivePathTester.Windows.Proxy;

namespace WlanLivePathTester.WindowsSmoke;

internal static class WindowsProxyDirectiveSourceExecutionCoordinatorTests
{
    private static readonly DateTimeOffset CapturedAt =
        DateTimeOffset.UnixEpoch.AddDays(11);
    private static readonly Uri TargetUri = new(
        "https://download.example.invalid/file.bin");

#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Run()
    {
        UsesManualProxyWithoutCallingTargetReaderWhenAutoIsOff();
        UsesTargetDecisionWhenPacOrAutoDetectIsEnabled();
        DoesNotFallBackToManualAfterTargetDecisionFailure();
        DoesNotAnalyzeTargetSpecificDirect();
        ManualReadFailureBlocksWithoutCallingTargetReader();
        PreCanceledTokenCallsNoReaderOrAnalyzer();
        TargetCancellationStopsBeforeAnalyzer();
        InvalidTargetUriCallsNoReader();
        SafeJsonDoesNotExposeRawProxySourcesOrAnalysis();
        Console.WriteLine(
            "PASS Windows proxy source reader and execution coordinator tests");
    }

    private static void
        UsesManualProxyWithoutCallingTargetReaderWhenAutoIsOff()
    {
        const string manualDirective =
            "PROXY manual-source.example.invalid:3128; DIRECT";
        RecordingManualSource manual = new(
            new WindowsManualProxyConfigurationReadResult(
                ProxyDirectiveSourceReadStatus.Success,
                ManualProxyConfigured: true,
                ManualProxyDirective: manualDirective,
                AutoDetectEnabled: false,
                PacConfigured: false,
                PacUrl: null));
        RecordingTargetSource target = new(
            (_, _, _) => throw new InvalidOperationException(
                "대상별 reader를 호출하면 안 됩니다."));
        WindowsProxyDirectiveSourceExecutionCoordinator coordinator =
            CreateCoordinator(manual, target);
        int analyzerCalls = 0;
        string? analyzerInput = null;

        WindowsProxyDirectiveSourceExecutionResult<string> result =
            coordinator.ReadAndExecuteAsync(
                    TargetUri,
                    (directive, _) =>
                    {
                        analyzerCalls++;
                        analyzerInput = directive;
                        return Task.FromResult("manual-analysis");
                    })
                .GetAwaiter()
                .GetResult();

        Ensure(manual.CallCount == 1,
            "수동 프록시 설정 reader는 한 번 호출해야 합니다.");
        Ensure(target.CallCount == 0,
            "자동 검색과 PAC가 꺼져 있으면 대상별 reader를 호출하면 안 됩니다.");
        Ensure(analyzerCalls == 1
               && analyzerInput == manualDirective,
            "대상별 판정 미시도 상태에서는 선택된 수동 프록시만 분석해야 합니다.");
        Ensure(result.Status
               == WindowsProxyDirectiveSourceExecutionStatus.Completed
               && result.Audit?.SourceKind
                   == ProxyDirectiveSourceKind.ManualProxyConfiguration
               && result.Analysis == "manual-analysis",
            "수동 프록시 출처·완료 상태·메모리 분석 결과를 유지해야 합니다.");
        Ensure(result.Snapshot?.TargetDecisionStatus
               == ProxyDirectiveSourceReadStatus.NotAttempted,
            "대상별 판정 미시도를 스냅샷에 유지해야 합니다.");
    }

    private static void
        UsesTargetDecisionWhenPacOrAutoDetectIsEnabled()
    {
        const string targetDirective =
            "PROXY target-source.example.invalid:8080; DIRECT";
        const string manualDirective =
            "PROXY ignored-manual.example.invalid:3128";
        const string pacUrl =
            "https://pac.example.invalid/proxy.pac";
        RecordingManualSource manual = new(
            new WindowsManualProxyConfigurationReadResult(
                ProxyDirectiveSourceReadStatus.Success,
                ManualProxyConfigured: true,
                ManualProxyDirective: manualDirective,
                AutoDetectEnabled: true,
                PacConfigured: true,
                PacUrl: pacUrl));
        RecordingTargetSource target = new((uri, configuration, _) =>
        {
            Ensure(uri == TargetUri,
                "대상별 reader에 사용자가 선택한 정확한 URL을 전달해야 합니다.");
            Ensure(configuration.PacUrl == pacUrl
                   && configuration.ManualProxyDirective
                       == manualDirective,
                "기존 Windows reader 어댑터가 필요로 하는 메모리 전용 설정을 유지해야 합니다.");
            return Task.FromResult(
                new WindowsTargetProxyDecisionReadResult(
                    ProxyDirectiveSourceReadStatus.Success,
                    IsDirect: false,
                    DirectiveText: targetDirective));
        });
        WindowsProxyDirectiveSourceExecutionCoordinator coordinator =
            CreateCoordinator(manual, target);
        int analyzerCalls = 0;
        string? analyzerInput = null;

        WindowsProxyDirectiveSourceExecutionResult<int> result =
            coordinator.ReadAndExecuteAsync(
                    TargetUri,
                    (directive, _) =>
                    {
                        analyzerCalls++;
                        analyzerInput = directive;
                        return Task.FromResult(17);
                    })
                .GetAwaiter()
                .GetResult();

        Ensure(manual.CallCount == 1 && target.CallCount == 1,
            "수동 설정과 대상별 판정 reader를 각각 한 번 호출해야 합니다.");
        Ensure(analyzerCalls == 1
               && analyzerInput == targetDirective,
            "수동 프록시가 있어도 대상별 PAC/WPAD 판정 원문만 분석해야 합니다.");
        Ensure(result.Status
               == WindowsProxyDirectiveSourceExecutionStatus.Completed
               && result.Audit?.SelectionCode
                   == ProxyDirectiveSourceSelectionCode
                       .TargetSpecificProxy
               && result.Audit.PlanCode
                   == ProxyDirectiveRouteAnalysisPlanCode
                       .TargetSpecificProxySelected
               && result.Analysis == 17,
            "대상별 선택 코드·계획 코드·완료 결과를 유지해야 합니다.");
    }

    private static void
        DoesNotFallBackToManualAfterTargetDecisionFailure()
    {
        const string manualDirective =
            "PROXY valid-but-blocked.example.invalid:3128";
        RecordingManualSource manual = new(
            new WindowsManualProxyConfigurationReadResult(
                ProxyDirectiveSourceReadStatus.Success,
                ManualProxyConfigured: true,
                ManualProxyDirective: manualDirective,
                AutoDetectEnabled: true,
                PacConfigured: false,
                PacUrl: null));
        RecordingTargetSource target = new(
            (_, _, _) => throw new InvalidOperationException(
                "합성 WinHTTP 대상별 판정 실패"));
        WindowsProxyDirectiveSourceExecutionCoordinator coordinator =
            CreateCoordinator(manual, target);
        int analyzerCalls = 0;

        WindowsProxyDirectiveSourceExecutionResult<string> result =
            coordinator.ReadAndExecuteAsync(
                    TargetUri,
                    (_, _) =>
                    {
                        analyzerCalls++;
                        return Task.FromResult("must-not-run");
                    })
                .GetAwaiter()
                .GetResult();

        Ensure(target.CallCount == 1,
            "자동 검색이 켜져 있으면 대상별 reader를 시도해야 합니다.");
        Ensure(analyzerCalls == 0,
            "대상별 판정 실패 뒤 유효한 수동 프록시로 자동 fallback하면 안 됩니다.");
        Ensure(result.Status
               == WindowsProxyDirectiveSourceExecutionStatus.Blocked
               && result.Audit?.TargetDecisionReadStatus
                   == ProxyDirectiveSourceReadStatus.Failed
               && result.Audit.SourceKind
                   == ProxyDirectiveSourceKind.TargetSpecificAutoProxy
               && result.Analysis is null,
            "대상별 실패 출처를 유지하고 분석을 차단해야 합니다.");
        Ensure(result.Snapshot?.ManualProxyDirective
               == manualDirective,
            "수동 원문은 진단 메모리에서 유지할 수 있지만 선택·실행되면 안 됩니다.");
    }

    private static void DoesNotAnalyzeTargetSpecificDirect()
    {
        RecordingManualSource manual = new(
            new WindowsManualProxyConfigurationReadResult(
                ProxyDirectiveSourceReadStatus.Success,
                ManualProxyConfigured: true,
                ManualProxyDirective:
                    "PROXY ignored-direct.example.invalid:3128",
                AutoDetectEnabled: false,
                PacConfigured: true,
                PacUrl:
                    "https://pac.example.invalid/proxy.pac"));
        RecordingTargetSource target = new((_, _, _) =>
            Task.FromResult(
                new WindowsTargetProxyDecisionReadResult(
                    ProxyDirectiveSourceReadStatus.Success,
                    IsDirect: true,
                    DirectiveText: null)));
        WindowsProxyDirectiveSourceExecutionCoordinator coordinator =
            CreateCoordinator(manual, target);
        int analyzerCalls = 0;

        WindowsProxyDirectiveSourceExecutionResult<string> result =
            coordinator.ReadAndExecuteAsync(
                    TargetUri,
                    (_, _) =>
                    {
                        analyzerCalls++;
                        return Task.FromResult("must-not-run");
                    })
                .GetAwaiter()
                .GetResult();

        Ensure(target.CallCount == 1 && analyzerCalls == 0,
            "대상별 DIRECT에서는 판정 reader 이후 프록시 분석 콜백을 호출하면 안 됩니다.");
        Ensure(result.Status
               == WindowsProxyDirectiveSourceExecutionStatus.DirectOnly
               && result.Audit?.NetworkLookupAllowed == false
               && result.Audit.DirectDirectiveCount == 1,
            "DIRECT-only와 네트워크 조회 차단 상태를 유지해야 합니다.");
    }

    private static void
        ManualReadFailureBlocksWithoutCallingTargetReader()
    {
        RecordingManualSource manual = new(
            new InvalidOperationException(
                "합성 Windows 수동 프록시 읽기 오류"));
        RecordingTargetSource target = new(
            (_, _, _) => throw new InvalidOperationException(
                "수동 설정 읽기 실패 후 대상 reader를 호출하면 안 됩니다."));
        WindowsProxyDirectiveSourceExecutionCoordinator coordinator =
            CreateCoordinator(manual, target);
        int analyzerCalls = 0;

        WindowsProxyDirectiveSourceExecutionResult<string> result =
            coordinator.ReadAndExecuteAsync(
                    TargetUri,
                    (_, _) =>
                    {
                        analyzerCalls++;
                        return Task.FromResult("must-not-run");
                    })
                .GetAwaiter()
                .GetResult();

        Ensure(manual.CallCount == 1
               && target.CallCount == 0
               && analyzerCalls == 0,
            "수동 설정 읽기 실패에서는 대상 판정과 분석 콜백을 호출하면 안 됩니다.");
        Ensure(result.Status
               == WindowsProxyDirectiveSourceExecutionStatus.Blocked
               && result.Snapshot?.ManualConfigurationStatus
                   == ProxyDirectiveSourceReadStatus.Failed
               && result.Audit?.SelectionCode
                   == ProxyDirectiveSourceSelectionCode
                       .ManualConfigurationInvalid,
            "수동 설정 읽기 실패를 Invalid·Blocked로 유지해야 합니다.");
    }

    private static void PreCanceledTokenCallsNoReaderOrAnalyzer()
    {
        RecordingManualSource manual = new(
            new WindowsManualProxyConfigurationReadResult(
                ProxyDirectiveSourceReadStatus.Success,
                true,
                "PROXY cancel.example.invalid:8080",
                true,
                false,
                null));
        RecordingTargetSource target = new((_, _, _) =>
            Task.FromResult(
                new WindowsTargetProxyDecisionReadResult(
                    ProxyDirectiveSourceReadStatus.Success,
                    false,
                    "PROXY cancel-target.example.invalid:8080")));
        WindowsProxyDirectiveSourceExecutionCoordinator coordinator =
            CreateCoordinator(manual, target);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        int analyzerCalls = 0;

        WindowsProxyDirectiveSourceExecutionResult<string> result =
            coordinator.ReadAndExecuteAsync(
                    TargetUri,
                    (_, _) =>
                    {
                        analyzerCalls++;
                        return Task.FromResult("must-not-run");
                    },
                    cancellation.Token)
                .GetAwaiter()
                .GetResult();

        Ensure(manual.CallCount == 0
               && target.CallCount == 0
               && analyzerCalls == 0,
            "사전 취소에서는 어떤 reader나 분석 콜백도 호출하면 안 됩니다.");
        Ensure(result.Status
               == WindowsProxyDirectiveSourceExecutionStatus.Canceled
               && result.Snapshot is null
               && result.Audit is null,
            "사전 취소는 스냅샷을 만들지 않은 Canceled 결과여야 합니다.");
    }

    private static void TargetCancellationStopsBeforeAnalyzer()
    {
        RecordingManualSource manual = new(
            new WindowsManualProxyConfigurationReadResult(
                ProxyDirectiveSourceReadStatus.Success,
                false,
                null,
                true,
                false,
                null));
        RecordingTargetSource target = new(
            (_, _, _) => throw new OperationCanceledException(
                "합성 대상별 판정 취소"));
        WindowsProxyDirectiveSourceExecutionCoordinator coordinator =
            CreateCoordinator(manual, target);
        int analyzerCalls = 0;

        WindowsProxyDirectiveSourceExecutionResult<string> result =
            coordinator.ReadAndExecuteAsync(
                    TargetUri,
                    (_, _) =>
                    {
                        analyzerCalls++;
                        return Task.FromResult("must-not-run");
                    })
                .GetAwaiter()
                .GetResult();

        Ensure(manual.CallCount == 1
               && target.CallCount == 1
               && analyzerCalls == 0,
            "대상별 판정 취소 후 분석 콜백을 호출하면 안 됩니다.");
        Ensure(result.Status
               == WindowsProxyDirectiveSourceExecutionStatus.Canceled
               && result.Snapshot is null
               && result.Execution is null,
            "reader 취소는 원문 스냅샷을 반환하지 않는 Canceled 결과여야 합니다.");
    }

    private static void InvalidTargetUriCallsNoReader()
    {
        RecordingManualSource manual = new(
            new WindowsManualProxyConfigurationReadResult(
                ProxyDirectiveSourceReadStatus.Success,
                false,
                null,
                false,
                false,
                null));
        RecordingTargetSource target = new((_, _, _) =>
            throw new InvalidOperationException(
                "잘못된 URL에서 target reader를 호출하면 안 됩니다."));
        WindowsProxyDirectiveSourceExecutionCoordinator coordinator =
            CreateCoordinator(manual, target);

        WindowsProxyDirectiveSourceExecutionResult<string> result =
            coordinator.ReadAndExecuteAsync(
                    new Uri("ftp://download.example.invalid/file.bin"),
                    (_, _) => Task.FromResult("must-not-run"))
                .GetAwaiter()
                .GetResult();

        Ensure(manual.CallCount == 0 && target.CallCount == 0,
            "HTTP·HTTPS가 아닌 URL은 reader 호출 전에 차단해야 합니다.");
        Ensure(result.Status
               == WindowsProxyDirectiveSourceExecutionStatus.Failed
               && result.Snapshot is null,
            "잘못된 대상 URL은 원문 스냅샷 없는 안전한 Failed 결과여야 합니다.");
    }

    private static void
        SafeJsonDoesNotExposeRawProxySourcesOrAnalysis()
    {
        const string manualDirective =
            "PROXY json-manual-private.example.invalid:3128";
        const string targetDirective =
            "PROXY json-target-private.example.invalid:8080; DIRECT";
        const string pacUrl =
            "https://json-pac-private.example.invalid/proxy.pac";
        const string analysisPayload =
            "private-analysis-payload-with-interface-guid";
        RecordingManualSource manual = new(
            new WindowsManualProxyConfigurationReadResult(
                ProxyDirectiveSourceReadStatus.Success,
                true,
                manualDirective,
                true,
                true,
                pacUrl));
        RecordingTargetSource target = new((_, _, _) =>
            Task.FromResult(
                new WindowsTargetProxyDecisionReadResult(
                    ProxyDirectiveSourceReadStatus.Success,
                    false,
                    targetDirective)));
        WindowsProxyDirectiveSourceExecutionCoordinator coordinator =
            CreateCoordinator(manual, target);

        WindowsProxyDirectiveSourceExecutionResult<string> result =
            coordinator.ReadAndExecuteAsync(
                    TargetUri,
                    (_, _) => Task.FromResult(analysisPayload))
                .GetAwaiter()
                .GetResult();
        string json = JsonSerializer.Serialize(result);
        string manualJson = JsonSerializer.Serialize(manual.Result!);
        string targetJson = JsonSerializer.Serialize(
            target.LastResult!);

        foreach (string secret in new[]
                 {
                     manualDirective,
                     targetDirective,
                     pacUrl,
                     "json-manual-private.example.invalid",
                     "json-target-private.example.invalid",
                     "json-pac-private.example.invalid",
                     analysisPayload
                 })
        {
            Ensure(!json.Contains(
                    secret,
                    StringComparison.OrdinalIgnoreCase),
                $"코디네이터 기본 JSON에 메모리 전용 값이 남았습니다: {secret}");
            Ensure(!manualJson.Contains(
                    secret,
                    StringComparison.OrdinalIgnoreCase),
                $"수동 reader 결과 JSON에 원문이 남았습니다: {secret}");
            Ensure(!targetJson.Contains(
                    secret,
                    StringComparison.OrdinalIgnoreCase),
                $"대상별 reader 결과 JSON에 원문이 남았습니다: {secret}");
        }

        Ensure(json.Contains(
                "TargetSpecificProxySelected",
                StringComparison.Ordinal)
               && json.Contains(
                   "\"proxyEndpointCount\":1",
                   StringComparison.Ordinal),
            "안전한 JSON에는 감사 계획 코드와 후보 수가 필요합니다.");
        Ensure(result.HasCompletedAnalysis
               && result.Analysis == analysisPayload,
            "분석 payload는 호출자 메모리에서는 유지해야 합니다.");
    }

    private static WindowsProxyDirectiveSourceExecutionCoordinator
        CreateCoordinator(
            RecordingManualSource manual,
            RecordingTargetSource target) =>
        new(
            new WindowsProxyDirectiveSourceSnapshotReader(
                manual,
                target,
                static () => CapturedAt));

    private static void Ensure(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed class RecordingManualSource
        : IWindowsManualProxyConfigurationSource
    {
        private readonly Exception? _exception;

        public RecordingManualSource(
            WindowsManualProxyConfigurationReadResult result)
        {
            Result = result;
        }

        public RecordingManualSource(Exception exception)
        {
            _exception = exception;
        }

        public int CallCount { get; private set; }

        public WindowsManualProxyConfigurationReadResult? Result
        {
            get;
        }

        public Task<WindowsManualProxyConfigurationReadResult>
            ReadAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            if (_exception is not null)
            {
                throw _exception;
            }

            return Task.FromResult(Result
                ?? throw new InvalidOperationException(
                    "합성 수동 프록시 결과가 없습니다."));
        }
    }

    private sealed record TargetReadRequest(
        Uri TargetUri,
        WindowsManualProxyConfigurationReadResult ManualConfiguration);

    private sealed class RecordingTargetSource
        : IWindowsTargetProxyDecisionSource
    {
        private readonly Func<Uri,
            WindowsManualProxyConfigurationReadResult,
            CancellationToken,
            Task<WindowsTargetProxyDecisionReadResult>> _handler;

        public RecordingTargetSource(
            Func<Uri,
                WindowsManualProxyConfigurationReadResult,
                CancellationToken,
                Task<WindowsTargetProxyDecisionReadResult>> handler)
        {
            _handler = handler;
        }

        public int CallCount { get; private set; }

        public List<TargetReadRequest> Requests { get; } = [];

        public WindowsTargetProxyDecisionReadResult? LastResult
        {
            get;
            private set;
        }

        public async Task<WindowsTargetProxyDecisionReadResult>
            ReadAsync(
                Uri targetUri,
                WindowsManualProxyConfigurationReadResult
                    manualConfiguration,
                CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            Requests.Add(new TargetReadRequest(
                targetUri,
                manualConfiguration));
            LastResult = await _handler(
                    targetUri,
                    manualConfiguration,
                    cancellationToken)
                .ConfigureAwait(false);
            return LastResult;
        }
    }
}
