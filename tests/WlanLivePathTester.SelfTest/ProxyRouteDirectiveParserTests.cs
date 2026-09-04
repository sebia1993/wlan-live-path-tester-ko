using System.Runtime.CompilerServices;
using System.Text.Json;
using WlanLivePathTester.Core.Proxy;

namespace WlanLivePathTester.SelfTest;

internal static class ProxyRouteDirectiveParserTests
{
#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Run()
    {
        ParsesPacFallbackChainInOrder();
        ParsesWindowsSchemeMappings();
        ParsesAbsoluteUrisAndDefaultPorts();
        ParsesBracketedIpv6AndIdn();
        KeepsValidSegmentsInPartialResult();
        DeduplicatesCanonicalEndpoints();
        RejectsCredentialsPathsPortsAndUnbracketedIpv6();
        RejectsControlCharactersLengthAndSegmentFloods();
        DoesNotExposeRawHostThroughDisplayOrJson();
        HandlesEmptyInputWithoutFailure();
        Console.WriteLine(
            "PASS local proxy route directive parser tests");
    }

    private static void ParsesPacFallbackChainInOrder()
    {
        ProxyDirectiveParseResult result =
            ProxyRouteDirectiveParser.Parse(
                "PROXY proxy-a.example.invalid:8080; HTTPS proxy-b.example.invalid:8443; SOCKS5 [2001:db8::5]:1080; DIRECT");

        Ensure(result.Status == ProxyDirectiveParseStatus.Success,
            $"PAC fallback chain should parse successfully: {result.Status}");
        Ensure(result.Directives.Count == 4,
            "PAC fallback chain should preserve four directives.");
        Ensure(result.Directives[0].Kind
               == ProxyRouteDirectiveKind.HttpProxy,
            "PROXY should map to HttpProxy.");
        Ensure(result.Directives[1].Kind
               == ProxyRouteDirectiveKind.HttpsProxy,
            "HTTPS PAC keyword should map to HttpsProxy.");
        Ensure(result.Directives[2].Kind
               == ProxyRouteDirectiveKind.Socks5Proxy,
            "SOCKS5 PAC keyword should map to Socks5Proxy.");
        Ensure(result.Directives[3].IsDirect,
            "DIRECT fallback should remain the last directive.");
        Ensure(result.HasProxyEndpoint && result.HasDirectFallback,
            "PAC chain should expose proxy endpoints and a DIRECT fallback.");
        Ensure(result.Directives.Select(item => item.Sequence)
                .SequenceEqual([1, 2, 3, 4]),
            "Directive sequence should preserve source order.");
    }

    private static void ParsesWindowsSchemeMappings()
    {
        ProxyDirectiveParseResult result =
            ProxyRouteDirectiveParser.Parse(
                "http=proxy-http.example.invalid:8080;https=proxy-connect.example.invalid:8080;ftp=DIRECT;socks=proxy-socks.example.invalid:1080");

        Ensure(result.Status == ProxyDirectiveParseStatus.Success,
            "Windows per-scheme proxy mappings should parse.");
        Ensure(result.Directives.Count == 4,
            "All four Windows mappings should be retained.");
        Ensure(result.Directives[0].Scope == "http"
               && result.Directives[0].Kind
                   == ProxyRouteDirectiveKind.HttpProxy,
            "HTTP mapping should retain its destination scope.");
        Ensure(result.Directives[1].Scope == "https"
               && result.Directives[1].Kind
                   == ProxyRouteDirectiveKind.HttpProxy,
            "Windows https= mapping describes an HTTP CONNECT proxy for the HTTPS destination scope.");
        Ensure(result.Directives[2].Scope == "ftp"
               && result.Directives[2].IsDirect,
            "Scoped DIRECT should remain scoped.");
        Ensure(result.Directives[3].Scope == "socks"
               && result.Directives[3].Kind
                   == ProxyRouteDirectiveKind.SocksProxy,
            "SOCKS mapping should retain its kind and scope.");
        Ensure(result.Directives.All(item =>
                item.SourceSyntax
                    == ProxyDirectiveSourceSyntax.SchemeMapping),
            "Windows mappings should identify SchemeMapping syntax.");
    }

    private static void ParsesAbsoluteUrisAndDefaultPorts()
    {
        ProxyDirectiveParseResult http =
            ProxyRouteDirectiveParser.Parse(
                "http://proxy-http.example.invalid");
        ProxyDirectiveParseResult https =
            ProxyRouteDirectiveParser.Parse(
                "https://proxy-https.example.invalid");
        ProxyDirectiveParseResult socks =
            ProxyRouteDirectiveParser.Parse(
                "socks5://proxy-socks.example.invalid");

        Ensure(http.Directives.Single().Port == 80
               && http.Directives.Single().Kind
                   == ProxyRouteDirectiveKind.HttpProxy,
            "HTTP URI should use default port 80.");
        Ensure(https.Directives.Single().Port == 443
               && https.Directives.Single().Kind
                   == ProxyRouteDirectiveKind.HttpsProxy,
            "HTTPS URI should use default port 443.");
        Ensure(socks.Directives.Single().Port == 1080
               && socks.Directives.Single().Kind
                   == ProxyRouteDirectiveKind.Socks5Proxy,
            "SOCKS5 URI should use default port 1080.");
        Ensure(http.Directives.Single().SourceSyntax
               == ProxyDirectiveSourceSyntax.AbsoluteUri,
            "Absolute proxy URI syntax should be recorded.");
    }

    private static void ParsesBracketedIpv6AndIdn()
    {
        ProxyDirectiveParseResult ipv6 =
            ProxyRouteDirectiveParser.Parse(
                "PROXY [2001:0db8:0:0:0:0:0:1]:8080");
        ProxyDirectiveParseResult idn =
            ProxyRouteDirectiveParser.Parse(
                "PROXY münich.example:8080");

        ProxyRouteDirective ipv6Directive = ipv6.Directives.Single();
        ProxyRouteDirective idnDirective = idn.Directives.Single();
        Ensure(ipv6Directive.Host == "2001:db8::1",
            "IPv6 host should use canonical local form.");
        Ensure(ipv6Directive.Port == 8080,
            "Bracketed IPv6 port should be preserved.");
        Ensure(idnDirective.Host == "xn--mnich-kva.example",
            "IDN host should normalize to lowercase ASCII for deterministic matching.");
        Ensure(idnDirective.HostFingerprint.Length
               == ProxyHostFingerprint.DisplayLength,
            "Normalized host should expose only a short fingerprint for display.");
    }

    private static void KeepsValidSegmentsInPartialResult()
    {
        ProxyDirectiveParseResult result =
            ProxyRouteDirectiveParser.Parse(
                "PROXY valid-a.example.invalid:8080;UNKNOWN invalid;DIRECT;PROXY valid-b.example.invalid:3128");

        Ensure(result.Status
               == ProxyDirectiveParseStatus.PartialSuccess,
            "Valid directives should remain when another segment is invalid.");
        Ensure(result.Directives.Count == 3,
            "Two proxies and DIRECT should remain.");
        Ensure(result.Issues.Count(issue =>
                issue.Severity
                    == ProxyDirectiveIssueSeverity.Error) == 1,
            "One invalid segment should produce one error without echoing the raw token.");
        Ensure(result.Message.Contains(
                "유효한 프록시 지시문 3개",
                StringComparison.Ordinal),
            "Partial result message should report only counts.");
    }

    private static void DeduplicatesCanonicalEndpoints()
    {
        ProxyDirectiveParseResult result =
            ProxyRouteDirectiveParser.Parse(
                "PROXY Example.COM.:8080;proxy example.com:8080;DIRECT;direct");

        Ensure(result.Status == ProxyDirectiveParseStatus.Success,
            "Duplicate-only warnings should not make the parse partial.");
        Ensure(result.Directives.Count == 2,
            "Canonical proxy endpoint and DIRECT should each remain once.");
        Ensure(result.Issues.Count(issue =>
                issue.Code == "DUPLICATE_DIRECTIVE") == 2,
            "Both duplicate segments should produce deterministic warnings.");
        Ensure(result.Directives[0].Host == "example.com",
            "DNS host should be lowercase with the terminal dot removed.");
    }

    private static void
        RejectsCredentialsPathsPortsAndUnbracketedIpv6()
    {
        string[] invalidInputs =
        [
            "http://user:secret@proxy.example.invalid:8080",
            "https://proxy.example.invalid:8443/private",
            "PROXY proxy.example.invalid:0",
            "PROXY proxy.example.invalid:65536",
            "PROXY proxy.example.invalid:not-a-port",
            "PROXY 2001:db8::1:8080",
            "PROXY [2001:db8::1]8080",
            "ftp://proxy.example.invalid:21",
            "PROXY proxy.example.invalid:8080/path",
            "PROXY user@proxy.example.invalid:8080"
        ];

        foreach (string input in invalidInputs)
        {
            ProxyDirectiveParseResult result =
                ProxyRouteDirectiveParser.Parse(input);
            Ensure(result.Status
                   == ProxyDirectiveParseStatus.InvalidInput,
                $"Unsafe proxy input should be rejected: {input}");
            Ensure(result.Directives.Count == 0,
                "Rejected input should not expose a usable endpoint.");
            Ensure(result.Issues.Any(issue =>
                    issue.Severity
                        == ProxyDirectiveIssueSeverity.Error),
                "Rejected input should have a stable error issue.");
        }
    }

    private static void
        RejectsControlCharactersLengthAndSegmentFloods()
    {
        ProxyDirectiveParseResult newline =
            ProxyRouteDirectiveParser.Parse(
                "PROXY proxy.example.invalid:8080\r\nDIRECT");
        ProxyDirectiveParseResult tooLong =
            ProxyRouteDirectiveParser.Parse(
                new string('a',
                    ProxyRouteDirectiveParser.MaximumInputLength + 1));
        string tooManyValue = string.Join(
            ';',
            Enumerable.Range(
                    0,
                    ProxyRouteDirectiveParser.MaximumSegments + 1)
                .Select(index =>
                    $"PROXY p{index}.example.invalid:8080"));
        ProxyDirectiveParseResult tooMany =
            ProxyRouteDirectiveParser.Parse(tooManyValue);

        Ensure(newline.Issues.Single().Code
               == "CONTROL_CHARACTER",
            "Control characters should fail before segment parsing.");
        Ensure(tooLong.Issues.Single().Code == "INPUT_TOO_LONG",
            "Excessive input should fail with INPUT_TOO_LONG.");
        Ensure(tooMany.Issues.Single().Code
               == "TOO_MANY_SEGMENTS",
            "Segment flood should fail before endpoint parsing.");
    }

    private static void DoesNotExposeRawHostThroughDisplayOrJson()
    {
        const string secretHost =
            "highly-sensitive-proxy.example.invalid";
        ProxyDirectiveParseResult result =
            ProxyRouteDirectiveParser.Parse(
                $"PROXY {secretHost}:8080;DIRECT");
        ProxyRouteDirective directive = result.Directives[0];

        Ensure(!directive.ToString().Contains(
                secretHost,
                StringComparison.OrdinalIgnoreCase),
            "ProxyRouteDirective.ToString must not expose the raw host.");
        Ensure(!directive.RedactedDisplay.Contains(
                secretHost,
                StringComparison.OrdinalIgnoreCase),
            "Redacted display must not expose the raw host.");

        string json = JsonSerializer.Serialize(result);
        Ensure(!json.Contains(
                secretHost,
                StringComparison.OrdinalIgnoreCase),
            "Default JSON serialization must ignore raw proxy hosts.");
        Ensure(json.Contains(
                directive.HostFingerprint,
                StringComparison.Ordinal),
            "JSON may retain the non-reversible short host fingerprint.");
        Ensure(!result.Message.Contains(
                secretHost,
                StringComparison.OrdinalIgnoreCase)
               && !result.Issues.Any(issue =>
                   issue.Message.Contains(
                       secretHost,
                       StringComparison.OrdinalIgnoreCase)),
            "Result and issue messages must not echo proxy host input.");
    }

    private static void HandlesEmptyInputWithoutFailure()
    {
        foreach (string? input in new string?[] { null, string.Empty, "   " })
        {
            ProxyDirectiveParseResult result =
                ProxyRouteDirectiveParser.Parse(input);
            Ensure(result.Status == ProxyDirectiveParseStatus.Empty,
                "Null or whitespace input should be Empty, not invalid.");
            Ensure(!result.HasUsableDirective
                   && result.Issues.Count == 0,
                "Empty input should not create endpoints or issues.");
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
