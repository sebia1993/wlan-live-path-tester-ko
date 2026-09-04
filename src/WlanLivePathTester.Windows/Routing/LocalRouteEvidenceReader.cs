using System.Net;
using System.Net.Sockets;
using System.Runtime.Versioning;
using WlanLivePathTester.Core.Routing;

namespace WlanLivePathTester.Windows.Routing;

[SupportedOSPlatform("windows")]
public static class LocalRouteEvidenceReader
{
    private const int MaximumInputLength = 2048;
    private const int MaximumResolvedAddresses = 16;

    public static async Task<DestinationRouteEvidence> ReadAsync(
        string target,
        string targetLabel,
        RouteProbePurpose purpose,
        int dnsTimeoutSeconds = 5,
        CancellationToken cancellationToken = default)
    {
        string safeLabel = NormalizeLabel(targetLabel);
        if (!OperatingSystem.IsWindows())
        {
            return RouteEvidenceEvaluator.Invalid(
                safeLabel,
                purpose,
                "Windows에서만 목적지별 최적 인터페이스를 확인할 수 있습니다.");
        }

        if (dnsTimeoutSeconds is < 1 or > 30)
        {
            return RouteEvidenceEvaluator.Invalid(
                safeLabel,
                purpose,
                "DNS 제한 시간은 1~30초 범위여야 합니다.");
        }

        if (!TryExtractHost(target, out string host, out string error))
        {
            return RouteEvidenceEvaluator.Invalid(
                safeLabel,
                purpose,
                error);
        }

        bool dnsWasUsed = !IPAddress.TryParse(host, out IPAddress? literal);
        IPAddress[] addresses;
        try
        {
            if (literal is not null)
            {
                addresses = [literal];
            }
            else
            {
                using CancellationTokenSource timeout =
                    CancellationTokenSource.CreateLinkedTokenSource(
                        cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(dnsTimeoutSeconds));
                addresses = await Dns.GetHostAddressesAsync(
                        host,
                        timeout.Token)
                    .ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            return RouteEvidenceEvaluator.Canceled(
                safeLabel,
                purpose,
                dnsWasUsed);
        }
        catch (OperationCanceledException)
        {
            return RouteEvidenceEvaluator.ResolutionFailed(
                safeLabel,
                purpose,
                dnsWasUsed,
                $"DNS 확인이 {dnsTimeoutSeconds}초 안에 완료되지 않았습니다.");
        }
        catch (SocketException exception)
        {
            return RouteEvidenceEvaluator.ResolutionFailed(
                safeLabel,
                purpose,
                dnsWasUsed,
                $"호스트 주소를 확인하지 못했습니다: SocketError={exception.SocketErrorCode}");
        }
        catch (ArgumentException)
        {
            return RouteEvidenceEvaluator.ResolutionFailed(
                safeLabel,
                purpose,
                dnsWasUsed,
                "호스트 이름을 DNS 확인에 사용할 수 없습니다.");
        }

        IPAddress[] usableAddresses = addresses
            .Where(IsUsableAddress)
            .GroupBy(
                address => address.ToString(),
                StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(address =>
                address.AddressFamily == AddressFamily.InterNetwork
                    ? 0
                    : 1)
            .ThenBy(address => address.ToString(), StringComparer.Ordinal)
            .Take(MaximumResolvedAddresses)
            .ToArray();

        if (usableAddresses.Length == 0)
        {
            return RouteEvidenceEvaluator.ResolutionFailed(
                safeLabel,
                purpose,
                dnsWasUsed,
                "IPv4 또는 IPv6 유니캐스트 주소를 확인하지 못했습니다.");
        }

        List<RouteAddressEvidence> evidence = [];
        foreach (IPAddress address in usableAddresses)
        {
            cancellationToken.ThrowIfCancellationRequested();
            WindowsBestInterfaceResult result =
                WindowsBestInterfaceResolver.Resolve(address);
            evidence.Add(new RouteAddressEvidence(
                AddressFamily: address.AddressFamily
                    == AddressFamily.InterNetwork
                    ? RouteAddressFamilyKind.IPv4
                    : RouteAddressFamilyKind.IPv6,
                Status: result.Status,
                Interface: result.Interface,
                NativeErrorCode: result.NativeErrorCode,
                Message: result.Message));
        }

        return RouteEvidenceEvaluator.Evaluate(
            capturedAt: DateTimeOffset.UtcNow,
            targetLabel: safeLabel,
            purpose: purpose,
            dnsWasUsed: dnsWasUsed,
            resolvedAddressCount: usableAddresses.Length,
            addressEvidence: evidence);
    }

    public static bool TryExtractHost(
        string? target,
        out string host,
        out string error)
    {
        host = string.Empty;
        error = string.Empty;
        string value = (target ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            error = "URL, 호스트 이름 또는 IP 주소를 입력하십시오.";
            return false;
        }

        if (value.Length > MaximumInputLength
            || value.Contains('\r')
            || value.Contains('\n')
            || value.Contains('\0'))
        {
            error = "대상 입력이 너무 길거나 허용되지 않는 제어 문자를 포함합니다.";
            return false;
        }

        string unbracketed = value.Trim('[', ']');
        if (IPAddress.TryParse(unbracketed, out IPAddress? address))
        {
            host = address.ToString();
            return true;
        }

        if (Uri.TryCreate(value, UriKind.Absolute, out Uri? absoluteUri))
        {
            if (!absoluteUri.Scheme.Equals(
                    Uri.UriSchemeHttp,
                    StringComparison.OrdinalIgnoreCase)
                && !absoluteUri.Scheme.Equals(
                    Uri.UriSchemeHttps,
                    StringComparison.OrdinalIgnoreCase))
            {
                error = "HTTP 또는 HTTPS URL만 지원합니다.";
                return false;
            }

            if (!string.IsNullOrEmpty(absoluteUri.UserInfo)
                || !string.IsNullOrEmpty(absoluteUri.Fragment))
            {
                error = "사용자 정보 또는 fragment가 포함된 URL은 사용할 수 없습니다.";
                return false;
            }

            host = absoluteUri.IdnHost;
            return ValidateHost(host, out error);
        }

        if (value.Contains("://", StringComparison.Ordinal))
        {
            error = "지원하지 않는 URL 스킴입니다.";
            return false;
        }

        if (!Uri.TryCreate(
                "http://" + value,
                UriKind.Absolute,
                out Uri? hostUri)
            || !string.IsNullOrEmpty(hostUri.UserInfo)
            || !string.IsNullOrEmpty(hostUri.Query)
            || !string.IsNullOrEmpty(hostUri.Fragment)
            || !hostUri.AbsolutePath.Equals(
                "/",
                StringComparison.Ordinal))
        {
            error = "호스트 이름, IP 주소 또는 host:port 형식이어야 합니다.";
            return false;
        }

        host = hostUri.IdnHost;
        return ValidateHost(host, out error);
    }

    private static bool ValidateHost(string value, out string error)
    {
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(value)
            || value.Length > 253
            || Uri.CheckHostName(value) == UriHostNameType.Unknown)
        {
            error = "유효한 DNS 호스트 이름 또는 IP 주소가 아닙니다.";
            return false;
        }

        return true;
    }

    private static bool IsUsableAddress(IPAddress address)
    {
        if (address.AddressFamily is not AddressFamily.InterNetwork
            and not AddressFamily.InterNetworkV6)
        {
            return false;
        }

        if (address.Equals(IPAddress.Any)
            || address.Equals(IPAddress.IPv6Any)
            || address.Equals(IPAddress.None)
            || address.Equals(IPAddress.IPv6None)
            || address.IsIPv6Multicast)
        {
            return false;
        }

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            byte first = address.GetAddressBytes()[0];
            return first < 224;
        }

        return true;
    }

    private static string NormalizeLabel(string? label)
    {
        string normalized = (label ?? string.Empty)
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Replace('\t', ' ')
            .Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return "라우팅 확인 대상";
        }

        return normalized.Length <= 100
            ? normalized
            : normalized[..97] + "...";
    }
}
