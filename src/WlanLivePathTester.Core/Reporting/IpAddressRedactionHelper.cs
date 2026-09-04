using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace WlanLivePathTester.Core.Reporting;

public static partial class IpAddressRedactionHelper
{
    public static string RedactIpv6Candidates(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return Ipv6CandidateRegex().Replace(
            value,
            match => IPAddress.TryParse(match.Value, out IPAddress? address)
                && address.AddressFamily == AddressFamily.InterNetworkV6
                    ? "[IPv6 마스킹됨]"
                    : match.Value);
    }

    [GeneratedRegex(@"(?i)(?<![0-9A-F:])[0-9A-F:]{2,45}(?![0-9A-F:])")]
    private static partial Regex Ipv6CandidateRegex();
}
