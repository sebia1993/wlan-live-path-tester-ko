using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using WlanLivePathTester.Core.NetworkEnvironment;
using WlanLivePathTester.Core.Proxy;
using WlanLivePathTester.Core.Reporting;
using WlanLivePathTester.Core.Routing;

namespace WlanLivePathTester.ReportSmoke;

internal static class InternalProxyRouteComparisonRunReportV2Tests
{
    private const string SecretInternalTarget =
        "https://internal-report-secret.example.invalid/private.bin";
    private const string SecretExternalTarget =
        "https://external-report-secret.example.invalid/file.bin";
    private const string SecretProxyHost =
        "proxy-report-secret.example.invalid";
    private const string SecretEmail =
        "route-report@example.invalid";
    private const string SecretIp = "10.66.77.88";
    private const string SecretInternalGuid =
        "55B2C3D4-E5F6-47A8-9123-1234567890AB";
    private const string SecretProxyGuid =
        "66B2C3D4-E5F6-47A8-9123-1234567890AB";
    private const string SecretInterfaceDescription =
        "Corporate Secret Proxy Tunnel";
    private const string InternalFingerprint = "0123456789";
    private const string ProxyFingerprint = "abcdef0123";
    private static readonly DateTimeOffset FixedTime =
        DateTimeOffset.UnixEpoch.AddDays(50);

#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Run()
    {
        InternalProxyRouteComparisonRunReportDocument document =
            InternalProxyRouteComparisonRunReportWriter.CreateDocument(
                CreateCompletedDivergedRun(),
                "=1+1 <script>version</script>",
                FixedTime.AddHours(9));

        VerifySafeSnapshot(document);
        VerifyJsonCsvHtml(document);
        VerifyDirectRunReport();
        VerifyUnknownAndInvalidStructuredValues();
        VerifyLocalFilesCollisionAndHashes(document);
        Console.WriteLine(
            "PASS coordinated route comparison report v2 privacy formats and hashes");
    }

    private static void VerifySafeSnapshot(
        InternalProxyRouteComparisonRunReportDocument document)
    {
        InternalProxyRouteComparisonRunReportSnapshot snapshot =
            document.RouteComparison;
        Ensure(document.SchemaVersion == "1.0",
            "경로 비교 보고서 스키마가 잘못됐습니다.");
        Ensure(!document.SensitiveValuesIncluded,
            "경로 비교 보고서는 민감값 미포함을 선언해야 합니다.");
        Ensure(snapshot.RunStatus == "Completed"
               && snapshot.ProxyPlanCode == "ManualProxySelected"
               && snapshot.ProxyExecutionStatus == "Completed"
               && snapshot.ProxyRouteStatus == "Success",
            "실행·계획·프록시 상태를 구조화해 유지해야 합니다.");
        Ensure(snapshot.TargetScheme == "https"
               && snapshot.ParsedProxyEndpointCount == 1
               && snapshot.ApplicableProxyEndpointCount == 1
               && snapshot.AnalyzedProxyEndpointCount == 1
               && snapshot.SuccessfulProxyEndpointCount == 1,
            "대상 스킴과 후보 집계를 유지해야 합니다.");
        Ensure(snapshot.DirectPresent
               && !snapshot.DirectIsPrimary
               && snapshot.DirectFallback,
            "프록시 뒤 DIRECT fallback을 유지해야 합니다.");
        Ensure(snapshot.OperationCompleted
               && snapshot.HasComparableResult,
            "완료와 비교 결과 존재 여부를 유지해야 합니다.");

        InternalProxyRouteComparisonReportComparison comparison =
            snapshot.Comparison
            ?? throw new InvalidOperationException(
                "Diverged 비교 스냅샷이 필요합니다.");
        Ensure(comparison.Status == "Diverged"
               && comparison.Relation == "DifferentInterface"
               && comparison.Code == "DifferentLocalInterface",
            "비교 상태·관계·원인 코드를 유지해야 합니다.");
        Ensure(comparison.InternalInterfaceFingerprint
               == InternalFingerprint
               && comparison.ProxyInterfaceFingerprints
                   .SequenceEqual([ProxyFingerprint]),
            "검증된 10자리 인터페이스 지문을 유지해야 합니다.");
        Ensure(comparison.InternalInterfaceCategory == "Wireless"
               && comparison.ProxyInterfaceCategories
                   .SequenceEqual(["Tunnel"]),
            "알려진 인터페이스 범주를 유지해야 합니다.");
        Ensure(comparison.ExactIdentityComparisonPerformed
               && comparison.HasCompleteComparableEvidence,
            "Diverged의 전체 GUID 정확 비교 근거를 유지해야 합니다.");

        InternalProxyRouteComparisonReportProxyEntry entry =
            snapshot.ProxyEntries.Single();
        Ensure(entry.Sequence == 1
               && entry.Transport == "Http"
               && entry.AppliesToScheme == "all"
               && entry.Port == 8080,
            "안전한 프록시 후보 순서·종류·범위·포트를 유지해야 합니다.");
        Ensure(entry.HostFingerprint == "112233aabb"
               && entry.SelectedInterfaceFingerprint
                   == ProxyFingerprint
               && entry.SelectedInterfaceCategory == "Tunnel",
            "원문 대신 프록시·인터페이스 지문과 범주를 유지해야 합니다.");
        Ensure(entry.RouteStatus == "Success"
               && entry.WlanCorrelationStatus
                   == "DifferentInterface",
            "경로 상태와 WLAN 상관 상태를 유지해야 합니다.");
        Ensure(snapshot.Finding.Code
               == "INTERNAL_PROXY_ROUTE_DIVERGED"
               && snapshot.Finding.Severity == "Information",
            "현재 실행 Finding 코드와 심각도를 유지해야 합니다.");
    }

    private static void VerifyJsonCsvHtml(
        InternalProxyRouteComparisonRunReportDocument document)
    {
        string json =
            InternalProxyRouteComparisonRunReportWriter.RenderJson(
                document);
        string csv =
            InternalProxyRouteComparisonRunReportWriter.RenderCsv(
                document);
        string html =
            InternalProxyRouteComparisonRunReportWriter.RenderHtml(
                document);

        using JsonDocument parsed = JsonDocument.Parse(json);
        Ensure(parsed.RootElement
                .GetProperty("routeComparison")
                .GetProperty("runStatus")
                .GetString() == "Completed",
            "JSON에 실행 상태가 필요합니다.");
        Ensure(parsed.RootElement
                .GetProperty("routeComparison")
                .GetProperty("comparison")
                .GetProperty("status")
                .GetString() == "Diverged",
            "JSON에 비교 상태가 필요합니다.");
        Ensure(parsed.RootElement
                .GetProperty("routeComparison")
                .GetProperty("proxyEntries")
                .GetArrayLength() == 1,
            "JSON에 안전 프록시 후보 한 개가 필요합니다.");
        foreach (string forbiddenProperty in new[]
                 {
                     "internalRouteEvidence",
                     "proxyExecution",
                     "selectedInterfaceIdentity",
                     "endpointLabel",
                     "warnings"
                 })
        {
            Ensure(!json.Contains(
                    $"\"{forbiddenProperty}\"",
                    StringComparison.OrdinalIgnoreCase),
                $"보고서 JSON에 원본 속성이 남았습니다: {forbiddenProperty}");
        }

        Ensure(csv.StartsWith(
                "section,key,value",
                StringComparison.Ordinal),
            "CSV는 section,key,value 스키마를 사용해야 합니다.");
        Ensure(csv.Contains(
                "\"run\",\"status\",\"Completed\"",
                StringComparison.Ordinal)
               && csv.Contains(
                   "\"comparison\",\"status\",\"Diverged\"",
                   StringComparison.Ordinal)
               && csv.Contains(
                   "\"finding\",\"code\",\"INTERNAL_PROXY_ROUTE_DIVERGED\"",
                   StringComparison.Ordinal),
            "CSV에 실행·비교·Finding 코드가 필요합니다.");
        Ensure(csv.Contains(
                "\"metadata\",\"applicationVersion\",\"'=1+1",
                StringComparison.Ordinal),
            "수식 시작 애플리케이션 버전은 CSV에서 비활성화해야 합니다.");

        Ensure(html.StartsWith(
                "<!doctype html>",
                StringComparison.OrdinalIgnoreCase)
               && html.Contains(
                   "Content-Security-Policy",
                   StringComparison.Ordinal)
               && html.Contains(
                   "INTERNAL_PROXY_ROUTE_DIVERGED",
                   StringComparison.Ordinal)
               && html.Contains(
                   "Diverged",
                   StringComparison.Ordinal),
            "HTML에 doctype·CSP·비교 상태·Finding 코드가 필요합니다.");
        Ensure(html.Contains(
                "&lt;script&gt;version&lt;/script&gt;",
                StringComparison.OrdinalIgnoreCase),
            "애플리케이션 버전의 HTML 태그를 인코딩해야 합니다.");
        Ensure(!html.Contains(
                "<script",
                StringComparison.OrdinalIgnoreCase)
               && !html.Contains(
                   "<iframe",
                   StringComparison.OrdinalIgnoreCase)
               && !html.Contains(
                   "<link",
                   StringComparison.OrdinalIgnoreCase),
            "HTML에 실행·외부 표시 리소스를 포함하면 안 됩니다.");

        AssertSecretsAbsent(json, "JSON");
        AssertSecretsAbsent(csv, "CSV");
        AssertSecretsAbsent(html, "HTML");
    }

    private static void VerifyDirectRunReport()
    {
        InternalProxyRouteComparisonRunResult direct =
            CreateRun(
                InternalProxyRouteComparisonRunStatus
                    .DirectPathSelected,
                comparison: null,
                execution: null) with
            {
                ProxyPlanStatus =
                    ProxyDirectiveRouteAnalysisPlanStatus.DirectOnly,
                ProxyPlanCode =
                    ProxyDirectiveRouteAnalysisPlanCode.ManualDirect,
                ProxyExecutionStatus = null,
                ProxyRouteStatus = null,
                ProxyDecision = ProxyEndpointDecision.Direct,
                DirectPresent = true,
                DirectIsPrimary = true,
                DirectFallback = false,
                InternalRouteReadPerformed = false,
                ProxyRouteAnalysisPerformed = false
            };
        InternalProxyRouteComparisonRunReportDocument document =
            InternalProxyRouteComparisonRunReportWriter.CreateDocument(
                direct,
                "0.1.0-test",
                FixedTime);

        Ensure(document.RouteComparison.Comparison is null
               && document.RouteComparison.ProxyEntries.Count == 0,
            "DIRECT 우선 보고서에 비교·프록시 후보를 만들면 안 됩니다.");
        Ensure(document.RouteComparison.Finding.Code
               == "INTERNAL_PROXY_ROUTE_RUN_DIRECT_PRIMARY",
            "DIRECT 우선 실행 Finding이 필요합니다.");
        Ensure(document.RouteComparison.InternalRouteReadPerformed
                   == false
               && document.RouteComparison
                   .ProxyRouteAnalysisPerformed == false,
            "DIRECT 우선의 zero-read 경계를 유지해야 합니다.");
    }

    private static void VerifyUnknownAndInvalidStructuredValues()
    {
        InternalProxyRouteComparisonResult comparison =
            CreateComparison() with
            {
                Status = (InternalProxyRouteComparisonStatus)999,
                Relation = (InternalProxyRouteRelation)999,
                Code = (InternalProxyRouteComparisonCode)999,
                InternalInterfaceFingerprint = SecretGuidLikeValue(),
                InternalInterfaceCategory =
                    (NetworkAdapterCategory)999,
                ProxyInterfaceFingerprints =
                    ["invalid", ProxyFingerprint],
                ProxyInterfaceCategories =
                    [(NetworkAdapterCategory)999],
                ProxyApplicableEndpointCount = -1,
                ProxyAnalyzedEndpointCount = -2,
                ProxySuccessfulEndpointCount = -3,
                ProxyDistinctInterfaceCount = -4,
                ProxySkippedAfterDirectCount = -5
            };
        InternalProxyRouteComparisonRunResult run = CreateRun(
            (InternalProxyRouteComparisonRunStatus)999,
            comparison,
            execution: null) with
        {
            ProxySourceKind = (ProxyDirectiveSourceKind)999,
            ProxySelectionStatus =
                (ProxyDirectiveSourceSelectionStatus)999,
            ProxyPlanStatus =
                (ProxyDirectiveRouteAnalysisPlanStatus)999,
            ProxyPlanCode =
                (ProxyDirectiveRouteAnalysisPlanCode)999,
            ProxyEndpointSourceKind =
                (ProxyEndpointSourceKind)999,
            ProxyDecision = (ProxyEndpointDecision)999,
            TargetScheme = SecretProxyHost,
            ParsedProxyEndpointCount = -1,
            ApplicableProxyEndpointCount = -2,
            AnalyzedProxyEndpointCount = -3,
            SuccessfulProxyEndpointCount = -4,
            DistinctProxyInterfaceCount = -5
        };
        InternalProxyRouteComparisonRunReportSnapshot snapshot =
            InternalProxyRouteComparisonRunReportSnapshotMapper
                .FromResult(run);

        Ensure(snapshot.RunStatus == "Unknown"
               && snapshot.ProxySourceKind == "Unknown"
               && snapshot.ProxyPlanStatus == "Unknown"
               && snapshot.ProxyDecision == "Unknown"
               && snapshot.TargetScheme == "none",
            "정의되지 않은 enum과 허용되지 않는 스킴을 안전값으로 치환해야 합니다.");
        Ensure(snapshot.ParsedProxyEndpointCount == 0
               && snapshot.ApplicableProxyEndpointCount == 0
               && snapshot.AnalyzedProxyEndpointCount == 0
               && snapshot.SuccessfulProxyEndpointCount == 0
               && snapshot.DistinctProxyInterfaceCount == 0,
            "음수 실행 집계를 0으로 제한해야 합니다.");
        InternalProxyRouteComparisonReportComparison safeComparison =
            snapshot.Comparison
            ?? throw new InvalidOperationException(
                "안전 비교 스냅샷이 필요합니다.");
        Ensure(safeComparison.Status == "Unknown"
               && safeComparison.Relation == "Unknown"
               && safeComparison.Code == "Unknown",
            "정의되지 않은 비교 enum을 숫자값으로 반사하면 안 됩니다.");
        Ensure(safeComparison.InternalInterfaceFingerprint is null
               && safeComparison.InternalInterfaceCategory == string.Empty,
            "전체 GUID·알 수 없는 범주를 안전 인터페이스 값으로 받아들이면 안 됩니다.");
        Ensure(safeComparison.ProxyInterfaceFingerprints
                .SequenceEqual([ProxyFingerprint])
               && safeComparison.ProxyInterfaceCategories.Count == 0,
            "유효한 지문만 유지하고 알 수 없는 범주를 제외해야 합니다.");
        Ensure(safeComparison.ProxyApplicableEndpointCount == 0
               && safeComparison.ProxySkippedAfterDirectCount == 0,
            "음수 비교 집계를 0으로 제한해야 합니다.");
    }

    private static void VerifyLocalFilesCollisionAndHashes(
        InternalProxyRouteComparisonRunReportDocument document)
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "WlanRouteComparisonRunReportV2",
            Guid.NewGuid().ToString("N"));
        try
        {
            InternalProxyRouteComparisonRunReportExportResult first =
                InternalProxyRouteComparisonRunReportWriter.WriteAll(
                    document,
                    directory,
                    "합성 경로 비교");
            InternalProxyRouteComparisonRunReportExportResult second =
                InternalProxyRouteComparisonRunReportWriter.WriteAll(
                    document,
                    directory,
                    "합성 경로 비교");
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
                "각 실행에서 JSON·CSV·HTML·SHA-256 네 파일을 생성해야 합니다.");
            Ensure(firstFiles.Zip(secondFiles).All(pair =>
                    !pair.First.Equals(
                        pair.Second,
                        StringComparison.OrdinalIgnoreCase)),
                "같은 초의 두 보고서가 기존 파일을 덮어쓰면 안 됩니다.");
            Ensure(Directory.GetFiles(directory).Length == 8,
                "두 번 생성하면 총 8개 독립 파일이 필요합니다.");

            foreach (InternalProxyRouteComparisonRunReportExportResult export
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
                        $"SHA-256이 일치하지 않습니다: {fileName}");
                    AssertSecretsAbsent(
                        File.ReadAllText(path),
                        fileName);
                }

                string checksum = File.ReadAllText(export.Sha256Path);
                Ensure(export.Sha256.All(pair => checksum.Contains(
                        $"{pair.Value}  {pair.Key}",
                        StringComparison.Ordinal)),
                    "SHA256SUMS에 세 보고서 파일의 해시가 필요합니다.");
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

    private static InternalProxyRouteComparisonRunResult
        CreateCompletedDivergedRun()
    {
        ProxyEndpointRouteAnalysisResult analysis =
            CreateProxyAnalysis();
        return CreateRun(
            InternalProxyRouteComparisonRunStatus.Completed,
            CreateComparison(),
            CreateExecution(analysis)) with
        {
            InternalRouteEvidence = CreateRouteEvidence(
                RouteProbePurpose.InternalDirectTarget,
                SecretInternalGuid,
                NetworkAdapterCategory.Wireless,
                SecretInternalTarget),
            Message =
                $"{SecretInternalTarget} {SecretProxyHost} {SecretEmail}",
            Limitation =
                $"{SecretExternalTarget} {SecretIp} {SecretProxyGuid}"
        };
    }

    private static InternalProxyRouteComparisonRunResult CreateRun(
        InternalProxyRouteComparisonRunStatus status,
        InternalProxyRouteComparisonResult? comparison,
        ProxyDirectiveRouteAnalysisExecutionResult<
            ProxyEndpointRouteAnalysisResult>? execution) =>
        new(
            CompletedAt: FixedTime,
            Status: status,
            ProxySourceKind:
                ProxyDirectiveSourceKind.ManualProxyConfiguration,
            ProxySelectionStatus:
                ProxyDirectiveSourceSelectionStatus.Selected,
            ProxyPlanStatus:
                ProxyDirectiveRouteAnalysisPlanStatus
                    .AnalyzeProxyEndpoints,
            ProxyPlanCode:
                ProxyDirectiveRouteAnalysisPlanCode.ManualProxySelected,
            ProxyExecutionStatus: execution?.Status
                ?? ProxyDirectiveRouteAnalysisExecutionStatus.Completed,
            ProxyEndpointSourceKind:
                ProxyEndpointSourceKind.ManualServerList,
            ProxyDecision:
                ProxyEndpointDecision.ProxyWithDirectFallback,
            TargetScheme: "https",
            InternalRouteStatus:
                DestinationRouteEvidenceStatus.Success,
            ProxyRouteStatus: execution?.Analysis?.Status
                ?? ProxyEndpointRouteAnalysisStatus.Success,
            Comparison: comparison,
            ParsedProxyEndpointCount: 1,
            ApplicableProxyEndpointCount: 1,
            AnalyzedProxyEndpointCount: 1,
            SuccessfulProxyEndpointCount: 1,
            DistinctProxyInterfaceCount: 1,
            DirectPresent: true,
            DirectIsPrimary: false,
            DirectFallback: true,
            ProxyParseErrorsPresent: false,
            ExpectedWlanIdentityAvailable: true,
            InternalRouteReadPerformed: true,
            ProxyRouteAnalysisPerformed: true,
            Message: SecretInternalTarget,
            Limitation: SecretProxyHost,
            InternalRouteEvidence: null,
            ProxyExecution: execution);

    private static InternalProxyRouteComparisonResult
        CreateComparison() =>
        new(
            EvaluatedAt: FixedTime,
            Status: InternalProxyRouteComparisonStatus.Diverged,
            Relation: InternalProxyRouteRelation.DifferentInterface,
            Code:
                InternalProxyRouteComparisonCode.DifferentLocalInterface,
            InternalRouteStatus:
                DestinationRouteEvidenceStatus.Success,
            ProxyExecutionStatus:
                ProxyDirectiveRouteAnalysisExecutionStatus.Completed,
            ProxyAnalysisStatus:
                ProxyEndpointRouteAnalysisStatus.Success,
            ProxySourceKind:
                ProxyDirectiveSourceKind.ManualProxyConfiguration,
            ProxyPlanCode:
                ProxyDirectiveRouteAnalysisPlanCode.ManualProxySelected,
            InternalInterfaceFingerprint: InternalFingerprint,
            InternalInterfaceCategory:
                NetworkAdapterCategory.Wireless,
            ProxyInterfaceFingerprints: [ProxyFingerprint],
            ProxyInterfaceCategories:
                [NetworkAdapterCategory.Tunnel],
            ProxyApplicableEndpointCount: 1,
            ProxyAnalyzedEndpointCount: 1,
            ProxySuccessfulEndpointCount: 1,
            ProxyDistinctInterfaceCount: 1,
            ProxySkippedAfterDirectCount: 0,
            ProxyDirectPresent: true,
            ProxyDirectIsPrimary: false,
            ProxyDirectFallbackPresent: true,
            ProxyParseErrorsPresent: false,
            ExactIdentityComparisonPerformed: true,
            Message: SecretInternalTarget,
            Interpretation: SecretProxyHost,
            Limitation: SecretEmail,
            NextStep: $"{SecretIp} {SecretInternalGuid}");

    private static ProxyEndpointRouteAnalysisResult CreateProxyAnalysis()
    {
        ProxyEndpointRouteEvidenceItem endpoint = new(
            Sequence: 1,
            EndpointLabel: SecretProxyHost,
            HostFingerprint: "112233aabb",
            AppliesToScheme: null,
            Transport: ProxyEndpointTransport.Http,
            Port: 8080,
            RouteStatus: DestinationRouteEvidenceStatus.Success,
            WlanCorrelationStatus:
                RouteWlanCorrelationStatus.DifferentInterface,
            SelectedInterfaceFingerprint: ProxyFingerprint,
            SelectedInterfaceCategory:
                NetworkAdapterCategory.Tunnel,
            SelectedInterfaceIsVirtual: true,
            SelectedInterfaceIsVpn: true,
            SelectedInterfaceIsUp: true,
            SelectedInterfaceHasDefaultGateway: true,
            ResolvedAddressCount: 1,
            SuccessfulAddressCount: 1,
            FailedAddressCount: 0,
            Message: SecretInternalTarget,
            Warnings:
                [$"{SecretEmail} {SecretIp} {SecretProxyGuid}"])
        {
            SelectedInterfaceIdentity = SecretProxyGuid
        };
        return new ProxyEndpointRouteAnalysisResult(
            CapturedAt: FixedTime,
            Status: ProxyEndpointRouteAnalysisStatus.Success,
            SourceKind: ProxyEndpointSourceKind.ManualServerList,
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
            Warnings: [SecretEmail, SecretIp],
            Message: SecretProxyHost,
            Limitation: SecretExternalTarget);
    }

    private static ProxyDirectiveRouteAnalysisExecutionResult<
        ProxyEndpointRouteAnalysisResult> CreateExecution(
            ProxyEndpointRouteAnalysisResult analysis)
    {
        ProxyDirectiveSourceSelectionResult selection =
            ProxyDirectiveSourceSelectionPolicy.Select(
                targetDecisionWasEvaluated: false,
                targetDecisionIsDirect: false,
                targetSpecificDirective: null,
                manualProxyConfigured: true,
                manualProxyDirective:
                    $"PROXY {SecretProxyHost}:8080; DIRECT");
        return ProxyDirectiveRouteAnalysisExecutor.ExecuteAsync(
                selection,
                (_, _) => Task.FromResult(analysis))
            .GetAwaiter()
            .GetResult();
    }

    private static DestinationRouteEvidence CreateRouteEvidence(
        RouteProbePurpose purpose,
        string interfaceId,
        NetworkAdapterCategory category,
        string targetLabel)
    {
        RouteInterfaceDescriptor selected = new(
            InterfaceIdentity: interfaceId,
            DisplayName: SecretInterfaceDescription,
            Description: SecretInterfaceDescription,
            NativeInterfaceType: category.ToString(),
            Category: category,
            OperationalState: NetworkAdapterOperationalState.Up,
            HasDefaultGateway: true,
            IsVirtual: category == NetworkAdapterCategory.Tunnel,
            IsVpn: category == NetworkAdapterCategory.Tunnel);
        return new DestinationRouteEvidence(
            CapturedAt: FixedTime,
            TargetLabel: targetLabel,
            Purpose: purpose,
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
                    Message: SecretExternalTarget)
            ],
            Warnings: [SecretEmail, SecretIp],
            Message: SecretProxyHost);
    }

    private static string SecretGuidLikeValue() =>
        SecretInternalGuid;

    private static void AssertSecretsAbsent(
        string content,
        string label)
    {
        string[] secrets =
        [
            SecretInternalTarget,
            "internal-report-secret.example.invalid",
            SecretExternalTarget,
            "external-report-secret.example.invalid",
            SecretProxyHost,
            SecretEmail,
            SecretIp,
            SecretInternalGuid,
            SecretProxyGuid,
            SecretInterfaceDescription
        ];

        foreach (string secret in secrets)
        {
            Ensure(!content.Contains(
                    secret,
                    StringComparison.OrdinalIgnoreCase),
                $"{label}에 경로 비교 민감값이 남았습니다: {secret}");
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
