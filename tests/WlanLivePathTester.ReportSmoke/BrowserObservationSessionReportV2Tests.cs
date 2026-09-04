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
    private const string SecretDescription =
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

        VerifyDocumentAndFormats(document);
        VerifyLegacyTerminationFallback();
        VerifyNonFiniteNumbersAreNormalized();
        VerifyLocalFiles(document);
        Console.WriteLine(
            "PASS dedicated browser observation JSON CSV HTML SHA-256 report tests");
    }

    private static void VerifyDocumentAndFormats(
        BrowserObservationSessionReportDocument document)
    {
        Ensure(document.SchemaVersion == "1.1",
            "전용 관찰 보고서 스키마가 잘못됐습니다.");
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
            "민감값 미포함을 선언해야 합니다.");
        Ensure(document.Summary?.Samples.Count == 2,
            "시간축 샘플 두 개가 필요합니다.");
        Ensure(document.Summary?.BssidChangeCount == 1,
            "BSSID 원문 없이 변경 횟수를 유지해야 합니다.");
        Ensure(document.Summary?.CounterResetCount == 1,
            "카운터 재설정 횟수를 유지해야 합니다.");

        string json = BrowserObservationSessionReportWriter.RenderJson(
            document);
        string csv = BrowserObservationSessionReportWriter.RenderCsv(
            document);
        string html = BrowserObservationSessionReportWriter.RenderHtml(
            document);

        using JsonDocument parsed = JsonDocument.Parse(json);
        Ensure(parsed.RootElement
                .GetProperty("terminationReason")
                .GetString() == "CounterProviderMismatch",
            "JSON에 구조화 종료 원인이 필요합니다.");
        Ensure(parsed.RootElement
                .GetProperty("summary")
                .GetProperty("samples")
                .GetArrayLength() == 2,
            "JSON에 시간축 샘플 두 개가 필요합니다.");
        Ensure(csv.StartsWith(
                "section,key,value",
                StringComparison.Ordinal),
            "CSV 스키마가 잘못됐습니다.");
        Ensure(csv.Contains(
                "\"observation\",\"terminationReason\",\"CounterProviderMismatch\"",
                StringComparison.Ordinal),
            "CSV에 종료 원인 행이 필요합니다.");
        Ensure(csv.Contains(
                "\"summary\",\"counterResetCount\",\"1\"",
                StringComparison.Ordinal),
            "CSV에 카운터 재설정 횟수가 필요합니다.");
        Ensure(csv.Contains("\"'=HYPERLINK", StringComparison.Ordinal),
            "수식 시작 메모는 CSV에서 비활성화해야 합니다.");
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
            "HTML에 종료 원인이 필요합니다.");
        Ensure(html.Contains("시간축 샘플", StringComparison.Ordinal),
            "HTML에 시간축 샘플 표가 필요합니다.");
        Ensure(!html.Contains("<script", StringComparison.OrdinalIgnoreCase)
               && !html.Contains("<iframe", StringComparison.OrdinalIgnoreCase)
               && !html.Contains("<link", StringComparison.OrdinalIgnoreCase),
            "HTML에 외부 실행·표시 리소스를 포함하면 안 됩니다.");

        AssertSecretsAbsent(json, "JSON");
        AssertSecretsAbsent(csv, "CSV");
        AssertSecretsAbsent(html, "HTML");
    }

    private static void VerifyLegacyTerminationFallback()
    {
        BrowserObservationResult legacy = new(
            BrowserObservationStatus.Canceled,
            null,
            null,
            "기존 네 값 취소 결과");
        BrowserObservationSessionReportDocument document =
            BrowserObservationSessionReportWriter.CreateDocument(
                legacy,
                "0.1.0-test",
                DateTimeOffset.UnixEpoch);

        Ensure(document.TerminationReason == "CanceledByUser",
            "기존 Canceled 결과는 EffectiveTerminationReason을 사용해야 합니다.");
        Ensure(document.TerminationDisplay == "사용자 중지",
            "기존 취소 결과의 한국어 설명이 필요합니다.");
    }

    private static void VerifyNonFiniteNumbersAreNormalized()
    {
        DateTimeOffset startedAt = DateTimeOffset.UnixEpoch;
        BrowserObservationSample sample = new(
            startedAt.AddSeconds(1),
            TimeSpan.FromSeconds(1),
            false,
            SecretInterfaceId,
            -1,
            -2,
            double.NaN,
            double.PositiveInfinity,
            double.NegativeInfinity,
            -60,
            SecretBssid,
            1_200_000_000,
            1_200_000_000,
            false,
            false,
            true,
            false,
            false,
            false,
            false,
            null);
        BrowserObservationSummary summary = new(
            startedAt,
            startedAt.AddSeconds(1),
            TimeSpan.FromSeconds(1),
            double.NaN,
            double.PositiveInfinity,
            double.NegativeInfinity,
            -10,
            1,
            0,
            0,
            0,
            0,
            1,
            0,
            ObservationConfidence.Low,
            [sample],
            "합성 비정상 숫자",
            "합성 한계");
        BrowserObservationResult result = new(
            BrowserObservationStatus.PartialSuccess,
            summary,
            null,
            "합성 비정상 숫자",
            BrowserObservationTerminationReason.Failed);
        BrowserObservationSessionReportDocument document =
            BrowserObservationSessionReportWriter.CreateDocument(
                result,
                "0.1.0-test",
                DateTimeOffset.UnixEpoch);

        Ensure(document.Summary?.BaselineReceiveMbps == 0,
            "NaN 기준값은 0으로 정규화해야 합니다.");
        Ensure(document.Summary?.AverageAdjustedReceiveMbps is null
               && document.Summary.PeakAdjustedReceiveMbps is null,
            "무한대 처리량은 null로 제거해야 합니다.");
        Ensure(document.Summary.TotalReceiveBytes == 0,
            "음수 총 수신량은 0으로 제한해야 합니다.");
        Ensure(document.Summary.Samples[0].ReceiveBytesDelta == 0
               && document.Summary.Samples[0].RawReceiveMbps is null,
            "음수 델타와 NaN 샘플 처리량을 정규화해야 합니다.");
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
                AssertSecretsAbsent(File.ReadAllText(path), fileName);
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
                startedAt.AddSeconds(1),
                TimeSpan.FromSeconds(1),
                true,
                SecretInterfaceId,
                125_000,
                12_500,
                1,
                0.1,
                1,
                -55,
                SecretBssid,
                1_200_000_000,
                1_200_000_000,
                false,
                false,
                true,
                false,
                false,
                false,
                false,
                $"=HYPERLINK(\"{SecretUrl}\",\"{SecretInterfaceId}\")"),
            new BrowserObservationSample(
                startedAt.AddSeconds(2),
                TimeSpan.FromSeconds(1),
                false,
                SecretInterfaceId,
                10_000_000,
                100_000,
                80,
                0.8,
                79,
                -56,
                "AA:BB:CC:DD:EE:00",
                1_200_000_000,
                1_200_000_000,
                false,
                false,
                false,
                false,
                true,
                false,
                false,
                $"합성 {SecretEmail} {SecretIp} {SecretSsid} {SecretDescription}")
        ];
        BrowserObservationSummary summary = new(
            startedAt,
            startedAt.AddSeconds(2),
            TimeSpan.FromSeconds(1),
            1,
            79,
            79,
            10_000_000,
            1,
            0,
            0,
            1,
            0,
            1,
            0,
            ObservationConfidence.Low,
            samples,
            $"합성 요약 {SecretEmail} {SecretIp} {SecretUrl}",
            $"합성 한계 {SecretDescription} {SecretInterfaceId}");
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
            InterfaceDescription: SecretDescription,
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
            $"합성 공급자 불일치 {SecretEmail} {SecretIp} {SecretUrl} {SecretInterfaceId} {SecretDescription} {SecretSsid} {SecretBssid}",
            BrowserObservationTerminationReason.CounterProviderMismatch);
    }

    private static void AssertSecretsAbsent(
        string content,
        string format)
    {
        string[] secrets =
        [
            SecretInterfaceId,
            SecretDescription,
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
