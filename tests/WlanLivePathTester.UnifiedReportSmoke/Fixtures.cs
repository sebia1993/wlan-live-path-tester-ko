using WlanLivePathTester.Core.Measurements;
using WlanLivePathTester.Core.Models;
using WlanLivePathTester.Core.NetworkEnvironment;
using WlanLivePathTester.Core.Proxy;
using WlanLivePathTester.Core.Reporting;
using WlanLivePathTester.Core.Routing;

namespace WlanLivePathTester.UnifiedReportSmoke;

internal static class Fixtures
{
    internal static readonly DateTimeOffset CapturedAt = DateTimeOffset.UnixEpoch.AddDays(20);
    internal const string SecretHost = "private-unified-proxy.example.invalid";
    internal const string SecretInternal = "https://private-unified-internal.example.invalid/file.bin";
    internal const string SecretExternal = "https://private-unified-external.example.invalid/file.bin";
    internal const string SecretEmail = "private-unified-user@example.invalid";
    internal const string SecretIp = "10.79.68.57";
    internal const string SecretGuid = "B740138E-98D3-4B27-AF04-3FB110AD8006";
    internal const string SecretDescription = "PRIVATE_UNIFIED_ADAPTER_DESCRIPTION";
    internal static readonly string[] Secrets =
    [
        SecretHost, SecretInternal, "private-unified-internal.example.invalid",
        SecretExternal, "private-unified-external.example.invalid",
        SecretEmail, SecretIp, SecretGuid, SecretDescription
    ];

    internal static ReportFinding Finding(string code) =>
        new(code, "Information", "합성 판정", "합성 근거", "합성 해석", "합성 한계", "합성 조치");

    internal static LocalDiagnosticReport Report()
    {
        DownloadMeasurementResult download = new(
            TargetName: "synthetic-measurement", PathKind: NetworkPathKind.Internal,
            Status: MeasurementStatus.Success, StartedAt: CapturedAt.AddMinutes(10),
            CompletedAt: CapturedAt.AddMinutes(10).AddSeconds(1), BytesReceived: 1024 * 1024,
            AverageMbps: 8.388608, TimeToFirstByte: TimeSpan.FromMilliseconds(10),
            HttpStatusCode: 200, ProxyWasUsed: false, StreamsRequested: 1, StreamsCompleted: 1,
            RedirectCount: 0, FinalUrl: "https://example.invalid/file.bin",
            Samples: [new ThroughputSample(0, TimeSpan.FromSeconds(1), 1024 * 1024, 8.388608)],
            ResponseHeaders: new Dictionary<string, string>(), ErrorCode: null, Message: "synthetic result");
        return new LocalDiagnosticReport(
            SchemaVersion: "1.1",
            Metadata: new ReportMetadata(
                GeneratedAt: CapturedAt.AddHours(1), ApplicationName: "WLAN Live Path Tester KO",
                ApplicationVersion: "0.1.0-test", OperatingSystem: "synthetic Windows",
                RuntimeVersion: "synthetic runtime", Culture: "ko-KR", SensitiveValuesIncluded: false,
                DataHandlingStatement: "합성 로컬 보고서"),
            Wlan: new ReportWlanSection(
                CapturedAt: CapturedAt.AddMinutes(30), IsConnected: true,
                InterfaceDescription: "synthetic adapter", InterfaceState: "Connected",
                Ssid: "[마스킹]", Bssid: "[마스킹]", RssiDbm: -50, SignalQualityPercent: 90,
                Channel: 36, CenterFrequencyMhz: 5180, Band: "5 GHz", PhyType: "HE",
                ReceiveLinkMbps: 1200, TransmitLinkMbps: 1200,
                Authentication: "802.1X", Cipher: "AES", ReadError: null),
            Proxy: new ReportProxySection(true, "synthetic proxy", false, true, false, false, null, "원문 미포함"),
            Measurements: [new ReportTextSection("synthetic", "합성 측정", "합성 화면 결과", CapturedAt)],
            BrowserObservation: null,
            Findings: [Finding("NO_CLEAR_FAILURE_PATTERN"), Finding("BROWSER_OBSERVATION_WLAN_IDENTITY_UNAVAILABLE")],
            Limitations: ["기존 합성 한계"],
            StructuredMeasurements: [ReportMeasurementMapper.FromResult(download)]);
    }

    internal static InternalProxyRouteComparisonRunResult Run(
        InternalProxyRouteComparisonStatus status = InternalProxyRouteComparisonStatus.Diverged)
    {
        ProxyEndpointRouteEvidenceItem endpoint = new(
            Sequence: 1, EndpointLabel: SecretHost, HostFingerprint: "112233aabb",
            AppliesToScheme: "https", Transport: ProxyEndpointTransport.Http, Port: 8080,
            RouteStatus: DestinationRouteEvidenceStatus.Success,
            WlanCorrelationStatus: RouteWlanCorrelationStatus.DifferentInterface,
            SelectedInterfaceFingerprint: "abcdef0123", SelectedInterfaceCategory: NetworkAdapterCategory.Tunnel,
            SelectedInterfaceIsVirtual: true, SelectedInterfaceIsVpn: true,
            SelectedInterfaceIsUp: true, SelectedInterfaceHasDefaultGateway: true,
            ResolvedAddressCount: 1, SuccessfulAddressCount: 1, FailedAddressCount: 0,
            Message: SecretInternal, Warnings: [SecretEmail, SecretIp, SecretDescription])
        {
            SelectedInterfaceIdentity = SecretGuid
        };
        ProxyEndpointRouteAnalysisResult analysis = new(
            CapturedAt: CapturedAt, Status: ProxyEndpointRouteAnalysisStatus.Success,
            SourceKind: ProxyEndpointSourceKind.ManualServerList,
            ProxyDecision: ProxyEndpointDecision.ProxyWithDirectFallback, TargetScheme: "https",
            DirectPresent: true, DirectIsPrimary: false, DirectFallback: true, DirectSequence: 2,
            ParsedEndpointCount: 1, ApplicableEndpointCount: 1, AnalyzedEndpointCount: 1,
            SkippedAfterDirectCount: 0, SuccessfulEndpointCount: 1, DistinctInterfaceCount: 1,
            Endpoints: [endpoint], Warnings: [SecretEmail, SecretIp], Message: SecretHost, Limitation: SecretExternal);
        ProxyDirectiveSourceSelectionResult selection = ProxyDirectiveSourceSelectionPolicy.Select(
            targetDecisionWasEvaluated: false, targetDecisionIsDirect: false, targetSpecificDirective: null,
            manualProxyConfigured: true, manualProxyDirective: $"PROXY {SecretHost}:8080; DIRECT");
        var execution = ProxyDirectiveRouteAnalysisExecutor.ExecuteAsync(selection,
            (_, _) => Task.FromResult(analysis)).GetAwaiter().GetResult();
        bool exact = status is InternalProxyRouteComparisonStatus.Ready or InternalProxyRouteComparisonStatus.Diverged;
        InternalProxyRouteComparisonResult comparison = new(
            EvaluatedAt: CapturedAt, Status: status,
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
            ProxyAnalysisStatus: ProxyEndpointRouteAnalysisStatus.Success,
            ProxySourceKind: ProxyDirectiveSourceKind.ManualProxyConfiguration,
            ProxyPlanCode: ProxyDirectiveRouteAnalysisPlanCode.ManualProxySelected,
            InternalInterfaceFingerprint: "0123456789", InternalInterfaceCategory: NetworkAdapterCategory.Wireless,
            ProxyInterfaceFingerprints: ["abcdef0123"], ProxyInterfaceCategories: [NetworkAdapterCategory.Tunnel],
            ProxyApplicableEndpointCount: 1, ProxyAnalyzedEndpointCount: 1, ProxySuccessfulEndpointCount: 1,
            ProxyDistinctInterfaceCount: 1, ProxySkippedAfterDirectCount: 0,
            ProxyDirectPresent: true, ProxyDirectIsPrimary: false, ProxyDirectFallbackPresent: true,
            ProxyParseErrorsPresent: false, ExactIdentityComparisonPerformed: exact,
            Message: SecretInternal, Interpretation: SecretHost, Limitation: SecretDescription, NextStep: SecretGuid);
        RouteInterfaceDescriptor adapter = new(SecretGuid, SecretDescription, SecretDescription,
            "Wireless", NetworkAdapterCategory.Wireless, NetworkAdapterOperationalState.Up, true, false, false);
        DestinationRouteEvidence evidence = new(
            CapturedAt: CapturedAt, TargetLabel: SecretInternal, Purpose: RouteProbePurpose.InternalDirectTarget,
            DnsWasUsed: true, ResolvedAddressCount: 1, Status: DestinationRouteEvidenceStatus.Success,
            SelectedInterface: adapter,
            AddressEvidence: [new RouteAddressEvidence(RouteAddressFamilyKind.IPv4,
                RouteAddressEvidenceStatus.Success, adapter, null, SecretExternal)],
            Warnings: [SecretEmail, SecretIp], Message: SecretHost);
        return new InternalProxyRouteComparisonRunResult(
            CompletedAt: CapturedAt, Status: InternalProxyRouteComparisonRunStatus.Completed,
            ProxySourceKind: ProxyDirectiveSourceKind.ManualProxyConfiguration,
            ProxySelectionStatus: ProxyDirectiveSourceSelectionStatus.Selected,
            ProxyPlanStatus: ProxyDirectiveRouteAnalysisPlanStatus.AnalyzeProxyEndpoints,
            ProxyPlanCode: ProxyDirectiveRouteAnalysisPlanCode.ManualProxySelected,
            ProxyExecutionStatus: execution.Status, ProxyEndpointSourceKind: ProxyEndpointSourceKind.ManualServerList,
            ProxyDecision: ProxyEndpointDecision.ProxyWithDirectFallback, TargetScheme: "https",
            InternalRouteStatus: DestinationRouteEvidenceStatus.Success, ProxyRouteStatus: analysis.Status,
            Comparison: comparison, ParsedProxyEndpointCount: 1, ApplicableProxyEndpointCount: 1,
            AnalyzedProxyEndpointCount: 1, SuccessfulProxyEndpointCount: 1, DistinctProxyInterfaceCount: 1,
            DirectPresent: true, DirectIsPrimary: false, DirectFallback: true, ProxyParseErrorsPresent: false,
            ExpectedWlanIdentityAvailable: true, InternalRouteReadPerformed: true, ProxyRouteAnalysisPerformed: true,
            Message: SecretInternal, Limitation: SecretExternal, InternalRouteEvidence: evidence, ProxyExecution: execution);
    }

    internal static LocalReportExportResult Export(string stem = "synthetic-report") =>
        new("synthetic-directory", stem + ".json", stem + ".csv", stem + ".html", stem + "_SHA256SUMS.txt",
            new Dictionary<string, string>());

    internal static void Ensure(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
