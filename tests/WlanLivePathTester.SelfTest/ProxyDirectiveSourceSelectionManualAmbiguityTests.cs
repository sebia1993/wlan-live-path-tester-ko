using System.Runtime.CompilerServices;
using WlanLivePathTester.Core.Proxy;

namespace WlanLivePathTester.SelfTest;

internal static class ProxyDirectiveSourceSelectionManualAmbiguityTests
{
#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Run()
    {
        ProxyDirectiveSourceSelectionResult result =
            ProxyDirectiveSourceSelectionPolicy.Select(
                targetDecisionWasEvaluated: false,
                targetDecisionIsDirect: false,
                targetSpecificDirective: null,
                manualProxyConfigured: true,
                manualProxyDirective:
                    "DIRECT; UNKNOWN possibly-proxy.example.invalid:8080");

        Ensure(result.Status
               == ProxyDirectiveSourceSelectionStatus.Invalid,
            "DIRECT와 해석 불가 구간이 섞인 수동 설정은 Invalid여야 합니다.");
        Ensure(result.SourceKind
               == ProxyDirectiveSourceKind.ManualProxyConfiguration,
            "오류 출처는 수동 프록시 설정이어야 합니다.");
        Ensure(result.Code
               == ProxyDirectiveSourceSelectionCode
                   .ManualConfigurationInvalid,
            "수동 설정 오류 코드를 사용해야 합니다.");
        Ensure(!result.HasUsableSelection
               && result.SelectedDirectiveText is null,
            "모호한 수동 설정을 실행 가능한 DIRECT로 축소하면 안 됩니다.");
        Ensure(result.DirectDirectiveCount == 1
               && result.ProxyEndpointCount == 0,
            "안전한 진단을 위해 파싱된 DIRECT 개수는 유지할 수 있습니다.");
        Ensure(result.ParseResult?.Issues.Any(issue =>
                issue.Severity
                    == ProxyDirectiveIssueSeverity.Error) == true,
            "해석 불가 구간의 구조화 오류를 유지해야 합니다.");
        Ensure(result.Message.Contains(
                "DIRECT-only 정책으로 축소하지 않았습니다",
                StringComparison.Ordinal),
            "숨은 프록시 후보를 놓치지 않는 fail-closed 설명이 필요합니다.");

        Console.WriteLine(
            "PASS manual proxy DIRECT ambiguity fail-closed test");
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
