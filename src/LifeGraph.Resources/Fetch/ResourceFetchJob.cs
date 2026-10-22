using System.Globalization;
using System.Text.Json;
using LifeGraph.Graph.Contracts;
using LifeGraph.Infrastructure.Jobs;
using LifeGraph.Resources.Domain;
using LifeGraph.Resources.Extraction;
using Microsoft.Extensions.Logging;
using Keys = LifeGraph.Graph.Contracts.SystemOntology.ResourcePropertyKeys;

namespace LifeGraph.Resources.Fetch;

/// <summary>
/// Fetches a Resource's metadata (DA-058) in the two phases of DA-152. The I/O phase reads the
/// Resource's <c>url</c> in a short transaction and fetches it with no transaction open. The
/// apply phase, in the job's transaction, reads the Resource again and writes, as the system
/// (DA-153), the <c>fetch_status</c> and every field the person has not changed (DA-150,
/// DA-151). A failed fetch is <c>fetch_status=failed</c>, never a failed job (DA-059). A
/// Resource gone, or whose <c>url</c> changed meanwhile (its new URL has its own job), gets
/// nothing. Running it again writes the same values, so it changes nothing (BE-034).
/// </summary>
internal sealed partial class ResourceFetchJob(
    IResourceFetchGraph graph,
    IResourceFetcher fetcher,
    ILogger<ResourceFetchJob> logger) : ITwoPhaseJobHandler
{
    private const string HtmlMediaType = "text/html";

    public string Kind => IResourceFetchGraph.JobKind;

    public async Task<JobApplyPhase> RunOutsideTransactionAsync(string payload, CancellationToken cancellationToken)
    {
        var nodeId = ResourceFetchPayload.Parse(payload).NodeId;
        var resource = await graph.FindAsync(nodeId, cancellationToken);
        if (resource?.Url is not { } url)
        {
            return _ => Task.CompletedTask;
        }

        var outcome = await FetchAsync(url, cancellationToken);
        return token => ApplyAsync(nodeId, url, outcome, token);
    }

    private async Task<FetchOutcome> FetchAsync(string url, CancellationToken cancellationToken)
    {
        // A URL with credentials is never fetched (DA-058); capture refuses it already (DA-150).
        if (ResourceUrl.Parse(url).Url is not { } resourceUrl)
        {
            return FetchOutcome.Failed;
        }

        if (YouTubeAddress.VideoIdOf(resourceUrl.Address) is { } videoId)
        {
            var answer = await fetcher.FetchAsync(
                new ResourceRequest(YouTubeOEmbedExtractor.RequestUrlFor(videoId), YouTubeOEmbedExtractor.MediaType),
                cancellationToken);
            return answer?.Body is { } json && YouTubeOEmbedExtractor.Extract(json) is { } video
                ? new FetchOutcome(video, YouTubeAddress.Provider)
                : FetchOutcome.Failed;
        }

        var page = await fetcher.FetchAsync(new ResourceRequest(resourceUrl.Address, HtmlMediaType), cancellationToken);
        if (page is null)
        {
            return FetchOutcome.Failed;
        }

        if (page.Body is not { } html)
        {
            // Only the headers of a pdf, an image or a document (DA-058).
            return new FetchOutcome(new ExtractedMetadata(MediaTypeKinds.KindOf(page.MediaType) ?? resourceUrl.Kind), resourceUrl.Provider);
        }

        var metadata = HtmlMetadataExtractor.Extract(html, page.Charset);

        // A provider's video page is still a video, whatever its HTML says.
        return new FetchOutcome(
            resourceUrl.Kind == ResourceKind.Video ? metadata with { Kind = ResourceKind.Video } : metadata,
            resourceUrl.Provider);
    }

    private async Task ApplyAsync(Guid nodeId, string url, FetchOutcome outcome, CancellationToken cancellationToken)
    {
        var resource = await graph.FindAsync(nodeId, cancellationToken);
        if (resource is null || resource.Url != url)
        {
            return;
        }

        var untouched = (string field) => !resource.HumanEditedFields.Contains(field);
        var values = new Dictionary<Guid, JsonElement>();
        void Fill(string key, string? value)
        {
            if (value is not null && untouched(key) && resource.PropertyIds.TryGetValue(key, out var propertyId))
            {
                values[propertyId] = JsonSerializer.SerializeToElement(value);
            }
        }

        var metadata = outcome.Metadata;
        Fill(Keys.FetchStatus, metadata is null ? FetchStatus.Failed : FetchStatus.Fetched);
        Fill(Keys.ResourceKind, metadata?.Kind.ToWireName());
        Fill(Keys.Provider, metadata is null ? null : outcome.Provider);
        Fill(Keys.SiteName, metadata?.SiteName);
        Fill(Keys.Author, metadata?.Author);
        Fill(Keys.Description, metadata?.Description);
        Fill(Keys.PublishedAt, metadata?.PublishedAt?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        var update = new UpdateNode(nodeId, resource.Version)
        {
            Title = untouched(ResourceToFetch.TitleField) ? metadata?.Title : null,
            Body = untouched(ResourceToFetch.BodyField) ? metadata?.Description : null,
            Properties = values,
        };
        var written = await graph.WriteAsSystemAsync([update], cancellationToken);
        if (!written.IsSuccess)
        {
            // What the graph refused is not retried: the next fetch of this URL would be refused too.
            LogWriteRefused(logger, nodeId, written.Error!.Code);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The fetched metadata of Resource {NodeId} was refused by the graph ({ErrorCode})")]
    private static partial void LogWriteRefused(ILogger logger, Guid nodeId, string errorCode);

    /// <param name="Metadata">What the fetch learned; <c>null</c> when it failed.</param>
    private sealed record FetchOutcome(ExtractedMetadata? Metadata, string? Provider)
    {
        public static readonly FetchOutcome Failed = new(null, null);
    }
}
