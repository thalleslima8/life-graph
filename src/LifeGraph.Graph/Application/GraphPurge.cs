using System.Text.Json;
using System.Text.Json.Serialization;
using LifeGraph.Graph.Contracts;
using LifeGraph.Graph.Domain;
using LifeGraph.Infrastructure.Jobs;
using LifeGraph.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LifeGraph.Graph.Application;

/// <summary>
/// Purge at the end of the delete window (DA-021): the tombstone goes, and every change
/// entry that recorded it loses its content, keeping only the skeleton. Every Node or
/// Relation a GraphChangeSet tombstones gets its own job, stored with the GraphChangeSet;
/// deleting again later makes a new job with a new deadline (DA-113, DA-115).
/// </summary>
internal sealed class GraphPurge(LifeGraphDbContext db, TimeProvider clock) : IJobHandler
{
    public const string JobKind = "graph.purge";

    private static readonly JsonSerializerOptions PayloadOptions = new(JsonSerializerOptions.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
    };

    public string Kind => JobKind;

    /// <summary>Schedules the purge of every Node and Relation the GraphChangeSet tombstones.</summary>
    public static void ScheduleFor(LifeGraphDbContext db, GraphChangeSet changeSet, DateTimeOffset now)
    {
        foreach (var entry in changeSet.Entries.Where(entry => entry.Operation == GraphChangeOperation.Deleted))
        {
            var deletedAt = entry.EntityKind switch
            {
                GraphEntityKind.Node => ChangeSnapshots.ReadNode(entry.After!).DeletedAt,
                GraphEntityKind.Relation => ChangeSnapshots.ReadRelation(entry.After!).DeletedAt,
                _ => null,
            };
            if (deletedAt is not { } tombstonedAt)
            {
                continue;
            }

            var payload = JsonSerializer.Serialize(new PurgePayload(entry.EntityKind, entry.EntityId, tombstonedAt), PayloadOptions);
            db.Add(Job.Schedule(changeSet.AccountId, JobKind, payload, tombstonedAt + GraphRetention.DeleteWindow, now));
        }
    }

    // It checks again, now, that the entity is still the tombstone the job was made for:
    // a restore or a newer delete since then makes this job a no-op (DA-113).
    public async Task RunAsync(string payload, CancellationToken cancellationToken)
    {
        var purge = JsonSerializer.Deserialize<PurgePayload>(payload, PayloadOptions)
            ?? throw new JsonException("Empty purge payload.");
        var now = clock.GetUtcNow();

        var purgedIds = purge.EntityKind switch
        {
            GraphEntityKind.Node => await RemoveNodeAsync(purge, cancellationToken),
            GraphEntityKind.Relation => await RemoveRelationAsync(purge, cancellationToken),
            _ => throw new NotSupportedException($"Only Nodes and Relations are purged, not {purge.EntityKind}."),
        };
        if (purgedIds.Count > 0)
        {
            await EmptyHistoryAsync(purgedIds, now, cancellationToken);
        }
    }

    // A Node takes along the Relations that touch it; they are all tombstones by then, since
    // a Relation only lives between live Nodes.
    private async Task<IReadOnlyList<Guid>> RemoveNodeAsync(PurgePayload purge, CancellationToken cancellationToken)
    {
        var node = await db.Set<Node>().SingleOrDefaultAsync(entity => entity.Id == purge.EntityId, cancellationToken);
        if (node is null || node.DeletedAt != purge.DeletedAt)
        {
            return [];
        }

        var relations = await db.Set<Relation>()
            .Where(relation => relation.SourceNodeId == node.Id || relation.TargetNodeId == node.Id)
            .ToListAsync(cancellationToken);
        if (relations.Any(relation => !relation.IsDeleted))
        {
            throw new InvalidOperationException("A live Relation touches a Node past its delete window.");
        }

        db.RemoveRange(relations);
        db.Remove(node);
        return [node.Id, .. relations.Select(relation => relation.Id)];
    }

    private async Task<IReadOnlyList<Guid>> RemoveRelationAsync(PurgePayload purge, CancellationToken cancellationToken)
    {
        var relation = await db.Set<Relation>().SingleOrDefaultAsync(entity => entity.Id == purge.EntityId, cancellationToken);
        if (relation is null || relation.DeletedAt != purge.DeletedAt)
        {
            return [];
        }

        db.Remove(relation);
        return [relation.Id];
    }

    // Every entry that recorded the purged entities, in any GraphChangeSet, Undo ones
    // included; a GraphChangeSet left with nothing but skeletons forgets its source (DA-114).
    private async Task EmptyHistoryAsync(IReadOnlyList<Guid> entityIds, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var entries = await db.Set<ChangeEntry>().Where(entry => entityIds.Contains(entry.EntityId)).ToListAsync(cancellationToken);
        foreach (var entry in entries)
        {
            entry.Purge(now);
        }

        var changeSetIds = entries.Select(entry => entry.ChangeSetId).Distinct().ToList();
        var changeSets = await db.Set<GraphChangeSet>()
            .Include(changeSet => changeSet.Entries)
            .Where(changeSet => changeSetIds.Contains(changeSet.Id))
            .ToListAsync(cancellationToken);
        foreach (var changeSet in changeSets)
        {
            changeSet.ForgetSourceIfFullyPurged();
        }
    }

    private sealed record PurgePayload(GraphEntityKind EntityKind, Guid EntityId, DateTimeOffset DeletedAt);
}
