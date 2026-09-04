using WlanLivePathTester.Core.NetworkEnvironment;
using WlanLivePathTester.Core.Proxy;

namespace WlanLivePathTester.Core.Routing;

public static class InternalProxyRouteComparisonEvaluator
{
    public static InternalProxyRouteComparisonResult Evaluate(
        DestinationRouteEvidence? internalDirectRoute,
        ProxyEndpointRouteAnalysisResult? proxyAnalysis)
    {
        ComparisonSnapshot snapshot = CreateSnapshot(
            internalDirectRoute,
            proxyAnalysis);

        if (internalDirectRoute is null)
        {
            return Incomplete(
                snapshot,
                InternalProxyRouteComparisonCode.InternalRouteMissing,
                "내부 DIRECT 대상의 Windows 로컬 경로 근거가 없습니다.",
                "내부망 기준 경로와 프록시 엔드포인트 경로를 비교할 수 없습니다.",
                "프록시 경로만으로 내부망과 외부망의 첫 로컬 인터페이스 차이를 판단할 수 없습니다.",
                "승인된 내부 DIRECT 대상의 경로 확인을 먼저 실행하십시오.");
        }

        if (internalDirectRoute.Purpose
            != RouteProbePurpose.InternalDirectTarget)
        {
            return Incomplete(
                snapshot,
                InternalProxyRouteComparisonCode.InternalPurposeMismatch,
                "비교 입력이 내부 DIRECT 대상 용도로 수집된 경로가 아닙니다.",
                "일반 목적지나 프록시 엔드포인트 근거를 내부망 기준 경로로 재사용하지 않았습니다.",
                "대상 용도만으로 실제 PAC·바이패스 정책을 증명할 수는 없지만 비교 입력의 의미를 고정하기 위한 경계입니다.",
                "내부망에서 DIRECT로 승인된 대상을 InternalDirectTarget 용도로 다시 확인하십시오.");
        }

        if (proxyAnalysis is null)
        {
            return Incomplete(
                snapshot,
                InternalProxyRouteComparisonCode.ProxyAnalysisMissing,
                "프록시 엔드포인트의 Windows 로컬 경로 분석 결과가 없습니다.",
                "내부 DIRECT 경로만으로 프록시 경유 외부망의 첫 로컬 인터페이스를 비교할 수 없습니다.",
                "Windows 프록시 설정 존재 여부만으로 실제 프록시 후보까지의 로컬 경로를 알 수 없습니다.",
                "현재 대상에 적용된 프록시 지시문을 해석한 뒤 프록시 엔드포인트 경로 분석을 실행하십시오.");
        }

        if (internalDirectRoute.Status
            == DestinationRouteEvidenceStatus.MultipleInterfaces)
        {
            return Ambiguous(
                snapshot,
                InternalProxyRouteComparisonCode.InternalRouteAmbiguous,
                "내부 DIRECT 대상의 주소 계열별 Windows 최적 경로가 여러 인터페이스를 선택했습니다.",
                "내부 기준 경로가 하나로 확정되지 않아 프록시 경로와 단일 NIC 비교를 수행하지 않았습니다.",
                "IPv4·IPv6가 서로 다른 인터페이스를 선택하거나 라우팅 상태가 수집 중 변했을 수 있습니다.",
                "내부 대상의 IPv4·IPv6 경로를 각각 확인하고 VPN·유선·무선 라우팅 우선순위를 점검하십시오.");
        }

        if (proxyAnalysis.Entries.Any(entry =>
                !entry.IsDirect
                && entry.Status
                    == ProxyEndpointRouteEntryStatus.MultipleInterfaces))
        {
            return Ambiguous(
                snapshot,
                InternalProxyRouteComparisonCode.ProxyRouteAmbiguous,
                "하나 이상의 프록시 호스트가 주소 계열별로 서로 다른 Windows 로컬 인터페이스를 선택했습니다.",
                "프록시 후보의 첫 로컬 경로가 하나로 확정되지 않아 내부 DIRECT 경로와 단일 NIC 비교를 수행하지 않았습니다.",
                "IPv4·IPv6, VPN 정책 또는 경로 우선순위 차이가 같은 결과를 만들 수 있습니다.",
                "프록시 후보별 IPv4·IPv6 경로와 VPN·터널·유선·무선 인터페이스 우선순위를 확인하십시오.");
        }

        ExactProxyIdentityCollection proxyIdentities =
            CollectExactProxyIdentities(proxyAnalysis);
        if (proxyIdentities.DistinctExactIdentities.Count > 1)
        {
            return Ambiguous(
                snapshot,
                InternalProxyRouteComparisonCode.ProxyRouteAmbiguous,
                "확인된 프록시 엔드포인트들이 둘 이상의 Windows 로컬 인터페이스를 선택했습니다.",
                "PAC fallback 후보마다 첫 로컬 송출 NIC가 달라 하나의 프록시 경로로 요약하지 않았습니다.",
                "후보별 경로 차이는 의도된 분산·VPN 정책일 수 있으며 실제 요청이 어느 후보를 사용했는지는 이 비교만으로 알 수 없습니다.",
                "프록시 후보 순서와 각 후보의 인터페이스 지문·범주를 개별적으로 확인하십시오.");
        }

        if (proxyAnalysis.Status
            == ProxyEndpointRouteAnalysisStatus.DirectOnly)
        {
            return Incomplete(
                snapshot,
                InternalProxyRouteComparisonCode.ProxyDirectOnly,
                "현재 지시문에는 비교할 프록시 엔드포인트가 없고 DIRECT만 있습니다.",
                "프록시 서버까지의 로컬 경로가 존재하지 않으므로 내부 DIRECT 경로와 프록시 경로 비교를 수행하지 않았습니다.",
                "DIRECT는 해당 판정 대상에 대한 결과이며 다른 외부 URL에도 같은 정책이 적용된다는 뜻은 아닙니다.",
                "프록시가 적용되는 승인된 외부 대상의 PAC/WPAD 판정 결과로 다시 분석하십시오.");
        }

        if (proxyAnalysis.Status is
            ProxyEndpointRouteAnalysisStatus.Empty
            or ProxyEndpointRouteAnalysisStatus.InvalidInput)
        {
            return Incomplete(
                snapshot,
                InternalProxyRouteComparisonCode.ProxyDirectiveMissing,
                "사용할 수 있는 프록시 지시문 또는 엔드포인트가 없습니다.",
                "프록시 후보가 없어 내부 DIRECT 경로와 비교하지 않았습니다.",
                "수동 프록시 문자열과 PAC/WPAD 결과는 대상 URL·사용자·정책 시점에 따라 달라질 수 있습니다.",
                "현재 외부 측정 대상에 적용된 프록시 판정 결과를 다시 수집하십시오.");
        }

        if (internalDirectRoute.Status
                != DestinationRouteEvidenceStatus.Success
            || internalDirectRoute.SelectedInterface is null)
        {
            return Incomplete(
                snapshot,
                InternalProxyRouteComparisonCode.InternalRouteIncomplete,
                "내부 DIRECT 대상의 단일 Windows 최적 인터페이스를 완전히 확인하지 못했습니다.",
                "일부 주소 또는 실패 상태의 내부 경로를 확정 기준으로 사용하지 않았습니다.",
                "DNS·IPv4·IPv6·라우팅 테이블 변화 중 어느 단계가 불완전했는지는 주소별 근거를 함께 확인해야 합니다.",
                "내부 DIRECT 대상의 주소 해석과 Windows 최적 경로를 다시 확인하십시오.");
        }

        if (!TryNormalizeExactGuid(
                internalDirectRoute.SelectedInterface.InterfaceIdentity,
                out string internalExactIdentity))
        {
            return Incomplete(
                snapshot,
                InternalProxyRouteComparisonCode.ExactIdentityUnavailable,
                "내부 DIRECT 경로의 전체 Windows 인터페이스 GUID를 정확 비교용으로 확인하지 못했습니다.",
                "짧은 인터페이스 지문만으로 동일 NIC를 단정하지 않았습니다.",
                "표시용 지문은 충돌 가능성이 있는 축약값이며 정확한 로컬 비교 근거가 아닙니다.",
                "현재 Windows 인터페이스 ID 수집 상태를 확인하고 경로 분석을 다시 실행하십시오.");
        }

        ProxyEndpointRouteEntry[] proxyEntries = proxyAnalysis.Entries
            .Where(entry => !entry.IsDirect)
            .ToArray();
        if (proxyEntries.Length == 0)
        {
            return Incomplete(
                snapshot,
                InternalProxyRouteComparisonCode.ProxyRouteMissing,
                "비교할 비-DIRECT 프록시 엔드포인트 경로가 없습니다.",
                "내부 경로만 존재하므로 프록시 경로와의 관계를 결정하지 않았습니다.",
                "파서 일부 오류나 후보 상한 때문에 프록시 후보가 제외됐을 수 있습니다.",
                "프록시 지시문 파싱 결과와 후보 상한을 확인하십시오.");
        }

        bool analysisIncomplete = proxyAnalysis.Status
                != ProxyEndpointRouteAnalysisStatus.Success
            || proxyAnalysis.ParseStatus
                != ProxyDirectiveParseStatus.Success
            || proxyAnalysis.WasTruncated
            || proxyEntries.Any(entry =>
                entry.Status != ProxyEndpointRouteEntryStatus.Success);
        if (analysisIncomplete)
        {
            return Incomplete(
                snapshot,
                InternalProxyRouteComparisonCode.ProxyAnalysisIncomplete,
                "프록시 후보 중 일부가 미확정·취소·잘림 상태이거나 지시문 파싱이 부분 성공입니다.",
                "확인된 일부 후보만으로 전체 프록시 fallback 경로를 하나로 단정하지 않았습니다.",
                "실제 요청은 아직 확인하지 못한 후보나 DIRECT fallback을 사용할 수 있습니다.",
                "실패한 후보의 DNS·Windows 경로를 확인하고 후보 상한 이내에서 분석을 다시 실행하십시오.");
        }

        if (!proxyIdentities.AllUsableEntriesHadExactIdentity
            || proxyIdentities.DistinctExactIdentities.Count != 1)
        {
            return Incomplete(
                snapshot,
                InternalProxyRouteComparisonCode.ExactIdentityUnavailable,
                "하나 이상의 프록시 경로에서 전체 Windows 인터페이스 GUID를 정확 비교용으로 확인하지 못했습니다.",
                "표시용 지문이 같더라도 전체 인터페이스 ID가 없으면 동일 NIC로 판정하지 않습니다.",
                "지문은 공개 표시용 축약값이며 정확 비교는 메모리 내 전체 GUID가 있을 때만 수행합니다.",
                "프록시 엔드포인트 경로 분석을 현재 세션에서 다시 실행한 뒤 즉시 비교하십시오.");
        }

        string proxyExactIdentity =
            proxyIdentities.DistinctExactIdentities.Single();
        if (proxyExactIdentity.Equals(
                internalExactIdentity,
                StringComparison.OrdinalIgnoreCase))
        {
            return Complete(
                snapshot,
                InternalProxyRouteComparisonStatus.Ready,
                InternalProxyRouteRelation.SameInterface,
                InternalProxyRouteComparisonCode.SameLocalInterface,
                "내부 DIRECT 대상과 모든 확인된 프록시 엔드포인트가 동일한 Windows 로컬 인터페이스를 선택했습니다.",
                "현재 PC에서 두 경로의 첫 로컬 송출 NIC는 같습니다.",
                "같은 로컬 NIC를 사용해도 이후 사내 라우팅, 프록시, 인터넷 회선과 대상 서버 경로가 같다는 뜻은 아닙니다.",
                "내부·외부 다운로드 처리량, 프록시 판정과 WLAN 상태를 같은 시각 기준으로 비교하십시오.");
        }

        return Complete(
            snapshot,
            InternalProxyRouteComparisonStatus.Diverged,
            InternalProxyRouteRelation.DifferentInterface,
            InternalProxyRouteComparisonCode.DifferentLocalInterface,
            "내부 DIRECT 대상과 확인된 프록시 엔드포인트가 서로 다른 Windows 로컬 인터페이스를 선택했습니다.",
            "현재 PC에서 내부 경로와 프록시 경로의 첫 로컬 송출 NIC가 분리돼 있습니다. VPN·터널·유선 우선순위 또는 의도된 분할 라우팅일 수 있습니다.",
            "인터페이스 차이만으로 장애를 확정할 수 없으며 실제 프록시 요청의 성공·인증·처리량은 별도 근거가 필요합니다.",
            "각 인터페이스 범주·지문, VPN 정책과 내부·외부 측정 결과를 함께 확인하십시오.");
    }

    private static ComparisonSnapshot CreateSnapshot(
        DestinationRouteEvidence? internalRoute,
        ProxyEndpointRouteAnalysisResult? proxyAnalysis)
    {
        RouteInterfaceDescriptor? internalInterface =
            internalRoute?.SelectedInterface;
        string? internalFingerprint =
            NormalizeFingerprint(internalInterface?.IdentityFingerprint);
        string? internalCategory = internalInterface is null
            ? null
            : internalInterface.Category.ToString();

        List<string> proxyFingerprints = [];
        List<string> proxyCategories = [];
        if (proxyAnalysis is not null)
        {
            foreach (ProxyEndpointRouteEntry entry in
                     proxyAnalysis.Entries.Where(entry => !entry.IsDirect))
            {
                string? fingerprint = GetSafeProxyFingerprint(entry);
                if (fingerprint is not null
                    && !proxyFingerprints.Contains(
                        fingerprint,
                        StringComparer.OrdinalIgnoreCase))
                {
                    proxyFingerprints.Add(fingerprint);
                }

                string? category = GetSafeProxyCategory(entry);
                if (category is not null
                    && !proxyCategories.Contains(
                        category,
                        StringComparer.OrdinalIgnoreCase))
                {
                    proxyCategories.Add(category);
                }
            }
        }

        proxyFingerprints.Sort(StringComparer.Ordinal);
        proxyCategories.Sort(StringComparer.Ordinal);
        return new ComparisonSnapshot(
            InternalRouteStatus:
                internalRoute?.Status.ToString() ?? "Missing",
            ProxyAnalysisStatus:
                proxyAnalysis?.Status.ToString() ?? "Missing",
            InternalInterfaceFingerprint: internalFingerprint,
            InternalInterfaceCategory: internalCategory,
            ProxyInterfaceFingerprints: proxyFingerprints,
            ProxyInterfaceCategories: proxyCategories,
            ProxyEndpointCount:
                proxyAnalysis?.ProxyEndpointCount ?? 0,
            SuccessfulProxyRouteCount:
                proxyAnalysis?.SuccessfulRouteCount ?? 0,
            DirectDirectiveCount:
                proxyAnalysis?.DirectDirectiveCount ?? 0,
            ProxyAnalysisWasTruncated:
                proxyAnalysis?.WasTruncated ?? false);
    }

    private static ExactProxyIdentityCollection
        CollectExactProxyIdentities(
            ProxyEndpointRouteAnalysisResult proxyAnalysis)
    {
        List<string> identities = [];
        bool allUsableHadIdentity = true;
        foreach (ProxyEndpointRouteEntry entry in proxyAnalysis.Entries)
        {
            if (entry.IsDirect || !entry.HasUsableRoute)
            {
                continue;
            }

            DestinationRouteEvidence? route = entry.RouteEvidence;
            if (route?.Purpose != RouteProbePurpose.ProxyEndpoint
                || route.SelectedInterface is null
                || !TryNormalizeExactGuid(
                    route.SelectedInterface.InterfaceIdentity,
                    out string exactIdentity))
            {
                allUsableHadIdentity = false;
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
        return new ExactProxyIdentityCollection(
            identities,
            allUsableHadIdentity);
    }

    private static string? GetSafeProxyFingerprint(
        ProxyEndpointRouteEntry entry)
    {
        string? fromRoute = NormalizeFingerprint(
            entry.RouteEvidence?.SelectedInterface?.IdentityFingerprint);
        return fromRoute
            ?? NormalizeFingerprint(entry.SelectedInterfaceFingerprint);
    }

    private static string? GetSafeProxyCategory(
        ProxyEndpointRouteEntry entry)
    {
        NetworkAdapterCategory? fromRoute =
            entry.RouteEvidence?.SelectedInterface?.Category;
        if (fromRoute.HasValue)
        {
            return fromRoute.Value.ToString();
        }

        return Enum.TryParse(
            entry.SelectedInterfaceCategory,
            ignoreCase: true,
            out NetworkAdapterCategory parsed)
            ? parsed.ToString()
            : null;
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
        ComparisonSnapshot snapshot,
        InternalProxyRouteComparisonStatus status,
        InternalProxyRouteRelation relation,
        InternalProxyRouteComparisonCode code,
        string message,
        string interpretation,
        string limitation,
        string nextStep) =>
        Build(
            snapshot,
            status,
            relation,
            code,
            ExactIdentityComparisonPerformed: true,
            message,
            interpretation,
            limitation,
            nextStep);

    private static InternalProxyRouteComparisonResult Incomplete(
        ComparisonSnapshot snapshot,
        InternalProxyRouteComparisonCode code,
        string message,
        string interpretation,
        string limitation,
        string nextStep) =>
        Build(
            snapshot,
            InternalProxyRouteComparisonStatus.Incomplete,
            InternalProxyRouteRelation.Unknown,
            code,
            ExactIdentityComparisonPerformed: false,
            message,
            interpretation,
            limitation,
            nextStep);

    private static InternalProxyRouteComparisonResult Ambiguous(
        ComparisonSnapshot snapshot,
        InternalProxyRouteComparisonCode code,
        string message,
        string interpretation,
        string limitation,
        string nextStep) =>
        Build(
            snapshot,
            InternalProxyRouteComparisonStatus.Ambiguous,
            InternalProxyRouteRelation.MultipleInterfaces,
            code,
            ExactIdentityComparisonPerformed: false,
            message,
            interpretation,
            limitation,
            nextStep);

    private static InternalProxyRouteComparisonResult Build(
        ComparisonSnapshot snapshot,
        InternalProxyRouteComparisonStatus status,
        InternalProxyRouteRelation relation,
        InternalProxyRouteComparisonCode code,
        bool ExactIdentityComparisonPerformed,
        string message,
        string interpretation,
        string limitation,
        string nextStep) =>
        new(
            Status: status,
            Relation: relation,
            Code: code,
            InternalRouteStatus: snapshot.InternalRouteStatus,
            ProxyAnalysisStatus: snapshot.ProxyAnalysisStatus,
            InternalInterfaceFingerprint:
                snapshot.InternalInterfaceFingerprint,
            InternalInterfaceCategory:
                snapshot.InternalInterfaceCategory,
            ProxyInterfaceFingerprints:
                snapshot.ProxyInterfaceFingerprints,
            ProxyInterfaceCategories:
                snapshot.ProxyInterfaceCategories,
            ProxyEndpointCount: snapshot.ProxyEndpointCount,
            SuccessfulProxyRouteCount:
                snapshot.SuccessfulProxyRouteCount,
            DirectDirectiveCount:
                snapshot.DirectDirectiveCount,
            ProxyAnalysisWasTruncated:
                snapshot.ProxyAnalysisWasTruncated,
            ExactIdentityComparisonPerformed:
                ExactIdentityComparisonPerformed,
            Message: message,
            Interpretation: interpretation,
            Limitation: limitation,
            NextStep: nextStep);

    private sealed record ComparisonSnapshot(
        string InternalRouteStatus,
        string ProxyAnalysisStatus,
        string? InternalInterfaceFingerprint,
        string? InternalInterfaceCategory,
        IReadOnlyList<string> ProxyInterfaceFingerprints,
        IReadOnlyList<string> ProxyInterfaceCategories,
        int ProxyEndpointCount,
        int SuccessfulProxyRouteCount,
        int DirectDirectiveCount,
        bool ProxyAnalysisWasTruncated);

    private sealed record ExactProxyIdentityCollection(
        IReadOnlyList<string> DistinctExactIdentities,
        bool AllUsableEntriesHadExactIdentity);
}
