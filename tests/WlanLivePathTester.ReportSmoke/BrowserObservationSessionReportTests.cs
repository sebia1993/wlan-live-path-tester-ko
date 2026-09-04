using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using WlanLivePathTester.Core.Models;
using WlanLivePathTester.Core.Observation;
using WlanLivePathTester.Core.Reporting;

namespace WlanLivePathTester.ReportSmoke;

internal static class BrowserObservationSessionReportTests
{
    private const string SecretInterfaceId =
        "A1B2C3D4-E5F6-47A8-9123-1234567890AB";
    private const string SecretBssid = "AA:BB:CC:DD:EE:FF";

#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Run()
    {
        BrowserObservationSessionReportDocument document =
            BrowserObservationSessionReportWriter.CreateDocument(
                CreateResult(),
                "0.1.0-test",
                DateTimeOffset.UnixEpoch.AddHours(9));

        VerifyDocument(document);
        VerifyJson(document);
        VerifyCsv(document);
        VerifyHtml(document);
        VerifyLocalFiles(document);
        Console.WriteLine("PASS browser observation termination JSON CSV HTML SHA-256 report tests");
    }

    private static void VerifyDocument(
        BrowserObservationSessionReportDocument document)
    {
        Ensure(document.Status == "PartialSuccess",
            "관찰 상태를 구조화해야 합니다.");
        Ensure(document.TerminationReason == "AdapterChanged",
            "관찰 종료 원인을 구조화해야 합니다.");
        Ensure(document.Summary?.Samples.Count == 2,
            "관찰 샘플 두 개를 구조화해야 합니다.");
        Ensure(document.Summary?.BssidChangeCount == 1,
            "BSSID 원문 없이 변경 횟수만 기록해야 합니다.");
        Ensure(document.Summary?.AdapterChangeCount == 1,
            "어댑터 변경 횟수를 기록해야 합니다.");
    }

    private static void VerifyJson(
        BrowserObservationSessionReportDocument document)
    {
        string json = BrowserObservationSessionReportWriter.RenderJson(
            document);
        using JsonDocument parsed = JsonDocument.Parse(json);

        Ensure(parsed.RootElement
                .GetProperty("terminationReason")
                .GetString() == "AdapterChanged",
            "JSON에 구조화 종료 원인이 필요합니다.");
        Ensure(parsed.RootElement
                .GetProperty("summary")
                .GetProperty("samples")
                .GetArrayLength() == 2,
            "JSON에 두 시간축 샘플이 필요합니다.");
        AssertSensitiveValuesAbsent(json, "JSON");
    }

    private static void VerifyCsv(
        BrowserObservationSessionReportDocument document)
    {
        string csv = BrowserObservationSessionReportWriter.RenderCsv(
            document);

        Ensure(csv.StartsWith("section,key,value", StringComparison.Ordinal),
            "CSV는 section,key,value 스키마를 사용해야 합니다.");
        Ensure(csv.Contains(
                "\"observation\",\"terminationReason\",\"AdapterChanged\"",
                StringComparison.Ordinal),
            "CSV에 구조화 종료 원인 행이 필요합니다.");
        Ensure(csv.Contains(
                "\"sample.1\",\"adjustedReceiveMbps\"",
                StringComparison.Ordinal),
            "CSV에 시간축 샘플 행이 필요합니다.");
        AssertSensitiveValuesAbsent(csv, "CSV");
    }

    private static void VerifyHtml(
        BrowserObservationSessionReportDocument document)
    {
        string html = BrowserObservationSessionReportWriter.RenderHtml(
            document);

        Ensure(html.StartsWith("<!doctype html>", StringComparison.OrdinalIgnoreCase),
            "HTML5 doctype이 필요합니다.");
        Ensure(html.Contains("Content-Security-Policy", StringComparison.Ordinal),
            "HTML에 CSP가 필요합니다.");
        Ensure(html.Contains("AdapterChanged", StringComparison.Ordinal),
            "HTML에 종료 원인이 필요합니다.");
        Ensure(html.Contains("시간축 샘플", StringComparison.Ordinal),
            "HTML에 시간축 샘플 표가 필요합니다.");
        Ensure(!html.Contains("<script", StringComparison.OrdinalIgnoreCase),
            "HTML에 script를 포함하면 안 됩니다.");
        Ensure(!html.Contains("<iframe", StringComparison.OrdinalIgnoreCase),
            "HTML에 iframe을 포함하면 안 됩니다.");
        Ensure(!html.Contains("<link", StringComparison.OrdinalIgnoreCase),
            "HTML에 외부 stylesheet 링크를 포함하면 안 됩니다.");
        AssertSensitiveValuesAbsent(html, "HTML");
    }

    private static void VerifyLocalFiles(
        BrowserObservationSessionReportDocument document)
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "WlanObservationSessionReportSmoke",
            Guid.NewGuid().ToString("N"));

        try
        {
            BrowserObservationSessionReportExportResult export =
                BrowserObservationSessionReportWriter.WriteAll(
                    document,
                    directory,
                    "합성 관찰 보고서");

            string[] files =
            [
                export.JsonPath,
                export.CsvPath,
                export.HtmlPath,
                export.Sha256Path
            ];
            Ensure(files.All(File.Exists),
                "관찰 보고서 네 파일을 모두 생성해야 합니다.");
            Ensure(export.Sha256.Count == 3,
                "JSON CSV HTML 해시 세 개가 필요합니다.");

            foreach ((string fileName, string expectedHash) in export.Sha256)
            {
                string path = Path.Combine(export.OutputDirectory, fileName);
                using FileStream stream = File.OpenRead(path);
                string actualHash = Convert.ToHexString(SHA256.HashData(stream))
                    .ToLowerInvariant();
                Ensure(actualHash == expectedHash,
                    $"SHA-256이 일치하지 않습니다: {fileName}");
                AssertSensitiveValuesAbsent(
                    File.ReadAllText(path),
                    fileName);
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

    private static BrowserObservationResult CreateResult()
    {
        DateTimeOffset startedAt = DateTimeOffset.UnixEpoch
            .AddHours(9);
        BrowserObservationSample[] samples =
        [
            Sample(
                startedAt.AddSeconds(1),
                isBaseline: true,
                bssidChanged: false,
                adapterChanged: false,
                note: "기준 샘플"),
            Sample(
                startedAt.AddSeconds(2),
                isBaseline: false,
                bssidChanged: true,
                adapterChanged: true,
                note: "user@example.invalid 10.20.30.40 https://corp.example.invalid/secret")
        ];
        BrowserObservationSummary summary = new(
            StartedAt: startedAt,
            CompletedAt: startedAt.AddSeconds(2),
            ObservedDuration: TimeSpan.FromSeconds(2),
            BaselineReceiveMbps: 0.5,
            AverageAdjustedReceiveMbps: 80,
            PeakAdjustedReceiveMbps: 100,
            TotalReceiveBytes: 20_000_000,
            ActiveSampleCount: 1,
            PauseCount: 0,
            SuddenDropCount: 0,
            BssidChangeCount: 1,
            AdapterChangeCount: 1,
            CounterResetCount: 0,
            WlanDisconnectedSampleCount: 0,
            Confidence: ObservationConfidence.Low,
            Samples: samples,
            Message: "합성 관찰 종료 user@example.invalid",
            Limitation: "인터페이스 전체 트래픽 10.20.30.40");
        WlanSnapshot initialWlan = new(
            Timestamp: startedAt,
            IsConnected: true,
            Ssid: "SECRET-SSID",
            Bssid: SecretBssid,
            RssiDbm: -55,
            Channel: 36,
            PhyType: "802.11ax",
            ReceiveLinkSpeedBps: 1_200_000_000,
            TransmitLinkSpeedBps: 1_200_000_000,
            InterfaceDescription: "Secret Wi-Fi Adapter",
            InterfaceId: SecretInterfaceId);

        return new BrowserObservationResult(
            BrowserObservationStatus.PartialSuccess,
            summary,
            initialWlan,
            "어댑터가 변경됐습니다. user@example.invalid",
            BrowserObservationTerminationReason.AdapterChanged);
    }

    private static BrowserObservationSample Sample(
        DateTimeOffset timestamp,
        bool isBaseline,
        bool bssidChanged,
        bool adapterChanged,
        string note) =>
        new(
            Timestamp: timestamp,
            Interval: TimeSpan.FromSeconds(1),
            IsBaseline: isBaseline,
            InterfaceId: SecretInterfaceId,
            ReceiveBytesDelta: 10_000_000,
            TransmitBytesDelta: 100_000,
            RawReceiveMbps: 80,
            RawTransmitMbps: 0.8,
            AdjustedReceiveMbps: isBaseline ? null : 79.5,
            RssiDbm: -55,
            Bssid: SecretBssid,
            ReceiveLinkSpeedBps: 1_200_000_000,
            TransmitLinkSpeedBps: 1_200_000_000,
            InvalidInterval: false,
            AdapterChanged: adapterChanged,
            CounterReset: false,
            WlanDisconnected: false,
            BssidChanged: bssidChanged,
            PauseDetected: false,
            SuddenDropDetected: false,
            Note: note);

    private static void AssertSensitiveValuesAbsent(
        string content,
        string format)
    {
        string[] secrets =
        [
            SecretInterfaceId,
            SecretBssid,
            "SECRET-SSID",
            "Secret Wi-Fi Adapter",
            "user@example.invalid",
            "10.20.30.40",
            "corp.example.invalid"
        ];

        foreach (string secret in secrets)
        {
            Ensure(!content.Contains(
                    secret,
                    StringComparison.OrdinalIgnoreCase),
                $"{format}에 민감값이 남았습니다: {secret}");
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
