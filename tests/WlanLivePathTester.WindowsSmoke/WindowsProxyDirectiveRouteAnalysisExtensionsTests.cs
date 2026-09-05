using System.Runtime.CompilerServices;
using WlanLivePathTester.Core.NetworkEnvironment;
using WlanLivePathTester.Core.Proxy;
using WlanLivePathTester.Core.Routing;
using WlanLivePathTester.Windows.Proxy;
using WlanLivePathTester.Windows.Routing;

namespace WlanLivePathTester.WindowsSmoke;

internal static class WindowsProxyDirectiveRouteAnalysisExtensionsTests
{
    private const string WlanInterfaceId =
        "E3B2C3D4-E5F6-47A8-9123-1234567890AB";
    private static readonly Uri TargetUri = new(
        "https://download.example.invalid/file.bin");

#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Run()
    {
        ApprovedTargetProxyReachesRouteAnalyzerOnce();
        FailedTargetDecisionNeverReachesRouteResolver();
        TargetDirectNeverReachesRouteResolver();
        PreCanceledExecutionCallsNothing();
        Console.WriteLine(
            "PASS approved Windows proxy directive to route analyzer tests");
    }

    private static void ApprovedTargetProxyReachesRouteAnalyzerOnce()
    {
        const string targetHost =
            "approved-route-proxy.example.invalid";
        ManualSource manual = new(
            new WindowsManualProxyConfigurationReadResult(
                ProxyDirectiveSourceReadStatus.Success,
                ManualProxyConfigured: true,
                ManualProxyDirective:
                    "PROXY ignored-manual.example.invalid:3128",
                AutoDetectEnabled: true,
                PacConfigured: false,
                PacUrl: null));
        TargetSource target = new((_, _, _) =>
            Task.FromResult(
                new WindowsTargetProxyDecisionReadResult(
                    ProxyDirectiveSourceReadStatus.Success,
                    IsDirect: false,
                    DirectiveText:
                        $"PROXY {targetHost}:8080; DIRECT")));
        RouteResolver resolver = new((host, label, _, _) =>
        {
            Ensure(host == targetHost,
                "route analyzer에는 승인된 대상별 프록시 호스트만 전달해야 합니다.");
            Ensure(!label.Contains(
                    targetHost,
                    StringComparison.OrdinalIgnoreCase),
                "route label에는 프록시 호스트 원문을 포함하면 안 됩니다.");
            return Task.FromResult(SuccessRoute(label));
        });
        WindowsProxyDirectiveSourceExecutionCoordinator coordinator =
            CreateCoordinator(manual, target);
        ProxyEndpointRouteAnalyzer analyzer = new(resolver);

        WindowsProxyDirectiveSourceExecutionResult<
            ProxyEndpointRouteAnalysisResult> result =
            coordinator.ReadAndAnalyzeRoutesAsync(
                    TargetUri,
                    analyzer,
                    WlanInterfaceId,
                    dnsTimeoutSeconds: 3,
                    endpointLimit: 4)
                .GetAwaiter()
                .GetResult();

        Ensure(manual.CallCount == 1
               && target.CallCount == 1
               && resolver.CallCount == 1,
            "수동 설정, 대상 판정과 승인된 프록시 route resolver를 각각 한 번 호출해야 합니다.");
        Ensure(result.Status
               == WindowsProxyDirectiveSourceExecutionStatus.Completed
               && result.Analysis?.Status
                   == ProxyEndpointRouteAnalysisStatus.Success,
            "승인된 대상별 프록시의 route 분석은 Completed·Success여야 합니다.");
        Ensure(result.Analysis.ProxyEndpointCount == 1
               && result.Analysis.DirectDirectiveCount == 1,
            "프록시 후보와 DIRECT fallback을 route 결과에 유지해야 합니다.");
        Ensure(result.Analysis.Entries[0].WlanCorrelationStatus
               == RouteWlanCorrelationStatus.Matched.ToString(),
            "선택 route가 현재 WLAN 전체 GUID와 일치해야 합니다.");
        Ensure(result.Audit?.PlanCode
               == ProxyDirectiveRouteAnalysisPlanCode
                   .TargetSpecificProxySelected,
            "감사 결과에 대상별 승인 계획 코드를 유지해야 합니다.");
    }

    private static void
        FailedTargetDecisionNeverReachesRouteResolver()
    {
        ManualSource manual = new(
            new WindowsManualProxyConfigurationReadResult(
                ProxyDirectiveSourceReadStatus.Success,
                ManualProxyConfigured: true,
                ManualProxyDirective:
                    "PROXY valid-manual-but-blocked.example.invalid:3128",
                AutoDetectEnabled: false,
                PacConfigured: true,
                PacUrl:
                    "https://pac.example.invalid/proxy.pac"));
        TargetSource target = new(
            (_, _, _) => throw new InvalidOperationException(
                "합성 WinHTTP 판정 실패"));
        RouteResolver resolver = new((_, _, _, _) =>
            throw new InvalidOperationException(
                "대상별 판정 실패 뒤 route resolver를 호출하면 안 됩니다."));
        WindowsProxyDirectiveSourceExecutionCoordinator coordinator =
            CreateCoordinator(manual, target);

        WindowsProxyDirectiveSourceExecutionResult<
            ProxyEndpointRouteAnalysisResult> result =
            coordinator.ReadAndAnalyzeRoutesAsync(
                    TargetUri,
                    new ProxyEndpointRouteAnalyzer(resolver),
                    WlanInterfaceId)
                .GetAwaiter()
                .GetResult();

        Ensure(target.CallCount == 1
               && resolver.CallCount == 0,
            "대상별 판정 실패 후 수동 프록시나 route resolver로 fallback하면 안 됩니다.");
        Ensure(result.Status
               == WindowsProxyDirectiveSourceExecutionStatus.Blocked
               && result.Analysis is null
               && result.Audit?.NetworkLookupAllowed == false,
            "판정 실패를 Blocked와 분석 결과 없음으로 유지해야 합니다.");
    }

    private static void TargetDirectNeverReachesRouteResolver()
    {
        ManualSource manual = new(
            new WindowsManualProxyConfigurationReadResult(
                ProxyDirectiveSourceReadStatus.Success,
                ManualProxyConfigured: true,
                ManualProxyDirective:
                    "PROXY ignored-direct-manual.example.invalid:3128",
                AutoDetectEnabled: true,
                PacConfigured: false,
                PacUrl: null));
        TargetSource target = new((_, _, _) =>
            Task.FromResult(
                new WindowsTargetProxyDecisionReadResult(
                    ProxyDirectiveSourceReadStatus.Success,
                    IsDirect: true,
                    DirectiveText: null)));
        RouteResolver resolver = new((_, _, _, _) =>
            throw new InvalidOperationException(
                "DIRECT-only에서 route resolver를 호출하면 안 됩니다."));
        WindowsProxyDirectiveSourceExecutionCoordinator coordinator =
            CreateCoordinator(manual, target);

        WindowsProxyDirectiveSourceExecutionResult<
            ProxyEndpointRouteAnalysisResult> result =
            coordinator.ReadAndAnalyzeRoutesAsync(
                    TargetUri,
                    new ProxyEndpointRouteAnalyzer(resolver),
                    WlanInterfaceId)
                .GetAwaiter()
                .GetResult();

        Ensure(target.CallCount == 1
               && resolver.CallCount == 0,
            "DIRECT-only에서는 프록시 route resolver 호출이 없어야 합니다.");
        Ensure(result.Status
               == WindowsProxyDirectiveSourceExecutionStatus.DirectOnly
               && result.Analysis is null,
            "DIRECT-only를 route 분석 성공으로 오인하면 안 됩니다.");
    }

    private static void PreCanceledExecutionCallsNothing()
    {
        ManualSource manual = new(
            new WindowsManualProxyConfigurationReadResult(
                ProxyDirectiveSourceReadStatus.Success,
                true,
                "PROXY canceled.example.invalid:8080",
                true,
                false,
                null));
        TargetSource target = new((_, _, _) =>
            Task.FromResult(
                new WindowsTargetProxyDecisionReadResult(
                    ProxyDirectiveSourceReadStatus.Success,
                    false,
                    "PROXY canceled-target.example.invalid:8080")));
        RouteResolver resolver = new((_, _, _, _) =>
            Task.FromResult(SuccessRoute("must-not-run")));
        WindowsProxyDirectiveSourceExecutionCoordinator coordinator =
            CreateCoordinator(manual, target);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        WindowsProxyDirectiveSourceExecutionResult<
            ProxyEndpointRouteAnalysisResult> result =
            coordinator.ReadAndAnalyzeRoutesAsync(
                    TargetUri,
                    new ProxyEndpointRouteAnalyzer(resolver),
                    WlanInterfaceId,
                    cancellationToken: cancellation.Token)
                .GetAwaiter()
                .GetResult();

        Ensure(manual.CallCount == 0
               && target.CallCount == 0
               && resolver.CallCount == 0,
            "사전 취소에서는 Windows source와 route resolver를 모두 호출하면 안 됩니다.");
        Ensure(result.Status
               == WindowsProxyDirectiveSourceExecutionStatus.Canceled,
            "사전 취소 결과는 Canceled여야 합니다.");
    }

    private static DestinationRouteEvidence SuccessRoute(
        string targetLabel)
    {
        RouteInterfaceDescriptor selected = new(
            InterfaceIdentity: WlanInterfaceId,
            DisplayName: "Synthetic Wi-Fi",
            Description: "Synthetic Wi-Fi Adapter",
            NativeInterfaceType: "Wireless80211",
            Category: NetworkAdapterCategory.Wireless,
            OperationalState: NetworkAdapterOperationalState.Up,
            HasDefaultGateway: true,
            IsVirtual: false,
            IsVpn: false);
        return new DestinationRouteEvidence(
            CapturedAt: DateTimeOffset.UnixEpoch,
            TargetLabel: targetLabel,
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
                    Message: "합성 Windows 최적 경로")
            ],
            Warnings: Array.Empty<string>(),
            Message: "합성 route 성공");
    }

    private static WindowsProxyDirectiveSourceExecutionCoordinator
        CreateCoordinator(
            ManualSource manual,
            TargetSource target) =>
        new(
            new WindowsProxyDirectiveSourceSnapshotReader(
                manual,
                target,
                static () => DateTimeOffset.UnixEpoch.AddDays(12)));

    private static void Ensure(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed class ManualSource
        : IWindowsManualProxyConfigurationSource
    {
        private readonly WindowsManualProxyConfigurationReadResult
            _result;

        public ManualSource(
            WindowsManualProxyConfigurationReadResult result)
        {
            _result = result;
        }

        public int CallCount { get; private set; }

        public Task<WindowsManualProxyConfigurationReadResult>
            ReadAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            return Task.FromResult(_result);
        }
    }

    private sealed class TargetSource
        : IWindowsTargetProxyDecisionSource
    {
        private readonly Func<Uri,
            WindowsManualProxyConfigurationReadResult,
            CancellationToken,
            Task<WindowsTargetProxyDecisionReadResult>> _handler;

        public TargetSource(
            Func<Uri,
                WindowsManualProxyConfigurationReadResult,
                CancellationToken,
                Task<WindowsTargetProxyDecisionReadResult>> handler)
        {
            _handler = handler;
        }

        public int CallCount { get; private set; }

        public Task<WindowsTargetProxyDecisionReadResult> ReadAsync(
            Uri targetUri,
            WindowsManualProxyConfigurationReadResult manualConfiguration,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            return _handler(
                targetUri,
                manualConfiguration,
                cancellationToken);
        }
    }

    private sealed class RouteResolver
        : IProxyEndpointRouteResolver
    {
        private readonly Func<string,
            string,
            int,
            CancellationToken,
            Task<DestinationRouteEvidence>> _handler;

        public RouteResolver(
            Func<string,
                string,
                int,
                CancellationToken,
                Task<DestinationRouteEvidence>> handler)
        {
            _handler = handler;
        }

        public int CallCount { get; private set; }

        public Task<DestinationRouteEvidence> ResolveAsync(
            string host,
            string redactedTargetLabel,
            int dnsTimeoutSeconds,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            return _handler(
                host,
                redactedTargetLabel,
                dnsTimeoutSeconds,
                cancellationToken);
        }
    }
}
