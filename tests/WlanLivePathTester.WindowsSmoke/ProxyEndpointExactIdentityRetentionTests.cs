using System.Runtime.CompilerServices;
using System.Text.Json;
using WlanLivePathTester.Core.NetworkEnvironment;
using WlanLivePathTester.Core.Proxy;
using WlanLivePathTester.Core.Routing;
using WlanLivePathTester.Windows.Routing;

namespace WlanLivePathTester.WindowsSmoke;

internal static class ProxyEndpointExactIdentityRetentionTests
{
    private const string ExactInterfaceId =
        "71B2C3D4-E5F6-47A8-9123-1234567890AB";
    private const string SecretHost =
        "memory-only-proxy.example.invalid";
    private const string SecretDescription =
        "Corporate Private Wireless Adapter";

#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Run()
    {
        RetainsExactIdentityInMemoryAndExcludesItFromJson();
        FailedRouteDoesNotInventExactIdentity();
        Console.WriteLine(
            "PASS proxy endpoint exact identity memory-only tests");
    }

    private static void
        RetainsExactIdentityInMemoryAndExcludesItFromJson()
    {
        RecordingReader reader = new((_, safeLabel, _, _) =>
            Task.FromResult(CreateSuccessRoute(safeLabel)));
        ProxyEndpointParseResult parsed = ProxyEndpointParser.Parse(
            $"PROXY {SecretHost}:8080",
            new Uri("https://download.example/file.bin"));
        ProxyEndpointRouteAnalysisResult result =
            new ProxyEndpointRouteAnalyzer(reader)
                .AnalyzeAsync(
                    parsed,
                    ExactInterfaceId,
                    dnsTimeoutSeconds: 2)
                .GetAwaiter()
                .GetResult();

        ProxyEndpointRouteEvidenceItem endpoint =
            result.Endpoints.Single();
        Ensure(endpoint.SelectedInterfaceIdentity
               == ExactInterfaceId,
            "성공 경로의 전체 인터페이스 ID를 현재 메모리에 유지해야 합니다.");
        Ensure(endpoint.SelectedInterfaceFingerprint
               == RouteInterfaceFingerprint.Create(ExactInterfaceId),
            "공개 결과에는 짧은 인터페이스 지문을 유지해야 합니다.");
        Ensure(endpoint.WlanCorrelationStatus
               == RouteWlanCorrelationStatus.Matched,
            "정확한 현재 WLAN ID와 같은 경로는 Matched여야 합니다.");

        string json = JsonSerializer.Serialize(result);
        foreach (string secret in new[]
                 {
                     ExactInterfaceId,
                     SecretHost,
                     SecretDescription
                 })
        {
            Ensure(!json.Contains(
                    secret,
                    StringComparison.OrdinalIgnoreCase),
                $"기본 경로 분석 JSON에 메모리 전용 값이 남았습니다: {secret}");
        }
        Ensure(!json.Contains(
                "selectedInterfaceIdentity",
                StringComparison.OrdinalIgnoreCase),
            "메모리 전용 전체 ID 속성 이름도 기본 JSON에 나타나면 안 됩니다.");
        Ensure(json.Contains(
                endpoint.SelectedInterfaceFingerprint!,
                StringComparison.Ordinal),
            "기본 JSON에는 짧은 인터페이스 지문을 유지해야 합니다.");
    }

    private static void FailedRouteDoesNotInventExactIdentity()
    {
        RecordingReader reader = new((_, safeLabel, _, _) =>
            Task.FromResult(new DestinationRouteEvidence(
                CapturedAt: DateTimeOffset.UnixEpoch,
                TargetLabel: safeLabel,
                Purpose: RouteProbePurpose.ProxyEndpoint,
                DnsWasUsed: true,
                ResolvedAddressCount: 0,
                Status: DestinationRouteEvidenceStatus.RouteNotFound,
                SelectedInterface: null,
                AddressEvidence: Array.Empty<RouteAddressEvidence>(),
                Warnings: Array.Empty<string>(),
                Message: "합성 경로 없음")));
        ProxyEndpointParseResult parsed = ProxyEndpointParser.Parse(
            "PROXY missing.example.invalid:8080",
            new Uri("https://download.example/file.bin"));
        ProxyEndpointRouteAnalysisResult result =
            new ProxyEndpointRouteAnalyzer(reader)
                .AnalyzeAsync(
                    parsed,
                    ExactInterfaceId,
                    dnsTimeoutSeconds: 2)
                .GetAwaiter()
                .GetResult();

        Ensure(result.Status
               == ProxyEndpointRouteAnalysisStatus.Failed,
            "경로가 없는 단일 후보는 Failed여야 합니다.");
        Ensure(result.Endpoints.Single().SelectedInterfaceIdentity
               is null,
            "선택 인터페이스가 없으면 전체 ID를 추정하면 안 됩니다.");
    }

    private static DestinationRouteEvidence CreateSuccessRoute(
        string safeLabel)
    {
        RouteInterfaceDescriptor selected = new(
            InterfaceIdentity: ExactInterfaceId,
            DisplayName: SecretDescription,
            Description: SecretDescription,
            NativeInterfaceType: "Wireless80211",
            Category: NetworkAdapterCategory.Wireless,
            OperationalState: NetworkAdapterOperationalState.Up,
            HasDefaultGateway: true,
            IsVirtual: false,
            IsVpn: false);
        return new DestinationRouteEvidence(
            CapturedAt: DateTimeOffset.UnixEpoch,
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
                    Message: "합성 최적 인터페이스")
            ],
            Warnings: Array.Empty<string>(),
            Message: "합성 최적 인터페이스");
    }

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

        public Task<DestinationRouteEvidence> ReadAsync(
            string host,
            string safeLabel,
            int dnsTimeoutSeconds,
            CancellationToken cancellationToken) =>
            _handler(
                host,
                safeLabel,
                dnsTimeoutSeconds,
                cancellationToken);
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
