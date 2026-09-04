using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using WlanLivePathTester.Core.NetworkEnvironment;
using WlanLivePathTester.Core.Routing;

namespace WlanLivePathTester.Core.Reporting;

public sealed record InternalProxyRouteComparisonReportInterface(
    string InterfaceFingerprint,
    string Category,
    bool? IsVirtual,
    bool? IsVpn,
    bool? IsUp,
    bool? HasDefaultGateway,
    bool? MatchesExpectedWlan);

public sealed record InternalProxyRouteComparisonReportDocument(
    string SchemaVersion,
    DateTimeOffset GeneratedAt,
    string ApplicationName,
    string ApplicationVersion,
    bool SensitiveValuesIncluded,
    string DataHandlingStatement,
    string Status,
    string Message,
    InternalProxyRouteComparisonReportInterface? InternalInterface,
    InternalProxyRouteComparisonReportInterface? ProxyInterface,
    string? ExpectedWlanInterfaceFingerprint,
    bool? SameLocalInterface,
    bool InternalEvidencePartial,
    bool ProxyEvidencePartial,
    bool ProxyDirectPathSelected,
    bool ProxyDirectFallbackPresent,
    int ProxyCandidateCount,
    int ProxySuccessfulCandidateCount,
    int ProxyDistinctInterfaceCount,
    bool AnyVirtualInterface,
    bool AnyVpnOrTunnelInterface,
    IReadOnlyList<ReportFinding> Findings,
    IReadOnlyList<string> Warnings,
    string Limitation);

public sealed record InternalProxyRouteComparisonReportExportResult(
    string OutputDirectory,
    string JsonPath,
    string CsvPath,
    string HtmlPath,
    string Sha256Path,
    IReadOnlyDictionary<string, string> Sha256);

public static class InternalProxyRouteComparisonFindingProvider
{
    public static IReadOnlyList<ReportFinding> Evaluate(
        InternalProxyRouteComparisonResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        ReportFinding primary = result.Status switch
        {
            InternalProxyRouteComparisonStatus.Ready => new ReportFinding(
                Code: "INTERNAL_PROXY_LOCAL_ROUTE_ALIGNED",
                Severity: "Information",
                Title: "내부 DIRECT와 프록시 로컬 경로 일치",
                Evidence: BuildEvidence(result),
                Interpretation: "내부 DIRECT 대상과 분석된 모든 프록시 후보가 같은 Windows 로컬 인터페이스 지문을 사용합니다.",
                Limitation: "같은 로컬 인터페이스는 내부 서비스, 프록시 또는 인터넷 성능이 정상이라는 뜻이 아닙니다.",
                NextStep: "내부·외부 처리량, 프록시 인증·HTTP 상태와 WLAN RSSI·PHY·로밍 근거를 같은 시점 기준으로 비교하십시오."),
            InternalProxyRouteComparisonStatus.Diverged => new ReportFinding(
                Code: "INTERNAL_PROXY_LOCAL_ROUTE_DIVERGED",
                Severity: "Warning",
                Title: "내부 DIRECT와 프록시 로컬 경로 분기",
                Evidence: BuildEvidence(result),
                Interpretation: "내부 DIRECT 대상과 프록시 엔드포인트가 서로 다른 Windows 로컬 인터페이스를 사용합니다.",
                Limitation: "경로 분기는 Windows 메트릭, 정적 경로, VPN·터널 또는 유선·무선 우선순위일 수 있으며 단독으로 장애를 확정하지 않습니다.",
                NextStep: "양쪽 인터페이스 범주와 WLAN 일치 여부를 확인하고 VPN, 인터페이스 메트릭, 정적 경로와 보안 에이전트 정책을 비교하십시오."),
            InternalProxyRouteComparisonStatus.Ambiguous => new ReportFinding(
                Code: "INTERNAL_PROXY_LOCAL_ROUTE_AMBIGUOUS",
                Severity: "Warning",
                Title: "내부 DIRECT–프록시 로컬 경로 근거 모호",
                Evidence: BuildEvidence(result),
                Interpretation: "내부 주소군 또는 프록시 후보가 여러 로컬 인터페이스로 나뉘거나 인터페이스 메타데이터가 충돌해 단일 비교 결론을 내리지 않았습니다.",
                Limitation: "수집 시점의 IPv4·IPv6, DNS 응답과 Windows 인터페이스 상태 변화가 같은 결과를 만들 수 있습니다.",
                NextStep: "IPv4·IPv6별 경로, 각 프록시 후보, VPN·유선·무선 연결 상태를 고정한 뒤 다시 수집하십시오."),
            _ => new ReportFinding(
                Code: "INTERNAL_PROXY_LOCAL_ROUTE_INCOMPLETE",
                Severity: "Information",
                Title: "내부 DIRECT–프록시 로컬 경로 비교 미완료",
                Evidence: BuildEvidence(result),
                Interpretation: "내부 경로, 프록시 경로 또는 fallback 후보의 근거가 부족해 같은 경로인지 다른 경로인지 결론 내리지 않았습니다.",
                Limitation: "외부 대상에서 DIRECT가 첫 경로이거나 일부 DNS·라우팅 판정이 실패한 경우에도 같은 상태가 됩니다.",
                NextStep: "내부 DIRECT 대상과 적용 프록시 후보의 로컬 경로를 모두 확인한 뒤 다시 비교하십시오.")
        };

        List<ReportFinding> findings = [primary];
        if (result.AnyVpnOrTunnelInterface)
        {
            findings.Add(new ReportFinding(
                Code: "LOCAL_ROUTE_VPN_OR_TUNNEL_PRESENT",
                Severity: "Information",
                Title: "로컬 경로에 VPN 또는 터널 포함",
                Evidence: "내부 DIRECT 또는 프록시 엔드포인트 경로 중 VPN·터널 범주가 확인됐습니다.",
                Interpretation: "split tunneling, 인터페이스 메트릭 또는 보안 에이전트 정책이 로컬 경로 선택에 영향을 줄 수 있습니다.",
                Limitation: "터널 포함 여부만으로 VPN 성능이나 정책 오류를 확정할 수 없습니다.",
                NextStep: "VPN 연결 전후와 회사 정책에 따른 인터페이스·정적 경로 변화를 비교하십시오."));
        }

        if (result.AnyVirtualInterface)
        {
            findings.Add(new ReportFinding(
                Code: "LOCAL_ROUTE_VIRTUAL_INTERFACE_PRESENT",
                Severity: "Information",
                Title: "로컬 경로에 가상 인터페이스 포함",
                Evidence: "내부 DIRECT 또는 프록시 엔드포인트 경로 중 가상 인터페이스가 확인됐습니다.",
                Interpretation: "Hyper-V, WSL, VMware, 보안 에이전트 또는 VPN 가상 NIC의 라우팅 영향 가능성이 있습니다.",
                Limitation: "가상 인터페이스 존재만으로 실제 트래픽이 해당 제품을 경유한다고 확정할 수 없습니다.",
                NextStep: "가상 NIC의 상태·메트릭·정적 경로를 확인하고 비활성화 전후 결과를 비교하십시오."));
        }

        return findings;
    }

    private static string BuildEvidence(
        InternalProxyRouteComparisonResult result) =>
        string.Join(
            " ",
            $"비교 상태는 {result.Status}입니다.",
            $"내부 경로 상태는 {result.InternalRouteStatus}, 프록시 경로 상태는 {result.ProxyRouteStatus}입니다.",
            $"프록시 분석 후보 {Math.Max(0, result.ProxyCandidateCount)}개 중 {Math.Max(0, result.ProxySuccessfulCandidateCount)}개가 성공했고 서로 다른 인터페이스는 {Math.Max(0, result.ProxyDistinctInterfaceCount)}개입니다.",
            result.SameLocalInterface.HasValue
                ? $"같은 로컬 인터페이스 여부는 {result.SameLocalInterface.Value}입니다."
                : "같은 로컬 인터페이스 여부는 판정하지 않았습니다.");
}

public static class InternalProxyRouteComparisonReportWriter
{
    private const string DefaultFilePrefix = "WlanInternalProxyRoute";
    private const int FingerprintLength =
        RouteInterfaceFingerprint.DisplayLength;

    private static readonly Regex GuidRegex = new(
        @"(?i)(?<![0-9a-f])\{?[0-9a-f]{8}-(?:[0-9a-f]{4}-){3}[0-9a-f]{12}\}?(?![0-9a-f])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static InternalProxyRouteComparisonReportDocument CreateDocument(
        InternalProxyRouteComparisonResult result,
        string applicationVersion,
        DateTimeOffset? generatedAt = null)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationVersion);

        return new InternalProxyRouteComparisonReportDocument(
            SchemaVersion: "1.0",
            GeneratedAt: generatedAt ?? DateTimeOffset.UtcNow,
            ApplicationName: "WLAN Live Path Tester KO",
            ApplicationVersion: SanitizeText(applicationVersion),
            SensitiveValuesIncluded: false,
            DataHandlingStatement: "내부 DIRECT–프록시 로컬 경로 비교 보고서는 현재 PC에서 생성되며 자동 업로드, 텔레메트리 또는 온라인 분석을 수행하지 않습니다.",
            Status: result.Status.ToString(),
            Message: SanitizeText(result.Message),
            InternalInterface: MapInterface(result.InternalInterface),
            ProxyInterface: MapInterface(result.ProxyInterface),
            ExpectedWlanInterfaceFingerprint: NormalizeFingerprint(
                result.ExpectedWlanInterfaceFingerprint),
            SameLocalInterface: result.SameLocalInterface,
            InternalEvidencePartial: result.InternalEvidencePartial,
            ProxyEvidencePartial: result.ProxyEvidencePartial,
            ProxyDirectPathSelected: result.ProxyDirectPathSelected,
            ProxyDirectFallbackPresent: result.ProxyDirectFallbackPresent,
            ProxyCandidateCount: Math.Max(0, result.ProxyCandidateCount),
            ProxySuccessfulCandidateCount: Math.Max(
                0,
                result.ProxySuccessfulCandidateCount),
            ProxyDistinctInterfaceCount: Math.Max(
                0,
                result.ProxyDistinctInterfaceCount),
            AnyVirtualInterface: result.AnyVirtualInterface,
            AnyVpnOrTunnelInterface: result.AnyVpnOrTunnelInterface,
            Findings: InternalProxyRouteComparisonFindingProvider
                .Evaluate(result)
                .Select(SanitizeFinding)
                .ToArray(),
            Warnings: result.Warnings
                .Select(SanitizeText)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.Ordinal)
                .ToArray(),
            Limitation: SanitizeText(result.Limitation));
    }

    public static InternalProxyRouteComparisonReportExportResult WriteAll(
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
            hashes.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
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
        AddCsv(builder, "metadata", "applicationVersion", report.ApplicationVersion);
        AddCsv(builder, "metadata", "sensitiveValuesIncluded", Invariant(report.SensitiveValuesIncluded));
        AddCsv(builder, "metadata", "dataHandling", report.DataHandlingStatement);
        AddCsv(builder, "comparison", "status", report.Status);
        AddCsv(builder, "comparison", "sameLocalInterface", Invariant(report.SameLocalInterface));
        AddCsv(builder, "comparison", "message", report.Message);
        AddCsv(builder, "comparison", "expectedWlanInterfaceFingerprint", report.ExpectedWlanInterfaceFingerprint ?? string.Empty);
        AddCsv(builder, "comparison", "internalEvidencePartial", Invariant(report.InternalEvidencePartial));
        AddCsv(builder, "comparison", "proxyEvidencePartial", Invariant(report.ProxyEvidencePartial));
        AddCsv(builder, "comparison", "proxyDirectPathSelected", Invariant(report.ProxyDirectPathSelected));
        AddCsv(builder, "comparison", "proxyDirectFallbackPresent", Invariant(report.ProxyDirectFallbackPresent));
        AddCsv(builder, "comparison", "proxyCandidateCount", Invariant(report.ProxyCandidateCount));
        AddCsv(builder, "comparison", "proxySuccessfulCandidateCount", Invariant(report.ProxySuccessfulCandidateCount));
        AddCsv(builder, "comparison", "proxyDistinctInterfaceCount", Invariant(report.ProxyDistinctInterfaceCount));
        AddCsv(builder, "comparison", "anyVirtualInterface", Invariant(report.AnyVirtualInterface));
        AddCsv(builder, "comparison", "anyVpnOrTunnelInterface", Invariant(report.AnyVpnOrTunnelInterface));
        AddInterfaceCsv(builder, "internalInterface", report.InternalInterface);
        AddInterfaceCsv(builder, "proxyInterface", report.ProxyInterface);

        for (int index = 0; index < report.Findings.Count; index++)
        {
            ReportFinding finding = report.Findings[index];
            string section = $"finding.{index + 1}";
            AddCsv(builder, section, "code", finding.Code);
            AddCsv(builder, section, "severity", finding.Severity);
            AddCsv(builder, section, "title", finding.Title);
            AddCsv(builder, section, "evidence", finding.Evidence);
            AddCsv(builder, section, "interpretation", finding.Interpretation);
            AddCsv(builder, section, "limitation", finding.Limitation);
            AddCsv(builder, section, "nextStep", finding.NextStep);
        }

        for (int index = 0; index < report.Warnings.Count; index++)
        {
            AddCsv(builder, "warning", (index + 1).ToString(CultureInfo.InvariantCulture), report.Warnings[index]);
        }

        AddCsv(builder, "limitation", "1", report.Limitation);
        return builder.ToString();
    }

    public static string RenderHtml(
        InternalProxyRouteComparisonReportDocument report)
    {
        ArgumentNullException.ThrowIfNull(report);
        StringBuilder builder = new(capacity: 24 * 1024);
        builder.Append("<!doctype html><html lang=\"ko\"><head><meta charset=\"utf-8\">");
        builder.Append("<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">");
        builder.Append("<meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'none'; style-src 'unsafe-inline'; base-uri 'none'; form-action 'none'; frame-ancestors 'none'\">");
        builder.Append("<title>내부 DIRECT–프록시 로컬 경로 비교</title><style>");
        builder.Append("body{margin:0;background:#f4f6f8;color:#17202a;font:14px/1.55 system-ui,-apple-system,'Segoe UI',sans-serif}main{max-width:1080px;margin:auto;padding:28px}h1{font-size:27px;margin:0}h2{font-size:19px;margin:0 0 12px}.sub,.small{color:#566573}.card{background:#fff;border:1px solid #d8dde3;border-radius:12px;padding:18px;margin-top:16px}.grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(190px,1fr));gap:10px}.metric{background:#f8fafb;border-radius:8px;padding:12px}.metric strong{display:block;font-size:17px}.badge{display:inline-block;border-radius:999px;padding:4px 10px;font-size:12px}.ready{background:#e8f6f3;color:#0e6251}.diverged,.ambiguous{background:#fdecea;color:#922b21}.incomplete{background:#fff3cd;color:#7d6608}.privacy{background:#fff8e7;border-color:#e8ce8a}table{width:100%;border-collapse:collapse}th,td{padding:8px;border-bottom:1px solid #e8ebed;text-align:left;vertical-align:top}.scroll{overflow:auto}@media(max-width:640px){main{padding:16px}.grid{display:block}.metric{margin-top:8px}}@media print{body{background:#fff}.card{break-inside:avoid}main{max-width:none;padding:0}}</style></head><body><main>");
        builder.Append("<header><h1>내부 DIRECT–프록시 로컬 경로 비교</h1><div class=\"sub\">");
        Html(builder, report.GeneratedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture));
        builder.Append(" · 스키마 ");
        Html(builder, report.SchemaVersion);
        builder.Append("</div></header>");
        builder.Append("<section class=\"card privacy\"><h2>데이터 처리</h2><p>");
        Html(builder, report.DataHandlingStatement);
        builder.Append("</p><p class=\"small\">내부 URL·프록시 호스트·인터페이스 GUID·이름·설명·IP·MAC·SSID·BSSID를 포함하지 않습니다.</p></section>");
        builder.Append("<section class=\"card\"><h2>비교 결과</h2><p><span class=\"badge ");
        Html(builder, report.Status.ToLowerInvariant());
        builder.Append("\">");
        Html(builder, report.Status);
        builder.Append("</span></p><p>");
        Html(builder, report.Message);
        builder.Append("</p><div class=\"grid\">");
        Metric(builder, "같은 인터페이스", report.SameLocalInterface.HasValue ? report.SameLocalInterface.Value.ToString() : "판정 안 함");
        Metric(builder, "프록시 후보", report.ProxyCandidateCount.ToString(CultureInfo.InvariantCulture));
        Metric(builder, "성공 후보", report.ProxySuccessfulCandidateCount.ToString(CultureInfo.InvariantCulture));
        Metric(builder, "서로 다른 인터페이스", report.ProxyDistinctInterfaceCount.ToString(CultureInfo.InvariantCulture));
        Metric(builder, "VPN·터널", report.AnyVpnOrTunnelInterface ? "포함" : "확인 안 됨");
        Metric(builder, "가상 인터페이스", report.AnyVirtualInterface ? "포함" : "확인 안 됨");
        builder.Append("</div></section>");
        AppendInterfaceHtml(builder, "내부 DIRECT 인터페이스", report.InternalInterface);
        AppendInterfaceHtml(builder, "프록시 인터페이스", report.ProxyInterface);
        builder.Append("<section class=\"card\"><h2>판정</h2>");
        foreach (ReportFinding finding in report.Findings)
        {
            builder.Append("<article><h3>");
            Html(builder, finding.Title);
            builder.Append(" <span class=\"small\">[");
            Html(builder, finding.Severity);
            builder.Append("]</span></h3><p><strong>근거:</strong> ");
            Html(builder, finding.Evidence);
            builder.Append("</p><p><strong>해석:</strong> ");
            Html(builder, finding.Interpretation);
            builder.Append("</p><p><strong>다음 확인:</strong> ");
            Html(builder, finding.NextStep);
            builder.Append("</p><p class=\"small\"><strong>한계:</strong> ");
            Html(builder, finding.Limitation);
            builder.Append("</p></article>");
        }
        builder.Append("</section>");
        if (report.Warnings.Count > 0)
        {
            builder.Append("<section class=\"card\"><h2>주의사항</h2><ul>");
            foreach (string warning in report.Warnings)
            {
                builder.Append("<li>");
                Html(builder, warning);
                builder.Append("</li>");
            }
            builder.Append("</ul></section>");
        }
        builder.Append("<section class=\"card\"><h2>판단 한계</h2><p>");
        Html(builder, report.Limitation);
        builder.Append("</p></section><footer class=\"small\">현재 PC에서 생성한 로컬 보고서입니다.</footer></main></body></html>");
        return builder.ToString();
    }

    private static InternalProxyRouteComparisonReportInterface? MapInterface(
        LocalRouteComparisonInterface? routeInterface)
    {
        if (routeInterface is null)
        {
            return null;
        }

        string? fingerprint = NormalizeFingerprint(
            routeInterface.InterfaceFingerprint);
        if (fingerprint is null)
        {
            return null;
        }

        return new InternalProxyRouteComparisonReportInterface(
            InterfaceFingerprint: fingerprint,
            Category: routeInterface.Category.ToString(),
            IsVirtual: routeInterface.IsVirtual,
            IsVpn: routeInterface.IsVpn,
            IsUp: routeInterface.IsUp,
            HasDefaultGateway: routeInterface.HasDefaultGateway,
            MatchesExpectedWlan: routeInterface.MatchesExpectedWlan);
    }

    private static ReportFinding SanitizeFinding(ReportFinding finding) =>
        new(
            Code: SanitizeCode(finding.Code),
            Severity: SanitizeCode(finding.Severity),
            Title: SanitizeText(finding.Title),
            Evidence: SanitizeText(finding.Evidence),
            Interpretation: SanitizeText(finding.Interpretation),
            Limitation: SanitizeText(finding.Limitation),
            NextStep: SanitizeText(finding.NextStep));

    private static string SanitizeCode(string? value)
    {
        string candidate = (value ?? string.Empty).Trim();
        return candidate.Length > 0
               && candidate.All(character =>
                   char.IsAsciiLetterOrDigit(character)
                   || character is '_' or '-')
            ? candidate
            : "INVALID";
    }

    private static string SanitizeText(string? value)
    {
        string withoutGuids = GuidRegex.Replace(
            value ?? string.Empty,
            "[인터페이스 ID 마스킹됨]");
        return SensitiveDataRedactor.RedactText(withoutGuids)
            ?? string.Empty;
    }

    private static string? NormalizeFingerprint(string? value)
    {
        string candidate = (value ?? string.Empty).Trim().ToLowerInvariant();
        return candidate.Length == FingerprintLength
               && candidate.All(character =>
                   character is >= '0' and <= '9'
                   || character is >= 'a' and <= 'f')
            ? candidate
            : null;
    }

    private static void AddInterfaceCsv(
        StringBuilder builder,
        string section,
        InternalProxyRouteComparisonReportInterface? routeInterface)
    {
        AddCsv(builder, section, "available", Invariant(routeInterface is not null));
        if (routeInterface is null)
        {
            return;
        }
        AddCsv(builder, section, "interfaceFingerprint", routeInterface.InterfaceFingerprint);
        AddCsv(builder, section, "category", routeInterface.Category);
        AddCsv(builder, section, "isVirtual", Invariant(routeInterface.IsVirtual));
        AddCsv(builder, section, "isVpn", Invariant(routeInterface.IsVpn));
        AddCsv(builder, section, "isUp", Invariant(routeInterface.IsUp));
        AddCsv(builder, section, "hasDefaultGateway", Invariant(routeInterface.HasDefaultGateway));
        AddCsv(builder, section, "matchesExpectedWlan", Invariant(routeInterface.MatchesExpectedWlan));
    }

    private static void AppendInterfaceHtml(
        StringBuilder builder,
        string title,
        InternalProxyRouteComparisonReportInterface? routeInterface)
    {
        builder.Append("<section class=\"card\"><h2>");
        Html(builder, title);
        builder.Append("</h2>");
        if (routeInterface is null)
        {
            builder.Append("<p>단일 인터페이스를 확인하지 못했습니다.</p></section>");
            return;
        }
        builder.Append("<div class=\"scroll\"><table><tbody>");
        TableRow(builder, "인터페이스 지문", routeInterface.InterfaceFingerprint);
        TableRow(builder, "범주", routeInterface.Category);
        TableRow(builder, "가상", Invariant(routeInterface.IsVirtual));
        TableRow(builder, "VPN", Invariant(routeInterface.IsVpn));
        TableRow(builder, "Up", Invariant(routeInterface.IsUp));
        TableRow(builder, "기본 게이트웨이", Invariant(routeInterface.HasDefaultGateway));
        TableRow(builder, "현재 WLAN 일치", Invariant(routeInterface.MatchesExpectedWlan));
        builder.Append("</tbody></table></div></section>");
    }

    private static void TableRow(StringBuilder builder, string key, string value)
    {
        builder.Append("<tr><th>");
        Html(builder, key);
        builder.Append("</th><td>");
        Html(builder, string.IsNullOrWhiteSpace(value) ? "확인 안 됨" : value);
        builder.Append("</td></tr>");
    }

    private static void Metric(StringBuilder builder, string label, string value)
    {
        builder.Append("<div class=\"metric\"><span class=\"small\">");
        Html(builder, label);
        builder.Append("</span><strong>");
        Html(builder, value);
        builder.Append("</strong></div>");
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
            _ => Convert.ToString(value, CultureInfo.InvariantCulture)
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
                && !File.Exists(Path.Combine(directory, candidate + ".csv"))
                && !File.Exists(Path.Combine(directory, candidate + ".html"))
                && !File.Exists(Path.Combine(directory, candidate + "_SHA256SUMS.txt")))
            {
                return candidate;
            }
        }

        throw new IOException("사용 가능한 로컬 경로 비교 보고서 파일 이름을 만들지 못했습니다.");
    }

    private static void WriteAtomic(
        string destination,
        string content,
        Encoding encoding)
    {
        string directory = Path.GetDirectoryName(destination)
            ?? throw new InvalidOperationException("보고서 출력 디렉터리를 확인할 수 없습니다.");
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
