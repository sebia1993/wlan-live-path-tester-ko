using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using WlanLivePathTester.Core.NetworkEnvironment;
using WlanLivePathTester.Core.Proxy;
using WlanLivePathTester.Core.Reporting;
using WlanLivePathTester.Core.Routing;

namespace WlanLivePathTester.ReportSmoke;

internal static class InternalProxyRouteComparisonReportWriterTests
{
    private const string SecretInternalUrl =
        "https://internal-secret.example.invalid/private.bin";
    private const string SecretProxyHost =
        "proxy-secret.example.invalid";
    private const string SecretEmail =
        "route-user@example.invalid";
    private const string SecretIp = "10.77.66.55";
    private const string SecretInternalInterfaceId =
        "C3B2C3D4-E5F6-47A8-9123-1234567890AB";
    private const string SecretProxyInterfaceId =
        "D3B2C3D4-E5F6-47A8-9123-1234567890AB";
    private const string SecretInterfaceDescription =
        "Corporate Secret Tunnel Adapter";

#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Run()
    {
        InternalProxyRouteComparisonReportDocument document =
            InternalProxyRouteComparisonReportWriter.CreateDocument(
                CreateComparison(),
                CreateProxyAnalysis(),
                CreateFinding(),
                "0.1.0-test",
                DateTimeOffset.UnixEpoch.AddHours(9));

        VerifyDocument(document);
        VerifyJsonCsvHtml(document);
        VerifyLocalFilesAndCollisionSuffix(document);
        Console.WriteLine(
            "PASS internal and proxy route comparison JSON CSV HTML SHA-256 report tests");
    }

    private static void VerifyDocument(
        InternalProxyRouteComparisonReportDocument document)
    {
        Ensure(document.SchemaVersion == "1.0",
            "경로 비교 전용 보고서 스키마가 잘못됐습니다.");
        Ensure(!document.SensitiveValuesIncluded,
            "경로 비교 보고서는 민감값 미포함을 선언해야 합니다.");
        Ensure(document.Comparison.Status == "Diverged"
               && document.Comparison.Relation
                   == "DifferentInterface"
               && document.Comparison.Code
                   == "DifferentLocalInterface",
            "구조화 비교 상태·관계·코드를 유지해야 합니다.");
        Ensure(document.Comparison.ExactIdentityComparisonPerformed
               && document.Comparison.HasCompleteComparableEvidence,
            "Diverged는 전체 GUID 정확 비교와 완전 증거를 표시해야 합니다.");
        Ensure(document.ProxyEntries.Count == 2,
            "프록시 후보와 DIRECT 두 항목을 유지해야 합니다.");
        Ensure(document.ProxyEntries[0].NetworkLookupPerformed
               && !document.ProxyEntries[1].NetworkLookupPerformed,
            "프록시 후보와 DIRECT의 네트워크 조회 경계를 구분해야 합니다.");
        Ensure(document.ParseIssues.Count == 1
               && document.ParseIssues[0].Code
                   == "DUPLICATE_DIRECTIVE",
            "파싱 Issue는 안전한 고정 코드만 유지해야 합니다.");
        Ensure(document.Finding.Code
               == "INTERNAL_PROXY_ROUTE_DIVERGED"
               && document.Finding.Severity == "Information",
            "경로 분리 Finding 코드와 심각도를 유지해야 합니다.");
        Ensure(document.Finding.Title.StartsWith(
                "=1+1",
                StringComparison.Ordinal),
            "CSV 수식 방지 테스트용 제목은 문서 모델에서 유지해야 합니다.");
    }

    private static void VerifyJsonCsvHtml(
        InternalProxyRouteComparisonReportDocument document)
    {
        string json =
            InternalProxyRouteComparisonReportWriter.RenderJson(document);
        string csv =
            InternalProxyRouteComparisonReportWriter.RenderCsv(document);
        string html =
            InternalProxyRouteComparisonReportWriter.RenderHtml(document);

        using JsonDocument parsed = JsonDocument.Parse(json);
        Ensure(parsed.RootElement
                .GetProperty("comparison")
                .GetProperty("status")
                .GetString() == "Diverged",
            "JSON에 비교 상태가 필요합니다.");
        Ensure(parsed.RootElement
                .GetProperty("proxyEntries")
                .GetArrayLength() == 2,
            "JSON에 안전한 프록시 항목 두 개가 필요합니다.");
        Ensure(!json.Contains(
                "routeEvidence",
                StringComparison.OrdinalIgnoreCase),
            "보고서 JSON에 원본 RouteEvidence 속성을 포함하면 안 됩니다.");
        Ensure(csv.StartsWith(
                "section,key,value",
                StringComparison.Ordinal),
            "CSV는 section,key,value 스키마를 사용해야 합니다.");
        Ensure(csv.Contains(
                "\"comparison\",\"status\",\"Diverged\"",
                StringComparison.Ordinal),
            "CSV에 비교 상태가 필요합니다.");
        Ensure(csv.Contains(
                "\"finding\",\"code\",\"INTERNAL_PROXY_ROUTE_DIVERGED\"",
                StringComparison.Ordinal),
            "CSV에 Finding 코드가 필요합니다.");
        Ensure(csv.Contains(
                "\"finding\",\"title\",\"'=1+1",
                StringComparison.Ordinal),
            "수식 시작 Finding 제목은 CSV에서 apostrophe로 비활성화해야 합니다.");
        Ensure(html.StartsWith(
                "<!doctype html>",
                StringComparison.OrdinalIgnoreCase)
               && html.Contains(
                   "Content-Security-Policy",
                   StringComparison.Ordinal)
               && html.Contains(
                   "Diverged",
                   StringComparison.Ordinal)
               && html.Contains(
                   "INTERNAL_PROXY_ROUTE_DIVERGED",
                   StringComparison.Ordinal),
            "HTML에 doctype, CSP, 비교 상태와 Finding 코드가 필요합니다.");
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
        Ensure(html.Contains(
                "&lt;script&gt;",
                StringComparison.OrdinalIgnoreCase),
            "Finding 제목의 HTML 태그는 인코딩해야 합니다.");

        AssertSecretsAbsent(json, "JSON");
        AssertSecretsAbsent(csv, "CSV");
        AssertSecretsAbsent(html, "HTML");
    }

    private static void VerifyLocalFilesAndCollisionSuffix(
        InternalProxyRouteComparisonReportDocument document)
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "WlanRouteComparisonReportSmoke",
            Guid.NewGuid().ToString("N"));

        try
        {
            InternalProxyRouteComparisonReportExportResult first =
                InternalProxyRouteComparisonReportWriter.WriteAll(
                    document,
                    directory,
                    "합성 경로 비교");
            InternalProxyRouteComparisonReportExportResult second =
                InternalProxyRouteComparisonReportWriter.WriteAll(
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
                "각 보고서 실행에서 JSON·CSV·HTML·SHA-256 네 파일을 생성해야 합니다.");
            Ensure(firstFiles.Zip(secondFiles).All(pair =>
                    !pair.First.Equals(
                        pair.Second,
                        StringComparison.OrdinalIgnoreCase)),
                "같은 초에 생성한 두 보고서가 기존 파일을 덮어쓰면 안 됩니다.");
            Ensure(Directory.GetFiles(directory).Length == 8,
                "두 번 생성하면 중복 없이 총 8개 파일이 있어야 합니다.");

            foreach (InternalProxyRouteComparisonReportExportResult export
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
                    "SHA256SUMS에 보고서 세 파일의 해시가 필요합니다.");
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

    private static InternalProxyRouteComparisonResult CreateComparison() =>
        new(
            Status: InternalProxyRouteComparisonStatus.Diverged,
            Relation: InternalProxyRouteRelation.DifferentInterface,
            Code:
                InternalProxyRouteComparisonCode.DifferentLocalInterface,
            InternalRouteStatus: "Success",
            ProxyAnalysisStatus: "Success",
            InternalInterfaceFingerprint: "0123456789",
            InternalInterfaceCategory: "Wireless",
            ProxyInterfaceFingerprints: ["abcdef0123"],
            ProxyInterfaceCategories: ["Tunnel"],
            ProxyEndpointCount: 1,
            SuccessfulProxyRouteCount: 1,
            DirectDirectiveCount: 1,
            ProxyAnalysisWasTruncated: false,
            ExactIdentityComparisonPerformed: true,
            Message:
                $"내부 경로 {SecretInternalUrl}와 프록시 {SecretProxyHost}가 다른 인터페이스 {SecretInternalInterfaceId}를 선택했습니다.",
            Interpretation:
                $"사용자 {SecretEmail}, 주소 {SecretIp}, 프록시 {SecretProxyHost} 경로가 분리됐습니다.",
            Limitation:
                $"전체 인터페이스 {SecretProxyInterfaceId}와 {SecretInterfaceDescription}만으로 장애를 확정할 수 없습니다.",
            NextStep:
                $"{SecretInternalUrl}와 {SecretProxyHost}의 VPN 정책을 확인하십시오.");

    private static ReportFinding CreateFinding() =>
        new(
            Code: "INTERNAL_PROXY_ROUTE_DIVERGED",
            Severity: "Information",
            Title:
                "=1+1 <script>route finding</script>",
            Evidence:
                $"내부 {SecretInternalUrl}, 프록시 {SecretProxyHost}, 인터페이스 {SecretInternalInterfaceId}",
            Interpretation:
                $"사용자 {SecretEmail}와 IP {SecretIp}의 합성 해석",
            Limitation:
                $"{SecretInterfaceDescription} {SecretProxyInterfaceId}",
            NextStep:
                $"{SecretProxyHost} 경로를 다시 확인하십시오.");

    private static ProxyEndpointRouteAnalysisResult CreateProxyAnalysis()
    {
        RouteInterfaceDescriptor selected = new(
            InterfaceIdentity: SecretProxyInterfaceId,
            DisplayName: SecretInterfaceDescription,
            Description: SecretInterfaceDescription,
            NativeInterfaceType: "Tunnel",
            Category: NetworkAdapterCategory.Tunnel,
            OperationalState: NetworkAdapterOperationalState.Up,
            HasDefaultGateway: true,
            IsVirtual: true,
            IsVpn: true);
        DestinationRouteEvidence route = new(
            CapturedAt: DateTimeOffset.UnixEpoch,
            TargetLabel: SecretProxyHost,
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
                    Message: SecretInternalUrl)
            ],
            Warnings: [SecretEmail, SecretIp],
            Message: SecretProxyHost);
        ProxyEndpointRouteEntry proxy = new(
            Sequence: 1,
            Kind: ProxyRouteDirectiveKind.HttpProxy,
            SourceSyntax: ProxyDirectiveSourceSyntax.PacKeyword,
            Scope: "all",
            Port: 8080,
            HostFingerprint: "112233aabb",
            RedactedDisplay: SecretProxyHost,
            Status: ProxyEndpointRouteEntryStatus.Success,
            SelectedInterfaceFingerprint:
                selected.IdentityFingerprint,
            SelectedInterfaceCategory: "Tunnel",
            SelectedInterfaceOperationalState: "Up",
            WlanCorrelationStatus:
                RouteWlanCorrelationStatus.DifferentInterface.ToString(),
            RouteEvidence: route,
            Message: SecretInternalUrl);
        ProxyEndpointRouteEntry direct = new(
            Sequence: 2,
            Kind: ProxyRouteDirectiveKind.Direct,
            SourceSyntax: ProxyDirectiveSourceSyntax.PacKeyword,
            Scope: "all",
            Port: null,
            HostFingerprint: "없음",
            RedactedDisplay: "DIRECT",
            Status: ProxyEndpointRouteEntryStatus.Direct,
            SelectedInterfaceFingerprint: null,
            SelectedInterfaceCategory: null,
            SelectedInterfaceOperationalState: null,
            WlanCorrelationStatus:
                RouteWlanCorrelationStatus.NotEvaluated.ToString(),
            RouteEvidence: null,
            Message: SecretProxyHost);

        return new ProxyEndpointRouteAnalysisResult(
            Status: ProxyEndpointRouteAnalysisStatus.Success,
            ParseStatus: ProxyDirectiveParseStatus.Success,
            Entries: [proxy, direct],
            ParseIssues:
            [
                new ProxyDirectiveIssue(
                    SegmentIndex: 1,
                    Severity: ProxyDirectiveIssueSeverity.Warning,
                    Code: "DUPLICATE_DIRECTIVE",
                    Message:
                        $"{SecretProxyHost} {SecretEmail} {SecretIp}")
            ],
            EndpointLimit: 8,
            WasTruncated: false,
            Message: SecretProxyHost);
    }

    private static void AssertSecretsAbsent(
        string content,
        string label)
    {
        string[] secrets =
        [
            SecretInternalUrl,
            "internal-secret.example.invalid",
            SecretProxyHost,
            SecretEmail,
            SecretIp,
            SecretInternalInterfaceId,
            SecretProxyInterfaceId,
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
