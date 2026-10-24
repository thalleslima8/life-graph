using System.Text.Json;
using LifeGraph.Graph.Contracts;
using LifeGraph.Infrastructure.Jobs;
using LifeGraph.Infrastructure.Persistence;
using Microsoft.Extensions.Options;

namespace LifeGraph.Semantic.Embedding;

/// <summary>
/// Embeds every Node of an Account the configured model has not embedded yet (DA-181): the first
/// backfill and every model swap. One run, in the job's transaction, takes one page of Nodes in
/// id order, schedules an embedding job for each and, when the page was full, the next page as a
/// job of its own, so a big Account is a chain of short transactions that resumes where it
/// stopped (DB-014). The last page deletes the embeddings by other models. Running it twice
/// schedules embedding jobs twice at most, and each finds its text already embedded (BE-034).
/// </summary>
internal sealed class SemanticReindexJob(
    ISemanticGraph graph,
    IEmbedder embedder,
    LifeGraphDbContext db,
    IAccountContext accountContext,
    IOptions<EmbeddingOptions> options,
    TimeProvider clock) : IJobHandler
{
    public const string JobKind = ISemanticGraph.ReindexJobKind;

    public string Kind => JobKind;

    public async Task RunAsync(string payload, CancellationToken cancellationToken)
    {
        if (embedder.ModelId == UnavailableEmbedder.NoModelId)
        {
            return;
        }

        var accountId = accountContext.AccountId
            ?? throw new InvalidOperationException("A reindex runs only for an Account (DA-113).");
        var after = SemanticReindexPayload.Parse(payload).AfterNodeId;
        var pageSize = options.Value.ReindexPageSize;
        var page = await graph.FindNodesWithoutEmbeddingAsync(embedder.ModelId, after, pageSize, cancellationToken);
        var now = clock.GetUtcNow();
        foreach (var nodeId in page)
        {
            db.Add(Job.Schedule(accountId, ISemanticGraph.EmbedJobKind, new NodeEmbeddingPayload(nodeId).ToJson(), now, now));
        }

        if (page.Count == pageSize)
        {
            db.Add(Job.Schedule(accountId, JobKind, new SemanticReindexPayload(page[^1]).ToJson(), now, now));
            return;
        }

        await graph.DeleteEmbeddingsByOtherModelsAsync(embedder.ModelId, cancellationToken);
    }
}

/// <summary>Where a reindex goes on: after this Node id, or from the start. Ids only (GEN-043).</summary>
internal sealed record SemanticReindexPayload(Guid? AfterNodeId)
{
    public string ToJson() => JsonSerializer.Serialize(this, JsonSerializerOptions.Web);

    public static SemanticReindexPayload Parse(string json) =>
        JsonSerializer.Deserialize<SemanticReindexPayload>(json, JsonSerializerOptions.Web)
        ?? throw new JsonException("Empty reindex payload.");
}
