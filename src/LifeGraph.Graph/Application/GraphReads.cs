using System.Buffers.Text;
using System.Text.Json;
using LifeGraph.Graph.Contracts;
using LifeGraph.Graph.Domain;
using LifeGraph.Infrastructure.Agents;
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
internal sealed partial class GraphReads(
    LifeGraphDbContext db,
    IAccountContext accountContext,
    GraphReadFilter filter,
    IAgentIdentityDirectory agentDirectory,
    LocalGraphReads localGraphs,
    NodeViews views,
    HybridSearch hybridSearch)
{
    /// <summary>The fewest characters a search takes (DA-184): one letter finds too much to be useful.</summary>
    public const int SearchMinLength = 2;

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

            // The Unanchored View is a query (DA-197): the Nodes outside the reach of the root,
            // which is in its own reach and so never listed (DA-206). Only the first page counts
            // them, the one the Inbox counter reads: a later page would walk the reach again for
            // a total the client already has (DB-024).
            int? totalCount = null;
            if (query.Unanchored)
            {
                var anchored = AnchorReach.AnchoredNodeIds(db);
                nodes = nodes.Where(Node.IsNotAccountRootPredicate).Where(node => !anchored.Contains(node.Id));
                if (after is null)
                {
                    totalCount = await nodes.CountAsync(token);
                }
            }

            if (after is { } cursor)
            {
                nodes = nodes.Where(node => node.Id.CompareTo(cursor) < 0);
            }

            // Newest first: ids are UUIDv7, so they follow the creation time.
            var softRelations = filter.SoftRelations();
            var rows = await nodes.OrderByDescending(node => node.Id)
                .Take(limit + 1)
                .Select(node => new NodeSummary(node.Id, node.Title, node.TypeId, node.Version, node.InboxEnteredAt != null, node.CreatedAt, node.UpdatedAt, Node.SuggestedTypeOf(node.TypeId, node.SuggestedType))
                {
                    IsAccountRoot = node.SystemKey == SystemOntology.AccountRootTypeKey,
                    SoftRelationCount = softRelations.Count(soft => soft.SourceNodeId == node.Id || soft.TargetNodeId == node.Id),
                })
                .ToListAsync(token);
            var page = PageOf(rows, limit, summary => summary.Id).Value!;
            return Result<PagedList<NodeSummary>>.Ok(page with { Page = page.Page with { TotalCount = totalCount } });
        }, cancellationToken);
    }

    /// <summary>
    /// The Raiz da Conta (DA-206), through the central filter; <c>graph.root_missing</c> when the
    /// Account has none yet, never not found (DA-202).
    /// </summary>
    public Task<Result<AccountRootItem>> GetAccountRootAsync(CancellationToken cancellationToken) =>
        RunAsync(NoErrors(), async token =>
        {
            var rootId = await filter.Nodes().Where(Node.IsAccountRootPredicate).Select(node => (Guid?)node.Id).SingleOrDefaultAsync(token);
            return rootId is { } id
                ? Result<AccountRootItem>.Ok(new AccountRootItem(id))
                : Result<AccountRootItem>.Fail(GraphErrors.RootMissing.ToError(GraphContextReads.RootMissingMessage));
        }, cancellationToken);

    /// <summary>
    /// The person's search by words and by meaning (DA-183), over the Nodes the central filter
    /// shows; the model is asked before the transaction opens. The words are never logged (GEN-043).
    /// </summary>
    public async Task<Result<NodeSearchResults>> SearchAsync(string? q, int? limit, CancellationToken cancellationToken)
    {
        var errors = NoErrors();
        var text = q?.Trim() ?? string.Empty;
        if (text.Length < SearchMinLength || text.Length > GraphReadLimits.QueryMaxLength)
        {
            errors["q"] = [$"Must have between {SearchMinLength} and {GraphReadLimits.QueryMaxLength} characters."];
        }

        var count = limit ?? GraphReadLimits.SearchDefaultCount;
        if (count is < 1 or > GraphReadLimits.SearchMaxCount)
        {
            errors["limit"] = [$"Must be between 1 and {GraphReadLimits.SearchMaxCount}."];
        }

        var embedding = errors.Count == 0 ? await hybridSearch.EmbedAsync(text, cancellationToken) : null;
        return await RunAsync(errors, async token =>
        {
            var outcome = await hybridSearch.SearchAsync(GraphReader.Human, new NodeSearch(text, TypeId: null, count), embedding, token);
            var matches = outcome.Hits.ToDictionary(hit => hit.NodeId, hit => hit.Match);
            var hits = await views.HitsAsync([.. outcome.Hits.Select(hit => hit.NodeId)], token);
            var items = hits.Select(hit => new NodeSearchItem(hit.Id, hit.Title.Text, hit.TypeName, hit.Excerpt.Text, matches[hit.Id])).ToList();
            return Result<NodeSearchResults>.Ok(new NodeSearchResults(items, outcome.Degraded));
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

            var tags = await filter.NodeTags()
                .Where(link => link.NodeId == node.Id)
                .Join(filter.Tags(), link => link.TagId, tag => tag.Id, (link, tag) => new NodeTagItem(tag.Id, tag.Name))
                .ToListAsync(token);

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
                node.HiddenFromAgents,
                Node.SuggestedTypeOf(node.TypeId, node.SuggestedType))
            {
                IsAccountRoot = node.IsAccountRoot,
                Tags = [.. tags.OrderBy(tag => tag.Name, StringComparer.CurrentCultureIgnoreCase)],
            });
        }, cancellationToken);

    /// <summary>
    /// The local graph around a live Node (DA-138), from 1 to 3 Relations away; a Node the
    /// reader does not see reads as not found, as in <see cref="GetNodeAsync"/>.
    /// </summary>
    public Task<Result<LocalGraph>> GetLocalGraphAsync(Guid nodeId, int? depth, CancellationToken cancellationToken)
    {
        var errors = NoErrors();
        var walkDepth = depth ?? LocalGraphLimits.DefaultDepth;
        if (walkDepth is < LocalGraphLimits.MinDepth or > LocalGraphLimits.MaxDepth)
        {
            errors["depth"] = [$"Must be between {LocalGraphLimits.MinDepth} and {LocalGraphLimits.MaxDepth}."];
        }

        return RunAsync(errors, async token =>
            filter.Reader is { } reader && await localGraphs.ExpandAsync(reader, nodeId, walkDepth, token) is { } graph
                ? Result<LocalGraph>.Ok(graph)
                : Result<LocalGraph>.Fail(GraphErrors.NodeNotFound.ToError("The node was not found.")), cancellationToken);
    }

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

    /// <summary>
    /// The Soft Relations of a live Node the person may Accept or Ignore, newest first, each
    /// seen from the Node, with why it was inferred (DA-070, DA-179).
    /// </summary>
    public Task<Result<PagedList<SoftRelationItem>>> ListSoftRelationsAsync(Guid nodeId, string? cursor, int? limit, CancellationToken cancellationToken)
    {
        var errors = NoErrors();
        var after = PageCursor.Read(errors, cursor);
        var pageSize = PageSize(errors, limit, DefaultNodePageSize);

        return RunAsync(errors, async token =>
        {
            var nodes = filter.Nodes();
            if (!await nodes.AnyAsync(node => node.Id == nodeId, token))
            {
                return Result<PagedList<SoftRelationItem>>.Fail(GraphErrors.NodeNotFound.ToError("The node was not found."));
            }

            var softRelations = filter.SoftRelations()
                .Where(relation => relation.SourceNodeId == nodeId || relation.TargetNodeId == nodeId);
            if (after is { } last)
            {
                softRelations = softRelations.Where(relation => relation.Id.CompareTo(last) < 0);
            }

            var rows = await (
                from relation in softRelations
                join other in nodes on (relation.SourceNodeId == nodeId ? relation.TargetNodeId : relation.SourceNodeId) equals other.Id
                orderby relation.Id descending
                select new { relation.Id, OtherId = other.Id, other.Title, other.TypeId, relation.InferenceMethod, relation.InferenceModelId, relation.Confidence })
                .Take(pageSize + 1)
                .ToListAsync(token);
            var items = rows.Select(row => new SoftRelationItem(
                    row.Id,
                    row.OtherId,
                    row.Title,
                    row.TypeId,
                    new SoftRelationExplanation(row.InferenceMethod!, row.InferenceModelId!, row.Confidence!.Value)))
                .ToList();
            return Result<PagedList<SoftRelationItem>>.Ok(PageOf(items, pageSize, item => item.Id).Value!);
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

    /// <summary>The person's Tags, oldest first, each with how many live Nodes have it (DA-160).</summary>
    public Task<Result<PagedList<TagItem>>> ListTagsAsync(string? cursor, int? limit, CancellationToken cancellationToken)
    {
        var errors = NoErrors();
        var after = PageCursor.Read(errors, cursor);
        var pageSize = PageSize(errors, limit, MaxPageSize);

        return RunAsync(errors, async token =>
        {
            var tags = filter.Tags();
            if (after is { } last)
            {
                tags = tags.Where(tag => tag.Id.CompareTo(last) > 0);
            }

            var rows = await ToTagItems(tags.OrderBy(tag => tag.Id).Take(pageSize + 1)).ToListAsync(token);
            return PageOf(rows, pageSize, item => item.Id);
        }, cancellationToken);
    }

    public Task<Result<TagItem>> GetTagAsync(Guid tagId, CancellationToken cancellationToken) =>
        RunAsync(NoErrors(), async token =>
        {
            var tag = await ToTagItems(filter.Tags().Where(tag => tag.Id == tagId)).SingleOrDefaultAsync(token);
            return tag is null
                ? Result<TagItem>.Fail(GraphErrors.TagNotFound.ToError("The tag was not found."))
                : Result<TagItem>.Ok(tag);
        }, cancellationToken);

    private IQueryable<TagItem> ToTagItems(IQueryable<Tag> tags)
    {
        var links = filter.NodeTags();
        return tags.Select(tag => new TagItem(tag.Id, tag.Name, links.Count(link => link.TagId == tag.Id), tag.CreatedAt, tag.UpdatedAt));
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
            var typeCounts = await TypeCountsAsync([.. rows.Select(definition => definition.Id)], token);
            return PageOf([.. rows.Select(definition => ToItem(definition, typeCounts))], pageSize, item => item.Id);
        }, cancellationToken);
    }

    public Task<Result<PropertyDefinitionItem>> GetPropertyDefinitionAsync(Guid definitionId, CancellationToken cancellationToken) =>
        RunAsync(NoErrors(), async token =>
        {
            var definition = await db.Set<PropertyDefinition>().AsNoTracking().SingleOrDefaultAsync(definition => definition.Id == definitionId, token);
            return definition is null
                ? Result<PropertyDefinitionItem>.Fail(PropertyNotFound())
                : Result<PropertyDefinitionItem>.Ok(ToItem(definition, await TypeCountsAsync([definition.Id], token)));
        }, cancellationToken);

    /// <summary>
    /// What uses the Property Definition: Types, live and deleted Nodes, and each option
    /// (DA-066, DA-168). RLS keeps every count in the Account.
    /// </summary>
    public Task<Result<PropertyDefinitionUsage>> GetPropertyDefinitionUsageAsync(Guid definitionId, CancellationToken cancellationToken) =>
        RunAsync(NoErrors(), async token =>
        {
            var definition = await db.Set<PropertyDefinition>().AsNoTracking().SingleOrDefaultAsync(definition => definition.Id == definitionId, token);
            if (definition is null)
            {
                return Result<PropertyDefinitionUsage>.Fail(PropertyNotFound());
            }

            var key = definition.Id.ToString();
            var holding = await db.Database
                .SqlQuery<HoldingCount>($"""
                    SELECT count(*) FILTER (WHERE deleted_at IS NULL)::int AS live_count,
                           count(*) FILTER (WHERE deleted_at IS NOT NULL)::int AS deleted_count,
                           max(deleted_at) AS last_deleted_at
                    FROM nodes WHERE jsonb_exists(properties, {key})
                    """)
                .SingleAsync(token);
            var optionCounts = definition.Options.Count == 0
                ? []
                : await db.Database
                    .SqlQuery<OptionCount>($"""
                        SELECT chosen.option_id, count(*)::int AS node_count
                        FROM nodes,
                             LATERAL jsonb_array_elements_text(
                                 CASE jsonb_typeof(properties -> {key}) WHEN 'array' THEN properties -> {key} ELSE jsonb_build_array(properties -> {key}) END
                             ) AS chosen(option_id)
                        WHERE jsonb_exists(properties, {key})
                        GROUP BY chosen.option_id
                        """)
                    .ToListAsync(token);
            var typeCounts = await TypeCountsAsync([definition.Id], token);
            return Result<PropertyDefinitionUsage>.Ok(new PropertyDefinitionUsage(
                holding.LiveCount,
                typeCounts.GetValueOrDefault(definition.Id),
                holding.DeletedCount,
                holding.LastDeletedAt + GraphRetention.DeleteWindow,
                optionCounts
                    .Where(count => Guid.TryParse(count.OptionId, out _))
                    .ToDictionary(count => Guid.Parse(count.OptionId), count => count.NodeCount)));
        }, cancellationToken);

    private async Task<Dictionary<Guid, int>> TypeCountsAsync(List<Guid> definitionIds, CancellationToken cancellationToken) =>
        await db.Set<TypeProperty>().AsNoTracking()
            .Where(property => definitionIds.Contains(property.PropertyDefinitionId))
            .GroupBy(property => property.PropertyDefinitionId)
            .Select(group => new { DefinitionId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(row => row.DefinitionId, row => row.Count, cancellationToken);

    private static Error PropertyNotFound() => GraphErrors.PropertyDefinitionNotFound.ToError("The property was not found.");

    private sealed record HoldingCount(int LiveCount, int DeletedCount, DateTimeOffset? LastDeletedAt);

    private sealed record OptionCount(string OptionId, int NodeCount);

    /// <summary>
    /// The feed of GraphChangeSets (DA-024). With <paramref name="since"/>, the ones after it,
    /// oldest first, to catch up; otherwise the history, newest first, older pages through
    /// <paramref name="before"/>. Filtered for Agent Changes (v2 §65): by agent, and the ones not reviewed yet.
    /// </summary>
    public Task<Result<ChangeSetFeed>> ListChangeSetsAsync(
        string? since,
        string? before,
        int? limit,
        ChangeSetFeedFilter feedFilter,
        CancellationToken cancellationToken)
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
            var changeSets = Filtered(filter.ChangeSets(), feedFilter).Include(changeSet => changeSet.Entries);
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
            var proposals = await filter.Proposals()
                .Where(proposal => proposal.ChangeSetId != null && ids.Contains(proposal.ChangeSetId.Value))
                .Select(proposal => new { proposal.Id, ChangeSetId = proposal.ChangeSetId!.Value })
                .ToDictionaryAsync(proposal => proposal.ChangeSetId, proposal => proposal.Id, token);
            var agents = await AgentRefs.OfAsync(
                agentDirectory,
                [.. page.Data.Where(changeSet => changeSet.AgentIdentityId is not null).Select(changeSet => changeSet.AgentIdentityId!.Value)],
                token);
            var createdAccountRoot = await ChangeSetUndo.CreatedAccountRootAsync(db, page.Data, token);
            var items = page.Data.Select(changeSet => ToItem(
                changeSet,
                undoneBy.TryGetValue(changeSet.Id, out var undo) ? undo : null,
                proposals.TryGetValue(changeSet.Id, out var proposal) ? proposal : null,
                agents,
                createdAccountRoot.Contains(changeSet.Id))).ToList();
            return Result<ChangeSetFeed>.Ok(new ChangeSetFeed(items, page.Page, LatestCursor(items, since, before)));
        }, cancellationToken);
    }

    private static IQueryable<GraphChangeSet> Filtered(IQueryable<GraphChangeSet> changeSets, ChangeSetFeedFilter feedFilter)
    {
        if (feedFilter.AgentsOnly || feedFilter.Unreviewed)
        {
            changeSets = changeSets.Where(changeSet => changeSet.ActorKind == ChangeActorKind.AgentIdentity);
        }

        if (feedFilter.AgentIdentityId is { } agentIdentityId)
        {
            changeSets = changeSets.Where(changeSet => changeSet.AgentIdentityId == agentIdentityId);
        }

        return feedFilter.Unreviewed ? changeSets.Where(changeSet => changeSet.ReviewedAt == null) : changeSets;
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
                return new TypePropertyItem(definition.Id, definition.Name, definition.ValueKind, definition.IsSystem, definition.IsArchived);
            })],
            type.CreatedAt,
            type.UpdatedAt,
            type.HiddenFromAgents,
            type.IsSystem))];
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

    /// <param name="agents">The agents of the page, by id; an agent's change missing from it reads as "Agente removido".</param>
    /// <param name="createdAccountRoot">Whether it created the Raiz da Conta, from <see cref="ChangeSetUndo.CreatedAccountRootAsync"/>.</param>
    internal static ChangeSetItem ToItem(
        GraphChangeSet changeSet,
        Guid? undoneBy,
        Guid? proposalId,
        IReadOnlyDictionary<Guid, AgentRef> agents,
        bool createdAccountRoot)
    {
        var sequenceOf = changeSet.Entries.ToDictionary(entry => entry.Id, entry => entry.Sequence);
        var agent = changeSet.ActorKind != ChangeActorKind.AgentIdentity
            ? null
            : changeSet.AgentIdentityId is { } agentIdentityId
                ? agents.GetValueOrDefault(agentIdentityId) ?? new AgentRef(agentIdentityId, null, Revoked: false)
                : new AgentRef(null, null, Revoked: false);
        var undo = changeSet.Status != ChangeSetStatus.Applied
            ? ChangeSetUndoState.Undone(undoneBy)
            : createdAccountRoot
                ? ChangeSetUndoState.Unavailable(UndoUnavailableReason.AccountRoot)
                : ChangeSetUndo.PurgedReason(changeSet) is { } reason
                    ? ChangeSetUndoState.Unavailable(reason)
                    : ChangeSetUndoState.Available;
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
            proposalId,
            changeSet.ReviewedAt,
            agent,
            undo,
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

    private static PropertyDefinitionItem ToItem(PropertyDefinition definition, Dictionary<Guid, int> typeCounts) =>
        new(
            definition.Id,
            definition.Name,
            definition.ValueKind,
            definition.Options,
            definition.CreatedAt,
            definition.UpdatedAt,
            definition.IsSystem,
            typeCounts.GetValueOrDefault(definition.Id),
            definition.IsArchived);

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

        // TryDecodeFromChars throws on a character outside Base64Url: check the text first.
        var bytes = new byte[GuidByteCount];
        if (cursor.Length > EncodedLength
            || !Base64Url.IsValid(cursor, out var decodedLength)
            || decodedLength != GuidByteCount
            || !Base64Url.TryDecodeFromChars(cursor, bytes, out var written)
            || written != GuidByteCount)
        {
            return false;
        }

        lastId = new Guid(bytes, bigEndian: true);
        return true;
    }
}
