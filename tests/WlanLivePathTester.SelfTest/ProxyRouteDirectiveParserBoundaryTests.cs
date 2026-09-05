using System.Runtime.CompilerServices;
using System.Text.Json;
using WlanLivePathTester.Core.Proxy;

namespace WlanLivePathTester.SelfTest;

internal static class ProxyRouteDirectiveParserBoundaryTests
{
#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Run()
    {
        PreservesScopedDirectAndFallbackOrder();
        RejectsAmbiguousWhitespaceSeparatedFallbacks();
        RejectsUnsafeHostSyntaxWithoutReflection();
        RejectsMixedAbsoluteUriCredentialsAndFragments();
        TreatsEmptySegmentsAsWarningsOnly();
        KeepsRawHostsOutOfDefaultSerialization();
        Console.WriteLine(
            "PASS proxy route directive parser boundary tests");
    }

    private static void PreservesScopedDirectAndFallbackOrder()
    {
        ProxyDirectiveParseResult result =
            ProxyRouteDirectiveParser.Parse(
                "https=proxy-a.example.invalid:8080;ftp=DIRECT;SOCKS5 [2001:db8::10]:1080;DIRECT");

        Ensure(result.Status == ProxyDirectiveParseStatus.Success,
            "유효한 scoped DIRECT와 fallback 목록은 성공해야 합니다.");
        Ensure(result.Directives.Count == 4,
            "네 지시문을 모두 유지해야 합니다.");
        Ensure(result.Directives.Select(item => item.Sequence)
                .SequenceEqual([1, 2, 3, 4]),
            "원본 세그먼트 순서를 유지해야 합니다.");
        Ensure(result.Directives[1].IsDirect
               && result.Directives[1].Scope == "ftp",
            "ftp=DIRECT의 범위를 보존해야 합니다.");
        Ensure(result.Directives[3].IsDirect
               && result.Directives[3].Scope == "all",
            "마지막 전역 DIRECT fallback을 보존해야 합니다.");
    }

    private static void RejectsAmbiguousWhitespaceSeparatedFallbacks()
    {
        const string secret = "hidden-second-proxy.example.invalid";
        ProxyDirectiveParseResult result =
            ProxyRouteDirectiveParser.Parse(
                $"PROXY first.example.invalid:8080 PROXY {secret}:3128 DIRECT");

        Ensure(result.Status
               == ProxyDirectiveParseStatus.InvalidInput,
            "세미콜론 없는 다중 fallback을 하나의 endpoint로 보정하면 안 됩니다.");
        Ensure(result.Directives.Count == 0,
            "모호한 공백 목록에서 일부 프록시를 임의 추출하면 안 됩니다.");
        Ensure(!string.Join("\n", result.Issues.Select(item => item.Message))
                .Contains(secret, StringComparison.OrdinalIgnoreCase),
            "오류 메시지에 숨은 호스트 원문을 반사하면 안 됩니다.");
    }

    private static void RejectsUnsafeHostSyntaxWithoutReflection()
    {
        string[] inputs =
        [
            "PROXY -leading.example.invalid:8080",
            "PROXY trailing-.example.invalid:8080",
            "PROXY wildcard*.example.invalid:8080",
            "PROXY proxy..example.invalid:8080",
            "PROXY proxy.example.invalid.:0",
            "PROXY [2001:db8::1]:65536",
            "PROXY [not-ipv6]:8080"
        ];

        foreach (string input in inputs)
        {
            ProxyDirectiveParseResult result =
                ProxyRouteDirectiveParser.Parse(input);
            Ensure(result.Status
                   == ProxyDirectiveParseStatus.InvalidInput,
                $"안전하지 않은 host 또는 port를 거부해야 합니다: {input}");
            Ensure(result.Directives.Count == 0,
                "거부된 입력에서 사용할 수 있는 endpoint를 만들면 안 됩니다.");
            Ensure(result.Issues.All(issue =>
                    !issue.Message.Contains(
                        input,
                        StringComparison.OrdinalIgnoreCase)),
                "Issue 메시지에 전체 입력을 반사하면 안 됩니다.");
        }
    }

    private static void RejectsMixedAbsoluteUriCredentialsAndFragments()
    {
        const string secretUser = "corp-user";
        const string secretPassword = "super-secret";
        const string secretToken = "private-token";
        string[] inputs =
        [
            $"http://{secretUser}:{secretPassword}@proxy.example.invalid:8080",
            $"https://proxy.example.invalid:8443/#fragment-{secretToken}",
            $"socks5://proxy.example.invalid:1080?token={secretToken}",
            "https=http://proxy.example.invalid:8080/private"
        ];

        foreach (string input in inputs)
        {
            ProxyDirectiveParseResult result =
                ProxyRouteDirectiveParser.Parse(input);
            Ensure(result.Status
                   == ProxyDirectiveParseStatus.InvalidInput,
                "자격증명·fragment·query·path가 있는 URI는 거부해야 합니다.");
            string serialized = JsonSerializer.Serialize(result);
            foreach (string secret in new[]
                     {
                         secretUser,
                         secretPassword,
                         secretToken
                     })
            {
                Ensure(!serialized.Contains(
                        secret,
                        StringComparison.OrdinalIgnoreCase),
                    $"거부 결과 JSON에 비밀값이 남았습니다: {secret}");
            }
        }
    }

    private static void TreatsEmptySegmentsAsWarningsOnly()
    {
        ProxyDirectiveParseResult result =
            ProxyRouteDirectiveParser.Parse(
                ";PROXY proxy.example.invalid:8080;;DIRECT;");

        Ensure(result.Status == ProxyDirectiveParseStatus.Success,
            "빈 세그먼트만 있는 경우 유효 지시문을 부분 실패로 낮추면 안 됩니다.");
        Ensure(result.Directives.Count == 2
               && result.Directives[0].Sequence == 2
               && result.Directives[1].Sequence == 4,
            "실제 원본 세그먼트 번호를 유지해야 합니다.");
        Ensure(result.Issues.Count(issue =>
                issue.Code == "EMPTY_SEGMENT") == 3,
            "앞·중간·뒤 빈 세그먼트를 경고로 기록해야 합니다.");
        Ensure(result.Issues.All(issue =>
                issue.Severity
                    == ProxyDirectiveIssueSeverity.Warning),
            "빈 세그먼트는 오류가 아닌 경고여야 합니다.");
    }

    private static void KeepsRawHostsOutOfDefaultSerialization()
    {
        const string secretHost =
            "serialization-private-proxy.example.invalid";
        ProxyDirectiveParseResult result =
            ProxyRouteDirectiveParser.Parse(
                $"PROXY {secretHost}:8080;DIRECT");
        ProxyRouteDirective proxy = result.Directives[0];
        string json = JsonSerializer.Serialize(result);

        Ensure(proxy.Host == secretHost,
            "후속 사용자 실행 DNS·route 분석을 위해 메모리에는 정규화 호스트가 필요합니다.");
        Ensure(!json.Contains(
                secretHost,
                StringComparison.OrdinalIgnoreCase),
            "기본 JSON에는 원문 프록시 호스트가 없어야 합니다.");
        Ensure(!proxy.ToString().Contains(
                secretHost,
                StringComparison.OrdinalIgnoreCase),
            "기본 표시에도 원문 프록시 호스트가 없어야 합니다.");
        Ensure(json.Contains(
                proxy.HostFingerprint,
                StringComparison.Ordinal),
            "비가역 짧은 지문은 후보 상관분석용으로 유지할 수 있습니다.");
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
