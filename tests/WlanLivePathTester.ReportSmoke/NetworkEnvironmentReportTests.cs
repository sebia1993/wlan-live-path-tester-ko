using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using WlanLivePathTester.Core.NetworkEnvironment;
using WlanLivePathTester.Core.Reporting;

namespace WlanLivePathTester.ReportSmoke;

internal static class NetworkEnvironmentReportTests
{
    private const string SecretInterfaceId =
        "A1B2C3D4-E5F6-47A8-9123-1234567890AB";

#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Run()
    {
        LocalNetworkEnvironmentSnapshot snapshot = CreateSnapshot();
        NetworkEnvironmentReportDocument document =
            NetworkEnvironmentReportWriter.CreateDocument(
                snapshot,
                "0.1.0-test",
                DateTimeOffset.UnixEpoch.AddHours(9));

        VerifyDocument(document);
        VerifyJson(document);
        VerifyCsv(document);
        VerifyHtml(document);
        VerifyLocalFiles(document);
        Console.WriteLine("PASS network environment JSON CSV HTML SHA-256 report tests");
    }

    private static void VerifyDocument(
        NetworkEnvironmentReportDocument document)
    {
        Ensure(document.Adapters.Count == 3,
            "익명화된 어댑터 세 개를 기록해야 합니다.");
        Ensure(document.Adapters.All(adapter => adapter.Index > 0),
            "어댑터는 익명 순번으로만 식별해야 합니다.");
        Ensure(document.Summary.ActiveWirelessCount == 1,
            "활성 Wi-Fi 개수를 구조화해야 합니다.");
        Ensure(document.Summary.ActiveVpnCount == 1,
            "활성 VPN 개수를 구조화해야 합니다.");
        Ensure(document.Summary.RouteSelectionMayBeAmbiguous,
            "다중 기본 경로 혼재 가능성을 기록해야 합니다.");
    }

    private static void VerifyJson(
        NetworkEnvironmentReportDocument document)
    {
        string json = NetworkEnvironmentReportWriter.RenderJson(document);
        using JsonDocument parsed = JsonDocument.Parse(json);
        JsonElement root = parsed.RootElement;

        Ensure(root.GetProperty("schemaVersion").GetString() == "1.0",
            "JSON 스키마 버전이 필요합니다.");
        Ensure(root.GetProperty("adapters").GetArrayLength() == 3,
            "JSON에 익명화된 어댑터 세 개가 필요합니다.");
        Ensure(root.GetProperty("summary")
                .GetProperty("activeDefaultGatewayCount")
                .GetInt32() == 2,
            "JSON에 기본 게이트웨이 보유 인터페이스 개수가 필요합니다.");
        AssertSecretsAbsent(json, "JSON");
    }

    private static void VerifyCsv(
        NetworkEnvironmentReportDocument document)
    {
        string csv = NetworkEnvironmentReportWriter.RenderCsv(document);

        Ensure(csv.StartsWith("section,key,value", StringComparison.Ordinal),
            "CSV는 section,key,value 스키마를 사용해야 합니다.");
        Ensure(csv.Contains(
                "\"summary\",\"activeVpnCount\",\"1\"",
                StringComparison.Ordinal),
            "CSV에 활성 VPN 개수 행이 필요합니다.");
        Ensure(csv.Contains(
                "\"adapter.1\",\"category\"",
                StringComparison.Ordinal),
            "CSV에 익명 어댑터 구조가 필요합니다.");
        Ensure(!csv.Contains("displayName", StringComparison.OrdinalIgnoreCase),
            "CSV에 인터페이스 이름 필드를 만들면 안 됩니다.");
        Ensure(!csv.Contains("description", StringComparison.OrdinalIgnoreCase),
            "CSV에 인터페이스 설명 필드를 만들면 안 됩니다.");
        Ensure(!csv.Contains("interfaceId", StringComparison.OrdinalIgnoreCase),
            "CSV에 인터페이스 ID 필드를 만들면 안 됩니다.");
        AssertSecretsAbsent(csv, "CSV");
    }

    private static void VerifyHtml(
        NetworkEnvironmentReportDocument document)
    {
        string html = NetworkEnvironmentReportWriter.RenderHtml(document);

        Ensure(html.StartsWith("<!doctype html>", StringComparison.OrdinalIgnoreCase),
            "HTML5 doctype이 필요합니다.");
        Ensure(html.Contains("Content-Security-Policy", StringComparison.Ordinal),
            "HTML에 Content Security Policy가 필요합니다.");
        Ensure(html.Contains("익명화된 인터페이스 목록", StringComparison.Ordinal),
            "HTML에 익명 인터페이스 표가 필요합니다.");
        Ensure(html.Contains("경로 혼재 가능성", StringComparison.Ordinal),
            "HTML에 경로 혼재 판정이 필요합니다.");
        Ensure(!html.Contains("<script", StringComparison.OrdinalIgnoreCase),
            "HTML에 script를 포함하면 안 됩니다.");
        Ensure(!html.Contains("<iframe", StringComparison.OrdinalIgnoreCase),
            "HTML에 iframe을 포함하면 안 됩니다.");
        Ensure(!html.Contains("<link", StringComparison.OrdinalIgnoreCase),
            "HTML에 외부 스타일시트 링크를 포함하면 안 됩니다.");
        AssertSecretsAbsent(html, "HTML");
    }

    private static void VerifyLocalFiles(
        NetworkEnvironmentReportDocument document)
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "WlanNetworkEnvironmentReportSmoke",
            Guid.NewGuid().ToString("N"));

        try
        {
            NetworkEnvironmentReportExportResult export =
                NetworkEnvironmentReportWriter.WriteAll(
                    document,
                    directory,
                    "인터페이스 합성 보고서");

            string[] paths =
            [
                export.JsonPath,
                export.CsvPath,
                export.HtmlPath,
                export.Sha256Path
            ];
            Ensure(paths.All(File.Exists),
                "JSON CSV HTML SHA-256 파일을 모두 생성해야 합니다.");
            Ensure(export.Sha256.Count == 3,
                "JSON CSV HTML 해시 세 개가 필요합니다.");

            foreach ((string fileName, string expectedHash) in export.Sha256)
            {
                string path = Path.Combine(export.OutputDirectory, fileName);
                using FileStream stream = File.OpenRead(path);
                string actualHash = Convert.ToHexString(SHA256.HashData(stream))
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

    private static LocalNetworkEnvironmentSnapshot CreateSnapshot()
    {
        LocalNetworkAdapterSnapshot[] adapters =
        [
            Adapter(
                displayName: "Wi-Fi user@example.invalid 10.20.30.40",
                description: "Intel AA:BB:CC:DD:EE:FF https://corp.example.invalid/secret",
                nativeType: "Wireless80211",
                category: NetworkAdapterCategory.Wireless,
                gateway: true,
                isVirtual: false,
                isVpn: false,
                interfaceId: SecretInterfaceId),
            Adapter(
                displayName: "Company VPN C:\\Users\\alice",
                description: "AnyConnect 172.16.1.20",
                nativeType: "Tunnel",
                category: NetworkAdapterCategory.Tunnel,
                gateway: true,
                isVirtual: true,
                isVpn: true,
                interfaceId:
                    "B1B2C3D4-E5F6-47A8-9123-1234567890AB"),
            Adapter(
                displayName: "vEthernet (Default Switch)",
                description: "Hyper-V Virtual Ethernet Adapter",
                nativeType: "Ethernet",
                category: NetworkAdapterCategory.Ethernet,
                gateway: false,
                isVirtual: true,
                isVpn: false,
                interfaceId:
                    "C1B2C3D4-E5F6-47A8-9123-1234567890AB")
        ];
        NetworkEnvironmentAssessment assessment =
            NetworkEnvironmentEvaluator.Evaluate(adapters);

        return new LocalNetworkEnvironmentSnapshot(
            CapturedAt: DateTimeOffset.UnixEpoch.AddHours(9),
            Adapters: adapters,
            Assessment: assessment,
            Message: "합성 로컬 인터페이스 환경");
    }

    private static LocalNetworkAdapterSnapshot Adapter(
        string displayName,
        string description,
        string nativeType,
        NetworkAdapterCategory category,
        bool gateway,
        bool isVirtual,
        bool isVpn,
        string interfaceId) =>
        new(
            DisplayName: displayName,
            Description: description,
            NativeInterfaceType: nativeType,
            Category: category,
            OperationalState: NetworkAdapterOperationalState.Up,
            SpeedBitsPerSecond: 1_000_000_000,
            HasDefaultGateway: gateway,
            GatewayCount: gateway ? 1 : 0,
            HasIpv4: true,
            HasIpv6: true,
            UnicastAddressCount: 2,
            SupportsMulticast: true,
            IsVirtual: isVirtual,
            IsVpn: isVpn,
            ReadError: null,
            InterfaceId: interfaceId);

    private static void AssertSecretsAbsent(
        string content,
        string format)
    {
        string[] secrets =
        [
            "user@example.invalid",
            "10.20.30.40",
            "AA:BB:CC:DD:EE:FF",
            "corp.example.invalid",
            "172.16.1.20",
            "C:\\Users\\alice",
            "Company VPN",
            "vEthernet (Default Switch)",
            SecretInterfaceId,
            "B1B2C3D4-E5F6-47A8-9123-1234567890AB",
            "C1B2C3D4-E5F6-47A8-9123-1234567890AB"
        ];

        foreach (string secret in secrets)
        {
            Ensure(!content.Contains(secret, StringComparison.OrdinalIgnoreCase),
                $"{format}에 인터페이스 식별정보가 남았습니다: {secret}");
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
