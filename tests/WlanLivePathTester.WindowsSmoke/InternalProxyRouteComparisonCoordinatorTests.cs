using System.Runtime.CompilerServices;
using System.Text.Json;
using WlanLivePathTester.Core.NetworkEnvironment;
using WlanLivePathTester.Core.Proxy;
using WlanLivePathTester.Core.Routing;
using WlanLivePathTester.Windows.Routing;

namespace WlanLivePathTester.WindowsSmoke;

internal static class InternalProxyRouteComparisonCoordinatorTests
{
    private const string WlanInterfaceId =
        "71B2C3D4-E5F6-47A8-9123-1234567890AB";
    private const string SecretInternalTarget =
        "https://internal-secret.example.invalid/private.bin";
    private const string SecretProxyHost =
        "proxy-secret.example.invalid";
    private const string SecretInterfaceName =
        "Corporate Secret Wireless Adapter";
    private static readonly DateTimeOffset FixedNow =
        DateTimeOffset.UnixEpoch.AddDays(8);

#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Run()
    {
        InvalidProxyInputDoesNotReadAnyRoute();
        DirectPrimaryDoesNotReadAnyRoute();
        SuccessfulRunReadsInternalThenProxyAndReturnsReady();
        InternalRouteFailureSkipsProxyAnalysis();
        PreCanceledRunDoesNotReadAnyRoute();
        ReaderFailuresDoNotReflectInputOrExceptionText();
        CompletedResultDoesNotSerializeRawEvidence();
        RejectsInvalidDnsTimeout();
        Console.WriteLine(
            "PASS coordinated internal and proxy route comparison tests");
    }

    private static void InvalidProxyInputDoesNotReadAnyRoute()
    {
        RecordingInternalReader internalReader = new(
            _ => SuccessInternalRoute());
        RecordingProxyService proxyService = new(
            _ => SuccessProxyAnalysis());
        InternalProxyRouteComparisonCoordinator coordinator =
            CreateCoordinator(internalReader, proxyService);

        InternalProxyRouteComparisonRunResult result = coordinator
            .RunAsync(
                SecretInternalTarget,
                "http=http-only.example.invalid:8080",
                new Uri("https://download.example/file.bin"),
                WlanInterfaceId)
            .GetAwaiter()
            .GetResult();

        Ensure(result.Status
               == InternalProxyRouteComparisonRunStatus.InvalidInput,
            "현재 HTTPS 대상에 적용되지 않는 프록시 입력은 InvalidInput이어야 합니다.");
        Ensure(!result.InternalRouteReadPerformed
               && !result.ProxyRouteAnalysisPerformed,
            "프록시 입력이 유효하지 않으면 어떤 DNS·라우팅 조회도 시작하면 안 됩니다.");
        Ensure(internalReader.Requests.Count == 0
               && proxyService.Requests.Count == 0,
            "두 주입식 reader의 호출 횟수는 0이어야 합니다.");
    }

    private static void DirectPrimaryDoesNotReadAnyRoute()
    {
        RecordingInternalReader internalReader = new(
            _ => SuccessInternalRoute());
        RecordingProxyService proxyService = new(
            _ => SuccessProxyAnalysis());
        InternalProxyRouteComparisonCoordinator coordinator =
            CreateCoordinator(internalReader, proxyService);

        InternalProxyRouteComparisonRunResult result = coordinator
            .RunAsync(
                SecretInternalTarget,
                $"DIRECT; PROXY {SecretProxyHost}:8080",
                new Uri("https://download.example/file.bin"),
                WlanInterfaceId)
            .GetAwaiter()
            .GetResult();

        Ensure(result.Status
               == InternalProxyRouteComparisonRunStatus
                   .DirectPathSelected,
            "DIRECT가 먼저면 DirectPathSelected여야 합니다.");
        Ensure(result.ProxyDecision
               == ProxyEndpointDecision.DirectWithProxyAlternatives,
            "DIRECT 뒤 프록시 후보 순서를 보존해야 합니다.");
        Ensure(result.OperationCompleted
               && !result.HasComparableResult,
            "DIRECT 선택은 정상 완료지만 프록시 비교 결과는 없어야 합니다.");
        Ensure(internalReader.Requests.Count == 0
               && proxyService.Requests.Count == 0,
            "DIRECT 우선에서는 내부 대상과 프록시 후보 모두 DNS·라우팅 조회를 생략해야 합니다.");
    }

    private static void
        SuccessfulRunReadsInternalThenProxyAndReturnsReady()
    {
        List<string> order = [];
        RecordingInternalReader internalReader = new(_ =>
        {
            order.Add("internal");
            return SuccessInternalRoute();
        });
        RecordingProxyService proxyService = new(_ =>
        {
            order.Add("proxy");
            return SuccessProxyAnalysis();
        });
        InternalProxyRouteComparisonCoordinator coordinator =
            CreateCoordinator(internalReader, proxyService);

        InternalProxyRouteComparisonRunResult result = coordinator
            .RunAsync(
                SecretInternalTarget,
                $"PROXY {SecretProxyHost}:8080; DIRECT",
                new Uri("https://download.example/file.bin"),
                WlanInterfaceId)
            .GetAwaiter()
            .GetResult();

        Ensure(order.SequenceEqual(["internal", "proxy"]),
            "내부 기준 경로를 먼저 확인한 뒤 프록시 후보를 분석해야 합니다.");
        Ensure(result.Status
               == InternalProxyRouteComparisonRunStatus.Completed,
            "완전한 두 경로 근거는 Completed여야 합니다.");
        Ensure(result.InternalRouteReadPerformed
               && result.ProxyRouteAnalysisPerformed,
            "두 로컬 경로 단계를 모두 수행해야 합니다.");
        Ensure(result.Comparison?.Status
               == InternalProxyRouteComparisonStatus.Ready,
            "같은 인터페이스 지문은 Ready 비교여야 합니다.");
        Ensure(result.Comparison?.SameLocalInterface == true,
            "내부와 프록시의 같은 로컬 인터페이스를 확인해야 합니다.");
        Ensure(result.ParsedProxyEndpointCount == 1
               && result.AnalyzedProxyEndpointCount == 1
               && result.SuccessfulProxyEndpointCount == 1,
            "파싱·분석·성공 프록시 후보 수를 유지해야 합니다.");
        Ensure(result.DirectPresent && result.DirectFallback,
            "프록시 뒤 DIRECT fallback을 결과에 유지해야 합니다.");
        Ensure(result.ExpectedWlanIdentityAvailable,
            "유효한 Native WLAN GUID를 구조화 상태로 유지해야 합니다.");
        Ensure(result.CompletedAt == FixedNow,
            "주입한 TimeProvider의 시각을 사용해야 합니다.");
        Ensure(internalReader.Requests.Single().SafeLabel
               == "내부 DIRECT 기준 대상",
            "내부 reader에는 원문이 없는 고정 label을 전달해야 합니다.");
        Ensure(proxyService.Requests.Single()
                .Parsed.Endpoints.Single().Host
               == SecretProxyHost,
            "프록시 분석 서비스에는 로컬 DNS용 정규화 호스트가 메모리에서 전달돼야 합니다.");
    }

    private static void InternalRouteFailureSkipsProxyAnalysis()
    {
        RecordingInternalReader internalReader = new(_ =>
            FailedInternalRoute(
                DestinationRouteEvidenceStatus.RouteNotFound));
        RecordingProxyService proxyService = new(
            _ => SuccessProxyAnalysis());
        InternalProxyRouteComparisonCoordinator coordinator =
            CreateCoordinator(internalReader, proxyService);

        InternalProxyRouteComparisonRunResult result = coordinator
            .RunAsync(
                SecretInternalTarget,
                $"PROXY {SecretProxyHost}:8080",
                new Uri("https://download.example/file.bin"),
                WlanInterfaceId)
            .GetAwaiter()
            .GetResult();

        Ensure(result.Status
               == InternalProxyRouteComparisonRunStatus
                   .InternalRouteUnavailable,
            "내부 기준 경로 실패는 별도 상태여야 합니다.");
        Ensure(result.InternalRouteReadPerformed
               && !result.ProxyRouteAnalysisPerformed,
            "내부 기준이 없으면 불필요한 프록시 DNS·라우팅 조회를 시작하면 안 됩니다.");
        Ensure(internalReader.Requests.Count == 1
               && proxyService.Requests.Count == 0,
            "프록시 분석 서비스 호출은 0회여야 합니다.");
    }

    private static void PreCanceledRunDoesNotReadAnyRoute()
    {
        RecordingInternalReader internalReader = new(
            _ => SuccessInternalRoute());
        RecordingProxyService proxyService = new(
            _ => SuccessProxyAnalysis());
        InternalProxyRouteComparisonCoordinator coordinator =
            CreateCoordinator(internalReader, proxyService);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        InternalProxyRouteComparisonRunResult result = coordinator
            .RunAsync(
                SecretInternalTarget,
                $"PROXY {SecretProxyHost}:8080",
                new Uri("https://download.example/file.bin"),
                WlanInterfaceId,
                cancellationToken: cancellation.Token)
            .GetAwaiter()
            .GetResult();

        Ensure(result.Status
               == InternalProxyRouteComparisonRunStatus.Canceled,
            "사전 취소는 Canceled여야 합니다.");
        Ensure(internalReader.Requests.Count == 0
               && proxyService.Requests.Count == 0,
            "사전 취소에서는 어떤 reader도 호출하면 안 됩니다.");
    }

    private static void
        ReaderFailuresDoNotReflectInputOrExceptionText()
    {
        const string secretToken = "super-secret-error-token";
        RecordingInternalReader internalReader = new(_ =>
            throw new InvalidOperationException(
                $"{SecretInternalTarget} {secretToken}"));
        RecordingProxyService proxyService = new(
            _ => SuccessProxyAnalysis());
        InternalProxyRouteComparisonCoordinator coordinator =
            CreateCoordinator(internalReader, proxyService);

        InternalProxyRouteComparisonRunResult result = coordinator
            .RunAsync(
                SecretInternalTarget,
                $"PROXY {SecretProxyHost}:8080",
                new Uri("https://download.example/file.bin"),
                WlanInterfaceId)
            .GetAwaiter()
            .GetResult();
        string json = JsonSerializer.Serialize(result);

        Ensure(result.Status
               == InternalProxyRouteComparisonRunStatus.Failed,
            "reader 예외는 Failed 안전 결과로 변환해야 합니다.");
        foreach (string secret in new[]
                 {
                     SecretInternalTarget,
                     SecretProxyHost,
                     secretToken,
                     WlanInterfaceId
                 })
        {
            Ensure(!json.Contains(
                    secret,
                    StringComparison.OrdinalIgnoreCase),
                $"실패 결과 JSON에 입력·예외 원문이 남았습니다: {secret}");
        }
        Ensure(proxyService.Requests.Count == 0,
            "내부 reader 예외 뒤 프록시 분석을 시작하면 안 됩니다.");
    }

    private static void CompletedResultDoesNotSerializeRawEvidence()
    {
        RecordingInternalReader internalReader = new(
            _ => SuccessInternalRoute());
        RecordingProxyService proxyService = new(
            _ => SuccessProxyAnalysis());
        InternalProxyRouteComparisonRunResult result =
            CreateCoordinator(internalReader, proxyService)
                .RunAsync(
                    SecretInternalTarget,
                    $"PROXY {SecretProxyHost}:8080; DIRECT",
                    new Uri("https://download.example/file.bin"),
                    WlanInterfaceId)
                .GetAwaiter()
                .GetResult();
        string json = JsonSerializer.Serialize(result);

        Ensure(!json.Contains(
                "InternalRouteEvidence",
                StringComparison.OrdinalIgnoreCase)
               && !json.Contains(
                   "ProxyRouteAnalysis",
                   StringComparison.OrdinalIgnoreCase),
            "기본 JSON에는 원본 경로 근거 객체를 포함하면 안 됩니다.");
        foreach (string secret in new[]
                 {
                     SecretInternalTarget,
                     SecretProxyHost,
                     WlanInterfaceId,
                     SecretInterfaceName
                 })
        {
            Ensure(!json.Contains(
                    secret,
                    StringComparison.OrdinalIgnoreCase),
                $"완료 결과 JSON에 원문 식별값이 남았습니다: {secret}");
        }
        Ensure(result.InternalRouteEvidence is not null
               && result.ProxyRouteAnalysis is not null,
            "같은 프로세스의 후속 보고서에는 메모리 근거를 사용할 수 있어야 합니다.");
    }

    private static void RejectsInvalidDnsTimeout()
    {
        RecordingInternalReader internalReader = new(
            _ => SuccessInternalRoute());
        RecordingProxyService proxyService = new(
            _ => SuccessProxyAnalysis());
        InternalProxyRouteComparisonCoordinator coordinator =
            CreateCoordinator(internalReader, proxyService);

        EnsureThrows<ArgumentOutOfRangeException>(() =>
            coordinator.RunAsync(
                    SecretInternalTarget,
                    $"PROXY {SecretProxyHost}:8080",
                    new Uri("https://download.example/file.bin"),
                    WlanInterfaceId,
                    dnsTimeoutSeconds: 0)
                .GetAwaiter()
                .GetResult());
        Ensure(internalReader.Requests.Count == 0
               && proxyService.Requests.Count == 0,
            "잘못된 제한 시간에서는 reader 호출이 없어야 합니다.");
    }

    private static InternalProxyRouteComparisonCoordinator
        CreateCoordinator(
            RecordingInternalReader internalReader,
            RecordingProxyService proxyService) =>
        new(
            internalReader,
            proxyService,
            new FixedTimeProvider(FixedNow));

    private static DestinationRouteEvidence SuccessInternalRoute()
    {
        RouteInterfaceDescriptor selected = CreateInterface(
            WlanInterfaceId,
            NetworkAdapterCategory.Wireless);
        return new DestinationRouteEvidence(
            CapturedAt: FixedNow,
            TargetLabel: "내부 DIRECT 기준 대상",
            Purpose: RouteProbePurpose.InternalDirectTarget,
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
                    Message: "합성 내부 최적 경로")
            ],
            Warnings: Array.Empty<string>(),
            Message: "합성 내부 경로 성공");
    }

    private static DestinationRouteEvidence FailedInternalRoute(
        DestinationRouteEvidenceStatus status) =>
        new(
            CapturedAt: FixedNow,
            TargetLabel: "내부 DIRECT 기준 대상",
            Purpose: RouteProbePurpose.InternalDirectTarget,
            DnsWasUsed: true,
            ResolvedAddressCount: 1,
            Status: status,
            SelectedInterface: null,
            AddressEvidence:
                Array.Empty<RouteAddressEvidence>(),
            Warnings: Array.Empty<string>(),
            Message: "합성 내부 경로 실패");

    private static ProxyEndpointRouteAnalysisResult
        SuccessProxyAnalysis()
    {
        RouteInterfaceDescriptor selected = CreateInterface(
            WlanInterfaceId,
            NetworkAdapterCategory.Wireless);
        ProxyEndpointRouteEvidenceItem endpoint = new(
            Sequence: 1,
            EndpointLabel:
                "프록시 후보 1 · 모든 HTTP(S) 대상 · HTTP proxy · host#0123456789 · port 8080",
            HostFingerprint: "0123456789",
            AppliesToScheme: null,
            Transport: ProxyEndpointTransport.Http,
            Port: 8080,
            RouteStatus: DestinationRouteEvidenceStatus.Success,
            WlanCorrelationStatus:
                RouteWlanCorrelationStatus.Matched,
            SelectedInterfaceFingerprint:
                selected.IdentityFingerprint,
            SelectedInterfaceCategory: selected.Category,
            SelectedInterfaceIsVirtual: selected.IsVirtual,
            SelectedInterfaceIsVpn: selected.IsVpn,
            SelectedInterfaceIsUp: selected.IsUp,
            SelectedInterfaceHasDefaultGateway:
                selected.HasDefaultGateway,
            ResolvedAddressCount: 1,
            SuccessfulAddressCount: 1,
            FailedAddressCount: 0,
            Message: "합성 프록시 경로 성공",
            Warnings: Array.Empty<string>());
        return new ProxyEndpointRouteAnalysisResult(
            CapturedAt: FixedNow,
            Status: ProxyEndpointRouteAnalysisStatus.Success,
            SourceKind: ProxyEndpointSourceKind.AutoProxyResult,
            ProxyDecision:
                ProxyEndpointDecision.ProxyWithDirectFallback,
            TargetScheme: "https",
            DirectPresent: true,
            DirectIsPrimary: false,
            DirectFallback: true,
            DirectSequence: 2,
            ParsedEndpointCount: 1,
            ApplicableEndpointCount: 1,
            AnalyzedEndpointCount: 1,
            SkippedAfterDirectCount: 0,
            SuccessfulEndpointCount: 1,
            DistinctInterfaceCount: 1,
            Endpoints: [endpoint],
            Warnings: Array.Empty<string>(),
            Message: "합성 프록시 분석 성공",
            Limitation: "합성 프록시 분석 한계");
    }

    private static RouteInterfaceDescriptor CreateInterface(
        string interfaceId,
        NetworkAdapterCategory category) =>
        new(
            InterfaceIdentity: interfaceId,
            DisplayName: SecretInterfaceName,
            Description: SecretInterfaceName,
            NativeInterfaceType: category.ToString(),
            Category: category,
            OperationalState: NetworkAdapterOperationalState.Up,
            HasDefaultGateway: true,
            IsVirtual: false,
            IsVpn: false);

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
            $"예상 예외 {typeof(TException).Name}가 발생하지 않았습니다.");
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed record InternalReadRequest(
        string Target,
        string SafeLabel,
        int DnsTimeoutSeconds);

    private sealed class RecordingInternalReader
        : IInternalDirectRouteEvidenceReader
    {
        private readonly Func<InternalReadRequest,
            DestinationRouteEvidence> _handler;

        public RecordingInternalReader(
            Func<InternalReadRequest,
                DestinationRouteEvidence> handler)
        {
            _handler = handler;
        }

        public List<InternalReadRequest> Requests { get; } = [];

        public Task<DestinationRouteEvidence> ReadAsync(
            string target,
            string safeLabel,
            int dnsTimeoutSeconds,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            InternalReadRequest request = new(
                target,
                safeLabel,
                dnsTimeoutSeconds);
            Requests.Add(request);
            return Task.FromResult(_handler(request));
        }
    }

    private sealed record ProxyAnalysisRequest(
        ProxyEndpointParseResult Parsed,
        string? ExpectedWlanInterfaceId,
        int DnsTimeoutSeconds);

    private sealed class RecordingProxyService
        : IProxyEndpointRouteAnalysisService
    {
        private readonly Func<ProxyAnalysisRequest,
            ProxyEndpointRouteAnalysisResult> _handler;

        public RecordingProxyService(
            Func<ProxyAnalysisRequest,
                ProxyEndpointRouteAnalysisResult> handler)
        {
            _handler = handler;
        }

        public List<ProxyAnalysisRequest> Requests { get; } = [];

        public Task<ProxyEndpointRouteAnalysisResult> AnalyzeAsync(
            ProxyEndpointParseResult parsed,
            string? expectedWlanInterfaceId,
            int dnsTimeoutSeconds,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ProxyAnalysisRequest request = new(
                parsed,
                expectedWlanInterfaceId,
                dnsTimeoutSeconds);
            Requests.Add(request);
            return Task.FromResult(_handler(request));
        }
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _now;

        public FixedTimeProvider(DateTimeOffset now)
        {
            _now = now;
        }

        public override DateTimeOffset GetUtcNow() => _now;
    }
}
