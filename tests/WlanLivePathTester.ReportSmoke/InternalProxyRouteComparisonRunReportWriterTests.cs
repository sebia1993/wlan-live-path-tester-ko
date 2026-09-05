using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using WlanLivePathTester.Core.NetworkEnvironment;
using WlanLivePathTester.Core.Proxy;
using WlanLivePathTester.Core.Reporting;
using WlanLivePathTester.Core.Routing;

namespace WlanLivePathTester.ReportSmoke;

internal static class InternalProxyRouteComparisonRunReportWriterTests
{
    private const string InternalFingerprint = "0123456789";
    private const string ProxyFingerprint = "abcdef0123";
    private const string SecretGuid =
        "E3B2C3D4-E5F6-47A8-9123-1234567890AB";
    private const string SecretUrl =
        "https://internal-secret.example.invalid/private.bin";
    private const string SecretHost =
        "proxy-secret.example.invalid";
    private const string SecretEmail =
        "route-report@example.invalid";
    private const string SecretIp = "10.77.66.55";
    private static readonly DateTimeOffset FixedNow =
        DateTimeOffset.UnixEpoch.AddDays(8);

#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Run()
    {
        InternalProxyRouteComparisonRunReportDocument document =
            InternalProxyRouteComparisonRunReportWriter.CreateDocument(
                CreateMaliciousRun(),
                $"=1+1 <script>version</script> {SecretEmail} {SecretIp}",
                FixedNow);

        VerifyDocument(document);
        VerifyJsonCsvAndHtml(document);
        VerifyLocalFilesHashesAndCollisionSuffix(document);
        Console.WriteLine(
            "PASS coordinated route comparison JSON CSV HTML SHA-256 report tests");
    }

    private static void VerifyDocument(
        InternalProxyRouteComparisonRunReportDocument document)
    {
        Ensure(document.SchemaVersion == "1.0"
               && !document.SensitiveValuesIncluded,
            "보고서 스키마와 민감값 미포함 선언이 필요합니다.");
        Ensure(document.RouteComparison.RunStatus == "Completed"
               && document.RouteComparison.ComparisonStatus
                   == "Diverged",
            "실행·비교 상태를 구조화해야 합니다.");
        Ensure(document.RouteComparison.InternalInterface
                ?.InterfaceFingerprint == InternalFingerprint
               && document.RouteComparison.ProxyInterface
                   ?.InterfaceFingerprint == ProxyFingerprint,
            "검증된 짧은 인터페이스 지문을 유지해야 합니다.");
        Ensure(document.RouteComparison.Finding.Code
               == "INTERNAL_PROXY_ROUTE_COMPARISON_DIVERGED"
               && document.RouteComparison.Finding.Severity
                   == "Warning",
            "전용 보고서에 고정 경로 분기 Finding이 필요합니다.");
        Ensure(document.ApplicationVersion.StartsWith(
                "=1+1 <script>version</script>",
                StringComparison.Ordinal),
            "CSV·HTML 보호 테스트용 버전 문자열을 문서 모델에서 유지해야 합니다.");
        Ensure(!document.ApplicationVersion.Contains(
                SecretEmail,
                StringComparison.OrdinalIgnoreCase)
               && !document.ApplicationVersion.Contains(
                   SecretIp,
                   StringComparison.OrdinalIgnoreCase),
            "애플리케이션 버전의 이메일·IP는 문서 생성 단계에서 마스킹해야 합니다.");
    }

    private static void VerifyJsonCsvAndHtml(
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
                .GetProperty("comparisonStatus")
                .GetString() == "Diverged",
            "JSON에 비교 상태가 필요합니다.");
        Ensure(parsed.RootElement
                .GetProperty("routeComparison")
                .GetProperty("finding")
                .GetProperty("code")
                .GetString()
            == "INTERNAL_PROXY_ROUTE_COMPARISON_DIVERGED",
            "JSON에 Finding 코드가 필요합니다.");
        Ensure(!json.Contains(
                "InternalRouteEvidence",
                StringComparison.OrdinalIgnoreCase)
               && !json.Contains(
                   "ProxyRouteAnalysis",
                   StringComparison.OrdinalIgnoreCase),
            "JSON에 원본 경로 근거 속성을 포함하면 안 됩니다.");

        Ensure(csv.StartsWith(
                "section,key,value",
                StringComparison.Ordinal),
            "CSV는 section,key,value 스키마를 사용해야 합니다.");
        Ensure(csv.Contains(
                "\"run\",\"status\",\"Completed\"",
                StringComparison.Ordinal)
               && csv.Contains(
                   "\"finding\",\"code\",\"INTERNAL_PROXY_ROUTE_COMPARISON_DIVERGED\"",
                   StringComparison.Ordinal),
            "CSV에 실행 상태와 Finding 코드가 필요합니다.");
        Ensure(csv.Contains(
                "\"metadata\",\"applicationVersion\",\"'=1+1",
                StringComparison.Ordinal),
            "수식 시작 버전 문자열은 CSV에서 apostrophe로 비활성화해야 합니다.");

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
                   "INTERNAL_PROXY_ROUTE_COMPARISON_DIVERGED",
                   StringComparison.Ordinal),
            "HTML에 doctype, CSP, 비교 상태와 Finding 코드가 필요합니다.");
        Ensure(!html.Contains(
                "<script>",
                StringComparison.OrdinalIgnoreCase)
               && !html.Contains(
                   "<iframe",
                   StringComparison.OrdinalIgnoreCase)
               && !html.Contains(
                   "<link",
                   StringComparison.OrdinalIgnoreCase),
            "HTML에 실행·외부 표시 리소스를 포함하면 안 됩니다.");
        Ensure(html.Contains(
                "&lt;script&gt;version&lt;/script&gt;",
                StringComparison.OrdinalIgnoreCase),
            "버전 문자열의 HTML 태그를 인코딩해야 합니다.");

        AssertSecretsAbsent(json, "JSON");
        AssertSecretsAbsent(csv, "CSV");
        AssertSecretsAbsent(html, "HTML");
    }

    private static void VerifyLocalFilesHashesAndCollisionSuffix(
        InternalProxyRouteComparisonRunReportDocument document)
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "WlanCoordinatedRouteReportSmoke",
            Guid.NewGuid().ToString("N"));

        try
        {
            InternalProxyRouteComparisonRunReportExportResult first =
                InternalProxyRouteComparisonRunReportWriter.WriteAll(
                    document,
                    directory,
                    "합성 내부 프록시 경로");
            InternalProxyRouteComparisonRunReportExportResult second =
                InternalProxyRouteComparisonRunReportWriter.WriteAll(
                    document,
                    directory,
                    "합성 내부 프록시 경로");

            string[] firstPaths =
            [
                first.JsonPath,
                first.CsvPath,
                first.HtmlPath,
                first.Sha256Path
            ];
            string[] secondPaths =
            [
                second.JsonPath,
                second.CsvPath,
                second.HtmlPath,
                second.Sha256Path
            ];
            Ensure(firstPaths.All(File.Exists)
                   && secondPaths.All(File.Exists),
                "각 실행에서 JSON·CSV·HTML·SHA-256 네 파일을 생성해야 합니다.");
            Ensure(firstPaths.Zip(secondPaths).All(pair =>
                    !pair.First.Equals(
                        pair.Second,
                        StringComparison.OrdinalIgnoreCase)),
                "같은 초의 두 보고서가 기존 파일을 덮어쓰면 안 됩니다.");
            Ensure(Directory.GetFiles(directory).Length == 8,
                "두 번 생성하면 독립 파일이 총 8개여야 합니다.");

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

                string checksums = File.ReadAllText(
                    export.Sha256Path);
                Ensure(export.Sha256.All(pair => checksums.Contains(
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
            InternalRouteEvidence: null,
            ProxyRouteAnalysis: null);
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
                     SecretIp
                 })
        {
            Ensure(!content.Contains(
                    secret,
                    StringComparison.OrdinalIgnoreCase),
                $"{label}에 민감값이 남았습니다: {secret}");
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
