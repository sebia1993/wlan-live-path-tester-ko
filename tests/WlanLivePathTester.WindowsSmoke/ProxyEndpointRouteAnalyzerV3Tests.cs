using System.Runtime.CompilerServices;
using System.Text.Json;
using WlanLivePathTester.Core.NetworkEnvironment;
using WlanLivePathTester.Core.Proxy;
using WlanLivePathTester.Core.Routing;
using WlanLivePathTester.Windows.Routing;

namespace WlanLivePathTester.WindowsSmoke;

internal static class ProxyEndpointRouteAnalyzerV3Tests
{
    private const string WlanInterfaceId =
        "71B2C3D4-E5F6-47A8-9123-1234567890AB";
    private const string EthernetInterfaceId =
        "81B2C3D4-E5F6-47A8-9123-1234567890AB";

#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Run()
    {
        DirectPrimarySkipsAllRouteReads();
        AnalyzesOnlyProxyCandidatesBeforeDirect();
        DetectsMultipleLocalInterfaces();
        PreservesPartialAndCanceledResults();
        InvalidAndInapplicableInputsDoNotReadRoutes();
        RemovesEndpointAndInterfaceIdentifiers();
        RejectsInvalidDnsTimeout();
        Console.WriteLine(
            "PASS proxy endpoint local route analyzer v3 tests");
    }

    private static void DirectPrimarySkipsAllRouteReads()
    {
        ProxyEndpointParseResult parsed = ProxyEndpointParser.Parse(
            "DIRECT; PROXY later-proxy.example:8080",
            new Uri("https://download.example/file.bin"));
        RecordingRouteReader reader = new([]);

        ProxyEndpointRouteAnalysisResult result = Analyze(
            parsed,
            reader);

        Ensure(result.Status
               == ProxyEndpointRouteAnalysisStatus.DirectPathSelected,
            "DIRECT가 첫 경로이면 프록시 경로를 분석하면 안 됩니다.");
        Ensure(result.DirectIsPrimary
               && !result.DirectFallback,
            "DIRECT 우선과 프록시 뒤 DIRECT fallback을 구분해야 합니다.");
        Ensure(result.AnalyzedEndpointCount == 0
               && result.SkippedAfterDirectCount == 1
               && reader.Requests.Count == 0,
            "DIRECT 뒤 프록시에는 DNS·라우팅 reader 호출이 없어야 합니다.");
    }

    private static void AnalyzesOnlyProxyCandidatesBeforeDirect()
    {
        ProxyEndpointParseResult parsed = ProxyEndpointParser.Parse(
            "PROXY first-proxy.example:8080; HTTPS second-proxy.example:8443; DIRECT; PROXY after-direct.example:8081",
            new Uri("https://download.example/file.bin"));
        RecordingRouteReader reader = new(
        [
            SuccessEvidence(
                "first",
                WlanInterfaceId,
                NetworkAdapterCategory.Wireless),
            SuccessEvidence(
                "second",
                WlanInterfaceId,
                NetworkAdapterCategory.Wireless)
        ]);

        ProxyEndpointRouteAnalysisResult result = Analyze(
            parsed,
            reader);

        Ensure(result.Status
               == ProxyEndpointRouteAnalysisStatus.Success,
            "같은 인터페이스의 두 프록시 경로는 Success여야 합니다.");
        Ensure(result.DirectFallback
               && !result.DirectIsPrimary
               && result.DirectSequence == 3,
            "프록시 뒤 DIRECT fallback 순서를 보존해야 합니다.");
        Ensure(result.ApplicableEndpointCount == 3
               && result.AnalyzedEndpointCount == 2
               && result.SkippedAfterDirectCount == 1,
            "DIRECT 앞 후보만 분석하고 뒤 후보는 건너뛰어야 합니다.");
        Ensure(reader.Requests.Select(item => item.Host)
                .SequenceEqual(
                [
                    "first-proxy.example",
                    "second-proxy.example"
                ]),
            "reader 호출 순서는 프록시 route 순서와 같아야 합니다.");
        Ensure(result.Endpoints.All(item =>
                item.WlanCorrelationStatus
                    == RouteWlanCorrelationStatus.Matched),
            "선택 인터페이스가 현재 WLAN GUID와 일치해야 합니다.");
        Ensure(result.DistinctInterfaceCount == 1,
            "동일 인터페이스 지문은 하나로 집계해야 합니다.");
        Ensure(result.Warnings.Any(message => message.Contains(
                "DIRECT fallback",
                StringComparison.Ordinal)),
            "실제 DIRECT 전환을 확정하지 못한다는 한계가 필요합니다.");
    }

    private static void DetectsMultipleLocalInterfaces()
    {
        ProxyEndpointParseResult parsed = ProxyEndpointParser.Parse(
            "PROXY wireless-proxy.example:8080; PROXY ethernet-proxy.example:8080");
        RecordingRouteReader reader = new(
        [
            SuccessEvidence(
                "wireless",
                WlanInterfaceId,
                NetworkAdapterCategory.Wireless),
            SuccessEvidence(
                "ethernet",
                EthernetInterfaceId,
                NetworkAdapterCategory.Ethernet)
        ]);

        ProxyEndpointRouteAnalysisResult result = Analyze(
            parsed,
            reader);

        Ensure(result.Status
               == ProxyEndpointRouteAnalysisStatus.MultipleInterfaces,
            "프록시 후보가 서로 다른 로컬 NIC를 사용하면 별도 상태여야 합니다.");
        Ensure(result.SuccessfulEndpointCount == 2
               && result.DistinctInterfaceCount == 2,
            "성공 후보와 서로 다른 인터페이스 수를 집계해야 합니다.");
        Ensure(result.Endpoints[0].WlanCorrelationStatus
               == RouteWlanCorrelationStatus.Matched,
            "무선 경로는 현재 WLAN과 일치해야 합니다.");
        Ensure(result.Endpoints[1].WlanCorrelationStatus
               == RouteWlanCorrelationStatus.DifferentInterface
               && result.Endpoints[1].SelectedInterfaceCategory
                   == NetworkAdapterCategory.Ethernet,
            "유선 경로는 현재 WLAN과 다른 인터페이스로 표시해야 합니다.");
    }

    private static void PreservesPartialAndCanceledResults()
    {
        ProxyEndpointParseResult parsed = ProxyEndpointParser.Parse(
            "PROXY success-proxy.example:8080; PROXY missing-route.example:8080");
        RecordingRouteReader partialReader = new(
        [
            SuccessEvidence(
                "success",
                WlanInterfaceId,
                NetworkAdapterCategory.Wireless),
            FailedEvidence(
                DestinationRouteEvidenceStatus.RouteNotFound,
                "합성 경로 없음")
        ]);

        ProxyEndpointRouteAnalysisResult partial = Analyze(
            parsed,
            partialReader);
        Ensure(partial.Status
               == ProxyEndpointRouteAnalysisStatus.PartialSuccess
               && partial.SuccessfulEndpointCount == 1
               && partial.AnalyzedEndpointCount == 2,
            "일부 후보만 성공하면 PartialSuccess와 집계를 유지해야 합니다.");

        RecordingRouteReader canceledReader = new(
        [
            SuccessEvidence(
                "success",
                WlanInterfaceId,
                NetworkAdapterCategory.Wireless),
            FailedEvidence(
                DestinationRouteEvidenceStatus.Canceled,
                "합성 사용자 취소")
        ]);
        ProxyEndpointRouteAnalysisResult canceled = Analyze(
            parsed,
            canceledReader);
        Ensure(canceled.Status
               == ProxyEndpointRouteAnalysisStatus.Canceled
               && canceled.AnalyzedEndpointCount == 2
               && canceled.SuccessfulEndpointCount == 1,
            "취소 전 완료된 후보 결과만 보존해야 합니다.");
    }

    private static void InvalidAndInapplicableInputsDoNotReadRoutes()
    {
        RecordingRouteReader invalidReader = new([]);
        ProxyEndpointParseResult invalid = ProxyEndpointParser.Parse(
            "PROXY proxy.example:8080",
            new Uri("ftp://download.example/file.bin"));
        ProxyEndpointRouteAnalysisResult invalidResult = Analyze(
            invalid,
            invalidReader);
        Ensure(invalidResult.Status
               == ProxyEndpointRouteAnalysisStatus.InvalidInput
               && invalidReader.Requests.Count == 0,
            "파서 오류에서는 route reader를 호출하면 안 됩니다.");

        RecordingRouteReader noEndpointReader = new([]);
        ProxyEndpointParseResult noEndpoint =
            ProxyEndpointParser.Parse(
                "http=http-only-proxy.example:8080",
                new Uri("https://download.example/file.bin"));
        ProxyEndpointRouteAnalysisResult noEndpointResult = Analyze(
            noEndpoint,
            noEndpointReader);
        Ensure(noEndpointResult.Status
               == ProxyEndpointRouteAnalysisStatus.NoApplicableEndpoint
               && noEndpointReader.Requests.Count == 0,
            "현재 대상에 적용되는 프록시가 없으면 reader 호출이 없어야 합니다.");
    }

    private static void RemovesEndpointAndInterfaceIdentifiers()
    {
        const string host = "secret-proxy.internal.example";
        const string interfaceName = "Corporate Secret Wireless";
        const string interfaceDescription =
            "Private Adapter Description";
        const string email = "route-user@example.invalid";
        const string ip = "10.20.30.40";
        const string url =
            "https://internal.example.invalid/private.bin";
        ProxyEndpointParseResult parsed = ProxyEndpointParser.Parse(
            $"PROXY {host}:8080");
        RouteInterfaceDescriptor descriptor = new(
            InterfaceIdentity: WlanInterfaceId,
            DisplayName: interfaceName,
            Description: interfaceDescription,
            NativeInterfaceType: "Wireless80211",
            Category: NetworkAdapterCategory.Wireless,
            OperationalState: NetworkAdapterOperationalState.Up,
            HasDefaultGateway: true,
            IsVirtual: false,
            IsVpn: false);
        string unsafeMessage =
            $"route {host} via {WlanInterfaceId} {interfaceName} {interfaceDescription} {email} {ip} {url}";
        DestinationRouteEvidence unsafeEvidence = new(
            CapturedAt: DateTimeOffset.UnixEpoch,
            TargetLabel: "unsafe",
            Purpose: RouteProbePurpose.ProxyEndpoint,
            DnsWasUsed: true,
            ResolvedAddressCount: 1,
            Status: DestinationRouteEvidenceStatus.Success,
            SelectedInterface: descriptor,
            AddressEvidence:
            [
                new RouteAddressEvidence(
                    RouteAddressFamilyKind.IPv4,
                    RouteAddressEvidenceStatus.Success,
                    descriptor,
                    NativeErrorCode: null,
                    Message: unsafeMessage)
            ],
            Warnings: [unsafeMessage],
            Message: unsafeMessage);
        RecordingRouteReader reader = new([unsafeEvidence]);

        ProxyEndpointRouteAnalysisResult result = Analyze(
            parsed,
            reader);
        string serialized = JsonSerializer.Serialize(result);
        foreach (string secret in new[]
                 {
                     host,
                     WlanInterfaceId,
                     interfaceName,
                     interfaceDescription,
                     email,
                     ip,
                     url,
                     "internal.example.invalid"
                 })
        {
            Ensure(!serialized.Contains(
                    secret,
                    StringComparison.OrdinalIgnoreCase),
                $"안전 결과에 원문 식별값이 남았습니다: {secret}");
        }

        ProxyEndpointRouteEvidenceItem item =
            result.Endpoints.Single();
        Ensure(item.EndpointLabel.Contains(
                item.HostFingerprint,
                StringComparison.Ordinal)
               && item.SelectedInterfaceFingerprint?.Length
                   == RouteInterfaceFingerprint.DisplayLength,
            "원문 대신 프록시·인터페이스 지문을 유지해야 합니다.");
    }

    private static void RejectsInvalidDnsTimeout()
    {
        ProxyEndpointParseResult parsed =
            ProxyEndpointParser.Parse("PROXY proxy.example:8080");
        RecordingRouteReader reader = new([]);

        EnsureThrows<ArgumentOutOfRangeException>(() =>
            new ProxyEndpointRouteAnalyzer(reader)
                .AnalyzeAsync(
                    parsed,
                    WlanInterfaceId,
                    dnsTimeoutSeconds: 0)
                .GetAwaiter()
                .GetResult());
        Ensure(reader.Requests.Count == 0,
            "잘못된 DNS 제한 시간에서는 reader를 호출하면 안 됩니다.");
    }

    private static ProxyEndpointRouteAnalysisResult Analyze(
        ProxyEndpointParseResult parsed,
        RecordingRouteReader reader) =>
        new ProxyEndpointRouteAnalyzer(reader)
            .AnalyzeAsync(
                parsed,
                WlanInterfaceId,
                dnsTimeoutSeconds: 2,
                cancellationToken: default)
            .GetAwaiter()
            .GetResult();

    private static DestinationRouteEvidence SuccessEvidence(
        string label,
        string interfaceId,
        NetworkAdapterCategory category)
    {
        RouteInterfaceDescriptor descriptor = new(
            InterfaceIdentity: interfaceId,
            DisplayName: $"Synthetic {label} adapter",
            Description: $"Synthetic {label} description",
            NativeInterfaceType: category.ToString(),
            Category: category,
            OperationalState: NetworkAdapterOperationalState.Up,
            HasDefaultGateway: true,
            IsVirtual: false,
            IsVpn: category == NetworkAdapterCategory.Tunnel);
        return new DestinationRouteEvidence(
            CapturedAt: DateTimeOffset.UnixEpoch,
            TargetLabel: label,
            Purpose: RouteProbePurpose.ProxyEndpoint,
            DnsWasUsed: true,
            ResolvedAddressCount: 1,
            Status: DestinationRouteEvidenceStatus.Success,
            SelectedInterface: descriptor,
            AddressEvidence:
            [
                new RouteAddressEvidence(
                    RouteAddressFamilyKind.IPv4,
                    RouteAddressEvidenceStatus.Success,
                    descriptor,
                    NativeErrorCode: null,
                    Message: "합성 최적 인터페이스 확인")
            ],
            Warnings: Array.Empty<string>(),
            Message: "합성 최적 인터페이스 확인");
    }

    private static DestinationRouteEvidence FailedEvidence(
        DestinationRouteEvidenceStatus status,
        string message) =>
        new(
            CapturedAt: DateTimeOffset.UnixEpoch,
            TargetLabel: "합성 실패 후보",
            Purpose: RouteProbePurpose.ProxyEndpoint,
            DnsWasUsed: true,
            ResolvedAddressCount: 1,
            Status: status,
            SelectedInterface: null,
            AddressEvidence:
            [
                new RouteAddressEvidence(
                    RouteAddressFamilyKind.IPv4,
                    status == DestinationRouteEvidenceStatus.RouteNotFound
                        ? RouteAddressEvidenceStatus.RouteNotFound
                        : RouteAddressEvidenceStatus.Failed,
                    Interface: null,
                    NativeErrorCode: null,
                    Message: message)
            ],
            Warnings: Array.Empty<string>(),
            Message: message);

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

    private sealed record RouteReadRequest(
        string Host,
        string SafeLabel,
        int DnsTimeoutSeconds);

    private sealed class RecordingRouteReader
        : IProxyEndpointRouteEvidenceReader
    {
        private readonly Queue<DestinationRouteEvidence> _results;

        public RecordingRouteReader(
            IEnumerable<DestinationRouteEvidence> results)
        {
            _results = new Queue<DestinationRouteEvidence>(results);
        }

        public List<RouteReadRequest> Requests { get; } = [];

        public Task<DestinationRouteEvidence> ReadAsync(
            string host,
            string safeLabel,
            int dnsTimeoutSeconds,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(new RouteReadRequest(
                host,
                safeLabel,
                dnsTimeoutSeconds));
            if (_results.Count == 0)
            {
                throw new InvalidOperationException(
                    "합성 route evidence가 예상보다 많이 요청됐습니다.");
            }

            return Task.FromResult(_results.Dequeue());
        }
    }
}
