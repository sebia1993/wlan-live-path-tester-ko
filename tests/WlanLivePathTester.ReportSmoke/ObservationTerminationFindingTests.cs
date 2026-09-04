using System.Runtime.CompilerServices;
using WlanLivePathTester.Core.Reporting;

namespace WlanLivePathTester.ReportSmoke;

internal static class ObservationTerminationFindingTests
{
#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Run()
    {
        VerifyKnownTerminationReasons();
        VerifyReasonIsAuthoritativeOverStatus();
        VerifyUnknownReasonIsNotReflected();
        VerifyMissingReasonKeepsLegacyFallback();
        VerifyTerminationFindingIsUnique();
        Console.WriteLine("PASS deterministic observation termination finding tests");
    }

    private static void VerifyKnownTerminationReasons()
    {
        (string Reason, string Code, string Severity)[] cases =
        [
            ("Completed",
                "BROWSER_OBSERVATION_COMPLETED",
                "Information"),
            ("CanceledByUser",
                "BROWSER_OBSERVATION_CANCELED_BY_USER",
                "Information"),
            ("AdapterChanged",
                "BROWSER_OBSERVATION_ADAPTER_CHANGED",
                "Warning"),
            ("AdapterUnavailable",
                "BROWSER_OBSERVATION_ADAPTER_UNAVAILABLE",
                "Warning"),
            ("CounterProviderMismatch",
                "BROWSER_OBSERVATION_COUNTER_PROVIDER_MISMATCH",
                "Warning"),
            ("SystemSuspend",
                "BROWSER_OBSERVATION_SYSTEM_SUSPEND",
                "Warning"),
            ("TimingDiscontinuity",
                "BROWSER_OBSERVATION_TIMING_DISCONTINUITY",
                "Warning"),
            ("InvalidOptions",
                "BROWSER_OBSERVATION_INVALID_OPTIONS",
                "Warning"),
            ("UnsupportedPlatform",
                "BROWSER_OBSERVATION_UNSUPPORTED_PLATFORM",
                "Warning"),
            ("NoWirelessConnection",
                "BROWSER_OBSERVATION_NO_WLAN_CONNECTION",
                "Warning"),
            ("Failed",
                "BROWSER_OBSERVATION_FAILED",
                "Warning")
        ];

        foreach ((string reason, string expectedCode, string severity)
                 in cases)
        {
            IReadOnlyList<ReportFinding> findings = Evaluate(
                CreateObservation(reason));
            ReportFinding finding = SingleByCode(
                findings,
                expectedCode);

            Ensure(finding.Severity == severity,
                $"종료 원인 {reason}의 severity가 잘못됐습니다.");
            Ensure(finding.Evidence.Contains(
                    reason,
                    StringComparison.OrdinalIgnoreCase),
                $"종료 원인 {reason}의 고정 Evidence가 필요합니다.");
            Ensure(!findings.Any(item => item.Code ==
                    "NO_CLEAR_FAILURE_PATTERN"),
                $"구조화 종료 원인 {reason}이 있는데 일반 무패턴 Finding을 추가하면 안 됩니다.");
        }
    }

    private static void VerifyReasonIsAuthoritativeOverStatus()
    {
        ReportObservationSection observation = CreateObservation(
            "AdapterChanged") with
        {
            Status = "Success"
        };
        IReadOnlyList<ReportFinding> findings = Evaluate(observation);

        _ = SingleByCode(
            findings,
            "BROWSER_OBSERVATION_ADAPTER_CHANGED");
        Ensure(!findings.Any(item => item.Code ==
                "BROWSER_OBSERVATION_COMPLETED"),
            "종료 원인이 AdapterChanged이면 Status가 Success여도 완료 Finding으로 바꾸면 안 됩니다.");
    }

    private static void VerifyUnknownReasonIsNotReflected()
    {
        const string untrusted =
            "=HYPERLINK(\"https://evil.invalid\",\"secret\")";
        IReadOnlyList<ReportFinding> findings = Evaluate(
            CreateObservation(untrusted));
        ReportFinding finding = SingleByCode(
            findings,
            "BROWSER_OBSERVATION_TERMINATION_UNKNOWN");
        string combined = string.Join(
            Environment.NewLine,
            finding.Title,
            finding.Evidence,
            finding.Interpretation,
            finding.Limitation,
            finding.NextStep);

        Ensure(!combined.Contains(
                untrusted,
                StringComparison.Ordinal),
            "알 수 없는 종료 원인 원문을 Finding에 반사하면 안 됩니다.");
        Ensure(!combined.Contains(
                "evil.invalid",
                StringComparison.OrdinalIgnoreCase),
            "알 수 없는 종료 원인에 포함된 URL을 Finding에 노출하면 안 됩니다.");
    }

    private static void VerifyMissingReasonKeepsLegacyFallback()
    {
        IReadOnlyList<ReportFinding> findings = Evaluate(
            CreateObservation(reason: null));

        _ = SingleByCode(findings, "NO_CLEAR_FAILURE_PATTERN");
        Ensure(!findings.Any(item => item.Code.StartsWith(
                "BROWSER_OBSERVATION_",
                StringComparison.Ordinal)),
            "종료 원인이 없는 레거시 보고서에 특정 원인을 추정하면 안 됩니다.");
    }

    private static void VerifyTerminationFindingIsUnique()
    {
        ReportObservationSection observation = CreateObservation(
            "adapterchanged") with
        {
            Confidence = "Low",
            AdapterChangeCount = 2
        };
        IReadOnlyList<ReportFinding> findings = Evaluate(observation);

        Ensure(findings.Count(item => item.Code ==
                "BROWSER_OBSERVATION_ADAPTER_CHANGED") == 1,
            "같은 종료 원인 Finding을 중복 추가하면 안 됩니다.");
        _ = SingleByCode(
            findings,
            "BROWSER_OBSERVATION_LOW_CONFIDENCE");
    }

    private static IReadOnlyList<ReportFinding> Evaluate(
        ReportObservationSection observation) =>
        ReportFindingEngine.Evaluate(
            HealthyWlan(),
            HealthyProxy(),
            Array.Empty<ReportTextSection>(),
            observation,
            Array.Empty<ReportMeasurementSection>());

    private static ReportObservationSection CreateObservation(
        string? reason) =>
        new ReportObservationSection(
            Status: "PartialSuccess",
            StartedAt: DateTimeOffset.UnixEpoch,
            CompletedAt: DateTimeOffset.UnixEpoch.AddSeconds(10),
            ObservedSeconds: 10,
            BaselineReceiveMbps: 0.2,
            AverageAdjustedReceiveMbps: 80,
            PeakAdjustedReceiveMbps: 100,
            TotalReceiveBytes: 100_000_000,
            ActiveSampleCount: 8,
            PauseCount: 0,
            SuddenDropCount: 0,
            BssidChangeCount: 0,
            AdapterChangeCount: 0,
            CounterResetCount: 0,
            WlanDisconnectedSampleCount: 0,
            Confidence: "Medium",
            Message: "합성 관찰 결과",
            Limitation: "합성 한계",
            Samples: Array.Empty<ReportObservationSample>())
        {
            TerminationReason = reason
        };

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

    private static ReportFinding SingleByCode(
        IEnumerable<ReportFinding> findings,
        string code)
    {
        ReportFinding[] matches = findings
            .Where(item => item.Code.Equals(
                code,
                StringComparison.Ordinal))
            .ToArray();
        Ensure(matches.Length == 1,
            $"Finding {code}가 정확히 한 개여야 합니다. Actual={matches.Length}");
        return matches[0];
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
