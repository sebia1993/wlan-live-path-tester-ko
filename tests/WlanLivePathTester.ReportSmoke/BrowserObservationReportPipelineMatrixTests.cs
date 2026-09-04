using System.Net;
using System.Runtime.CompilerServices;
using System.Text.Json;
using WlanLivePathTester.Core.Models;
using WlanLivePathTester.Core.Observation;
using WlanLivePathTester.Core.Reporting;

namespace WlanLivePathTester.ReportSmoke;

internal static class BrowserObservationReportPipelineMatrixTests
{
    private const string SecretGuid =
        "61B2C3D4-E5F6-47A8-9123-1234567890AB";
    private const string SecretEmail = "matrix-user@example.invalid";
    private const string SecretIp = "10.99.88.77";
    private const string SecretUrl =
        "https://matrix.example.invalid/private.bin";

    private static readonly TerminationCase[] Cases =
    [
        new(
            BrowserObservationStatus.Success,
            BrowserObservationTerminationReason.Completed,
            "BROWSER_OBSERVATION_COMPLETED",
            "Information"),
        new(
            BrowserObservationStatus.Canceled,
            BrowserObservationTerminationReason.CanceledByUser,
            "BROWSER_OBSERVATION_CANCELED_BY_USER",
            "Information"),
        new(
            BrowserObservationStatus.AdapterChanged,
            BrowserObservationTerminationReason.AdapterChanged,
            "BROWSER_OBSERVATION_ADAPTER_CHANGED",
            "Warning"),
        new(
            BrowserObservationStatus.AdapterUnavailable,
            BrowserObservationTerminationReason.AdapterUnavailable,
            "BROWSER_OBSERVATION_ADAPTER_UNAVAILABLE",
            "Warning"),
        new(
            BrowserObservationStatus.AdapterUnavailable,
            BrowserObservationTerminationReason.WlanIdentityUnavailable,
            "BROWSER_OBSERVATION_WLAN_IDENTITY_UNAVAILABLE",
            "Warning"),
        new(
            BrowserObservationStatus.CounterProviderMismatch,
            BrowserObservationTerminationReason.CounterProviderMismatch,
            "BROWSER_OBSERVATION_COUNTER_PROVIDER_MISMATCH",
            "Warning"),
        new(
            BrowserObservationStatus.Canceled,
            BrowserObservationTerminationReason.SystemSuspend,
            "BROWSER_OBSERVATION_SYSTEM_SUSPEND",
            "Warning"),
        new(
            BrowserObservationStatus.PartialSuccess,
            BrowserObservationTerminationReason.TimingDiscontinuity,
            "BROWSER_OBSERVATION_TIMING_DISCONTINUITY",
            "Warning"),
        new(
            BrowserObservationStatus.InvalidOptions,
            BrowserObservationTerminationReason.InvalidOptions,
            "BROWSER_OBSERVATION_INVALID_OPTIONS",
            "Warning"),
        new(
            BrowserObservationStatus.UnsupportedPlatform,
            BrowserObservationTerminationReason.UnsupportedPlatform,
            "BROWSER_OBSERVATION_UNSUPPORTED_PLATFORM",
            "Warning"),
        new(
            BrowserObservationStatus.NoWirelessConnection,
            BrowserObservationTerminationReason.NoWirelessConnection,
            "BROWSER_OBSERVATION_NO_WLAN_CONNECTION",
            "Warning"),
        new(
            BrowserObservationStatus.Failed,
            BrowserObservationTerminationReason.Failed,
            "BROWSER_OBSERVATION_FAILED",
            "Warning")
    ];

#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Run()
    {
        VerifyTerminationMatrix();
        VerifyAllDefinedReasonsAreCovered();
        Console.WriteLine(
            "PASS browser observation report termination matrix tests");
    }

    private static void VerifyTerminationMatrix()
    {
        foreach (TerminationCase testCase in Cases)
        {
            BrowserObservationResult source = CreateResult(testCase);
            BrowserObservationSessionReportDocument dedicated =
                BrowserObservationSessionReportWriter.CreateDocument(
                    source,
                    "0.1.0-test",
                    DateTimeOffset.UnixEpoch.AddHours(9));
            ReportObservationSection unifiedObservation =
                ReportObservationMapper.FromResult(source)
                ?? throw new InvalidOperationException(
                    "관찰 결과를 통합 보고서 섹션으로 매핑해야 합니다.");
            IReadOnlyList<ReportFinding> findings =
                ReportFindingPipeline.Evaluate(
                    HealthyWlan(),
                    HealthyProxy(),
                    Array.Empty<ReportTextSection>(),
                    unifiedObservation,
                    Array.Empty<ReportMeasurementSection>());
            ReportFinding finding = findings.Single(item =>
                item.Code.Equals(
                    testCase.FindingCode,
                    StringComparison.Ordinal));
            LocalDiagnosticReport unified = CreateUnifiedReport(
                unifiedObservation,
                findings);

            string dedicatedJson =
                BrowserObservationSessionReportWriter.RenderJson(
                    dedicated);
            string dedicatedCsv =
                BrowserObservationSessionReportWriter.RenderCsv(
                    dedicated);
            string dedicatedHtml =
                BrowserObservationSessionReportWriter.RenderHtml(
                    dedicated);
            string unifiedJson = LocalReportWriter.RenderJson(unified);
            string unifiedCsv = LocalReportWriter.RenderCsv(unified);
            string unifiedHtml = LocalReportWriter.RenderHtml(unified);

            string reason = testCase.Reason.ToString();
            string display =
                BrowserObservationTerminationPolicy.ToDisplayText(
                    testCase.Reason);

            Ensure(dedicated.Status == testCase.Status.ToString(),
                $"전용 보고서 상태가 잘못됐습니다: {reason}");
            Ensure(dedicated.TerminationReason == reason,
                $"전용 보고서 종료 원인이 잘못됐습니다: {reason}");
            Ensure(dedicated.TerminationDisplay == display,
                $"전용 보고서 한국어 설명이 잘못됐습니다: {reason}");
            Ensure(unifiedObservation.Status
                   == testCase.Status.ToString(),
                $"통합 관찰 상태가 잘못됐습니다: {reason}");
            Ensure(unifiedObservation.TerminationReason == reason,
                $"통합 관찰 종료 원인이 잘못됐습니다: {reason}");
            Ensure(finding.Severity == testCase.Severity,
                $"종료 Finding 심각도가 잘못됐습니다: {reason}");
            Ensure(findings.Count(item => item.Code.Equals(
                    testCase.FindingCode,
                    StringComparison.Ordinal)) == 1,
                $"종료 Finding은 정확히 한 개여야 합니다: {reason}");
            Ensure(!findings.Any(item => item.Code.Equals(
                    "NO_CLEAR_FAILURE_PATTERN",
                    StringComparison.Ordinal)),
                $"구조화 종료 원인과 일반 무패턴 Finding이 함께 있으면 안 됩니다: {reason}");

            AssertDedicatedFormats(
                dedicatedJson,
                dedicatedCsv,
                dedicatedHtml,
                reason,
                display);
            AssertUnifiedFormats(
                unifiedJson,
                unifiedCsv,
                unifiedHtml,
                reason,
                testCase.FindingCode,
                finding);
            AssertSecretsAbsent(
                string.Join(
                    Environment.NewLine,
                    dedicatedJson,
                    dedicatedCsv,
                    dedicatedHtml,
                    unifiedJson,
                    unifiedCsv,
                    unifiedHtml),
                reason);
        }
    }

    private static void VerifyAllDefinedReasonsAreCovered()
    {
        BrowserObservationTerminationReason[] required =
            Enum.GetValues<BrowserObservationTerminationReason>()
                .Where(reason => reason
                    != BrowserObservationTerminationReason.None)
                .ToArray();
        BrowserObservationTerminationReason[] covered = Cases
            .Select(testCase => testCase.Reason)
            .Distinct()
            .ToArray();

        BrowserObservationTerminationReason[] missing = required
            .Except(covered)
            .ToArray();
        BrowserObservationTerminationReason[] duplicate = Cases
            .GroupBy(testCase => testCase.Reason)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();

        Ensure(missing.Length == 0,
            $"보고서 행렬에 누락된 종료 원인이 있습니다: {string.Join(", ", missing)}");
        Ensure(duplicate.Length == 0,
            $"보고서 행렬에 중복 종료 원인이 있습니다: {string.Join(", ", duplicate)}");
    }

    private static BrowserObservationResult CreateResult(
        TerminationCase testCase)
    {
        string message =
            $"합성 {testCase.Reason} {SecretEmail} {SecretIp} {SecretUrl} {SecretGuid}";
        WlanSnapshot initialWlan = new(
            Timestamp: DateTimeOffset.UnixEpoch,
            IsConnected: true,
            Ssid: "MATRIX-SECRET-SSID",
            Bssid: "AA:BB:CC:DD:EE:80",
            RssiDbm: -55,
            Channel: 36,
            PhyType: "802.11ax",
            ReceiveLinkSpeedBps: 1_200_000_000,
            TransmitLinkSpeedBps: 1_200_000_000,
            InterfaceDescription: "Matrix Secret Wi-Fi",
            InterfaceState: "Connected",
            SignalQualityPercent: 90,
            CenterFrequencyMhz: 5180,
            Authentication: "WPA2-Enterprise",
            Cipher: "CCMP",
            InterfaceId: SecretGuid);

        return new BrowserObservationResult(
            testCase.Status,
            summary: null,
            initialWlan,
            message,
            testCase.Reason);
    }

    private static void AssertDedicatedFormats(
        string json,
        string csv,
        string html,
        string reason,
        string display)
    {
        string decodedHtml = WebUtility.HtmlDecode(html);
        using JsonDocument parsed = JsonDocument.Parse(json);
        Ensure(parsed.RootElement
                .GetProperty("terminationReason")
                .GetString() == reason,
            $"전용 JSON 종료 원인이 잘못됐습니다: {reason}");
        Ensure(parsed.RootElement
                .GetProperty("terminationDisplay")
                .GetString() == display,
            $"전용 JSON 한국어 설명이 잘못됐습니다: {reason}");
        Ensure(csv.Contains(
                $"\"observation\",\"terminationReason\",\"{reason}\"",
                StringComparison.Ordinal),
            $"전용 CSV 종료 원인이 없습니다: {reason}");
        Ensure(csv.Contains(
                $"\"observation\",\"terminationDisplay\",\"{display}\"",
                StringComparison.Ordinal),
            $"전용 CSV 한국어 설명이 없습니다: {reason}");
        Ensure(decodedHtml.Contains(reason, StringComparison.Ordinal)
               && decodedHtml.Contains(display, StringComparison.Ordinal),
            $"전용 HTML에 종료 원인과 한국어 설명이 없습니다: {reason}");
    }

    private static void AssertUnifiedFormats(
        string json,
        string csv,
        string html,
        string reason,
        string findingCode,
        ReportFinding finding)
    {
        string decodedHtml = WebUtility.HtmlDecode(html);
        using JsonDocument parsed = JsonDocument.Parse(json);
        Ensure(parsed.RootElement
                .GetProperty("browserObservation")
                .GetProperty("terminationReason")
                .GetString() == reason,
            $"통합 JSON 종료 원인이 잘못됐습니다: {reason}");
        Ensure(parsed.RootElement
                .GetProperty("findings")
                .EnumerateArray()
                .Count(item => item.GetProperty("code").GetString()
                    == findingCode) == 1,
            $"통합 JSON Finding 코드가 정확히 한 개여야 합니다: {reason}");
        Ensure(csv.Contains(
                $"\"browserObservation\",\"terminationReason\",\"{reason}\"",
                StringComparison.Ordinal),
            $"통합 CSV 종료 원인이 없습니다: {reason}");
        Ensure(csv.Contains(findingCode, StringComparison.Ordinal),
            $"통합 CSV Finding 코드가 없습니다: {reason}");
        Ensure(decodedHtml.Contains(reason, StringComparison.Ordinal)
               && decodedHtml.Contains(
                   BrowserObservationTerminationPolicy.ToDisplayText(
                       Enum.Parse<BrowserObservationTerminationReason>(
                           reason)),
                   StringComparison.Ordinal),
            $"통합 HTML에 종료 원인 설명이 없습니다: {reason}");
        Ensure(decodedHtml.Contains(
                   finding.Title,
                   StringComparison.Ordinal)
               && decodedHtml.Contains(
                   finding.Interpretation,
                   StringComparison.Ordinal),
            $"통합 HTML에 Finding 제목과 해석이 없습니다: {reason}");
    }

    private static void AssertSecretsAbsent(
        string content,
        string reason)
    {
        string decoded = WebUtility.HtmlDecode(content);
        string[] secrets =
        [
            SecretGuid,
            SecretEmail,
            SecretIp,
            SecretUrl,
            "matrix.example.invalid",
            "MATRIX-SECRET-SSID",
            "AA:BB:CC:DD:EE:80",
            "Matrix Secret Wi-Fi"
        ];

        foreach (string secret in secrets)
        {
            Ensure(!decoded.Contains(
                    secret,
                    StringComparison.OrdinalIgnoreCase),
                $"{reason} 보고서 출력에 민감값이 남았습니다: {secret}");
        }
    }

    private static LocalDiagnosticReport CreateUnifiedReport(
        ReportObservationSection observation,
        IReadOnlyList<ReportFinding> findings) =>
        new(
            SchemaVersion: "1.1-test",
            Metadata: new ReportMetadata(
                GeneratedAt: DateTimeOffset.UnixEpoch,
                ApplicationName: "WLAN Live Path Tester KO",
                ApplicationVersion: "0.1.0-test",
                OperatingSystem: "Windows synthetic",
                RuntimeVersion: ".NET synthetic",
                Culture: "ko-KR",
                SensitiveValuesIncluded: false,
                DataHandlingStatement: "합성 로컬 보고서"),
            Wlan: HealthyWlan(),
            Proxy: HealthyProxy(),
            Measurements: Array.Empty<ReportTextSection>(),
            BrowserObservation: observation,
            Findings: findings,
            Limitations: Array.Empty<string>(),
            StructuredMeasurements:
                Array.Empty<ReportMeasurementSection>());

    private static ReportWlanSection HealthyWlan() =>
        new(
            CapturedAt: DateTimeOffset.UnixEpoch,
            IsConnected: true,
            InterfaceDescription: "[마스킹됨]",
            InterfaceState: "Connected",
            Ssid: "[마스킹됨]",
            Bssid: "[마스킹됨]",
            RssiDbm: -55,
            SignalQualityPercent: 90,
            Channel: 36,
            CenterFrequencyMhz: 5180,
            Band: "5 GHz",
            PhyType: "802.11ax",
            ReceiveLinkMbps: 1200,
            TransmitLinkMbps: 1200,
            Authentication: "WPA2-Enterprise",
            Cipher: "CCMP",
            ReadError: null);

    private static ReportProxySection HealthyProxy() =>
        new(
            ReadSucceeded: true,
            Mode: "Manual",
            AutoDetectEnabled: false,
            PacConfigured: false,
            ManualProxyConfigured: true,
            BypassConfigured: true,
            Win32Error: null,
            Statement: "프록시 값은 마스킹됨");

    private static void Ensure(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed record TerminationCase(
        BrowserObservationStatus Status,
        BrowserObservationTerminationReason Reason,
        string FindingCode,
        string Severity);
}
