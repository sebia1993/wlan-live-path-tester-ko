using System.Runtime.CompilerServices;
using System.Text.Json;
using WlanLivePathTester.Core.NetworkEnvironment;
using WlanLivePathTester.Core.Proxy;
using WlanLivePathTester.Core.Reporting;
using WlanLivePathTester.Core.Routing;
using WlanLivePathTester.Windows.Routing;

namespace WlanLivePathTester.WindowsSmoke;

internal static class InternalProxyRouteUserFlowTests
{
    private const string WlanId =
        "D2B2C3D4-E5F6-47A8-9123-1234567890AB";
    private const string TunnelId =
        "E2B2C3D4-E5F6-47A8-9123-1234567890AB";
    private const string SecretProxyHost =
        "secret-proxy.internal.example";

#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Run()
    {
        ProxyPathFlowProducesPrivacySafeDivergedReport();
        DirectPrimaryFlowDoesNotInvokeProxyReader();
        Console.WriteLine(
            "PASS complete internal and proxy local route user flow tests");
    }

    private static void
        ProxyPathFlowProducesPrivacySafeDivergedReport()
    {
        ProxyEndpointParseResult parsed = ProxyEndpointParser.Parse(
            $"PROXY {SecretProxyHost}:8080; DIRECT",
            new Uri("https://download.example/file.bin"));
        RecordingReader reader = new(
            ProxyRouteEvidence(TunnelId));
        ProxyEndpointRouteAnalysisResult proxy =
            new ProxyEndpointRouteAnalyzer(reader)
                .AnalyzeAsync(
                    parsed,
                    WlanId,
                    dnsTimeoutSeconds: 5,
                    cancellationToken: default)
                .GetAwaiter()
                .GetResult();
        DestinationRouteEvidence internalRoute =
            InternalRouteEvidence(WlanId);
        InternalProxyRouteComparisonResult comparison =
            InternalProxyRouteComparison.Compare(
                internalRoute,
                proxy,
                WlanId,
                DateTimeOffset.UnixEpoch.AddDays(9));

        Ensure(reader.ReadCount == 1
               && reader.LastHost == SecretProxyHost,
            "프록시가 DIRECT보다 먼저면 해당 후보 하나만 route reader에 전달해야 합니다.");
        Ensure(proxy.DirectFallback
               && proxy.Endpoints.Single().WlanCorrelationStatus
                   == RouteWlanCorrelationStatus.DifferentInterface,
            "프록시 뒤 DIRECT fallback과 WLAN 외 터널 경로를 구분해야 합니다.");
        Ensure(comparison.Status
               == InternalProxyRouteComparisonStatus.Diverged
               && comparison.SameLocalInterface == false,
            "내부 Wi-Fi와 프록시 터널은 Diverged여야 합니다.");
        Ensure(comparison.AnyVpnOrTunnelInterface
               && comparison.AnyVirtualInterface,
            "프록시 터널의 VPN·가상 근거를 유지해야 합니다.");

        InternalProxyRouteComparisonReportDocument report =
            InternalProxyRouteComparisonReportWriter.CreateDocument(
                comparison,
                "0.1.0-test",
                DateTimeOffset.UnixEpoch.AddDays(9));
        string json =
            InternalProxyRouteComparisonReportWriter.RenderJson(report);
        string csv =
            InternalProxyRouteComparisonReportWriter.RenderCsv(report);
        string html =
            InternalProxyRouteComparisonReportWriter.RenderHtml(report);
        string combined = string.Join(
            Environment.NewLine,
            json,
            csv,
            html);

        Ensure(report.Status == "Diverged",
            "사용자 흐름의 보고서 상태가 Diverged여야 합니다.");
        Ensure(report.Findings.Any(item => item.Code ==
                "INTERNAL_PROXY_LOCAL_ROUTE_DIVERGED"),
            "경로 분기 Finding이 필요합니다.");
        Ensure(report.Findings.Any(item => item.Code ==
                "LOCAL_ROUTE_VPN_OR_TUNNEL_PRESENT"),
            "VPN·터널 보조 Finding이 필요합니다.");
        Ensure(report.Findings.Any(item => item.Code ==
                "LOCAL_ROUTE_VIRTUAL_INTERFACE_PRESENT"),
            "가상 인터페이스 보조 Finding이 필요합니다.");
        using JsonDocument parsedJson = JsonDocument.Parse(json);
        Ensure(parsedJson.RootElement
                .GetProperty("sameLocalInterface")
                .GetBoolean() == false,
            "JSON에서 로컬 인터페이스 분기를 구조적으로 읽을 수 있어야 합니다.");
        Ensure(csv.Contains(
                "INTERNAL_PROXY_LOCAL_ROUTE_DIVERGED",
                StringComparison.Ordinal),
            "CSV에 머신용 분기 Finding 코드가 필요합니다.");
        Ensure(html.Contains(
                "내부 DIRECT와 프록시 로컬 경로 분기",
                StringComparison.Ordinal),
            "HTML에 사람이 읽는 분기 판정이 필요합니다.");

        foreach (string secret in new[]
                 {
                     SecretProxyHost,
                     WlanId,
                     TunnelId,
                     "Corporate WLAN",
                     "Corporate Tunnel"
                 })
        {
            Ensure(!combined.Contains(
                    secret,
                    StringComparison.OrdinalIgnoreCase),
                $"최종 보고서에 원문 식별값이 남았습니다: {secret}");
        }
    }

    private static void DirectPrimaryFlowDoesNotInvokeProxyReader()
    {
        ProxyEndpointParseResult parsed = ProxyEndpointParser.Parse(
            $"DIRECT; PROXY {SecretProxyHost}:8080",
            new Uri("https://download.example/file.bin"));
        RecordingReader reader = new(
            ProxyRouteEvidence(TunnelId));
        ProxyEndpointRouteAnalysisResult proxy =
            new ProxyEndpointRouteAnalyzer(reader)
                .AnalyzeAsync(
                    parsed,
                    WlanId,
                    dnsTimeoutSeconds: 5,
                    cancellationToken: default)
                .GetAwaiter()
                .GetResult();
        InternalProxyRouteComparisonResult comparison =
            InternalProxyRouteComparison.Compare(
                InternalRouteEvidence(WlanId),
                proxy,
                WlanId,
                DateTimeOffset.UnixEpoch.AddDays(9));

        Ensure(reader.ReadCount == 0,
            "DIRECT가 첫 경로이면 프록시 DNS·route reader를 호출하면 안 됩니다.");
        Ensure(proxy.Status
               == ProxyEndpointRouteAnalysisStatus.DirectPathSelected,
            "프록시 분석 결과는 DirectPathSelected여야 합니다.");
        Ensure(comparison.Status
               == InternalProxyRouteComparisonStatus.Incomplete
               && comparison.ProxyDirectPathSelected,
            "비교할 프록시 경로가 없으면 Incomplete여야 합니다.");

        InternalProxyRouteComparisonReportDocument report =
            InternalProxyRouteComparisonReportWriter.CreateDocument(
                comparison,
                "0.1.0-test",
                DateTimeOffset.UnixEpoch.AddDays(9));
        Ensure(report.Findings.Single(item => item.Code ==
                "INTERNAL_PROXY_LOCAL_ROUTE_INCOMPLETE")
                .Severity == "Information",
            "외부 DIRECT의 비교 미적용은 정보성 Finding이어야 합니다.");
    }

    private static DestinationRouteEvidence InternalRouteEvidence(
        string interfaceId)
    {
        RouteInterfaceDescriptor descriptor = new(
            InterfaceIdentity: interfaceId,
            DisplayName: "Corporate WLAN",
            Description: "Private wireless adapter",
            NativeInterfaceType: "Wireless80211",
            Category: NetworkAdapterCategory.Wireless,
            OperationalState: NetworkAdapterOperationalState.Up,
            HasDefaultGateway: true,
            IsVirtual: false,
            IsVpn: false);
        return new DestinationRouteEvidence(
            CapturedAt: DateTimeOffset.UnixEpoch,
            TargetLabel: "내부 DIRECT 대상",
            Purpose: RouteProbePurpose.InternalDirectTarget,
            DnsWasUsed: false,
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
                    Message: "합성 내부 경로")
            ],
            Warnings: Array.Empty<string>(),
            Message: "합성 내부 경로");
    }

    private static DestinationRouteEvidence ProxyRouteEvidence(
        string interfaceId)
    {
        RouteInterfaceDescriptor descriptor = new(
            InterfaceIdentity: interfaceId,
            DisplayName: "Corporate Tunnel",
            Description: "Private VPN tunnel",
            NativeInterfaceType: "Tunnel",
            Category: NetworkAdapterCategory.Tunnel,
            OperationalState: NetworkAdapterOperationalState.Up,
            HasDefaultGateway: true,
            IsVirtual: true,
            IsVpn: true);
        return new DestinationRouteEvidence(
            CapturedAt: DateTimeOffset.UnixEpoch,
            TargetLabel: "합성 프록시 후보",
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
                    Message: "합성 프록시 경로")
            ],
            Warnings: Array.Empty<string>(),
            Message: "합성 프록시 경로");
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed class RecordingReader
        : IProxyEndpointRouteEvidenceReader
    {
        private readonly DestinationRouteEvidence _result;

        public RecordingReader(DestinationRouteEvidence result)
        {
            _result = result;
        }

        public int ReadCount { get; private set; }

        public string? LastHost { get; private set; }

        public Task<DestinationRouteEvidence> ReadAsync(
            string host,
            string safeLabel,
            int dnsTimeoutSeconds,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReadCount++;
            LastHost = host;
            return Task.FromResult(_result);
        }
    }
}
