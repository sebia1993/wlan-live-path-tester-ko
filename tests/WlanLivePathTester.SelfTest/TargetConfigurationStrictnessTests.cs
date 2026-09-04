using System.Runtime.CompilerServices;
using System.Text.Json;
using WlanLivePathTester.Core.Configuration;
using WlanLivePathTester.Core.Models;
using WlanLivePathTester.Core.Security;

namespace WlanLivePathTester.SelfTest;

internal static class TargetConfigurationStrictnessTests
{
    [ModuleInitializer]
    internal static void Run()
    {
        RejectsUnknownProperty();
        AppliesSafePathDefaults();
        EnforcesActiveApprovedCatalog();
        Console.WriteLine("PASS  승인 대상 JSON 엄격 검증과 실행 경계");
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

    private static void EnforcesActiveApprovedCatalog()
    {
        MeasurementTargetDefinition approved = new(
            Name: "승인 외부 대상",
            Url: "https://example.invalid/approved.bin",
            PathKind: NetworkPathKind.External,
            RequireProxy: true,
            RequireDirect: false,
            MaxBytes: 10 * 1024 * 1024,
            TimeoutSeconds: 30,
            Streams: 1,
            MaxRedirects: 3,
            AllowedRedirectHosts: ["cdn.example.invalid"]);

        ApprovedTargetRuntimeCatalog.Replace([approved]);
        try
        {
            MeasurementTargetDefinition unapproved = approved with
            {
                Url = "https://other.example.invalid/file.bin"
            };
            IReadOnlyList<string> unapprovedErrors =
                TargetValidator.Validate(unapproved);
            if (!unapprovedErrors.Any(error => error.Contains(
                    "승인 대상 목록",
                    StringComparison.Ordinal)))
            {
                throw new InvalidOperationException(
                    "활성 승인 목록에 없는 URL을 차단해야 합니다.");
            }

            MeasurementTargetDefinition modifiedLimit = approved with
            {
                MaxBytes = approved.MaxBytes * 2
            };
            IReadOnlyList<string> limitErrors =
                TargetValidator.Validate(modifiedLimit);
            if (!limitErrors.Any(error => error.Contains(
                    "승인 대상 설정과 일치하지 않습니다",
                    StringComparison.Ordinal)))
            {
                throw new InvalidOperationException(
                    "승인된 URL의 실행 제한값 변경을 차단해야 합니다.");
            }

            if (TargetValidator.Validate(approved).Count != 0)
            {
                throw new InvalidOperationException(
                    "승인된 원본 대상은 검증을 통과해야 합니다.");
            }
        }
        finally
        {
            ApprovedTargetRuntimeCatalog.Clear();
        }
    }
}
