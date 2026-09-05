using WlanLivePathTester.Core.NetworkEnvironment;
using WlanLivePathTester.Core.Proxy;

namespace WlanLivePathTester.Core.Routing;

public static class InternalProxyRouteComparisonEvaluator
{
    public static InternalProxyRouteComparisonResult Evaluate(
        DestinationRouteEvidence? internalDirectRoute,
        ProxyDirectiveRouteAnalysisExecutionResult<
            ProxyEndpointRouteAnalysisResult>? proxyExecution,
        DateTimeOffset? evaluatedAt = null)
    {
        ComparisonSnapshot snapshot = CreateSnapshot(
            internalDirectRoute,
            proxyExecution);
        DateTimeOffset timestamp = evaluatedAt ?? DateTimeOffset.UtcNow;

        if (internalDirectRoute is null)
        {
            return Incomplete(
                timestamp,
                snapshot,
                InternalProxyRouteComparisonCode.InternalRouteMissing,
                "내부 DIRECT 대상의 Windows 로컬 경로 근거가 없습니다.",
                "내부 기준 경로가 없어 프록시 엔드포인트 경로와 비교하지 않았습니다.",
                "프록시 경로만으로 내부망과 외부망의 첫 로컬 인터페이스 차이를 판단할 수 없습니다.",
                "승인된 내부 DIRECT 대상의 로컬 경로를 먼저 확인하십시오.");
        }

        if (internalDirectRoute.Purpose
            != RouteProbePurpose.InternalDirectTarget)
        {
            return Incomplete(
                timestamp,
                snapshot,
                InternalProxyRouteComparisonCode.InternalPurposeMismatch,
                "비교 입력이 내부 DIRECT 대상 용도로 수집된 경로가 아닙니다.",
                "일반 목적지나 프록시 엔드포인트 근거를 내부 기준 경로로 재사용하지 않았습니다.",
                "Purpose 값은 실제 PAC 바이패스를 증명하지 않지만 서로 다른 의미의 경로를 잘못 비교하지 않기 위한 경계입니다.",
                "회사 정책상 DIRECT인 내부 대상을 InternalDirectTarget 용도로 다시 확인하십시오.");
        }

        if (internalDirectRoute.Status
            == DestinationRouteEvidenceStatus.MultipleInterfaces)
        {
            return Ambiguous(
                timestamp,
                snapshot,
                InternalProxyRouteComparisonCode.InternalRouteAmbiguous,
                "내부 DIRECT 대상의 주소별 Windows 최적 경로가 여러 인터페이스를 선택했습니다.",
                "내부 기준 경로가 하나로 확정되지 않아 프록시 경로와 단일 NIC 비교를 수행하지 않았습니다.",
                "IPv4·IPv6가 서로 다른 인터페이스를 선택했거나 수집 중 라우팅 상태가 변했을 수 있습니다.",
                "내부 대상의 IPv4·IPv6 경로와 VPN·유선·무선 우선순위를 각각 확인하십시오.");
        }

        if (proxyExecution is null)
        {
            return Incomplete(
                timestamp,
                snapshot,
                InternalProxyRouteComparisonCode.ProxyExecutionMissing,
                "프록시 출처 선택과 로컬 경로 분석 실행 결과가 없습니다.",
                "내부 DIRECT 경로만으로 프록시 엔드포인트의 첫 로컬 인터페이스를 비교할 수 없습니다.",
                "Windows 프록시 설정 존재 여부만으로 실제 대상 URL에 적용된 프록시 경로를 알 수 없습니다.",
                "대상별 PAC/WPAD 또는 수동 프록시 출처를 선택한 뒤 경로 분석을 실행하십시오.");
        }

        InternalProxyRouteComparisonResult? terminal =
            EvaluateExecutionTerminal(
                timestamp,
                snapshot,
                proxyExecution);
        if (terminal is not null)
        {
            return terminal;
        }

        ProxyEndpointRouteAnalysisResult? proxyAnalysis =
            proxyExecution.Analysis;
        if (proxyAnalysis is null)
        {
            return Incomplete(
                timestamp,
                snapshot,
                InternalProxyRouteComparisonCode.ProxyAnalysisMissing,
                "완료된 프록시 실행 결과에 메모리 경로 분석 객체가 없습니다.",
                "직렬화된 실행 요약이나 손상된 실행 객체를 정확 경로 비교에 사용하지 않았습니다.",
                "정확 비교에는 같은 실행 세션에서 유지된 프록시 후보별 메모리 근거가 필요합니다.",
                "프록시 엔드포인트 경로 분석을 다시 실행한 직후 비교하십시오.");
        }

        if (proxyAnalysis.Status
                == ProxyEndpointRouteAnalysisStatus.MultipleInterfaces
            || proxyAnalysis.DistinctInterfaceCount > 1
            || proxyAnalysis.Endpoints.Any(endpoint =>
                endpoint.RouteStatus
                    == DestinationRouteEvidenceStatus.MultipleInterfaces))
        {
            return Ambiguous(
                timestamp,
                snapshot,
                InternalProxyRouteComparisonCode.ProxyRouteAmbiguous,
                "확인된 프록시 엔드포인트가 둘 이상의 Windows 로컬 인터페이스를 선택했습니다.",
                "프록시 fallback 후보 또는 주소 계열마다 첫 로컬 송출 NIC가 달라 하나의 프록시 경로로 요약하지 않았습니다.",
                "실제 요청이 어느 후보와 주소 계열을 사용했는지는 이 비교만으로 알 수 없습니다.",
                "후보별 IPv4·IPv6 경로, VPN·터널과 인터페이스 우선순위를 확인하십시오.");
        }

        if (proxyAnalysis.Status
            == ProxyEndpointRouteAnalysisStatus.DirectPathSelected)
        {
            return Incomplete(
                timestamp,
                snapshot,
                InternalProxyRouteComparisonCode.ProxyDirectPathSelected,
                "외부 대상에서 DIRECT가 첫 적용 경로여서 비교할 프록시 엔드포인트 경로가 없습니다.",
                "프록시 서버까지의 로컬 경로가 없으므로 내부 DIRECT 경로와 프록시 경로 관계를 판단하지 않았습니다.",
                "이 DIRECT 판정은 해당 대상 URL과 수집 시점에 한정됩니다.",
                "프록시가 실제 적용되는 승인된 외부 대상의 판정 결과로 다시 실행하십시오.");
        }

        if (proxyAnalysis.Status
            == ProxyEndpointRouteAnalysisStatus.NoApplicableEndpoint)
        {
            return Incomplete(
                timestamp,
                snapshot,
                InternalProxyRouteComparisonCode.ProxyEndpointMissing,
                "현재 대상 스킴에 적용되는 프록시 엔드포인트가 없습니다.",
                "수동 프록시 범위와 대상 URL이 일치하지 않아 비교할 프록시 경로가 생성되지 않았습니다.",
                "다른 스킴의 수동 프록시를 임의 fallback하지 않는 보수적 선택 결과입니다.",
                "현재 대상 URL의 스킴과 Windows 프록시 범위를 확인하십시오.");
        }

        if (proxyAnalysis.Status
                != ProxyEndpointRouteAnalysisStatus.Success
            || proxyExecution.HasParseErrors)
        {
            return Incomplete(
                timestamp,
                snapshot,
                InternalProxyRouteComparisonCode.ProxyAnalysisIncomplete,
                "프록시 후보 중 일부가 미확정·취소·실패 상태이거나 지시문 파싱에서 제외된 구간이 있습니다.",
                "확인된 일부 후보만으로 전체 프록시 fallback 경로를 하나로 단정하지 않았습니다.",
                "실제 요청은 아직 확인하지 못한 후보 또는 DIRECT fallback을 사용할 수 있습니다.",
                "파싱 오류와 실패 후보의 DNS·Windows 경로를 해결한 뒤 다시 비교하십시오.");
        }

        if (internalDirectRoute.Status
                != DestinationRouteEvidenceStatus.Success
            || internalDirectRoute.SelectedInterface is null)
        {
            return Incomplete(
                timestamp,
                snapshot,
                InternalProxyRouteComparisonCode.InternalRouteIncomplete,
                "내부 DIRECT 대상의 단일 Windows 최적 인터페이스를 완전히 확인하지 못했습니다.",
                "부분 성공 또는 실패 상태의 내부 경로를 정확 비교 기준으로 사용하지 않았습니다.",
                "주소별 성공·실패 원인은 DNS와 IPv4·IPv6 경로 근거를 함께 확인해야 합니다.",
                "내부 DIRECT 대상의 주소 해석과 Windows 최적 경로를 다시 확인하십시오.");
        }

        if (!TryNormalizeExactGuid(
                internalDirectRoute.SelectedInterface.InterfaceIdentity,
                out string internalExactIdentity))
        {
            return Incomplete(
                timestamp,
                snapshot,
                InternalProxyRouteComparisonCode
                    .InternalExactIdentityUnavailable,
                "내부 DIRECT 경로의 전체 Windows 인터페이스 GUID를 정확 비교용으로 확인하지 못했습니다.",
                "짧은 표시 지문만으로 같은 NIC라고 단정하지 않았습니다.",
                "표시 지문은 충돌 가능성이 있는 축약값이며 정확한 인터페이스 식별자가 아닙니다.",
                "현재 Windows 인터페이스 ID 수집 상태를 확인하고 내부 경로를 다시 수집하십시오.");
        }

        ProxyEndpointRouteEvidenceItem[] endpoints =
            proxyAnalysis.Endpoints.ToArray();
        if (endpoints.Length == 0)
        {
            return Incomplete(
                timestamp,
                snapshot,
                InternalProxyRouteComparisonCode.ProxyEndpointMissing,
                "성공 상태의 프록시 분석에 비교할 엔드포인트 결과가 없습니다.",
                "불완전하거나 손상된 분석 결과를 정확 경로 비교에 사용하지 않았습니다.",
                "상태와 후보 수가 일치하는 같은 실행 세션의 분석 결과가 필요합니다.",
                "프록시 엔드포인트 경로 분석을 다시 실행하십시오.");
        }

        bool routeCountsConsistent =
            proxyAnalysis.AnalyzedEndpointCount == endpoints.Length
            && proxyAnalysis.SuccessfulEndpointCount == endpoints.Length
            && endpoints.All(endpoint =>
                endpoint.RouteStatus
                    == DestinationRouteEvidenceStatus.Success);
        if (!routeCountsConsistent)
        {
            return Incomplete(
                timestamp,
                snapshot,
                InternalProxyRouteComparisonCode.ProxyAnalysisIncomplete,
                "프록시 분석의 후보 수·성공 수 또는 후보별 경로 상태가 서로 일치하지 않습니다.",
                "부분·손상된 결과를 완전한 프록시 경로로 취급하지 않았습니다.",
                "분석 객체가 수집 이후 변경됐거나 일부 주소 경로가 실패했을 수 있습니다.",
                "현재 세션에서 프록시 경로 분석을 다시 실행하십시오.");
        }

        ExactIdentityCollection proxyIdentities =
            CollectExactProxyIdentities(endpoints);
        if (proxyIdentities.DistinctIdentities.Count > 1)
        {
            return Ambiguous(
                timestamp,
                snapshot,
                InternalProxyRouteComparisonCode.ProxyRouteAmbiguous,
                "프록시 후보들이 서로 다른 전체 Windows 인터페이스 GUID를 선택했습니다.",
                "축약 지문 집계와 관계없이 정확한 NIC가 둘 이상이므로 단일 프록시 경로를 선택하지 않았습니다.",
                "후보 순서와 실제 요청의 fallback 선택은 이 로컬 경로 비교만으로 확정할 수 없습니다.",
                "각 프록시 후보의 인터페이스 범주와 실제 프록시 판정·오류 근거를 함께 확인하십시오.");
        }

        if (!proxyIdentities.AllEndpointsHadExactIdentity
            || proxyIdentities.DistinctIdentities.Count != 1)
        {
            return Incomplete(
                timestamp,
                snapshot,
                InternalProxyRouteComparisonCode
                    .ProxyExactIdentityUnavailable,
                "하나 이상의 프록시 경로에서 전체 Windows 인터페이스 GUID를 정확 비교용으로 확인하지 못했습니다.",
                "표시 지문이 같더라도 전체 인터페이스 ID가 없으면 같은 NIC로 판정하지 않습니다.",
                "JSON 등으로 직렬화된 안전 결과에는 메모리 전용 전체 GUID가 포함되지 않습니다.",
                "프록시 경로 분석을 현재 세션에서 다시 실행한 직후 비교하십시오.");
        }

        string proxyExactIdentity =
            proxyIdentities.DistinctIdentities.Single();
        if (string.Equals(
                proxyExactIdentity,
                internalExactIdentity,
                StringComparison.OrdinalIgnoreCase))
        {
            return Complete(
                timestamp,
                snapshot,
                InternalProxyRouteComparisonStatus.Ready,
                InternalProxyRouteRelation.SameInterface,
                InternalProxyRouteComparisonCode.SameLocalInterface,
                "내부 DIRECT 대상과 모든 확인된 프록시 엔드포인트가 동일한 Windows 로컬 인터페이스를 선택했습니다.",
                "현재 PC에서 두 경로의 첫 로컬 송출 NIC는 같습니다.",
                proxyAnalysis.DirectFallback
                    ? "같은 로컬 NIC를 사용하지만 프록시 뒤 DIRECT fallback이 있어 실제 요청이 프록시 실패 후 DIRECT로 전환됐는지는 확인하지 않습니다."
                    : "같은 로컬 NIC를 사용해도 이후 사내 라우팅, 프록시, 인터넷 회선과 대상 서버 경로가 같다는 뜻은 아닙니다.",
                "내부·외부 처리량, 프록시 인증·HTTP 상태와 WLAN 상태를 같은 시각 기준으로 비교하십시오.");
        }

        return Complete(
            timestamp,
            snapshot,
            InternalProxyRouteComparisonStatus.Diverged,
            InternalProxyRouteRelation.DifferentInterface,
            InternalProxyRouteComparisonCode.DifferentLocalInterface,
            "내부 DIRECT 대상과 확인된 프록시 엔드포인트가 서로 다른 Windows 로컬 인터페이스를 선택했습니다.",
            "현재 PC에서 내부 경로와 프록시 경로의 첫 로컬 송출 NIC가 분리돼 있습니다. VPN·터널·유선 우선순위 또는 의도된 분할 라우팅일 수 있습니다.",
            "인터페이스 차이만으로 장애를 확정할 수 없으며 실제 프록시 연결·인증·처리량은 별도 근거가 필요합니다.",
            "각 인터페이스 범주·지문, VPN 정책과 내부·외부 측정 결과를 함께 확인하십시오.");
    }

    private static InternalProxyRouteComparisonResult?
        EvaluateExecutionTerminal(
            DateTimeOffset evaluatedAt,
            ComparisonSnapshot snapshot,
            ProxyDirectiveRouteAnalysisExecutionResult<
                ProxyEndpointRouteAnalysisResult> execution) =>
        execution.Status switch
        {
            ProxyDirectiveRouteAnalysisExecutionStatus.Completed => null,
            ProxyDirectiveRouteAnalysisExecutionStatus.DirectOnly =>
                Incomplete(
                    evaluatedAt,
                    snapshot,
                    InternalProxyRouteComparisonCode.ProxyDirectOnly,
                    "선택된 프록시 출처가 DIRECT-only입니다.",
                    "프록시 엔드포인트가 없어 내부 DIRECT 경로와 프록시 경로 비교를 수행하지 않았습니다.",
                    "DIRECT 판정은 해당 외부 대상과 수집 시점에만 적용됩니다.",
                    "프록시가 적용되는 승인된 외부 대상의 판정 결과를 사용하십시오."),
            ProxyDirectiveRouteAnalysisExecutionStatus.Blocked =>
                Incomplete(
                    evaluatedAt,
                    snapshot,
                    InternalProxyRouteComparisonCode.ProxySourceBlocked,
                    "프록시 출처 또는 실행 계획이 유효하지 않아 경로 분석이 차단됐습니다.",
                    "모순되거나 손상된 프록시 판정을 수동 설정이나 DIRECT로 대체하지 않았습니다.",
                    "대상별 PAC/WPAD 판정 실패와 수동 설정 오류는 서로 다른 원인일 수 있습니다.",
                    "프록시 출처 선택 코드와 reader 상태를 확인하십시오."),
            ProxyDirectiveRouteAnalysisExecutionStatus.Unavailable =>
                Incomplete(
                    evaluatedAt,
                    snapshot,
                    InternalProxyRouteComparisonCode.ProxySourceUnavailable,
                    "사용 가능한 대상별 또는 수동 프록시 출처가 없습니다.",
                    "프록시나 DIRECT를 추정하지 않았으므로 비교할 프록시 경로가 없습니다.",
                    "자동 검색·PAC·수동 프록시 설정이 실제로 없거나 아직 읽지 않았을 수 있습니다.",
                    "현재 외부 대상에 대한 Windows 프록시 판정을 먼저 수집하십시오."),
            ProxyDirectiveRouteAnalysisExecutionStatus.Canceled =>
                Incomplete(
                    evaluatedAt,
                    snapshot,
                    InternalProxyRouteComparisonCode.ProxyExecutionCanceled,
                    "프록시 엔드포인트 경로 분석이 취소됐습니다.",
                    "완료되지 않은 후보가 있어 전체 프록시 경로를 비교하지 않았습니다.",
                    "취소 전 일부 후보가 확인됐더라도 전체 fallback을 대표하지 않습니다.",
                    "필요한 경우 프록시 경로 분석을 다시 완료하십시오."),
            ProxyDirectiveRouteAnalysisExecutionStatus.Failed =>
                Incomplete(
                    evaluatedAt,
                    snapshot,
                    InternalProxyRouteComparisonCode.ProxyExecutionFailed,
                    "프록시 엔드포인트 경로 분석 실행이 실패했습니다.",
                    "실행 콜백 오류 또는 결과 누락으로 정확 비교를 수행하지 않았습니다.",
                    "예외 원문은 비교 결과에 포함되지 않으며 실제 원인은 Windows 로그와 분석 단계에서 확인해야 합니다.",
                    "프록시 출처·대상 URL·DNS 제한 시간을 확인한 뒤 다시 실행하십시오."),
            _ => Incomplete(
                evaluatedAt,
                snapshot,
                InternalProxyRouteComparisonCode.ProxyExecutionFailed,
                "정의되지 않은 프록시 실행 상태입니다.",
                "알 수 없는 실행 상태를 성공으로 취급하지 않았습니다.",
                "손상된 상태 값 또는 호환되지 않는 실행 객체일 수 있습니다.",
                "현재 버전에서 프록시 경로 분석을 다시 실행하십시오.")
        };

    private static ComparisonSnapshot CreateSnapshot(
        DestinationRouteEvidence? internalRoute,
        ProxyDirectiveRouteAnalysisExecutionResult<
            ProxyEndpointRouteAnalysisResult>? proxyExecution)
    {
        ProxyEndpointRouteAnalysisResult? analysis =
            proxyExecution?.Analysis;
        RouteInterfaceDescriptor? internalInterface =
            internalRoute?.SelectedInterface;

        string? internalFingerprint = NormalizeFingerprint(
            internalInterface?.IdentityFingerprint);
        NetworkAdapterCategory? internalCategory =
            internalInterface?.Category;

        string[] proxyFingerprints = (analysis?.Endpoints
                ?? Array.Empty<ProxyEndpointRouteEvidenceItem>())
            .Select(endpoint => NormalizeFingerprint(
                endpoint.SelectedInterfaceFingerprint))
            .Where(value => value is not null)
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        NetworkAdapterCategory[] proxyCategories = (analysis?.Endpoints
                ?? Array.Empty<ProxyEndpointRouteEvidenceItem>())
            .Where(endpoint => endpoint.SelectedInterfaceCategory.HasValue)
            .Select(endpoint => endpoint.SelectedInterfaceCategory!.Value)
            .Distinct()
            .OrderBy(value => value.ToString(), StringComparer.Ordinal)
            .ToArray();

        return new ComparisonSnapshot(
            InternalRouteStatus: internalRoute?.Status,
            ProxyExecutionStatus: proxyExecution?.Status,
            ProxyAnalysisStatus: analysis?.Status,
            ProxySourceKind: proxyExecution?.SourceKind,
            ProxyPlanCode: proxyExecution?.PlanCode,
            InternalInterfaceFingerprint: internalFingerprint,
            InternalInterfaceCategory: internalCategory,
            ProxyInterfaceFingerprints: proxyFingerprints,
            ProxyInterfaceCategories: proxyCategories,
            ProxyApplicableEndpointCount:
                Math.Max(0, analysis?.ApplicableEndpointCount ?? 0),
            ProxyAnalyzedEndpointCount:
                Math.Max(0, analysis?.AnalyzedEndpointCount ?? 0),
            ProxySuccessfulEndpointCount:
                Math.Max(0, analysis?.SuccessfulEndpointCount ?? 0),
            ProxyDistinctInterfaceCount:
                Math.Max(0, analysis?.DistinctInterfaceCount ?? 0),
            ProxySkippedAfterDirectCount:
                Math.Max(0, analysis?.SkippedAfterDirectCount ?? 0),
            ProxyDirectPresent: analysis?.DirectPresent
                ?? (proxyExecution?.DirectDirectiveCount ?? 0) > 0,
            ProxyDirectIsPrimary: analysis?.DirectIsPrimary ?? false,
            ProxyDirectFallbackPresent:
                analysis?.DirectFallback ?? false,
            ProxyParseErrorsPresent:
                proxyExecution?.HasParseErrors ?? false);
    }

    private static ExactIdentityCollection CollectExactProxyIdentities(
        IReadOnlyList<ProxyEndpointRouteEvidenceItem> endpoints)
    {
        List<string> identities = [];
        bool allHadIdentity = true;

        foreach (ProxyEndpointRouteEvidenceItem endpoint in endpoints)
        {
            if (!TryNormalizeExactGuid(
                    endpoint.SelectedInterfaceIdentity,
                    out string exactIdentity))
            {
                allHadIdentity = false;
                continue;
            }

            if (!identities.Contains(
                    exactIdentity,
                    StringComparer.OrdinalIgnoreCase))
            {
                identities.Add(exactIdentity);
            }
        }

        identities.Sort(StringComparer.Ordinal);
        return new ExactIdentityCollection(identities, allHadIdentity);
    }

    private static bool TryNormalizeExactGuid(
        string? value,
        out string normalized)
    {
        string candidate = (value ?? string.Empty)
            .Trim()
            .Trim('{', '}');
        if (Guid.TryParse(candidate, out Guid parsed))
        {
            normalized = parsed.ToString("D");
            return true;
        }

        normalized = string.Empty;
        return false;
    }

    private static string? NormalizeFingerprint(string? value)
    {
        string candidate = (value ?? string.Empty)
            .Trim()
            .ToLowerInvariant();
        return candidate.Length == RouteInterfaceFingerprint.DisplayLength
               && candidate.All(character =>
                   character is >= '0' and <= '9'
                       or >= 'a' and <= 'f')
            ? candidate
            : null;
    }

    private static InternalProxyRouteComparisonResult Complete(
        DateTimeOffset evaluatedAt,
        ComparisonSnapshot snapshot,
        InternalProxyRouteComparisonStatus status,
        InternalProxyRouteRelation relation,
        InternalProxyRouteComparisonCode code,
        string message,
        string interpretation,
        string limitation,
        string nextStep) =>
        Build(
            evaluatedAt,
            snapshot,
            status,
            relation,
            code,
            exactIdentityComparisonPerformed: true,
            message,
            interpretation,
            limitation,
            nextStep);

    private static InternalProxyRouteComparisonResult Incomplete(
        DateTimeOffset evaluatedAt,
        ComparisonSnapshot snapshot,
        InternalProxyRouteComparisonCode code,
        string message,
        string interpretation,
        string limitation,
        string nextStep) =>
        Build(
            evaluatedAt,
            snapshot,
            InternalProxyRouteComparisonStatus.Incomplete,
            InternalProxyRouteRelation.Unknown,
            code,
            exactIdentityComparisonPerformed: false,
            message,
            interpretation,
            limitation,
            nextStep);

    private static InternalProxyRouteComparisonResult Ambiguous(
        DateTimeOffset evaluatedAt,
        ComparisonSnapshot snapshot,
        InternalProxyRouteComparisonCode code,
        string message,
        string interpretation,
        string limitation,
        string nextStep) =>
        Build(
            evaluatedAt,
            snapshot,
            InternalProxyRouteComparisonStatus.Ambiguous,
            InternalProxyRouteRelation.MultipleInterfaces,
            code,
            exactIdentityComparisonPerformed: false,
            message,
            interpretation,
            limitation,
            nextStep);

    private static InternalProxyRouteComparisonResult Build(
        DateTimeOffset evaluatedAt,
        ComparisonSnapshot snapshot,
        InternalProxyRouteComparisonStatus status,
        InternalProxyRouteRelation relation,
        InternalProxyRouteComparisonCode code,
        bool exactIdentityComparisonPerformed,
        string message,
        string interpretation,
        string limitation,
        string nextStep) =>
        new(
            EvaluatedAt: evaluatedAt,
            Status: status,
            Relation: relation,
            Code: code,
            InternalRouteStatus: snapshot.InternalRouteStatus,
            ProxyExecutionStatus: snapshot.ProxyExecutionStatus,
            ProxyAnalysisStatus: snapshot.ProxyAnalysisStatus,
            ProxySourceKind: snapshot.ProxySourceKind,
            ProxyPlanCode: snapshot.ProxyPlanCode,
            InternalInterfaceFingerprint:
                snapshot.InternalInterfaceFingerprint,
            InternalInterfaceCategory:
                snapshot.InternalInterfaceCategory,
            ProxyInterfaceFingerprints:
                snapshot.ProxyInterfaceFingerprints,
            ProxyInterfaceCategories:
                snapshot.ProxyInterfaceCategories,
            ProxyApplicableEndpointCount:
                snapshot.ProxyApplicableEndpointCount,
            ProxyAnalyzedEndpointCount:
                snapshot.ProxyAnalyzedEndpointCount,
            ProxySuccessfulEndpointCount:
                snapshot.ProxySuccessfulEndpointCount,
            ProxyDistinctInterfaceCount:
                snapshot.ProxyDistinctInterfaceCount,
            ProxySkippedAfterDirectCount:
                snapshot.ProxySkippedAfterDirectCount,
            ProxyDirectPresent: snapshot.ProxyDirectPresent,
            ProxyDirectIsPrimary: snapshot.ProxyDirectIsPrimary,
            ProxyDirectFallbackPresent:
                snapshot.ProxyDirectFallbackPresent,
            ProxyParseErrorsPresent:
                snapshot.ProxyParseErrorsPresent,
            ExactIdentityComparisonPerformed:
                exactIdentityComparisonPerformed,
            Message: message,
            Interpretation: interpretation,
            Limitation: limitation,
            NextStep: nextStep);

    private sealed record ComparisonSnapshot(
        DestinationRouteEvidenceStatus? InternalRouteStatus,
        ProxyDirectiveRouteAnalysisExecutionStatus?
            ProxyExecutionStatus,
        ProxyEndpointRouteAnalysisStatus? ProxyAnalysisStatus,
        ProxyDirectiveSourceKind? ProxySourceKind,
        ProxyDirectiveRouteAnalysisPlanCode? ProxyPlanCode,
        string? InternalInterfaceFingerprint,
        NetworkAdapterCategory? InternalInterfaceCategory,
        IReadOnlyList<string> ProxyInterfaceFingerprints,
        IReadOnlyList<NetworkAdapterCategory>
            ProxyInterfaceCategories,
        int ProxyApplicableEndpointCount,
        int ProxyAnalyzedEndpointCount,
        int ProxySuccessfulEndpointCount,
        int ProxyDistinctInterfaceCount,
        int ProxySkippedAfterDirectCount,
        bool ProxyDirectPresent,
        bool ProxyDirectIsPrimary,
        bool ProxyDirectFallbackPresent,
        bool ProxyParseErrorsPresent);

    private sealed record ExactIdentityCollection(
        IReadOnlyList<string> DistinctIdentities,
        bool AllEndpointsHadExactIdentity);
}
