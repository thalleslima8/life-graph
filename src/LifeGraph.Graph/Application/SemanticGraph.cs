using System.Text.Json;
using LifeGraph.Graph.Contracts;
using LifeGraph.Graph.Domain;
using LifeGraph.Graph.Persistence;
using LifeGraph.Infrastructure.Jobs;
using LifeGraph.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace LifeGraph.Graph.Application;

/// <summary>The Semantic module's entry of the Graph (DA-069, DA-070, DA-176).</summary>
internal sealed class SemanticGraph(
    LifeGraphDbContext db,
    IAccountContext accountContext,
    TimeProvider clock) : ISemanticGraph
{
    public Task<NodeToEmbed?> FindNodeToEmbedAsync(Guid nodeId, string modelId, CancellationToken cancellationToken) =>
        db.Database.CurrentTransaction is null
            ? db.InAccountTransactionAsync(token => ReadAsync(nodeId, modelId, token), cancellationToken)
            : ReadAsync(nodeId, modelId, cancellationToken);

    public async Task SaveEmbeddingAsync(NodeEmbeddingToSave embedding, CancellationToken cancellationToken)
    {
        if (db.Database.CurrentTransaction is null || accountContext.AccountId is not { } accountId)
        {
            throw new InvalidOperationException("An embedding is stored only inside the job's transaction of an Account (DA-152).");
        }

        var now = clock.GetUtcNow();
        var stored = await db.Set<NodeEmbedding>().SingleOrDefaultAsync(
            row => row.NodeId == embedding.NodeId && row.ModelId == embedding.ModelId,
            cancellationToken);
        if (stored is null)
        {
            db.Add(NodeEmbedding.Of(accountId, embedding.NodeId, embedding.ModelId, embedding.ContentHash, embedding.Embedding, now));
        }
        else
        {
            stored.Replace(embedding.ContentHash, embedding.Embedding, now);
        }
    }

    public async Task<IReadOnlyList<Guid>> FindNodesWithoutEmbeddingAsync(string modelId, Guid? afterNodeId, int limit, CancellationToken cancellationToken)
    {
        RequireAccountTransaction();
        var embedded = db.Set<NodeEmbedding>().Where(row => row.ModelId == modelId).Select(row => row.NodeId);
        return await db.Set<Node>().AsNoTracking()
            .Where(node => node.DeletedAt == null && (afterNodeId == null || node.Id.CompareTo(afterNodeId.Value) > 0))
            .Where(Node.IsNotAccountRootPredicate)
            .Where(node => !embedded.Contains(node.Id))
            .OrderBy(node => node.Id)
            .Select(node => node.Id)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public Task<int> DeleteEmbeddingsByOtherModelsAsync(string modelId, CancellationToken cancellationToken)
    {
        RequireAccountTransaction();
        return db.Set<NodeEmbedding>().Where(row => row.ModelId != modelId).ExecuteDeleteAsync(cancellationToken);
    }

    public void ScheduleSoftRelations(Guid nodeId)
    {
        var accountId = RequireAccountTransaction();
        var now = clock.GetUtcNow();
        db.Add(Job.Schedule(accountId, ISemanticGraph.SoftRelationsJobKind, new NodeEmbeddingPayload(nodeId).ToJson(), now, now));
    }

    public async Task<IReadOnlyList<SoftRelationCandidate>?> FindSoftRelationCandidatesAsync(SoftRelationCandidateSearch search, CancellationToken cancellationToken)
    {
        RequireAccountTransaction();
        var (nodeId, modelId, minimumScore, topK, poolSize) = search;
        var embedded = await db.Set<NodeEmbedding>()
            .AnyAsync(row => row.NodeId == nodeId && row.ModelId == modelId
                && db.Set<Node>().Any(node => node.Id == nodeId && node.DeletedAt == null), cancellationToken);
        if (!embedded)
        {
            return null;
        }

        return await SqlRows.QueryAsync(
            db,
            SoftRelationCandidatesSql,
            [
                new NpgsqlParameter("node_id", NpgsqlDbType.Uuid) { Value = nodeId },
                new NpgsqlParameter("model_id", NpgsqlDbType.Text) { Value = modelId },
                new NpgsqlParameter("max_distance", NpgsqlDbType.Double) { Value = 1.0 - minimumScore },
                new NpgsqlParameter("top_k", NpgsqlDbType.Integer) { Value = topK },
                new NpgsqlParameter("pool_size", NpgsqlDbType.Integer) { Value = poolSize },
            ],
            reader => new SoftRelationCandidate(
                reader.GetFieldValue<Guid>(0),
                Math.Clamp(1.0 - reader.GetFieldValue<double>(1), 0.0, 1.0),
                reader.GetFieldValue<bool>(2)),
            cancellationToken);
    }

    public async Task ReplaceSoftRelationsAsync(Guid nodeId, string modelId, IReadOnlyList<SoftRelationToKeep> pairs, CancellationToken cancellationToken)
    {
        var accountId = RequireAccountTransaction();
        var now = clock.GetUtcNow();
        var stored = await RelationReadGate.SoftRelations(db)
            .Where(relation => relation.SourceNodeId == nodeId || relation.TargetNodeId == nodeId)
            .ToListAsync(cancellationToken);
        var kept = pairs.ToDictionary(pair => pair.OtherNodeId, pair => pair.Score);
        foreach (var relation in stored)
        {
            var otherNodeId = relation.SourceNodeId == nodeId ? relation.TargetNodeId : relation.SourceNodeId;
            if (kept.Remove(otherNodeId, out var score))
            {
                relation.Rescore(modelId, score, now);
            }
            else
            {
                db.Remove(relation);
            }
        }

        foreach (var (otherNodeId, score) in kept)
        {
            db.Add(Relation.InferSoft(Guid.CreateVersion7(), accountId, nodeId, otherNodeId, modelId, score, now));
        }
    }

    private Guid RequireAccountTransaction()
    {
        if (db.Database.CurrentTransaction is null || accountContext.AccountId is null)
        {
            throw new InvalidOperationException("The embeddings and Soft Relations are read and written only inside a transaction of an Account (DA-152, DA-181).");
        }

        return accountContext.AccountId.Value;
    }

    // Whether {a} and {b} may be a Soft pair: {b} live, no live Hard Relation between them
    // either way (a Hard one already says it), and the pair not Ignored (DA-071).
    private static string PairableSql(string a, string b) => $"""
        (EXISTS (SELECT 1 FROM nodes pn WHERE pn.id = {b} AND pn.deleted_at IS NULL)
         AND NOT EXISTS (
             SELECT 1 FROM {RelationReadGate.HardRelationsSql} hr
             WHERE hr.deleted_at IS NULL
               AND ((hr.source_node_id = {a} AND hr.target_node_id = {b}) OR (hr.source_node_id = {b} AND hr.target_node_id = {a})))
         AND NOT EXISTS (
             SELECT 1 FROM soft_dismissals d
             WHERE d.first_node_id = LEAST({a}, {b}) AND d.second_node_id = GREATEST({a}, {b})))
        """;

    // The pool by cosine distance (the HNSW index), then, for each one, whether the Node is
    // among its own top-k by the same rules. Ties go by id, so the answer is deterministic.
    private static readonly string SoftRelationCandidatesSql = $"""
        WITH self AS (
            SELECT e.embedding FROM node_embeddings e WHERE e.node_id = @node_id AND e.model_id = @model_id
        ),
        pool AS (
            SELECT c.node_id, c.embedding, c.embedding <=> self.embedding AS distance
            FROM node_embeddings c CROSS JOIN self
            WHERE c.model_id = @model_id
              AND c.node_id <> @node_id
              AND c.embedding <=> self.embedding <= @max_distance
              AND {PairableSql("@node_id", "c.node_id")}
            ORDER BY c.embedding <=> self.embedding, c.node_id
            LIMIT @pool_size
        )
        SELECT p.node_id, p.distance,
               @node_id IN (
                   SELECT o.node_id FROM node_embeddings o
                   WHERE o.model_id = @model_id
                     AND o.node_id <> p.node_id
                     AND o.embedding <=> p.embedding <= @max_distance
                     AND {PairableSql("p.node_id", "o.node_id")}
                   ORDER BY o.embedding <=> p.embedding, o.node_id
                   LIMIT @top_k) AS ranks_the_node
        FROM pool p
        ORDER BY p.distance, p.node_id
        """;

    private async Task<NodeToEmbed?> ReadAsync(Guid nodeId, string modelId, CancellationToken cancellationToken)
    {
        var node = await db.Set<Node>().AsNoTracking()
            .SingleOrDefaultAsync(entity => entity.Id == nodeId && entity.DeletedAt == null, cancellationToken);

        // The Raiz da Conta stays outside the semantic layer (DA-189): no embedding, so it is
        // never a Soft Relation's end nor a semantic hit.
        if (node is null || node.IsAccountRoot)
        {
            return null;
        }

        var propertyIds = node.Properties.Values.Keys.ToList();
        var textPropertyIds = await db.Set<PropertyDefinition>().AsNoTracking()
            .Where(definition => propertyIds.Contains(definition.Id) && definition.ValueKind == PropertyValueKind.Text)
            .Select(definition => definition.Id)
            .ToListAsync(cancellationToken);
        var textValues = textPropertyIds
            .Order()
            .Select(id => node.Properties.Values[id])
            .Where(value => value.ValueKind == JsonValueKind.String)
            .Select(value => value.GetString()!)
            .ToList();
        var storedContentHash = await db.Set<NodeEmbedding>().AsNoTracking()
            .Where(row => row.NodeId == nodeId && row.ModelId == modelId)
            .Select(row => row.ContentHash)
            .SingleOrDefaultAsync(cancellationToken);

        return new NodeToEmbed(node.Id, node.Title, node.Body, textValues, node.HiddenFromAgents, storedContentHash);
    }
}

/// <summary>
/// Schedules the embedding of a Node (DA-069) in the transaction of the write that created it,
/// restored it, changed its text or showed it again to agents (a third-party model skips a
/// hidden Node, DA-072). Every actor counts, the system's fetch and an Undo
/// included, since each may change the text. Only the Node's id goes in the job; the job
/// compares the text's hash before calling the model, so a change that left the text as it was
/// costs no embedding (BE-034). The Raiz da Conta is never embedded (DA-189); the write tracks it
/// whenever it changes it.
/// </summary>
internal static class EmbeddingScheduling
{
    public static void ScheduleFor(LifeGraphDbContext db, GraphChangeSet changeSet, DateTimeOffset now)
    {
        var rootIds = db.ChangeTracker.Entries<Node>()
            .Where(entry => entry.Entity.IsAccountRoot)
            .Select(entry => entry.Entity.Id)
            .ToHashSet();
        var nodeIds = changeSet.Entries
            .Where(entry => entry.EntityKind == GraphEntityKind.Node && !rootIds.Contains(entry.EntityId) && ChangesText(entry))
            .Select(entry => entry.EntityId)
            .Distinct();
        foreach (var nodeId in nodeIds)
        {
            db.Add(Job.Schedule(changeSet.AccountId, ISemanticGraph.EmbedJobKind, new NodeEmbeddingPayload(nodeId).ToJson(), now, now));
        }
    }

    private static bool ChangesText(ChangeEntry entry)
    {
        switch (entry.Operation)
        {
            case GraphChangeOperation.Created or GraphChangeOperation.Restored:
                return true;
            case GraphChangeOperation.Updated:
                var before = ChangeSnapshots.ReadNode(entry.Before!);
                var after = ChangeSnapshots.ReadNode(entry.After!);
                return before.Title != after.Title
                    || before.Body != after.Body
                    || !StringValues(before).SequenceEqual(StringValues(after))
                    || (before.HiddenFromAgents && !after.HiddenFromAgents);
            default:
                return false;
        }
    }

    // Only text is embedded, and every text value is a JSON string; which of them are text
    // Properties the job tells apart.
    private static IEnumerable<KeyValuePair<Guid, string?>> StringValues(NodeSnapshot snapshot) =>
        snapshot.Properties
            .Where(pair => pair.Value.ValueKind == JsonValueKind.String)
            .OrderBy(pair => pair.Key)
            .Select(pair => KeyValuePair.Create(pair.Key, pair.Value.GetString()));
}

/// <summary>
/// Schedules the Soft Relations of both Nodes of a pair the person makes free again, inside the
/// write's transaction: a Relation taken away (an Undo of Accept among them) or an Ignore taken
/// back. The projection then decides whether the pair is a Soft Relation again (DA-070, DA-071).
/// A Relation that went with its Node needs nothing: that Node is gone from the projection.
/// </summary>
internal static class SoftRelationScheduling
{
    public static void ScheduleFor(LifeGraphDbContext db, GraphChangeSet changeSet, DateTimeOffset now)
    {
        var nodeIds = changeSet.Entries
            .Where(entry => entry is { Operation: GraphChangeOperation.Deleted, IsCascade: false, Before: not null })
            .SelectMany(FreedPairOf)
            .Distinct();
        foreach (var nodeId in nodeIds)
        {
            db.Add(Job.Schedule(changeSet.AccountId, ISemanticGraph.SoftRelationsJobKind, new NodeEmbeddingPayload(nodeId).ToJson(), now, now));
        }
    }

    private static IEnumerable<Guid> FreedPairOf(ChangeEntry entry)
    {
        switch (entry.EntityKind)
        {
            case GraphEntityKind.Relation:
                var relation = ChangeSnapshots.ReadRelation(entry.Before!);
                return [relation.SourceNodeId, relation.TargetNodeId];
            case GraphEntityKind.SoftDismissal:
                var dismissal = ChangeSnapshots.ReadSoftDismissal(entry.Before!);
                return [dismissal.FirstNodeId, dismissal.SecondNodeId];
            default:
                return [];
        }
    }
}
