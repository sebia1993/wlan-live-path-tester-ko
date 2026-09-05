using System.Runtime.CompilerServices;
using System.Text.Json;
using WlanLivePathTester.Core.NetworkEnvironment;
using WlanLivePathTester.Core.Proxy;
using WlanLivePathTester.Core.Reporting;
using WlanLivePathTester.Core.Routing;

namespace WlanLivePathTester.ReportSmoke;

internal static class
    InternalProxyRouteComparisonRunFindingMapperV2Tests
{
    private const string SecretInternalTarget =
        "https://internal-secret.example.invalid/private.bin";
    private const string SecretProxyHost =
        "proxy-secret.example.invalid";
    private const string SecretEmail =
        "route-finding@example.invalid";
    private const string SecretIp = "10.88.77.66";
    private const string SecretGuid =
        "33B2C3D4-E5F6-47A8-9123-1234567890AB";
    private const string InternalFingerprint = "0123456789";
    private const string ProxyFingerprint = "abcdef0123";

#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Run()
    {
        VerifyNonCompletedRunMatrix();
        VerifyCompletedComparisonMatrix();
        VerifyCompletedResultMissing();
        VerifyUnknownRunAndComparisonStates();
        VerifyStructuredEvidenceAndCountClamping();
        VerifyFreeFormAndIdentityFieldsAreNotReflected();
        Console.WriteLine(
            "PASS coordinated route run finding v2 matrix and privacy tests");
    }

    private static void VerifyNonCompletedRunMatrix()
    {
        (InternalProxyRouteComparisonRunStatus Status,
            string Code,
            string Severity)[] cases =
        [
            (
                InternalProxyRouteComparisonRunStatus.InvalidInput,
                "INTERNAL_PROXY_ROUTE_RUN_INVALID_INPUT",
                "Warning"),
            (
                InternalProxyRouteComparisonRunStatus
                    .ProxySourceBlocked,
                "INTERNAL_PROXY_ROUTE_RUN_SOURCE_BLOCKED",
                "Warning"),
            (
                InternalProxyRouteComparisonRunStatus
                    .ProxySourceUnavailable,
                "INTERNAL_PROXY_ROUTE_RUN_SOURCE_UNAVAILABLE",
                "Information"),
            (
                InternalProxyRouteComparisonRunStatus
                    .DirectPathSelected,
                "INTERNAL_PROXY_ROUTE_RUN_DIRECT_PRIMARY",
                "Information"),
            (
                InternalProxyRouteComparisonRunStatus
                    .InternalRouteUnavailable,
                "INTERNAL_PROXY_ROUTE_RUN_INTERNAL_UNAVAILABLE",
                "Warning"),
            (
                InternalProxyRouteComparisonRunStatus.Canceled,
                "INTERNAL_PROXY_ROUTE_RUN_CANCELED",
                "Information"),
            (
                InternalProxyRouteComparisonRunStatus.Failed,
                "INTERNAL_PROXY_ROUTE_RUN_FAILED",
                "Warning")
        ];

        foreach ((InternalProxyRouteComparisonRunStatus status,
                  string expectedCode,
                  string expectedSeverity) in cases)
        {
            InternalProxyRouteComparisonRunResult run = CreateRun(
                status,
                comparison: null);
            ReportFinding finding =
                InternalProxyRouteComparisonRunFindingMapper
                    .FromResult(run);

            Ensure(finding.Code == expectedCode,
                $"실행 상태 {status}의 Finding 코드가 잘못됐습니다.");
            Ensure(finding.Severity == expectedSeverity,
                $"실행 상태 {status}의 심각도가 잘못됐습니다.");
            Ensure(finding.Evidence.Contains(
                    $"실행 상태는 {status}",
                    StringComparison.Ordinal),
                $"실행 상태 {status}의 구조화 근거가 필요합니다.");
            Ensure(!string.IsNullOrWhiteSpace(finding.Title)
                   && !string.IsNullOrWhiteSpace(
                       finding.Interpretation)
                   && !string.IsNullOrWhiteSpace(
                       finding.Limitation)
                   && !string.IsNullOrWhiteSpace(finding.NextStep),
                $"실행 상태 {status}의 사람이 읽는 판정이 완전해야 합니다.");
        }
    }

    private static void VerifyCompletedComparisonMatrix()
    {
        (InternalProxyRouteComparisonStatus Status,
            string Code,
            string Severity)[] cases =
        [
            (
                InternalProxyRouteComparisonStatus.Ready,
                "INTERNAL_PROXY_ROUTE_SAME_INTERFACE",
                "Information"),
            (
                InternalProxyRouteComparisonStatus.Diverged,
                "INTERNAL_PROXY_ROUTE_DIVERGED",
                "Information"),
            (
                InternalProxyRouteComparisonStatus.Ambiguous,
                "INTERNAL_PROXY_ROUTE_AMBIGUOUS",
                "Warning"),
            (
                InternalProxyRouteComparisonStatus.Incomplete,
                "INTERNAL_PROXY_ROUTE_INCOMPLETE",
                "Warning")
        ];

        foreach ((InternalProxyRouteComparisonStatus status,
                  string expectedCode,
                  string expectedSeverity) in cases)
        {
            InternalProxyRouteComparisonResult comparison =
                CreateComparison(status);
            InternalProxyRouteComparisonRunResult run = CreateRun(
                InternalProxyRouteComparisonRunStatus.Completed,
                comparison);
            ReportFinding finding =
                InternalProxyRouteComparisonRunFindingMapper
                    .FromResult(run);

            Ensure(finding.Code == expectedCode,
                $"비교 상태 {status}의 Finding 코드가 잘못됐습니다.");
            Ensure(finding.Severity == expectedSeverity,
                $"비교 상태 {status}의 심각도가 잘못됐습니다.");
            Ensure(finding.Evidence.Contains(
                    $"비교 상태는 {status}",
                    StringComparison.Ordinal),
                $"비교 상태 {status}의 구조화 근거가 필요합니다.");
            Ensure(finding.Evidence.Contains(
                    $"관계는 {comparison.Relation}",
                    StringComparison.Ordinal)
                   && finding.Evidence.Contains(
                       $"원인 코드는 {comparison.Code}",
                       StringComparison.Ordinal),
                $"비교 상태 {status}의 관계와 원인 코드가 필요합니다.");
            Ensure(finding.Evidence.Contains(
                    comparison.ExactIdentityComparisonPerformed
                        ? "전체 인터페이스 ID 정확 비교는 수행했습니다"
                        : "전체 인터페이스 ID 정확 비교는 미수행했습니다",
                    StringComparison.Ordinal),
                $"비교 상태 {status}의 정확 ID 비교 여부가 필요합니다.");
        }
    }

    private static void VerifyCompletedResultMissing()
    {
        ReportFinding finding =
            InternalProxyRouteComparisonRunFindingMapper.FromResult(
                CreateRun(
                    InternalProxyRouteComparisonRunStatus.Completed,
                    comparison: null));

        Ensure(finding.Code
               == "INTERNAL_PROXY_ROUTE_RUN_RESULT_MISSING",
            "Completed 상태에 비교 결과가 없으면 전용 누락 코드가 필요합니다.");
        Ensure(finding.Severity == "Warning",
            "완료 결과 누락은 Warning이어야 합니다.");
        Ensure(finding.Interpretation.Contains(
                "구조화 비교 결과가 없어",
                StringComparison.Ordinal),
            "실행·결과 계약 불일치를 직접 설명해야 합니다.");
    }

    private static void VerifyUnknownRunAndComparisonStates()
    {
        InternalProxyRouteComparisonRunResult unknownRun =
            CreateRun(
                (InternalProxyRouteComparisonRunStatus)999,
                comparison: null);
        ReportFinding runFinding =
            InternalProxyRouteComparisonRunFindingMapper.FromResult(
                unknownRun);
        Ensure(runFinding.Code
               == "INTERNAL_PROXY_ROUTE_RUN_UNKNOWN"
               && runFinding.Severity == "Warning",
            "알 수 없는 실행 enum은 fail-closed Finding이어야 합니다.");
        Ensure(runFinding.Evidence.Contains(
                "실행 상태는 Unknown",
                StringComparison.Ordinal),
            "알 수 없는 실행 enum의 숫자값을 근거에 반사하면 안 됩니다.");

        InternalProxyRouteComparisonResult unknownComparison =
            CreateComparison(
                (InternalProxyRouteComparisonStatus)999);
        InternalProxyRouteComparisonRunResult completed = CreateRun(
            InternalProxyRouteComparisonRunStatus.Completed,
            unknownComparison);
        ReportFinding comparisonFinding =
            InternalProxyRouteComparisonRunFindingMapper.FromResult(
                completed);
        Ensure(comparisonFinding.Code
               == "INTERNAL_PROXY_ROUTE_RUN_RESULT_UNKNOWN"
               && comparisonFinding.Severity == "Warning",
            "알 수 없는 비교 enum은 별도 fail-closed Finding이어야 합니다.");
        Ensure(comparisonFinding.Evidence.Contains(
                "비교 상태는 Unknown",
                StringComparison.Ordinal),
            "알 수 없는 비교 enum의 숫자값을 근거에 반사하면 안 됩니다.");
    }

    private static void VerifyStructuredEvidenceAndCountClamping()
    {
        InternalProxyRouteComparisonRunResult run = CreateRun(
            InternalProxyRouteComparisonRunStatus.Completed,
            CreateComparison(
                InternalProxyRouteComparisonStatus.Ready)) with
        {
            TargetScheme = "HTTPS",
            ParsedProxyEndpointCount = -1,
            ApplicableProxyEndpointCount = -2,
            AnalyzedProxyEndpointCount = -3,
            SuccessfulProxyEndpointCount = -4,
            DistinctProxyInterfaceCount = -5
        };
        ReportFinding finding =
            InternalProxyRouteComparisonRunFindingMapper.FromResult(run);

        Ensure(finding.Evidence.Contains(
                "대상 스킴은 https",
                StringComparison.Ordinal),
            "HTTP(S) 대상 스킴을 소문자로 정규화해야 합니다.");
        Ensure(finding.Evidence.Contains(
                "파싱 후보 0개, 적용 후보 0개, 분석 후보 0개, 성공 후보 0개",
                StringComparison.Ordinal),
            "음수 후보 집계를 0으로 제한해야 합니다.");
        Ensure(finding.Evidence.Contains(
                "실행 계획은 AnalyzeProxyEndpoints·ManualProxySelected",
                StringComparison.Ordinal),
            "출처 선택과 실행 계획 코드를 구조화 근거에 유지해야 합니다.");
        Ensure(finding.Evidence.Contains(
                "프록시 실행 상태는 Completed",
                StringComparison.Ordinal)
               && finding.Evidence.Contains(
                   "프록시 경로 상태는 Success",
                   StringComparison.Ordinal),
            "실행 상태와 경로 분석 상태를 분리해 표시해야 합니다.");
    }

    private static void
        VerifyFreeFormAndIdentityFieldsAreNotReflected()
    {
        InternalProxyRouteComparisonResult comparison =
            CreateComparison(
                InternalProxyRouteComparisonStatus.Diverged) with
        {
            InternalInterfaceFingerprint = InternalFingerprint,
            ProxyInterfaceFingerprints = [ProxyFingerprint],
            Message = SecretInternalTarget,
            Interpretation = SecretProxyHost,
            Limitation = SecretEmail,
            NextStep = $"{SecretIp} {SecretGuid}"
        };
        InternalProxyRouteComparisonRunResult run = CreateRun(
            InternalProxyRouteComparisonRunStatus.Completed,
            comparison) with
        {
            TargetScheme = SecretInternalTarget,
            Message = $"{SecretInternalTarget} {SecretProxyHost}",
            Limitation = $"{SecretEmail} {SecretIp} {SecretGuid}"
        };
        ReportFinding finding =
            InternalProxyRouteComparisonRunFindingMapper.FromResult(run);
        string json = JsonSerializer.Serialize(finding);

        foreach (string secret in new[]
                 {
                     SecretInternalTarget,
                     "internal-secret.example.invalid",
                     SecretProxyHost,
                     SecretEmail,
                     SecretIp,
                     SecretGuid,
                     InternalFingerprint,
                     ProxyFingerprint
                 })
        {
            Ensure(!json.Contains(
                    secret,
                    StringComparison.OrdinalIgnoreCase),
                $"Finding이 자유형 원문 또는 식별 지문을 반사했습니다: {secret}");
        }

        Ensure(finding.Code
               == "INTERNAL_PROXY_ROUTE_DIVERGED",
            "자유형 필드와 무관하게 구조화 비교 상태의 코드를 사용해야 합니다.");
        Ensure(finding.Evidence.Contains(
                "대상 스킴은 없음",
                StringComparison.Ordinal),
            "허용되지 않는 대상 스킴 문자열을 근거에 반사하면 안 됩니다.");
    }

    private static InternalProxyRouteComparisonRunResult CreateRun(
        InternalProxyRouteComparisonRunStatus status,
        InternalProxyRouteComparisonResult? comparison) =>
        new(
            CompletedAt: DateTimeOffset.UnixEpoch.AddDays(30),
            Status: status,
            ProxySourceKind:
                ProxyDirectiveSourceKind.ManualProxyConfiguration,
            ProxySelectionStatus:
                ProxyDirectiveSourceSelectionStatus.Selected,
            ProxyPlanStatus:
                ProxyDirectiveRouteAnalysisPlanStatus
                    .AnalyzeProxyEndpoints,
            ProxyPlanCode:
                ProxyDirectiveRouteAnalysisPlanCode.ManualProxySelected,
            ProxyExecutionStatus:
                ProxyDirectiveRouteAnalysisExecutionStatus.Completed,
            ProxyEndpointSourceKind:
                ProxyEndpointSourceKind.ManualServerList,
            ProxyDecision:
                ProxyEndpointDecision.ProxyWithDirectFallback,
            TargetScheme: "https",
            InternalRouteStatus:
                DestinationRouteEvidenceStatus.Success,
            ProxyRouteStatus:
                ProxyEndpointRouteAnalysisStatus.Success,
            Comparison: comparison,
            ParsedProxyEndpointCount: 2,
            ApplicableProxyEndpointCount: 2,
            AnalyzedProxyEndpointCount: 2,
            SuccessfulProxyEndpointCount: 2,
            DistinctProxyInterfaceCount: 1,
            DirectPresent: true,
            DirectIsPrimary: false,
            DirectFallback: true,
            ProxyParseErrorsPresent: false,
            ExpectedWlanIdentityAvailable: true,
            InternalRouteReadPerformed: true,
            ProxyRouteAnalysisPerformed: true,
            Message: SecretInternalTarget,
            Limitation: SecretProxyHost,
            InternalRouteEvidence: null,
            ProxyExecution: null);

    private static InternalProxyRouteComparisonResult
        CreateComparison(
            InternalProxyRouteComparisonStatus status)
    {
        InternalProxyRouteRelation relation = status switch
        {
            InternalProxyRouteComparisonStatus.Ready =>
                InternalProxyRouteRelation.SameInterface,
            InternalProxyRouteComparisonStatus.Diverged =>
                InternalProxyRouteRelation.DifferentInterface,
            InternalProxyRouteComparisonStatus.Ambiguous =>
                InternalProxyRouteRelation.MultipleInterfaces,
            _ => InternalProxyRouteRelation.Unknown
        };
        InternalProxyRouteComparisonCode code = status switch
        {
            InternalProxyRouteComparisonStatus.Ready =>
                InternalProxyRouteComparisonCode.SameLocalInterface,
            InternalProxyRouteComparisonStatus.Diverged =>
                InternalProxyRouteComparisonCode
                    .DifferentLocalInterface,
            InternalProxyRouteComparisonStatus.Ambiguous =>
                InternalProxyRouteComparisonCode.ProxyRouteAmbiguous,
            _ => InternalProxyRouteComparisonCode
                .ProxyAnalysisIncomplete
        };

        return new InternalProxyRouteComparisonResult(
            EvaluatedAt: DateTimeOffset.UnixEpoch.AddDays(30),
            Status: status,
            Relation: relation,
            Code: code,
            InternalRouteStatus:
                DestinationRouteEvidenceStatus.Success,
            ProxyExecutionStatus:
                ProxyDirectiveRouteAnalysisExecutionStatus.Completed,
            ProxyAnalysisStatus:
                ProxyEndpointRouteAnalysisStatus.Success,
            ProxySourceKind:
                ProxyDirectiveSourceKind.ManualProxyConfiguration,
            ProxyPlanCode:
                ProxyDirectiveRouteAnalysisPlanCode.ManualProxySelected,
            InternalInterfaceFingerprint: InternalFingerprint,
            InternalInterfaceCategory:
                NetworkAdapterCategory.Wireless,
            ProxyInterfaceFingerprints: [ProxyFingerprint],
            ProxyInterfaceCategories:
                [NetworkAdapterCategory.Tunnel],
            ProxyApplicableEndpointCount: 2,
            ProxyAnalyzedEndpointCount: 2,
            ProxySuccessfulEndpointCount: 2,
            ProxyDistinctInterfaceCount: status
                == InternalProxyRouteComparisonStatus.Ambiguous
                    ? 2
                    : 1,
            ProxySkippedAfterDirectCount: 0,
            ProxyDirectPresent: true,
            ProxyDirectIsPrimary: false,
            ProxyDirectFallbackPresent: true,
            ProxyParseErrorsPresent: false,
            ExactIdentityComparisonPerformed: status is
                InternalProxyRouteComparisonStatus.Ready
                or InternalProxyRouteComparisonStatus.Diverged,
            Message: SecretInternalTarget,
            Interpretation: SecretProxyHost,
            Limitation: SecretEmail,
            NextStep: $"{SecretIp} {SecretGuid}");
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
