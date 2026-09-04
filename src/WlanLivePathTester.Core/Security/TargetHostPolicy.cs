using System.Net;
using WlanLivePathTester.Core.Models;

namespace WlanLivePathTester.Core.Security;

public sealed record TargetHostPolicyResult(
    bool IsAllowed,
    string? ErrorCode,
    string Message);

public static class TargetHostPolicy
{
    public const int MaximumAdditionalHosts = 16;

    public static TargetHostPolicyResult EvaluateRedirect(
        MeasurementTargetDefinition target,
        Uri destination)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(destination);

        Uri origin = new(target.Url, UriKind.Absolute);
        string destinationHost = Normalize(destination.IdnHost);
        string originHost = Normalize(origin.IdnHost);

        bool hostAllowed = destinationHost.Equals(
                originHost,
                StringComparison.OrdinalIgnoreCase)
            || (target.AllowedRedirectHosts?.Any(host =>
                destinationHost.Equals(
                    Normalize(host),
                    StringComparison.OrdinalIgnoreCase)) == true);

        if (!hostAllowed)
        {
            return new TargetHostPolicyResult(
                IsAllowed: false,
                ErrorCode: "REDIRECT_HOST_NOT_APPROVED",
                Message: "리다이렉트 대상 호스트가 최초 대상 또는 allowedRedirectHosts 승인 목록에 없습니다.");
        }

        bool sameAuthority = destinationHost.Equals(
                originHost,
                StringComparison.OrdinalIgnoreCase)
            && destination.Port == origin.Port;
        bool defaultDestinationPort = destination.IsDefaultPort
            || (destination.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
                && destination.Port == 443)
            || (destination.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                && destination.Port == 80);

        if (!sameAuthority && !defaultDestinationPort)
        {
            return new TargetHostPolicyResult(
                IsAllowed: false,
                ErrorCode: "REDIRECT_PORT_NOT_APPROVED",
                Message: "리다이렉트 대상이 승인되지 않은 비표준 포트를 사용합니다.");
        }

        return new TargetHostPolicyResult(
            IsAllowed: true,
            ErrorCode: null,
            Message: "리다이렉트 호스트 정책을 통과했습니다.");
    }

    public static IReadOnlyList<string> ValidateConfiguredHosts(
        MeasurementTargetDefinition target)
    {
        ArgumentNullException.ThrowIfNull(target);

        IReadOnlyList<string>? configured = target.AllowedRedirectHosts;
        if (configured is null || configured.Count == 0)
        {
            return Array.Empty<string>();
        }

        List<string> errors = [];
        if (configured.Count > MaximumAdditionalHosts)
        {
            errors.Add($"allowedRedirectHosts는 최대 {MaximumAdditionalHosts}개까지 허용합니다.");
        }

        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        foreach (string rawHost in configured)
        {
            string host = Normalize(rawHost);
            if (string.IsNullOrWhiteSpace(host)
                || host.Contains('/', StringComparison.Ordinal)
                || host.Contains('?', StringComparison.Ordinal)
                || host.Contains('#', StringComparison.Ordinal)
                || host.Contains('@', StringComparison.Ordinal)
                || host.Contains(':', StringComparison.Ordinal)
                || Uri.CheckHostName(host) == UriHostNameType.Unknown)
            {
                errors.Add("allowedRedirectHosts에는 스킴·경로·포트가 없는 정확한 DNS 호스트명 또는 IPv4 주소만 사용할 수 있습니다.");
                continue;
            }

            if (!seen.Add(host))
            {
                errors.Add($"allowedRedirectHosts에 중복 호스트가 있습니다: {host}");
                continue;
            }

            if (target.PathKind == NetworkPathKind.External
                && IPAddress.TryParse(host, out IPAddress? address)
                && TargetValidator.IsLocalOrPrivate(address))
            {
                errors.Add("외부 대상의 allowedRedirectHosts에 로컬·사설·링크 로컬 IP를 사용할 수 없습니다.");
            }
        }

        return errors;
    }

    private static string Normalize(string? host) =>
        (host ?? string.Empty).Trim().TrimEnd('.');
}
