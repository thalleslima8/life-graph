using LifeGraph.Graph.Contracts;
using LifeGraph.Graph.Domain;
using LifeGraph.Infrastructure.Jobs;
using LifeGraph.Infrastructure.Persistence;
using Limaj.Framework.Core;
using Microsoft.EntityFrameworkCore;

namespace LifeGraph.Graph.Application;

/// <summary>The job's entry of the pipeline (DA-152, DA-153).</summary>
internal sealed class ResourceFetchGraph(
    LifeGraphDbContext db,
    GraphWriter writer,
    IAccountContext accountContext,
    TimeProvider clock) : IResourceFetchGraph
{
    private static readonly Provenance SystemProvenance = new(new GraphActor.System(), WriteChannel.System);

    public Task<ResourceToFetch?> FindAsync(Guid nodeId, CancellationToken cancellationToken) =>
        db.Database.CurrentTransaction is null
            ? db.InAccountTransactionAsync(token => ReadAsync(nodeId, token), cancellationToken)
            : ReadAsync(nodeId, cancellationToken);

    public async Task<Result<GraphWriteReceipt>> WriteAsSystemAsync(IReadOnlyList<GraphOperation> operations, CancellationToken cancellationToken)
    {
        if (db.Database.CurrentTransaction is null || accountContext.AccountId is not { } accountId)
        {
            throw new InvalidOperationException("The system writes only inside the job's transaction of an Account (DA-152).");
        }

        var shapeErrors = GraphWriter.CheckShape(SystemProvenance, operations);
        if (!shapeErrors.IsEmpty)
        {
            return Result<GraphWriteReceipt>.Fail(shapeErrors.ToError());
        }

        // The runner tracks its job; whatever the failed write tracked must not reach its save.
        var trackedBefore = db.ChangeTracker.Entries().Select(entry => entry.Entity).ToHashSet(ReferenceEqualityComparer.Instance);
        var now = GraphWriter.TruncatedToMicroseconds(clock.GetUtcNow());
        var written = await writer.WriteInTransactionAsync(
            new TransactionalWrite(accountId, SystemProvenance, operations, now),
            complete: null,
            cancellationToken);
        if (!written.IsSuccess)
        {
            foreach (var entry in db.ChangeTracker.Entries().Where(entry => !trackedBefore.Contains(entry.Entity)).ToList())
            {
                entry.State = EntityState.Detached;
            }
        }

        return written;
    }

    private async Task<ResourceToFetch?> ReadAsync(Guid nodeId, CancellationToken cancellationToken)
    {
        var node = await db.Set<Node>().AsNoTracking()
            .SingleOrDefaultAsync(entity => entity.Id == nodeId && entity.DeletedAt == null, cancellationToken);
        if (node?.TypeId is not { } typeId
            || !await db.Set<NodeType>().AnyAsync(type => type.Id == typeId && type.SystemKey == SystemOntology.ResourceTypeKey, cancellationToken))
        {
            return null;
        }

        var propertyIds = await db.Set<PropertyDefinition>().AsNoTracking()
            .Where(definition => definition.SystemKey != null)
            .ToDictionaryAsync(definition => definition.SystemKey!, definition => definition.Id, cancellationToken);
        var keysById = propertyIds.ToDictionary(pair => Node.FieldOf(pair.Value), pair => pair.Key);
        var humanEdited = node.HumanEditedFields
            .Select(field => field switch
            {
                Node.TitleField => ResourceToFetch.TitleField,
                Node.BodyField => ResourceToFetch.BodyField,
                _ => keysById.GetValueOrDefault(field),
            })
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);
        var url = propertyIds.TryGetValue(SystemOntology.ResourcePropertyKeys.Url, out var urlId)
            && node.Properties.Values.TryGetValue(urlId, out var value)
            && value.ValueKind == System.Text.Json.JsonValueKind.String
                ? value.GetString()
                : null;

        return new ResourceToFetch(node.Id, node.Version, url, propertyIds, humanEdited);
    }
}

/// <summary>
/// Schedules the Resource fetch (DA-058, DA-152) in the transaction of the write that gave a
/// Resource its <c>url</c>, or a new one: the job carries only the Node's id. The fetch's own
/// writes never change the <c>url</c>, and an Undo does not pass here, so neither fetches again
/// (DA-153).
/// </summary>
internal static class ResourceFetchScheduling
{
    /// <summary>Fetches again the Resources a capture found with a failed fetch (DA-156).</summary>
    public static void ScheduleAgain(LifeGraphDbContext db, Guid accountId, IReadOnlyList<Guid> nodeIds, DateTimeOffset now)
    {
        foreach (var nodeId in nodeIds.Distinct())
        {
            db.Add(Job.Schedule(accountId, IResourceFetchGraph.JobKind, new ResourceFetchPayload(nodeId).ToJson(), now, now));
        }
    }

    public static void ScheduleFor(LifeGraphDbContext db, GraphWorkingSet workingSet, GraphChangeSet changeSet, DateTimeOffset now)
    {
        if (changeSet.ActorKind == ChangeActorKind.System)
        {
            return;
        }

        var nodeEntries = changeSet.Entries.Where(entry => entry.EntityKind == GraphEntityKind.Node
            && entry.Operation is GraphChangeOperation.Created or GraphChangeOperation.Updated);
        foreach (var entry in nodeEntries)
        {
            var after = ChangeSnapshots.ReadNode(entry.After!);
            if (after.TypeId is not { } typeId
                || workingSet.FindSchema(typeId) is not { Type.SystemKey: SystemOntology.ResourceTypeKey } schema
                || schema.Attached.Values.FirstOrDefault(definition => definition.SystemKey == SystemOntology.ResourcePropertyKeys.Url) is not { } urlDefinition
                || !after.Properties.TryGetValue(urlDefinition.Id, out var url))
            {
                continue;
            }

            var before = entry.Before is null ? null : ChangeSnapshots.ReadNode(entry.Before);
            if (before is not null
                && before.TypeId == typeId
                && before.Properties.TryGetValue(urlDefinition.Id, out var previousUrl)
                && previousUrl.GetRawText() == url.GetRawText())
            {
                continue;
            }

            db.Add(Job.Schedule(changeSet.AccountId, IResourceFetchGraph.JobKind, new ResourceFetchPayload(entry.EntityId).ToJson(), now, now));
        }
    }
}
