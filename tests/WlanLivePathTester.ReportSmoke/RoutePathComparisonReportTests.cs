using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using WlanLivePathTester.Core.Reporting;
using WlanLivePathTester.Core.Routing;

namespace WlanLivePathTester.ReportSmoke;

internal static class RoutePathComparisonReportTests
{
    private const string InternalFingerprint = "a1b2c3d4e5";
    private const string ProxyFingerprint = "f6e7d8c9b0";
    private const string SecretInterfaceId =
        "A1B2C3D4-E5F6-47A8-9123-1234567890AB";

#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Run()
    {
        RoutePathComparisonResult comparison = CreateComparison();
        RoutePathComparisonReportDocument document =
            RoutePathComparisonReportWriter.CreateDocument(
                comparison,
                "0.1.0-test",
                DateTimeOffset.UnixEpoch.AddHours(9));

        VerifyDocument(document);
        VerifyJson(document);
        VerifyCsv(document);
        VerifyHtml(document);
        VerifyMalformedFingerprintIsDropped();
        VerifyLocalFiles(document);
        Console.WriteLine("PASS route comparison JSON CSV HTML and SHA-256 report tests");
    }

    private static void VerifyDocument(
        RoutePathComparisonReportDocument document)
    {
        Ensure(document.Status == "Diverged",
            "비교 상태를 구조화해야 합니다.");
        Ensure(document.InternalDirect?.InterfaceFingerprint
               == InternalFingerprint,
            "내부 경로 짧은 지문을 유지해야 합니다.");
        Ensure(document.ProxyEndpoint?.InterfaceFingerprint
               == ProxyFingerprint,
            "프록시 경로 짧은 지문을 유지해야 합니다.");
        Ensure(document.Findings.Count == 3,
            "합성 비교의 고정 판정 세 건이 필요합니다.");
    }

    private static void VerifyJson(
        RoutePathComparisonReportDocument document)
    {
        string json = RoutePathComparisonReportWriter.RenderJson(document);
        using JsonDocument parsed = JsonDocument.Parse(json);
        JsonElement root = parsed.RootElement;

        Ensure(root.GetProperty("status").GetString() == "Diverged",
            "JSON에 전체 비교 상태가 필요합니다.");
        Ensure(root.GetProperty("internalDirect")
                .GetProperty("interfaceFingerprint")
                .GetString() == InternalFingerprint,
            "JSON에 내부 경로 지문이 필요합니다.");
        Ensure(root.GetProperty("proxyEndpoint")
                .GetProperty("isVpn")
                .GetBoolean(),
            "JSON에 프록시 VPN 여부가 필요합니다.");
        Ensure(root.GetProperty("findings").GetArrayLength() == 3,
            "JSON에 구조화 Finding이 필요합니다.");
        AssertSecretsAbsent(json, "JSON");
    }

    private static void VerifyCsv(
        RoutePathComparisonReportDocument document)
    {
        string csv = RoutePathComparisonReportWriter.RenderCsv(document);

        Ensure(csv.StartsWith("section,key,value", StringComparison.Ordinal),
            "CSV는 section,key,value 스키마를 사용해야 합니다.");
        Ensure(csv.Contains(
                "\"comparison\",\"status\",\"Diverged\"",
                StringComparison.Ordinal),
            "CSV에 비교 상태 행이 필요합니다.");
        Ensure(csv.Contains(
                "\"comparison.internalDirect\",\"interfaceFingerprint\",\"a1b2c3d4e5\"",
                StringComparison.Ordinal),
            "CSV에 내부 지문 행이 필요합니다.");
        Ensure(csv.Contains(
                "\"comparison.proxyEndpoint\",\"isVpn\",\"True\"",
                StringComparison.Ordinal),
            "CSV에 프록시 VPN 상태 행이 필요합니다.");
        Ensure(csv.Contains(
                "\"finding.1\",\"code\"",
                StringComparison.Ordinal),
            "CSV에 Finding 구조가 필요합니다.");
        AssertSecretsAbsent(csv, "CSV");
    }

    private static void VerifyHtml(
        RoutePathComparisonReportDocument document)
    {
        string html = RoutePathComparisonReportWriter.RenderHtml(document);

        Ensure(html.StartsWith("<!doctype html>", StringComparison.OrdinalIgnoreCase),
            "HTML5 doctype이 필요합니다.");
        Ensure(html.Contains("Content-Security-Policy", StringComparison.Ordinal),
            "HTML에 CSP가 필요합니다.");
        Ensure(html.Contains("내부·프록시 로컬 경로 비교 보고서", StringComparison.Ordinal),
            "HTML에 보고서 제목이 필요합니다.");
        Ensure(html.Contains("Diverged", StringComparison.Ordinal),
            "HTML에 비교 상태가 필요합니다.");
        Ensure(html.Contains("프록시 엔드포인트", StringComparison.Ordinal),
            "HTML에 프록시 Point가 필요합니다.");
        Ensure(!html.Contains("<script", StringComparison.OrdinalIgnoreCase),
            "HTML에 script를 포함하면 안 됩니다.");
        Ensure(!html.Contains("<iframe", StringComparison.OrdinalIgnoreCase),
            "HTML에 iframe을 포함하면 안 됩니다.");
        Ensure(!html.Contains("<link", StringComparison.OrdinalIgnoreCase),
            "HTML에 외부 스타일시트 링크를 포함하면 안 됩니다.");
        AssertSecretsAbsent(html, "HTML");
    }

    private static void VerifyMalformedFingerprintIsDropped()
    {
        RoutePathComparisonResult comparison = CreateComparison() with
        {
            InternalDirect = CreateComparison().InternalDirect! with
            {
                InterfaceFingerprint = SecretInterfaceId
            }
        };
        RoutePathComparisonReportDocument document =
            RoutePathComparisonReportWriter.CreateDocument(
                comparison,
                "0.1.0-test",
                DateTimeOffset.UnixEpoch);

        Ensure(document.InternalDirect?.InterfaceFingerprint is null,
            "10자리 hex가 아닌 전체 GUID는 지문 필드에서 제거해야 합니다.");
        AssertSecretsAbsent(
            RoutePathComparisonReportWriter.RenderJson(document),
            "malformed fingerprint JSON");
    }

    private static void VerifyLocalFiles(
        RoutePathComparisonReportDocument document)
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "WlanRouteComparisonReportSmoke",
            Guid.NewGuid().ToString("N"));

        try
        {
            RoutePathComparisonReportExportResult export =
                RoutePathComparisonReportWriter.WriteAll(
                    document,
                    directory,
                    "합성 경로 비교 보고서");

            string[] files =
            [
                export.JsonPath,
                export.CsvPath,
                export.HtmlPath,
                export.Sha256Path
            ];
            Ensure(files.All(File.Exists),
                "경로 비교 보고서 네 파일을 모두 생성해야 합니다.");
            Ensure(export.Sha256.Count == 3,
                "JSON·CSV·HTML 해시 세 개가 필요합니다.");

            foreach ((string fileName, string expectedHash) in export.Sha256)
            {
                string path = Path.Combine(export.OutputDirectory, fileName);
                using FileStream stream = File.OpenRead(path);
                string actualHash = Convert.ToHexString(
                        SHA256.HashData(stream))
                    .ToLowerInvariant();
                Ensure(actualHash == expectedHash,
                    $"SHA-256이 일치하지 않습니다: {fileName}");
                AssertSecretsAbsent(File.ReadAllText(path), fileName);
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

    private static RoutePathComparisonResult CreateComparison() =>
        new(
            EvaluatedAt: DateTimeOffset.UnixEpoch,
            Status: RoutePathComparisonStatus.Diverged,
            InternalDirect: new RoutePathComparisonPoint(
                Purpose: RouteProbePurpose.InternalDirectTarget,
                CapturedAt: DateTimeOffset.UnixEpoch.AddSeconds(1),
                RouteStatus: DestinationRouteEvidenceStatus.Success,
                WlanCorrelationStatus: RouteWlanCorrelationStatus.Matched,
                InterfaceFingerprint: InternalFingerprint,
                InterfaceCategory: "Wireless",
                IsVpn: false,
                IsVirtual: false,
                WarningCount: 0),
            ProxyEndpoint: new RoutePathComparisonPoint(
                Purpose: RouteProbePurpose.ProxyEndpoint,
                CapturedAt: DateTimeOffset.UnixEpoch.AddSeconds(2),
                RouteStatus: DestinationRouteEvidenceStatus.Success,
                WlanCorrelationStatus:
                    RouteWlanCorrelationStatus.DifferentInterface,
                InterfaceFingerprint: ProxyFingerprint,
                InterfaceCategory: "Tunnel",
                IsVpn: true,
                IsVirtual: true,
                WarningCount: 2),
            ExternalReference: null,
            Findings:
            [
                Finding(
                    "INTERNAL_MATCHES_CONNECTED_WLAN",
                    RoutePathComparisonSeverity.Information,
                    "내부 DIRECT가 현재 WLAN과 일치"),
                Finding(
                    "PROXY_DIFFERS_FROM_CONNECTED_WLAN",
                    RoutePathComparisonSeverity.Warning,
                    "프록시 엔드포인트가 현재 WLAN과 다름"),
                Finding(
                    "INTERNAL_AND_PROXY_USE_DIFFERENT_INTERFACES",
                    RoutePathComparisonSeverity.Warning,
                    "내부·프록시 로컬 인터페이스 분리")
            ],
            Message: "내부 경로와 프록시 경로 사이에 인터페이스 차이가 확인됐습니다.");

    private static RoutePathComparisonFinding Finding(
        string code,
        RoutePathComparisonSeverity severity,
        string title) =>
        new(
            Code: code,
            Severity: severity,
            Title: title,
            Evidence: "합성 근거",
            Interpretation: "합성 해석",
            NextStep: "합성 다음 확인");

    private static void AssertSecretsAbsent(
        string content,
        string format)
    {
        string[] secrets =
        [
            SecretInterfaceId,
            "10.20.30.40",
            "192.168.1.1",
            "AA:BB:CC:DD:EE:FF",
            "Company Wi-Fi",
            "proxy.corp.example"
        ];

        foreach (string secret in secrets)
        {
            Ensure(!content.Contains(
                    secret,
                    StringComparison.OrdinalIgnoreCase),
                $"{format}에 경로 비교 민감값이 남았습니다: {secret}");
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
