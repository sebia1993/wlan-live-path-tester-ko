using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using WlanLivePathTester.Core.NetworkEnvironment;
using WlanLivePathTester.Core.Reporting;
using WlanLivePathTester.Core.Routing;

namespace WlanLivePathTester.ReportSmoke;

internal static class InternalProxyRouteComparisonReportTests
{
    private const string WlanFingerprint = "0123456789";
    private const string TunnelFingerprint = "abcdef0123";
    private const string SecretGuid =
        "C2B2C3D4-E5F6-47A8-9123-1234567890AB";
    private const string SecretEmail = "route-report@example.invalid";
    private const string SecretIp = "10.55.66.77";
    private const string SecretUrl =
        "https://internal.example.invalid/private.bin";

#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Run()
    {
        VerifyStatusAndFindingMatrix();
        VerifyPrivacyAndFingerprintValidation();
        VerifyCsvAndHtmlSafety();
        VerifyLocalFilesAndHashes();
        Console.WriteLine(
            "PASS internal and proxy local route comparison report tests");
    }

    private static void VerifyStatusAndFindingMatrix()
    {
        (InternalProxyRouteComparisonStatus Status,
            string Code,
            string Severity)[] cases =
        [
            (InternalProxyRouteComparisonStatus.Ready,
                "INTERNAL_PROXY_LOCAL_ROUTE_ALIGNED",
                "Information"),
            (InternalProxyRouteComparisonStatus.Diverged,
                "INTERNAL_PROXY_LOCAL_ROUTE_DIVERGED",
                "Warning"),
            (InternalProxyRouteComparisonStatus.Ambiguous,
                "INTERNAL_PROXY_LOCAL_ROUTE_AMBIGUOUS",
                "Warning"),
            (InternalProxyRouteComparisonStatus.Incomplete,
                "INTERNAL_PROXY_LOCAL_ROUTE_INCOMPLETE",
                "Information")
        ];

        foreach ((InternalProxyRouteComparisonStatus status,
                  string code,
                  string severity) in cases)
        {
            InternalProxyRouteComparisonResult source = CreateResult(
                status,
                includeTunnel: status
                    == InternalProxyRouteComparisonStatus.Diverged,
                includeVirtual: status
                    == InternalProxyRouteComparisonStatus.Diverged);
            InternalProxyRouteComparisonReportDocument report =
                InternalProxyRouteComparisonReportWriter.CreateDocument(
                    source,
                    "0.1.0-test",
                    DateTimeOffset.UnixEpoch.AddHours(9));

            Ensure(report.Status == status.ToString(),
                $"보고서 상태가 잘못됐습니다: {status}");
            ReportFinding primary = report.Findings.Single(item =>
                item.Code.Equals(code, StringComparison.Ordinal));
            Ensure(primary.Severity == severity,
                $"Finding 심각도가 잘못됐습니다: {status}");
            Ensure(report.Findings.Count(item => item.Code == code) == 1,
                $"주요 Finding은 한 개여야 합니다: {status}");

            string json =
                InternalProxyRouteComparisonReportWriter.RenderJson(
                    report);
            string csv =
                InternalProxyRouteComparisonReportWriter.RenderCsv(
                    report);
            string html =
                InternalProxyRouteComparisonReportWriter.RenderHtml(
                    report);
            using JsonDocument parsed = JsonDocument.Parse(json);
            Ensure(parsed.RootElement
                    .GetProperty("status")
                    .GetString() == status.ToString(),
                $"JSON 상태가 잘못됐습니다: {status}");
            Ensure(parsed.RootElement
                    .GetProperty("findings")
                    .EnumerateArray()
                    .Count(item => item.GetProperty("code").GetString()
                        == code) == 1,
                $"JSON Finding 코드가 한 개여야 합니다: {status}");
            Ensure(csv.Contains(code, StringComparison.Ordinal),
                $"CSV에 Finding 코드가 없습니다: {status}");
            Ensure(html.Contains(primary.Title, StringComparison.Ordinal)
                   && html.Contains(
                       primary.Interpretation,
                       StringComparison.Ordinal),
                $"HTML에 사람이 읽는 Finding이 없습니다: {status}");
        }
    }

    private static void VerifyPrivacyAndFingerprintValidation()
    {
        InternalProxyRouteComparisonResult source = CreateResult(
            InternalProxyRouteComparisonStatus.Diverged,
            includeTunnel: true,
            includeVirtual: true) with
        {
            Message =
                $"unsafe {SecretGuid} {SecretEmail} {SecretIp} {SecretUrl}",
            Warnings =
            [
                $"=HYPERLINK(\"{SecretUrl}\",\"{SecretEmail}\") {SecretGuid} {SecretIp}"
            ],
            InternalInterface = new LocalRouteComparisonInterface(
                InterfaceFingerprint: SecretGuid,
                Category: NetworkAdapterCategory.Wireless,
                IsVirtual: false,
                IsVpn: false,
                IsUp: true,
                HasDefaultGateway: true,
                MatchesExpectedWlan: true),
            ExpectedWlanInterfaceFingerprint = SecretGuid
        };
        InternalProxyRouteComparisonReportDocument report =
            InternalProxyRouteComparisonReportWriter.CreateDocument(
                source,
                "0.1.0-test",
                DateTimeOffset.UnixEpoch);

        Ensure(report.InternalInterface is null,
            "전체 GUID를 인터페이스 지문으로 받아들이면 안 됩니다.");
        Ensure(report.ExpectedWlanInterfaceFingerprint is null,
            "전체 GUID를 WLAN 지문으로 보고서에 유지하면 안 됩니다.");
        string combined = string.Join(
            Environment.NewLine,
            InternalProxyRouteComparisonReportWriter.RenderJson(report),
            InternalProxyRouteComparisonReportWriter.RenderCsv(report),
            InternalProxyRouteComparisonReportWriter.RenderHtml(report));
        AssertSecretsAbsent(combined);
    }

    private static void VerifyCsvAndHtmlSafety()
    {
        InternalProxyRouteComparisonReportDocument report =
            InternalProxyRouteComparisonReportWriter.CreateDocument(
                CreateResult(
                    InternalProxyRouteComparisonStatus.Diverged,
                    includeTunnel: true,
                    includeVirtual: true) with
                {
                    Warnings = ["=HYPERLINK(\"https://safe.invalid\",\"x\")"]
                },
                "0.1.0-test",
                DateTimeOffset.UnixEpoch);
        string csv =
            InternalProxyRouteComparisonReportWriter.RenderCsv(report);
        string html =
            InternalProxyRouteComparisonReportWriter.RenderHtml(report);

        Ensure(csv.StartsWith(
                "section,key,value",
                StringComparison.Ordinal),
            "CSV는 section,key,value 스키마를 사용해야 합니다.");
        Ensure(csv.Contains("\"'=HYPERLINK", StringComparison.Ordinal),
            "수식 시작 경고는 CSV에서 비활성화해야 합니다.");
        Ensure(html.StartsWith(
                "<!doctype html>",
                StringComparison.OrdinalIgnoreCase)
               && html.Contains(
                   "Content-Security-Policy",
                   StringComparison.Ordinal),
            "HTML5 doctype과 CSP가 필요합니다.");
        Ensure(!html.Contains("<script", StringComparison.OrdinalIgnoreCase)
               && !html.Contains("<iframe", StringComparison.OrdinalIgnoreCase)
               && !html.Contains("<link", StringComparison.OrdinalIgnoreCase),
            "HTML에 외부 실행·표시 리소스를 포함하면 안 됩니다.");
    }

    private static void VerifyLocalFilesAndHashes()
    {
        InternalProxyRouteComparisonReportDocument report =
            InternalProxyRouteComparisonReportWriter.CreateDocument(
                CreateResult(
                    InternalProxyRouteComparisonStatus.Ready,
                    includeTunnel: false,
                    includeVirtual: false),
                "0.1.0-test",
                DateTimeOffset.UnixEpoch.AddHours(9));
        string directory = Path.Combine(
            Path.GetTempPath(),
            "WlanRouteComparisonReportSmoke",
            Guid.NewGuid().ToString("N"));

        try
        {
            InternalProxyRouteComparisonReportExportResult export =
                InternalProxyRouteComparisonReportWriter.WriteAll(
                    report,
                    directory,
                    "합성 로컬 경로 비교");
            string[] files =
            [
                export.JsonPath,
                export.CsvPath,
                export.HtmlPath,
                export.Sha256Path
            ];
            Ensure(files.All(File.Exists),
                "JSON·CSV·HTML·SHA-256 네 파일을 생성해야 합니다.");
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
            }

            string checksum = File.ReadAllText(export.Sha256Path);
            Ensure(export.Sha256.All(pair => checksum.Contains(
                    $"{pair.Value}  {pair.Key}",
                    StringComparison.Ordinal)),
                "SHA256SUMS에 세 보고서 해시가 필요합니다.");
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private static InternalProxyRouteComparisonResult CreateResult(
        InternalProxyRouteComparisonStatus status,
        bool includeTunnel,
        bool includeVirtual)
    {
        LocalRouteComparisonInterface internalInterface = new(
            InterfaceFingerprint: WlanFingerprint,
            Category: NetworkAdapterCategory.Wireless,
            IsVirtual: false,
            IsVpn: false,
            IsUp: true,
            HasDefaultGateway: true,
            MatchesExpectedWlan: true);
        LocalRouteComparisonInterface proxyInterface = new(
            InterfaceFingerprint: status
                == InternalProxyRouteComparisonStatus.Diverged
                    ? TunnelFingerprint
                    : WlanFingerprint,
            Category: includeTunnel
                ? NetworkAdapterCategory.Tunnel
                : NetworkAdapterCategory.Wireless,
            IsVirtual: includeVirtual,
            IsVpn: includeTunnel,
            IsUp: true,
            HasDefaultGateway: true,
            MatchesExpectedWlan: status
                == InternalProxyRouteComparisonStatus.Diverged
                    ? false
                    : true);

        return new InternalProxyRouteComparisonResult(
            EvaluatedAt: DateTimeOffset.UnixEpoch,
            Status: status,
            InternalRouteStatus:
                DestinationRouteEvidenceStatus.Success,
            ProxyRouteStatus: status switch
            {
                InternalProxyRouteComparisonStatus.Ambiguous =>
                    ProxyEndpointRouteAnalysisStatus.MultipleInterfaces,
                InternalProxyRouteComparisonStatus.Incomplete =>
                    ProxyEndpointRouteAnalysisStatus.PartialSuccess,
                _ => ProxyEndpointRouteAnalysisStatus.Success
            },
            InternalInterface: internalInterface,
            ProxyInterface: status is
                InternalProxyRouteComparisonStatus.Ambiguous
                    or InternalProxyRouteComparisonStatus.Incomplete
                ? null
                : proxyInterface,
            ExpectedWlanInterfaceFingerprint: WlanFingerprint,
            SameLocalInterface: status switch
            {
                InternalProxyRouteComparisonStatus.Ready => true,
                InternalProxyRouteComparisonStatus.Diverged => false,
                _ => null
            },
            InternalEvidencePartial: false,
            ProxyEvidencePartial: status
                == InternalProxyRouteComparisonStatus.Incomplete,
            ProxyDirectPathSelected: false,
            ProxyDirectFallbackPresent: true,
            ProxyCandidateCount: 2,
            ProxySuccessfulCandidateCount: status
                == InternalProxyRouteComparisonStatus.Incomplete
                    ? 1
                    : 2,
            ProxyDistinctInterfaceCount: status
                == InternalProxyRouteComparisonStatus.Ambiguous
                    ? 2
                    : 1,
            AnyVirtualInterface: includeVirtual,
            AnyVpnOrTunnelInterface: includeTunnel,
            Warnings: ["합성 비교 경고"],
            Message: "합성 비교 결과",
            Limitation: "합성 비교 한계");
    }

    private static void AssertSecretsAbsent(string content)
    {
        foreach (string secret in new[]
                 {
                     SecretGuid,
                     SecretEmail,
                     SecretIp,
                     SecretUrl,
                     "internal.example.invalid"
                 })
        {
            Ensure(!content.Contains(
                    secret,
                    StringComparison.OrdinalIgnoreCase),
                $"보고서 출력에 민감값이 남았습니다: {secret}");
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
