using System.Net;
using System.Net.Sockets;

namespace LifeGraph.Infrastructure.Outbound;

/// <summary>
/// The HTTP handler for fetching a URL someone else chose (SSRF guard, DA-029). HTTPS only,
/// no redirects, no proxy and no cookies; the host is resolved here and the connection goes
/// to the vetted address itself, so a DNS answer cannot change between the check and the
/// connect (rebinding). Bodies are read through <see cref="ReadCappedAsync"/>.
/// </summary>
public static class SafeOutboundHttp
{
    /// <summary>A pooled connection is replaced after this long, so a host's new DNS answer is vetted again.</summary>
    public static readonly TimeSpan PooledConnectionLifetime = TimeSpan.FromMinutes(2);

    public static SocketsHttpHandler CreateHandler(TimeSpan connectTimeout) => new()
    {
        AllowAutoRedirect = false,
        UseCookies = false,
        UseProxy = false,
        ConnectTimeout = connectTimeout,
        AutomaticDecompression = DecompressionMethods.None,
        PooledConnectionLifetime = PooledConnectionLifetime,
        ConnectCallback = ConnectToAllowedAddressAsync,
    };

    /// <summary>Only <c>https</c> URLs with a host name or a public address, no user info and no fragment.</summary>
    public static bool IsFetchableUrl(Uri url)
    {
        ArgumentNullException.ThrowIfNull(url);

        return url.IsAbsoluteUri
            && url.Scheme == Uri.UriSchemeHttps
            && string.IsNullOrEmpty(url.UserInfo)
            && string.IsNullOrEmpty(url.Fragment)
            && (url.HostNameType != UriHostNameType.IPv4 && url.HostNameType != UriHostNameType.IPv6
                || OutboundAddressPolicy.IsAllowed(IPAddress.Parse(url.IdnHost)));
    }

    /// <summary>
    /// Reads at most <paramref name="maxBytes"/> of the body; <c>null</c> when it is longer,
    /// so an endless or oversized answer never fills memory.
    /// </summary>
    public static async Task<byte[]?> ReadCappedAsync(HttpContent content, int maxBytes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);

        if (content.Headers.ContentLength > maxBytes)
        {
            return null;
        }

        await using var stream = await content.ReadAsStreamAsync(cancellationToken);
        var buffer = new byte[maxBytes + 1];
        var total = 0;
        int read;
        while (total < buffer.Length && (read = await stream.ReadAsync(buffer.AsMemory(total), cancellationToken)) > 0)
        {
            total += read;
        }

        return total > maxBytes ? null : buffer[..total];
    }

    private static async ValueTask<Stream> ConnectToAllowedAddressAsync(SocketsHttpConnectionContext context, CancellationToken cancellationToken)
    {
        var host = context.DnsEndPoint.Host;
        var candidates = IPAddress.TryParse(host, out var literal)
            ? [literal]
            : await Dns.GetHostAddressesAsync(host, cancellationToken);

        // Every answer must be public: a name that also resolves to a private address is refused.
        if (candidates.Length == 0 || !candidates.All(OutboundAddressPolicy.IsAllowed))
        {
            throw new HttpRequestException(HttpRequestError.ConnectionError, "The destination address is not allowed.");
        }

        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(candidates, context.DnsEndPoint.Port, cancellationToken);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }
}
