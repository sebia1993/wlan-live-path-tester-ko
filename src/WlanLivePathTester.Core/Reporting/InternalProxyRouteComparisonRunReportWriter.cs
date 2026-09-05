using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WlanLivePathTester.Core.Routing;

namespace WlanLivePathTester.Core.Reporting;

public static class InternalProxyRouteComparisonRunReportWriter
{
    private const string DefaultFilePrefix =
        "WlanRouteComparison";
    private const int MaximumApplicationVersionLength = 128;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static InternalProxyRouteComparisonRunReportDocument
        CreateDocument(
            InternalProxyRouteComparisonRunResult result,
            string applicationVersion,
            DateTimeOffset? generatedAt = null)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationVersion);

        return new InternalProxyRouteComparisonRunReportDocument(
            SchemaVersion: "1.0",
            GeneratedAt: generatedAt ?? DateTimeOffset.UtcNow,
            ApplicationName: "WLAN Live Path Tester KO",
            ApplicationVersion:
                SanitizeApplicationVersion(applicationVersion),
            SensitiveValuesIncluded: false,
            DataHandlingStatement:
                "이 보고서는 내부 DIRECT–프록시 Windows 로컬 경로 비교의 검증된 상태, 개수, Boolean, 고정 Finding과 짧은 비가역 지문만 현재 PC에 저장합니다. 자동 업로드, 텔레메트리 또는 온라인 분석을 수행하지 않습니다.",
            RouteComparison:
                InternalProxyRouteComparisonRunReportSnapshotMapper
                    .FromResult(result),
            Limitations:
            [
                "이 결과는 현재 PC에서 내부 DIRECT 대상과 적용 프록시 엔드포인트까지 선택되는 Windows 첫 로컬 인터페이스만 비교합니다.",
                "Ready는 첫 로컬 NIC가 같다는 뜻이며 내부 서비스, 프록시, 인터넷 회선 또는 대상 서버의 품질이 정상이라는 뜻이 아닙니다.",
                "Diverged는 VPN·터널·정적 경로·인터페이스 메트릭 또는 의도된 유선·무선 분할 정책일 수 있으며 단독 장애 증거가 아닙니다.",
                "호스트·인터페이스 지문은 SHA-256 앞 10자의 표시값이며 정확한 NIC 판정은 같은 실행 세션의 전체 Windows 인터페이스 GUID로만 수행합니다.",
                "프록시 서버의 CPU·세션·큐·인증·정책·캐시·클러스터 상태와 프록시 이후 인터넷 경로는 확인하지 않습니다.",
                "마스킹은 보조 수단이므로 회사 밖으로 공유하기 전에 JSON·CSV·HTML 내용을 사용자가 다시 검토해야 합니다."
            ]);
    }

    public static InternalProxyRouteComparisonRunReportExportResult
        WriteAll(
            InternalProxyRouteComparisonRunReportDocument report,
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

        string jsonPath = Path.Combine(
            fullDirectory,
            baseName + ".json");
        string csvPath = Path.Combine(
            fullDirectory,
            baseName + ".csv");
        string htmlPath = Path.Combine(
            fullDirectory,
            baseName + ".html");
        string sha256Path = Path.Combine(
            fullDirectory,
            baseName + "_SHA256SUMS.txt");

        WriteAtomic(
            jsonPath,
            RenderJson(report),
            new UTF8Encoding(
                encoderShouldEmitUTF8Identifier: false));
        WriteAtomic(
            csvPath,
            RenderCsv(report),
            new UTF8Encoding(
                encoderShouldEmitUTF8Identifier: true));
        WriteAtomic(
            htmlPath,
            RenderHtml(report),
            new UTF8Encoding(
                encoderShouldEmitUTF8Identifier: false));

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
        WriteAtomic(
            sha256Path,
            checksumText,
            new UTF8Encoding(
                encoderShouldEmitUTF8Identifier: false));

        return new InternalProxyRouteComparisonRunReportExportResult(
            OutputDirectory: fullDirectory,
            JsonPath: jsonPath,
            CsvPath: csvPath,
            HtmlPath: htmlPath,
            Sha256Path: sha256Path,
            Sha256: hashes);
    }

    public static string RenderJson(
        InternalProxyRouteComparisonRunReportDocument report)
    {
        ArgumentNullException.ThrowIfNull(report);
        return JsonSerializer.Serialize(report, JsonOptions)
            + Environment.NewLine;
    }

    public static string RenderCsv(
        InternalProxyRouteComparisonRunReportDocument report)
    {
        ArgumentNullException.ThrowIfNull(report);

        StringBuilder builder = new();
        builder.AppendLine("section,key,value");
        AddCsv(builder, "metadata", "schemaVersion", report.SchemaVersion);
        AddCsv(
            builder,
            "metadata",
            "generatedAt",
            Iso(report.GeneratedAt));
        AddCsv(
            builder,
            "metadata",
            "applicationName",
            report.ApplicationName);
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

        AddRunCsv(builder, report.RouteComparison);
        AddComparisonCsv(
            builder,
            report.RouteComparison.Comparison);
        for (int index = 0;
             index < report.RouteComparison.ProxyEntries.Count;
             index++)
        {
            AddProxyEntryCsv(
                builder,
                report.RouteComparison.ProxyEntries[index],
                index + 1);
        }

        AddFindingCsv(builder, report.RouteComparison.Finding);
        for (int index = 0; index < report.Limitations.Count; index++)
        {
            AddCsv(
                builder,
                "limitation",
                (index + 1).ToString(
                    CultureInfo.InvariantCulture),
                report.Limitations[index]);
        }

        return builder.ToString();
    }

    public static string RenderHtml(
        InternalProxyRouteComparisonRunReportDocument report)
    {
        ArgumentNullException.ThrowIfNull(report);

        InternalProxyRouteComparisonRunReportSnapshot snapshot =
            report.RouteComparison;
        StringBuilder builder = new(capacity: 36 * 1024);
        builder.Append(
            "<!doctype html><html lang=\"ko\"><head><meta charset=\"utf-8\">");
        builder.Append(
            "<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">");
        builder.Append(
            "<meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'none'; style-src 'unsafe-inline'; base-uri 'none'; form-action 'none'; frame-ancestors 'none'\">");
        builder.Append(
            "<title>내부 DIRECT–프록시 로컬 경로 비교</title><style>");
        builder.Append(
            "body{margin:0;background:#f4f6f8;color:#17202a;font:14px/1.55 system-ui,-apple-system,'Segoe UI',sans-serif}main{max-width:1180px;margin:auto;padding:28px}h1{font-size:27px;margin:0}h2{font-size:19px;margin:0 0 12px}h3{font-size:16px}.sub,.small{color:#566573}.card{background:#fff;border:1px solid #d8dde3;border-radius:12px;padding:18px;margin-top:16px;box-shadow:0 1px 2px rgba(0,0,0,.03)}.privacy{background:#fff8e7;border-color:#e8ce8a}.grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(180px,1fr));gap:10px}.metric{background:#f8fafb;border-radius:8px;padding:12px}.metric strong{display:block;font-size:16px;overflow-wrap:anywhere}.badge{display:inline-block;border-radius:999px;padding:4px 10px;font-size:12px;background:#eaf2f8;color:#1b4f72}.ready{background:#e8f6f3;color:#0e6251}.info{background:#eaf2f8;color:#1b4f72}.warn{background:#fff3cd;color:#7d6608}.bad{background:#fdecea;color:#922b21}.scroll{overflow:auto}table{width:100%;border-collapse:collapse}th,td{padding:8px;border-bottom:1px solid #e8ebed;text-align:left;vertical-align:top}.nowrap{white-space:nowrap}.finding{border-left:5px solid #d6a900}@media(max-width:640px){main{padding:16px}.grid{display:block}.metric{margin-top:8px}}@media print{body{background:#fff}.card{box-shadow:none;break-inside:avoid}main{max-width:none;padding:0}}</style></head><body><main>");
        builder.Append(
            "<header><h1>내부 DIRECT ↔ 프록시 로컬 경로 비교</h1><div class=\"sub\">");
        Html(
            builder,
            report.GeneratedAt.ToLocalTime().ToString(
                "yyyy-MM-dd HH:mm:ss zzz",
                CultureInfo.InvariantCulture));
        builder.Append(" · 애플리케이션 ");
        Html(builder, report.ApplicationVersion);
        builder.Append(" · 스키마 ");
        Html(builder, report.SchemaVersion);
        builder.Append("</div></header>");

        builder.Append(
            "<section class=\"card privacy\"><h2>데이터 처리</h2><p>");
        Html(builder, report.DataHandlingStatement);
        builder.Append(
            "</p><p class=\"small\">내부·외부 URL, 프록시 호스트·지시문, 전체 인터페이스 GUID·이름·설명, IP·MAC·게이트웨이·DNS·SSID·BSSID와 원본 경로 객체를 포함하지 않습니다.</p></section>");

        AppendRunSummaryHtml(builder, snapshot);
        AppendComparisonHtml(builder, snapshot.Comparison);
        AppendProxyEntriesHtml(builder, snapshot.ProxyEntries);
        AppendFindingHtml(builder, snapshot.Finding);

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

    private static void AddRunCsv(
        StringBuilder builder,
        InternalProxyRouteComparisonRunReportSnapshot snapshot)
    {
        AddCsv(builder, "run", "completedAt", Iso(snapshot.CompletedAt));
        AddCsv(builder, "run", "status", snapshot.RunStatus);
        AddCsv(
            builder,
            "run",
            "proxySourceKind",
            snapshot.ProxySourceKind);
        AddCsv(
            builder,
            "run",
            "proxySelectionStatus",
            snapshot.ProxySelectionStatus);
        AddCsv(
            builder,
            "run",
            "proxyPlanStatus",
            snapshot.ProxyPlanStatus);
        AddCsv(
            builder,
            "run",
            "proxyPlanCode",
            snapshot.ProxyPlanCode);
        AddCsv(
            builder,
            "run",
            "proxyExecutionStatus",
            snapshot.ProxyExecutionStatus);
        AddCsv(
            builder,
            "run",
            "proxyEndpointSourceKind",
            snapshot.ProxyEndpointSourceKind);
        AddCsv(
            builder,
            "run",
            "proxyDecision",
            snapshot.ProxyDecision);
        AddCsv(
            builder,
            "run",
            "targetScheme",
            snapshot.TargetScheme);
        AddCsv(
            builder,
            "run",
            "internalRouteStatus",
            snapshot.InternalRouteStatus);
        AddCsv(
            builder,
            "run",
            "proxyRouteStatus",
            snapshot.ProxyRouteStatus);
        AddCsv(
            builder,
            "run",
            "parsedProxyEndpointCount",
            Invariant(snapshot.ParsedProxyEndpointCount));
        AddCsv(
            builder,
            "run",
            "applicableProxyEndpointCount",
            Invariant(snapshot.ApplicableProxyEndpointCount));
        AddCsv(
            builder,
            "run",
            "analyzedProxyEndpointCount",
            Invariant(snapshot.AnalyzedProxyEndpointCount));
        AddCsv(
            builder,
            "run",
            "successfulProxyEndpointCount",
            Invariant(snapshot.SuccessfulProxyEndpointCount));
        AddCsv(
            builder,
            "run",
            "distinctProxyInterfaceCount",
            Invariant(snapshot.DistinctProxyInterfaceCount));
        AddCsv(
            builder,
            "run",
            "directPresent",
            Invariant(snapshot.DirectPresent));
        AddCsv(
            builder,
            "run",
            "directIsPrimary",
            Invariant(snapshot.DirectIsPrimary));
        AddCsv(
            builder,
            "run",
            "directFallback",
            Invariant(snapshot.DirectFallback));
        AddCsv(
            builder,
            "run",
            "proxyParseErrorsPresent",
            Invariant(snapshot.ProxyParseErrorsPresent));
        AddCsv(
            builder,
            "run",
            "expectedWlanIdentityAvailable",
            Invariant(snapshot.ExpectedWlanIdentityAvailable));
        AddCsv(
            builder,
            "run",
            "internalRouteReadPerformed",
            Invariant(snapshot.InternalRouteReadPerformed));
        AddCsv(
            builder,
            "run",
            "proxyRouteAnalysisPerformed",
            Invariant(snapshot.ProxyRouteAnalysisPerformed));
        AddCsv(
            builder,
            "run",
            "operationCompleted",
            Invariant(snapshot.OperationCompleted));
        AddCsv(
            builder,
            "run",
            "hasComparableResult",
            Invariant(snapshot.HasComparableResult));
    }

    private static void AddComparisonCsv(
        StringBuilder builder,
        InternalProxyRouteComparisonReportComparison? comparison)
    {
        AddCsv(
            builder,
            "comparison",
            "available",
            Invariant(comparison is not null));
        if (comparison is null)
        {
            return;
        }

        AddCsv(
            builder,
            "comparison",
            "evaluatedAt",
            Iso(comparison.EvaluatedAt));
        AddCsv(builder, "comparison", "status", comparison.Status);
        AddCsv(
            builder,
            "comparison",
            "relation",
            comparison.Relation);
        AddCsv(builder, "comparison", "code", comparison.Code);
        AddCsv(
            builder,
            "comparison",
            "internalRouteStatus",
            comparison.InternalRouteStatus);
        AddCsv(
            builder,
            "comparison",
            "proxyExecutionStatus",
            comparison.ProxyExecutionStatus);
        AddCsv(
            builder,
            "comparison",
            "proxyAnalysisStatus",
            comparison.ProxyAnalysisStatus);
        AddCsv(
            builder,
            "comparison",
            "proxySourceKind",
            comparison.ProxySourceKind);
        AddCsv(
            builder,
            "comparison",
            "proxyPlanCode",
            comparison.ProxyPlanCode);
        AddCsv(
            builder,
            "comparison",
            "internalInterfaceFingerprint",
            comparison.InternalInterfaceFingerprint
            ?? string.Empty);
        AddCsv(
            builder,
            "comparison",
            "internalInterfaceCategory",
            comparison.InternalInterfaceCategory
            ?? string.Empty);
        AddCsv(
            builder,
            "comparison",
            "proxyInterfaceFingerprints",
            string.Join(
                " | ",
                comparison.ProxyInterfaceFingerprints));
        AddCsv(
            builder,
            "comparison",
            "proxyInterfaceCategories",
            string.Join(
                " | ",
                comparison.ProxyInterfaceCategories));
        AddCsv(
            builder,
            "comparison",
            "proxyApplicableEndpointCount",
            Invariant(comparison.ProxyApplicableEndpointCount));
        AddCsv(
            builder,
            "comparison",
            "proxyAnalyzedEndpointCount",
            Invariant(comparison.ProxyAnalyzedEndpointCount));
        AddCsv(
            builder,
            "comparison",
            "proxySuccessfulEndpointCount",
            Invariant(comparison.ProxySuccessfulEndpointCount));
        AddCsv(
            builder,
            "comparison",
            "proxyDistinctInterfaceCount",
            Invariant(comparison.ProxyDistinctInterfaceCount));
        AddCsv(
            builder,
            "comparison",
            "proxySkippedAfterDirectCount",
            Invariant(comparison.ProxySkippedAfterDirectCount));
        AddCsv(
            builder,
            "comparison",
            "proxyDirectPresent",
            Invariant(comparison.ProxyDirectPresent));
        AddCsv(
            builder,
            "comparison",
            "proxyDirectIsPrimary",
            Invariant(comparison.ProxyDirectIsPrimary));
        AddCsv(
            builder,
            "comparison",
            "proxyDirectFallbackPresent",
            Invariant(comparison.ProxyDirectFallbackPresent));
        AddCsv(
            builder,
            "comparison",
            "proxyParseErrorsPresent",
            Invariant(comparison.ProxyParseErrorsPresent));
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
    }

    private static void AddProxyEntryCsv(
        StringBuilder builder,
        InternalProxyRouteComparisonReportProxyEntry entry,
        int index)
    {
        string section = $"proxyEntry.{index}";
        AddCsv(
            builder,
            section,
            "sequence",
            Invariant(entry.Sequence));
        AddCsv(
            builder,
            section,
            "appliesToScheme",
            entry.AppliesToScheme);
        AddCsv(builder, section, "transport", entry.Transport);
        AddCsv(builder, section, "port", Invariant(entry.Port));
        AddCsv(
            builder,
            section,
            "hostFingerprint",
            entry.HostFingerprint ?? string.Empty);
        AddCsv(
            builder,
            section,
            "routeStatus",
            entry.RouteStatus);
        AddCsv(
            builder,
            section,
            "wlanCorrelationStatus",
            entry.WlanCorrelationStatus);
        AddCsv(
            builder,
            section,
            "selectedInterfaceFingerprint",
            entry.SelectedInterfaceFingerprint
            ?? string.Empty);
        AddCsv(
            builder,
            section,
            "selectedInterfaceCategory",
            entry.SelectedInterfaceCategory
            ?? string.Empty);
        AddCsv(
            builder,
            section,
            "selectedInterfaceIsVirtual",
            Invariant(entry.SelectedInterfaceIsVirtual));
        AddCsv(
            builder,
            section,
            "selectedInterfaceIsVpn",
            Invariant(entry.SelectedInterfaceIsVpn));
        AddCsv(
            builder,
            section,
            "selectedInterfaceIsUp",
            Invariant(entry.SelectedInterfaceIsUp));
        AddCsv(
            builder,
            section,
            "selectedInterfaceHasDefaultGateway",
            Invariant(entry.SelectedInterfaceHasDefaultGateway));
        AddCsv(
            builder,
            section,
            "resolvedAddressCount",
            Invariant(entry.ResolvedAddressCount));
        AddCsv(
            builder,
            section,
            "successfulAddressCount",
            Invariant(entry.SuccessfulAddressCount));
        AddCsv(
            builder,
            section,
            "failedAddressCount",
            Invariant(entry.FailedAddressCount));
    }

    private static void AddFindingCsv(
        StringBuilder builder,
        InternalProxyRouteComparisonReportFinding finding)
    {
        AddCsv(builder, "finding", "code", finding.Code);
        AddCsv(
            builder,
            "finding",
            "severity",
            finding.Severity);
        AddCsv(builder, "finding", "title", finding.Title);
        AddCsv(
            builder,
            "finding",
            "evidence",
            finding.Evidence);
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
        AddCsv(
            builder,
            "finding",
            "nextStep",
            finding.NextStep);
    }

    private static void AppendRunSummaryHtml(
        StringBuilder builder,
        InternalProxyRouteComparisonRunReportSnapshot snapshot)
    {
        builder.Append(
            "<section class=\"card\"><h2>실행 요약</h2><p><span class=\"badge ");
        Html(builder, RunCss(snapshot.RunStatus));
        builder.Append("\">");
        Html(builder, snapshot.RunStatus);
        builder.Append("</span> <span class=\"badge\">");
        Html(builder, snapshot.ProxyDecision);
        builder.Append("</span></p><div class=\"grid\">");
        Metric(
            builder,
            "프록시 출처 / 계획",
            $"{snapshot.ProxySourceKind} / {snapshot.ProxyPlanCode}");
        Metric(
            builder,
            "프록시 실행 / 경로",
            $"{snapshot.ProxyExecutionStatus} / {snapshot.ProxyRouteStatus}");
        Metric(
            builder,
            "대상 스킴",
            snapshot.TargetScheme);
        Metric(
            builder,
            "후보 파싱 / 적용",
            $"{snapshot.ParsedProxyEndpointCount} / {snapshot.ApplicableProxyEndpointCount}");
        Metric(
            builder,
            "후보 분석 / 성공",
            $"{snapshot.AnalyzedProxyEndpointCount} / {snapshot.SuccessfulProxyEndpointCount}");
        Metric(
            builder,
            "DIRECT 존재 / 첫 경로 / fallback",
            $"{YesNo(snapshot.DirectPresent)} / {YesNo(snapshot.DirectIsPrimary)} / {YesNo(snapshot.DirectFallback)}");
        Metric(
            builder,
            "내부 / 프록시 단계",
            $"{Performed(snapshot.InternalRouteReadPerformed)} / {Performed(snapshot.ProxyRouteAnalysisPerformed)}");
        Metric(
            builder,
            "현재 WLAN 전체 ID",
            snapshot.ExpectedWlanIdentityAvailable
                ? "확인"
                : "미확인");
        builder.Append("</div></section>");
    }

    private static void AppendComparisonHtml(
        StringBuilder builder,
        InternalProxyRouteComparisonReportComparison? comparison)
    {
        builder.Append(
            "<section class=\"card\"><h2>정확 인터페이스 비교</h2>");
        if (comparison is null)
        {
            builder.Append(
                "<p>구조화 비교 결과가 없습니다. 실행 상태와 Finding을 확인하십시오.</p></section>");
            return;
        }

        builder.Append("<p><span class=\"badge ");
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
            $"{comparison.InternalInterfaceCategory ?? "없음"} / {comparison.InternalInterfaceFingerprint ?? "없음"}");
        Metric(
            builder,
            "프록시 인터페이스 범주",
            comparison.ProxyInterfaceCategories.Count == 0
                ? "없음"
                : string.Join(
                    ", ",
                    comparison.ProxyInterfaceCategories));
        Metric(
            builder,
            "프록시 인터페이스 지문",
            comparison.ProxyInterfaceFingerprints.Count == 0
                ? "없음"
                : string.Join(
                    ", ",
                    comparison.ProxyInterfaceFingerprints));
        Metric(
            builder,
            "적용 / 분석 / 성공 / distinct",
            $"{comparison.ProxyApplicableEndpointCount} / {comparison.ProxyAnalyzedEndpointCount} / {comparison.ProxySuccessfulEndpointCount} / {comparison.ProxyDistinctInterfaceCount}");
        Metric(
            builder,
            "DIRECT 이후 제외",
            comparison.ProxySkippedAfterDirectCount.ToString(
                CultureInfo.InvariantCulture));
        Metric(
            builder,
            "전체 ID 정확 비교",
            Performed(
                comparison.ExactIdentityComparisonPerformed));
        builder.Append("</div></section>");
    }

    private static void AppendProxyEntriesHtml(
        StringBuilder builder,
        IReadOnlyList<InternalProxyRouteComparisonReportProxyEntry>
            entries)
    {
        builder.Append(
            "<section class=\"card\"><h2>프록시 후보 로컬 경로</h2>");
        if (entries.Count == 0)
        {
            builder.Append(
                "<p>저장된 프록시 후보가 없습니다.</p></section>");
            return;
        }

        builder.Append(
            "<div class=\"scroll\"><table><thead><tr><th>#</th><th>종류</th><th>범위</th><th>포트</th><th>호스트 지문</th><th>경로</th><th>인터페이스</th><th>WLAN</th><th>주소 성공</th></tr></thead><tbody>");
        foreach (InternalProxyRouteComparisonReportProxyEntry entry
                 in entries)
        {
            builder.Append("<tr><td>");
            Html(builder, Invariant(entry.Sequence));
            builder.Append("</td><td>");
            Html(builder, entry.Transport);
            builder.Append("</td><td>");
            Html(builder, entry.AppliesToScheme);
            builder.Append("</td><td>");
            Html(builder, Invariant(entry.Port));
            builder.Append("</td><td class=\"nowrap\">");
            Html(builder, entry.HostFingerprint ?? "없음");
            builder.Append("</td><td>");
            Html(builder, entry.RouteStatus);
            builder.Append("</td><td>");
            Html(
                builder,
                $"{entry.SelectedInterfaceCategory ?? "없음"} / {entry.SelectedInterfaceFingerprint ?? "없음"}");
            builder.Append("</td><td>");
            Html(builder, entry.WlanCorrelationStatus);
            builder.Append("</td><td>");
            Html(
                builder,
                $"{entry.SuccessfulAddressCount}/{entry.ResolvedAddressCount}");
            builder.Append("</td></tr>");
        }

        builder.Append("</tbody></table></div></section>");
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
        builder.Append(
            "</p><p class=\"small\"><strong>한계:</strong> ");
        Html(builder, finding.Limitation);
        builder.Append("</p></section>");
    }

    private static string RunCss(string status) =>
        status switch
        {
            "Completed" => "ready",
            "DirectPathSelected" => "info",
            "Canceled" or "ProxySourceUnavailable" => "warn",
            _ => "bad"
        };

    private static string ComparisonCss(string status) =>
        status switch
        {
            "Ready" => "ready",
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

    private static string SanitizeApplicationVersion(string value)
    {
        string candidate = SensitiveDataRedactor.RedactText(value)
            ?? string.Empty;
        candidate = candidate
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Replace('\t', ' ')
            .Trim();
        if (candidate.Length == 0)
        {
            return "unknown";
        }

        return candidate.Length <= MaximumApplicationVersionLength
            ? candidate
            : candidate[..MaximumApplicationVersionLength];
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

    private static string Performed(bool value) =>
        value ? "수행" : "미수행";

    private static string YesNo(bool value) =>
        value ? "예" : "아니오";

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
            if (!File.Exists(
                    Path.Combine(directory, candidate + ".json"))
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
            "사용 가능한 경로 비교 보고서 파일 이름을 만들지 못했습니다.");
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
