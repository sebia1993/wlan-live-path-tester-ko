using System.Runtime.CompilerServices;
using WlanLivePathTester.Core.Proxy;

namespace WlanLivePathTester.SelfTest;

internal static class ProxyEndpointParserTests
{
#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Run()
    {
        ParsesSingleManualProxy();
        SelectsSchemeSpecificProxy();
        UsesHttpMappingAsHttpsFallback();
        ParsesProxyListWithDirectFallback();
        ParsesPrefixedPacTokens();
        ParsesBracketedIpv6();
        DeduplicatesCandidates();
        RejectsCredentialsAndPaths();
        RejectsInvalidPortsAndTooManyCandidates();
        ProducesStableHostFingerprint();
        Console.WriteLine("PASS strict effective proxy endpoint parser tests");
    }

    private static void ParsesSingleManualProxy()
    {
        EffectiveProxyParseResult result = ProxyEndpointParser.Parse(
            "proxy.corp.example:8080",
            "https");

        Ensure(result.IsValid,
            "단일 수동 프록시를 파싱해야 합니다.");
        Ensure(result.Decision == EffectiveProxyDecisionKind.Proxy,
            "단일 프록시는 Proxy 결정이어야 합니다.");
        Ensure(result.Endpoints.Count == 1,
            "프록시 후보 한 개가 필요합니다.");
        Ensure(result.Endpoints[0].Host == "proxy.corp.example"
               && result.Endpoints[0].Port == 8080,
            "수동 프록시 호스트와 포트를 정확히 파싱해야 합니다.");
        Ensure(result.Endpoints[0].Transport
               == ProxyEndpointTransport.Http,
            "스킴 없는 일반 WinHTTP 프록시는 HTTP transport여야 합니다.");
    }

    private static void SelectsSchemeSpecificProxy()
    {
        EffectiveProxyParseResult result = ProxyEndpointParser.Parse(
            "http=proxy-http.example:8080;https=proxy-tls.example:8443",
            "https");

        Ensure(result.IsValid,
            "스킴별 수동 프록시를 파싱해야 합니다.");
        Ensure(result.Endpoints.Count == 1,
            "https 대상에는 https 매핑 하나만 선택해야 합니다.");
        Ensure(result.Endpoints[0].Host == "proxy-tls.example"
               && result.Endpoints[0].Port == 8443,
            "https 매핑을 우선 선택해야 합니다.");
    }

    private static void UsesHttpMappingAsHttpsFallback()
    {
        EffectiveProxyParseResult result = ProxyEndpointParser.Parse(
            "http=shared-proxy.example:3128",
            "https");

        Ensure(result.IsValid && result.Endpoints.Count == 1,
            "https 전용 매핑이 없으면 http 매핑을 공통 프록시로 사용할 수 있어야 합니다.");
        Ensure(result.Endpoints[0].Host == "shared-proxy.example",
            "https fallback 프록시 호스트가 잘못됐습니다.");
    }

    private static void ParsesProxyListWithDirectFallback()
    {
        EffectiveProxyParseResult result = ProxyEndpointParser.Parse(
            "proxy-a.example:8080;proxy-b.example:8080;DIRECT",
            "https");

        Ensure(result.IsValid,
            "프록시 목록과 DIRECT fallback을 파싱해야 합니다.");
        Ensure(result.Decision
               == EffectiveProxyDecisionKind.ProxyWithDirectFallback,
            "프록시+DIRECT는 ProxyWithDirectFallback이어야 합니다.");
        Ensure(result.Endpoints.Count == 2 && result.HasDirectFallback,
            "프록시 두 개와 DIRECT fallback이 필요합니다.");
    }

    private static void ParsesPrefixedPacTokens()
    {
        EffectiveProxyParseResult result = ProxyEndpointParser.Parse(
            "PROXY proxy-a.example:8080; HTTPS proxy-b.example:443; SOCKS5 socks.example:1080; DIRECT",
            "https");

        Ensure(result.IsValid && result.Endpoints.Count == 3,
            "PAC 스타일 transport prefix를 파싱해야 합니다.");
        Ensure(result.Endpoints[0].Transport
               == ProxyEndpointTransport.Http,
            "PROXY prefix는 HTTP 프록시여야 합니다.");
        Ensure(result.Endpoints[1].Transport
               == ProxyEndpointTransport.Https,
            "HTTPS prefix를 구분해야 합니다.");
        Ensure(result.Endpoints[2].Transport
               == ProxyEndpointTransport.Socks,
            "SOCKS5 prefix를 Socks로 구분해야 합니다.");
    }

    private static void ParsesBracketedIpv6()
    {
        EffectiveProxyParseResult result = ProxyEndpointParser.Parse(
            "HTTPS [2001:db8::10]:8443",
            "https");

        Ensure(result.IsValid && result.Endpoints.Count == 1,
            "대괄호 IPv6 프록시를 파싱해야 합니다.");
        Ensure(result.Endpoints[0].Host == "2001:db8::10"
               && result.Endpoints[0].Port == 8443,
            "IPv6 호스트와 포트를 정확히 분리해야 합니다.");
    }

    private static void DeduplicatesCandidates()
    {
        EffectiveProxyParseResult result = ProxyEndpointParser.Parse(
            "proxy.example:8080;PROXY PROXY.EXAMPLE.:8080;DIRECT",
            "http");

        Ensure(result.IsValid,
            "중복 후보가 있어도 전체 결정은 유효해야 합니다.");
        Ensure(result.Endpoints.Count == 1,
            "정규화한 같은 프록시 후보를 중복 저장하면 안 됩니다.");
        Ensure(result.Warnings.Any(warning => warning.Contains(
                "중복",
                StringComparison.Ordinal)),
            "중복 제거 경고가 필요합니다.");
    }

    private static void RejectsCredentialsAndPaths()
    {
        string[] invalidValues =
        [
            "http://user:secret@proxy.example:8080",
            "http://proxy.example:8080/path",
            "proxy.example:8080/path",
            "proxy.example:8080?x=1",
            "proxy.example:8080#fragment"
        ];

        foreach (string invalid in invalidValues)
        {
            EffectiveProxyParseResult result =
                ProxyEndpointParser.Parse(invalid, "https");
            Ensure(!result.IsValid,
                $"자격 증명·경로·쿼리·fragment 프록시는 거부해야 합니다: {invalid}");
            Ensure(result.Endpoints.Count == 0,
                "잘못된 프록시 후보를 부분 적용하면 안 됩니다.");
        }
    }

    private static void RejectsInvalidPortsAndTooManyCandidates()
    {
        EffectiveProxyParseResult invalidPort =
            ProxyEndpointParser.Parse(
                "proxy.example:70000",
                "https");
        Ensure(!invalidPort.IsValid,
            "65535를 넘는 프록시 포트를 거부해야 합니다.");

        string tooMany = string.Join(
            ';',
            Enumerable.Range(1, ProxyEndpointParser.MaximumEndpointCount + 1)
                .Select(index => $"proxy-{index}.example:8080"));
        EffectiveProxyParseResult overflow =
            ProxyEndpointParser.Parse(tooMany, "https");
        Ensure(!overflow.IsValid,
            "허용 개수를 넘는 프록시 후보를 거부해야 합니다.");
        Ensure(overflow.Endpoints.Count
               == ProxyEndpointParser.MaximumEndpointCount,
            "오류 전까지도 최대 허용 후보 수를 넘기면 안 됩니다.");
    }

    private static void ProducesStableHostFingerprint()
    {
        string first = ProxyEndpointFingerprint.Create(
            "Proxy.Corp.Example.");
        string second = ProxyEndpointFingerprint.Create(
            "proxy.corp.example");

        Ensure(first == second,
            "대소문자와 끝 점이 다른 같은 호스트는 같은 지문이어야 합니다.");
        Ensure(first.Length == ProxyEndpointFingerprint.DisplayLength,
            "프록시 호스트 지문은 고정 길이여야 합니다.");
        Ensure(!first.Contains("proxy", StringComparison.OrdinalIgnoreCase),
            "프록시 호스트 지문에 원문이 포함되면 안 됩니다.");
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
