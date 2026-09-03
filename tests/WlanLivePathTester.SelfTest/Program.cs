using WlanLivePathTester.Core.Configuration;
using WlanLivePathTester.Core.Models;
using WlanLivePathTester.Core.Rules;
using WlanLivePathTester.Core.Security;

namespace WlanLivePathTester.SelfTest;

internal static class Program
{
    private static int Main()
    {
        (string Name, Action Test)[] tests =
        [
            ("외부 대상의 FTP 거부", RejectsFtp),
            ("URL 사용자정보 거부", RejectsUrlCredentials),
            ("외부 대상의 사설 IP 거부", RejectsPrivateExternalAddress),
            ("합성 설정 로드", LoadsSyntheticConfiguration),
            ("프록시 인증 실패 판정", DetectsProxyAuthenticationFailure),
            ("공통 외부 경로 저하 판정", DetectsCommonExternalPathDegradation)
        ];

        int failures = 0;

        foreach ((string name, Action test) in tests)
        {
            try
            {
                test();
                Console.WriteLine($"PASS  {name}");
            }
            catch (Exception exception)
            {
                failures++;
                Console.Error.WriteLine($"FAIL  {name}: {exception.Message}");
            }
        }

        Console.WriteLine($"총 {tests.Length}개, 실패 {failures}개");
        return failures == 0 ? 0 : 1;
    }

    private static void RejectsFtp()
    {
        MeasurementTargetDefinition target = CreateExternalTarget("ftp://example.invalid/file.bin");
        AssertContains(TargetValidator.Validate(target), "HTTP 또는 HTTPS");
    }

    private static void RejectsUrlCredentials()
    {
        MeasurementTargetDefinition target =
            CreateExternalTarget("https://user:password@example.invalid/file.bin");
        AssertContains(TargetValidator.Validate(target), "사용자 이름");
    }

    private static void RejectsPrivateExternalAddress()
    {
        MeasurementTargetDefinition target = CreateExternalTarget("https://192.168.10.10/file.bin");
        AssertContains(TargetValidator.Validate(target), "사설");
    }

    private static void LoadsSyntheticConfiguration()
    {
        const string json = """
        {
          "schemaVersion": 1,
          "defaults": {
            "timeoutSeconds": 30,
            "maxBytes": 104857600,
            "streams": 1,
            "maxRedirects": 5
          },
          "internalTargets": [
            {
              "name": "내부 예시",
              "url": "http://192.0.2.10/test.bin",
              "requireDirect": true
            }
          ],
          "externalTargets": [
            {
              "name": "외부 예시",
              "url": "https://example.invalid/test.bin",
              "requireProxy": true
            }
          ]
        }
        """;

        IReadOnlyList<MeasurementTargetDefinition> targets =
            TargetConfigurationLoader.LoadFromJson(json);

        Assert(targets.Count == 2, "두 개의 합성 대상이 로드되어야 합니다.");
        Assert(targets[0].PathKind == NetworkPathKind.Internal, "첫 대상은 내부망이어야 합니다.");
        Assert(targets[1].RequireProxy, "외부 대상은 프록시 필수여야 합니다.");
    }

    private static void DetectsProxyAuthenticationFailure()
    {
        DiagnosisEngine engine = new();
        IReadOnlyList<DiagnosisFinding> findings = engine.Evaluate(
            CreateConnectedWlan(),
            internalMeasurement: null,
            externalMeasurements:
            [
                new DownloadMeasurement(
                    "외부 A",
                    NetworkPathKind.External,
                    MeasurementStatus.ProxyAuthenticationRequired,
                    0,
                    TimeSpan.Zero,
                    null,
                    407,
                    true,
                    "HTTP_407")
            ]);

        Assert(
            findings.Any(item => item.Code == "PROXY_AUTHENTICATION_REQUIRED"),
            "프록시 인증 실패 코드가 필요합니다.");
    }

    private static void DetectsCommonExternalPathDegradation()
    {
        DiagnosisEngine engine = new();
        DownloadMeasurement internalResult = new(
            "내부",
            NetworkPathKind.Internal,
            MeasurementStatus.Success,
            100_000_000,
            TimeSpan.FromSeconds(2),
            400,
            200,
            false,
            null);

        DownloadMeasurement[] externalResults =
        [
            new(
                "외부 A",
                NetworkPathKind.External,
                MeasurementStatus.Success,
                100_000_000,
                TimeSpan.FromSeconds(20),
                40,
                200,
                true,
                null),
            new(
                "외부 B",
                NetworkPathKind.External,
                MeasurementStatus.Success,
                100_000_000,
                TimeSpan.FromSeconds(25),
                32,
                200,
                true,
                null)
        ];

        IReadOnlyList<DiagnosisFinding> findings =
            engine.Evaluate(CreateConnectedWlan(), internalResult, externalResults);

        Assert(
            findings.Any(item => item.Code == "COMMON_EXTERNAL_PATH_DEGRADED"),
            "복수 외부 대상의 공통 경로 저하 코드가 필요합니다.");
    }

    private static MeasurementTargetDefinition CreateExternalTarget(string url) =>
        new(
            Name: "외부 예시",
            Url: url,
            PathKind: NetworkPathKind.External,
            RequireProxy: true,
            RequireDirect: false,
            MaxBytes: 100 * 1024 * 1024,
            TimeoutSeconds: 30,
            Streams: 1,
            MaxRedirects: 5);

    private static WlanSnapshot CreateConnectedWlan() =>
        new(
            Timestamp: DateTimeOffset.UnixEpoch,
            IsConnected: true,
            Ssid: "SYNTHETIC-SSID",
            Bssid: "00:00:00:00:00:00",
            RssiDbm: -55,
            Channel: 36,
            PhyType: "802.11ax",
            ReceiveLinkSpeedBps: 1_200_000_000,
            TransmitLinkSpeedBps: 1_200_000_000);

    private static void AssertContains(IEnumerable<string> values, string fragment)
    {
        Assert(
            values.Any(value => value.Contains(fragment, StringComparison.Ordinal)),
            $"'{fragment}' 문구가 필요합니다.");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
