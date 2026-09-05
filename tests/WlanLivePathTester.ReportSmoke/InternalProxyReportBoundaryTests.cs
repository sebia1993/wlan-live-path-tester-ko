using System.Runtime.CompilerServices;
using System.Text.Json;
using WlanLivePathTester.Core.NetworkEnvironment;
using WlanLivePathTester.Core.Proxy;
using WlanLivePathTester.Core.Reporting;
using WlanLivePathTester.Core.Routing;

namespace WlanLivePathTester.ReportSmoke;

internal static class InternalProxyReportBoundaryTests
{
    private static readonly DateTimeOffset FixedTime = DateTimeOffset.UnixEpoch;
#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Run()
    {
        EveryRunStateHasAStableFinding();
        EveryComparisonStateIsPresentInAllFormats();
        UnknownCategoryAndScopeAreNotInvented();
        MissingStatusRemainsDifferentFromInvalidStatus();
        Console.WriteLine("PASS route report status matrix and unknown-value boundaries");
    }

    private static void EveryRunStateHasAStableFinding()
    {
        (InternalProxyRouteComparisonRunStatus Status, string Suffix)[] cases =
        [
            (InternalProxyRouteComparisonRunStatus.InvalidInput, "INVALID_INPUT"),
            (InternalProxyRouteComparisonRunStatus.ProxySourceBlocked, "SOURCE_BLOCKED"),
            (InternalProxyRouteComparisonRunStatus.ProxySourceUnavailable, "SOURCE_UNAVAILABLE"),
            (InternalProxyRouteComparisonRunStatus.DirectPathSelected, "DIRECT_PRIMARY"),
            (InternalProxyRouteComparisonRunStatus.InternalRouteUnavailable, "INTERNAL_UNAVAILABLE"),
            (InternalProxyRouteComparisonRunStatus.Canceled, "CANCELED"),
            (InternalProxyRouteComparisonRunStatus.Failed, "FAILED"),
            (InternalProxyRouteComparisonRunStatus.Completed, "RESULT_MISSING"),
            ((InternalProxyRouteComparisonRunStatus)999, "UNKNOWN")
        ];
        foreach ((InternalProxyRouteComparisonRunStatus status, string suffix) in cases)
        {
            var report = Document(MakeRun(status, null, null));
            string code = "INTERNAL_PROXY_ROUTE_RUN_" + suffix;
            Ensure(report.RouteComparison.Finding.Code == code, "Unexpected run finding.");
            AssertAllFormats(report, code);
            Ensure(report.RouteComparison.Comparison is null, "Do not invent comparison evidence.");
            Ensure(report.RouteComparison.ProxyEntries.Count == 0, "Do not invent proxy candidates.");
        }
    }

    private static void EveryComparisonStateIsPresentInAllFormats()
    {
        (InternalProxyRouteComparisonStatus Status, string Suffix)[] cases =
        [
            (InternalProxyRouteComparisonStatus.Ready, "SAME_INTERFACE"),
            (InternalProxyRouteComparisonStatus.Diverged, "DIVERGED"),
            (InternalProxyRouteComparisonStatus.Ambiguous, "AMBIGUOUS"),
            (InternalProxyRouteComparisonStatus.Incomplete, "INCOMPLETE")
        ];
        foreach ((InternalProxyRouteComparisonStatus status, string suffix) in cases)
        {
            var report = Document(MakeRun(InternalProxyRouteComparisonRunStatus.Completed, Comparison(status), null));
            Ensure(report.RouteComparison.Comparison?.Status == status.ToString(), "Comparison state must survive mapping.");
            AssertAllFormats(report, "INTERNAL_PROXY_ROUTE_" + suffix);
        }
    }

    private static void UnknownCategoryAndScopeAreNotInvented()
    {
        (string? Input, string Expected)[] scopes =
        [
            (null, "all"), ("all", "all"), (" HTTPS ", "https"),
            ("ftp", "unknown"), ("https://private.invalid/secret", "unknown")
        ];
        foreach ((string? scope, string expected) in scopes)
        {
            ProxyEndpointRouteEvidenceItem endpoint = new(
                Sequence: 1, EndpointLabel: "private.invalid", HostFingerprint: "112233aabb", AppliesToScheme: scope,
                Transport: ProxyEndpointTransport.Http, Port: 8080, RouteStatus: DestinationRouteEvidenceStatus.RouteNotFound,
                WlanCorrelationStatus: RouteWlanCorrelationStatus.NotEvaluated,
                SelectedInterfaceFingerprint: "12345678-1234-1234-1234-123456789abc",
                SelectedInterfaceCategory: (NetworkAdapterCategory)999,
                SelectedInterfaceIsVirtual: null, SelectedInterfaceIsVpn: null, SelectedInterfaceIsUp: null,
                SelectedInterfaceHasDefaultGateway: null, ResolvedAddressCount: 1, SuccessfulAddressCount: 0,
                FailedAddressCount: 1, Message: "private.invalid", Warnings: ["private.invalid"]);
            ProxyEndpointRouteAnalysisResult analysis = new(
                CapturedAt: FixedTime, Status: ProxyEndpointRouteAnalysisStatus.Failed,
                SourceKind: ProxyEndpointSourceKind.ManualServerList, ProxyDecision: ProxyEndpointDecision.ProxyWithDirectFallback,
                TargetScheme: "https", DirectPresent: true, DirectIsPrimary: false, DirectFallback: true,
                DirectSequence: 2, ParsedEndpointCount: 1, ApplicableEndpointCount: 1, AnalyzedEndpointCount: 1,
                SkippedAfterDirectCount: 0, SuccessfulEndpointCount: 0, DistinctInterfaceCount: 0,
                Endpoints: [endpoint], Warnings: [], Message: "private.invalid", Limitation: "private.invalid");
            var report = Document(MakeRun(InternalProxyRouteComparisonRunStatus.Completed,
                Comparison(InternalProxyRouteComparisonStatus.Incomplete) with
                { InternalInterfaceCategory = (NetworkAdapterCategory)999 }, analysis));
            var mapped = report.RouteComparison.ProxyEntries.Single();
            Ensure(mapped.AppliesToScheme == expected, "Invalid scope must not become all.");
            Ensure(mapped.SelectedInterfaceCategory == string.Empty, "Invalid category must remain unavailable.");
            Ensure(report.RouteComparison.Comparison?.InternalInterfaceCategory == string.Empty,
                "Internal and proxy category handling must agree.");
            Ensure(mapped.SelectedInterfaceFingerprint is null, "A full GUID is not a display fingerprint.");
            Ensure(!InternalProxyRouteComparisonRunReportWriter.RenderJson(report)
                .Contains("private.invalid", StringComparison.OrdinalIgnoreCase), "Raw labels must not be reflected.");
        }
    }

    private static void MissingStatusRemainsDifferentFromInvalidStatus()
    {
        var source = MakeRun(InternalProxyRouteComparisonRunStatus.ProxySourceUnavailable, null, null);
        var missing = Document(source).RouteComparison;
        var invalid = Document(source with
        {
            ProxyExecutionStatus = (ProxyDirectiveRouteAnalysisExecutionStatus)999,
            InternalRouteStatus = (DestinationRouteEvidenceStatus)999
        }).RouteComparison;
        Ensure(missing.ProxyExecutionStatus == "None" && missing.InternalRouteStatus == "None", "Missing statuses are None.");
        Ensure(invalid.ProxyExecutionStatus == "Unknown" && invalid.InternalRouteStatus == "Unknown", "Invalid statuses are Unknown.");
    }

    private static void AssertAllFormats(InternalProxyRouteComparisonRunReportDocument report, string code)
    {
        using JsonDocument parsed = JsonDocument.Parse(InternalProxyRouteComparisonRunReportWriter.RenderJson(report));
        Ensure(parsed.RootElement.GetProperty("routeComparison").GetProperty("finding").GetProperty("code").GetString() == code,
            "JSON finding mismatch.");
        Ensure(InternalProxyRouteComparisonRunReportWriter.RenderCsv(report).Contains(code, StringComparison.Ordinal), "CSV finding missing.");
        Ensure(InternalProxyRouteComparisonRunReportWriter.RenderHtml(report).Contains(code, StringComparison.Ordinal), "HTML finding missing.");
    }
    private static InternalProxyRouteComparisonRunReportDocument Document(InternalProxyRouteComparisonRunResult run) =>
        InternalProxyRouteComparisonRunReportWriter.CreateDocument(run, "0.1.0-test", FixedTime);

    private static InternalProxyRouteComparisonRunResult MakeRun(InternalProxyRouteComparisonRunStatus status,
        InternalProxyRouteComparisonResult? comparison, ProxyEndpointRouteAnalysisResult? analysis)
    {
        ProxyDirectiveRouteAnalysisExecutionResult<ProxyEndpointRouteAnalysisResult>? execution = null;
        if (analysis is not null)
        {
            var selection = ProxyDirectiveSourceSelectionPolicy.Select(false, false, null, true, "PROXY proxy.invalid:8080; DIRECT");
            ProxyEndpointRouteAnalysisResult captured = analysis;
            execution = ProxyDirectiveRouteAnalysisExecutor.ExecuteAsync(selection,
                (directive, token) => Task.FromResult(captured)).GetAwaiter().GetResult();
        }
        return new InternalProxyRouteComparisonRunResult(
            CompletedAt: FixedTime, Status: status, ProxySourceKind: ProxyDirectiveSourceKind.ManualProxyConfiguration,
            ProxySelectionStatus: ProxyDirectiveSourceSelectionStatus.Selected,
            ProxyPlanStatus: ProxyDirectiveRouteAnalysisPlanStatus.AnalyzeProxyEndpoints,
            ProxyPlanCode: ProxyDirectiveRouteAnalysisPlanCode.ManualProxySelected,
            ProxyExecutionStatus: execution?.Status, ProxyEndpointSourceKind: ProxyEndpointSourceKind.ManualServerList,
            ProxyDecision: ProxyEndpointDecision.ProxyWithDirectFallback, TargetScheme: "https",
            InternalRouteStatus: null, ProxyRouteStatus: analysis?.Status, Comparison: comparison,
            ParsedProxyEndpointCount: analysis?.ParsedEndpointCount ?? 0,
            ApplicableProxyEndpointCount: analysis?.ApplicableEndpointCount ?? 0,
            AnalyzedProxyEndpointCount: analysis?.AnalyzedEndpointCount ?? 0,
            SuccessfulProxyEndpointCount: analysis?.SuccessfulEndpointCount ?? 0,
            DistinctProxyInterfaceCount: analysis?.DistinctInterfaceCount ?? 0,
            DirectPresent: true, DirectIsPrimary: false, DirectFallback: true,
            ProxyParseErrorsPresent: false, ExpectedWlanIdentityAvailable: false,
            InternalRouteReadPerformed: false, ProxyRouteAnalysisPerformed: analysis is not null,
            Message: "not exported", Limitation: "not exported", InternalRouteEvidence: null, ProxyExecution: execution);
    }

    private static InternalProxyRouteComparisonResult Comparison(InternalProxyRouteComparisonStatus status)
    {
        bool complete = status is InternalProxyRouteComparisonStatus.Ready or InternalProxyRouteComparisonStatus.Diverged;
        return new InternalProxyRouteComparisonResult(
            EvaluatedAt: FixedTime, Status: status,
            Relation: status switch
            {
                InternalProxyRouteComparisonStatus.Ready => InternalProxyRouteRelation.SameInterface,
                InternalProxyRouteComparisonStatus.Diverged => InternalProxyRouteRelation.DifferentInterface,
                InternalProxyRouteComparisonStatus.Ambiguous => InternalProxyRouteRelation.MultipleInterfaces,
                _ => InternalProxyRouteRelation.Unknown
            },
            Code: status switch
            {
                InternalProxyRouteComparisonStatus.Ready => InternalProxyRouteComparisonCode.SameLocalInterface,
                InternalProxyRouteComparisonStatus.Diverged => InternalProxyRouteComparisonCode.DifferentLocalInterface,
                InternalProxyRouteComparisonStatus.Ambiguous => InternalProxyRouteComparisonCode.ProxyRouteAmbiguous,
                _ => InternalProxyRouteComparisonCode.ProxyAnalysisIncomplete
            },
            InternalRouteStatus: DestinationRouteEvidenceStatus.Success,
            ProxyExecutionStatus: ProxyDirectiveRouteAnalysisExecutionStatus.Completed,
            ProxyAnalysisStatus: complete ? ProxyEndpointRouteAnalysisStatus.Success : ProxyEndpointRouteAnalysisStatus.PartialSuccess,
            ProxySourceKind: ProxyDirectiveSourceKind.ManualProxyConfiguration,
            ProxyPlanCode: ProxyDirectiveRouteAnalysisPlanCode.ManualProxySelected,
            InternalInterfaceFingerprint: "0123456789", InternalInterfaceCategory: NetworkAdapterCategory.Wireless,
            ProxyInterfaceFingerprints: ["abcdef0123"], ProxyInterfaceCategories: [NetworkAdapterCategory.Tunnel],
            ProxyApplicableEndpointCount: 1, ProxyAnalyzedEndpointCount: 1,
            ProxySuccessfulEndpointCount: complete ? 1 : 0, ProxyDistinctInterfaceCount: 1,
            ProxySkippedAfterDirectCount: 0, ProxyDirectPresent: true, ProxyDirectIsPrimary: false,
            ProxyDirectFallbackPresent: true, ProxyParseErrorsPresent: false, ExactIdentityComparisonPerformed: complete,
            Message: "not exported", Interpretation: "not exported", Limitation: "not exported", NextStep: "not exported");
    }
    private static void Ensure(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
