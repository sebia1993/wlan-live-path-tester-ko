using WlanLivePathTester.Core.Configuration;
using WlanLivePathTester.Core.Models;
using WlanLivePathTester.Core.Proxy;
using WlanLivePathTester.Core.Rules;
using WlanLivePathTester.Core.Security;
using WlanLivePathTester.Core.Wlan;

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
            ("합성 설정과 승인 리다이렉트 로드", LoadsSyntheticConfiguration),
            ("동일 호스트 리다이렉트 허용", AllowsSameHostRedirect),
            ("미승인 리다이렉트 호스트 거부", RejectsUnapprovedRedirectHost),
            ("승인된 리다이렉트 호스트 허용", AllowsApprovedRedirectHost),
            ("승인 호스트 비표준 포트 거부", RejectsNonDefaultRedirectPort),
            ("외부 승인 목록의 사설 IP 거부", RejectsPrivateAllowedRedirectHost),
            ("프록시 인증 실패 판정", DetectsProxyAuthenticationFailure),
            ("공통 외부 경로 저하 판정", DetectsCommonExternalPathDegradation),
            ("2.4 GHz 채널 변환", Converts24GhzChannel),
            ("5 GHz 채널 변환", Converts5GhzChannel),
            ("6 GHz 채널 변환", Converts6GhzChannel),
            ("알 수 없는 주파수 거부", RejectsUnknownFrequency),
            ("수동 공통 프록시 선택", SelectsCatchAllManualProxy),
            ("프로토콜별 프록시 선택", SelectsProtocolSpecificProxy),
            ("미설정 프로토콜 DIRECT 처리", UsesDirectWhenSchemeIsNotConfigured),
            ("로컬 이름 바이패스", BypassesLocalHostName),
            ("와일드카드 도메인 바이패스", BypassesWildcardDomain),
            ("프록시와 DIRECT fallback 파싱", ParsesProxyWithDirectFallback),
            ("잘못된 프록시 설정 판단 불가", RejectsMalformedProxyConfiguration),
            ("내부·외부 기대 경로 판정", EvaluatesProxyPathExpectation)
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
              "requireProxy": true,
              "allowedRedirectHosts": [
                "cdn.example.invalid"
              ]
            }
          ]
        }
        """;

        IReadOnlyList<MeasurementTargetDefinition> targets =
            TargetConfigurationLoader.LoadFromJson(json);

        Assert(targets.Count == 2, "두 개의 합성 대상이 로드되어야 합니다.");
        Assert(targets[0].PathKind == NetworkPathKind.Internal, "첫 대상은 내부망이어야 합니다.");
        Assert(targets[1].RequireProxy, "외부 대상은 프록시 필수여야 합니다.");
        Assert(
            targets[1].AllowedRedirectHosts?.Single()
                == "cdn.example.invalid",
            "승인 리다이렉트 호스트를 로드해야 합니다.");
    }

    private static void AllowsSameHostRedirect()
    {
        MeasurementTargetDefinition target =
            CreateExternalTarget("https://example.invalid/start.bin");
        TargetHostPolicyResult result = TargetHostPolicy.EvaluateRedirect(
            target,
            new Uri("https://example.invalid/next.bin"));

        Assert(result.IsAllowed, "동일 호스트의 HTTPS 리다이렉트는 허용해야 합니다.");
    }

    private static void RejectsUnapprovedRedirectHost()
    {
        MeasurementTargetDefinition target =
            CreateExternalTarget("https://example.invalid/start.bin");
        TargetHostPolicyResult result = TargetHostPolicy.EvaluateRedirect(
            target,
            new Uri("https://other.example.invalid/next.bin"));

        Assert(!result.IsAllowed, "미승인 교차 호스트 리다이렉트를 거부해야 합니다.");
        Assert(result.ErrorCode == "REDIRECT_HOST_NOT_APPROVED",
            "미승인 호스트 오류 코드가 필요합니다.");
    }

    private static void AllowsApprovedRedirectHost()
    {
        MeasurementTargetDefinition target = CreateExternalTarget(
            "https://example.invalid/start.bin",
            ["cdn.example.invalid"]);
        TargetHostPolicyResult result = TargetHostPolicy.EvaluateRedirect(
            target,
            new Uri("https://cdn.example.invalid/next.bin"));

        Assert(result.IsAllowed, "설정에 등록한 CDN 호스트는 허용해야 합니다.");
    }

    private static void RejectsNonDefaultRedirectPort()
    {
        MeasurementTargetDefinition target = CreateExternalTarget(
            "https://example.invalid/start.bin",
            ["cdn.example.invalid"]);
        TargetHostPolicyResult result = TargetHostPolicy.EvaluateRedirect(
            target,
            new Uri("https://cdn.example.invalid:8443/next.bin"));

        Assert(!result.IsAllowed, "승인 호스트라도 비표준 포트는 거부해야 합니다.");
        Assert(result.ErrorCode == "REDIRECT_PORT_NOT_APPROVED",
            "비표준 포트 오류 코드가 필요합니다.");
    }

    private static void RejectsPrivateAllowedRedirectHost()
    {
        MeasurementTargetDefinition target = CreateExternalTarget(
            "https://example.invalid/start.bin",
            ["192.168.10.10"]);

        AssertContains(
            TargetValidator.Validate(target),
            "allowedRedirectHosts");
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

    private static void Converts24GhzChannel()
    {
        Assert(
            WlanChannelCalculator.FromCenterFrequencyMhz(2412) == 1,
            "2412 MHz는 채널 1이어야 합니다.");
        Assert(
            WlanChannelCalculator.FromCenterFrequencyMhz(2484) == 14,
            "2484 MHz는 채널 14여야 합니다.");
    }

    private static void Converts5GhzChannel()
    {
        Assert(
            WlanChannelCalculator.FromCenterFrequencyMhz(5180) == 36,
            "5180 MHz는 채널 36이어야 합니다.");
    }

    private static void Converts6GhzChannel()
    {
        Assert(
            WlanChannelCalculator.FromCenterFrequencyMhz(5955) == 1,
            "5955 MHz는 6 GHz 채널 1이어야 합니다.");
        Assert(
            WlanChannelCalculator.FromCenterFrequencyMhz(5935) == 2,
            "5935 MHz는 6 GHz 채널 2여야 합니다.");
    }

    private static void RejectsUnknownFrequency()
    {
        Assert(
            WlanChannelCalculator.FromCenterFrequencyMhz(3000) is null,
            "지원하지 않는 주파수는 채널을 만들지 않아야 합니다.");
    }

    private static void SelectsCatchAllManualProxy()
    {
        ProxySelection selection = ProxyDirectiveParser.SelectManual(
            new Uri("https://service.example.invalid/resource"),
            "proxy.example.invalid:8080",
            bypassList: null);

        Assert(selection.RouteKind == ProxyRouteKind.Proxy, "수동 프록시를 선택해야 합니다.");
        Assert(selection.ProxyCandidateCount == 1, "프록시 후보가 하나여야 합니다.");
        Assert(
            selection.ProxyUris[0] == "http://proxy.example.invalid:8080",
            "프록시 주소를 정규화해야 합니다.");
    }

    private static void SelectsProtocolSpecificProxy()
    {
        ProxySelection selection = ProxyDirectiveParser.SelectManual(
            new Uri("https://service.example.invalid/resource"),
            "http=proxy-http.example.invalid:8080;https=proxy-https.example.invalid:8443",
            bypassList: null);

        Assert(selection.RouteKind == ProxyRouteKind.Proxy, "HTTPS 프록시를 선택해야 합니다.");
        Assert(
            selection.ProxyUris.Single() == "http://proxy-https.example.invalid:8443",
            "대상 URL 스킴의 프록시만 선택해야 합니다.");
    }

    private static void UsesDirectWhenSchemeIsNotConfigured()
    {
        ProxySelection selection = ProxyDirectiveParser.SelectManual(
            new Uri("https://service.example.invalid/resource"),
            "http=proxy-http.example.invalid:8080",
            bypassList: null);

        Assert(selection.RouteKind == ProxyRouteKind.Direct, "HTTPS 설정이 없으면 DIRECT여야 합니다.");
    }

    private static void BypassesLocalHostName()
    {
        ProxySelection selection = ProxyDirectiveParser.SelectManual(
            new Uri("http://intranet/resource"),
            "proxy.example.invalid:8080",
            "<local>");

        Assert(selection.RouteKind == ProxyRouteKind.Direct, "로컬 이름은 DIRECT여야 합니다.");
        Assert(selection.WasBypassed, "바이패스에 의한 DIRECT임을 기록해야 합니다.");
    }

    private static void BypassesWildcardDomain()
    {
        ProxySelection selection = ProxyDirectiveParser.SelectManual(
            new Uri("https://app.corp.invalid/resource"),
            "proxy.example.invalid:8080",
            "*.corp.invalid");

        Assert(selection.RouteKind == ProxyRouteKind.Direct, "와일드카드 도메인은 바이패스해야 합니다.");
        Assert(selection.WasBypassed, "와일드카드 바이패스를 기록해야 합니다.");
    }

    private static void ParsesProxyWithDirectFallback()
    {
        ProxySelection selection = ProxyDirectiveParser.SelectAutoProxyList(
            new Uri("https://service.example.invalid/resource"),
            "PROXY proxy-a.example.invalid:8080; PROXY proxy-b.example.invalid:8080; DIRECT");

        Assert(selection.RouteKind == ProxyRouteKind.Proxy, "첫 경로는 프록시여야 합니다.");
        Assert(selection.ProxyCandidateCount == 2, "프록시 후보가 두 개여야 합니다.");
        Assert(selection.HasDirectFallback, "DIRECT fallback을 기록해야 합니다.");
    }

    private static void RejectsMalformedProxyConfiguration()
    {
        ProxySelection selection = ProxyDirectiveParser.SelectManual(
            new Uri("https://service.example.invalid/resource"),
            "https=://bad-proxy",
            bypassList: null);

        Assert(selection.RouteKind == ProxyRouteKind.Unknown, "잘못된 설정은 판단 불가여야 합니다.");
        Assert(selection.InvalidDirectiveCount == 1, "잘못된 지시문 수를 기록해야 합니다.");
    }

    private static void EvaluatesProxyPathExpectation()
    {
        Assert(
            ProxyRouteExpectationEvaluator.Evaluate(
                NetworkPathKind.Internal,
                ProxyRouteKind.Direct) == ProxyPathExpectation.Match,
            "내부망 DIRECT는 기대 경로 일치여야 합니다.");
        Assert(
            ProxyRouteExpectationEvaluator.Evaluate(
                NetworkPathKind.External,
                ProxyRouteKind.Proxy) == ProxyPathExpectation.Match,
            "외부망 PROXY는 기대 경로 일치여야 합니다.");
        Assert(
            ProxyRouteExpectationEvaluator.Evaluate(
                NetworkPathKind.External,
                ProxyRouteKind.Direct) == ProxyPathExpectation.Mismatch,
            "외부망 DIRECT는 기대 경로 불일치여야 합니다.");
        Assert(
            ProxyRouteExpectationEvaluator.Evaluate(
                NetworkPathKind.Internal,
                ProxyRouteKind.Unknown) == ProxyPathExpectation.Unknown,
            "판단 불가 경로는 기대 여부도 판단 불가여야 합니다.");
    }

    private static MeasurementTargetDefinition CreateExternalTarget(
        string url,
        IReadOnlyList<string>? allowedRedirectHosts = null) =>
        new(
            Name: "외부 예시",
            Url: url,
            PathKind: NetworkPathKind.External,
            RequireProxy: true,
            RequireDirect: false,
            MaxBytes: 100 * 1024 * 1024,
            TimeoutSeconds: 30,
            Streams: 1,
            MaxRedirects: 5,
            AllowedRedirectHosts: allowedRedirectHosts);

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
