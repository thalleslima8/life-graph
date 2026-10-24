using LifeGraph.Graph.Contracts;
using LifeGraph.Infrastructure.Accounts;
using LifeGraph.Infrastructure.Jobs;
using LifeGraph.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LifeGraph.Semantic.Embedding;

/// <summary>
/// The safety net of the reindex at sign-in (DA-181): an Account with a live Node the configured
/// model never embedded, and no embedding or reindex job waiting, gets a reindex. The migration
/// that swaps a model schedules one per Account; this catches an Account that missed it, such as
/// one whose embedding jobs ended dead. Idempotent, like every setup participant (DA-149).
/// </summary>
internal sealed class SemanticReindexSetup(
    ISemanticGraph graph,
    IEmbedder embedder,
    LifeGraphDbContext db,
    TimeProvider clock) : IAccountSetupParticipant
{
    public async Task EnsureAsync(Guid accountId, CancellationToken cancellationToken)
    {
        if (embedder.ModelId == UnavailableEmbedder.NoModelId)
        {
            return;
        }

        var waiting = await db.Set<Job>().AnyAsync(SemanticIndexing.InProgress, cancellationToken);
        if (waiting)
        {
            return;
        }

        var missing = await graph.FindNodesWithoutEmbeddingAsync(embedder.ModelId, afterNodeId: null, limit: 1, cancellationToken);
        if (missing.Count == 0)
        {
            return;
        }

        var now = clock.GetUtcNow();
        db.Add(Job.Schedule(accountId, SemanticReindexJob.JobKind, new SemanticReindexPayload(null).ToJson(), now, now));
        await db.SaveChangesAsync(cancellationToken);
    }
}
