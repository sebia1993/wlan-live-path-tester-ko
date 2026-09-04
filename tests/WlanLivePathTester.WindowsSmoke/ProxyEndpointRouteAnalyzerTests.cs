using System.Runtime.CompilerServices;
using System.Text.Json;
using WlanLivePathTester.Core.NetworkEnvironment;
using WlanLivePathTester.Core.Proxy;
using WlanLivePathTester.Core.Routing;
using WlanLivePathTester.Windows.Routing;

namespace WlanLivePathTester.WindowsSmoke;

internal static class ProxyEndpointRouteAnalyzerTests
{
    private const string WlanInterfaceId =
        "71B2C3D4-E5F6-47A8-9123-1234567890AB";
    private const string EthernetInterfaceId =
        "81B2C3D4-E5F6-47A8-9123-1234567890AB";
    private const string SecretInterfaceDescription =
        "Corporate Secret Wi-Fi Adapter";

#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Run()
    {
        DirectOnlyDoesNotResolveAnything();
        ResolvesProxyFallbacksInOrderAndCorrelatesWlan();
        KeepsDirectFallbackWhenProxyResolutionFails();
        EnforcesEndpointLimitWithoutDroppingDirect();
        StopsAfterCanceledRoute();
        RecordsDifferentLocalInterfaceWithoutLeakingIdentity();
        RejectsInvalidAnalyzerOptions();
        Console.WriteLine(
            "PASS local proxy endpoint Windows route analyzer tests");
    }

    private static void DirectOnlyDoesNotResolveAnything()
    {
        FakeResolver resolver = new((_, _, _, _) =>
            throw new InvalidOperationException(
                "DIRECT-only analysis must not call the resolver."));
        ProxyEndpointRouteAnalysisResult result =
            Analyze(resolver, "DIRECT");

        Ensure(result.Status
               == ProxyEndpointRouteAnalysisStatus.DirectOnly,
            "DIRECT-only plan should have DirectOnly status.");
        Ensure(result.Entries.Count == 1
               && result.Entries[0].Status
                   == ProxyEndpointRouteEntryStatus.Direct,
            "DIRECT-only plan should retain one direct entry.");
        Ensure(resolver.Requests.Count == 0,
            "DIRECT should not perform DNS or Windows route lookup.");
        Ensure(result.Message.Contains(
                "DNS 또는 프록시 엔드포인트 경로 조회를 수행하지 않았습니다",
                StringComparison.Ordinal),
            "DIRECT-only result should explain its no-network boundary.");
    }

    private static void
        ResolvesProxyFallbacksInOrderAndCorrelatesWlan()
    {
        FakeResolver resolver = new((host, label, _, _) =>
            Task.FromResult(SuccessRoute(
                label,
                WlanInterfaceId,
                NetworkAdapterCategory.Wireless)));
        const string firstHost = "proxy-a.example.invalid";
        const string secondHost = "proxy-b.example.invalid";
        ProxyEndpointRouteAnalysisResult result = Analyze(
            resolver,
            $"PROXY {firstHost}:8080; HTTPS {secondHost}:8443; DIRECT");

        Ensure(result.Status
               == ProxyEndpointRouteAnalysisStatus.Success,
            $"All resolved proxy routes should succeed: {result.Status}");
        Ensure(result.ProxyEndpointCount == 2
               && result.DirectDirectiveCount == 1
               && result.SuccessfulRouteCount == 2,
            "Two proxy routes and one DIRECT fallback should be retained.");
        Ensure(resolver.Requests.Select(request => request.Host)
                .SequenceEqual([firstHost, secondHost]),
            "Proxy endpoint hosts should reach the local resolver in fallback order.");
        Ensure(resolver.Requests.All(request =>
                !request.RedactedLabel.Contains(
                    request.Host,
                    StringComparison.OrdinalIgnoreCase)),
            "Route target labels must use host fingerprints, not raw hosts.");
        Ensure(result.Entries
                .Where(entry => !entry.IsDirect)
                .All(entry => entry.WlanCorrelationStatus
                    == RouteWlanCorrelationStatus.Matched.ToString()),
            "Routes using the selected WLAN interface should be Matched.");
        Ensure(result.Entries[2].IsDirect,
            "DIRECT fallback order should be preserved after proxy entries.");
    }

    private static void KeepsDirectFallbackWhenProxyResolutionFails()
    {
        FakeResolver resolver = new((_, label, _, _) =>
            Task.FromResult(FailedRoute(
                label,
                DestinationRouteEvidenceStatus.ResolutionFailed)));
        ProxyEndpointRouteAnalysisResult result = Analyze(
            resolver,
            "PROXY unavailable.example.invalid:8080;DIRECT");

        Ensure(result.Status
               == ProxyEndpointRouteAnalysisStatus.PartialSuccess,
            "A failed proxy route with a retained DIRECT fallback should be partial.");
        Ensure(result.Entries.Count == 2,
            "Failed proxy and DIRECT fallback should both remain.");
        Ensure(result.Entries[0].Status
               == ProxyEndpointRouteEntryStatus.ResolutionFailed,
            "Resolution failure should remain a structured route entry status.");
        Ensure(result.Entries[1].Status
               == ProxyEndpointRouteEntryStatus.Direct,
            "DIRECT fallback should not be converted into a failed route.");
    }

    private static void EnforcesEndpointLimitWithoutDroppingDirect()
    {
        FakeResolver resolver = new((_, label, _, _) =>
            Task.FromResult(SuccessRoute(
                label,
                WlanInterfaceId,
                NetworkAdapterCategory.Wireless)));
        string input = string.Join(
            ';',
            Enumerable.Range(1, 10)
                .Select(index =>
                    $"PROXY proxy-{index}.example.invalid:8080")
                .Append("DIRECT"));
        ProxyEndpointRouteAnalysisResult result =
            new ProxyEndpointRouteAnalyzer(resolver)
                .AnalyzeAsync(
                    input,
                    WlanInterfaceId,
                    dnsTimeoutSeconds: 5,
                    endpointLimit: 3)
                .GetAwaiter()
                .GetResult();

        Ensure(result.Status
               == ProxyEndpointRouteAnalysisStatus.PartialSuccess,
            "Truncated endpoint analysis should be PartialSuccess.");
        Ensure(result.WasTruncated,
            "Endpoint limit should set WasTruncated.");
        Ensure(resolver.Requests.Count == 3
               && result.ProxyEndpointCount == 3,
            "Only the first three non-DIRECT candidates should be resolved.");
        Ensure(result.DirectDirectiveCount == 1
               && result.Entries[^1].IsDirect,
            "DIRECT fallback should remain even after proxy endpoint truncation.");
        Ensure(result.Message.Contains(
                "상한 3개",
                StringComparison.Ordinal),
            "Truncation message should include the configured count only.");
    }

    private static void StopsAfterCanceledRoute()
    {
        int invocation = 0;
        FakeResolver resolver = new((_, label, _, _) =>
        {
            invocation++;
            return Task.FromResult(invocation == 1
                ? SuccessRoute(
                    label,
                    WlanInterfaceId,
                    NetworkAdapterCategory.Wireless)
                : FailedRoute(
                    label,
                    DestinationRouteEvidenceStatus.Canceled));
        });
        ProxyEndpointRouteAnalysisResult result = Analyze(
            resolver,
            "PROXY first.example.invalid:8080;PROXY second.example.invalid:8080;PROXY third.example.invalid:8080;DIRECT");

        Ensure(result.Status
               == ProxyEndpointRouteAnalysisStatus.Canceled,
            "Canceled route evidence should cancel the remaining proxy analysis.");
        Ensure(resolver.Requests.Count == 2,
            "No endpoint after the canceled candidate should be resolved.");
        Ensure(result.Entries.Count == 2
               && result.Entries[^1].Status
                   == ProxyEndpointRouteEntryStatus.Canceled,
            "Completed and canceled entries should be retained in order.");
        Ensure(result.DirectDirectiveCount == 0,
            "DIRECT after a canceled route should not be processed because the user stopped the operation.");
    }

    private static void
        RecordsDifferentLocalInterfaceWithoutLeakingIdentity()
    {
        const string secretProxyHost =
            "highly-sensitive-proxy.example.invalid";
        FakeResolver resolver = new((_, label, _, _) =>
            Task.FromResult(SuccessRoute(
                label,
                EthernetInterfaceId,
                NetworkAdapterCategory.Ethernet)));
        ProxyEndpointRouteAnalysisResult result = Analyze(
            resolver,
            $"PROXY {secretProxyHost}:8080");
        ProxyEndpointRouteEntry entry = result.Entries.Single();

        Ensure(entry.Status == ProxyEndpointRouteEntryStatus.Success,
            "Windows route lookup can succeed even when it differs from WLAN.");
        Ensure(entry.WlanCorrelationStatus
               == RouteWlanCorrelationStatus.DifferentInterface.ToString(),
            "Ethernet route should be distinguished from the expected WLAN interface.");
        Ensure(entry.SelectedInterfaceCategory == "Ethernet",
            "Redacted result may retain the adapter category.");
        Ensure(entry.SelectedInterfaceFingerprint?.Length
               == RouteInterfaceFingerprint.DisplayLength,
            "Redacted result should retain only the short interface fingerprint.");

        string json = JsonSerializer.Serialize(result);
        string[] forbidden =
        [
            secretProxyHost,
            WlanInterfaceId,
            EthernetInterfaceId,
            SecretInterfaceDescription,
            "Synthetic Ethernet Secret"
        ];
        foreach (string value in forbidden)
        {
            Ensure(!json.Contains(
                    value,
                    StringComparison.OrdinalIgnoreCase),
                $"Default analysis JSON must not expose raw identity: {value}");
        }

        Ensure(json.Contains(
                entry.HostFingerprint,
                StringComparison.Ordinal)
               && json.Contains(
                   entry.SelectedInterfaceFingerprint!,
                   StringComparison.Ordinal),
            "Redacted analysis JSON should retain host and interface fingerprints.");
    }

    private static void RejectsInvalidAnalyzerOptions()
    {
        FakeResolver resolver = new((_, label, _, _) =>
            Task.FromResult(SuccessRoute(
                label,
                WlanInterfaceId,
                NetworkAdapterCategory.Wireless)));
        ProxyEndpointRouteAnalyzer analyzer = new(resolver);

        EnsureThrows<ArgumentOutOfRangeException>(() =>
            analyzer.AnalyzeAsync(
                    "DIRECT",
                    WlanInterfaceId,
                    dnsTimeoutSeconds: 0)
                .GetAwaiter()
                .GetResult());
        EnsureThrows<ArgumentOutOfRangeException>(() =>
            analyzer.AnalyzeAsync(
                    "DIRECT",
                    WlanInterfaceId,
                    endpointLimit: 17)
                .GetAwaiter()
                .GetResult());
    }

    private static ProxyEndpointRouteAnalysisResult Analyze(
        FakeResolver resolver,
        string input) =>
        new ProxyEndpointRouteAnalyzer(resolver)
            .AnalyzeAsync(
                input,
                WlanInterfaceId,
                dnsTimeoutSeconds: 5,
                endpointLimit:
                    ProxyEndpointRouteAnalyzer.DefaultEndpointLimit)
            .GetAwaiter()
            .GetResult();

    private static DestinationRouteEvidence SuccessRoute(
        string label,
        string interfaceId,
        NetworkAdapterCategory category)
    {
        RouteInterfaceDescriptor selected = new(
            InterfaceIdentity: interfaceId,
            DisplayName: category == NetworkAdapterCategory.Ethernet
                ? "Synthetic Ethernet Secret"
                : "Synthetic Wi-Fi Secret",
            Description: SecretInterfaceDescription,
            NativeInterfaceType: category.ToString(),
            Category: category,
            OperationalState: NetworkAdapterOperationalState.Up,
            HasDefaultGateway: true,
            IsVirtual: false,
            IsVpn: false);
        return new DestinationRouteEvidence(
            CapturedAt: DateTimeOffset.UnixEpoch,
            TargetLabel: label,
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
            Message: "합성 경로 확인 성공");
    }

    private static DestinationRouteEvidence FailedRoute(
        string label,
        DestinationRouteEvidenceStatus status) =>
        new(
            CapturedAt: DateTimeOffset.UnixEpoch,
            TargetLabel: label,
            Purpose: RouteProbePurpose.ProxyEndpoint,
            DnsWasUsed: true,
            ResolvedAddressCount: 0,
            Status: status,
            SelectedInterface: null,
            AddressEvidence: Array.Empty<RouteAddressEvidence>(),
            Warnings: Array.Empty<string>(),
            Message: "합성 경로 미확정");

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
            $"Expected exception was not thrown: {typeof(TException).Name}");
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed record ResolverRequest(
        string Host,
        string RedactedLabel,
        int DnsTimeoutSeconds);

    private sealed class FakeResolver : IProxyEndpointRouteResolver
    {
        private readonly Func<string, string, int, CancellationToken,
            Task<DestinationRouteEvidence>> _handler;

        public FakeResolver(
            Func<string, string, int, CancellationToken,
                Task<DestinationRouteEvidence>> handler)
        {
            _handler = handler;
        }

        public List<ResolverRequest> Requests { get; } = [];

        public Task<DestinationRouteEvidence> ResolveAsync(
            string host,
            string redactedTargetLabel,
            int dnsTimeoutSeconds,
            CancellationToken cancellationToken)
        {
            Requests.Add(new ResolverRequest(
                host,
                redactedTargetLabel,
                dnsTimeoutSeconds));
            return _handler(
                host,
                redactedTargetLabel,
                dnsTimeoutSeconds,
                cancellationToken);
        }
    }
}
