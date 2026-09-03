using System.Net;
using System.Net.Sockets;
using WlanLivePathTester.Core.Models;

namespace WlanLivePathTester.Core.Security;

public static class TargetValidator
{
    private const long MinimumBytes = 1 * 1024 * 1024;
    private const long MaximumBytes = 1024L * 1024 * 1024;

    public static IReadOnlyList<string> Validate(MeasurementTargetDefinition target)
    {
        ArgumentNullException.ThrowIfNull(target);

        List<string> errors = [];

        if (string.IsNullOrWhiteSpace(target.Name))
        {
            errors.Add("대상 이름이 비어 있습니다.");
        }

        if (!Uri.TryCreate(target.Url, UriKind.Absolute, out Uri? uri))
        {
            errors.Add("유효한 절대 URL이 아닙니다.");
            return errors;
        }

        if (!uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            && !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("HTTP 또는 HTTPS URL만 허용합니다.");
        }

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            errors.Add("URL에 사용자 이름이나 비밀번호를 포함할 수 없습니다.");
        }

        if (!string.IsNullOrEmpty(uri.Fragment))
        {
            errors.Add("URL fragment는 측정 대상에 사용할 수 없습니다.");
        }

        if (target.RequireProxy && target.RequireDirect)
        {
            errors.Add("프록시 필수와 직접 연결 필수를 동시에 지정할 수 없습니다.");
        }

        if (target.MaxBytes is < MinimumBytes or > MaximumBytes)
        {
            errors.Add($"최대 다운로드 크기는 {MinimumBytes}~{MaximumBytes}바이트 범위여야 합니다.");
        }

        if (target.TimeoutSeconds is < 5 or > 300)
        {
            errors.Add("제한 시간은 5~300초 범위여야 합니다.");
        }

        if (target.Streams is < 1 or > 4)
        {
            errors.Add("동시 스트림 수는 1~4 범위여야 합니다.");
        }

        if (target.MaxRedirects is < 0 or > 10)
        {
            errors.Add("최대 리다이렉트 수는 0~10 범위여야 합니다.");
        }

        if (target.PathKind == NetworkPathKind.External
            && IPAddress.TryParse(uri.Host, out IPAddress? address)
            && IsLocalOrPrivate(address))
        {
            errors.Add("외부 측정 대상에 로컬·사설·링크 로컬 IP를 사용할 수 없습니다.");
        }

        return errors;
    }

    public static bool IsLocalOrPrivate(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);

        if (IPAddress.IsLoopback(address)
            || address.Equals(IPAddress.Any)
            || address.Equals(IPAddress.IPv6Any)
            || address.IsIPv6LinkLocal
            || address.IsIPv6Multicast
            || address.IsIPv6SiteLocal)
        {
            return true;
        }

        if (address.AddressFamily != AddressFamily.InterNetwork)
        {
            return false;
        }

        byte[] bytes = address.GetAddressBytes();
        return bytes[0] == 10
            || bytes[0] == 127
            || (bytes[0] == 169 && bytes[1] == 254)
            || (bytes[0] == 172 && bytes[1] is >= 16 and <= 31)
            || (bytes[0] == 192 && bytes[1] == 168)
            || bytes[0] >= 224;
    }
}
