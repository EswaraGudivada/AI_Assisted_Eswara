using System.Net;
using System.Net.Sockets;
using UrlShortener.Core.Common;

namespace UrlShortener.Core.Safety;

/// <summary>
/// Refuses targets that point at infrastructure: loopback, RFC 1918, link-local (incl. the cloud metadata
/// address 169.254.169.254), CGNAT, unique-local IPv6 and internal host names.
/// Deliberately does not resolve DNS: the answer can change a second later (DNS rebinding), and this service
/// never fetches the target itself.
/// </summary>
public sealed class PrivateNetworkRule(SecurityOptions options) : IUrlSafetyRule
{
    private static readonly string[] BlockedNames = ["localhost", "metadata.google.internal"];
    private static readonly string[] BlockedSuffixes = [".localhost", ".local", ".internal", ".localdomain"];

    public Error? Evaluate(Uri url)
    {
        ArgumentNullException.ThrowIfNull(url);
        if (options.AllowPrivateNetworkTargets)
        {
            return null;
        }

        var host = url.Host;
        if (BlockedNames.Contains(host, StringComparer.OrdinalIgnoreCase)
            || BlockedSuffixes.Any(s => host.EndsWith(s, StringComparison.OrdinalIgnoreCase)))
        {
            return ShortLinkErrors.PrivateTarget;
        }

        return IPAddress.TryParse(host.Trim('[', ']'), out var address) && IsPrivate(address)
            ? ShortLinkErrors.PrivateTarget
            : null;
    }

    internal static bool IsPrivate(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any))
        {
            return true;
        }

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = address.GetAddressBytes();
            return b[0] switch
            {
                0 or 10 or 127 => true,
                169 => b[1] == 254,
                172 => b[1] is >= 16 and <= 31,
                192 => b[1] == 168,
                100 => b[1] is >= 64 and <= 127,
                _ => false,
            };
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            return address.IsIPv6LinkLocal
                || address.IsIPv6SiteLocal
                || address.IsIPv6UniqueLocal
                || (address.GetAddressBytes()[0] & 0xFE) == 0xFC;
        }

        return true;
    }
}
