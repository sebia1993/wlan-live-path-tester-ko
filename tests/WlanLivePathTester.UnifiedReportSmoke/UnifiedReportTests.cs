using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WlanLivePathTester.Core.Reporting;
using WlanLivePathTester.Core.Routing;
using static WlanLivePathTester.UnifiedReportSmoke.Fixtures;

namespace WlanLivePathTester.UnifiedReportSmoke;

internal static class UnifiedReportTests
{
    internal static IEnumerable<(string Name, Action Test)> Cases =>
    [
        ("legacy constructor deconstruction and absent section", LegacyContract),
        ("four comparison outcomes in all formats", ComparisonMatrix),
        ("all run outcomes and unknown states", RunMatrix),
        ("canonical route finding and idempotent composition", FindingComposition),
        ("raw evidence and identifiers never exported", PrivacyBoundary),
        ("HTML encoding and CSV formula protection", OutputEncoding),
        ("JSON old new and explicit-null round trips", JsonCompatibility),
        ("safe scalar field parity and separate capture times", FieldParity),
        ("independent file sets with matching SHA-256", LocalFiles)
    ];

    private static void LegacyContract()
    {
        LocalDiagnosticReport legacy = Report();
        var (schema, metadata, wlan, proxy, texts, observation, findings, limitations, structured) = legacy;
        LocalDiagnosticReport positional = new(schema, metadata, wlan, proxy, texts,
            observation, findings, limitations, structured);
        Ensure(ReferenceEquals(LocalReportRouteComparison.Attach(legacy, null), legacy),
            "No route input must preserve the original report reference.");
        Ensure(positional.SchemaVersion == "1.1" && positional.InternalProxyRouteComparison is null,
            "The original nine-position constructor and Deconstruct must remain usable.");
        using JsonDocument json = JsonDocument.Parse(LocalReportWriter.RenderJson(legacy));
        Ensure(!json.RootElement.TryGetProperty("internalProxyRouteComparison", out _),
            "Absent route evidence must not add a JSON field to legacy output.");
        Ensure(!LocalReportWriter.RenderCsv(legacy).Contains("internalProxyRouteComparison", StringComparison.Ordinal)
            && !LocalReportWriter.RenderHtml(legacy).Contains("internal-proxy-route-comparison", StringComparison.Ordinal),
            "Do not add a placeholder route section to CSV or HTML when no comparison ran.");
        Ensure(legacy.Findings.Any(f => f.Code == "NO_CLEAR_FAILURE_PATTERN"),
            "The route composer must not alter existing findings without route evidence.");
    }

    private static void ComparisonMatrix()
    {
        foreach (InternalProxyRouteComparisonStatus status in Enum.GetValues<InternalProxyRouteComparisonStatus>())
        {
            LocalDiagnosticReport report = LocalReportRouteComparison.Attach(Report(), Run(status));
            using JsonDocument json = JsonDocument.Parse(LocalReportWriter.RenderJson(report));
            JsonElement root = json.RootElement;
            JsonElement route = root.GetProperty("internalProxyRouteComparison");
            Ensure(root.GetProperty("schemaVersion").GetString() == "1.2"
                && route.GetProperty("comparison").GetProperty("status").GetString() == status.ToString(),
                "Every comparison status must be preserved under the optional schema 1.2 section.");
            string code = route.GetProperty("finding").GetProperty("code").GetString()!;
            Ensure(root.GetProperty("findings").EnumerateArray()
                .Count(f => f.GetProperty("code").GetString() == code) == 1,
                "The global Finding array must contain exactly one canonical route finding.");
            string csv = LocalReportWriter.RenderCsv(report);
            string html = LocalReportWriter.RenderHtml(report);
            Ensure(CsvRows(csv).Any(row => row.Length == 3
                && row[0] == "internalProxyRouteComparison.comparison" && row[1] == "status" && row[2] == status.ToString()),
                "CSV must preserve the structured comparison status.");
            Ensure(html.Contains("id=\"internal-proxy-route-comparison\"", StringComparison.Ordinal)
                && html.Contains(status.ToString(), StringComparison.Ordinal)
                && html.Contains(code, StringComparison.Ordinal), "HTML must include the status and finding code.");
            Ensure(!report.Findings.Any(f => f.Code == "NO_CLEAR_FAILURE_PATTERN"),
                "A specific route result must not coexist with the generic no-pattern finding.");
        }
    }

    private static void RunMatrix()
    {
        foreach (InternalProxyRouteComparisonRunStatus status in Enum.GetValues<InternalProxyRouteComparisonRunStatus>()
                     .Append((InternalProxyRouteComparisonRunStatus)999))
        {
            var run = Run() with { Status = status, Comparison = null, ProxyExecution = null, InternalRouteEvidence = null };
            LocalDiagnosticReport report = LocalReportRouteComparison.Attach(Report(), run);
            string expected = Enum.IsDefined(status) ? status.ToString() : "Unknown";
            using JsonDocument json = JsonDocument.Parse(LocalReportWriter.RenderJson(report));
            var route = json.RootElement.GetProperty("internalProxyRouteComparison");
            Ensure(route.GetProperty("runStatus").GetString() == expected
                && route.GetProperty("comparison").ValueKind == JsonValueKind.Null,
                "Failed, canceled, unavailable and unknown runs must not fabricate a comparison.");
            Ensure(LocalReportWriter.RenderCsv(report).Contains(expected, StringComparison.Ordinal)
                && LocalReportWriter.RenderHtml(report).Contains(expected, StringComparison.Ordinal),
                "All output formats must preserve non-success run outcomes.");
        }
    }

    private static void FindingComposition()
    {
        LocalDiagnosticReport baseline = Report();
        ReportFinding unrelated = baseline.Findings[1];
        baseline = baseline with
        {
            Findings = [Finding("internal_proxy_route_stale"), Finding("INTERNAL_PROXY_ROUTE_RUN_FAILED"),
                Finding("no_clear_failure_pattern"), unrelated, Finding("PROXY_AUTH_REQUIRED")]
        };
        LocalDiagnosticReport once = LocalReportRouteComparison.Attach(baseline, Run());
        LocalDiagnosticReport twice = LocalReportRouteComparison.Attach(once, Run());
        Ensure(twice.Findings.Count(f => f.Code.StartsWith("INTERNAL_PROXY_ROUTE_", StringComparison.OrdinalIgnoreCase)) == 1,
            "Repeated composition must replace stale route findings instead of appending duplicates.");
        Ensure(twice.Findings.Contains(unrelated) && twice.Findings.Any(f => f.Code == "PROXY_AUTH_REQUIRED"),
            "Existing observation and authentication findings must be retained.");
        Ensure(twice.Limitations.Count(l => l == LocalReportRouteComparison.CorrelationLimitation) == 1
            && twice.Limitations.Count(l => l == LocalReportRouteComparison.PathLimitation) == 1,
            "The two fixed route limitations must not accumulate.");
        Ensure(LocalReportWriter.RenderJson(once) == LocalReportWriter.RenderJson(twice)
            && LocalReportWriter.RenderCsv(once) == LocalReportWriter.RenderCsv(twice)
            && LocalReportWriter.RenderHtml(once) == LocalReportWriter.RenderHtml(twice),
            "Composition and rendering must remain deterministic and idempotent.");
        Ensure(baseline.SchemaVersion == "1.1" && baseline.InternalProxyRouteComparison is null,
            "Composition must not mutate the input record.");
        LocalDiagnosticReport future = LocalReportRouteComparison.Attach(baseline with { SchemaVersion = "2.0" }, Run());
        Ensure(future.SchemaVersion == "2.0", "Do not downgrade a future schema version.");
    }

    private static void PrivacyBoundary()
    {
        var raw = Run();
        string rawText = raw.InternalRouteEvidence!.TargetLabel;
        Ensure(rawText.Contains(SecretInternal, StringComparison.Ordinal), "The fixture must actually contain raw secrets.");
        var report = LocalReportRouteComparison.Attach(Report(), raw);
        foreach (string content in new[] { LocalReportWriter.RenderJson(report), LocalReportWriter.RenderCsv(report), LocalReportWriter.RenderHtml(report) })
        {
            foreach (string secret in Secrets)
                Ensure(!content.Contains(secret, StringComparison.OrdinalIgnoreCase), "Raw route data leaked to a unified format.");
            Ensure(!content.Contains("internalRouteEvidence", StringComparison.OrdinalIgnoreCase)
                && !content.Contains("selectedInterfaceIdentity", StringComparison.OrdinalIgnoreCase),
                "Raw evidence fields must never be part of the unified snapshot.");
            Ensure(content.Contains("112233aabb", StringComparison.Ordinal),
                "Keep the permitted endpoint fingerprint rather than dropping all endpoint evidence.");
        }
        Ensure(report.StructuredMeasurements!.Count == 1 && report.Measurements.Count == 1
            && report.Metadata == Report().Metadata, "Route composition must preserve the legacy measurements and metadata.");
    }

    private static void OutputEncoding()
    {
        LocalDiagnosticReport report = LocalReportRouteComparison.Attach(Report(), Run());
        var snapshot = report.InternalProxyRouteComparison!;
        const string formula = "=1+1 <script>alert(1)</script>,\"quoted\"\nnext";
        report = report with
        {
            InternalProxyRouteComparison = snapshot with
            {
                Finding = snapshot.Finding with { Title = formula }
            }
        };
        string csv = LocalReportWriter.RenderCsv(report);
        string html = LocalReportWriter.RenderHtml(report);
        Ensure(CsvRows(csv).Any(row => row.Length == 3 && row[0].StartsWith("finding.", StringComparison.Ordinal)
            && row[1] == "title" && row[2] == "'" + formula),
            "Route finding CSV cells must retain escaping and formula protection.");
        Ensure(!html.Contains("<script", StringComparison.OrdinalIgnoreCase)
            && html.Contains("&lt;script&gt;", StringComparison.Ordinal)
            && html.Contains("Content-Security-Policy", StringComparison.Ordinal)
            && !html.Contains("<iframe", StringComparison.OrdinalIgnoreCase)
            && !html.Contains("<link", StringComparison.OrdinalIgnoreCase),
            "Dynamic route fields must be encoded inside the existing offline HTML/CSP.");
        using JsonDocument json = JsonDocument.Parse(LocalReportWriter.RenderJson(report));
        Ensure(json.RootElement.GetProperty("findings").EnumerateArray()
            .Any(f => f.GetProperty("title").GetString() == formula), "JSON must retain valid escaped text.");
    }

    private static void JsonCompatibility()
    {
        JsonSerializerOptions options = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true };
        string legacyJson = LocalReportWriter.RenderJson(Report());
        LocalDiagnosticReport legacy = JsonSerializer.Deserialize<LocalDiagnosticReport>(legacyJson, options)!;
        Ensure(legacy.InternalProxyRouteComparison is null && legacy.SchemaVersion == "1.1", "Old JSON must remain readable.");
        string explicitNull = legacyJson.TrimEnd().TrimEnd('}') + ",\"internalProxyRouteComparison\":null}";
        Ensure(JsonSerializer.Deserialize<LocalDiagnosticReport>(explicitNull, options)!.InternalProxyRouteComparison is null,
            "An explicit null optional section must remain valid.");
        string currentJson = LocalReportWriter.RenderJson(LocalReportRouteComparison.Attach(Report(), Run()));
        LocalDiagnosticReport current = JsonSerializer.Deserialize<LocalDiagnosticReport>(currentJson, options)!;
        Ensure(LocalReportWriter.RenderJson(current) == currentJson,
            "Safe schema 1.2 snapshots must round trip without rebuilding raw route identity.");
        Ensure(current.InternalProxyRouteComparison!.CompletedAt == CapturedAt,
            "Round trips must retain the original route completion timestamp.");
    }

    private static void FieldParity()
    {
        var report = LocalReportRouteComparison.Attach(Report(), Run());
        using JsonDocument document = JsonDocument.Parse(LocalReportWriter.RenderJson(report));
        JsonElement snapshot = document.RootElement.GetProperty("internalProxyRouteComparison");
        string[][] rows = CsvRows(LocalReportWriter.RenderCsv(report)).Where(row => row.Length == 3).ToArray();
        string html = LocalReportWriter.RenderHtml(report);
        CheckScalars(snapshot, "internalProxyRouteComparison", ["comparison", "proxyEntries", "finding"]);
        CheckScalars(snapshot.GetProperty("comparison"), "internalProxyRouteComparison.comparison", []);
        CheckScalars(snapshot.GetProperty("proxyEntries")[0], "internalProxyRouteComparison.proxyEntry.1", []);
        Ensure(snapshot.GetProperty("completedAt").GetDateTimeOffset() == CapturedAt
            && document.RootElement.GetProperty("metadata").GetProperty("generatedAt").GetDateTimeOffset() > CapturedAt,
            "Report creation time must not replace the separately collected route timestamp.");
        Ensure(report.Limitations.Contains(LocalReportRouteComparison.CorrelationLimitation),
            "A unified report must not imply simultaneous throughput and route measurement.");
        void CheckScalars(JsonElement element, string section, string[] excluded)
        {
            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (excluded.Contains(property.Name, StringComparer.Ordinal)) continue;
                Ensure(rows.Any(row => row[0] == section && row[1] == property.Name),
                    $"CSV omitted safe field {section}.{property.Name}.");
                Ensure(html.Contains(property.Name, StringComparison.Ordinal), "HTML must show the same safe scalar projection.");
            }
        }
    }

    private static void LocalFiles()
    {
        string directory = Path.Combine(Path.GetTempPath(), "UnifiedReportSmoke", Guid.NewGuid().ToString("N"));
        try
        {
            var report = LocalReportRouteComparison.Attach(Report(), Run());
            LocalReportExportResult first = LocalReportWriter.WriteAll(report, directory, "unified");
            LocalReportExportResult second = LocalReportWriter.WriteAll(report, directory, "unified");
            Ensure(first.JsonPath != second.JsonPath && Directory.GetFiles(directory).Length == 8,
                "Sequential reports with the same timestamp must not overwrite an earlier set.");
            foreach (LocalReportExportResult export in new[] { first, second })
            {
                Ensure(export.Sha256.Count == 3, "Each set needs JSON, CSV and HTML checksums.");
                string checksum = File.ReadAllText(export.Sha256Path);
                foreach ((string name, string expected) in export.Sha256)
                {
                    string path = Path.Combine(export.OutputDirectory, name);
                    using FileStream stream = File.OpenRead(path);
                    string actual = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
                    Ensure(actual == expected && checksum.Contains($"{expected}  {name}", StringComparison.Ordinal),
                        "The checksum file must match the actual persisted bytes.");
                    string content = File.ReadAllText(path);
                    foreach (string secret in Secrets) Ensure(!content.Contains(secret, StringComparison.OrdinalIgnoreCase),
                        "Persisted report files must not include raw route secrets.");
                }
            }
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }

    private static IEnumerable<string[]> CsvRows(string text)
    {
        List<string> cells = [];
        StringBuilder cell = new();
        bool quoted = false;
        for (int i = 0; i < text.Length; i++)
        {
            char current = text[i];
            if (current == '"')
            {
                if (quoted && i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i++; }
                else quoted = !quoted;
            }
            else if (current == ',' && !quoted) { cells.Add(cell.ToString()); cell.Clear(); }
            else if (current is '\r' or '\n' && !quoted)
            {
                if (current == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                cells.Add(cell.ToString()); cell.Clear();
                yield return cells.ToArray(); cells.Clear();
            }
            else cell.Append(current);
        }
        Ensure(!quoted, "CSV must not end inside a quoted field.");
        if (cell.Length > 0 || cells.Count > 0) { cells.Add(cell.ToString()); yield return cells.ToArray(); }
    }
}
