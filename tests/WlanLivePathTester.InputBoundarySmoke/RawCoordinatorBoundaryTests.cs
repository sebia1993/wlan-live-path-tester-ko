using System.Runtime.CompilerServices;
using WlanLivePathTester.Core.Proxy;
using WlanLivePathTester.Core.Routing;
using WlanLivePathTester.Windows.Routing;

namespace WlanLivePathTester.InputBoundarySmoke;

internal static class RawCoordinatorBoundaryTests
{
#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Run()
    {
        if (!OperatingSystem.IsWindows()) return;
        VerifyAsync().GetAwaiter().GetResult();
        Console.WriteLine(
            "PASS raw comparison coordinators reject invalid OriginalString before route readers");
    }

    private static async Task VerifyAsync()
    {
        CountingInternalReader reader = new();
        InternalProxyRouteComparisonCoordinator comparison = new(
            reader,
            new ProxyDirectiveRouteAnalysisCoordinator());
        ProxyDirectiveSourceSelectionResult selection =
            ProxyDirectiveSourceSelectionPolicy.Select(
                targetDecisionWasEvaluated: false,
                targetDecisionIsDirect: false,
                targetSpecificDirective: null,
                manualProxyConfigured: true,
                manualProxyDirective:
                    "PROXY proxy.example.invalid:8080");
        Uri validExternal = new(
            "https://target.example.invalid/file",
            UriKind.Absolute);

        InternalProxyRouteComparisonRunResult badInternal =
            await comparison.RunAsync(
                "internal.example.invalid\t",
                selection,
                validExternal,
                expectedWlanInterfaceId: null);
        Ensure(
            badInternal.Status
                == InternalProxyRouteComparisonRunStatus.InvalidInput
            && !badInternal.InternalRouteReadPerformed
            && !badInternal.ProxyRouteAnalysisPerformed
            && reader.Calls == 0,
            "Invalid raw internal target must not reach either route reader.");

        Uri badExternal = new(
            "https://target.example.invalid/file\t",
            UriKind.Absolute);
        InternalProxyRouteComparisonRunResult badExternalResult =
            await comparison.RunAsync(
                "internal.example.invalid",
                selection,
                badExternal,
                expectedWlanInterfaceId: null);
        Ensure(
            badExternalResult.Status
                == InternalProxyRouteComparisonRunStatus.InvalidInput
            && !badExternalResult.InternalRouteReadPerformed
            && !badExternalResult.ProxyRouteAnalysisPerformed
            && reader.Calls == 0,
            "Invalid external OriginalString must be rejected before route work.");

        ProxyDirectiveRouteAnalysisCoordinator proxy = new();
        bool threw = false;
        try
        {
            _ = proxy.ExecuteAsync(
                selection,
                badExternal,
                expectedWlanInterfaceId: null);
        }
        catch (ArgumentException)
        {
            threw = true;
        }
        Ensure(threw,
            "Proxy route coordinator must revalidate Uri.OriginalString before analysis.");
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class CountingInternalReader
        : IInternalDirectRouteEvidenceReader
    {
        public int Calls { get; private set; }

        public Task<DestinationRouteEvidence> ReadAsync(
            string target,
            string safeLabel,
            int dnsTimeoutSeconds,
            CancellationToken cancellationToken)
        {
            Calls++;
            throw new InvalidOperationException(
                "Synthetic reader must not be called for invalid input.");
        }
    }
}
