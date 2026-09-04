using System.Runtime.CompilerServices;
using WlanLivePathTester.Core.Configuration;
using WlanLivePathTester.Core.Models;
using WlanLivePathTester.Core.Security;

namespace WlanLivePathTester.SelfTest;

internal static class AdministratorApprovedTargetPolicyTests
{
#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Run()
    {
        try
        {
            VerifyEnforcementFlagLoads();
            VerifyEnforcedCatalogRejectsUnapprovedTarget();
            VerifyBlockedAdministratorPolicyRejectsAllTargets();
            VerifyAdvisoryCatalogCanBeClearedForManualMode();
            Console.WriteLine("PASS administrator approved target policy tests");
        }
        finally
        {
            ApprovedTargetRuntimeCatalog.Clear();
        }
    }

    private static void VerifyEnforcementFlagLoads()
    {
        LoadedTargetConfiguration loaded =
            TargetConfigurationLoader.LoadWithPolicyFromJson(
                CreateConfigurationJson(enforceApprovedTargets: true));

        Ensure(loaded.EnforceApprovedTargets,
            "관리자 강제 승인 정책 플래그를 로드해야 합니다.");
        Ensure(loaded.Targets.Count == 2,
            "내부·외부 승인 대상 두 개를 로드해야 합니다.");
        Ensure(loaded.Targets.Single(target =>
                target.PathKind == NetworkPathKind.Internal).RequireDirect,
            "내부 대상은 기본 DIRECT 필수여야 합니다.");
        Ensure(loaded.Targets.Single(target =>
                target.PathKind == NetworkPathKind.External).RequireProxy,
            "외부 대상은 기본 PROXY 필수여야 합니다.");
    }

    private static void VerifyEnforcedCatalogRejectsUnapprovedTarget()
    {
        LoadedTargetConfiguration loaded =
            TargetConfigurationLoader.LoadWithPolicyFromJson(
                CreateConfigurationJson(enforceApprovedTargets: true));
        ApprovedTargetRuntimeCatalog.Configure(
            loaded.Targets,
            enforceApprovedTargets: true,
            sourceDescription: "합성 관리자 설정");

        MeasurementTargetDefinition approved = loaded.Targets
            .Single(target => target.PathKind == NetworkPathKind.External);
        Ensure(TargetValidator.Validate(approved).Count == 0,
            "등록된 외부 대상은 관리자 강제 정책을 통과해야 합니다.");

        MeasurementTargetDefinition unapproved = approved with
        {
            Name = "미승인 외부 대상",
            Url = "https://other.example.invalid/test.bin"
        };
        IReadOnlyList<string> errors = TargetValidator.Validate(unapproved);
        Ensure(errors.Any(error => error.Contains(
                "관리자 강제 승인 대상 목록",
                StringComparison.Ordinal)),
            "미승인 URL은 관리자 강제 정책 오류로 차단해야 합니다.");
    }

    private static void VerifyBlockedAdministratorPolicyRejectsAllTargets()
    {
        ApprovedTargetRuntimeCatalog.BlockEnforcedPolicy(
            "합성 관리자 설정",
            "합성 JSON 오류");

        MeasurementTargetDefinition target = new(
            Name: "차단 확인",
            Url: "https://example.invalid/test.bin",
            PathKind: NetworkPathKind.External,
            RequireProxy: true,
            RequireDirect: false,
            MaxBytes: 100 * 1024 * 1024,
            TimeoutSeconds: 30,
            Streams: 1,
            MaxRedirects: 3);

        IReadOnlyList<string> errors = TargetValidator.Validate(target);
        Ensure(errors.Any(error => error.Contains(
                "모든 다운로드 측정을 차단",
                StringComparison.Ordinal)),
            "손상된 관리자 정책은 모든 대상을 fail-closed 방식으로 차단해야 합니다.");

        ApprovedTargetRuntimePolicyStatus status =
            ApprovedTargetRuntimeCatalog.GetStatus();
        Ensure(status.IsEnforced && status.IsBlocked,
            "차단된 관리자 강제 정책 상태를 명확히 노출해야 합니다.");
    }

    private static void VerifyAdvisoryCatalogCanBeClearedForManualMode()
    {
        LoadedTargetConfiguration loaded =
            TargetConfigurationLoader.LoadWithPolicyFromJson(
                CreateConfigurationJson(enforceApprovedTargets: false));
        ApprovedTargetRuntimeCatalog.Configure(
            loaded.Targets,
            enforceApprovedTargets: false,
            sourceDescription: "합성 사용자 설정");
        Ensure(ApprovedTargetRuntimeCatalog.IsActive,
            "승인 목록 모드에서는 런타임 카탈로그가 활성화되어야 합니다.");

        ApprovedTargetRuntimeCatalog.Clear();
        Ensure(!ApprovedTargetRuntimeCatalog.IsActive,
            "비강제 승인 목록은 수동 모드 전환 시 해제할 수 있어야 합니다.");
    }

    private static string CreateConfigurationJson(
        bool enforceApprovedTargets) =>
        $$"""
        {
          "schemaVersion": 1,
          "enforceApprovedTargets": {{enforceApprovedTargets.ToString().ToLowerInvariant()}},
          "defaults": {
            "timeoutSeconds": 30,
            "maxBytes": 104857600,
            "streams": 1,
            "maxRedirects": 3
          },
          "internalTargets": [
            {
              "name": "내부 승인 대상",
              "url": "http://192.0.2.10/test.bin",
              "allowedRedirectHosts": []
            }
          ],
          "externalTargets": [
            {
              "name": "외부 승인 대상",
              "url": "https://example.invalid/test.bin",
              "allowedRedirectHosts": [
                "cdn.example.invalid"
              ]
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
