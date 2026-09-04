using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using WlanLivePathTester.Core.NetworkEnvironment;
using WlanLivePathTester.Core.Reporting;
using WlanLivePathTester.Core.Routing;

namespace WlanLivePathTester.ReportSmoke;

internal static class RouteEvidenceReportTests
{
    private const string SecretInterfaceId =
        "A1B2C3D4-E5F6-47A8-9123-1234567890AB";
    private const string SecretInterfaceName =
        "Company Wi-Fi 10.20.30.40";
    private const string SecretDescription =
        "Adapter AA:BB:CC:DD:EE:FF user@example.invalid";

#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Run()
    {
        DestinationRouteEvidence evidence = CreateEvidence();
        RouteEvidenceReportDocument document =
            RouteEvidenceReportWriter.CreateDocument(
                [evidence],
                "0.1.0-test",
                DateTimeOffset.UnixEpoch.AddHours(9));

        VerifyDocument(document);
        VerifyJson(document);
        VerifyCsv(document);
        VerifyHtml(document);
        VerifyLocalFiles(document);
        Console.WriteLine("PASS route evidence JSON CSV HTML and SHA-256 report tests");
    }

    private static void VerifyDocument(
        RouteEvidenceReportDocument document)
    {
        Ensure(document.Results.Count == 1,
            "라우팅 근거 한 건을 구조화해야 합니다.");
        RouteEvidenceReportEntry result = document.Results[0];
        Ensure(result.SelectedInterface is not null,
            "선택 인터페이스 구조가 필요합니다.");
        Ensure(result.SelectedInterface!.IdFingerprint.Length
               == RouteInterfaceFingerprint.DisplayLength,
            "전체 GUID 대신 고정 길이 지문을 기록해야 합니다.");
        Ensure(result.AddressEvidence.Count == 2,
            "IPv4·IPv6 주소 계열별 근거가 필요합니다.");
    }

    private static void VerifyJson(
        RouteEvidenceReportDocument document)
    {
        string json = RouteEvidenceReportWriter.RenderJson(document);
        using JsonDocument parsed = JsonDocument.Parse(json);
        JsonElement result = parsed.RootElement
            .GetProperty("results")[0];

        Ensure(result.GetProperty("dnsWasUsed").GetBoolean(),
            "JSON에 DNS 사용 여부를 기록해야 합니다.");
        Ensure(result.GetProperty("resolvedAddressCount").GetInt32() == 2,
            "JSON에 확인 주소 수를 기록해야 합니다.");
        Ensure(result.GetProperty("selectedInterface")
                .GetProperty("category")
                .GetString() == "Wireless",
            "JSON에 익명화된 인터페이스 범주가 필요합니다.");
        AssertSecretsAbsent(json, "JSON");
    }

    private static void VerifyCsv(
        RouteEvidenceReportDocument document)
    {
        string csv = RouteEvidenceReportWriter.RenderCsv(document);

        Ensure(csv.StartsWith("section,key,value", StringComparison.Ordinal),
            "CSV는 section,key,value 스키마를 사용해야 합니다.");
        Ensure(csv.Contains(
                "\"route.1\",\"status\",\"PartialSuccess\"",
                StringComparison.Ordinal),
            "CSV에 라우팅 집계 상태가 필요합니다.");
        Ensure(csv.Contains(
                "\"route.1.selectedInterface\",\"category\",\"Wireless\"",
                StringComparison.Ordinal),
            "CSV에 선택 인터페이스 범주가 필요합니다.");
        Ensure(csv.Contains(
                "\"route.1.address.1\",\"addressFamily\",\"IPv4\"",
                StringComparison.Ordinal),
            "CSV에 주소 계열별 구조가 필요합니다.");
        AssertSecretsAbsent(csv, "CSV");
    }

    private static void VerifyHtml(
        RouteEvidenceReportDocument document)
    {
        string html = RouteEvidenceReportWriter.RenderHtml(document);

        Ensure(html.StartsWith("<!doctype html>", StringComparison.OrdinalIgnoreCase),
            "HTML5 doctype이 필요합니다.");
        Ensure(html.Contains("Content-Security-Policy", StringComparison.Ordinal),
            "HTML에 CSP가 필요합니다.");
        Ensure(html.Contains("목적지별 Windows 라우팅 근거 보고서", StringComparison.Ordinal),
            "HTML에 보고서 제목이 필요합니다.");
        Ensure(html.Contains("주소 계열별 근거", StringComparison.Ordinal),
            "HTML에 IPv4·IPv6 근거 표가 필요합니다.");
        Ensure(!html.Contains("<script", StringComparison.OrdinalIgnoreCase),
            "HTML에 script를 포함하면 안 됩니다.");
        Ensure(!html.Contains("<iframe", StringComparison.OrdinalIgnoreCase),
            "HTML에 iframe을 포함하면 안 됩니다.");
        Ensure(!html.Contains("<link", StringComparison.OrdinalIgnoreCase),
            "HTML에 외부 스타일시트 링크를 포함하면 안 됩니다.");
        AssertSecretsAbsent(html, "HTML");
    }

    private static void VerifyLocalFiles(
        RouteEvidenceReportDocument document)
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "WlanRouteEvidenceReportSmoke",
            Guid.NewGuid().ToString("N"));

        try
        {
            RouteEvidenceReportExportResult export =
                RouteEvidenceReportWriter.WriteAll(
                    document,
                    directory,
                    "합성 라우팅 보고서");

            string[] files =
            [
                export.JsonPath,
                export.CsvPath,
                export.HtmlPath,
                export.Sha256Path
            ];
            Ensure(files.All(File.Exists),
                "라우팅 보고서 네 파일을 모두 생성해야 합니다.");
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

    private static DestinationRouteEvidence CreateEvidence()
    {
        RouteInterfaceDescriptor routeInterface = new(
            InterfaceIdentity: SecretInterfaceId,
            DisplayName: SecretInterfaceName,
            Description: SecretDescription,
            NativeInterfaceType: "Wireless80211",
            Category: NetworkAdapterCategory.Wireless,
            OperationalState: NetworkAdapterOperationalState.Up,
            HasDefaultGateway: true,
            IsVirtual: false,
            IsVpn: false);

        return new DestinationRouteEvidence(
            CapturedAt: DateTimeOffset.UnixEpoch,
            TargetLabel: "내부 DIRECT 측정 대상",
            Purpose: RouteProbePurpose.InternalDirectTarget,
            DnsWasUsed: true,
            ResolvedAddressCount: 2,
            Status: DestinationRouteEvidenceStatus.PartialSuccess,
            SelectedInterface: routeInterface,
            AddressEvidence:
            [
                new RouteAddressEvidence(
                    AddressFamily: RouteAddressFamilyKind.IPv4,
                    Status: RouteAddressEvidenceStatus.Success,
                    Interface: routeInterface,
                    NativeErrorCode: null,
                    Message: "Windows 최적 인터페이스 확인 성공"),
                new RouteAddressEvidence(
                    AddressFamily: RouteAddressFamilyKind.IPv6,
                    Status: RouteAddressEvidenceStatus.RouteNotFound,
                    Interface: null,
                    NativeErrorCode: 123,
                    Message: "합성 IPv6 경로 없음")
            ],
            Warnings:
            [
                "일부 주소의 경로는 확인하지 못했습니다."
            ],
            Message: "성공한 주소는 같은 물리 Wi-Fi를 선택합니다.");
    }

    private static void AssertSecretsAbsent(
        string content,
        string format)
    {
        string[] secrets =
        [
            SecretInterfaceId,
            SecretInterfaceName,
            SecretDescription,
            "10.20.30.40",
            "AA:BB:CC:DD:EE:FF",
            "user@example.invalid"
        ];

        foreach (string secret in secrets)
        {
            Ensure(!content.Contains(
                    secret,
                    StringComparison.OrdinalIgnoreCase),
                $"{format}에 라우팅 식별정보가 남았습니다: {secret}");
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
