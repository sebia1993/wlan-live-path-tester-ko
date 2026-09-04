using System.Runtime.CompilerServices;
using System.Text.Json;
using WlanLivePathTester.Core.Configuration;
using WlanLivePathTester.Core.Models;

namespace WlanLivePathTester.SelfTest;

internal static class TargetConfigurationStrictnessTests
{
    [ModuleInitializer]
    internal static void Run()
    {
        RejectsUnknownProperty();
        AppliesSafePathDefaults();
        Console.WriteLine("PASS  승인 대상 JSON 엄격 검증과 경로 기본값");
    }

    private static void RejectsUnknownProperty()
    {
        const string json = """
        {
          "schemaVersion": 1,
          "defaults": {
            "timeoutSeconds": 30,
            "maxBytes": 10485760,
            "streams": 1,
            "maxRedirects": 3,
            "typoTimeout": 99
          },
          "internalTargets": [
            {
              "name": "내부 예시",
              "url": "http://192.0.2.10/test.bin"
            }
          ],
          "externalTargets": []
        }
        """;

        try
        {
            _ = TargetConfigurationLoader.LoadFromJson(json);
            throw new InvalidOperationException(
                "알 수 없는 설정 속성은 거부되어야 합니다.");
        }
        catch (JsonException)
        {
            // Expected: misspelled or unsupported configuration keys fail closed.
        }
    }

    private static void AppliesSafePathDefaults()
    {
        const string json = """
        {
          "schemaVersion": 1,
          "defaults": {
            "timeoutSeconds": 30,
            "maxBytes": 10485760,
            "streams": 1,
            "maxRedirects": 3
          },
          "internalTargets": [
            {
              "name": "내부 예시",
              "url": "http://192.0.2.10/test.bin"
            }
          ],
          "externalTargets": [
            {
              "name": "외부 예시",
              "url": "https://example.invalid/test.bin"
            }
          ]
        }
        """;

        IReadOnlyList<MeasurementTargetDefinition> targets =
            TargetConfigurationLoader.LoadFromJson(json);
        MeasurementTargetDefinition internalTarget = targets.Single(
            target => target.PathKind == NetworkPathKind.Internal);
        MeasurementTargetDefinition externalTarget = targets.Single(
            target => target.PathKind == NetworkPathKind.External);

        if (!internalTarget.RequireDirect || internalTarget.RequireProxy)
        {
            throw new InvalidOperationException(
                "내부 승인 대상은 기본적으로 DIRECT만 요구해야 합니다.");
        }

        if (!externalTarget.RequireProxy || externalTarget.RequireDirect)
        {
            throw new InvalidOperationException(
                "외부 승인 대상은 기본적으로 PROXY만 요구해야 합니다.");
        }
    }
}
