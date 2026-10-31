using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using LifeGraph.Graph.Domain;
using LifeGraph.Infrastructure.Accounts;
using LifeGraph.Infrastructure.Agents;
using LifeGraph.Infrastructure.Identity;
using LifeGraph.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace LifeGraph.Graph.Application;

/// <summary>
/// Reads everything the Account export holds (DA-247) as the person who owns the Account sees
/// it, through the central read filter: live Nodes, Hard Relations only (DA-178), no derived
/// data. Every table is read in keyset batches ordered by id (DB-023, DA-256), untracked
/// (DB-025), and each batch goes straight into the archive, which charges it to the size
/// budget: the read stops at the first batch past the cap. The caller runs it inside one
/// read-only snapshot (DA-248), so the batches agree with each other.
/// </summary>
internal sealed class AccountExportReader(
    LifeGraphDbContext db,
    IAccountHolderExport accountHolder,
    IAgentIdentityDirectory agentIdentities,
    IOptions<AccountExportOptions> options,
    TimeProvider clock)
{
    private int BatchSize => options.Value.ReadBatchSize;

    /// <summary>The archive, or <c>null</c> when it passes the cap: <paramref name="budget"/> then names the data that passed it.</summary>
    public async Task<byte[]?> BuildArchiveAsync(Guid accountId, AccountExportSizeBudget budget, CancellationToken cancellationToken)
    {
        // The job has no signed-in principal: it reads as the person who asked, and RLS keeps
        // every query to their Account.
        var filter = new GraphReadFilter(db, new AccountOwnerPrincipal(accountId));
        var now = clock.GetUtcNow();
        using var archive = new AccountExportArchiveWriter(budget);

        var account = await db.Set<Account>().AsNoTracking().SingleAsync(entity => entity.Id == accountId, cancellationToken);
        var email = await accountHolder.EmailOfAsync(accountId, cancellationToken);
        archive.WriteHeader(now, new ExportedAccount(account.Id, account.CreatedAt, email));

        // Kept for the Markdown of the Nodes, which names Types, Properties and Tags and links Relations.
        var definitions = new List<ExportedPropertyDefinition>();
        var types = new List<ExportedType>();
        var nodes = new List<ExportedNode>();
        var relations = new List<ExportedRelation>();
        var tags = new List<ExportedTag>();
        var attachedByType = new Dictionary<Guid, IReadOnlyList<Guid>>();

        var written = await WriteSectionAsync(archive, AccountExportData.PropertyDefinitions, db.Set<PropertyDefinition>().AsNoTracking(), definition => definition.Id,
            batch =>
            {
                definitions.AddRange(batch.Select(definition => new ExportedPropertyDefinition(
                    definition.Id,
                    definition.Name,
                    definition.ValueKind,
                    definition.Options,
                    definition.SystemKey,
                    definition.IsArchived,
                    definition.CreatedAt,
                    definition.UpdatedAt)));
                return Task.FromResult(archive.WriteBatch(definitions[^batch.Count..]));
            },
            cancellationToken)
            && await WriteSectionAsync(archive, AccountExportData.Types, filter.Types(), type => type.Id,
            async batch =>
            {
                // A Type holds at most GraphLimits.TypePropertiesMaxCount Properties, so this is bounded by the batch.
                var typeIds = batch.Select(type => type.Id).ToList();
                var attached = await db.Set<TypeProperty>().AsNoTracking()
                    .Where(property => typeIds.Contains(property.TypeId))
                    .OrderBy(property => property.TypeId).ThenBy(property => property.Position)
                    .Take(typeIds.Count * GraphLimits.TypePropertiesMaxCount)
                    .ToListAsync(cancellationToken);
                foreach (var group in attached.GroupBy(property => property.TypeId))
                {
                    attachedByType[group.Key] = [.. group.Select(property => property.PropertyDefinitionId)];
                }

                types.AddRange(batch.Select(type => new ExportedType(
                    type.Id,
                    type.Name,
                    type.HiddenFromAgents,
                    type.SystemKey,
                    attachedByType.GetValueOrDefault(type.Id, []),
                    type.CreatedAt,
                    type.UpdatedAt)));
                return archive.WriteBatch(types[^batch.Count..]);
            },
            cancellationToken)
            && await WriteSectionAsync(archive, AccountExportData.Nodes, filter.Nodes(), node => node.Id,
            async batch =>
            {
                var nodeIds = batch.Select(node => node.Id).ToList();
                var links = await ReadAllAsync(filter.NodeTags().Where(link => nodeIds.Contains(link.NodeId)), link => link.Id, cancellationToken);
                var tagIdsByNode = links.ToLookup(link => link.NodeId, link => link.TagId);
                nodes.AddRange(batch.Select(node => ExportedNodeOf(node, attachedByType, tagIdsByNode)));
                return archive.WriteBatch(nodes[^batch.Count..]);
            },
            cancellationToken)
            && await WriteSectionAsync(archive, AccountExportData.Relations, filter.Relations(), relation => relation.Id,
            batch =>
            {
                relations.AddRange(batch.Select(relation => new ExportedRelation(
                    relation.Id,
                    relation.SourceNodeId,
                    relation.TargetNodeId,
                    relation.Kind,
                    relation.Origin,
                    relation.Confidence,
                    relation.Strength,
                    relation.CreatedByAgentIdentityId,
                    relation.CreatedAt,
                    relation.UpdatedAt)));
                return Task.FromResult(archive.WriteBatch(relations[^batch.Count..]));
            },
            cancellationToken)
            && await WriteSectionAsync(archive, AccountExportData.Tags, filter.Tags(), tag => tag.Id,
            batch =>
            {
                tags.AddRange(batch.Select(tag => new ExportedTag(tag.Id, tag.Name, tag.CreatedAt, tag.UpdatedAt)));
                return Task.FromResult(archive.WriteBatch(tags[^batch.Count..]));
            },
            cancellationToken)
            && await WriteSectionAsync(archive, AccountExportData.Collections, filter.Collections(), collection => collection.Id,
            async batch =>
            {
                var collectionIds = batch.Select(collection => collection.Id).ToList();
                var liveNodes = filter.Nodes();
                var members = await ReadAllAsync(
                    db.Set<CollectionMember>().AsNoTracking()
                        .Where(member => collectionIds.Contains(member.CollectionId) && liveNodes.Any(node => node.Id == member.NodeId)),
                    member => member.Id,
                    cancellationToken);
                var membersByCollection = members.ToLookup(member => member.CollectionId, member => member.NodeId);
                return archive.WriteBatch(batch.Select(collection => new ExportedCollection(
                    collection.Id,
                    collection.Name,
                    collection.Criteria,
                    collection.Pinned,
                    [.. membersByCollection[collection.Id]],
                    collection.CreatedAt,
                    collection.UpdatedAt)));
            },
            cancellationToken)
            && await WriteSectionAsync(archive, AccountExportData.ChangeSets, filter.ChangeSets(), changeSet => changeSet.Id,
            async batch =>
            {
                var changeSetIds = batch.Select(changeSet => changeSet.Id).ToList();
                var entries = await ReadAllAsync(filter.ChangeEntries().Where(entry => changeSetIds.Contains(entry.ChangeSetId)), entry => entry.Id, cancellationToken);
                var entriesByChangeSet = entries.OrderBy(entry => entry.Sequence).ToLookup(entry => entry.ChangeSetId);
                var agentIds = batch.Where(changeSet => changeSet.AgentIdentityId is not null).Select(changeSet => changeSet.AgentIdentityId!.Value).ToHashSet();
                var agentNames = await agentIdentities.NamesOfAsync(agentIds, cancellationToken);
                return archive.WriteBatch(batch.Select(changeSet => new ExportedChangeSet(
                    changeSet.Id,
                    changeSet.Status,
                    new ExportedProvenance(
                        changeSet.ActorKind,
                        changeSet.AgentIdentityId,
                        changeSet.AgentIdentityId is { } agentId && agentNames.TryGetValue(agentId, out var agent) ? agent.Name : null,
                        changeSet.Channel,
                        changeSet.Source),
                    changeSet.RevertsChangeSetId,
                    changeSet.ReviewedAt,
                    changeSet.CreatedAt,
                    [.. entriesByChangeSet[changeSet.Id].Select(entry => new ExportedChangeEntry(
                        entry.Sequence,
                        entry.EntityKind,
                        entry.EntityId,
                        entry.Operation,
                        JsonOf(entry.Before),
                        JsonOf(entry.After),
                        entry.CascadeOf,
                        entry.PurgedAt))])));
            },
            cancellationToken)
            && await WriteSectionAsync(archive, AccountExportData.ChangeProposals, filter.Proposals(), proposal => proposal.Id,
            batch => Task.FromResult(archive.WriteBatch(batch.Select(proposal => new ExportedChangeProposal(
                proposal.Id,
                proposal.AgentIdentityId,
                proposal.StatusAt(now),
                proposal.OperationCount,
                JsonOf(proposal.Operations),
                proposal.RequiresUser,
                proposal.Source,
                proposal.ChangeSetId,
                proposal.CreatedAt,
                proposal.ExpiresAt,
                proposal.DecidedAtAsOf(now))))),
            cancellationToken)
            && await WriteAgentIdentitiesAsync(archive, accountId, cancellationToken)
            && await WriteSectionAsync(archive, AccountExportData.AgentReads, db.Set<AgentRead>().AsNoTracking(), read => read.Id,
            batch => Task.FromResult(archive.WriteBatch(batch.Select(read => new ExportedAgentRead(
                read.Id,
                read.AgentIdentityId,
                read.Operation,
                JsonOf(read.Arguments) ?? EmptyObject,
                read.ReturnedNodeIds,
                read.CreatedAt)))),
            cancellationToken)
            && await WriteSectionAsync(archive, AccountExportData.SharedGraphViews, db.Set<SharedGraphView>().AsNoTracking(), share => share.Id,
            async batch =>
            {
                var shareIds = batch.Select(share => share.Id).ToList();
                var shareNodes = await ReadAllAsync(
                    db.Set<SharedGraphViewNode>().AsNoTracking().Where(member => shareIds.Contains(member.SharedGraphViewId)), member => member.Id, cancellationToken);
                var shareProperties = await ReadAllAsync(
                    db.Set<SharedGraphViewProperty>().AsNoTracking().Where(property => shareIds.Contains(property.SharedGraphViewId)), property => property.Id, cancellationToken);
                var nodesByShare = shareNodes.ToLookup(member => member.SharedGraphViewId, member => member.NodeId);
                var propertiesByShare = shareProperties.ToLookup(property => property.SharedGraphViewId, property => property.PropertyDefinitionId);
                return archive.WriteBatch(batch.Select(share => new ExportedSharedGraphView(
                    share.Id,
                    share.RootNodeId,
                    share.MaxDepth,
                    share.IncludeTags,
                    [.. nodesByShare[share.Id]],
                    [.. propertiesByShare[share.Id]],
                    share.CreatedBy,
                    share.CreatedVia,
                    share.CreatedAt,
                    share.RevokedAt,
                    share.LastAccessedOn,
                    share.AccessDayCount)));
            },
            cancellationToken);

        return written ? archive.Finish(new AccountExportGraph(definitions, types, nodes, relations, tags)) : null;
    }

    /// <summary>Writes one section batch by batch; <c>false</c> once the archive is past the cap, and nothing more is read.</summary>
    private async Task<bool> WriteSectionAsync<TEntity>(
        AccountExportArchiveWriter archive,
        AccountExportData data,
        IQueryable<TEntity> source,
        Expression<Func<TEntity, Guid>> key,
        Func<IReadOnlyList<TEntity>, Task<bool>> writeBatch,
        CancellationToken cancellationToken)
    {
        archive.BeginSection(data);
        await foreach (var batch in BatchesAsync(source, key, cancellationToken))
        {
            if (!await writeBatch(batch))
            {
                return false;
            }
        }

        archive.EndSection();
        return true;
    }

    private async Task<bool> WriteAgentIdentitiesAsync(AccountExportArchiveWriter archive, Guid accountId, CancellationToken cancellationToken)
    {
        archive.BeginSection(AccountExportData.AgentIdentities);
        Guid? afterId = null;
        while (true)
        {
            var batch = await accountHolder.AgentIdentitiesAfterAsync(accountId, afterId, BatchSize, cancellationToken);
            if (batch.Count == 0)
            {
                break;
            }

            var exported = batch.Select(identity => new ExportedAgentIdentity(
                identity.Id,
                identity.Name,
                identity.ClientId,
                identity.ClientName,
                identity.RequiresApprovalForWrites,
                identity.CreatedAt,
                identity.LastUsedAt,
                identity.RevokedAt));
            if (!archive.WriteBatch(exported))
            {
                return false;
            }

            if (batch.Count < BatchSize)
            {
                break;
            }

            afterId = batch[^1].Id;
        }

        archive.EndSection();
        return true;
    }

    // The rows that hang off one batch (a Node's Tags, a GraphChangeSet's entries), read in batches too.
    private async Task<List<TEntity>> ReadAllAsync<TEntity>(IQueryable<TEntity> source, Expression<Func<TEntity, Guid>> key, CancellationToken cancellationToken)
    {
        var rows = new List<TEntity>();
        await foreach (var batch in BatchesAsync(source, key, cancellationToken))
        {
            rows.AddRange(batch);
        }

        return rows;
    }

    /// <summary>
    /// Keyset pagination (DB-023, DA-256): each query takes the next <see cref="BatchSize"/> rows
    /// in id order after the last id read, so a batch boundary neither repeats nor skips a row.
    /// </summary>
    private async IAsyncEnumerable<IReadOnlyList<TEntity>> BatchesAsync<TEntity>(
        IQueryable<TEntity> source,
        Expression<Func<TEntity, Guid>> key,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // The cursor is read through a member, so EF sends it as a parameter (DB-020).
        var cursor = new KeysetCursor();
        var afterCursor = Expression.Lambda<Func<TEntity, bool>>(
            Expression.GreaterThan(
                Expression.Call(key.Body, CompareTo, Expression.Field(Expression.Constant(cursor), nameof(KeysetCursor.LastId))),
                Expression.Constant(0)),
            key.Parameters);
        var keyOf = key.Compile();

        var first = true;
        while (true)
        {
            var page = first ? source : source.Where(afterCursor);
            var batch = await page.OrderBy(key).Take(BatchSize).ToListAsync(cancellationToken);
            if (batch.Count > 0)
            {
                yield return batch;
            }

            if (batch.Count < BatchSize)
            {
                yield break;
            }

            cursor.LastId = keyOf(batch[^1]);
            first = false;
        }
    }

    private static readonly MethodInfo CompareTo = typeof(Guid).GetMethod(nameof(Guid.CompareTo), [typeof(Guid)])!;

    private sealed class KeysetCursor
    {
        public Guid LastId;
    }

    private static readonly JsonElement EmptyObject = JsonDocument.Parse("{}").RootElement.Clone();

    private static ExportedNode ExportedNodeOf(Node node, Dictionary<Guid, IReadOnlyList<Guid>> attachedByType, ILookup<Guid, Guid> tagIdsByNode)
    {
        var attached = node.TypeId is { } typeId ? attachedByType.GetValueOrDefault(typeId, []).ToHashSet() : [];
        var values = node.Properties.Values;
        return new ExportedNode(
            node.Id,
            node.TypeId,
            node.Title,
            node.Body,
            [.. values.Where(pair => attached.Contains(pair.Key)).Select(pair => new ExportedPropertyValue(pair.Key, pair.Value))],
            [.. values.Where(pair => !attached.Contains(pair.Key)).Select(pair => new ExportedPropertyValue(pair.Key, pair.Value))],
            [.. tagIdsByNode[node.Id]],
            node.HiddenFromAgents,
            node.InboxEnteredAt,
            node.SystemKey,
            node.CanonicalUrl,
            Node.SuggestedTypeOf(node.TypeId, node.SuggestedType),
            node.CreatedByAgentIdentityId,
            node.Version,
            node.CreatedAt,
            node.UpdatedAt);
    }

    // Recorded states, proposed operations and read arguments are stored as JSON; an emptied one is absent.
    private static JsonElement? JsonOf(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    /// <summary>The person who owns the Account, for the filter of a read that has no signed-in principal.</summary>
    private sealed class AccountOwnerPrincipal(Guid accountId) : ICurrentPrincipal
    {
        public AuthenticatedPrincipal? Authenticated { get; } = new(accountId, PrincipalType.Human);
    }
}
