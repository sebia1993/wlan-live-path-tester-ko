using System.Runtime.CompilerServices;
using WlanLivePathTester.Core.Proxy;

namespace WlanLivePathTester.SelfTest;

internal static class ProxyEndpointParserV2Tests
{
#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Run()
    {
        HandlesEmptyAndDirectOnlyInput();
        PreservesAutomaticProxyOrderAndDirectFallback();
        HandlesWhitespaceSeparatedAutomaticResult();
        SelectsOnlyTheExactManualTargetScheme();
        DoesNotGuessCrossSchemeManualFallback();
        SupportsCommonAndExplicitUriEndpoints();
        SupportsIpv4Ipv6AndIdnHosts();
        RejectsCredentialsPathsQueriesAndInvalidPorts();
        DeduplicatesEndpointsAndDirectives();
        PreservesDirectBeforeProxyOrdering();
        ReportsMixedInputAndBoundedLimits();
        ProducesPrivacySafeStableLabels();
        RejectsUnsupportedTargetUris();
        Console.WriteLine(
            "PASS deterministic proxy endpoint parser v2 tests");
    }

    private static void HandlesEmptyAndDirectOnlyInput()
    {
        ProxyEndpointParseResult empty =
            ProxyEndpointParser.Parse(null);
        Ensure(!empty.InputPresent,
            "빈 입력은 InputPresent=false여야 합니다.");
        Ensure(empty.Decision == ProxyEndpointDecision.Unknown,
            "빈 입력에서 경로를 추정하면 안 됩니다.");
        Ensure(!empty.IsUsable
               && empty.Endpoints.Count == 0
               && empty.DirectSequences.Count == 0,
            "빈 입력에는 사용 가능한 경로가 없어야 합니다.");
        Ensure(empty.Warnings.Count == 1
               && empty.Errors.Count == 0,
            "빈 입력은 오류가 아닌 제한 경고여야 합니다.");

        ProxyEndpointParseResult direct =
            ProxyEndpointParser.Parse("DIRECT");
        Ensure(direct.SourceKind
               == ProxyEndpointSourceKind.AutoProxyResult,
            "DIRECT는 자동 프록시 결과 형식으로 분류해야 합니다.");
        Ensure(direct.Decision == ProxyEndpointDecision.Direct,
            "DIRECT 단독 입력은 직접 경로여야 합니다.");
        Ensure(direct.DirectPresent
               && !direct.DirectFallback
               && direct.DirectSequences.SequenceEqual([1]),
            "DIRECT 순서와 기본 경로 의미를 유지해야 합니다.");
        Ensure(direct.IsUsable,
            "DIRECT 단독 입력은 사용 가능한 결정입니다.");
    }

    private static void
        PreservesAutomaticProxyOrderAndDirectFallback()
    {
        ProxyEndpointParseResult result = ProxyEndpointParser.Parse(
            "PROXY proxy-a.example:8080; HTTPS proxy-b.example:8443; SOCKS5 [2001:db8::1]:1080; DIRECT",
            new Uri("https://download.example/file.bin"));

        Ensure(result.SourceKind
               == ProxyEndpointSourceKind.AutoProxyResult,
            "자동 지시문 목록으로 분류해야 합니다.");
        Ensure(result.Decision
               == ProxyEndpointDecision.ProxyWithDirectFallback,
            "프록시 뒤 DIRECT는 직접 경로 fallback이어야 합니다.");
        Ensure(result.DirectFallback
               && result.DirectSequences.SequenceEqual([4]),
            "DIRECT의 실제 순서를 보존해야 합니다.");
        Ensure(result.Endpoints.Count == 3,
            "자동 프록시 후보 세 개를 유지해야 합니다.");
        Ensure(result.Endpoints.Select(item => item.Sequence)
                .SequenceEqual([1, 2, 3]),
            "프록시 후보 입력 순서를 유지해야 합니다.");
        Ensure(result.Endpoints[0].Transport
               == ProxyEndpointTransport.Http
               && result.Endpoints[0].Host == "proxy-a.example"
               && result.Endpoints[0].Port == 8080,
            "PROXY 지시문을 HTTP 프록시로 해석해야 합니다.");
        Ensure(result.Endpoints[1].Transport
               == ProxyEndpointTransport.Https
               && result.Endpoints[1].Port == 8443,
            "HTTPS 프록시 전송 유형과 포트를 유지해야 합니다.");
        Ensure(result.Endpoints[2].Transport
               == ProxyEndpointTransport.Socks5
               && result.Endpoints[2].Host == "2001:db8::1"
               && result.Endpoints[2].Port == 1080,
            "대괄호 IPv6 SOCKS5 엔드포인트를 해석해야 합니다.");
    }

    private static void HandlesWhitespaceSeparatedAutomaticResult()
    {
        ProxyEndpointParseResult result =
            ProxyEndpointParser.Parse(
                "PROXY first.example:8080 HTTPS second.example:8443 DIRECT");

        Ensure(result.Endpoints.Count == 2,
            "공백으로 구분된 두 프록시 지시문을 해석해야 합니다.");
        Ensure(result.Endpoints[0].Sequence == 1
               && result.Endpoints[1].Sequence == 2
               && result.DirectSequences.SequenceEqual([3]),
            "공백 목록에서도 route 순서를 보존해야 합니다.");
        Ensure(result.Decision
               == ProxyEndpointDecision.ProxyWithDirectFallback,
            "공백 목록의 마지막 DIRECT를 fallback으로 유지해야 합니다.");
    }

    private static void SelectsOnlyTheExactManualTargetScheme()
    {
        const string manual =
            "http=proxy-http.example:8080;https=proxy-https.example:8443";
        ProxyEndpointParseResult https = ProxyEndpointParser.Parse(
            manual,
            new Uri("https://download.example/file.bin"));
        ProxyEndpointParseResult http = ProxyEndpointParser.Parse(
            manual,
            new Uri("http://download.example/file.bin"));
        ProxyEndpointParseResult all =
            ProxyEndpointParser.Parse(manual);

        Ensure(https.SourceKind
               == ProxyEndpointSourceKind.ManualServerList,
            "scheme=server 목록은 수동 서버 목록이어야 합니다.");
        Ensure(https.TargetScheme == "https"
               && https.Endpoints.Count == 1
               && https.Endpoints[0].Host
                   == "proxy-https.example"
               && https.Endpoints[0].AppliesToScheme == "https",
            "HTTPS 대상에는 https= 항목만 선택해야 합니다.");
        Ensure(https.IgnoredEndpointCount == 1,
            "HTTP 전용 후보 한 개를 제외했다는 집계가 필요합니다.");
        Ensure(http.Endpoints.Count == 1
               && http.Endpoints[0].Host
                   == "proxy-http.example",
            "HTTP 대상에는 http= 항목만 선택해야 합니다.");
        Ensure(all.Endpoints.Count == 2
               && all.Endpoints.Select(item => item.AppliesToScheme)
                   .SequenceEqual(["http", "https"]),
            "대상이 없으면 모든 수동 매핑과 적용 스킴을 유지해야 합니다.");
    }

    private static void DoesNotGuessCrossSchemeManualFallback()
    {
        ProxyEndpointParseResult result = ProxyEndpointParser.Parse(
            "http=proxy-http.example:8080",
            new Uri("https://download.example/file.bin"));

        Ensure(result.ParsedEndpointCount == 1
               && result.IgnoredEndpointCount == 1,
            "유효하지만 다른 대상 스킴인 항목을 구분해야 합니다.");
        Ensure(result.Endpoints.Count == 0
               && result.Decision == ProxyEndpointDecision.Unknown,
            "HTTPS 대상에 http= 프록시를 임의 fallback하면 안 됩니다.");
        Ensure(!result.IsUsable,
            "적용 가능한 경로가 없으면 사용 가능으로 표시하면 안 됩니다.");
    }

    private static void SupportsCommonAndExplicitUriEndpoints()
    {
        ProxyEndpointParseResult common = ProxyEndpointParser.Parse(
            "all=common.example:3128",
            new Uri("https://download.example/file.bin"));
        ProxyEndpointParseResult explicitUri =
            ProxyEndpointParser.Parse(
                "https=https://secure.example:9443",
                new Uri("https://download.example/file.bin"));
        ProxyEndpointParseResult defaultPorts =
            ProxyEndpointParser.Parse(
                "PROXY http://http-proxy.example; SOCKS5 socks5://socks-proxy.example");

        Ensure(common.Endpoints.Count == 1
               && common.Endpoints[0].AppliesToScheme == "all",
            "all= 후보는 HTTP와 HTTPS 대상에 적용돼야 합니다.");
        Ensure(explicitUri.Endpoints.Count == 1
               && explicitUri.Endpoints[0].Transport
                   == ProxyEndpointTransport.Https
               && explicitUri.Endpoints[0].Port == 9443,
            "명시적 HTTPS proxy URI의 전송 유형과 포트를 유지해야 합니다.");
        Ensure(defaultPorts.Endpoints.Count == 2
               && defaultPorts.Endpoints[0].Port == 80
               && defaultPorts.Endpoints[1].Port == 1080,
            "명시적 URI에만 안전한 스킴 기본 포트를 적용해야 합니다.");

        ProxyEndpointParseResult unspecified =
            ProxyEndpointParser.Parse("bare-proxy.example");
        Ensure(unspecified.Endpoints.Count == 1
               && unspecified.Endpoints[0].Transport
                   == ProxyEndpointTransport.Unspecified
               && unspecified.Endpoints[0].Port is null,
            "스킴과 포트가 없는 값에 전송 유형이나 포트를 추정하면 안 됩니다.");
    }

    private static void SupportsIpv4Ipv6AndIdnHosts()
    {
        ProxyEndpointParseResult addresses =
            ProxyEndpointParser.Parse(
                "PROXY 192.0.2.10:8080; SOCKS [2001:db8::1]:1080; PROXY 2001:db8::2");

        Ensure(addresses.Endpoints.Count == 3,
            "IPv4와 두 IPv6 후보를 모두 해석해야 합니다.");
        Ensure(addresses.Endpoints[0].Host == "192.0.2.10"
               && addresses.Endpoints[0].Port == 8080,
            "IPv4 host:port를 유지해야 합니다.");
        Ensure(addresses.Endpoints[1].Host == "2001:db8::1"
               && addresses.Endpoints[1].Port == 1080,
            "대괄호 IPv6 포트를 유지해야 합니다.");
        Ensure(addresses.Endpoints[2].Host == "2001:db8::2"
               && addresses.Endpoints[2].Port is null,
            "대괄호 없는 유효 IPv6는 포트 없는 literal로 해석해야 합니다.");

        ProxyEndpointParseResult idn = ProxyEndpointParser.Parse(
            "PROXY bücher.example:8080; PROXY xn--bcher-kva.example:8080");
        Ensure(idn.Endpoints.Count == 1
               && idn.Endpoints[0].Host
                   == "xn--bcher-kva.example",
            "IDN과 동일 punycode 호스트를 정규화하고 중복 제거해야 합니다.");
        Ensure(idn.DuplicateEndpointCount == 1,
            "IDN 정규화 후 중복 집계가 필요합니다.");
    }

    private static void
        RejectsCredentialsPathsQueriesAndInvalidPorts()
    {
        const string input =
            "PROXY user:secret@private-proxy.example:8080; "
            + "PROXY http://path-proxy.example/private; "
            + "PROXY https://query-proxy.example:443?token=secret; "
            + "PROXY zero-port.example:0; "
            + "PROXY high-port.example:65536; "
            + "PROXY [2001:db8::1]8080; "
            + "PROXY good.example:8080";
        ProxyEndpointParseResult result =
            ProxyEndpointParser.Parse(input);

        Ensure(result.Endpoints.Count == 1
               && result.Endpoints[0].Host == "good.example",
            "안전한 마지막 프록시 후보만 유지해야 합니다.");
        Ensure(result.RejectedTokenCount == 6,
            "자격 증명·경로·query·잘못된 포트·IPv6 suffix를 거부해야 합니다.");
        string warnings = string.Join("\n", result.Warnings);
        string[] forbidden =
        [
            "user:secret",
            "private-proxy.example",
            "path-proxy.example",
            "query-proxy.example",
            "token=secret"
        ];
        foreach (string secret in forbidden)
        {
            Ensure(!warnings.Contains(
                    secret,
                    StringComparison.OrdinalIgnoreCase),
                $"거부 경고에 원문 프록시 값을 반사하면 안 됩니다: {secret}");
        }
    }

    private static void DeduplicatesEndpointsAndDirectives()
    {
        ProxyEndpointParseResult result =
            ProxyEndpointParser.Parse(
                "PROXY Proxy.Example:8080; HTTP proxy.example.:8080; DIRECT; DIRECT");

        Ensure(result.Endpoints.Count == 1
               && result.Endpoints[0].Sequence == 1,
            "중복 프록시는 첫 번째 순서만 유지해야 합니다.");
        Ensure(result.DuplicateEndpointCount == 1,
            "중복 프록시 집계가 필요합니다.");
        Ensure(result.DirectSequences.SequenceEqual([3])
               && result.DuplicateDirectCount == 1,
            "중복 DIRECT도 첫 번째 순서만 유지해야 합니다.");
        Ensure(result.Decision
               == ProxyEndpointDecision.ProxyWithDirectFallback,
            "중복 제거 뒤에도 프록시 우선·DIRECT fallback을 유지해야 합니다.");
    }

    private static void PreservesDirectBeforeProxyOrdering()
    {
        ProxyEndpointParseResult result =
            ProxyEndpointParser.Parse(
                "DIRECT; PROXY later.example:8080");

        Ensure(result.Decision
               == ProxyEndpointDecision.DirectWithProxyAlternatives,
            "DIRECT가 먼저면 프록시 기본 경로로 표시하면 안 됩니다.");
        Ensure(!result.DirectFallback,
            "DIRECT 우선 목록에서 DIRECT를 프록시 fallback이라고 부르면 안 됩니다.");
        Ensure(result.DirectSequences.SequenceEqual([1])
               && result.Endpoints[0].Sequence == 2,
            "DIRECT와 프록시의 실제 순서를 보존해야 합니다.");
        Ensure(result.Warnings.Any(message => message.Contains(
                "DIRECT가",
                StringComparison.Ordinal)),
            "DIRECT 우선 순서를 사용자가 확인할 수 있는 경고가 필요합니다.");
    }

    private static void ReportsMixedInputAndBoundedLimits()
    {
        ProxyEndpointParseResult mixed = ProxyEndpointParser.Parse(
            "http=manual.example:8080; PROXY automatic.example:8081; DIRECT",
            new Uri("https://download.example/file.bin"));
        Ensure(mixed.SourceKind == ProxyEndpointSourceKind.Mixed,
            "수동 매핑과 자동 지시문 혼합을 구분해야 합니다.");
        Ensure(mixed.Endpoints.Count == 1
               && mixed.Endpoints[0].Host
                   == "automatic.example"
               && mixed.IgnoredEndpointCount == 1,
            "HTTPS 대상에서 HTTP 수동 매핑은 제외하고 자동 후보를 유지해야 합니다.");

        string many = string.Join(
            ' ',
            Enumerable.Range(1, 70)
                .Select(index =>
                    $"proxy-{index:D2}.example:{8000 + index}"));
        ProxyEndpointParseResult bounded =
            ProxyEndpointParser.Parse(many);
        Ensure(bounded.TruncatedTokenCount == 6,
            "64개를 넘는 원시 토큰 수를 집계해야 합니다.");
        Ensure(bounded.ParsedEndpointCount == 64,
            "해석 대상으로 유지한 64개 토큰을 모두 검증해야 합니다.");
        Ensure(bounded.Endpoints.Count
               == ProxyEndpointParser.MaximumEndpointCount,
            "선택 프록시 후보는 32개로 제한해야 합니다.");
        Ensure(bounded.IgnoredEndpointCount == 32,
            "후보 제한으로 제외한 32개를 집계해야 합니다.");
        Ensure(bounded.Warnings.Count(message => message.Contains(
                "최대 32개",
                StringComparison.Ordinal)) == 1,
            "후보 제한 경고는 한 번만 생성해야 합니다.");

        string tooLong = new(
            'a',
            ProxyEndpointParser.MaximumInputLength + 1);
        ProxyEndpointParseResult oversized =
            ProxyEndpointParser.Parse(tooLong);
        Ensure(oversized.Errors.Count == 1
               && oversized.Endpoints.Count == 0,
            "입력 길이 상한 초과는 파싱 전에 오류로 중단해야 합니다.");
    }

    private static void ProducesPrivacySafeStableLabels()
    {
        ProxyEndpointParseResult result =
            ProxyEndpointParser.Parse(
                "PROXY Secret-Proxy.Example:8080");
        ProxyEndpointCandidate endpoint = result.Endpoints.Single();
        string fingerprintFromCaseVariant =
            ProxyEndpointParser.CreateHostFingerprint(
                "SECRET-PROXY.EXAMPLE.");

        Ensure(endpoint.HostFingerprint.Length
               == ProxyEndpointParser.FingerprintLength,
            "호스트 지문 길이가 고정돼야 합니다.");
        Ensure(endpoint.HostFingerprint
               == fingerprintFromCaseVariant,
            "호스트 지문은 대소문자와 마지막 점에 안정적이어야 합니다.");
        Ensure(endpoint.SafeLabel.Contains(
                endpoint.HostFingerprint,
                StringComparison.Ordinal)
               && endpoint.SafeLabel.Contains(
                   "8080",
                   StringComparison.Ordinal),
            "안전 라벨에는 지문과 포트가 필요합니다.");
        Ensure(!endpoint.SafeLabel.Contains(
                endpoint.Host,
                StringComparison.OrdinalIgnoreCase)
               && !endpoint.SafeLabel.Contains(
                   "secret-proxy",
                   StringComparison.OrdinalIgnoreCase),
            "안전 라벨에 프록시 호스트 원문을 포함하면 안 됩니다.");
    }

    private static void RejectsUnsupportedTargetUris()
    {
        ProxyEndpointParseResult ftp = ProxyEndpointParser.Parse(
            "PROXY proxy.example:8080",
            new Uri("ftp://download.example/file.bin"));
        ProxyEndpointParseResult relative = ProxyEndpointParser.Parse(
            "PROXY proxy.example:8080",
            new Uri("/file.bin", UriKind.Relative));

        Ensure(ftp.Errors.Count == 1
               && ftp.Endpoints.Count == 0
               && !ftp.IsUsable,
            "FTP 대상은 프록시 후보를 선택하기 전에 거부해야 합니다.");
        Ensure(relative.Errors.Count == 1
               && relative.Endpoints.Count == 0,
            "상대 URI는 대상 스킴 선택에 사용할 수 없습니다.");
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
