using System.Globalization;
using System.Text;
using WlanLivePathTester.Core.Routing;

namespace WlanLivePathTester.Core.Reporting;

public static class InternalProxyRouteComparisonRunTextRenderer
{
    public static string Render(
        InternalProxyRouteComparisonRunResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        InternalProxyRouteComparisonRunSnapshot snapshot =
            InternalProxyRouteComparisonRunSnapshotMapper.FromResult(
                result);
        StringBuilder builder = new();
        builder.AppendLine("내부 DIRECT ↔ 프록시 로컬 경로 비교");
        builder.AppendLine(
            $"완료 시각: {snapshot.CompletedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture)}");
        builder.AppendLine($"실행 상태: {snapshot.RunStatus}");
        builder.AppendLine(
            $"프록시 출처 / 결정: {snapshot.ProxySourceKind} / {snapshot.ProxyDecision}");
        builder.AppendLine(
            $"외부 대상 스킴: {snapshot.TargetScheme ?? "확인 안 됨"}");
        builder.AppendLine(
            $"내부 / 프록시 경로 상태: {snapshot.InternalRouteStatus ?? "-"} / {snapshot.ProxyRouteStatus ?? "-"}");
        builder.AppendLine(
            $"내부 / 프록시 단계: {(snapshot.InternalRouteReadPerformed ? "수행" : "미수행")} / {(snapshot.ProxyRouteAnalysisPerformed ? "수행" : "미수행")}");
        builder.AppendLine(
            $"프록시 후보(파싱 / 분석 / 성공): {snapshot.ParsedProxyEndpointCount} / {snapshot.AnalyzedProxyEndpointCount} / {snapshot.SuccessfulProxyEndpointCount}");
        builder.AppendLine(
            $"DIRECT / fallback: {(snapshot.DirectPresent ? "있음" : "없음")} / {(snapshot.DirectFallback ? "있음" : "없음")}");
        builder.AppendLine(
            $"현재 WLAN ID: {(snapshot.ExpectedWlanIdentityAvailable ? "확인" : "확인 안 됨")}");

        if (snapshot.ComparisonStatus is not null)
        {
            builder.AppendLine();
            builder.AppendLine("[비교 결과]");
            builder.AppendLine(
                $"상태: {snapshot.ComparisonStatus}");
            builder.AppendLine(
                $"같은 로컬 인터페이스: {FormatNullableBoolean(snapshot.SameLocalInterface)}");
            builder.AppendLine(
                $"서로 다른 프록시 인터페이스: {snapshot.ProxyDistinctInterfaceCount}개");
            builder.AppendLine(
                $"부분 근거(내부 / 프록시): {(snapshot.InternalEvidencePartial ? "있음" : "없음")} / {(snapshot.ProxyEvidencePartial ? "있음" : "없음")}");
            builder.AppendLine(
                $"VPN·터널 / 가상 NIC: {(snapshot.AnyVpnOrTunnelInterface ? "있음" : "확인 안 됨")} / {(snapshot.AnyVirtualInterface ? "있음" : "확인 안 됨")}");
        }

        AppendInterface(
            builder,
            "내부 DIRECT 인터페이스",
            snapshot.InternalInterface);
        AppendInterface(
            builder,
            "프록시 인터페이스",
            snapshot.ProxyInterface);

        ReportFinding finding = snapshot.Finding;
        builder.AppendLine();
        builder.AppendLine("[판정]");
        builder.AppendLine(
            $"{finding.Severity} · {finding.Code}");
        builder.AppendLine(finding.Title);
        builder.AppendLine($"근거: {finding.Evidence}");
        builder.AppendLine($"해석: {finding.Interpretation}");
        builder.AppendLine($"한계: {finding.Limitation}");
        builder.AppendLine($"다음 확인: {finding.NextStep}");
        builder.AppendLine();
        builder.AppendLine("[데이터 처리]");
        builder.AppendLine(snapshot.DataHandlingStatement);
        return builder.ToString().TrimEnd();
    }

    private static void AppendInterface(
        StringBuilder builder,
        string title,
        SafeLocalRouteInterfaceSnapshot? routeInterface)
    {
        builder.AppendLine();
        builder.AppendLine($"[{title}]");
        if (routeInterface is null)
        {
            builder.AppendLine("단일 안전 인터페이스 근거 없음");
            return;
        }

        builder.AppendLine(
            $"범주 / 지문: {routeInterface.Category} / {routeInterface.InterfaceFingerprint}");
        builder.AppendLine(
            $"Up / 기본 게이트웨이: {FormatNullableBoolean(routeInterface.IsUp)} / {FormatNullableBoolean(routeInterface.HasDefaultGateway)}");
        builder.AppendLine(
            $"가상 / VPN: {FormatNullableBoolean(routeInterface.IsVirtual)} / {FormatNullableBoolean(routeInterface.IsVpn)}");
        builder.AppendLine(
            $"현재 WLAN 일치: {FormatNullableBoolean(routeInterface.MatchesExpectedWlan)}");
    }

    private static string FormatNullableBoolean(bool? value) =>
        value switch
        {
            true => "예",
            false => "아니요",
            null => "판정 안 함"
        };
}
