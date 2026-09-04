using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using WlanLivePathTester.Core.Models;
using WlanLivePathTester.Core.Observation;
using WlanLivePathTester.Core.Reporting;

namespace WlanLivePathTester.ReportSmoke;

internal static class BrowserObservationSessionReportV2Tests
{
    private const string SecretInterfaceId =
        "A1B2C3D4-E5F6-47A8-9123-1234567890AB";
    private const string SecretInterfaceDescription =
        "Corporate Secret Wi-Fi Adapter";
    private const string SecretSsid = "CORP-SECRET-SSID";
    private const string SecretBssid = "AA:BB:CC:DD:EE:FF";
    private const string SecretEmail = "user@example.invalid";
    private const string SecretIp = "10.20.30.40";
    private const string SecretUrl =
        "https://internal.example.invalid/private.bin";

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
        VerifyLegacyTerminationFallback();
        VerifyNonFiniteNumbersAreNormalized();
        VerifyLocalFiles(document);
        Console.WriteLine(
            "PASS dedicated browser observation JSON CSV HTML SHA-256 report tests");
    }

    private static void VerifyDocument(
        BrowserObservationSessionReportDocument document)
    {
        Ensure(document.SchemaVersion == "1.1",
            "전용 관찰 보고서는 현재 스키마 버전을 사용해야 합니다.");
        Ensure(document.Status == "CounterProviderMismatch",
            "관찰 상태를 구조화해야 합니다.");
        Ensure(document.TerminationReason
               == "CounterProviderMismatch",
            "구조화 종료 원인을 유지해야 합니다.");
        Ensure(document.TerminationDisplay.Contains(
                "카운터 공급자",
                StringComparison.Ordinal),
            "종료 원인의 한국어 설명이 필요합니다.");
        Ensure(!document.SensitiveValuesIncluded,
            "전용 관찰 보고서는 민감값 미포함을 선언해야 합니다.");
        Ensure(document.Summary?.Samples.Count == 2,
            "합성 시간축 샘플 두 개를 유지해야 합니다.");
        Ensure(document.Summary?.BssidChangeCount == 1,
            "BSSID 원문 없이 변경 횟수를 유지해야 합니다.");
        Ensure(document.Summary?.CounterResetCount == 1,
            "카운터 재설정 횟수를 유지해야 합니다.");
    }

    private static void VerifyJson(
        BrowserObservationSessionReportDocument document)
    {
        string json = BrowserObservationSessionReportWriter.RenderJson(
            document);
        using JsonDocument parsed = JsonDocument.Parse(json);
        JsonElement root = parsed.RootElement;

        Ensure(root.GetProperty("terminationReason").GetString()
               == "CounterProviderMismatch",
            "JSON에 구조화 종료 원인이 필요합니다.");
        Ensure(root.GetProperty("terminationDisplay").GetString()
               is string display
               && display.Contains("카운터 공급자", StringComparison.Ordinal),
            "JSON에 사람이 읽을 수 있는 종료 설명이 필요합니다.");
        Ensure(root.GetProperty("summary")
                .GetProperty("samples")
                .GetArrayLength() == 2,
            "JSON에 시간축 샘플 두 개가 필요합니다.");
        AssertSecretsAbsent(json, "JSON");
    }

    private static void VerifyCsv(
        BrowserObservationSessionReportDocument document)
    {
        string csv = BrowserObservationSessionReportWriter.RenderCsv(
            document);

        Ensure(csv.StartsWith(
                "section,key,value",
                StringComparison.Ordinal),
            "CSV는 section,key,value 스키마를 사용해야 합니다.");
        Ensure(csv.Contains(
                "\"observation\",\"terminationReason\",\"CounterProviderMismatch\"",
                StringComparison.Ordinal),
            "CSV에 구조화 종료 원인 행이 필요합니다.");
        Ensure(csv.Contains(
                "\"summary\",\"counterResetCount\",\"1\"",
                StringComparison.Ordinal),
            "CSV에 카운터 재설정 횟수가 필요합니다.");
        Ensure(csv.Contains(
                "\"sample.1\",\"counterReset\",\"True\"",
                StringComparison.Ordinal),
            "CSV에 샘플 재설정 상태가 필요합니다.");
        Ensure(csv.Contains("\"'=HYPERLINK", StringComparison.Ordinal),
            "수식 시작 샘플 메모는 CSV에서 비활성화해야 합니다.");
        AssertSecretsAbsent(csv, "CSV");
    }

    private static void VerifyHtml(
        BrowserObservationSessionReportDocument document)
    {
        string html = BrowserObservationSessionReportWriter.RenderHtml(
            document);

        Ensure(html.StartsWith(
                "<!doctype html>",
                StringComparison.OrdinalIgnoreCase),
            "HTML5 doctype이 필요합니다.");
        Ensure(html.Contains(
                "Content-Security-Policy",
                StringComparison.Ordinal),
            "HTML에 CSP가 필요합니다.");
        Ensure(html.Contains(
                "CounterProviderMismatch",
                StringComparison.Ordinal),
            "HTML에 구조화 종료 원인이 필요합니다.");
        Ensure(html.Contains(
                "시간축 샘플",
                StringComparison.Ordinal),
            "HTML에 시간축 샘플 표가 필요합니다.");
        Ensure(!html.Contains(
                "<script",
                StringComparison.OrdinalIgnoreCase),
            "HTML에 script를 포함하면 안 됩니다.");
        Ensure(!html.Contains(
                "<iframe",
                StringComparison.OrdinalIgnoreCase),
            "HTML에 iframe을 포함하면 안 됩니다.");
        Ensure(!html.Contains(
                "<link",
                StringComparison.OrdinalIgnoreCase),
            "HTML에 외부 stylesheet 링크를 포함하면 안 됩니다.");
        AssertSecretsAbsent(html, "HTML");
    }

    private static void VerifyLegacyTerminationFallback()
    {
        BrowserObservationResult legacy = new(
            BrowserObservationStatus.Canceled,
            summary: null,
            initialWlan: null,
            message: "기존 네 값 취소 결과");
        BrowserObservationSessionReportDocument document =
            BrowserObservationSessionReportWriter.CreateDocument(
                legacy,
                "0.1.0-test",
                DateTimeOffset.UnixEpoch);

        Ensure(document.TerminationReason == "CanceledByUser",
            "명시값 없는 기존 Canceled 결과는 EffectiveTerminationReason을 사용해야 합니다.");
        Ensure(document.TerminationDisplay == "사용자 중지",
            "기존 취소 결과의 한국어 종료 설명이 필요합니다.");
    }

    private static void VerifyNonFiniteNumbersAreNormalized()
    {
        DateTimeOffset startedAt = DateTimeOffset.UnixEpoch;
        BrowserObservationSample sample = new(
            Timestamp: startedAt.AddSeconds(1),
            Interval: TimeSpan.FromSeconds(1),
            IsBaseline: false,
            InterfaceId: SecretInterfaceId,
            ReceiveBytesDelta: -1,
            TransmitBytesDelta: -2,
            RawReceiveMbps: double.NaN,
            RawTransmitMbps: double.PositiveInfinity,
            AdjustedReceiveMbps: double.NegativeInfinity,
            RssiDbm: -60,
            Bssid: SecretBssid,
            ReceiveLinkSpeedBps: 1_200_000_000,
            TransmitLinkSpeedBps: 1_200_000_000,
            InvalidInterval: false,
            AdapterChanged: false,
            CounterReset: true,
            WlanDisconnected: false,
            BssidChanged: false,
            PauseDetected: false,
            SuddenDropDetected: false,
            Note: null);
        BrowserObservationSummary summary = new(
            StartedAt: startedAt,
            CompletedAt: startedAt.AddSeconds(1),
            ObservedDuration: TimeSpan.FromSeconds(1),
            BaselineReceiveMbps: double.NaN,
            AverageAdjustedReceiveMbps: double.PositiveInfinity,
            PeakAdjustedReceiveMbps: double.NegativeInfinity,
            TotalReceiveBytes: -10,
            ActiveSampleCount: 1,
            PauseCount: 0,
            SuddenDropCount: 0,
            BssidChangeCount: 0,
            AdapterChangeCount: 0,
            CounterResetCount: 1,
            WlanDisconnectedSampleCount: 0,
            Confidence: ObservationConfidence.Low,
            Samples: [sample],
            Message: "합성 비정상 숫자",
            Limitation: "합성 한계");
        BrowserObservationResult result = new(
            BrowserObservationStatus.PartialSuccess,
            summary,
            initialWlan: null,
            message: "합성 비정상 숫자",
            BrowserObservationTerminationReason.Failed);
        BrowserObservationSessionReportDocument document =
            BrowserObservationSessionReportWriter.CreateDocument(
                result,
                "0.1.0-test",
                DateTimeOffset.UnixEpoch);

        Ensure(document.Summary?.BaselineReceiveMbps == 0,
            "NaN 기준값은 0으로 정규화해야 합니다.");
        Ensure(document.Summary?.AverageAdjustedReceiveMbps is null,
            "무한대 평균은 null로 제거해야 합니다.");
        Ensure(document.Summary?.PeakAdjustedReceiveMbps is null,
            "무한대 최고값은 null로 제거해야 합니다.");
        Ensure(document.Summary?.TotalReceiveBytes == 0,
            "음수 총 수신량은 0으로 제한해야 합니다.");
        Ensure(document.Summary?.Samples[0].ReceiveBytesDelta == 0,
            "음수 수신 델타는 0으로 제한해야 합니다.");
        Ensure(document.Summary?.Samples[0].RawReceiveMbps is null,
            "NaN 샘플 처리량은 null로 제거해야 합니다.");
        _ = JsonDocument.Parse(
            BrowserObservationSessionReportWriter.RenderJson(document));
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
                "JSON·CSV·HTML 해시 세 개가 필요합니다.");

            foreach ((string fileName, string expectedHash)
                     in export.Sha256)
            {
                string path = Path.Combine(
                    export.OutputDirectory,
                    fileName);
                using FileStream stream = File.OpenRead(path);
                string actualHash = Convert.ToHexString(
                        SHA256.HashData(stream))
                    .ToLowerInvariant();
                Ensure(actualHash == expectedHash,
                    $"SHA-256이 일치하지 않습니다: {fileName}");
                AssertSecretsAbsent(
                    File.ReadAllText(path),
                    fileName);
            }

            string checksum = File.ReadAllText(export.Sha256Path);
            Ensure(export.Sha256.All(pair => checksum.Contains(
                    $"{pair.Value}  {pair.Key}",
                    StringComparison.Ordinal)),
                "SHA256SUMS에 모든 보고서 해시가 필요합니다.");
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
            new BrowserObservationSample(
                Timestamp: startedAt.AddSeconds(1),
                Interval: TimeSpan.FromSeconds(1),
                IsBaseline: true,
                InterfaceId: SecretInterfaceId,
                ReceiveBytesDelta: 125_000,
                TransmitBytesDelta: 12_500,
                RawReceiveMbps: 1,
                RawTransmitMbps: 0.1,
                AdjustedReceiveMbps: 1,
                RssiDbm: -55,
                Bssid: SecretBssid,
                ReceiveLinkSpeedBps: 1_200_000_000,
                TransmitLinkSpeedBps: 1_200_000_000,
                InvalidInterval: false,
                AdapterChanged: false,
                CounterReset: true,
                WlanDisconnected: false,
                BssidChanged: false,
                PauseDetected: false,
                SuddenDropDetected: false,
                Note:
                    $"=HYPERLINK(\"{SecretUrl}\",\"{SecretInterfaceId}\")"),
            new BrowserObservationSample(
                Timestamp: startedAt.AddSeconds(2),
                Interval: TimeSpan.FromSeconds(1),
                IsBaseline: false,
                InterfaceId: SecretInterfaceId,
                ReceiveBytesDelta: 10_000_000,
                TransmitBytesDelta: 100_000,
                RawReceiveMbps: 80,
                RawTransmitMbps: 0.8,
                AdjustedReceiveMbps: 79,
                RssiDbm: -56,
                Bssid: "AA:BB:CC:DD:EE:00",
                ReceiveLinkSpeedBps: 1_200_000_000,
                TransmitLinkSpeedBps: 1_200_000_000,
                InvalidInterval: false,
                AdapterChanged: false,
                CounterReset: false,
                WlanDisconnected: false,
                BssidChanged: true,
                PauseDetected: false,
                SuddenDropDetected: false,
                Note:
                    $"합성 {SecretEmail} {SecretIp} {SecretSsid} {SecretInterfaceDescription}")
        ];
        BrowserObservationSummary summary = new(
            StartedAt: startedAt,
            CompletedAt: startedAt.AddSeconds(2),
            ObservedDuration: TimeSpan.FromSeconds(1),
            BaselineReceiveMbps: 1,
            AverageAdjustedReceiveMbps: 79,
            PeakAdjustedReceiveMbps: 79,
            TotalReceiveBytes: 10_000_000,
            ActiveSampleCount: 1,
            PauseCount: 0,
            SuddenDropCount: 0,
            BssidChangeCount: 1,
            AdapterChangeCount: 0,
            CounterResetCount: 1,
            WlanDisconnectedSampleCount: 0,
            Confidence: ObservationConfidence.Low,
            Samples: samples,
            Message:
                $"합성 요약 {SecretEmail} {SecretIp} {SecretUrl}",
            Limitation:
                $"합성 한계 {SecretInterfaceDescription} {SecretInterfaceId}");
        WlanSnapshot initialWlan = new(
            Timestamp: startedAt,
            IsConnected: true,
            Ssid: SecretSsid,
            Bssid: SecretBssid,
            RssiDbm: -55,
            Channel: 36,
            PhyType: "802.11ax",
            ReceiveLinkSpeedBps: 1_200_000_000,
            TransmitLinkSpeedBps: 1_200_000_000,
            InterfaceDescription: SecretInterfaceDescription,
            InterfaceState: "Connected",
            SignalQualityPercent: 90,
            CenterFrequencyMhz: 5180,
            Authentication: "WPA2-Enterprise",
            Cipher: "CCMP",
            InterfaceId: SecretInterfaceId);

        return new BrowserObservationResult(
            BrowserObservationStatus.CounterProviderMismatch,
            summary,
            initialWlan,
            $"합성 공급자 불일치 {SecretEmail} {SecretIp} {SecretUrl} {SecretInterfaceId} {SecretInterfaceDescription} {SecretSsid} {SecretBssid}",
            BrowserObservationTerminationReason
                .CounterProviderMismatch);
    }

    private static void AssertSecretsAbsent(
        string content,
        string format)
    {
        string[] secrets =
        [
            SecretInterfaceId,
            SecretInterfaceDescription,
            SecretSsid,
            SecretBssid,
            SecretEmail,
            SecretIp,
            SecretUrl,
            "internal.example.invalid"
        ];

        foreach (string secret in secrets)
        {
            Ensure(!content.Contains(
                    secret,
                    StringComparison.OrdinalIgnoreCase),
                $"{format}에 관찰 민감값이 남았습니다: {secret}");
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
