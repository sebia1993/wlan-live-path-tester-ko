using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WlanLivePathTester.Core.Measurements;

namespace WlanLivePathTester.Core.Reporting;

public static class RepeatedMeasurementReportWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static RepeatedMeasurementReportDocument CreateDocument(
        IReadOnlyList<RepeatedMeasurementResult> results,
        string applicationVersion,
        DateTimeOffset? generatedAt = null)
    {
        ArgumentNullException.ThrowIfNull(results);
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationVersion);

        return new RepeatedMeasurementReportDocument(
            SchemaVersion: "1.0",
            GeneratedAt: generatedAt ?? DateTimeOffset.UtcNow,
            ApplicationName: "WLAN Live Path Tester KO",
            ApplicationVersion: applicationVersion,
            SensitiveValuesIncluded: false,
            DataHandlingStatement: "반복 측정 요약은 현재 PC에서 생성되며 자동 업로드, 텔레메트리 또는 온라인 분석을 수행하지 않습니다.",
            Measurements: results.Select(Map).ToArray(),
            Limitations:
            [
                "외부 결과는 회사 프록시와 외부 대상 서버·CDN을 포함한 체감 다운로드 성능입니다.",
                "중앙값과 변동계수는 입력된 반복 횟수 범위의 로컬 규칙이며 통계적 품질 보증이 아닙니다.",
                "캐시 관련 응답 헤더가 없다고 해서 프록시 또는 CDN 캐시가 없음을 증명하지 않습니다.",
                "보고서에는 대상 URL, 프록시 주소, PAC URL, SSID와 BSSID를 포함하지 않습니다.",
                "실제 사내 환경의 장애 원인은 프록시·방화벽·회선·외부 서버 운영 지표와 함께 확인해야 합니다."
            ]);
    }

    public static RepeatedMeasurementReportExportResult WriteAll(
        RepeatedMeasurementReportDocument report,
        string outputDirectory,
        string filePrefix = "WlanRepeatedMeasurement")
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);

        string fullDirectory = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(fullDirectory);
        string safePrefix = SensitiveDataRedactor.SafeFileComponent(
            filePrefix,
            "WlanRepeatedMeasurement");
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

        Dictionary<string, string> hashes = new(StringComparer.OrdinalIgnoreCase)
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

        return new RepeatedMeasurementReportExportResult(
            fullDirectory,
            jsonPath,
            csvPath,
            htmlPath,
            sha256Path,
            hashes);
    }

    public static string RenderJson(
        RepeatedMeasurementReportDocument report)
    {
        ArgumentNullException.ThrowIfNull(report);
        return JsonSerializer.Serialize(report, JsonOptions)
            + Environment.NewLine;
    }

    public static string RenderCsv(
        RepeatedMeasurementReportDocument report)
    {
        ArgumentNullException.ThrowIfNull(report);

        StringBuilder builder = new();
        builder.AppendLine("section,key,value");
        AddCsv(builder, "metadata", "schemaVersion", report.SchemaVersion);
        AddCsv(builder, "metadata", "generatedAt", Iso(report.GeneratedAt));
        AddCsv(builder, "metadata", "applicationName", report.ApplicationName);
        AddCsv(builder, "metadata", "applicationVersion", report.ApplicationVersion);
        AddCsv(
            builder,
            "metadata",
            "sensitiveValuesIncluded",
            FormatInvariant(report.SensitiveValuesIncluded));
        AddCsv(builder, "metadata", "dataHandling", report.DataHandlingStatement);

        for (int index = 0; index < report.Measurements.Count; index++)
        {
            RepeatedMeasurementReportEntry entry = report.Measurements[index];
            string section = $"repeatedMeasurement.{index + 1}";
            AddCsv(builder, section, "targetName", entry.TargetName);
            AddCsv(builder, section, "pathKind", entry.PathKind);
            AddCsv(builder, section, "startedAt", Iso(entry.StartedAt));
            AddCsv(builder, section, "completedAt", Iso(entry.CompletedAt));
            AddCsv(builder, section, "repeatCount", FormatInvariant(entry.RepeatCount));
            AddCsv(builder, section, "includeWarmup", FormatInvariant(entry.IncludeWarmup));
            AddCsv(builder, section, "delayMilliseconds", FormatInvariant(entry.DelayMilliseconds));
            AddCsv(builder, section, "plannedCount", FormatInvariant(entry.PlannedMeasurementCount));
            AddCsv(builder, section, "completedCount", FormatInvariant(entry.CompletedMeasurementCount));
            AddCsv(builder, section, "successCount", FormatInvariant(entry.SuccessfulMeasurementCount));
            AddCsv(builder, section, "failedCount", FormatInvariant(entry.FailedMeasurementCount));
            AddCsv(builder, section, "notCompletedCount", FormatInvariant(entry.NotCompletedMeasurementCount));
            AddCsv(builder, section, "totalBytesReceived", FormatInvariant(entry.TotalBytesReceived));
            AddCsv(builder, section, "medianMbps", FormatInvariant(entry.MedianMbps));
            AddCsv(builder, section, "minimumMbps", FormatInvariant(entry.MinimumMbps));
            AddCsv(builder, section, "maximumMbps", FormatInvariant(entry.MaximumMbps));
            AddCsv(builder, section, "meanMbps", FormatInvariant(entry.MeanMbps));
            AddCsv(builder, section, "standardDeviationMbps", FormatInvariant(entry.StandardDeviationMbps));
            AddCsv(builder, section, "coefficientOfVariation", FormatInvariant(entry.CoefficientOfVariation));
            AddCsv(builder, section, "representativeSequence", FormatInvariant(entry.RepresentativeSequence));
            AddCsv(builder, section, "cacheHitPossible", FormatInvariant(entry.CacheHitPossible));
            AddCsv(builder, section, "confidence", entry.Confidence);
            AddCsv(
                builder,
                section,
                "confidenceReasons",
                string.Join(" | ", entry.ConfidenceReasons));

            for (int runIndex = 0; runIndex < entry.Runs.Count; runIndex++)
            {
                RepeatedMeasurementReportRun run = entry.Runs[runIndex];
                string runSection = $"{section}.run.{runIndex + 1}";
                AddCsv(builder, runSection, "sequence", FormatInvariant(run.Sequence));
                AddCsv(builder, runSection, "isWarmup", FormatInvariant(run.IsWarmup));
                AddCsv(builder, runSection, "status", run.Status);
                AddCsv(builder, runSection, "startedAt", Iso(run.StartedAt));
                AddCsv(builder, runSection, "completedAt", Iso(run.CompletedAt));
                AddCsv(builder, runSection, "durationSeconds", FormatInvariant(run.DurationSeconds));
                AddCsv(builder, runSection, "bytesReceived", FormatInvariant(run.BytesReceived));
                AddCsv(builder, runSection, "averageMbps", FormatInvariant(run.AverageMbps));
                AddCsv(builder, runSection, "peakMbps", FormatInvariant(run.PeakMbps));
                AddCsv(builder, runSection, "ttfbMilliseconds", FormatInvariant(run.TimeToFirstByteMilliseconds));
                AddCsv(builder, runSection, "httpStatusCode", FormatInvariant(run.HttpStatusCode));
                AddCsv(builder, runSection, "proxyWasUsed", FormatInvariant(run.ProxyWasUsed));
                AddCsv(builder, runSection, "streamsRequested", FormatInvariant(run.StreamsRequested));
                AddCsv(builder, runSection, "streamsCompleted", FormatInvariant(run.StreamsCompleted));
                AddCsv(builder, runSection, "redirectCount", FormatInvariant(run.RedirectCount));
                AddCsv(builder, runSection, "cacheClassification", run.CacheClassification);
                AddCsv(builder, runSection, "confidence", run.Confidence);
                AddCsv(builder, runSection, "errorCode", run.ErrorCode ?? string.Empty);
            }
        }

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
        RepeatedMeasurementReportDocument report)
    {
        ArgumentNullException.ThrowIfNull(report);

        StringBuilder builder = new(capacity: 32 * 1024);
        builder.Append("<!doctype html><html lang=\"ko\"><head><meta charset=\"utf-8\">");
        builder.Append("<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">");
        builder.Append("<meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'none'; style-src 'unsafe-inline'; base-uri 'none'; form-action 'none'; frame-ancestors 'none'\">");
        builder.Append("<title>반복 다운로드 측정 보고서</title><style>");
        builder.Append("body{margin:0;background:#f4f6f8;color:#17202a;font:14px/1.55 system-ui,-apple-system,'Segoe UI',sans-serif}main{max-width:1180px;margin:auto;padding:28px}h1{font-size:28px;margin:0}h2{font-size:19px;margin:0 0 12px}.sub,.small{color:#566573}.card{background:#fff;border:1px solid #d8dde3;border-radius:12px;padding:18px;margin-top:16px;box-shadow:0 1px 2px rgba(0,0,0,.03)}.grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(220px,1fr));gap:10px}.metric{background:#f8fafb;border-radius:8px;padding:12px}.metric strong{display:block;font-size:19px}.badge{display:inline-block;border-radius:999px;padding:3px 9px;font-size:12px;background:#eaf2f8;color:#1b4f72}.high{background:#e8f6f3;color:#0e6251}.medium{background:#fff3cd;color:#7d6608}.low{background:#fdecea;color:#922b21}table{width:100%;border-collapse:collapse}th,td{padding:8px;border-bottom:1px solid #e8ebed;text-align:left;vertical-align:top}.scroll{overflow:auto}.privacy{background:#fff8e7;border-color:#e8ce8a}@media(max-width:640px){main{padding:16px}.grid{display:block}.metric{margin-top:8px}}@media print{body{background:#fff}.card{box-shadow:none;break-inside:avoid}main{max-width:none;padding:0}}</style></head><body><main>");
        builder.Append("<header><h1>반복 다운로드 측정 보고서</h1><div class=\"sub\">");
        Html(
            builder,
            report.GeneratedAt.ToLocalTime()
                .ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture));
        builder.Append(" · 스키마 ");
        Html(builder, report.SchemaVersion);
        builder.Append("</div></header><section class=\"card privacy\"><h2>데이터 처리</h2><p>");
        Html(builder, report.DataHandlingStatement);
        builder.Append("</p><p class=\"small\">대상 URL·프록시 주소·PAC URL·SSID·BSSID는 포함하지 않습니다. 이 HTML은 외부 리소스와 스크립트를 포함하지 않습니다.</p></section>");

        if (report.Measurements.Count == 0)
        {
            builder.Append("<section class=\"card\"><h2>측정 결과</h2><p>저장된 반복 측정 결과가 없습니다.</p></section>");
        }

        for (int index = 0; index < report.Measurements.Count; index++)
        {
            AppendMeasurementHtml(builder, report.Measurements[index], index + 1);
        }

        builder.Append("<section class=\"card\"><h2>판단 한계</h2><ul>");
        foreach (string limitation in report.Limitations)
        {
            builder.Append("<li>");
            Html(builder, limitation);
            builder.Append("</li>");
        }
        builder.Append("</ul></section><footer class=\"small\">현재 PC에서 생성한 로컬 보고서입니다.</footer></main></body></html>");
        return builder.ToString();
    }

    private static void AppendMeasurementHtml(
        StringBuilder builder,
        RepeatedMeasurementReportEntry entry,
        int index)
    {
        builder.Append("<section class=\"card\"><h2>");
        Html(builder, $"대상 {index}: {entry.TargetName}");
        builder.Append("</h2><p><span class=\"badge\">");
        Html(builder, entry.PathKind);
        builder.Append("</span> <span class=\"badge ");
        Html(builder, ConfidenceCss(entry.Confidence));
        builder.Append("\">신뢰도 ");
        Html(builder, entry.Confidence);
        builder.Append("</span></p><div class=\"grid\">");
        Metric(builder, "대표 중앙값", Mbps(entry.MedianMbps));
        Metric(builder, "최소~최대", $"{Mbps(entry.MinimumMbps)} ~ {Mbps(entry.MaximumMbps)}");
        Metric(builder, "변동계수", Percent(entry.CoefficientOfVariation));
        Metric(builder, "성공/계획", $"{entry.SuccessfulMeasurementCount}/{entry.PlannedMeasurementCount}");
        Metric(builder, "실제 총 수신량", Bytes(entry.TotalBytesReceived));
        Metric(builder, "캐시 적중 가능성", entry.CacheHitPossible ? "있음" : "확인되지 않음");
        builder.Append("</div><p class=\"small\"><strong>신뢰도 근거:</strong> ");
        Html(builder, string.Join(" · ", entry.ConfidenceReasons));
        builder.Append("</p><p class=\"small\">예열: ");
        Html(builder, entry.IncludeWarmup ? "사용" : "미사용");
        builder.Append(" · 본 측정 ");
        Html(builder, entry.RepeatCount.ToString(CultureInfo.InvariantCulture));
        builder.Append("회 · 측정 간 대기 ");
        Html(builder, entry.DelayMilliseconds.ToString(CultureInfo.InvariantCulture));
        builder.Append("ms · 실패 ");
        Html(builder, entry.FailedMeasurementCount.ToString(CultureInfo.InvariantCulture));
        builder.Append("회 · 미완료 ");
        Html(builder, entry.NotCompletedMeasurementCount.ToString(CultureInfo.InvariantCulture));
        builder.Append("회</p><div class=\"scroll\"><table><thead><tr><th>회차</th><th>구분</th><th>상태</th><th>평균</th><th>최고 구간</th><th>소요</th><th>수신량</th><th>HTTP</th><th>프록시</th><th>신뢰도</th></tr></thead><tbody>");

        foreach (RepeatedMeasurementReportRun run in entry.Runs)
        {
            builder.Append("<tr><td>");
            Html(builder, run.IsWarmup ? "-" : run.Sequence.ToString(CultureInfo.InvariantCulture));
            builder.Append("</td><td>");
            Html(builder, run.IsWarmup ? "예열" : "본 측정");
            builder.Append("</td><td>");
            Html(builder, run.Status);
            builder.Append("</td><td>");
            Html(builder, Mbps(run.AverageMbps));
            builder.Append("</td><td>");
            Html(builder, Mbps(run.PeakMbps));
            builder.Append("</td><td>");
            Html(builder, $"{run.DurationSeconds:F2}초");
            builder.Append("</td><td>");
            Html(builder, Bytes(run.BytesReceived));
            builder.Append("</td><td>");
            Html(builder, run.HttpStatusCode?.ToString(CultureInfo.InvariantCulture) ?? "없음");
            builder.Append("</td><td>");
            Html(
                builder,
                run.ProxyWasUsed switch
                {
                    true => "사용",
                    false => "미사용",
                    _ => "확인 불가"
                });
            builder.Append("</td><td>");
            Html(builder, run.Confidence);
            builder.Append("</td></tr>");
        }

        builder.Append("</tbody></table></div></section>");
    }

    private static RepeatedMeasurementReportEntry Map(
        RepeatedMeasurementResult result)
    {
        RepeatedMeasurementSummary summary = result.Summary;
        return new RepeatedMeasurementReportEntry(
            TargetName: SensitiveDataRedactor.RedactText(result.TargetName)
                ?? "반복 측정 대상",
            PathKind: result.PathKind.ToString(),
            StartedAt: result.StartedAt,
            CompletedAt: result.CompletedAt,
            RepeatCount: result.Plan.RepeatCount,
            IncludeWarmup: result.Plan.IncludeWarmup,
            DelayMilliseconds: result.Plan.DelayMilliseconds,
            PlannedMeasurementCount: summary.PlannedMeasurementCount,
            CompletedMeasurementCount: summary.CompletedMeasurementCount,
            SuccessfulMeasurementCount: summary.SuccessfulMeasurementCount,
            FailedMeasurementCount: summary.FailedMeasurementCount,
            NotCompletedMeasurementCount: summary.NotCompletedMeasurementCount,
            TotalBytesReceived: result.TotalBytesReceived,
            MedianMbps: summary.MedianMbps,
            MinimumMbps: summary.MinimumMbps,
            MaximumMbps: summary.MaximumMbps,
            MeanMbps: summary.MeanMbps,
            StandardDeviationMbps: summary.StandardDeviationMbps,
            CoefficientOfVariation: summary.CoefficientOfVariation,
            RepresentativeSequence: summary.RepresentativeSequence,
            CacheHitPossible: summary.CacheHitPossible,
            Confidence: summary.Confidence.ToString(),
            ConfidenceReasons: summary.ConfidenceReasons
                .Select(reason => SensitiveDataRedactor.RedactText(reason)
                    ?? string.Empty)
                .ToArray(),
            Runs: result.Runs.Select(MapRun).ToArray());
    }

    private static RepeatedMeasurementReportRun MapRun(
        RepeatedMeasurementRun run)
    {
        MeasurementQualityAssessment quality =
            MeasurementQualityEvaluator.Evaluate(run.Result);
        return new RepeatedMeasurementReportRun(
            Sequence: run.Sequence,
            IsWarmup: run.IsWarmup,
            Status: run.Result.Status.ToString(),
            StartedAt: run.Result.StartedAt,
            CompletedAt: run.Result.CompletedAt,
            DurationSeconds: Math.Max(0, run.Result.Duration.TotalSeconds),
            BytesReceived: run.Result.BytesReceived,
            AverageMbps: run.Result.AverageMbps,
            PeakMbps: run.Result.PeakMbps,
            TimeToFirstByteMilliseconds: run.Result.TimeToFirstByte?.TotalMilliseconds,
            HttpStatusCode: run.Result.HttpStatusCode,
            ProxyWasUsed: run.Result.ProxyWasUsed,
            StreamsRequested: run.Result.StreamsRequested,
            StreamsCompleted: run.Result.StreamsCompleted,
            RedirectCount: run.Result.RedirectCount,
            CacheClassification: quality.CacheClassification.ToString(),
            Confidence: quality.Confidence.ToString(),
            ErrorCode: SensitiveDataRedactor.RedactText(run.Result.ErrorCode));
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

    private static string FormatInvariant(object? value) =>
        value switch
        {
            null => string.Empty,
            IFormattable formattable =>
                formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture)
                ?? string.Empty
        };

    private static string Iso(DateTimeOffset value) =>
        value.ToString("O", CultureInfo.InvariantCulture);

    private static void Metric(
        StringBuilder builder,
        string label,
        string value)
    {
        builder.Append("<div class=\"metric\"><span class=\"small\">");
        Html(builder, label);
        builder.Append("</span><strong>");
        Html(builder, value);
        builder.Append("</strong></div>");
    }

    private static void Html(StringBuilder builder, string? value) =>
        builder.Append(WebUtility.HtmlEncode(value ?? string.Empty));

    private static string ConfidenceCss(string confidence) =>
        confidence.Equals("High", StringComparison.OrdinalIgnoreCase)
            ? "high"
            : confidence.Equals("Medium", StringComparison.OrdinalIgnoreCase)
                ? "medium"
                : confidence.Equals("Low", StringComparison.OrdinalIgnoreCase)
                    ? "low"
                    : string.Empty;

    private static string Mbps(double? value) =>
        value.HasValue ? $"{value.Value:F1} Mbps" : "계산 안 함";

    private static string Percent(double? value) =>
        value.HasValue
            ? value.Value.ToString("P1", CultureInfo.InvariantCulture)
            : "계산 안 함";

    private static string Bytes(long bytes) =>
        bytes >= 1024L * 1024 * 1024
            ? $"{bytes / 1024d / 1024 / 1024:F2} GiB"
            : $"{bytes / 1024d / 1024:F2} MiB";

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
                && !File.Exists(Path.Combine(
                    directory,
                    candidate + "_SHA256SUMS.txt")))
            {
                return candidate;
            }
        }

        throw new IOException("사용 가능한 반복 측정 보고서 파일 이름을 만들지 못했습니다.");
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
