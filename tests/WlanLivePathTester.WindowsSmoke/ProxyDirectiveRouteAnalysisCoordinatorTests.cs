using System.Runtime.CompilerServices;
using System.Text.Json;
using WlanLivePathTester.Core.NetworkEnvironment;
using WlanLivePathTester.Core.Proxy;
using WlanLivePathTester.Core.Routing;
using WlanLivePathTester.Windows.Routing;

namespace WlanLivePathTester.WindowsSmoke;

internal static class ProxyDirectiveRouteAnalysisCoordinatorTests
{
    private const string WlanInterfaceId =
        "71B2C3D4-E5F6-47A8-9123-1234567890AB";
    private const string EthernetInterfaceId =
        "81B2C3D4-E5F6-47A8-9123-1234567890AB";
    private static readonly DateTimeOffset CapturedAt =
        DateTimeOffset.UnixEpoch.AddDays(8);

#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Run()
    {
        TargetSpecificProxyOverridesManualAndUsesExistingAnalyzer();
        TargetSpecificDirectPerformsNoRouteRead();
        FailedTargetDecisionNeverFallsBackToManual();
        ManualSchemeSelectionUsesOnlyTheTargetScheme();
        PreCanceledExecutionPerformsNoRouteRead();
        ReaderExceptionDoesNotLeakHostTokenOrInterfaceIdentity();
        InvalidTargetUriAndTimeoutFailBeforeRouteRead();
        Console.WriteLine(
            "PASS proxy source to existing Windows route analyzer coordinator tests");
    }

    private static void
        TargetSpecificProxyOverridesManualAndUsesExistingAnalyzer()
    {
        const string targetHost = "target-route.example.invalid";
        const string manualHost = "manual-route.example.invalid";
        RecordingReader reader = new((host, label, _, _) =>
            Task.FromResult(CreateSuccessRoute(
                label,
                WlanInterfaceId,
                NetworkAdapterCategory.Wireless)));
        ProxyDirectiveRouteAnalysisCoordinator coordinator =
            CreateCoordinator(reader);
        ProxyDirectiveSourceSnapshot snapshot = new(
            CapturedAt,
            ProxyDirectiveSourceReadStatus.Success,
            targetDecisionIsDirect: false,
            targetSpecificDirective:
                $"PROXY {targetHost}:8080; DIRECT",
            ProxyDirectiveSourceReadStatus.Success,
            manualProxyConfigured: true,
            manualProxyDirective:
                $"PROXY {manualHost}:3128",
            autoDetectEnabled: true,
            pacConfigured: true);

        ProxyDirectiveRouteAnalysisExecutionResult<
            ProxyEndpointRouteAnalysisResult> execution =
            coordinator.ExecuteAsync(
                    snapshot,
                    new Uri("https://download.example/file.bin"),
                    WlanInterfaceId)
                .GetAwaiter()
                .GetResult();

        Ensure(execution.Status
               == ProxyDirectiveRouteAnalysisExecutionStatus.Completed
               && execution.PlanCode
                   == ProxyDirectiveRouteAnalysisPlanCode
                       .TargetSpecificProxySelected,
            "대상별 프록시는 승인된 실행 계획으로 완료돼야 합니다.");
        Ensure(reader.Requests.Count == 1
               && reader.Requests[0].Host == targetHost,
            "대상별 프록시 호스트만 Windows 경로 reader에 전달해야 합니다.");
        Ensure(!reader.Requests[0].SafeLabel.Contains(
                targetHost,
                StringComparison.OrdinalIgnoreCase)
               && !reader.Requests[0].SafeLabel.Contains(
                   manualHost,
                   StringComparison.OrdinalIgnoreCase),
            "reader용 안전 라벨에 대상별·수동 프록시 원문이 없어야 합니다.");

        ProxyEndpointRouteAnalysisResult analysis =
            execution.Analysis
            ?? throw new InvalidOperationException(
                "완료 실행에 메모리 경로 분석 결과가 필요합니다.");
        Ensure(analysis.Status
               == ProxyEndpointRouteAnalysisStatus.Success,
            "기존 Windows 프록시 경로 분석기의 성공 상태를 유지해야 합니다.");
        Ensure(analysis.DirectFallback
               && analysis.DirectSequence == 2
               && analysis.AnalyzedEndpointCount == 1,
            "프록시 뒤 DIRECT fallback 순서와 분석 후보 수를 유지해야 합니다.");
        Ensure(analysis.Endpoints.Single().WlanCorrelationStatus
               == RouteWlanCorrelationStatus.Matched,
            "현재 WLAN과 같은 Windows 인터페이스를 Matched로 표시해야 합니다.");
        Ensure(!JsonSerializer.Serialize(analysis).Contains(
                targetHost,
                StringComparison.OrdinalIgnoreCase),
            "구조화 경로 분석 결과에 프록시 호스트 원문이 남으면 안 됩니다.");
    }

    private static void TargetSpecificDirectPerformsNoRouteRead()
    {
        RecordingReader reader = new((_, _, _, _) =>
            throw new InvalidOperationException(
                "DIRECT-only 실행에서 route reader를 호출하면 안 됩니다."));
        ProxyDirectiveSourceSnapshot snapshot = new(
            CapturedAt,
            ProxyDirectiveSourceReadStatus.Success,
            targetDecisionIsDirect: true,
            targetSpecificDirective: null,
            ProxyDirectiveSourceReadStatus.Success,
            manualProxyConfigured: true,
            manualProxyDirective:
                "PROXY ignored-manual.example.invalid:8080",
            autoDetectEnabled: true,
            pacConfigured: true);

        ProxyDirectiveRouteAnalysisExecutionResult<
            ProxyEndpointRouteAnalysisResult> execution =
            CreateCoordinator(reader).ExecuteAsync(
                    snapshot,
                    new Uri("https://download.example/file.bin"),
                    WlanInterfaceId)
                .GetAwaiter()
                .GetResult();

        Ensure(execution.Status
               == ProxyDirectiveRouteAnalysisExecutionStatus.DirectOnly,
            "대상별 DIRECT는 DirectOnly 실행이어야 합니다.");
        Ensure(execution.PlanCode
               == ProxyDirectiveRouteAnalysisPlanCode
                   .TargetSpecificDirect,
            "대상별 DIRECT 계획 코드를 유지해야 합니다.");
        Ensure(execution.Analysis is null
               && reader.Requests.Count == 0,
            "DIRECT-only에서는 parser callback과 route reader를 실행하면 안 됩니다.");
    }

    private static void FailedTargetDecisionNeverFallsBackToManual()
    {
        const string manualHost = "valid-manual.example.invalid";
        RecordingReader reader = new((_, _, _, _) =>
            throw new InvalidOperationException(
                "실패한 대상별 판정을 수동 프록시로 대체하면 안 됩니다."));
        ProxyDirectiveSourceSnapshot snapshot = new(
            CapturedAt,
            ProxyDirectiveSourceReadStatus.Failed,
            targetDecisionIsDirect: false,
            targetSpecificDirective: null,
            ProxyDirectiveSourceReadStatus.Success,
            manualProxyConfigured: true,
            manualProxyDirective:
                $"PROXY {manualHost}:8080",
            autoDetectEnabled: true,
            pacConfigured: true);

        ProxyDirectiveRouteAnalysisExecutionResult<
            ProxyEndpointRouteAnalysisResult> execution =
            CreateCoordinator(reader).ExecuteAsync(
                    snapshot,
                    new Uri("https://download.example/file.bin"),
                    WlanInterfaceId)
                .GetAwaiter()
                .GetResult();

        Ensure(execution.Status
               == ProxyDirectiveRouteAnalysisExecutionStatus.Blocked,
            "대상별 판정 실패는 Blocked 실행이어야 합니다.");
        Ensure(execution.PlanCode
               == ProxyDirectiveRouteAnalysisPlanCode
                   .InvalidSourceDecision,
            "잘못된 대상별 출처 계획 코드를 유지해야 합니다.");
        Ensure(execution.Analysis is null
               && reader.Requests.Count == 0,
            "유효한 수동 프록시가 있어도 reader 호출이 없어야 합니다.");
        Ensure(!JsonSerializer.Serialize(execution).Contains(
                manualHost,
                StringComparison.OrdinalIgnoreCase),
            "선택하지 않은 수동 프록시 호스트가 실행 결과에 남으면 안 됩니다.");
    }

    private static void ManualSchemeSelectionUsesOnlyTheTargetScheme()
    {
        const string httpHost = "manual-http.example.invalid";
        const string httpsHost = "manual-https.example.invalid";
        RecordingReader reader = new((_, label, _, _) =>
            Task.FromResult(CreateSuccessRoute(
                label,
                EthernetInterfaceId,
                NetworkAdapterCategory.Ethernet)));
        ProxyDirectiveSourceSnapshot snapshot = new(
            CapturedAt,
            ProxyDirectiveSourceReadStatus.NotAttempted,
            targetDecisionIsDirect: false,
            targetSpecificDirective: null,
            ProxyDirectiveSourceReadStatus.Success,
            manualProxyConfigured: true,
            manualProxyDirective:
                $"http={httpHost}:8080;https={httpsHost}:8443",
            autoDetectEnabled: false,
            pacConfigured: false);

        ProxyDirectiveRouteAnalysisExecutionResult<
            ProxyEndpointRouteAnalysisResult> execution =
            CreateCoordinator(reader).ExecuteAsync(
                    snapshot,
                    new Uri("https://download.example/file.bin"),
                    WlanInterfaceId)
                .GetAwaiter()
                .GetResult();

        Ensure(execution.Status
               == ProxyDirectiveRouteAnalysisExecutionStatus.Completed
               && execution.PlanCode
                   == ProxyDirectiveRouteAnalysisPlanCode
                       .ManualProxySelected,
            "대상별 판정 미시도에서는 수동 프록시 계획을 실행해야 합니다.");
        Ensure(reader.Requests.Count == 1
               && reader.Requests[0].Host == httpsHost,
            "HTTPS 대상에는 수동 https= 후보만 조회해야 합니다.");
        Ensure(!reader.Requests.Any(request =>
                request.Host == httpHost),
            "HTTPS 대상에 http= 후보를 임의 fallback하면 안 됩니다.");

        ProxyEndpointRouteAnalysisResult analysis =
            execution.Analysis!;
        Ensure(analysis.Status
               == ProxyEndpointRouteAnalysisStatus.Success
               && analysis.TargetScheme == "https"
               && analysis.ApplicableEndpointCount == 1
               && analysis.AnalyzedEndpointCount == 1,
            "기존 target-aware parser의 정확한 HTTPS 후보 선택을 유지해야 합니다.");
        Ensure(analysis.Endpoints.Single().WlanCorrelationStatus
               == RouteWlanCorrelationStatus.DifferentInterface,
            "유선 프록시 경로를 현재 WLAN과 다른 인터페이스로 표시해야 합니다.");
    }

    private static void PreCanceledExecutionPerformsNoRouteRead()
    {
        RecordingReader reader = new((_, _, _, _) =>
            throw new InvalidOperationException(
                "사전 취소 실행에서 route reader를 호출하면 안 됩니다."));
        using CancellationTokenSource source = new();
        source.Cancel();
        ProxyDirectiveSourceSelectionResult selection =
            ProxyDirectiveSourceSelectionPolicy.Select(
                targetDecisionWasEvaluated: true,
                targetDecisionIsDirect: false,
                targetSpecificDirective:
                    "PROXY canceled.example.invalid:8080",
                manualProxyConfigured: false,
                manualProxyDirective: null);

        ProxyDirectiveRouteAnalysisExecutionResult<
            ProxyEndpointRouteAnalysisResult> execution =
            CreateCoordinator(reader).ExecuteAsync(
                    selection,
                    new Uri("https://download.example/file.bin"),
                    WlanInterfaceId,
                    cancellationToken: source.Token)
                .GetAwaiter()
                .GetResult();

        Ensure(execution.Status
               == ProxyDirectiveRouteAnalysisExecutionStatus.Canceled
               && execution.Analysis is null,
            "사전 취소는 Canceled이며 분석 결과가 없어야 합니다.");
        Ensure(reader.Requests.Count == 0,
            "사전 취소에서는 parser callback과 route reader를 실행하면 안 됩니다.");
    }

    private static void
        ReaderExceptionDoesNotLeakHostTokenOrInterfaceIdentity()
    {
        const string secretHost =
            "exception-private-proxy.example.invalid";
        const string secretToken = "super-secret-route-token";
        RecordingReader reader = new((_, _, _, _) =>
            throw new InvalidOperationException(
                $"{secretHost} {secretToken} {WlanInterfaceId}"));
        ProxyDirectiveSourceSelectionResult selection =
            ProxyDirectiveSourceSelectionPolicy.Select(
                targetDecisionWasEvaluated: true,
                targetDecisionIsDirect: false,
                targetSpecificDirective:
                    $"PROXY {secretHost}:8080",
                manualProxyConfigured: false,
                manualProxyDirective: null);

        ProxyDirectiveRouteAnalysisExecutionResult<
            ProxyEndpointRouteAnalysisResult> execution =
            CreateCoordinator(reader).ExecuteAsync(
                    selection,
                    new Uri("https://download.example/file.bin"),
                    WlanInterfaceId)
                .GetAwaiter()
                .GetResult();

        Ensure(execution.Status
               == ProxyDirectiveRouteAnalysisExecutionStatus.Completed,
            "reader 후보 실패는 분석 객체 생성 자체를 실패시키면 안 됩니다.");
        ProxyEndpointRouteAnalysisResult analysis =
            execution.Analysis!;
        Ensure(analysis.Status
               == ProxyEndpointRouteAnalysisStatus.Failed,
            "모든 프록시 후보의 reader 예외는 구조화 Failed여야 합니다.");
        string json = JsonSerializer.Serialize(analysis);
        foreach (string secret in new[]
                 {
                     secretHost,
                     secretToken,
                     WlanInterfaceId
                 })
        {
            Ensure(!json.Contains(
                    secret,
                    StringComparison.OrdinalIgnoreCase),
                $"경로 분석 결과에 reader 예외 원문이 남았습니다: {secret}");
        }
        Ensure(analysis.Endpoints.Single().Message.Contains(
                "예외 원문은 결과에 포함하지 않았습니다",
                StringComparison.Ordinal),
            "reader 예외가 마스킹됐다는 고정 설명이 필요합니다.");
    }

    private static void InvalidTargetUriAndTimeoutFailBeforeRouteRead()
    {
        RecordingReader reader = new((_, _, _, _) =>
            throw new InvalidOperationException(
                "잘못된 입력에서 route reader를 호출하면 안 됩니다."));
        ProxyDirectiveSourceSelectionResult selection =
            ProxyDirectiveSourceSelectionPolicy.Select(
                targetDecisionWasEvaluated: true,
                targetDecisionIsDirect: false,
                targetSpecificDirective:
                    "PROXY proxy.example.invalid:8080",
                manualProxyConfigured: false,
                manualProxyDirective: null);
        ProxyDirectiveRouteAnalysisCoordinator coordinator =
            CreateCoordinator(reader);

        EnsureThrows<ArgumentException>(() =>
            coordinator.ExecuteAsync(
                    selection,
                    new Uri("ftp://download.example/file.bin"),
                    WlanInterfaceId)
                .GetAwaiter()
                .GetResult());
        EnsureThrows<ArgumentOutOfRangeException>(() =>
            coordinator.ExecuteAsync(
                    selection,
                    new Uri("https://download.example/file.bin"),
                    WlanInterfaceId,
                    dnsTimeoutSeconds: 0)
                .GetAwaiter()
                .GetResult());
        Ensure(reader.Requests.Count == 0,
            "잘못된 대상 URL·DNS 제한 시간에서는 route reader 호출이 없어야 합니다.");
    }

    private static ProxyDirectiveRouteAnalysisCoordinator
        CreateCoordinator(RecordingReader reader) =>
        new(new ProxyEndpointRouteAnalyzer(reader));

    private static DestinationRouteEvidence CreateSuccessRoute(
        string safeLabel,
        string interfaceId,
        NetworkAdapterCategory category)
    {
        RouteInterfaceDescriptor selected = new(
            InterfaceIdentity: interfaceId,
            DisplayName: "Synthetic Private Adapter",
            Description: "Synthetic Private Adapter Description",
            NativeInterfaceType: category.ToString(),
            Category: category,
            OperationalState: NetworkAdapterOperationalState.Up,
            HasDefaultGateway: true,
            IsVirtual: category == NetworkAdapterCategory.Tunnel,
            IsVpn: category == NetworkAdapterCategory.Tunnel);
        return new DestinationRouteEvidence(
            CapturedAt: CapturedAt,
            TargetLabel: safeLabel,
            Purpose: RouteProbePurpose.ProxyEndpoint,
            DnsWasUsed: true,
            ResolvedAddressCount: 1,
            Status: DestinationRouteEvidenceStatus.Success,
            SelectedInterface: selected,
            AddressEvidence:
            [
                new RouteAddressEvidence(
                    RouteAddressFamilyKind.IPv4,
                    RouteAddressEvidenceStatus.Success,
                    selected,
                    NativeErrorCode: null,
                    Message: "합성 Windows 최적 인터페이스 확인")
            ],
            Warnings: Array.Empty<string>(),
            Message: "합성 Windows 최적 인터페이스 확인");
    }

    private static void EnsureThrows<TException>(Action action)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }

        throw new InvalidOperationException(
            $"예상 예외가 발생하지 않았습니다: {typeof(TException).Name}");
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed record RouteReadRequest(
        string Host,
        string SafeLabel,
        int DnsTimeoutSeconds);

    private sealed class RecordingReader
        : IProxyEndpointRouteEvidenceReader
    {
        private readonly Func<string, string, int, CancellationToken,
            Task<DestinationRouteEvidence>> _handler;

        public RecordingReader(
            Func<string, string, int, CancellationToken,
                Task<DestinationRouteEvidence>> handler)
        {
            _handler = handler;
        }

        public List<RouteReadRequest> Requests { get; } = [];

        public Task<DestinationRouteEvidence> ReadAsync(
            string host,
            string safeLabel,
            int dnsTimeoutSeconds,
            CancellationToken cancellationToken)
        {
            Requests.Add(new RouteReadRequest(
                host,
                safeLabel,
                dnsTimeoutSeconds));
            return _handler(
                host,
                safeLabel,
                dnsTimeoutSeconds,
                cancellationToken);
        }
    }
}
