using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using WlanLivePathTester.Core.Measurements;
using WlanLivePathTester.Core.Models;
using WlanLivePathTester.Core.Reporting;

namespace WlanLivePathTester.ReportSmoke;

internal static class RepeatedMeasurementReportTests
{
    [ModuleInitializer]
    internal static void Run()
    {
        RepeatedMeasurementResult repeated = CreateRepeatedResult();
        RepeatedMeasurementReportDocument document =
            RepeatedMeasurementReportWriter.CreateDocument(
                [repeated],
                "0.1.0-test",
                DateTimeOffset.UnixEpoch.AddHours(9));

        VerifiesJson(document);
        VerifiesCsv(document);
        VerifiesHtml(document);
        VerifiesLocalFiles(document);
        Console.WriteLine("PASS  반복 측정 JSON·CSV·HTML·SHA-256 보고서");
    }

    private static void VerifiesJson(
        RepeatedMeasurementReportDocument document)
    {
        string json = RepeatedMeasurementReportWriter.RenderJson(document);
        using JsonDocument parsed = JsonDocument.Parse(json);
        JsonElement measurement = parsed.RootElement
            .GetProperty("measurements")[0];

        Ensure(
            measurement.GetProperty("medianMbps").GetDouble() == 100,
            "JSON에 반복 중앙값을 숫자로 기록해야 합니다.");
        Ensure(
            measurement.GetProperty("successfulMeasurementCount").GetInt32()
                == 3,
            "JSON에 성공 본 측정 횟수를 기록해야 합니다.");
        Ensure(
            measurement.GetProperty("runs").GetArrayLength() == 4,
            "JSON에 예열과 본 측정 회차를 모두 기록해야 합니다.");
        Ensure(!json.Contains("example.invalid", StringComparison.OrdinalIgnoreCase),
            "반복 보고서 JSON에 대상 URL 호스트가 남으면 안 됩니다.");
        Ensure(!json.Contains("token=secret", StringComparison.Ordinal),
            "반복 보고서 JSON에 URL 쿼리가 남으면 안 됩니다.");
    }

    private static void VerifiesCsv(
        RepeatedMeasurementReportDocument document)
    {
        string csv = RepeatedMeasurementReportWriter.RenderCsv(document);
        string[] lines = csv.Split(
            ['\r', '\n'],
            StringSplitOptions.RemoveEmptyEntries);

        Ensure(lines.Length == 6,
            "CSV는 헤더, 요약 1행, 예열·본 측정 4행이어야 합니다.");
        Ensure(lines[1].Contains("\"summary\"", StringComparison.Ordinal),
            "CSV에 요약 행 구분이 필요합니다.");
        Ensure(lines.Skip(2).All(line =>
                line.Contains("\"run\"", StringComparison.Ordinal)),
            "CSV의 각 회차는 run 행이어야 합니다.");
        Ensure(csv.Contains("\"100\"", StringComparison.Ordinal),
            "CSV에 중앙값을 기록해야 합니다.");
        Ensure(csv.Contains("'=HYPERLINK", StringComparison.Ordinal),
            "CSV에서 대상 이름 수식 시작 문자를 비활성화해야 합니다.");
        Ensure(!csv.Contains("example.invalid", StringComparison.OrdinalIgnoreCase),
            "CSV에 대상 URL 호스트가 남으면 안 됩니다.");
    }

    private static void VerifiesHtml(
        RepeatedMeasurementReportDocument document)
    {
        string html = RepeatedMeasurementReportWriter.RenderHtml(document);

        Ensure(html.StartsWith("<!doctype html>", StringComparison.OrdinalIgnoreCase),
            "HTML5 doctype이 필요합니다.");
        Ensure(html.Contains("Content-Security-Policy", StringComparison.Ordinal),
            "HTML에 CSP가 필요합니다.");
        Ensure(html.Contains("대표 중앙값", StringComparison.Ordinal),
            "HTML에 반복 대표값이 필요합니다.");
        Ensure(html.Contains("변동계수", StringComparison.Ordinal),
            "HTML에 반복 편차가 필요합니다.");
        Ensure(!html.Contains("<script", StringComparison.OrdinalIgnoreCase),
            "HTML에 스크립트를 포함하면 안 됩니다.");
        Ensure(!html.Contains("<iframe", StringComparison.OrdinalIgnoreCase),
            "HTML에 iframe을 포함하면 안 됩니다.");
        Ensure(!html.Contains("example.invalid", StringComparison.OrdinalIgnoreCase),
            "HTML에 대상 URL 호스트가 남으면 안 됩니다.");
        Ensure(html.Contains("=HYPERLINK", StringComparison.Ordinal),
            "HTML은 대상 이름을 텍스트로 표시해야 합니다.");
    }

    private static void VerifiesLocalFiles(
        RepeatedMeasurementReportDocument document)
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "WlanRepeatedReportSmoke",
            Guid.NewGuid().ToString("N"));

        try
        {
            RepeatedMeasurementReportExportResult export =
                RepeatedMeasurementReportWriter.WriteAll(
                    document,
                    directory,
                    "반복 합성 보고서");

            string[] files =
            [
                export.JsonPath,
                export.CsvPath,
                export.HtmlPath,
                export.Sha256Path
            ];
            Ensure(files.All(File.Exists),
                "반복 보고서 네 파일을 모두 생성해야 합니다.");
            Ensure(export.Sha256.Count == 3,
                "JSON·CSV·HTML 해시 세 개가 필요합니다.");

            foreach ((string fileName, string expectedHash) in export.Sha256)
            {
                string path = Path.Combine(export.OutputDirectory, fileName);
                using FileStream stream = File.OpenRead(path);
                string actualHash = Convert.ToHexString(SHA256.HashData(stream))
                    .ToLowerInvariant();
                Ensure(actualHash == expectedHash,
                    $"SHA-256이 일치하지 않습니다: {fileName}");
            }
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private static RepeatedMeasurementResult CreateRepeatedResult()
    {
        RepeatedMeasurementPlan plan = new(
            RepeatCount: 3,
            IncludeWarmup: true,
            DelayMilliseconds: 500);
        RepeatedMeasurementRun[] runs =
        [
            new(0, true, CreateDownloadResult(900, cacheHit: true)),
            new(1, false, CreateDownloadResult(98, cacheHit: false)),
            new(2, false, CreateDownloadResult(100, cacheHit: false)),
            new(3, false, CreateDownloadResult(102, cacheHit: false))
        ];
        RepeatedMeasurementSummary summary =
            RepeatedMeasurementAggregator.Summarize(plan, runs);

        return new RepeatedMeasurementResult(
            TargetName: "=HYPERLINK(\"https://example.invalid/?token=secret\")",
            PathKind: NetworkPathKind.External,
            StartedAt: runs[0].Result.StartedAt,
            CompletedAt: runs[^1].Result.CompletedAt,
            Plan: plan,
            Runs: runs,
            Summary: summary);
    }

    private static DownloadMeasurementResult CreateDownloadResult(
        double mbps,
        bool cacheHit)
    {
        DateTimeOffset start = DateTimeOffset.UnixEpoch
            .AddHours(9)
            .AddSeconds(mbps);
        Dictionary<string, string> headers = new()
        {
            ["Content-Length"] = "104857600"
        };
        if (cacheHit)
        {
            headers["Age"] = "120";
        }

        return new DownloadMeasurementResult(
            TargetName: "원본 합성 대상",
            PathKind: NetworkPathKind.External,
            Status: MeasurementStatus.Success,
            StartedAt: start,
            CompletedAt: start.AddSeconds(8),
            BytesReceived: 100L * 1024 * 1024,
            AverageMbps: mbps,
            TimeToFirstByte: TimeSpan.FromMilliseconds(120),
            HttpStatusCode: 200,
            ProxyWasUsed: true,
            StreamsRequested: 1,
            StreamsCompleted: 1,
            RedirectCount: 0,
            FinalUrl: "https://example.invalid/file.bin?token=secret",
            Samples:
            [
                new ThroughputSample(1, TimeSpan.FromSeconds(1), 12_500_000, mbps),
                new ThroughputSample(1, TimeSpan.FromSeconds(2), 12_500_000, mbps)
            ],
            ResponseHeaders: headers,
            ErrorCode: null,
            Message: "합성 성공");
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
