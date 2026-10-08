using System.Net;
using System.Net.Sockets;

namespace LifeGraph.Infrastructure.Outbound;

/// <summary>
/// Which addresses a server-side fetch of a URL someone else chose may reach (SSRF guard,
/// GEN security, DA-029). Only public unicast addresses: loopback, private, link-local,
/// shared (CGNAT), multicast, documentation and reserved ranges are refused, IPv4 hidden
/// inside IPv6 (mapped, NAT64, 6to4) included. The CIMD fetch (E3) and the Resource fetch
/// (E7) share it.
/// </summary>
public static class OutboundAddressPolicy
{
    private static readonly (IPAddress Network, int PrefixLength)[] BlockedV4 =
    [
        (IPAddress.Parse("0.0.0.0"), 8),        // "this" network
        (IPAddress.Parse("10.0.0.0"), 8),       // private
        (IPAddress.Parse("100.64.0.0"), 10),    // shared address space (CGNAT)
        (IPAddress.Parse("127.0.0.0"), 8),      // loopback
        (IPAddress.Parse("169.254.0.0"), 16),   // link-local, cloud metadata
        (IPAddress.Parse("172.16.0.0"), 12),    // private
        (IPAddress.Parse("192.0.0.0"), 24),     // IETF protocol assignments
        (IPAddress.Parse("192.0.2.0"), 24),     // documentation
        (IPAddress.Parse("192.88.99.0"), 24),   // 6to4 relay anycast
        (IPAddress.Parse("192.168.0.0"), 16),   // private
        (IPAddress.Parse("198.18.0.0"), 15),    // benchmarking
        (IPAddress.Parse("198.51.100.0"), 24),  // documentation
        (IPAddress.Parse("203.0.113.0"), 24),   // documentation
        (IPAddress.Parse("224.0.0.0"), 4),      // multicast
        (IPAddress.Parse("240.0.0.0"), 4),      // reserved and broadcast
    ];

    private static readonly (IPAddress Network, int PrefixLength)[] BlockedV6 =
    [
        (IPAddress.Parse("::"), 128),           // unspecified
        (IPAddress.Parse("::1"), 128),          // loopback
        (IPAddress.Parse("100::"), 64),         // discard
        (IPAddress.Parse("2001:db8::"), 32),    // documentation
        (IPAddress.Parse("fc00::"), 7),         // unique local
        (IPAddress.Parse("fe80::"), 10),        // link-local
        (IPAddress.Parse("fec0::"), 10),        // site-local (deprecated)
        (IPAddress.Parse("ff00::"), 8),         // multicast
    ];

    private static readonly (IPAddress Network, int PrefixLength) Nat64 = (IPAddress.Parse("64:ff9b::"), 96);
    private static readonly (IPAddress Network, int PrefixLength) SixToFour = (IPAddress.Parse("2002::"), 16);
    private static readonly (IPAddress Network, int PrefixLength) Teredo = (IPAddress.Parse("2001::"), 32);

    /// <summary>Whether a fetch may connect to <paramref name="address"/>.</summary>
    public static bool IsAllowed(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            return !BlockedV4.Any(range => IsInRange(address, range));
        }

        if (address.AddressFamily != AddressFamily.InterNetworkV6)
        {
            return false;
        }

        // An IPv4 address carried inside IPv6 is judged as that IPv4 address.
        if (address.IsIPv4MappedToIPv6)
        {
            return IsAllowed(address.MapToIPv4());
        }

        var bytes = address.GetAddressBytes();
        if (IsInRange(address, Nat64))
        {
            return IsAllowed(new IPAddress(bytes[12..16]));
        }

        if (IsInRange(address, SixToFour))
        {
            return IsAllowed(new IPAddress(bytes[2..6]));
        }

        // Teredo hides the client's address obfuscated; nothing legitimate needs it here.
        if (IsInRange(address, Teredo))
        {
            return false;
        }

        // IPv4-compatible (deprecated): ::a.b.c.d
        if (bytes.Take(12).All(octet => octet == 0))
        {
            return false;
        }

        return !BlockedV6.Any(range => IsInRange(address, range));
    }

    private static bool IsInRange(IPAddress address, (IPAddress Network, int PrefixLength) range)
    {
        if (address.AddressFamily != range.Network.AddressFamily)
        {
            return false;
        }

        var addressBytes = address.GetAddressBytes();
        var networkBytes = range.Network.GetAddressBytes();
        var fullBytes = range.PrefixLength / 8;
        for (var index = 0; index < fullBytes; index++)
        {
            if (addressBytes[index] != networkBytes[index])
            {
                return false;
            }
        }

        var remainingBits = range.PrefixLength % 8;
        if (remainingBits == 0)
        {
            return true;
        }

        var mask = (byte)(0xFF << (8 - remainingBits));
        return (addressBytes[fullBytes] & mask) == (networkBytes[fullBytes] & mask);
    }
}
