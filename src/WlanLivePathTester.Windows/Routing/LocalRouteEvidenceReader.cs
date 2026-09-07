using System.Net;
using System.Net.Sockets;
using System.Runtime.Versioning;
using WlanLivePathTester.Core.Routing;
using WlanLivePathTester.Core.Security;

namespace WlanLivePathTester.Windows.Routing;

[SupportedOSPlatform("windows")]
public static class LocalRouteEvidenceReader
{
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
                safeLabel, purpose,
                "Windows에서만 목적지별 최적 인터페이스를 확인할 수 있습니다.");
        }
        if (!Enum.IsDefined(purpose) || dnsTimeoutSeconds is < 1 or > 30)
        {
            return RouteEvidenceEvaluator.Invalid(
                safeLabel, purpose,
                "조회 목적과 DNS 제한 시간(1~30초)을 확인하십시오.");
        }
        if (!TryExtractHost(target, out string host, out string error))
        {
            return RouteEvidenceEvaluator.Invalid(safeLabel, purpose, error);
        }

        bool dnsWasUsed = !IPAddress.TryParse(host, out IPAddress? literal);
        if (cancellationToken.IsCancellationRequested)
            return RouteEvidenceEvaluator.Canceled(safeLabel, purpose, dnsWasUsed: false);

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
                    CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(dnsTimeoutSeconds));
                addresses = await Dns.GetHostAddressesAsync(host, timeout.Token)
                    .ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return RouteEvidenceEvaluator.Canceled(safeLabel, purpose, dnsWasUsed);
        }
        catch (OperationCanceledException)
        {
            return RouteEvidenceEvaluator.ResolutionFailed(safeLabel, purpose, dnsWasUsed,
                $"DNS 확인이 {dnsTimeoutSeconds}초 안에 완료되지 않았습니다.");
        }
        catch (SocketException exception)
        {
            return RouteEvidenceEvaluator.ResolutionFailed(safeLabel, purpose, dnsWasUsed,
                $"호스트 주소를 확인하지 못했습니다: SocketError={exception.SocketErrorCode}");
        }
        catch (ArgumentException)
        {
            return RouteEvidenceEvaluator.ResolutionFailed(safeLabel, purpose, dnsWasUsed,
                "호스트 이름을 DNS 확인에 사용할 수 없습니다.");
        }

        IPAddress[] usableAddresses = addresses
            .Where(IsUsableAddress)
            .GroupBy(address => address.ToString(), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(address => address.AddressFamily == AddressFamily.InterNetwork ? 0 : 1)
            .ThenBy(address => address.ToString(), StringComparer.Ordinal)
            .Take(MaximumResolvedAddresses)
            .ToArray();

        if (usableAddresses.Length == 0)
        {
            return RouteEvidenceEvaluator.ResolutionFailed(safeLabel, purpose, dnsWasUsed,
                "IPv4 또는 IPv6 유니캐스트 주소를 확인하지 못했습니다.");
        }

        List<RouteAddressEvidence> evidence = [];
        foreach (IPAddress address in usableAddresses)
        {
            if (cancellationToken.IsCancellationRequested)
                return RouteEvidenceEvaluator.Canceled(safeLabel, purpose, dnsWasUsed);
            WindowsBestInterfaceResult result = WindowsBestInterfaceResolver.Resolve(address);
            evidence.Add(new RouteAddressEvidence(
                AddressFamily: address.AddressFamily == AddressFamily.InterNetwork
                    ? RouteAddressFamilyKind.IPv4 : RouteAddressFamilyKind.IPv6,
                Status: result.Status,
                Interface: result.Interface,
                NativeErrorCode: result.NativeErrorCode,
                Message: result.Message));
        }
        if (cancellationToken.IsCancellationRequested)
            return RouteEvidenceEvaluator.Canceled(safeLabel, purpose, dnsWasUsed);

        return RouteEvidenceEvaluator.Evaluate(
            capturedAt: DateTimeOffset.UtcNow,
            targetLabel: safeLabel,
            purpose: purpose,
            dnsWasUsed: dnsWasUsed,
            resolvedAddressCount: usableAddresses.Length,
            addressEvidence: evidence);
    }

    // The shared parser receives the original text: neither Trim nor Uri
    // construction may erase its length/control characters before admission.
    public static bool TryExtractHost(string? target, out string host, out string error) =>
        NetworkInputBoundary.TryRouteHost(target, out host, out error);

    private static bool IsUsableAddress(IPAddress address)
    {
        if (address.AddressFamily is not AddressFamily.InterNetwork and not AddressFamily.InterNetworkV6)
            return false;
        if (address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any)
            || address.Equals(IPAddress.None) || address.Equals(IPAddress.IPv6None)
            || address.IsIPv6Multicast)
            return false;
        if (address.AddressFamily == AddressFamily.InterNetwork)
            return address.GetAddressBytes()[0] < 224;
        return true;
    }

    private static string NormalizeLabel(string? label)
    {
        string normalized = (label ?? string.Empty)
            .Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ').Trim();
        if (string.IsNullOrWhiteSpace(normalized)) return "라우팅 확인 대상";
        return normalized.Length <= 100 ? normalized : normalized[..97] + "...";
    }
}
