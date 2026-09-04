using WlanLivePathTester.Core.Models;

namespace WlanLivePathTester.Core.NetworkEnvironment;

public enum WlanInterfaceCorrelationStatus
{
    NoConnectedWlan,
    MatchedByInterfaceId,
    MatchedByDescription,
    MultipleMatches,
    NoMatch
}

public sealed record WlanInterfaceCorrelationResult(
    WlanInterfaceCorrelationStatus Status,
    string? MatchedDisplayName,
    bool? MatchedAdapterIsUp,
    bool? MatchedAdapterHasDefaultGateway,
    bool? MatchedAdapterIsVirtual,
    bool? MatchedAdapterIsVpn,
    string Message,
    IReadOnlyList<string> Warnings)
{
    public bool IsMatched =>
        Status is WlanInterfaceCorrelationStatus.MatchedByInterfaceId
            or WlanInterfaceCorrelationStatus.MatchedByDescription;
}

public static class WlanInterfaceCorrelator
{
    public static WlanInterfaceCorrelationResult Correlate(
        WlanSnapshot? connectedWlan,
        IReadOnlyList<LocalNetworkAdapterSnapshot> adapters)
    {
        ArgumentNullException.ThrowIfNull(adapters);

        if (connectedWlan is null || !connectedWlan.IsConnected)
        {
            return new WlanInterfaceCorrelationResult(
                Status: WlanInterfaceCorrelationStatus.NoConnectedWlan,
                MatchedDisplayName: null,
                MatchedAdapterIsUp: null,
                MatchedAdapterHasDefaultGateway: null,
                MatchedAdapterIsVirtual: null,
                MatchedAdapterIsVpn: null,
                Message: "연결된 Native WLAN 인터페이스가 없어 로컬 NIC와 대응시키지 않았습니다.",
                Warnings: Array.Empty<string>());
        }

        LocalNetworkAdapterSnapshot[] wirelessAdapters = adapters
            .Where(adapter =>
                adapter.Category == NetworkAdapterCategory.Wireless)
            .ToArray();

        string? wlanId = NormalizeInterfaceId(connectedWlan.InterfaceId);
        if (wlanId is not null)
        {
            LocalNetworkAdapterSnapshot[] idMatches = wirelessAdapters
                .Where(adapter => string.Equals(
                    NormalizeInterfaceId(adapter.InterfaceId),
                    wlanId,
                    StringComparison.OrdinalIgnoreCase))
                .ToArray();

            if (idMatches.Length == 1)
            {
                return CreateMatched(
                    WlanInterfaceCorrelationStatus.MatchedByInterfaceId,
                    idMatches[0],
                    "Native WLAN 인터페이스 GUID와 로컬 NetworkInterface ID가 일치합니다.");
            }

            if (idMatches.Length > 1)
            {
                return Multiple(
                    "같은 인터페이스 ID를 가진 무선 어댑터가 여러 개여서 대응을 확정하지 않았습니다.");
            }
        }

        string description = NormalizeDescription(
            connectedWlan.InterfaceDescription);
        if (!string.IsNullOrWhiteSpace(description))
        {
            LocalNetworkAdapterSnapshot[] descriptionMatches = wirelessAdapters
                .Where(adapter =>
                    NormalizeDescription(adapter.Description).Equals(
                        description,
                        StringComparison.OrdinalIgnoreCase)
                    || NormalizeDescription(adapter.DisplayName).Equals(
                        description,
                        StringComparison.OrdinalIgnoreCase))
                .ToArray();

            if (descriptionMatches.Length == 1)
            {
                return CreateMatched(
                    WlanInterfaceCorrelationStatus.MatchedByDescription,
                    descriptionMatches[0],
                    "인터페이스 GUID를 직접 일치시키지 못했지만 Native WLAN 설명과 로컬 무선 NIC 설명이 정확히 일치합니다.");
            }

            if (descriptionMatches.Length > 1)
            {
                return Multiple(
                    "같은 설명을 가진 무선 어댑터가 여러 개여서 대응을 확정하지 않았습니다.");
            }
        }

        return new WlanInterfaceCorrelationResult(
            Status: WlanInterfaceCorrelationStatus.NoMatch,
            MatchedDisplayName: null,
            MatchedAdapterIsUp: null,
            MatchedAdapterHasDefaultGateway: null,
            MatchedAdapterIsVirtual: null,
            MatchedAdapterIsVpn: null,
            Message: wirelessAdapters.Length == 0
                ? "로컬 인터페이스 목록에 무선 어댑터가 없어 Native WLAN 결과와 대응시키지 못했습니다."
                : $"로컬 무선 어댑터 {wirelessAdapters.Length}개 중 Native WLAN 인터페이스와 정확히 일치하는 항목을 찾지 못했습니다.",
            Warnings:
            [
                "인터페이스 대응이 확인되지 않았으므로 브라우저 관찰 카운터와 Native WLAN 상태가 같은 NIC라고 단정하지 마십시오."
            ]);
    }

    private static WlanInterfaceCorrelationResult CreateMatched(
        WlanInterfaceCorrelationStatus status,
        LocalNetworkAdapterSnapshot adapter,
        string message)
    {
        List<string> warnings = [];
        if (!adapter.IsUp)
        {
            warnings.Add("Native WLAN은 연결 상태이지만 대응된 로컬 어댑터가 Up 상태가 아닙니다.");
        }

        if (adapter.IsVirtual)
        {
            warnings.Add("대응된 무선 어댑터가 가상 인터페이스로 분류됐습니다.");
        }

        if (adapter.IsVpn)
        {
            warnings.Add("대응된 어댑터가 VPN 또는 터널 후보로도 분류됐습니다.");
        }

        if (!adapter.HasDefaultGateway)
        {
            warnings.Add("대응된 Wi-Fi 어댑터에 기본 게이트웨이가 확인되지 않았습니다. 더 구체적인 경로 또는 다른 인터페이스가 사용될 수 있습니다.");
        }

        return new WlanInterfaceCorrelationResult(
            Status: status,
            MatchedDisplayName: adapter.DisplayName,
            MatchedAdapterIsUp: adapter.IsUp,
            MatchedAdapterHasDefaultGateway: adapter.HasDefaultGateway,
            MatchedAdapterIsVirtual: adapter.IsVirtual,
            MatchedAdapterIsVpn: adapter.IsVpn,
            Message: message,
            Warnings: warnings);
    }

    private static WlanInterfaceCorrelationResult Multiple(
        string message) =>
        new(
            Status: WlanInterfaceCorrelationStatus.MultipleMatches,
            MatchedDisplayName: null,
            MatchedAdapterIsUp: null,
            MatchedAdapterHasDefaultGateway: null,
            MatchedAdapterIsVirtual: null,
            MatchedAdapterIsVpn: null,
            Message: message,
            Warnings:
            [
                "중복 후보가 있으므로 인터페이스 이름과 Windows 라우팅 정보를 직접 비교하십시오."
            ]);

    private static string? NormalizeInterfaceId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string trimmed = value.Trim().Trim('{', '}');
        return Guid.TryParse(trimmed, out Guid parsed)
            ? parsed.ToString("D")
            : trimmed.ToLowerInvariant();
    }

    private static string NormalizeDescription(string? value) =>
        string.Join(
            ' ',
            (value ?? string.Empty)
                .Split(
                    [' ', '\t', '\r', '\n'],
                    StringSplitOptions.RemoveEmptyEntries
                    | StringSplitOptions.TrimEntries));
}
