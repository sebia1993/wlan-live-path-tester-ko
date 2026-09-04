using System.Net;
using System.Runtime.CompilerServices;
using WlanLivePathTester.Core.NetworkEnvironment;
using WlanLivePathTester.Core.Routing;
using WlanLivePathTester.Windows.Routing;

namespace WlanLivePathTester.WindowsSmoke;

internal static class RouteEvidenceWindowsSmoke
{
#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Run()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        VerifyTargetParsing();
        VerifyLoopbackBestInterface();
        VerifyLiteralReadDoesNotUseDns();
        Console.WriteLine("PASS Windows local route evidence smoke tests");
    }

    private static void VerifyTargetParsing()
    {
        Ensure(LocalRouteEvidenceReader.TryExtractHost(
                "https://example.invalid:8443/path/file.bin?x=1",
                out string uriHost,
                out _),
            "HTTP URL에서 호스트를 추출해야 합니다.");
        Ensure(uriHost == "example.invalid",
            "URL 호스트 추출 결과가 잘못됐습니다.");

        Ensure(LocalRouteEvidenceReader.TryExtractHost(
                "proxy.example.invalid:8080",
                out string endpointHost,
                out _),
            "host:port 형식에서 프록시 호스트를 추출해야 합니다.");
        Ensure(endpointHost == "proxy.example.invalid",
            "프록시 엔드포인트 호스트 추출 결과가 잘못됐습니다.");

        Ensure(!LocalRouteEvidenceReader.TryExtractHost(
                "ftp://example.invalid/file.bin",
                out _,
                out _),
            "지원하지 않는 URL 스킴은 거부해야 합니다.");
        Ensure(!LocalRouteEvidenceReader.TryExtractHost(
                "https://user:secret@example.invalid/file.bin",
                out _,
                out _),
            "사용자 정보가 포함된 URL은 거부해야 합니다.");
    }

    private static void VerifyLoopbackBestInterface()
    {
        WindowsBestInterfaceResult result =
            WindowsBestInterfaceResolver.Resolve(IPAddress.Loopback);

        Ensure(result.Status == RouteAddressEvidenceStatus.Success,
            $"IPv4 loopback 최적 인터페이스 확인이 실패했습니다: {result.Status} {result.Message}");
        Ensure(result.Interface is not null,
            "성공한 loopback 경로에는 인터페이스 정보가 필요합니다.");
        Ensure(result.Interface.Category
               == NetworkAdapterCategory.Loopback,
            "127.0.0.1의 Windows 최적 인터페이스는 Loopback이어야 합니다.");
        Ensure(result.Interface.IdentityFingerprint.Length
               == RouteInterfaceFingerprint.DisplayLength,
            "로컬 인터페이스 ID 지문 길이가 잘못됐습니다.");
    }

    private static void VerifyLiteralReadDoesNotUseDns()
    {
        DestinationRouteEvidence result =
            LocalRouteEvidenceReader.ReadAsync(
                    "127.0.0.1",
                    "합성 loopback",
                    RouteProbePurpose.ManualDestination,
                    dnsTimeoutSeconds: 2)
                .GetAwaiter()
                .GetResult();

        Ensure(result.IsSuccess,
            $"IP 리터럴 라우팅 근거 수집이 실패했습니다: {result.Status} {result.Message}");
        Ensure(!result.DnsWasUsed,
            "IP 리터럴 경로 확인은 DNS를 사용하면 안 됩니다.");
        Ensure(result.ResolvedAddressCount == 1,
            "IP 리터럴은 주소 한 개만 확인해야 합니다.");
        Ensure(result.SelectedInterface?.Category
               == NetworkAdapterCategory.Loopback,
            "IP 리터럴 수집 결과가 loopback 인터페이스여야 합니다.");
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
