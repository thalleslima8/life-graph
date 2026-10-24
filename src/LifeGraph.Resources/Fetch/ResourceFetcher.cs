using System.Net;
using System.Net.Http.Headers;
using LifeGraph.Infrastructure.Outbound;

namespace LifeGraph.Resources.Fetch;

/// <summary>What one fetch asks for: the URL, and the only media type whose body it reads.</summary>
internal sealed record ResourceRequest(Uri Url, string BodyMediaType);

/// <summary>
/// The answer to a fetch: the media type the server declared, its charset, and the body only
/// when the media type is the one asked for (DA-058: HTML only for text/html, headers only for
/// a pdf or an image).
/// </summary>
internal sealed record FetchedResponse(string? MediaType, string? Charset, byte[]? Body);

/// <summary>Gets a Resource's answer; replaced in tests, which have no network (GEN-072).</summary>
internal interface IResourceFetcher
{
    /// <returns><c>null</c> when the fetch failed, was refused or timed out; never an exception for those.</returns>
    Task<FetchedResponse?> FetchAsync(ResourceRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// Fetches through the SSRF guard (DA-058, DA-156): http or https, public addresses only, the DNS
/// vetted and the address pinned on every connection, at most <see cref="MaxRedirects"/>
/// redirects with each hop checked again, <see cref="Timeout"/> for the whole fetch and at
/// most <see cref="MaxBytes"/> of body.
/// </summary>
internal sealed class HttpResourceFetcher : IResourceFetcher, IDisposable
{
    public const int MaxRedirects = 3;

    public const int MaxBytes = 512 * 1024;

    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private const string UserAgent = "LifeGraph-MetadataFetch/1.0";

    private readonly HttpClient _client;

    public HttpResourceFetcher()
        : this(SafeOutboundHttp.CreateHandler(Timeout))
    {
    }

    /// <param name="handler">The guarded handler; tests pass one that answers without network.</param>
    internal HttpResourceFetcher(HttpMessageHandler handler)
    {
        _client = new HttpClient(handler) { Timeout = System.Threading.Timeout.InfiniteTimeSpan };
    }

    public async Task<FetchedResponse?> FetchAsync(ResourceRequest request, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);
        try
        {
            var url = request.Url;
            for (var redirects = 0; ; redirects++)
            {
                if (!SafeOutboundHttp.IsFetchableResourceUrl(url))
                {
                    return null;
                }

                using var message = new HttpRequestMessage(HttpMethod.Get, url);
                message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(request.BodyMediaType));
                message.Headers.UserAgent.ParseAdd(UserAgent);
                using var response = await _client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
                if (IsRedirect(response.StatusCode))
                {
                    if (redirects == MaxRedirects || response.Headers.Location is not { } location)
                    {
                        return null;
                    }

                    url = location.IsAbsoluteUri ? location : new Uri(url, location);
                    continue;
                }

                return await ReadAsync(response, request.BodyMediaType, timeout.Token);
            }
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    public void Dispose() => _client.Dispose();

    private static async Task<FetchedResponse?> ReadAsync(HttpResponseMessage response, string bodyMediaType, CancellationToken cancellationToken)
    {
        if (response.StatusCode != HttpStatusCode.OK)
        {
            return null;
        }

        var contentType = response.Content.Headers.ContentType;
        var mediaType = contentType?.MediaType?.ToLowerInvariant();
        if (mediaType != bodyMediaType)
        {
            return new FetchedResponse(mediaType, contentType?.CharSet, Body: null);
        }

        var body = await SafeOutboundHttp.ReadCappedAsync(response.Content, MaxBytes, cancellationToken);
        return body is null ? null : new FetchedResponse(mediaType, contentType?.CharSet, body);
    }

    private static bool IsRedirect(HttpStatusCode status) => status is HttpStatusCode.MovedPermanently
        or HttpStatusCode.Found
        or HttpStatusCode.SeeOther
        or HttpStatusCode.TemporaryRedirect
        or HttpStatusCode.PermanentRedirect;
}
