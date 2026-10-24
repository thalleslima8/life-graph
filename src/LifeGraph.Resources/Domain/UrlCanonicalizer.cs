using System.Text;

namespace LifeGraph.Resources.Domain;

/// <summary>
/// The canonical form of a link, the key that makes a capture idempotent per Account (DA-060).
/// Only what cannot change the target is normalized: scheme and host case, the host in
/// punycode, the default port, an empty path, and the known tracking parameters
/// (<c>utm_*</c>). Every other query parameter is kept as sent and in order, since signed
/// Drive or S3 links stop working, or merge wrongly, without them. The fragment is kept too:
/// single-page apps route by it. A YouTube video becomes its watch link.
/// </summary>
public static class UrlCanonicalizer
{
    private const string TrackingParameterPrefix = "utm_";

    public static string Canonicalize(Uri address)
    {
        if (YouTubeAddress.IsYouTube(address) && YouTubeAddress.VideoIdOf(address) is { } videoId)
        {
            return YouTubeAddress.WatchUrlOf(videoId);
        }

        var canonical = new StringBuilder()
            .Append(address.Scheme)
            .Append(Uri.SchemeDelimiter)
            .Append(HostOf(address));
        if (!address.IsDefaultPort)
        {
            canonical.Append(':').Append(address.Port);
        }

        canonical.Append(address.AbsolutePath.Length == 0 ? "/" : address.AbsolutePath);

        var kept = address.Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Where(parameter => !parameter.StartsWith(TrackingParameterPrefix, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (kept.Count > 0)
        {
            canonical.Append('?').AppendJoin('&', kept);
        }

        return canonical.Append(address.Fragment).ToString();
    }

    private static string HostOf(Uri address) => address.HostNameType == UriHostNameType.IPv6
        ? $"[{address.IdnHost}]"
        : address.IdnHost.ToLowerInvariant();
}
