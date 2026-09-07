using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Net;
using System.Text.Json;
using WlanLivePathTester.Core.Configuration;
using WlanLivePathTester.Core.Models;
using WlanLivePathTester.Core.Proxy;
using WlanLivePathTester.Core.Security;
using WlanLivePathTester.Core.Routing;
using WlanLivePathTester.Windows.Http;
using WlanLivePathTester.Windows.Routing;

namespace WlanLivePathTester.InputBoundarySmoke;

internal static class Program
{
    private const string Url = "https://measurement.example.invalid/File.bin?token=AbC";
    private static readonly Uri Destination = new(Url);
    private static int _failures;
    private static int _groups;
    private static int _rawCases;

    private static int Main()
    {
        (string Name, Action Test)[] tests =
        [
            ("raw control matrix across URL parser proxy list and bypass", RawControls),
            ("Unicode and escaped path compatibility", UnicodeAndEscapes),
            ("raw length before edge-space normalization", RawLengths),
            ("HTTP authority credentials fragments and ports", InvalidHttpSyntax),
            ("route literal bracket boundaries", RouteHostSyntax),
            ("URL-list line boundaries and case-sensitive resources", UrlLists),
            ("URI OriginalString is revalidated", OriginalString),
            ("IPv4-mapped IPv6 private-range parity", MappedAddresses),
            ("external local host names include terminal root dot", LocalNames),
            ("invalid measurement enum and limits fail", InvalidTargetMetadata),
            ("exact approved redirect hosts are bounded and validated", AllowedHosts),
            ("relative redirects and independent origin approval", RedirectApproval),
            ("redirect raw input and downgrade rejection", RedirectRaw),
            ("duplicate JSON names include case and escaped spelling", DuplicateJson),
            ("JSON unknown members comments and null entries fail", InvalidJsonShape),
            ("JSON UTF-8 size depth and target-count limits", JsonBounds),
            ("configuration retains invalid raw hosts for validation", RawConfiguration),
            ("catalog authority case folding preserves path and query case", CatalogIdentity),
            ("catalog replacement is validated before publication", CatalogAtomicity),
            ("approved redirect host collections are immutable snapshots", CatalogSnapshot),
            ("proxy candidate order scopes and IPv6 remain supported", ProxyCompatibility),
            ("runtime proxy selection and bypass remain bounded", RuntimeProxy),
            ("safe parser JSON and error messages exclude input payload", Privacy),
            ("WinHTTP rejects invalid raw requests before proxy lookup", TransportAdmission),
            ("explicit proxy endpoint rejects invalid syntax before connection", ExplicitProxyAdmission),
            ("cancellation retains the pre-request no-I/O path", PreCanceled),
            ("local route reader preserves raw input and pre-cancellation", LocalRouteAdmission)
        ];
        foreach ((string name, Action test) in tests)
        {
            _groups++;
            ApprovedTargetRuntimeCatalog.Clear();
            try { test(); Console.WriteLine($"PASS {name}"); }
            catch (Exception exception)
            {
                _failures++;
                Console.Error.WriteLine($"FAIL {name}: {exception}");
            }
            finally { ApprovedTargetRuntimeCatalog.Clear(); }
        }
        Console.WriteLine($"Input boundary smoke: {_groups} groups, {_failures} failures, {_rawCases} raw matrix assertions. No successful network request is executed.");
        return _failures == 0 ? 0 : 1;
    }

    private static void RawControls()
    {
        foreach (char character in Enumerable.Range(0, 160).Select(value => (char)value).Where(char.IsControl))
        {
            foreach (string raw in new[] { character + Url, Url + character, Url.Insert(12, character.ToString()), character.ToString() })
            {
                Ensure(!NetworkInputBoundary.TryHttpUri(raw, out Uri? rejected, out _) && rejected is null,
                    "A control character must not be erased before HTTP validation.");
                Ensure(TargetValidator.ValidateDefinition(Target(raw)).Count > 0,
                    "Target validation must reject raw control input.");
                _rawCases += 2;
            }
            foreach (string raw in new[] { character + "PROXY proxy.example.invalid:8080", "PROXY proxy.example.invalid:8080" + character,
                         "PROXY proxy.example" + character + ".invalid:8080", character.ToString() })
            {
                var parsed = ProxyEndpointParser.Parse(raw, Destination);
                Ensure(!parsed.IsUsable && parsed.Endpoints.Count == 0 && !parsed.DirectPresent,
                    "The endpoint parser must return no usable candidate for invalid raw input.");
                Ensure(ProxyDirectiveParser.SelectManual(Destination, raw, null).RouteKind == ProxyRouteKind.Unknown,
                    "The runtime manual parser must not turn controls into DIRECT or a proxy.");
                Ensure(ProxyDirectiveParser.SelectAutoProxyList(Destination, raw).RouteKind == ProxyRouteKind.Unknown,
                    "The runtime automatic parser must reject raw controls.");
                Ensure(!ProxyBypassMatcher.IsBypassed(Destination, "*" + character),
                    "Invalid bypass input must not become a wildcard bypass.");
                _rawCases += 4;
            }
        }
    }

    private static void UnicodeAndEscapes()
    {
        foreach (string text in new[] { "https://example.invalid/문서%20파일?값=한글", "https://예시.테스트/자료", "https://example.invalid/\U0001F4C4", "https://example.invalid/a%2Fb?q=%E2%82%AC" })
            Ensure(NetworkInputBoundary.TryHttpUri(text, out _, out _), "Ordinary Unicode and escaped paths must remain valid.");
        foreach (string suffix in new[] { "\u200B", "\u202E", "\u2028", "\u2029", "\u00A0", "\uFEFF", "\uD800", "\uDC00", "%00", "%0A", "%0d", "%09", "%7F", "%", "%x0", "%0" })
            Ensure(!NetworkInputBoundary.TryHttpUri(Url + suffix, out _, out _), "Malformed Unicode, escapes or hidden controls must fail.");
        Ensure(NetworkInputBoundary.TryHttpUri("  " + Url + "  ", out Uri? uri, out _) && uri.OriginalString == Url,
            "Ordinary edge spaces may be removed only after validating the raw envelope.");
    }

    private static void RawLengths()
    {
        const string prefix = "https://example.invalid/";
        string exact = prefix + new string('a', NetworkInputBoundary.MaximumUrlLength - prefix.Length);
        Ensure(NetworkInputBoundary.TryHttpUri(exact, out _, out _), "The exact URL limit must remain valid.");
        Ensure(!NetworkInputBoundary.TryHttpUri(" " + exact, out _, out _), "Trimming must not rescue an over-limit URL.");
        string proxy = "PROXY proxy.example.invalid:8080";
        Ensure(!ProxyEndpointParser.Parse(new string(' ', ProxyEndpointParser.MaximumInputLength) + proxy).IsUsable,
            "The endpoint parser must count the original input length.");
        Ensure(!NetworkInputBoundary.TrySingleLine("  abc  ", 6, out _, out _), "Generic raw length must precede trimming.");
    }

    private static void InvalidHttpSyntax()
    {
        foreach (string value in new[] { "ftp://example.invalid/file", "/relative", "http:example.invalid", "https://user:password@example.invalid/", "https://@example.invalid/", "https://example.invalid/#", "https://example.invalid/#x", "https://example.invalid:0/", "https://example.invalid:65536/", "https://example.invalid:/", "https://example.invalid/a b", "https://example.invalid\\other/", "https://%65xample.invalid/" })
            Ensure(!NetworkInputBoundary.TryHttpUri(value, out _, out _), "An invalid authority or URI syntax must not be normalized into approval.");
        Ensure(NetworkInputBoundary.TryHttpUri("https://[2001:db8::1]:8443/file", out _, out _), "Bracketed IPv6 and valid explicit ports must work.");
    }

    private static void RouteHostSyntax()
    {
        foreach (string value in new[] { "10.2.3.4", "[2001:db8::1]", "[2001:db8::1]:8080", "proxy.example.invalid:8080", "https://example.invalid/file", "fe80::1%12" })
            Ensure(NetworkInputBoundary.TryRouteHost(value, out string host, out _) && host.Length > 0,
                "Supported route-only URL, host or IP forms must work.");
        foreach (string value in new[] { "[[2001:db8::1]]", "[2001:db8::1", "proxy.example.invalid:0", "proxy.example.invalid:/", "host\t", "user@host" })
            Ensure(!NetworkInputBoundary.TryRouteHost(value, out _, out _), "Malformed brackets and raw route input must fail.");
    }

    private static void UrlLists()
    {
        Ensure(NetworkInputBoundary.TryHttpUrlLines("  " + Url + " \r\nhttps://measurement.example.invalid/file.bin?token=AbC\n" + Url,
                out string[] values, out _) && values.Length == 2,
            "Exact duplicates coalesce, but differently cased paths remain distinct.");
        foreach (string raw in new[] { Url + "\t", new string(' ', 2049) + "\n" + Url,
                     new string(' ', NetworkInputBoundary.MaximumUrlListLength + 1),
                     string.Join('\n', Enumerable.Range(0, 5).Select(i => $"https://example.invalid/{i}")) })
            Ensure(!NetworkInputBoundary.TryHttpUrlLines(raw, out _, out _), "Invalid lines or oversized lists must fail as a whole.");
    }

    private static void OriginalString()
    {
        Uri original = new(Url + "\t", UriKind.Absolute);
        Ensure(!NetworkInputBoundary.IsHttpUri(original, out _), "Already-constructed URI values retain a raw-input boundary.");
        Ensure(!ProxyEndpointParser.Parse("PROXY proxy.example.invalid:8080", original).IsUsable,
            "Endpoint scope selection must not trust a normalized URI alone.");
        Ensure(!NetworkInputBoundary.IsHttpUri(new Uri("relative", UriKind.Relative), out _), "Relative URIs must not throw or pass.");
    }

    private static void MappedAddresses()
    {
        foreach (string literal in new[] { "0.0.0.0", "10.0.0.1", "127.0.0.1", "100.64.0.1", "169.254.1.1", "172.16.0.1", "192.168.0.1", "198.18.0.1", "224.0.0.1", "255.255.255.255" })
        {
            IPAddress ipv4 = IPAddress.Parse(literal);
            Ensure(TargetValidator.IsLocalOrPrivate(ipv4) && TargetValidator.IsLocalOrPrivate(ipv4.MapToIPv6()),
                "Mapped IPv6 must use the same local/private classification as IPv4.");
            Ensure(TargetValidator.ValidateDefinition(Target($"https://[::ffff:{literal}]/")).Count > 0,
                "External mapped private destinations must be denied.");
        }
        Ensure(!TargetValidator.IsLocalOrPrivate(IPAddress.Parse("198.51.100.10").MapToIPv6()),
            "The mapped-address fix must not reject every IPv6 destination.");
        Ensure(TargetValidator.ValidateDefinition(Target("http://127.0.0.1/file", NetworkPathKind.Internal)).Count == 0,
            "Internal synthetic/local server use remains supported.");
    }

    private static void LocalNames()
    {
        foreach (string host in new[] { "localhost", "localhost.", "machine", "machine.", "a.local", "a.local.", "a.localhost." })
            Ensure(TargetValidator.ValidateDefinition(Target($"https://{host}/file")).Count > 0,
                "A terminal root dot must not turn a local host name into an external host.");
        Ensure(TargetValidator.ValidateDefinition(Target("https://example.invalid./file")).Count == 0,
            "An ordinary rooted public-form DNS name must remain valid.");
    }

    private static void InvalidTargetMetadata()
    {
        MeasurementTargetDefinition original = Target();
        foreach (MeasurementTargetDefinition invalid in new[] { original with { PathKind = (NetworkPathKind)999 },
                     original with { Name = "name\n" }, original with { Name = new string('a', 129) },
                     original with { Streams = 0 }, original with { TimeoutSeconds = 301 },
                     original with { MaxBytes = 0 }, original with { MaxRedirects = 11 },
                     original with { RequireDirect = true, RequireProxy = true } })
            Ensure(TargetValidator.ValidateDefinition(invalid).Count > 0, "Malformed metadata and execution limits must fail.");
    }

    private static void AllowedHosts()
    {
        foreach (string host in new[] { "", "\t", "cdn.example.invalid\n", "cdn.example.invalid..", "*.example.invalid", "https://cdn.example.invalid", "cdn.example.invalid:443", "localhost.", "192.168.1.1", new string('a', 254) })
            Ensure(TargetHostPolicy.ValidateConfiguredHosts(Target() with { AllowedRedirectHosts = [host] }).Count > 0,
                "Invalid configured hosts must not be dropped or silently widened.");
        Ensure(TargetHostPolicy.ValidateConfiguredHosts(Target() with { AllowedRedirectHosts = ["cdn.example.invalid", "CDN.EXAMPLE.INVALID."] }).Count > 0,
            "Normalized duplicate host entries must be diagnosed.");
        Ensure(TargetHostPolicy.ValidateConfiguredHosts(Target() with { AllowedRedirectHosts = ["cdn.example.invalid."] }).Count == 0,
            "A single rooted exact host remains supported.");
    }

    private static void RedirectApproval()
    {
        MeasurementTargetDefinition approved = Target() with { AllowedRedirectHosts = ["cdn.example.invalid"] };
        ApprovedTargetRuntimeCatalog.Configure([approved], true, "synthetic policy");
        foreach (string location in new[] { "/next/file", "https://cdn.example.invalid/file" })
        {
            RedirectValidationResult redirect = RedirectTargetValidator.Evaluate(Destination, location, NetworkPathKind.External);
            Ensure(redirect.IsAllowed && redirect.Destination is not null,
                "Syntax validation must not demand that every redirected path be a root catalog entry.");
            Ensure(TargetHostPolicy.EvaluateRedirect(approved, redirect.Destination).IsAllowed,
                "The original approved target must govern redirect host authorization.");
        }
        Ensure(!TargetHostPolicy.EvaluateRedirect(approved, new Uri("https://other.example.invalid/file")).IsAllowed,
            "Removing duplicate catalog validation must not allow an unapproved redirect host.");
        Ensure(!TargetHostPolicy.EvaluateRedirect(approved, new Uri("https://cdn.example.invalid:8443/file")).IsAllowed,
            "An approved host must not automatically approve a different non-default port.");
    }

    private static void RedirectRaw()
    {
        foreach (string location in new[] { "/next\t", "/next\r\n", "/a%0d%0a", "//user:secret@example.invalid/file", "https://example.invalid/#", "http://example.invalid/file", new string('a', 2049) })
            Ensure(!RedirectTargetValidator.Evaluate(Destination, location, NetworkPathKind.External).IsAllowed,
                "Invalid raw Location, credentials, fragments and external downgrade must fail.");
        Ensure(!RedirectTargetValidator.Evaluate(Destination, "/file", (NetworkPathKind)999).IsAllowed,
            "Undefined path-kind values cannot bypass redirect policy.");
    }

    private static void DuplicateJson()
    {
        string original = Config(Url);
        foreach (string json in new[]
        {
            original.Replace("\"schemaVersion\":1", "\"schemaVersion\":1,\"SchemaVersion\":1", StringComparison.Ordinal),
            original.Replace("\"url\":", "\"URL\":\"https://other.example.invalid/\",\"url\":", StringComparison.Ordinal),
            original.Replace("\"url\":", "\"\\u0075rl\":\"https://other.example.invalid/\",\"url\":", StringComparison.Ordinal),
            original.Replace("\"schemaVersion\":1", "\"enforceApprovedTargets\":true,\"enforceApprovedTargets\":false,\"schemaVersion\":1", StringComparison.Ordinal)
        })
            Throws<JsonException>(() => TargetConfigurationLoader.LoadWithPolicyFromJson(json));
    }

    private static void InvalidJsonShape()
    {
        Throws<JsonException>(() => TargetConfigurationLoader.LoadFromJson(Config(Url).Replace("\"schemaVersion\":1", "\"unknown\":true,\"schemaVersion\":1", StringComparison.Ordinal)));
        Throws<JsonException>(() => TargetConfigurationLoader.LoadFromJson("/* comment */" + Config(Url)));
        Throws<JsonException>(() => TargetConfigurationLoader.LoadFromJson(Config(Url)[..^1] + ",}"));
        Throws<InvalidDataException>(() => TargetConfigurationLoader.LoadFromJson("{\"schemaVersion\":1,\"externalTargets\":[null]}"));
        Throws<InvalidDataException>(() => TargetConfigurationLoader.LoadFromJson("{\"schemaVersion\":2,\"externalTargets\":[]}"));
        Throws<InvalidDataException>(() => TargetConfigurationLoader.LoadFromJson("{\"schemaVersion\":1,\"externalTargets\":[]}"));
    }

    private static void JsonBounds()
    {
        Throws<InvalidDataException>(() => TargetConfigurationLoader.LoadFromJson(Config(Url) + new string(' ', 1024 * 1024)));
        string largeUtf8 = "{\"schemaVersion\":1,\"externalTargets\":[{\"name\":\"" + new string('가', 360000) + "\",\"url\":\"" + Url + "\"}]}";
        InvalidDataException size = Throws<InvalidDataException>(() => TargetConfigurationLoader.LoadFromJson(largeUtf8));
        Ensure(size.Message.Contains("UTF-8", StringComparison.Ordinal), "UTF-8 byte size must be checked before parsing short-character-count input.");
        Throws<JsonException>(() => TargetConfigurationLoader.LoadFromJson("{\"defaults\":" + new string('[', 20) + "0" + new string(']', 20) + "}"));
        Ensure(TargetConfigurationLoader.LoadFromJson(Config(Enumerable.Range(0, 64).Select(i => $"https://example.invalid/{i}").ToArray())).Count == 64,
            "The exact target count limit must be supported.");
        Throws<InvalidDataException>(() => TargetConfigurationLoader.LoadFromJson(Config(Enumerable.Range(0, 65).Select(i => $"https://example.invalid/{i}").ToArray())));
    }

    private static void RawConfiguration()
    {
        foreach (string? host in new string?[] { "", null, "cdn.example.invalid\t" })
        {
            string json = JsonSerializer.Serialize(new { schemaVersion = 1, externalTargets = new[] { new { name = "synthetic", url = Url, allowedRedirectHosts = new[] { host } } } });
            Throws<InvalidDataException>(() => TargetConfigurationLoader.LoadFromJson(json));
        }
        Throws<InvalidDataException>(() => TargetConfigurationLoader.LoadFromJson(Config(Url + "\t")));
        Ensure(TargetConfigurationLoader.LoadFromJson(Config(Url, Url.Replace("File.bin", "file.bin", StringComparison.Ordinal))).Count == 2,
            "Different resource path casing must not be treated as duplicate definitions.");
        Throws<InvalidDataException>(() => TargetConfigurationLoader.LoadFromJson(Config(Url, Url)));
    }

    private static void CatalogIdentity()
    {
        MeasurementTargetDefinition approved = Target();
        ApprovedTargetRuntimeCatalog.Configure([approved], true, "synthetic");
        Ensure(ApprovedTargetRuntimeCatalog.TryResolve(approved with { Url = Url.Replace("https://measurement.example.invalid", "HTTPS://MEASUREMENT.EXAMPLE.INVALID", StringComparison.Ordinal) }, out _),
            "Scheme and host case are not resource identity differences.");
        foreach (string other in new[] { Url.Replace("File.bin", "file.bin", StringComparison.Ordinal), Url.Replace("AbC", "abc", StringComparison.Ordinal), Url + "\t" })
            Ensure(!ApprovedTargetRuntimeCatalog.TryResolve(approved with { Url = other }, out _),
                "Different path/query or malformed raw input must not match approval.");
    }

    private static void CatalogAtomicity()
    {
        MeasurementTargetDefinition approved = Target();
        ApprovedTargetRuntimeCatalog.Configure([approved], true, "synthetic");
        Throws<InvalidDataException>(() => ApprovedTargetRuntimeCatalog.Configure([approved with { Url = Url + "\n" }], false, "replacement"));
        Ensure(ApprovedTargetRuntimeCatalog.IsEnforced && ApprovedTargetRuntimeCatalog.TryResolve(approved, out _),
            "A rejected replacement must not destroy the existing enforced policy.");
        ApprovedTargetRuntimeCatalog.BlockEnforcedPolicy("synthetic", "synthetic failure");
        Ensure(!ApprovedTargetRuntimeCatalog.TryResolve(approved, out _) && TargetValidator.Validate(approved).Count > 0,
            "Blocked enforced policy must still reject downloads.");
    }

    private static void CatalogSnapshot()
    {
        string[] hosts = ["cdn.example.invalid"];
        MeasurementTargetDefinition approved = Target() with { AllowedRedirectHosts = hosts };
        ApprovedTargetRuntimeCatalog.Configure([approved], true, "synthetic");
        hosts[0] = "unapproved.example.invalid";
        Ensure(ApprovedTargetRuntimeCatalog.TryResolve(approved, out MeasurementTargetDefinition effective)
            && effective.AllowedRedirectHosts![0] == "cdn.example.invalid", "Caller mutation must not change an installed policy.");
        Throws<NotSupportedException>(() => ((IList<string>)effective.AllowedRedirectHosts!)[0] = "unapproved.example.invalid");
    }

    private static void ProxyCompatibility()
    {
        var parsed = ProxyEndpointParser.Parse("PROXY first.example.invalid:8080; DIRECT; PROXY later.example.invalid:8081", Destination);
        Ensure(parsed.IsUsable && parsed.Endpoints.Count == 2 && parsed.DirectFallback
            && parsed.Endpoints[0].Sequence < parsed.DirectSequences[0]
            && parsed.Endpoints[1].Sequence > parsed.DirectSequences[0], "Proxy and DIRECT order must be preserved.");
        var scoped = ProxyEndpointParser.Parse("http=first.example.invalid:8080;https=[2001:db8::1]:8443", Destination);
        Ensure(scoped.IsUsable && scoped.Endpoints.Count == 1 && scoped.Endpoints[0].Port == 8443,
            "Scheme selection and IPv6 endpoints must remain supported.");
        var duplicate = ProxyEndpointParser.Parse("PROXY first.example.invalid:8080; PROXY FIRST.EXAMPLE.INVALID:8080", Destination);
        Ensure(duplicate.Endpoints.Count == 1 && duplicate.DuplicateEndpointCount == 1, "Duplicate endpoints retain first occurrence.");
    }

    private static void RuntimeProxy()
    {
        var manual = ProxyDirectiveParser.SelectManual(Destination, "https=proxy.example.invalid:8080", null);
        Ensure(manual.RouteKind == ProxyRouteKind.Proxy && manual.ProxyCandidateCount == 1, "Ordinary manual proxy selection must remain valid.");
        var automatic = ProxyDirectiveParser.SelectAutoProxyList(Destination, "PROXY [2001:db8::1]:8080; DIRECT");
        Ensure(automatic.RouteKind == ProxyRouteKind.Proxy && automatic.HasDirectFallback
            && automatic.ProxyUris.Single() == "http://[2001:db8::1]:8080", "IPv6 proxy authority must use exactly one bracket pair.");
        Ensure(ProxyBypassMatcher.IsBypassed(new Uri("http://intranet/file"), "<local>")
            && ProxyBypassMatcher.IsBypassed(Destination, "*.example.invalid"), "Ordinary bypass rules remain supported.");
        Ensure(!ProxyBypassMatcher.IsValidList(new string('*', 1025)), "An over-limit bypass pattern must be rejected.");
        Ensure(ProxyDirectiveParser.SelectAutoProxyList(Destination, string.Join(';', Enumerable.Repeat("DIRECT", 129))).RouteKind == ProxyRouteKind.Unknown,
            "Excess runtime hops must not be truncated into DIRECT.");
    }

    private static void Privacy()
    {
        const string secret = "secret-input-proxy.example.invalid";
        var parsed = ProxyEndpointParser.Parse($"PROXY {secret}:8080", Destination);
        string safe = JsonSerializer.Serialize(parsed) + parsed.Endpoints.Single().ToString();
        Ensure(!safe.Contains(secret, StringComparison.OrdinalIgnoreCase), "Default candidate serialization and display must not contain the raw host.");
        JsonException json = Throws<JsonException>(() => TargetConfigurationLoader.LoadFromJson("{\"" + secret + "\":0}"));
        Ensure(!json.ToString().Contains(secret, StringComparison.Ordinal), "Configuration errors must not reflect arbitrary field names.");
        InvalidDataException definition = Throws<InvalidDataException>(() => TargetConfigurationLoader.LoadFromJson(Config("https://" + secret + "/bad\t")));
        Ensure(!definition.ToString().Contains(secret, StringComparison.Ordinal), "Rejected configuration URLs must not be copied into errors.");
    }

    private static void TransportAdmission()
    {
        foreach (WinHttpRequestOptions options in new[]
        {
            new WinHttpRequestOptions(Url + "\t", NetworkPathKind.External),
            new WinHttpRequestOptions(Url + "%0d", NetworkPathKind.External),
            new WinHttpRequestOptions("ftp://example.invalid/file", NetworkPathKind.External),
            new WinHttpRequestOptions(Url, (NetworkPathKind)999),
            new WinHttpRequestOptions(Url, NetworkPathKind.External, (WinHttpRequestMethod)999),
            new WinHttpRequestOptions(Url, NetworkPathKind.External, TimeoutMilliseconds: 999)
        })
        {
            WinHttpRequestResult result = WinHttpRequestExecutor.Execute(options);
            Ensure(result.Status == WinHttpRequestStatus.InvalidRequest && result.Route is null && result.BytesReceived == 0,
                "Malformed requests must fail before proxy lookup, allocation or network work.");
        }
    }

    private static void ExplicitProxyAdmission()
    {
        WinHttpRequestOptions options = new("http://127.0.0.1:9/file", NetworkPathKind.Internal);
        foreach (string proxy in new[] { "http://proxy.example.invalid:8080\t", "http://user:secret@proxy.example.invalid:8080", "ftp://proxy.example.invalid:8080", "http://proxy.example.invalid:0", "http://proxy.example.invalid:8080/path" })
        {
            WinHttpRequestResult result = WinHttpRequestExecutor.ExecuteExplicitForSmoke(options, proxy);
            Ensure(result.Status == WinHttpRequestStatus.InvalidRequest && result.BytesReceived == 0,
                "Invalid explicit proxy authority must fail before any native connection.");
        }
    }

    private static void PreCanceled()
    {
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        WinHttpRequestResult result = WinHttpRequestExecutor.Execute(new(Url, NetworkPathKind.External, CancellationToken: cancellation.Token));
        Ensure(result.Status == WinHttpRequestStatus.Canceled && result.Route is null && result.BytesReceived == 0,
            "Pre-canceled requests must not resolve a proxy or start transport.");
    }

    private static void LocalRouteAdmission()
    {
        foreach (char character in Enumerable.Range(0, 160).Select(value => (char)value).Where(char.IsControl))
        {
            foreach (string raw in new[] { character + "route.example.invalid", "route.example.invalid" + character,
                         "route" + character + ".example.invalid", character.ToString() })
            {
                Ensure(!LocalRouteEvidenceReader.TryExtractHost(raw, out string host, out _) && host.Length == 0,
                    "The Windows route adapter must not erase controls before calling the shared parser.");
                _rawCases++;
            }
        }
        foreach (string raw in new[] { "https://example.invalid/a%0d", "https://user:secret@example.invalid/", "host\u200B" })
            Ensure(!LocalRouteEvidenceReader.TryExtractHost(raw, out _, out _), "Malformed route input must fail locally.");
        Ensure(LocalRouteEvidenceReader.TryExtractHost("  route.example.invalid:8080  ", out string ordinary, out _)
            && ordinary == "route.example.invalid", "Ordinary edge spaces and host:port remain supported.");
        using CancellationTokenSource canceled = new();
        canceled.Cancel();
        DestinationRouteEvidence result = LocalRouteEvidenceReader.ReadAsync(
            "route.example.invalid", "synthetic", RouteProbePurpose.InternalDirectTarget,
            cancellationToken: canceled.Token).GetAwaiter().GetResult();
        Ensure(result.Status == DestinationRouteEvidenceStatus.Canceled && !result.DnsWasUsed,
            "Pre-cancellation must avoid DNS and report that it was not performed.");
    }

    private static MeasurementTargetDefinition Target(string url = Url, NetworkPathKind path = NetworkPathKind.External) =>
        new(Name: "synthetic", Url: url, PathKind: path, RequireProxy: path == NetworkPathKind.External,
            RequireDirect: path == NetworkPathKind.Internal, MaxBytes: 1024 * 1024, TimeoutSeconds: 30,
            Streams: 1, MaxRedirects: 3);
    private static string Config(params string[] urls) => JsonSerializer.Serialize(new
    {
        schemaVersion = 1,
        externalTargets = urls.Select(url => new { name = "synthetic", url }).ToArray()
    });
    private static T Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T exception) { return exception; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }
    private static void Ensure([DoesNotReturnIf(false)] bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
