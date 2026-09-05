using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using WlanLivePathTester.Core.NetworkEnvironment;
using WlanLivePathTester.Core.Proxy;
using WlanLivePathTester.Core.Reporting;
using WlanLivePathTester.Core.Routing;

namespace WlanLivePathTester.ReportSmoke;

internal static class LocalDiagnosticReportRouteComparisonWriterTests
{
    private const string InternalFingerprint = "0123456789";
    private const string ProxyFingerprint = "abcdef0123";
    private const string SecretGuid =
        "13B2C3D4-E5F6-47A8-9123-1234567890AB";
    private const string SecretUrl =
        "https://internal-secret.example.invalid/private.bin";
    private const string SecretHost =
        "proxy-secret.example.invalid";
    private const string SecretEmail =
        "unified-route@example.invalid";
    private const string SecretIp = "10.99.88.77";
    private static readonly DateTimeOffset FixedNow =
        DateTimeOffset.UnixEpoch.AddDays(8);

#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Run()
    {
        NullRouteComparisonPreservesExistingOutputExactly();
        AddsOptionalRouteSectionAndOneTopLevelFinding();
        DoesNotDuplicateAnExistingTopLevelFinding();
        WritesIndependentFilesAndVerifiesHashes();
        Console.WriteLine(
            "PASS optional unified route comparison JSON CSV HTML tests");
    }

    private static void
        NullRouteComparisonPreservesExistingOutputExactly()
    {
        LocalDiagnosticReport report = CreateBaseReport(
            Array.Empty<ReportFinding>());

        Ensure(LocalDiagnosticReportRouteComparisonWriter.RenderJson(
                   report,
                   routeComparison: null)
               == LocalReportWriter.RenderJson(report),
            "경로 비교가 없으면 기존 JSON을 바이트 단위로 유지해야 합니다.");
        Ensure(LocalDiagnosticReportRouteComparisonWriter.RenderCsv(
                   report,
                   routeComparison: null)
               == LocalReportWriter.RenderCsv(report),
            "경로 비교가 없으면 기존 CSV를 바이트 단위로 유지해야 합니다.");
        Ensure(LocalDiagnosticReportRouteComparisonWriter.RenderHtml(
                   report,
                   routeComparison: null)
               == LocalReportWriter.RenderHtml(report),
            "경로 비교가 없으면 기존 HTML을 바이트 단위로 유지해야 합니다.");
    }

    private static void
        AddsOptionalRouteSectionAndOneTopLevelFinding()
    {
        LocalDiagnosticReport report = CreateBaseReport(
            Array.Empty<ReportFinding>());
        InternalProxyRouteComparisonRunResult run =
            CreateMaliciousRun();
        string json =
            LocalDiagnosticReportRouteComparisonWriter.RenderJson(
                report,
                run);
        string csv =
            LocalDiagnosticReportRouteComparisonWriter.RenderCsv(
                report,
                run);
        string html =
            LocalDiagnosticReportRouteComparisonWriter.RenderHtml(
                report,
                run);

        using JsonDocument parsed = JsonDocument.Parse(json);
        JsonElement route = parsed.RootElement.GetProperty(
            "internalProxyRouteComparison");
        Ensure(route.GetProperty("runStatus").GetString()
               == "Completed"
               && route.GetProperty("comparisonStatus").GetString()
                   == "Diverged",
            "통합 JSON의 선택적 경로 비교 상태가 잘못됐습니다.");
        Ensure(route.GetProperty("internalInterface")
                   .GetProperty("interfaceFingerprint")
                   .GetString() == InternalFingerprint
               && route.GetProperty("proxyInterface")
                   .GetProperty("interfaceFingerprint")
                   .GetString() == ProxyFingerprint,
            "통합 JSON에 검증된 짧은 인터페이스 지문이 필요합니다.");
        Ensure(parsed.RootElement.GetProperty("findings")
                .EnumerateArray()
                .Count(item => item.GetProperty("code").GetString()
                    == "INTERNAL_PROXY_ROUTE_COMPARISON_DIVERGED") == 1,
            "통합 JSON의 최상위 Finding에는 경로 비교 코드가 정확히 한 개여야 합니다.");
        Ensure(!json.Contains(
                "internalRouteEvidence",
                StringComparison.OrdinalIgnoreCase)
               && !json.Contains(
                   "proxyRouteAnalysis",
                   StringComparison.OrdinalIgnoreCase),
            "통합 JSON에 원본 경로 근거 속성을 포함하면 안 됩니다.");

        Ensure(CountOccurrences(
                   csv,
                   "section,key,value") == 1,
            "통합 CSV header는 한 개여야 합니다.");
        Ensure(csv.Contains(
                "\"internalProxyRouteComparison\",\"runStatus\",\"Completed\"",
                StringComparison.Ordinal)
               && csv.Contains(
                   "\"internalProxyRouteComparison.finding\",\"code\",\"INTERNAL_PROXY_ROUTE_COMPARISON_DIVERGED\"",
                   StringComparison.Ordinal),
            "통합 CSV에 경로 비교 상태와 Finding 코드가 필요합니다.");
        Ensure(html.Contains(
                "id=\"internal-proxy-route-comparison\"",
                StringComparison.Ordinal)
               && html.Contains(
                   "INTERNAL_PROXY_ROUTE_COMPARISON_DIVERGED",
                   StringComparison.Ordinal)
               && html.Contains(
                   "내부 DIRECT ↔ 프록시 로컬 경로 비교",
                   StringComparison.Ordinal),
            "통합 HTML에 선택적 경로 비교 섹션과 Finding이 필요합니다.");
        Ensure(html.IndexOf(
                   "id=\"internal-proxy-route-comparison\"",
                   StringComparison.Ordinal)
               < html.LastIndexOf(
                   "</main>",
                   StringComparison.OrdinalIgnoreCase),
            "경로 비교 섹션은 기존 HTML main 안에 삽입해야 합니다.");

        AssertSecretsAbsent(json, "JSON");
        AssertSecretsAbsent(csv, "CSV");
        AssertSecretsAbsent(html, "HTML");
    }

    private static void DoesNotDuplicateAnExistingTopLevelFinding()
    {
        InternalProxyRouteComparisonRunResult run =
            CreateMaliciousRun();
        ReportFinding routeFinding =
            InternalProxyRouteComparisonRunFindingMapper.FromResult(run);
        LocalDiagnosticReport report = CreateBaseReport(
            [routeFinding]);
        string json =
            LocalDiagnosticReportRouteComparisonWriter.RenderJson(
                report,
                run);
        string csv =
            LocalDiagnosticReportRouteComparisonWriter.RenderCsv(
                report,
                run);
        string html =
            LocalDiagnosticReportRouteComparisonWriter.RenderHtml(
                report,
                run);

        using JsonDocument parsed = JsonDocument.Parse(json);
        Ensure(parsed.RootElement.GetProperty("findings")
                .EnumerateArray()
                .Count(item => item.GetProperty("code").GetString()
                    == routeFinding.Code) == 1,
            "기존 최상위 경로 Finding을 JSON에 중복 추가하면 안 됩니다.");
        Ensure(CountOccurrences(csv, routeFinding.Code) == 1,
            "기존 경로 Finding을 CSV에 중복 추가하면 안 됩니다.");
        Ensure(html.Contains(
                "기존 통합 Finding 목록에 이미 포함",
                StringComparison.Ordinal),
            "HTML 선택 섹션은 Finding 중복 생략 사실을 표시해야 합니다.");
        AssertSecretsAbsent(json, "중복 JSON");
        AssertSecretsAbsent(csv, "중복 CSV");
        AssertSecretsAbsent(html, "중복 HTML");
    }

    private static void WritesIndependentFilesAndVerifiesHashes()
    {
        LocalDiagnosticReport report = CreateBaseReport(
            Array.Empty<ReportFinding>());
        InternalProxyRouteComparisonRunResult run =
            CreateMaliciousRun();
        string directory = Path.Combine(
            Path.GetTempPath(),
            "WlanUnifiedRouteComparisonSmoke",
            Guid.NewGuid().ToString("N"));

        try
        {
            LocalDiagnosticReportRouteComparisonExportResult first =
                LocalDiagnosticReportRouteComparisonWriter.WriteAll(
                    report,
                    run,
                    directory,
                    "합성 통합 경로",
                    generatedAt: FixedNow);
            LocalDiagnosticReportRouteComparisonExportResult second =
                LocalDiagnosticReportRouteComparisonWriter.WriteAll(
                    report,
                    run,
                    directory,
                    "합성 통합 경로",
                    generatedAt: FixedNow);

            string[] firstFiles =
            [
                first.JsonPath,
                first.CsvPath,
                first.HtmlPath,
                first.Sha256Path
            ];
            string[] secondFiles =
            [
                second.JsonPath,
                second.CsvPath,
                second.HtmlPath,
                second.Sha256Path
            ];
            Ensure(firstFiles.All(File.Exists)
                   && secondFiles.All(File.Exists),
                "각 통합 보고서 실행에서 JSON·CSV·HTML·SHA-256 네 파일을 생성해야 합니다.");
            Ensure(firstFiles.Zip(secondFiles).All(pair =>
                    !pair.First.Equals(
                        pair.Second,
                        StringComparison.OrdinalIgnoreCase)),
                "같은 시각의 두 통합 보고서가 기존 파일을 덮어쓰면 안 됩니다.");
            Ensure(Directory.GetFiles(directory).Length == 8,
                "두 번 생성하면 독립 파일이 총 8개여야 합니다.");

            foreach (LocalDiagnosticReportRouteComparisonExportResult export
                     in new[] { first, second })
            {
                Ensure(export.Sha256.Count == 3,
                    "JSON·CSV·HTML 해시 세 개가 필요합니다.");
                foreach ((string fileName, string expectedHash)
                         in export.Sha256)
                {
                    string path = Path.Combine(
                        export.OutputDirectory,
                        fileName);
                    using FileStream stream = File.OpenRead(path);
                    string actualHash = Convert.ToHexString(
                            SHA256.HashData(stream))
                        .ToLowerInvariant();
                    Ensure(actualHash == expectedHash,
                        $"통합 보고서 SHA-256이 일치하지 않습니다: {fileName}");
                    AssertSecretsAbsent(
                        File.ReadAllText(path),
                        fileName);
                }

                string checksums = File.ReadAllText(
                    export.Sha256Path);
                Ensure(export.Sha256.All(pair => checksums.Contains(
                        $"{pair.Value}  {pair.Key}",
                        StringComparison.Ordinal)),
                    "통합 SHA256SUMS에 세 보고서 해시가 필요합니다.");
            }
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private static LocalDiagnosticReport CreateBaseReport(
        IReadOnlyList<ReportFinding> findings) =>
        new(
            SchemaVersion: "1.1-test",
            Metadata: new ReportMetadata(
                GeneratedAt: FixedNow,
                ApplicationName: "WLAN Live Path Tester KO",
                ApplicationVersion: "0.1.0-test",
                OperatingSystem: "Windows synthetic",
                RuntimeVersion: ".NET synthetic",
                Culture: "ko-KR",
                SensitiveValuesIncluded: false,
                DataHandlingStatement: "합성 로컬 보고서"),
            Wlan: HealthyWlan(),
            Proxy: HealthyProxy(),
            Measurements: Array.Empty<ReportTextSection>(),
            BrowserObservation: null,
            Findings: findings,
            Limitations: Array.Empty<string>(),
            StructuredMeasurements:
                Array.Empty<ReportMeasurementSection>());

    private static ReportWlanSection HealthyWlan() =>
        new(
            CapturedAt: FixedNow,
            IsConnected: true,
            InterfaceDescription: "[마스킹됨]",
            InterfaceState: "Connected",
            Ssid: "[마스킹됨]",
            Bssid: "[마스킹됨]",
            RssiDbm: -55,
            SignalQualityPercent: 90,
            Channel: 36,
            CenterFrequencyMhz: 5180,
            Band: "5 GHz",
            PhyType: "802.11ax",
            ReceiveLinkMbps: 1200,
            TransmitLinkMbps: 1200,
            Authentication: "WPA2-Enterprise",
            Cipher: "CCMP",
            ReadError: null);

    private static ReportProxySection HealthyProxy() =>
        new(
            ReadSucceeded: true,
            Mode: "Manual",
            AutoDetectEnabled: false,
            PacConfigured: false,
            ManualProxyConfigured: true,
            BypassConfigured: true,
            Win32Error: null,
            Statement: "프록시 값은 마스킹됨");

    private static InternalProxyRouteComparisonRunResult
        CreateMaliciousRun()
    {
        LocalRouteComparisonInterface internalInterface = new(
            InterfaceFingerprint: InternalFingerprint,
            Category: NetworkAdapterCategory.Wireless,
            IsVirtual: false,
            IsVpn: false,
            IsUp: true,
            HasDefaultGateway: true,
            MatchesExpectedWlan: true);
        LocalRouteComparisonInterface proxyInterface = new(
            InterfaceFingerprint: ProxyFingerprint,
            Category: NetworkAdapterCategory.Tunnel,
            IsVirtual: true,
            IsVpn: true,
            IsUp: true,
            HasDefaultGateway: true,
            MatchesExpectedWlan: false);
        InternalProxyRouteComparisonResult comparison = new(
            EvaluatedAt: FixedNow,
            Status: InternalProxyRouteComparisonStatus.Diverged,
            InternalRouteStatus:
                DestinationRouteEvidenceStatus.Success,
            ProxyRouteStatus:
                ProxyEndpointRouteAnalysisStatus.Success,
            InternalInterface: internalInterface,
            ProxyInterface: proxyInterface,
            ExpectedWlanInterfaceFingerprint:
                InternalFingerprint,
            SameLocalInterface: false,
            InternalEvidencePartial: false,
            ProxyEvidencePartial: false,
            ProxyDirectPathSelected: false,
            ProxyDirectFallbackPresent: true,
            ProxyCandidateCount: 1,
            ProxySuccessfulCandidateCount: 1,
            ProxyDistinctInterfaceCount: 1,
            AnyVirtualInterface: true,
            AnyVpnOrTunnelInterface: true,
            Warnings:
            [
                $"{SecretUrl} {SecretHost} {SecretGuid}"
            ],
            Message:
                $"{SecretUrl} {SecretHost} {SecretGuid}",
            Limitation:
                $"{SecretEmail} {SecretIp} {SecretGuid}");

        return new InternalProxyRouteComparisonRunResult(
            CompletedAt: FixedNow,
            Status: InternalProxyRouteComparisonRunStatus.Completed,
            ProxySourceKind:
                ProxyEndpointSourceKind.AutoProxyResult,
            ProxyDecision:
                ProxyEndpointDecision.ProxyWithDirectFallback,
            TargetScheme: "https",
            InternalRouteStatus:
                DestinationRouteEvidenceStatus.Success,
            ProxyRouteStatus:
                ProxyEndpointRouteAnalysisStatus.Success,
            Comparison: comparison,
            ParsedProxyEndpointCount: 1,
            AnalyzedProxyEndpointCount: 1,
            SuccessfulProxyEndpointCount: 1,
            DirectPresent: true,
            DirectFallback: true,
            ExpectedWlanIdentityAvailable: true,
            InternalRouteReadPerformed: true,
            ProxyRouteAnalysisPerformed: true,
            Message:
                $"{SecretUrl} {SecretHost} {SecretGuid}",
            Limitation:
                $"{SecretEmail} {SecretIp} {SecretGuid}",
            InternalRouteEvidence: CreateRawInternalEvidence(),
            ProxyRouteAnalysis: CreateRawProxyAnalysis());
    }

    private static DestinationRouteEvidence CreateRawInternalEvidence()
    {
        RouteInterfaceDescriptor selected = new(
            InterfaceIdentity: SecretGuid,
            DisplayName: "Corporate Secret Adapter",
            Description: "Corporate Secret Adapter",
            NativeInterfaceType: "Wireless80211",
            Category: NetworkAdapterCategory.Wireless,
            OperationalState: NetworkAdapterOperationalState.Up,
            HasDefaultGateway: true,
            IsVirtual: false,
            IsVpn: false);
        return new DestinationRouteEvidence(
            CapturedAt: FixedNow,
            TargetLabel: SecretUrl,
            Purpose: RouteProbePurpose.InternalDirectTarget,
            DnsWasUsed: true,
            ResolvedAddressCount: 1,
            Status: DestinationRouteEvidenceStatus.Success,
            SelectedInterface: selected,
            AddressEvidence: Array.Empty<RouteAddressEvidence>(),
            Warnings: [SecretEmail, SecretIp],
            Message: SecretUrl);
    }

    private static ProxyEndpointRouteAnalysisResult
        CreateRawProxyAnalysis() =>
        new(
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
            Endpoints: Array.Empty<ProxyEndpointRouteEvidenceItem>(),
            Warnings: [SecretHost, SecretGuid],
            Message: SecretUrl,
            Limitation: SecretEmail);

    private static int CountOccurrences(
        string source,
        string value)
    {
        int count = 0;
        int offset = 0;
        while ((offset = source.IndexOf(
                   value,
                   offset,
                   StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += value.Length;
        }

        return count;
    }

    private static void AssertSecretsAbsent(
        string content,
        string label)
    {
        foreach (string secret in new[]
                 {
                     SecretUrl,
                     "internal-secret.example.invalid",
                     SecretHost,
                     SecretGuid,
                     SecretEmail,
                     SecretIp,
                     "Corporate Secret Adapter"
                 })
        {
            Ensure(!content.Contains(
                    secret,
                    StringComparison.OrdinalIgnoreCase),
                $"{label}에 통합 경로 비교 민감값이 남았습니다: {secret}");
        }
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
