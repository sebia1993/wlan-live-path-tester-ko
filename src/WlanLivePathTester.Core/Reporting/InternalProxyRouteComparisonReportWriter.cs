using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using WlanLivePathTester.Core.NetworkEnvironment;
using WlanLivePathTester.Core.Proxy;
using WlanLivePathTester.Core.Routing;

namespace WlanLivePathTester.Core.Reporting;

public sealed record InternalProxyRouteComparisonReportDocument(
    string SchemaVersion,
    DateTimeOffset GeneratedAt,
    string ApplicationName,
    string ApplicationVersion,
    bool SensitiveValuesIncluded,
    string DataHandlingStatement,
    InternalProxyRouteComparisonReportSection Comparison,
    IReadOnlyList<InternalProxyRouteComparisonReportProxyEntry>
        ProxyEntries,
    IReadOnlyList<InternalProxyRouteComparisonReportIssue> ParseIssues,
    InternalProxyRouteComparisonReportFinding Finding,
    IReadOnlyList<string> Limitations);

public sealed record InternalProxyRouteComparisonReportSection(
    string Status,
    string Relation,
    string Code,
    string InternalRouteStatus,
    string ProxyAnalysisStatus,
    string InternalInterfaceFingerprint,
    string InternalInterfaceCategory,
    IReadOnlyList<string> ProxyInterfaceFingerprints,
    IReadOnlyList<string> ProxyInterfaceCategories,
    int ProxyEndpointCount,
    int SuccessfulProxyRouteCount,
    int DirectDirectiveCount,
    bool ProxyAnalysisWasTruncated,
    bool ExactIdentityComparisonPerformed,
    bool HasCompleteComparableEvidence,
    string Message,
    string Interpretation,
    string Limitation,
    string NextStep);

public sealed record InternalProxyRouteComparisonReportProxyEntry(
    int Sequence,
    string Kind,
    string SourceSyntax,
    string Scope,
    int? Port,
    string HostFingerprint,
    string Status,
    string SelectedInterfaceFingerprint,
    string SelectedInterfaceCategory,
    string SelectedInterfaceOperationalState,
    string WlanCorrelationStatus,
    bool NetworkLookupPerformed);

public sealed record InternalProxyRouteComparisonReportIssue(
    int SegmentIndex,
    string Severity,
    string Code);

public sealed record InternalProxyRouteComparisonReportFinding(
    string Code,
    string Severity,
    string Title,
    string Evidence,
    string Interpretation,
    string Limitation,
    string NextStep);

public sealed record InternalProxyRouteComparisonReportExportResult(
    string OutputDirectory,
    string JsonPath,
    string CsvPath,
    string HtmlPath,
    string Sha256Path,
    IReadOnlyDictionary<string, string> Sha256);

public static class InternalProxyRouteComparisonReportWriter
{
    private const string DefaultFilePrefix = "WlanRouteComparison";
    private const int MaximumNarrativeLength = 2048;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static readonly Regex GuidRegex = new(
        @"(?i)(?<![0-9a-f])\{?[0-9a-f]{8}-(?:[0-9a-f]{4}-){3}[0-9a-f]{12}\}?(?![0-9a-f])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex DnsNameRegex = new(
        @"(?i)(?<![a-z0-9-])(?:[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\.)+[a-z]{2,63}(?![a-z0-9-])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static InternalProxyRouteComparisonReportDocument
        CreateDocument(
            InternalProxyRouteComparisonResult comparison,
            ProxyEndpointRouteAnalysisResult proxyAnalysis,
            ReportFinding finding,
            string applicationVersion,
            DateTimeOffset? generatedAt = null)
    {
        ArgumentNullException.ThrowIfNull(comparison);
        ArgumentNullException.ThrowIfNull(proxyAnalysis);
        ArgumentNullException.ThrowIfNull(finding);
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationVersion);

        InternalProxyRouteComparisonReportSection safeComparison =
            MapComparison(comparison);
        InternalProxyRouteComparisonReportFinding safeFinding =
            MapFinding(finding);
        InternalProxyRouteComparisonReportProxyEntry[] safeEntries =
            proxyAnalysis.Entries
                .OrderBy(entry => entry.Sequence)
                .Select(MapProxyEntry)
                .ToArray();
        InternalProxyRouteComparisonReportIssue[] safeIssues =
            proxyAnalysis.ParseIssues
                .OrderBy(issue => issue.SegmentIndex)
                .ThenBy(issue => issue.Code, StringComparer.Ordinal)
                .Select(MapIssue)
                .ToArray();

        return new InternalProxyRouteComparisonReportDocument(
            SchemaVersion: "1.0",
            GeneratedAt: generatedAt ?? DateTimeOffset.UtcNow,
            ApplicationName: "WLAN Live Path Tester KO",
            ApplicationVersion: SanitizeNarrative(
                applicationVersion),
            SensitiveValuesIncluded: false,
            DataHandlingStatement:
                "내부 DIRECT·프록시 경로 비교 보고서는 현재 PC에서 생성되며 자동 업로드, 텔레메트리 또는 온라인 분석을 수행하지 않습니다.",
            Comparison: safeComparison,
            ProxyEntries: safeEntries,
            ParseIssues: safeIssues,
            Finding: safeFinding,
            Limitations:
            [
                "이 비교는 Windows가 선택한 첫 로컬 인터페이스만 확인하며 이후 사내 라우팅, 프록시, 인터넷 회선과 대상 서버 경로를 측정하지 않습니다.",
                "Ready는 내부 DIRECT와 프록시 후보의 첫 로컬 NIC가 같다는 뜻이며 서비스 품질 정상 판정이 아닙니다.",
                "Diverged는 VPN·터널·유선 우선순위 또는 의도된 분할 라우팅일 수 있으므로 장애로 단정하지 않습니다.",
                "프록시 서버의 CPU, 세션, 큐, 인증, 캐시, 정책 로그와 클러스터 상태에는 접근하지 않습니다.",
                "호스트와 인터페이스 지문은 SHA-256 앞 10자의 표시값이며 정확한 NIC 판정은 같은 실행 세션의 전체 GUID로만 수행합니다.",
                "마스킹은 보조 수단이므로 회사 밖으로 공유하기 전에 JSON·CSV·HTML 내용을 사용자가 다시 확인해야 합니다."
            ]);
    }

    public static InternalProxyRouteComparisonReportExportResult
        WriteAll(
            InternalProxyRouteComparisonReportDocument report,
            string outputDirectory,
            string filePrefix = DefaultFilePrefix)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);

        string fullDirectory = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(fullDirectory);
        string safePrefix = SensitiveDataRedactor.SafeFileComponent(
            filePrefix,
            DefaultFilePrefix);
        string timestamp = report.GeneratedAt.ToLocalTime()
            .ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        string baseName = GetAvailableBaseName(
            fullDirectory,
            $"{safePrefix}_{timestamp}");

        string jsonPath = Path.Combine(fullDirectory, baseName + ".json");
        string csvPath = Path.Combine(fullDirectory, baseName + ".csv");
        string htmlPath = Path.Combine(fullDirectory, baseName + ".html");
        string sha256Path = Path.Combine(
            fullDirectory,
            baseName + "_SHA256SUMS.txt");

        WriteAtomic(jsonPath, RenderJson(report), new UTF8Encoding(false));
        WriteAtomic(csvPath, RenderCsv(report), new UTF8Encoding(true));
        WriteAtomic(htmlPath, RenderHtml(report), new UTF8Encoding(false));

        Dictionary<string, string> hashes = new(
            StringComparer.OrdinalIgnoreCase)
        {
            [Path.GetFileName(jsonPath)] = ComputeSha256(jsonPath),
            [Path.GetFileName(csvPath)] = ComputeSha256(csvPath),
            [Path.GetFileName(htmlPath)] = ComputeSha256(htmlPath)
        };
        string checksumText = string.Join(
            Environment.NewLine,
            hashes.OrderBy(
                    pair => pair.Key,
                    StringComparer.OrdinalIgnoreCase)
                .Select(pair => $"{pair.Value}  {pair.Key}"))
            + Environment.NewLine;
        WriteAtomic(sha256Path, checksumText, new UTF8Encoding(false));

        return new InternalProxyRouteComparisonReportExportResult(
            OutputDirectory: fullDirectory,
            JsonPath: jsonPath,
            CsvPath: csvPath,
            HtmlPath: htmlPath,
            Sha256Path: sha256Path,
            Sha256: hashes);
    }

    public static string RenderJson(
        InternalProxyRouteComparisonReportDocument report)
    {
        ArgumentNullException.ThrowIfNull(report);
        return JsonSerializer.Serialize(report, JsonOptions)
            + Environment.NewLine;
    }

    public static string RenderCsv(
        InternalProxyRouteComparisonReportDocument report)
    {
        ArgumentNullException.ThrowIfNull(report);

        StringBuilder builder = new();
        builder.AppendLine("section,key,value");
        AddCsv(builder, "metadata", "schemaVersion", report.SchemaVersion);
        AddCsv(builder, "metadata", "generatedAt", Iso(report.GeneratedAt));
        AddCsv(builder, "metadata", "applicationName", report.ApplicationName);
        AddCsv(
            builder,
            "metadata",
            "applicationVersion",
            report.ApplicationVersion);
        AddCsv(
            builder,
            "metadata",
            "sensitiveValuesIncluded",
            Invariant(report.SensitiveValuesIncluded));
        AddCsv(
            builder,
            "metadata",
            "dataHandling",
            report.DataHandlingStatement);

        AddComparisonCsv(builder, report.Comparison);
        for (int index = 0; index < report.ProxyEntries.Count; index++)
        {
            AddProxyEntryCsv(
                builder,
                report.ProxyEntries[index],
                index + 1);
        }

        for (int index = 0; index < report.ParseIssues.Count; index++)
        {
            InternalProxyRouteComparisonReportIssue issue =
                report.ParseIssues[index];
            string section = $"parseIssue.{index + 1}";
            AddCsv(
                builder,
                section,
                "segmentIndex",
                Invariant(issue.SegmentIndex));
            AddCsv(builder, section, "severity", issue.Severity);
            AddCsv(builder, section, "code", issue.Code);
        }

        AddFindingCsv(builder, report.Finding);
        for (int index = 0; index < report.Limitations.Count; index++)
        {
            AddCsv(
                builder,
                "limitation",
                (index + 1).ToString(CultureInfo.InvariantCulture),
                report.Limitations[index]);
        }

        return builder.ToString();
    }

    public static string RenderHtml(
        InternalProxyRouteComparisonReportDocument report)
    {
        ArgumentNullException.ThrowIfNull(report);

        StringBuilder builder = new(capacity: 32 * 1024);
        builder.Append(
            "<!doctype html><html lang=\"ko\"><head><meta charset=\"utf-8\">");
        builder.Append(
            "<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">");
        builder.Append(
            "<meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'none'; style-src 'unsafe-inline'; base-uri 'none'; form-action 'none'; frame-ancestors 'none'\">");
        builder.Append(
            "<title>내부 DIRECT·프록시 경로 비교 보고서</title><style>");
        builder.Append(
            "body{margin:0;background:#f4f6f8;color:#17202a;font:14px/1.55 system-ui,-apple-system,'Segoe UI',sans-serif}main{max-width:1180px;margin:auto;padding:28px}h1{font-size:28px;margin:0}h2{font-size:19px;margin:0 0 12px}.sub,.small{color:#566573}.card{background:#fff;border:1px solid #d8dde3;border-radius:12px;padding:18px;margin-top:16px;box-shadow:0 1px 2px rgba(0,0,0,.03)}.grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(190px,1fr));gap:10px}.metric{background:#f8fafb;border-radius:8px;padding:12px}.metric strong{display:block;font-size:17px;overflow-wrap:anywhere}.badge{display:inline-block;border-radius:999px;padding:3px 9px;font-size:12px;background:#eaf2f8;color:#1b4f72}.ok{background:#e8f6f3;color:#0e6251}.warn{background:#fff3cd;color:#7d6608}.info{background:#eaf2f8;color:#1b4f72}.bad{background:#fdecea;color:#922b21}.privacy{background:#fff8e7;border-color:#e8ce8a}table{width:100%;border-collapse:collapse}th,td{padding:8px;border-bottom:1px solid #e8ebed;text-align:left;vertical-align:top}.scroll{overflow:auto}.nowrap{white-space:nowrap}.finding{border-left:5px solid #d6a900}@media(max-width:640px){main{padding:16px}.grid{display:block}.metric{margin-top:8px}}@media print{body{background:#fff}.card{box-shadow:none;break-inside:avoid}main{max-width:none;padding:0}}</style></head><body><main>");
        builder.Append(
            "<header><h1>내부 DIRECT ↔ 프록시 로컬 경로 비교</h1><div class=\"sub\">");
        Html(
            builder,
            report.GeneratedAt.ToLocalTime().ToString(
                "yyyy-MM-dd HH:mm:ss zzz",
                CultureInfo.InvariantCulture));
        builder.Append(" · 스키마 ");
        Html(builder, report.SchemaVersion);
        builder.Append("</div></header>");

        builder.Append(
            "<section class=\"card privacy\"><h2>데이터 처리</h2><p>");
        Html(builder, report.DataHandlingStatement);
        builder.Append(
            "</p><p class=\"small\">내부 URL·프록시 호스트·전체 인터페이스 GUID·이름·설명·IP·MAC·게이트웨이·DNS·SSID·BSSID를 포함하지 않습니다. 이 HTML은 외부 리소스와 스크립트를 사용하지 않습니다.</p></section>");

        AppendComparisonHtml(builder, report.Comparison);
        AppendProxyEntriesHtml(builder, report.ProxyEntries);
        AppendIssuesHtml(builder, report.ParseIssues);
        AppendFindingHtml(builder, report.Finding);

        builder.Append(
            "<section class=\"card\"><h2>판단 한계</h2><ul>");
        foreach (string limitation in report.Limitations)
        {
            builder.Append("<li>");
            Html(builder, limitation);
            builder.Append("</li>");
        }

        builder.Append(
            "</ul></section><footer class=\"small\">현재 PC에서 생성한 로컬 보고서입니다.</footer></main></body></html>");
        return builder.ToString();
    }

    private static InternalProxyRouteComparisonReportSection
        MapComparison(
            InternalProxyRouteComparisonResult comparison)
    {
        string status = Enum.IsDefined(comparison.Status)
            ? comparison.Status.ToString()
            : InternalProxyRouteComparisonStatus.Incomplete.ToString();
        string relation = Enum.IsDefined(comparison.Relation)
            ? comparison.Relation.ToString()
            : InternalProxyRouteRelation.Unknown.ToString();
        string code = Enum.IsDefined(comparison.Code)
            ? comparison.Code.ToString()
            : InternalProxyRouteComparisonCode
                .ProxyAnalysisIncomplete.ToString();
        string internalStatus = Enum.TryParse(
            comparison.InternalRouteStatus,
            ignoreCase: true,
            out DestinationRouteEvidenceStatus parsedInternalStatus)
            ? parsedInternalStatus.ToString()
            : "Unknown";
        string proxyStatus = Enum.TryParse(
            comparison.ProxyAnalysisStatus,
            ignoreCase: true,
            out ProxyEndpointRouteAnalysisStatus parsedProxyStatus)
            ? parsedProxyStatus.ToString()
            : "Unknown";

        return new InternalProxyRouteComparisonReportSection(
            Status: status,
            Relation: relation,
            Code: code,
            InternalRouteStatus: internalStatus,
            ProxyAnalysisStatus: proxyStatus,
            InternalInterfaceFingerprint: NormalizeFingerprint(
                comparison.InternalInterfaceFingerprint,
                allowNone: true),
            InternalInterfaceCategory: NormalizeAdapterCategory(
                comparison.InternalInterfaceCategory),
            ProxyInterfaceFingerprints:
                comparison.ProxyInterfaceFingerprints
                    .Select(value => NormalizeFingerprint(
                        value,
                        allowNone: false))
                    .Where(value => value != "확인 불가")
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(value => value, StringComparer.Ordinal)
                    .ToArray(),
            ProxyInterfaceCategories:
                comparison.ProxyInterfaceCategories
                    .Select(NormalizeAdapterCategory)
                    .Where(value => value != "확인 불가")
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(value => value, StringComparer.Ordinal)
                    .ToArray(),
            ProxyEndpointCount: Math.Max(
                0,
                comparison.ProxyEndpointCount),
            SuccessfulProxyRouteCount: Math.Max(
                0,
                comparison.SuccessfulProxyRouteCount),
            DirectDirectiveCount: Math.Max(
                0,
                comparison.DirectDirectiveCount),
            ProxyAnalysisWasTruncated:
                comparison.ProxyAnalysisWasTruncated,
            ExactIdentityComparisonPerformed:
                comparison.ExactIdentityComparisonPerformed,
            HasCompleteComparableEvidence: comparison.Status is
                InternalProxyRouteComparisonStatus.Ready
                or InternalProxyRouteComparisonStatus.Diverged,
            Message: SanitizeNarrative(comparison.Message),
            Interpretation: SanitizeNarrative(
                comparison.Interpretation),
            Limitation: SanitizeNarrative(comparison.Limitation),
            NextStep: SanitizeNarrative(comparison.NextStep));
    }

    private static InternalProxyRouteComparisonReportProxyEntry
        MapProxyEntry(ProxyEndpointRouteEntry entry)
    {
        bool direct = entry.IsDirect;
        return new InternalProxyRouteComparisonReportProxyEntry(
            Sequence: Math.Max(0, entry.Sequence),
            Kind: Enum.IsDefined(entry.Kind)
                ? entry.Kind.ToString()
                : "Unknown",
            SourceSyntax: Enum.IsDefined(entry.SourceSyntax)
                ? entry.SourceSyntax.ToString()
                : "Unknown",
            Scope: NormalizeScope(entry.Scope),
            Port: entry.Port is >= 1 and <= 65535
                ? entry.Port
                : null,
            HostFingerprint: NormalizeFingerprint(
                entry.HostFingerprint,
                allowNone: direct),
            Status: Enum.IsDefined(entry.Status)
                ? entry.Status.ToString()
                : ProxyEndpointRouteEntryStatus.Failed.ToString(),
            SelectedInterfaceFingerprint: NormalizeFingerprint(
                entry.SelectedInterfaceFingerprint,
                allowNone: true),
            SelectedInterfaceCategory: NormalizeAdapterCategory(
                entry.SelectedInterfaceCategory),
            SelectedInterfaceOperationalState:
                NormalizeOperationalState(
                    entry.SelectedInterfaceOperationalState),
            WlanCorrelationStatus: NormalizeWlanCorrelation(
                entry.WlanCorrelationStatus),
            NetworkLookupPerformed: !direct);
    }

    private static InternalProxyRouteComparisonReportIssue MapIssue(
        ProxyDirectiveIssue issue) =>
        new(
            SegmentIndex: Math.Max(0, issue.SegmentIndex),
            Severity: Enum.IsDefined(issue.Severity)
                ? issue.Severity.ToString()
                : ProxyDirectiveIssueSeverity.Error.ToString(),
            Code: NormalizeCode(issue.Code));

    private static InternalProxyRouteComparisonReportFinding MapFinding(
        ReportFinding finding) =>
        new(
            Code: NormalizeCode(finding.Code),
            Severity: NormalizeFindingSeverity(finding.Severity),
            Title: SanitizeNarrative(finding.Title),
            Evidence: SanitizeNarrative(finding.Evidence),
            Interpretation: SanitizeNarrative(
                finding.Interpretation),
            Limitation: SanitizeNarrative(finding.Limitation),
            NextStep: SanitizeNarrative(finding.NextStep));

    private static void AddComparisonCsv(
        StringBuilder builder,
        InternalProxyRouteComparisonReportSection comparison)
    {
        AddCsv(builder, "comparison", "status", comparison.Status);
        AddCsv(builder, "comparison", "relation", comparison.Relation);
        AddCsv(builder, "comparison", "code", comparison.Code);
        AddCsv(
            builder,
            "comparison",
            "internalRouteStatus",
            comparison.InternalRouteStatus);
        AddCsv(
            builder,
            "comparison",
            "proxyAnalysisStatus",
            comparison.ProxyAnalysisStatus);
        AddCsv(
            builder,
            "comparison",
            "internalInterfaceFingerprint",
            comparison.InternalInterfaceFingerprint);
        AddCsv(
            builder,
            "comparison",
            "internalInterfaceCategory",
            comparison.InternalInterfaceCategory);
        AddCsv(
            builder,
            "comparison",
            "proxyInterfaceFingerprints",
            string.Join(" | ", comparison.ProxyInterfaceFingerprints));
        AddCsv(
            builder,
            "comparison",
            "proxyInterfaceCategories",
            string.Join(" | ", comparison.ProxyInterfaceCategories));
        AddCsv(
            builder,
            "comparison",
            "proxyEndpointCount",
            Invariant(comparison.ProxyEndpointCount));
        AddCsv(
            builder,
            "comparison",
            "successfulProxyRouteCount",
            Invariant(comparison.SuccessfulProxyRouteCount));
        AddCsv(
            builder,
            "comparison",
            "directDirectiveCount",
            Invariant(comparison.DirectDirectiveCount));
        AddCsv(
            builder,
            "comparison",
            "proxyAnalysisWasTruncated",
            Invariant(comparison.ProxyAnalysisWasTruncated));
        AddCsv(
            builder,
            "comparison",
            "exactIdentityComparisonPerformed",
            Invariant(comparison.ExactIdentityComparisonPerformed));
        AddCsv(
            builder,
            "comparison",
            "hasCompleteComparableEvidence",
            Invariant(comparison.HasCompleteComparableEvidence));
        AddCsv(builder, "comparison", "message", comparison.Message);
        AddCsv(
            builder,
            "comparison",
            "interpretation",
            comparison.Interpretation);
        AddCsv(
            builder,
            "comparison",
            "limitation",
            comparison.Limitation);
        AddCsv(builder, "comparison", "nextStep", comparison.NextStep);
    }

    private static void AddProxyEntryCsv(
        StringBuilder builder,
        InternalProxyRouteComparisonReportProxyEntry entry,
        int index)
    {
        string section = $"proxyEntry.{index}";
        AddCsv(builder, section, "sequence", Invariant(entry.Sequence));
        AddCsv(builder, section, "kind", entry.Kind);
        AddCsv(builder, section, "sourceSyntax", entry.SourceSyntax);
        AddCsv(builder, section, "scope", entry.Scope);
        AddCsv(builder, section, "port", Invariant(entry.Port));
        AddCsv(
            builder,
            section,
            "hostFingerprint",
            entry.HostFingerprint);
        AddCsv(builder, section, "status", entry.Status);
        AddCsv(
            builder,
            section,
            "selectedInterfaceFingerprint",
            entry.SelectedInterfaceFingerprint);
        AddCsv(
            builder,
            section,
            "selectedInterfaceCategory",
            entry.SelectedInterfaceCategory);
        AddCsv(
            builder,
            section,
            "selectedInterfaceOperationalState",
            entry.SelectedInterfaceOperationalState);
        AddCsv(
            builder,
            section,
            "wlanCorrelationStatus",
            entry.WlanCorrelationStatus);
        AddCsv(
            builder,
            section,
            "networkLookupPerformed",
            Invariant(entry.NetworkLookupPerformed));
    }

    private static void AddFindingCsv(
        StringBuilder builder,
        InternalProxyRouteComparisonReportFinding finding)
    {
        AddCsv(builder, "finding", "code", finding.Code);
        AddCsv(builder, "finding", "severity", finding.Severity);
        AddCsv(builder, "finding", "title", finding.Title);
        AddCsv(builder, "finding", "evidence", finding.Evidence);
        AddCsv(
            builder,
            "finding",
            "interpretation",
            finding.Interpretation);
        AddCsv(
            builder,
            "finding",
            "limitation",
            finding.Limitation);
        AddCsv(builder, "finding", "nextStep", finding.NextStep);
    }

    private static void AppendComparisonHtml(
        StringBuilder builder,
        InternalProxyRouteComparisonReportSection comparison)
    {
        builder.Append(
            "<section class=\"card\"><h2>비교 판정</h2><p><span class=\"badge ");
        Html(builder, ComparisonCss(comparison.Status));
        builder.Append("\">");
        Html(builder, comparison.Status);
        builder.Append("</span> <span class=\"badge\">");
        Html(builder, comparison.Relation);
        builder.Append("</span> <span class=\"badge\">");
        Html(builder, comparison.Code);
        builder.Append("</span></p><div class=\"grid\">");
        Metric(
            builder,
            "내부 인터페이스",
            $"{comparison.InternalInterfaceCategory} / {comparison.InternalInterfaceFingerprint}");
        Metric(
            builder,
            "프록시 인터페이스 지문",
            comparison.ProxyInterfaceFingerprints.Count == 0
                ? "없음"
                : string.Join(", ",
                    comparison.ProxyInterfaceFingerprints));
        Metric(
            builder,
            "프록시 인터페이스 범주",
            comparison.ProxyInterfaceCategories.Count == 0
                ? "없음"
                : string.Join(", ",
                    comparison.ProxyInterfaceCategories));
        Metric(
            builder,
            "후보 / 성공 / DIRECT",
            $"{comparison.ProxyEndpointCount} / {comparison.SuccessfulProxyRouteCount} / {comparison.DirectDirectiveCount}");
        Metric(
            builder,
            "후보 잘림",
            comparison.ProxyAnalysisWasTruncated ? "있음" : "없음");
        Metric(
            builder,
            "전체 ID 정확 비교",
            comparison.ExactIdentityComparisonPerformed
                ? "수행"
                : "미수행");
        builder.Append("</div><p>");
        Html(builder, comparison.Message);
        builder.Append("</p><p><strong>해석:</strong> ");
        Html(builder, comparison.Interpretation);
        builder.Append("</p><p><strong>다음 확인:</strong> ");
        Html(builder, comparison.NextStep);
        builder.Append("</p><p class=\"small\"><strong>한계:</strong> ");
        Html(builder, comparison.Limitation);
        builder.Append("</p></section>");
    }

    private static void AppendProxyEntriesHtml(
        StringBuilder builder,
        IReadOnlyList<InternalProxyRouteComparisonReportProxyEntry>
            entries)
    {
        builder.Append(
            "<section class=\"card\"><h2>프록시 지시문과 로컬 경로</h2>");
        if (entries.Count == 0)
        {
            builder.Append("<p>저장된 프록시 후보가 없습니다.</p></section>");
            return;
        }

        builder.Append(
            "<div class=\"scroll\"><table><thead><tr><th>#</th><th>종류</th><th>범위</th><th>포트</th><th>호스트 지문</th><th>경로 상태</th><th>인터페이스</th><th>WLAN 상관</th><th>조회</th></tr></thead><tbody>");
        foreach (InternalProxyRouteComparisonReportProxyEntry entry
                 in entries)
        {
            builder.Append("<tr><td>");
            Html(builder, Invariant(entry.Sequence));
            builder.Append("</td><td>");
            Html(builder, entry.Kind);
            builder.Append("</td><td>");
            Html(builder, entry.Scope);
            builder.Append("</td><td>");
            Html(builder, Invariant(entry.Port));
            builder.Append("</td><td class=\"nowrap\">");
            Html(builder, entry.HostFingerprint);
            builder.Append("</td><td>");
            Html(builder, entry.Status);
            builder.Append("</td><td>");
            Html(
                builder,
                $"{entry.SelectedInterfaceCategory} / {entry.SelectedInterfaceFingerprint}");
            builder.Append("</td><td>");
            Html(builder, entry.WlanCorrelationStatus);
            builder.Append("</td><td>");
            Html(
                builder,
                entry.NetworkLookupPerformed ? "수행" : "없음");
            builder.Append("</td></tr>");
        }

        builder.Append("</tbody></table></div></section>");
    }

    private static void AppendIssuesHtml(
        StringBuilder builder,
        IReadOnlyList<InternalProxyRouteComparisonReportIssue> issues)
    {
        if (issues.Count == 0)
        {
            return;
        }

        builder.Append(
            "<section class=\"card\"><h2>프록시 문자열 경고</h2><ul>");
        foreach (InternalProxyRouteComparisonReportIssue issue in issues)
        {
            builder.Append("<li>구간 ");
            Html(builder, Invariant(issue.SegmentIndex));
            builder.Append(" · ");
            Html(builder, issue.Severity);
            builder.Append(" · ");
            Html(builder, issue.Code);
            builder.Append("</li>");
        }

        builder.Append("</ul></section>");
    }

    private static void AppendFindingHtml(
        StringBuilder builder,
        InternalProxyRouteComparisonReportFinding finding)
    {
        builder.Append(
            "<section class=\"card finding\"><h2>보고서 판정</h2><p><span class=\"badge ");
        Html(builder, FindingCss(finding.Severity));
        builder.Append("\">");
        Html(builder, finding.Severity);
        builder.Append("</span> <span class=\"badge\">");
        Html(builder, finding.Code);
        builder.Append("</span></p><h3>");
        Html(builder, finding.Title);
        builder.Append("</h3><p><strong>근거:</strong> ");
        Html(builder, finding.Evidence);
        builder.Append("</p><p><strong>해석:</strong> ");
        Html(builder, finding.Interpretation);
        builder.Append("</p><p><strong>다음 확인:</strong> ");
        Html(builder, finding.NextStep);
        builder.Append("</p><p class=\"small\"><strong>한계:</strong> ");
        Html(builder, finding.Limitation);
        builder.Append("</p></section>");
    }

    private static string ComparisonCss(string status) =>
        status switch
        {
            "Ready" => "ok",
            "Diverged" => "info",
            "Ambiguous" => "warn",
            _ => "bad"
        };

    private static string FindingCss(string severity) =>
        severity switch
        {
            "Information" => "info",
            "Warning" => "warn",
            _ => "bad"
        };

    private static void Metric(
        StringBuilder builder,
        string label,
        string value)
    {
        builder.Append(
            "<div class=\"metric\"><span class=\"small\">");
        Html(builder, label);
        builder.Append("</span><strong>");
        Html(builder, value);
        builder.Append("</strong></div>");
    }

    private static string NormalizeScope(string? value)
    {
        string candidate = (value ?? string.Empty)
            .Trim()
            .ToLowerInvariant();
        return candidate switch
        {
            "all" or "http" or "https" or "ftp"
                or "socks" or "socks4" or "socks5" => candidate,
            _ => "unknown"
        };
    }

    private static string NormalizeAdapterCategory(string? value) =>
        Enum.TryParse(
            value,
            ignoreCase: true,
            out NetworkAdapterCategory parsed)
            ? parsed.ToString()
            : "확인 불가";

    private static string NormalizeOperationalState(string? value) =>
        Enum.TryParse(
            value,
            ignoreCase: true,
            out NetworkAdapterOperationalState parsed)
            ? parsed.ToString()
            : "확인 불가";

    private static string NormalizeWlanCorrelation(string? value) =>
        Enum.TryParse(
            value,
            ignoreCase: true,
            out RouteWlanCorrelationStatus parsed)
            ? parsed.ToString()
            : RouteWlanCorrelationStatus.NotEvaluated.ToString();

    private static string NormalizeFindingSeverity(string? value) =>
        (value ?? string.Empty).Trim() switch
        {
            "Information" => "Information",
            "Warning" => "Warning",
            "Error" => "Error",
            _ => "Warning"
        };

    private static string NormalizeCode(string? value)
    {
        string candidate = (value ?? string.Empty)
            .Trim()
            .ToUpperInvariant();
        if (candidate.Length is < 1 or > 96
            || candidate.Any(character =>
                !(character is >= 'A' and <= 'Z'
                    or >= '0' and <= '9'
                    or '_')))
        {
            return "INVALID_CODE";
        }

        return candidate;
    }

    private static string NormalizeFingerprint(
        string? value,
        bool allowNone)
    {
        string candidate = (value ?? string.Empty)
            .Trim()
            .ToLowerInvariant();
        if (allowNone
            && (candidate.Length == 0
                || candidate.Equals(
                    "없음",
                    StringComparison.Ordinal)))
        {
            return "없음";
        }

        return candidate.Length == 10
               && candidate.All(character =>
                   character is >= '0' and <= '9'
                       or >= 'a' and <= 'f')
            ? candidate
            : "확인 불가";
    }

    private static string SanitizeNarrative(string? value)
    {
        string sanitized = SensitiveDataRedactor.RedactText(value)
            ?? string.Empty;
        sanitized = GuidRegex.Replace(
            sanitized,
            "[인터페이스 ID 마스킹됨]");
        sanitized = DnsNameRegex.Replace(
            sanitized,
            "[호스트 마스킹됨]");
        sanitized = sanitized
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Replace('\t', ' ')
            .Trim();
        return sanitized.Length <= MaximumNarrativeLength
            ? sanitized
            : sanitized[..(MaximumNarrativeLength - 3)] + "...";
    }

    private static void AddCsv(
        StringBuilder builder,
        string section,
        string key,
        string value)
    {
        builder.Append(Csv(section));
        builder.Append(',');
        builder.Append(Csv(key));
        builder.Append(',');
        builder.AppendLine(Csv(value));
    }

    private static string Csv(string value)
    {
        string safe = SensitiveDataRedactor.ProtectCsvFormula(value);
        return '"' + safe.Replace("\"", "\"\"") + '"';
    }

    private static string Invariant(object? value) =>
        value switch
        {
            null => string.Empty,
            IFormattable formattable => formattable.ToString(
                null,
                CultureInfo.InvariantCulture),
            _ => Convert.ToString(
                    value,
                    CultureInfo.InvariantCulture)
                ?? string.Empty
        };

    private static string Iso(DateTimeOffset value) =>
        value.ToString("O", CultureInfo.InvariantCulture);

    private static void Html(StringBuilder builder, string? value) =>
        builder.Append(WebUtility.HtmlEncode(value ?? string.Empty));

    private static string GetAvailableBaseName(
        string directory,
        string desired)
    {
        for (int suffix = 0; suffix <= 9999; suffix++)
        {
            string candidate = suffix == 0
                ? desired
                : $"{desired}_{suffix}";
            if (!File.Exists(Path.Combine(directory, candidate + ".json"))
                && !File.Exists(
                    Path.Combine(directory, candidate + ".csv"))
                && !File.Exists(
                    Path.Combine(directory, candidate + ".html"))
                && !File.Exists(Path.Combine(
                    directory,
                    candidate + "_SHA256SUMS.txt")))
            {
                return candidate;
            }
        }

        throw new IOException(
            "사용 가능한 내부·프록시 경로 비교 보고서 파일 이름을 만들지 못했습니다.");
    }

    private static void WriteAtomic(
        string destination,
        string content,
        Encoding encoding)
    {
        string directory = Path.GetDirectoryName(destination)
            ?? throw new InvalidOperationException(
                "보고서 출력 디렉터리를 확인할 수 없습니다.");
        string temporary = Path.Combine(
            directory,
            $".{Path.GetFileName(destination)}.{Guid.NewGuid():N}.tmp");

        try
        {
            File.WriteAllText(temporary, content, encoding);
            File.Move(temporary, destination, overwrite: false);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private static string ComputeSha256(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream))
            .ToLowerInvariant();
    }
}
