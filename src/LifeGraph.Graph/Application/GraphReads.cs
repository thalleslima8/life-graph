using System.Buffers.Text;
using System.Text.Json;
using LifeGraph.Graph.Contracts;
using LifeGraph.Graph.Domain;
using LifeGraph.Infrastructure.Errors;
using LifeGraph.Infrastructure.Persistence;
using Limaj.Framework.Core;
using Microsoft.EntityFrameworkCore;

namespace LifeGraph.Graph.Application;

/// <summary>
/// The reads of the graph for the person's own views (Lista, Inspector, Types). They run in
/// a transaction of the current Account, so RLS only shows its rows, and start from the
/// central read filter (<see cref="GraphReadFilter"/>), which never shows tombstones: a
/// deleted Node or one of another Account reads as not found (DA-104).
/// </summary>
internal sealed class GraphReads(LifeGraphDbContext db, IAccountContext accountContext, GraphReadFilter filter)
{
    public const int DefaultNodePageSize = 50;

    public const int MaxPageSize = 100;

    public Task<Result<PagedList<NodeSummary>>> ListNodesAsync(NodeListQuery query, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var after = PageCursor.Read(errors, query.Cursor);
        var limit = PageSize(errors, query.Limit, DefaultNodePageSize);
        if (query.WithoutType && query.TypeId is not null)
        {
            errors["withoutType"] = ["Filter by a type or by no type, not both."];
        }

        return RunAsync(errors, async token =>
        {
            var nodes = filter.Nodes();
            if (query.WithoutType)
            {
                nodes = nodes.Where(node => node.TypeId == null);
            }
            else if (query.TypeId is { } typeId)
            {
                nodes = nodes.Where(node => node.TypeId == typeId);
            }

            if (query.InInbox)
            {
                nodes = nodes.Where(node => node.InboxEnteredAt != null);
            }

            if (after is { } cursor)
            {
                nodes = nodes.Where(node => node.Id.CompareTo(cursor) < 0);
            }

            // Newest first: ids are UUIDv7, so they follow the creation time.
            var rows = await nodes.OrderByDescending(node => node.Id)
                .Take(limit + 1)
                .Select(node => new NodeSummary(node.Id, node.Title, node.TypeId, node.Version, node.InboxEnteredAt != null, node.CreatedAt, node.UpdatedAt))
                .ToListAsync(token);
            return PageOf(rows, limit, summary => summary.Id);
        }, cancellationToken);
    }

    public Task<Result<NodeDetail>> GetNodeAsync(Guid nodeId, CancellationToken cancellationToken) =>
        RunAsync(NoErrors(), async token =>
        {
            var node = await filter.Nodes().SingleOrDefaultAsync(node => node.Id == nodeId, token);
            if (node is null)
            {
                return Result<NodeDetail>.Fail(GraphErrors.NodeNotFound.ToError("The node was not found."));
            }

            var type = node.TypeId is { } typeId
                ? await filter.Types().Include(type => type.Properties).SingleAsync(type => type.Id == typeId, token)
                : null;
            var attachedIds = type?.Properties.OrderBy(property => property.Position).Select(property => property.PropertyDefinitionId).ToList() ?? [];
            var definitionIds = attachedIds.Concat(node.Properties.Values.Keys).Distinct().ToList();
            var definitions = await db.Set<PropertyDefinition>().AsNoTracking()
                .Where(definition => definitionIds.Contains(definition.Id))
                .ToDictionaryAsync(definition => definition.Id, token);

            NodePropertyValue ValueOf(Guid definitionId)
            {
                var definition = definitions[definitionId];
                JsonElement? value = node.Properties.Values.TryGetValue(definitionId, out var stored) ? stored : null;
                return new NodePropertyValue(definition.Id, definition.Name, definition.ValueKind, value);
            }

            var properties = attachedIds.Select(ValueOf).ToList();
            var otherProperties = node.Properties.Values.Keys
                .Where(definitionId => !attachedIds.Contains(definitionId))
                .Select(ValueOf)
                .OrderBy(property => property.Name, StringComparer.Ordinal)
                .ToList();

            return Result<NodeDetail>.Ok(new NodeDetail(
                node.Id,
                node.Title,
                node.Body,
                node.TypeId,
                type?.Name,
                node.Version,
                node.IsInInbox,
                node.InboxEnteredAt,
                node.CreatedAt,
                node.UpdatedAt,
                properties,
                otherProperties,
                node.HiddenFromAgents));
        }, cancellationToken);

    /// <summary>The live Relations of a live Node, either direction, newest first.</summary>
    public Task<Result<PagedList<RelationItem>>> ListRelationsAsync(Guid nodeId, string? cursor, int? limit, CancellationToken cancellationToken)
    {
        var errors = NoErrors();
        var after = PageCursor.Read(errors, cursor);
        var pageSize = PageSize(errors, limit, DefaultNodePageSize);

        return RunAsync(errors, async token =>
        {
            var nodes = filter.Nodes();
            if (!await nodes.AnyAsync(node => node.Id == nodeId, token))
            {
                return Result<PagedList<RelationItem>>.Fail(GraphErrors.NodeNotFound.ToError("The node was not found."));
            }

            var relations = filter.Relations()
                .Where(relation => relation.SourceNodeId == nodeId || relation.TargetNodeId == nodeId);
            if (after is { } last)
            {
                relations = relations.Where(relation => relation.Id.CompareTo(last) < 0);
            }

            var rows = await (
                from relation in relations
                join source in nodes on relation.SourceNodeId equals source.Id
                join target in nodes on relation.TargetNodeId equals target.Id
                orderby relation.Id descending
                select new RelationItem(
                    relation.Id,
                    relation.Kind,
                    relation.SourceNodeId,
                    source.Title,
                    relation.TargetNodeId,
                    target.Title,
                    relation.Assertion,
                    relation.Origin,
                    relation.Confidence,
                    relation.Strength,
                    relation.CreatedAt))
                .Take(pageSize + 1)
                .ToListAsync(token);
            return Result<PagedList<RelationItem>>.Ok(PageOf(rows, pageSize, item => item.Id).Value!);
        }, cancellationToken);
    }

    public Task<Result<PagedList<TypeItem>>> ListTypesAsync(string? cursor, int? limit, CancellationToken cancellationToken)
    {
        var errors = NoErrors();
        var after = PageCursor.Read(errors, cursor);
        var pageSize = PageSize(errors, limit, MaxPageSize);

        return RunAsync(errors, async token =>
        {
            var types = filter.Types().Include(type => type.Properties).AsQueryable();
            if (after is { } last)
            {
                types = types.Where(type => type.Id.CompareTo(last) > 0);
            }

            var rows = await types.OrderBy(type => type.Id).Take(pageSize + 1).ToListAsync(token);
            var items = await ToItemsAsync(rows, token);
            return PageOf(items, pageSize, item => item.Id);
        }, cancellationToken);
    }

    public Task<Result<TypeItem>> GetTypeAsync(Guid typeId, CancellationToken cancellationToken) =>
        RunAsync(NoErrors(), async token =>
        {
            var type = await filter.Types().Include(type => type.Properties).SingleOrDefaultAsync(type => type.Id == typeId, token);
            return type is null
                ? Result<TypeItem>.Fail(GraphErrors.TypeNotFound.ToError("The type was not found."))
                : Result<TypeItem>.Ok((await ToItemsAsync([type], token))[0]);
        }, cancellationToken);

    public Task<Result<PagedList<PropertyDefinitionItem>>> ListPropertyDefinitionsAsync(string? cursor, int? limit, CancellationToken cancellationToken)
    {
        var errors = NoErrors();
        var after = PageCursor.Read(errors, cursor);
        var pageSize = PageSize(errors, limit, MaxPageSize);

        return RunAsync(errors, async token =>
        {
            var definitions = db.Set<PropertyDefinition>().AsNoTracking();
            if (after is { } last)
            {
                definitions = definitions.Where(definition => definition.Id.CompareTo(last) > 0);
            }

            var rows = await definitions.OrderBy(definition => definition.Id).Take(pageSize + 1).ToListAsync(token);
            return PageOf([.. rows.Select(ToItem)], pageSize, item => item.Id);
        }, cancellationToken);
    }

    public Task<Result<PropertyDefinitionItem>> GetPropertyDefinitionAsync(Guid definitionId, CancellationToken cancellationToken) =>
        RunAsync(NoErrors(), async token =>
        {
            var definition = await db.Set<PropertyDefinition>().AsNoTracking().SingleOrDefaultAsync(definition => definition.Id == definitionId, token);
            return definition is null
                ? Result<PropertyDefinitionItem>.Fail(GraphErrors.PropertyDefinitionNotFound.ToError("The property was not found."))
                : Result<PropertyDefinitionItem>.Ok(ToItem(definition));
        }, cancellationToken);

    /// <summary>
    /// The feed of GraphChangeSets (DA-024). With <paramref name="since"/>, the ones after it,
    /// oldest first, to catch up; otherwise the history, newest first, older pages through
    /// <paramref name="before"/>.
    /// </summary>
    public Task<Result<ChangeSetFeed>> ListChangeSetsAsync(string? since, string? before, int? limit, CancellationToken cancellationToken)
    {
        var errors = NoErrors();
        var after = PageCursor.Read(errors, since);
        var older = PageCursor.Read(errors, before);
        var pageSize = PageSize(errors, limit, DefaultNodePageSize);
        if (since is not null && before is not null)
        {
            errors["since"] = ["Read after a cursor or before one, not both."];
        }

        return RunAsync(errors, async token =>
        {
            var changeSets = filter.ChangeSets().Include(changeSet => changeSet.Entries);
            List<GraphChangeSet> rows;
            if (after is { } sinceId)
            {
                rows = await changeSets.Where(changeSet => changeSet.Id.CompareTo(sinceId) > 0)
                    .OrderBy(changeSet => changeSet.Id)
                    .Take(pageSize + 1)
                    .AsSplitQuery()
                    .ToListAsync(token);
            }
            else
            {
                var history = older is { } beforeId ? changeSets.Where(changeSet => changeSet.Id.CompareTo(beforeId) < 0) : changeSets;
                rows = await history.OrderByDescending(changeSet => changeSet.Id)
                    .Take(pageSize + 1)
                    .AsSplitQuery()
                    .ToListAsync(token);
            }

            var page = PageOf(rows, pageSize, changeSet => changeSet.Id).Value!;
            var ids = page.Data.Select(changeSet => changeSet.Id).ToList();
            var undoneBy = await filter.ChangeSets()
                .Where(changeSet => changeSet.RevertsChangeSetId != null && ids.Contains(changeSet.RevertsChangeSetId.Value))
                .Select(changeSet => new { changeSet.Id, Reverts = changeSet.RevertsChangeSetId!.Value })
                .ToDictionaryAsync(undo => undo.Reverts, undo => undo.Id, token);
            var items = page.Data.Select(changeSet => ToItem(changeSet, undoneBy.TryGetValue(changeSet.Id, out var undo) ? undo : null)).ToList();
            return Result<ChangeSetFeed>.Ok(new ChangeSetFeed(items, page.Page, LatestCursor(items, since, before)));
        }, cancellationToken);
    }

    private async Task<Result<T>> RunAsync<T>(
        Dictionary<string, string[]> errors,
        Func<CancellationToken, Task<Result<T>>> read,
        CancellationToken cancellationToken)
    {
        if (accountContext.AccountId is null)
        {
            return Result<T>.Fail(CommonErrors.Unauthorized.ToError("Sign in to read the graph."));
        }

        if (errors.Count > 0)
        {
            return Result<T>.Fail(CommonErrors.ValidationFailed.ToError(CommonErrors.ValidationFailedMessage, errors));
        }

        return await db.InAccountTransactionAsync(read, cancellationToken);
    }

    private async Task<List<TypeItem>> ToItemsAsync(List<NodeType> types, CancellationToken cancellationToken)
    {
        var definitionIds = types.SelectMany(type => type.Properties.Select(property => property.PropertyDefinitionId)).Distinct().ToList();
        var definitions = await db.Set<PropertyDefinition>().AsNoTracking()
            .Where(definition => definitionIds.Contains(definition.Id))
            .ToDictionaryAsync(definition => definition.Id, cancellationToken);

        return [.. types.Select(type => new TypeItem(
            type.Id,
            type.Name,
            [.. type.Properties.OrderBy(property => property.Position).Select(property =>
            {
                var definition = definitions[property.PropertyDefinitionId];
                return new TypePropertyItem(definition.Id, definition.Name, definition.ValueKind);
            })],
            type.CreatedAt,
            type.UpdatedAt,
            type.HiddenFromAgents))];
    }

    // The newest GraphChangeSet this read returned, never one it did not: resuming from it
    // skips nothing the person saw. Ids are UUIDv7, so a write that commits late with an
    // earlier id can be missed; the refetch on focus covers it (DA-024).
    private static string? LatestCursor(List<ChangeSetItem> items, string? since, string? before) =>
        (since, before) switch
        {
            (not null, _) => items.Count > 0 ? PageCursor.Write(items[^1].Id) : since,
            (null, null) => items.Count > 0 ? PageCursor.Write(items[0].Id) : null,
            _ => null,
        };

    private static ChangeSetItem ToItem(GraphChangeSet changeSet, Guid? undoneBy)
    {
        var sequenceOf = changeSet.Entries.ToDictionary(entry => entry.Id, entry => entry.Sequence);
        return new ChangeSetItem(
            changeSet.Id,
            changeSet.Status,
            changeSet.ActorKind,
            changeSet.AgentIdentityId,
            changeSet.Channel,
            changeSet.Source,
            changeSet.CreatedAt,
            changeSet.RevertsChangeSetId,
            undoneBy,
            [.. changeSet.Entries.OrderBy(entry => entry.Sequence).Select(entry => new ChangeEntryItem(
                entry.Sequence,
                entry.EntityKind,
                entry.EntityId,
                entry.Operation,
                LabelOf(entry),
                entry.IsPurged,
                entry.CascadeOf is { } root ? sequenceOf[root] : null))]);
    }

    // What the person recognizes the entity by, from the recorded state; nothing once purged.
    private static string? LabelOf(ChangeEntry entry)
    {
        if ((entry.After ?? entry.Before) is not { } recorded)
        {
            return null;
        }

        using var snapshot = JsonDocument.Parse(recorded);
        var field = entry.EntityKind switch
        {
            GraphEntityKind.Node => "title",
            GraphEntityKind.Relation => "kind",
            _ => "name",
        };
        return snapshot.RootElement.TryGetProperty(field, out var label) ? label.GetString() : null;
    }

    private static PropertyDefinitionItem ToItem(PropertyDefinition definition) =>
        new(definition.Id, definition.Name, definition.ValueKind, definition.Options, definition.CreatedAt, definition.UpdatedAt);

    private static Dictionary<string, string[]> NoErrors() => new(StringComparer.Ordinal);

    private static int PageSize(Dictionary<string, string[]> errors, int? limit, int defaultSize)
    {
        if (limit is null)
        {
            return defaultSize;
        }

        if (limit is < 1 or > MaxPageSize)
        {
            errors["limit"] = [$"Must be between 1 and {MaxPageSize}."];
        }

        return limit.Value;
    }

    // One row more than the page tells whether there is a next page.
    private static Result<PagedList<T>> PageOf<T>(List<T> rows, int pageSize, Func<T, Guid> idOf)
    {
        var hasMore = rows.Count > pageSize;
        var items = hasMore ? rows[..pageSize] : rows;
        return Result<PagedList<T>>.Ok(new PagedList<T>(items, new PageInfo(hasMore ? PageCursor.Write(idOf(items[^1])) : null)));
    }
}

/// <summary>An opaque page cursor (API-014): the id of the last item, base64url-encoded.</summary>
internal static class PageCursor
{
    private const int GuidByteCount = 16;

    // A longer cursor cannot decode to one Guid: refuse it before decoding.
    private static readonly int EncodedLength = Base64Url.GetEncodedLength(GuidByteCount);

    public static string Write(Guid lastId) => Base64Url.EncodeToString(lastId.ToByteArray(bigEndian: true));

    public const string UnknownCursorMessage = "Unknown cursor. Start again from the first page.";

    public static Guid? Read(Dictionary<string, string[]> errors, string? cursor)
    {
        if (!TryRead(cursor, out var lastId))
        {
            errors["cursor"] = [UnknownCursorMessage];
        }

        return lastId;
    }

    /// <summary>The last id a cursor names; <c>null</c> with no cursor. False for a cursor this did not write.</summary>
    public static bool TryRead(string? cursor, out Guid? lastId)
    {
        lastId = null;
        if (cursor is null)
        {
            return true;
        }

        var bytes = new byte[GuidByteCount];
        if (cursor.Length > EncodedLength || !Base64Url.TryDecodeFromChars(cursor, bytes, out var written) || written != GuidByteCount)
        {
            return false;
        }

        lastId = new Guid(bytes, bigEndian: true);
        return true;
    }
}
