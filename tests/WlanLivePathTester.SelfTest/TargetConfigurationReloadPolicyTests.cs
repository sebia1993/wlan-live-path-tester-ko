using System.Runtime.CompilerServices;
using WlanLivePathTester.Core.Configuration;
using WlanLivePathTester.Core.Models;

namespace WlanLivePathTester.SelfTest;

internal static class TargetConfigurationReloadPolicyTests
{
#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Run()
    {
        try
        {
            LoadedTargetConfiguration first =
                TargetConfigurationLoader.LoadWithPolicyFromJson(
                    CreateJson("https://first.example.invalid/test.bin"));
            ApprovedTargetRuntimeCatalog.Configure(
                first.Targets,
                enforceApprovedTargets: true,
                sourceDescription: "첫 관리자 설정");

            LoadedTargetConfiguration second =
                TargetConfigurationLoader.LoadWithPolicyFromJson(
                    CreateJson("https://second.example.invalid/test.bin"));

            Ensure(second.Targets.Any(target =>
                    target.PathKind == NetworkPathKind.External
                    && target.Url.Equals(
                        "https://second.example.invalid/test.bin",
                        StringComparison.OrdinalIgnoreCase)),
                "현재 강제 정책과 다른 새 관리자 설정도 정의 검증 단계에서 정상적으로 로드되어야 합니다.");

            Console.WriteLine("PASS target configuration reload outside runtime policy");
        }
        finally
        {
            ApprovedTargetRuntimeCatalog.Clear();
        }
    }

    private static string CreateJson(string externalUrl) =>
        $$"""
        {
          "schemaVersion": 1,
          "enforceApprovedTargets": true,
          "defaults": {
            "timeoutSeconds": 30,
            "maxBytes": 104857600,
            "streams": 1,
            "maxRedirects": 3
          },
          "internalTargets": [
            {
              "name": "내부 승인 대상",
              "url": "http://192.0.2.10/test.bin"
            }
          ],
          "externalTargets": [
            {
              "name": "외부 승인 대상",
              "url": "{{externalUrl}}"
            }
          ]
        }
        """;

    private static void Ensure(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
