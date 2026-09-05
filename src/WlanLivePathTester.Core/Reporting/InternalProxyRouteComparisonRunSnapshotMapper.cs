using WlanLivePathTester.Core.NetworkEnvironment;
using WlanLivePathTester.Core.Proxy;
using WlanLivePathTester.Core.Routing;

namespace WlanLivePathTester.Core.Reporting;

public sealed record InternalProxyRouteComparisonRunSnapshot(
    string SchemaVersion,
    DateTimeOffset CompletedAt,
    bool SensitiveValuesIncluded,
    string DataHandlingStatement,
    string RunStatus,
    string ProxySourceKind,
    string ProxyDecision,
    string? TargetScheme,
    string? InternalRouteStatus,
    string? ProxyRouteStatus,
    string? ComparisonStatus,
    bool? SameLocalInterface,
    SafeLocalRouteInterfaceSnapshot? InternalInterface,
    SafeLocalRouteInterfaceSnapshot? ProxyInterface,
    int ParsedProxyEndpointCount,
    int AnalyzedProxyEndpointCount,
    int SuccessfulProxyEndpointCount,
    int ProxyDistinctInterfaceCount,
    bool DirectPresent,
    bool DirectFallback,
    bool ExpectedWlanIdentityAvailable,
    bool InternalRouteReadPerformed,
    bool ProxyRouteAnalysisPerformed,
    bool InternalEvidencePartial,
    bool ProxyEvidencePartial,
    bool AnyVirtualInterface,
    bool AnyVpnOrTunnelInterface,
    ReportFinding Finding);

public sealed record SafeLocalRouteInterfaceSnapshot(
    string InterfaceFingerprint,
    string Category,
    bool? IsVirtual,
    bool? IsVpn,
    bool? IsUp,
    bool? HasDefaultGateway,
    bool? MatchesExpectedWlan);

public static class InternalProxyRouteComparisonRunSnapshotMapper
{
    public static InternalProxyRouteComparisonRunSnapshot FromResult(
        InternalProxyRouteComparisonRunResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        InternalProxyRouteComparisonResult? comparison =
            result.Comparison;
        return new InternalProxyRouteComparisonRunSnapshot(
            SchemaVersion: "1.0",
            CompletedAt: result.CompletedAt,
            SensitiveValuesIncluded: false,
            DataHandlingStatement:
                "이 스냅샷은 내부 DIRECT–프록시 로컬 경로 비교의 검증된 enum, 개수, Boolean과 짧은 비가역 인터페이스 지문만 포함합니다. 원본 대상·프록시 호스트·전체 인터페이스 ID·경로 객체는 포함하지 않습니다.",
            RunStatus: NormalizeEnum(
                result.Status,
                InternalProxyRouteComparisonRunStatus.Failed),
            ProxySourceKind: NormalizeEnum(
                result.ProxySourceKind,
                ProxyEndpointSourceKind.Unknown),
            ProxyDecision: NormalizeEnum(
                result.ProxyDecision,
                ProxyEndpointDecision.Unknown),
            TargetScheme: NormalizeTargetScheme(result.TargetScheme),
            InternalRouteStatus: NormalizeNullableEnum(
                result.InternalRouteStatus),
            ProxyRouteStatus: NormalizeNullableEnum(
                result.ProxyRouteStatus),
            ComparisonStatus: comparison is null
                ? null
                : NormalizeEnum(
                    comparison.Status,
                    InternalProxyRouteComparisonStatus.Incomplete),
            SameLocalInterface: comparison?.SameLocalInterface,
            InternalInterface: MapInterface(
                comparison?.InternalInterface),
            ProxyInterface: MapInterface(
                comparison?.ProxyInterface),
            ParsedProxyEndpointCount: Math.Max(
                0,
                result.ParsedProxyEndpointCount),
            AnalyzedProxyEndpointCount: Math.Max(
                0,
                result.AnalyzedProxyEndpointCount),
            SuccessfulProxyEndpointCount: Math.Max(
                0,
                result.SuccessfulProxyEndpointCount),
            ProxyDistinctInterfaceCount: Math.Max(
                0,
                comparison?.ProxyDistinctInterfaceCount ?? 0),
            DirectPresent: result.DirectPresent,
            DirectFallback: result.DirectFallback,
            ExpectedWlanIdentityAvailable:
                result.ExpectedWlanIdentityAvailable,
            InternalRouteReadPerformed:
                result.InternalRouteReadPerformed,
            ProxyRouteAnalysisPerformed:
                result.ProxyRouteAnalysisPerformed,
            InternalEvidencePartial:
                comparison?.InternalEvidencePartial ?? false,
            ProxyEvidencePartial:
                comparison?.ProxyEvidencePartial ?? false,
            AnyVirtualInterface:
                comparison?.AnyVirtualInterface ?? false,
            AnyVpnOrTunnelInterface:
                comparison?.AnyVpnOrTunnelInterface ?? false,
            Finding:
                InternalProxyRouteComparisonRunFindingMapper
                    .FromResult(result));
    }

    private static SafeLocalRouteInterfaceSnapshot? MapInterface(
        LocalRouteComparisonInterface? routeInterface)
    {
        if (routeInterface is null)
        {
            return null;
        }

        string? fingerprint = NormalizeFingerprint(
            routeInterface.InterfaceFingerprint);
        if (fingerprint is null
            || !Enum.IsDefined(routeInterface.Category))
        {
            return null;
        }

        return new SafeLocalRouteInterfaceSnapshot(
            InterfaceFingerprint: fingerprint,
            Category: routeInterface.Category.ToString(),
            IsVirtual: routeInterface.IsVirtual,
            IsVpn: routeInterface.IsVpn,
            IsUp: routeInterface.IsUp,
            HasDefaultGateway: routeInterface.HasDefaultGateway,
            MatchesExpectedWlan:
                routeInterface.MatchesExpectedWlan);
    }

    private static string NormalizeEnum<TEnum>(
        TEnum value,
        TEnum fallback)
        where TEnum : struct, Enum =>
        Enum.IsDefined(value)
            ? value.ToString()
            : fallback.ToString();

    private static string? NormalizeNullableEnum<TEnum>(
        TEnum? value)
        where TEnum : struct, Enum =>
        value.HasValue
            ? Enum.IsDefined(value.Value)
                ? value.Value.ToString()
                : "Unknown"
            : null;

    private static string? NormalizeTargetScheme(string? value)
    {
        string candidate = (value ?? string.Empty)
            .Trim()
            .ToLowerInvariant();
        return candidate is "http" or "https"
            ? candidate
            : null;
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
}
