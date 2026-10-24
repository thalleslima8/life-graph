using LifeGraph.Graph.Contracts;
using LifeGraph.Graph.Domain;
using LifeGraph.Infrastructure.Persistence;
using Limaj.Framework.Core;
using Microsoft.EntityFrameworkCore;

namespace LifeGraph.Graph.Application;

/// <summary>
/// Restores one deleted Node or Relation with a compensating GraphChangeSet of its own
/// (DA-115), apart from the GraphChangeSet that deleted it: that one may hold other edits
/// (an approved agent proposal, a bulk action), so undoing it whole is no restore. The conflict
/// check covers only the entity and, for a Node, the Relations that went with it
/// (<c>cascade_of</c>): a cascade whose other Node is no longer live stays deleted and the
/// receipt says so, as in an Undo. Past the delete window the answer is
/// <c>graph.undo_window_closed</c>; after Purge, and for another Account, the entity is not found.
/// The history it restores from is read through the central read filter, which shows it to the
/// person only.
/// </summary>
internal sealed class EntityRestore
{
    public const string ConflictMessage = "The deleted entity changed since it was deleted. Nothing was restored.";

    private readonly LifeGraphDbContext _db;
    private readonly GraphReadFilter _filter;
    private readonly EntityToRestore _restore;

    private EntityRestore(LifeGraphDbContext db, GraphReadFilter filter, EntityToRestore restore)
    {
        _db = db;
        _filter = filter;
        _restore = restore;
    }

    private DateTimeOffset Now => _restore.Now;

    public static Task<Result<GraphWriteReceipt>> RunAsync(
        LifeGraphDbContext db,
        GraphReadFilter filter,
        EntityToRestore restore,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(restore);
        var run = new EntityRestore(db, filter, restore);
        return restore.Kind switch
        {
            GraphEntityKind.Node => run.RestoreNodeAsync(restore.EntityId, cancellationToken),
            GraphEntityKind.Relation => run.RestoreRelationAsync(restore.EntityId, cancellationToken),
            _ => throw new NotSupportedException($"Only Nodes and Relations are restored, not {restore.Kind}."),
        };
    }

    private async Task<Result<GraphWriteReceipt>> RestoreNodeAsync(Guid nodeId, CancellationToken cancellationToken)
    {
        var node = await _db.Set<Node>().SingleOrDefaultAsync(entity => entity.Id == nodeId, cancellationToken);
        if (node is null)
        {
            return Result<GraphWriteReceipt>.Fail(GraphErrors.NodeNotFound.ToError("The node was not found."));
        }

        if (!node.IsDeleted)
        {
            return Result<GraphWriteReceipt>.Ok(new GraphWriteReceipt(null, []));
        }

        if (Now - node.DeletedAt!.Value > GraphRetention.DeleteWindow)
        {
            return Result<GraphWriteReceipt>.Fail(WindowClosed());
        }

        var current = ChangeSnapshots.Of(node);
        var deletion = await NodeDeletionEntryAsync(nodeId, current, cancellationToken);
        if (deletion is null)
        {
            return Result<GraphWriteReceipt>.Fail(Conflict());
        }

        var cascadeEntries = await _filter.ChangeEntries()
            .Where(entry => entry.ChangeSetId == deletion.ChangeSetId && entry.CascadeOf == deletion.Id)
            .OrderBy(entry => entry.Sequence)
            .ToListAsync(cancellationToken);
        var cascadeIds = cascadeEntries.Select(entry => entry.EntityId).ToList();
        var relations = await _db.Set<Relation>().Where(relation => cascadeIds.Contains(relation.Id)).ToListAsync(cancellationToken);
        var otherNodeIds = relations.Select(relation => relation.SourceNodeId == nodeId ? relation.TargetNodeId : relation.SourceNodeId).Distinct().ToList();
        var liveOtherNodes = await _db.Set<Node>()
            .Where(other => otherNodeIds.Contains(other.Id) && other.DeletedAt == null)
            .Select(other => other.Id)
            .ToListAsync(cancellationToken);

        var changeSet = GraphChangeSet.Applied(_restore.AccountId, _restore.Provenance, Now);
        var changes = new List<GraphEntityChange>();
        var recorded = ChangeSnapshots.ReadNode(deletion.Before!).ToState();
        node.Revert(recorded with { DeletedAt = null }, Now);
        var root = changeSet.Record(GraphEntityKind.Node, node.Id, GraphChangeOperation.Restored, current, ChangeSnapshots.Of(node));
        changes.Add(new GraphEntityChange(GraphEntityKind.Node, node.Id, GraphChangeOperation.Restored, node.Version));

        var leftDeleted = new List<Guid>();
        foreach (var cascade in cascadeEntries)
        {
            var relation = relations.SingleOrDefault(candidate => candidate.Id == cascade.EntityId);
            var otherNodeId = relation is null ? Guid.Empty : relation.SourceNodeId == nodeId ? relation.TargetNodeId : relation.SourceNodeId;
            if (relation is null
                || cascade.IsPurged
                || !relation.IsDeleted
                || !ChangeSnapshots.AreEqual(cascade.After, ChangeSnapshots.Of(relation))
                || !liveOtherNodes.Contains(otherNodeId))
            {
                leftDeleted.Add(cascade.EntityId);
                continue;
            }

            var before = ChangeSnapshots.Of(relation);
            relation.Revert(deletedAt: null, Now);
            changeSet.Record(GraphEntityKind.Relation, relation.Id, GraphChangeOperation.Restored, before, ChangeSnapshots.Of(relation))
                .CascadesFrom(root);
            changes.Add(new GraphEntityChange(GraphEntityKind.Relation, relation.Id, GraphChangeOperation.Restored, null));
        }

        _db.Add(changeSet);
        EmbeddingScheduling.ScheduleFor(_db, changeSet, Now);
        return Result<GraphWriteReceipt>.Ok(new GraphWriteReceipt(changeSet.Id, changes) { RelationsLeftDeleted = leftDeleted });
    }

    private async Task<Result<GraphWriteReceipt>> RestoreRelationAsync(Guid relationId, CancellationToken cancellationToken)
    {
        var relation = await _db.Set<Relation>().SingleOrDefaultAsync(entity => entity.Id == relationId, cancellationToken);
        if (relation is null)
        {
            return Result<GraphWriteReceipt>.Fail(GraphErrors.RelationNotFound.ToError("The relation was not found."));
        }

        if (!relation.IsDeleted)
        {
            return Result<GraphWriteReceipt>.Ok(new GraphWriteReceipt(null, []));
        }

        if (Now - relation.DeletedAt!.Value > GraphRetention.DeleteWindow)
        {
            return Result<GraphWriteReceipt>.Fail(WindowClosed());
        }

        var current = ChangeSnapshots.Of(relation);
        var deletion = await RelationDeletionEntryAsync(relationId, current, cancellationToken);
        if (deletion is null)
        {
            return Result<GraphWriteReceipt>.Fail(Conflict());
        }

        var liveNodes = await _db.Set<Node>()
            .CountAsync(
                node => (node.Id == relation.SourceNodeId || node.Id == relation.TargetNodeId) && node.DeletedAt == null,
                cancellationToken);
        if (liveNodes < 2)
        {
            return Result<GraphWriteReceipt>.Fail(GraphErrors.UndoConflict.ToError(
                "A node of the relation is deleted. Restore it first.",
                new Dictionary<string, string[]> { ["relation"] = ["A node of the relation is deleted."] }));
        }

        var changeSet = GraphChangeSet.Applied(_restore.AccountId, _restore.Provenance, Now);
        relation.Revert(deletedAt: null, Now);
        changeSet.Record(GraphEntityKind.Relation, relation.Id, GraphChangeOperation.Restored, current, ChangeSnapshots.Of(relation));
        _db.Add(changeSet);
        return Result<GraphWriteReceipt>.Ok(new GraphWriteReceipt(
            changeSet.Id,
            [new GraphEntityChange(GraphEntityKind.Relation, relation.Id, GraphChangeOperation.Restored, null)]));
    }

    // The entry that left the Node as it is now: the newest deletion of the Node itself (never
    // one cascading from another entity) whose recorded state is still the current one. None
    // means something else touched the tombstone since.
    private async Task<ChangeEntry?> NodeDeletionEntryAsync(Guid nodeId, string current, CancellationToken cancellationToken) =>
        LeftAsItIs((await DeletionsAsync(GraphEntityKind.Node, nodeId, cancellationToken)).Where(entry => !entry.IsCascade), current);

    // The entry that left the Relation as it is now: its newest deletion, on its own or with its
    // Node, whose recorded state is still the current one.
    private async Task<ChangeEntry?> RelationDeletionEntryAsync(Guid relationId, string current, CancellationToken cancellationToken) =>
        LeftAsItIs(await DeletionsAsync(GraphEntityKind.Relation, relationId, cancellationToken), current);

    // The entity's live deletion entries, newest first.
    private Task<List<ChangeEntry>> DeletionsAsync(GraphEntityKind kind, Guid entityId, CancellationToken cancellationToken) =>
        _filter.ChangeEntries()
            .Where(entry => entry.EntityKind == kind
                && entry.EntityId == entityId
                && entry.Operation == GraphChangeOperation.Deleted
                && entry.PurgedAt == null)
            .OrderByDescending(entry => entry.Id)
            .ToListAsync(cancellationToken);

    private static ChangeEntry? LeftAsItIs(IEnumerable<ChangeEntry> deletions, string current) =>
        deletions.FirstOrDefault(entry => ChangeSnapshots.AreEqual(entry.After, current));

    private static Error WindowClosed() => GraphErrors.UndoWindowClosed.ToError(
        $"The entity was deleted more than {GraphRetention.DeleteWindow.TotalDays:0} days ago. It can no longer be restored.");

    private static Error Conflict() => GraphErrors.UndoConflict.ToError(
        ConflictMessage,
        new Dictionary<string, string[]> { ["entity"] = ["Changed after it was deleted."] });
}

/// <summary>One deleted entity to restore, in the request's transaction, and who restores it.</summary>
internal sealed record EntityToRestore(Guid AccountId, Provenance Provenance, GraphEntityKind Kind, Guid EntityId, DateTimeOffset Now);
