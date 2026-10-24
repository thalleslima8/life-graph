using System.Text.RegularExpressions;

namespace LifeGraph.Resources.Domain;

/// <summary>Recognizes YouTube links and the video id they point at (DA-060).</summary>
internal static partial class YouTubeAddress
{
    public const string Provider = "youtube";

    private const string VideoIdParameter = "v";

    private static readonly HashSet<string> WatchHosts = new(StringComparer.Ordinal)
    {
        "youtube.com",
        "www.youtube.com",
        "m.youtube.com",
        "music.youtube.com",
        "youtube-nocookie.com",
        "www.youtube-nocookie.com",
    };

    private const string ShortLinkHost = "youtu.be";

    /// <summary>The path prefixes followed by the video id, as in <c>/shorts/{id}</c>.</summary>
    private static readonly string[] VideoPathPrefixes = ["shorts", "embed", "live", "v"];

    public static bool IsYouTube(Uri address) =>
        WatchHosts.Contains(address.IdnHost) || address.IdnHost == ShortLinkHost;

    /// <summary>The 11-character video id, or <c>null</c> when the link is not a single video (a channel, a playlist).</summary>
    public static string? VideoIdOf(Uri address)
    {
        var segments = address.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        string? candidate = null;
        if (address.IdnHost == ShortLinkHost)
        {
            candidate = segments.Length == 1 ? segments[0] : null;
        }
        else if (WatchHosts.Contains(address.IdnHost))
        {
            candidate = segments switch
            {
                ["watch"] => QueryValue(address, VideoIdParameter),
                [var prefix, var id] when VideoPathPrefixes.Contains(prefix, StringComparer.Ordinal) => id,
                _ => null,
            };
        }

        return candidate is not null && VideoId().IsMatch(candidate) ? candidate : null;
    }

    public static string WatchUrlOf(string videoId) => $"https://www.youtube.com/watch?v={videoId}";

    private static string? QueryValue(Uri address, string name)
    {
        foreach (var parameter in address.Query.TrimStart('?').Split('&'))
        {
            var separator = parameter.IndexOf('=');
            if (separator > 0 && parameter[..separator] == name)
            {
                return parameter[(separator + 1)..];
            }
        }

        return null;
    }

    [GeneratedRegex("^[A-Za-z0-9_-]{11}$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex VideoId();
}
