using System.Net;
using System.Net.Http.Headers;
using LifeGraph.Infrastructure.Outbound;

namespace LifeGraph.Accounts.ClientMetadata;

/// <summary>A fetched metadata document and how long it may be reused.</summary>
internal sealed record FetchedDocument(byte[] Content, TimeSpan? MaxAge);

/// <summary>Gets a client's metadata document; replaced in tests, which have no network.</summary>
internal interface IClientMetadataDocumentFetcher
{
    /// <returns><c>null</c> when the document could not be fetched as a small JSON answer.</returns>
    Task<FetchedDocument?> FetchAsync(Uri url, CancellationToken cancellationToken);
}

/// <summary>
/// Fetches through the SSRF guard (DA-029): HTTPS only, public addresses only, no redirects,
/// a short timeout and a size cap.
/// </summary>
internal sealed class HttpClientMetadataDocumentFetcher : IClientMetadataDocumentFetcher, IDisposable
{
    /// <summary>The TCP connection to the client's host, including the address checks.</summary>
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(5);

    /// <summary>The whole fetch: an agent's consent waits on it.</summary>
    private static readonly TimeSpan FetchTimeout = TimeSpan.FromSeconds(10);

    private readonly HttpClient _client = new(SafeOutboundHttp.CreateHandler(ConnectTimeout))
    {
        Timeout = FetchTimeout,
        MaxResponseContentBufferSize = ClientMetadataDocument.MaxBytes,
    };

    public async Task<FetchedDocument?> FetchAsync(Uri url, CancellationToken cancellationToken)
    {
        if (!SafeOutboundHttp.IsFetchableUrl(url))
        {
            return null;
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        try
        {
            using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            var mediaType = response.Content.Headers.ContentType?.MediaType;
            if (response.StatusCode != HttpStatusCode.OK
                || mediaType is null
                || !(mediaType == "application/json" || mediaType.EndsWith("+json", StringComparison.Ordinal)))
            {
                return null;
            }

            var content = await SafeOutboundHttp.ReadCappedAsync(response.Content, ClientMetadataDocument.MaxBytes, cancellationToken);
            return content is null ? null : new FetchedDocument(content, response.Headers.CacheControl?.MaxAge);
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    public void Dispose() => _client.Dispose();
}
