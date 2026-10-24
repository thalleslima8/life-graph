using System.Globalization;
using LifeGraph.Graph.Contracts;
using LifeGraph.Infrastructure.Jobs;
using LifeGraph.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LifeGraph.Graph.Application;

/// <summary>
/// The search by words and by meaning (DA-183), the same for the person's REST search and an
/// agent's <c>search_graph</c>, behind the central read filter (an agent never meets a Node
/// Oculto para agentes by either way, DA-035). The query is embedded before the read's
/// transaction (<see cref="EmbedAsync"/>); the read itself (<see cref="SearchAsync"/>) falls back
/// to words alone, and says so, when there is no embedding or the Account is being indexed.
/// </summary>
internal sealed class HybridSearch(LifeGraphDbContext db, IQueryEmbedder queryEmbedder)
{
    // pgvector >= 0.8: lets the HNSW scan go on past the rows a filter drops, for this transaction.
    private const string IterativeHnswScanSql = "SET LOCAL hnsw.iterative_scan = relaxed_order";

    /// <summary>How many of the best Nodes by words, and of the nearest by meaning, are fused.</summary>
    public const int CandidatesPerList = 50;

    /// <summary>The constant of Reciprocal Rank Fusion: 1 / (60 + rank), the value of its original paper.</summary>
    public const int FusionRankOffset = 60;

    /// <summary>The query's embedding, or <c>null</c>; called outside any transaction.</summary>
    public Task<QueryEmbedding?> EmbedAsync(string text, CancellationToken cancellationToken) =>
        queryEmbedder.EmbedQueryAsync(text, cancellationToken);

    /// <summary>The hits of the search, in the open transaction of the Account.</summary>
    public async Task<HybridSearchOutcome> SearchAsync(
        GraphReader reader,
        NodeSearch search,
        QueryEmbedding? embedding,
        CancellationToken cancellationToken)
    {
        var byMeaning = embedding is not null && !await IndexingAsync(cancellationToken) ? embedding : null;
        if (byMeaning is not null)
        {
            // pgvector >= 0.8: the HNSW scan goes on past the Nodes the filter drops, so a
            // filtered search still finds the nearest it may show.
            await db.Database.ExecuteSqlRawAsync(IterativeHnswScanSql, cancellationToken);
        }

        var hits = await ContextWalk.HybridSearchAsync(db, reader, search, byMeaning, cancellationToken);
        return new HybridSearchOutcome(hits, Degraded: byMeaning is null);
    }

    /// <summary>The vector as pgvector reads it in text: <c>[0.1,-0.2,…]</c>.</summary>
    public static string VectorLiteral(ReadOnlyMemory<float> vector)
    {
        var components = new string[vector.Length];
        var span = vector.Span;
        for (var index = 0; index < span.Length; index++)
        {
            components[index] = span[index].ToString("R", CultureInfo.InvariantCulture);
        }

        return $"[{string.Join(',', components)}]";
    }

    // While indexing runs, the embeddings of the Account trail its Nodes (DA-181: backfill,
    // model swap, a Node just written), so the search finds by words only.
    private Task<bool> IndexingAsync(CancellationToken cancellationToken) =>
        db.Set<Job>().AnyAsync(SemanticIndexing.InProgress, cancellationToken);
}

/// <summary>The hits, best first, and whether the search found by words only.</summary>
internal sealed record HybridSearchOutcome(IReadOnlyList<HybridHit> Hits, bool Degraded);
