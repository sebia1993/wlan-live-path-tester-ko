using System.Net;
using WlanLivePathTester.Core.Configuration;
using WlanLivePathTester.Core.Models;

namespace WlanLivePathTester.Core.Security;

public sealed record TargetHostPolicyResult(bool IsAllowed, string? ErrorCode, string Message);

public static class TargetHostPolicy
{
    public const int MaximumAdditionalHosts = 16;

    public static TargetHostPolicyResult EvaluateRedirect(MeasurementTargetDefinition target, Uri destination)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(destination);
        if (!NetworkInputBoundary.IsHttpUri(destination, out _)
            || TargetValidator.Validate(target).Count > 0)
            return new(false, "REDIRECT_INPUT_INVALID", "리다이렉트 입력 또는 최초 대상 정책이 올바르지 않습니다.");

        MeasurementTargetDefinition effectiveTarget = ApprovedTargetRuntimeCatalog.Apply(target);
        Uri origin = new(effectiveTarget.Url, UriKind.Absolute);
        string destinationHost = NormalizeUriHost(destination);
        string originHost = NormalizeUriHost(origin);
        bool hostAllowed = destinationHost.Equals(originHost, StringComparison.OrdinalIgnoreCase)
            || effectiveTarget.AllowedRedirectHosts?.Any(raw =>
                NetworkInputBoundary.TryAllowedHost(raw, out string approvedHost, out _)
                && destinationHost.Equals(approvedHost, StringComparison.OrdinalIgnoreCase)) == true;
        if (!hostAllowed)
            return new(false, "REDIRECT_HOST_NOT_APPROVED",
                "리다이렉트 대상 호스트가 최초 대상 또는 allowedRedirectHosts 승인 목록에 없습니다.");

        bool sameAuthority = destinationHost.Equals(originHost, StringComparison.OrdinalIgnoreCase)
            && destination.Port == origin.Port;
        if (!sameAuthority && !destination.IsDefaultPort)
            return new(false, "REDIRECT_PORT_NOT_APPROVED", "리다이렉트 대상이 승인되지 않은 비표준 포트를 사용합니다.");
        return new(true, null, "리다이렉트 호스트 정책을 통과했습니다.");
    }

    public static IReadOnlyList<string> ValidateConfiguredHosts(MeasurementTargetDefinition target)
    {
        ArgumentNullException.ThrowIfNull(target);
        IReadOnlyList<string>? configured = target.AllowedRedirectHosts;
        if (configured is null || configured.Count == 0) return Array.Empty<string>();
        List<string> errors = [];
        if (configured.Count > MaximumAdditionalHosts)
        {
            errors.Add($"allowedRedirectHosts는 최대 {MaximumAdditionalHosts}개까지 허용합니다.");
            return errors;
        }
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        foreach (string? rawHost in configured)
        {
            if (!NetworkInputBoundary.TryAllowedHost(rawHost, out string host, out string error))
            {
                errors.Add($"allowedRedirectHosts: {error}");
                continue;
            }
            if (!seen.Add(host))
            {
                errors.Add("allowedRedirectHosts에 정규화 후 중복되는 호스트가 있습니다.");
                continue;
            }
            if (target.PathKind == NetworkPathKind.External)
            {
                if (IPAddress.TryParse(host, out IPAddress? address))
                {
                    if (TargetValidator.IsLocalOrPrivate(address))
                        errors.Add("외부 대상의 allowedRedirectHosts에 로컬·사설·링크 로컬 IP를 사용할 수 없습니다.");
                }
                else if (NetworkInputBoundary.IsLocalDnsName(host))
                    errors.Add("외부 대상의 allowedRedirectHosts에 로컬 또는 단일 레이블 이름을 사용할 수 없습니다.");
            }
        }
        return errors;
    }

    private static string NormalizeUriHost(Uri uri)
    {
        string host = uri.IdnHost.Trim('[', ']').TrimEnd('.');
        return IPAddress.TryParse(host, out IPAddress? address)
            ? address.ToString() : host.ToLowerInvariant();
    }
}
