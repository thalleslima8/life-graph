using LifeGraph.Graph.Contracts;
using LifeGraph.Graph.Domain;
using Limaj.Framework.Core;
using Microsoft.EntityFrameworkCore;

namespace LifeGraph.Graph.Application;

/// <summary>
/// The reads of Collections (DA-163). A dynamic Collection's Nodes are evaluated on every read,
/// never stored (DA-063), from the central read filter, so a Collection never shows a Node the
/// reader could not see otherwise. A criterion that refers to something gone matches nothing
/// and is reported in <see cref="CollectionItem.NeedsReview"/> (DA-171).
/// </summary>
internal sealed partial class GraphReads
{
    public Task<Result<PagedList<CollectionItem>>> ListCollectionsAsync(string? cursor, int? limit, CancellationToken cancellationToken)
    {
        var errors = NoErrors();
        var after = PageCursor.Read(errors, cursor);
        var pageSize = PageSize(errors, limit, MaxPageSize);

        return RunAsync(errors, async token =>
        {
            var collections = filter.Collections();
            if (after is { } last)
            {
                collections = collections.Where(collection => collection.Id.CompareTo(last) > 0);
            }

            // In the order they were created: ids are UUIDv7 (DA-173 pins the first ones).
            var rows = await collections.OrderBy(collection => collection.Id).Take(pageSize + 1).ToListAsync(token);
            var references = await ReferencesOfAsync(rows, token);
            return PageOf([.. rows.Select(collection => ToItem(collection, references))], pageSize, item => item.Id);
        }, cancellationToken);
    }

    public Task<Result<CollectionItem>> GetCollectionAsync(Guid collectionId, CancellationToken cancellationToken) =>
        RunAsync(NoErrors(), async token =>
        {
            var collection = await filter.Collections().SingleOrDefaultAsync(collection => collection.Id == collectionId, token);
            if (collection is null)
            {
                return CollectionNotFound<CollectionItem>();
            }

            var references = await ReferencesOfAsync([collection], token);
            return Result<CollectionItem>.Ok(ToItem(collection, references));
        }, cancellationToken);

    /// <summary>The Nodes of a Collection the reader sees, newest first.</summary>
    public Task<Result<PagedList<NodeSummary>>> ListCollectionMembersAsync(
        Guid collectionId,
        string? cursor,
        int? limit,
        CancellationToken cancellationToken)
    {
        var errors = NoErrors();
        var after = PageCursor.Read(errors, cursor);
        var pageSize = PageSize(errors, limit, DefaultNodePageSize);

        return RunAsync(errors, async token =>
        {
            var collection = await filter.Collections().SingleOrDefaultAsync(collection => collection.Id == collectionId, token);
            if (collection is null)
            {
                return CollectionNotFound<PagedList<NodeSummary>>();
            }

            var nodes = await MembersOfAsync(collection, token);
            if (after is { } last)
            {
                nodes = nodes.Where(node => node.Id.CompareTo(last) < 0);
            }

            var softRelations = filter.SoftRelations();
            var rows = await nodes.OrderByDescending(node => node.Id)
                .Take(pageSize + 1)
                .Select(node => new NodeSummary(node.Id, node.Title, node.TypeId, node.Version, node.InboxEnteredAt != null, node.CreatedAt, node.UpdatedAt, Node.SuggestedTypeOf(node.TypeId, node.SuggestedType))
                {
                    SoftRelationCount = softRelations.Count(soft => soft.SourceNodeId == node.Id || soft.TargetNodeId == node.Id),
                })
                .ToListAsync(token);
            return PageOf(rows, pageSize, summary => summary.Id);
        }, cancellationToken);
    }

    private static Result<T> CollectionNotFound<T>() =>
        Result<T>.Fail(GraphErrors.CollectionNotFound.ToError("The collection was not found."));

    private static CollectionItem ToItem(Collection collection, ICollectionReferences references)
    {
        var needsReview = collection.Criteria is { } criteria
            ? CollectionCriteriaRules.Review(criteria, references).Select(broken => new CollectionReviewItem(broken.CriterionIndex, broken.Reason)).ToList()
            : null;
        return new CollectionItem(
            collection.Id,
            collection.Name,
            collection.Criteria,
            collection.Pinned,
            needsReview is { Count: > 0 } ? needsReview : null,
            collection.CreatedAt,
            collection.UpdatedAt);
    }

    private async Task<IQueryable<Node>> MembersOfAsync(Collection collection, CancellationToken token)
    {
        var nodes = filter.Nodes();
        if (collection.Criteria is not { } criteria)
        {
            var members = db.Set<CollectionMember>().Where(member => member.CollectionId == collection.Id);
            return nodes.Where(node => members.Any(member => member.NodeId == node.Id));
        }

        var references = await ReferencesOfAsync([collection], token);
        var broken = CollectionCriteriaRules.Review(criteria, references).Select(item => item.CriterionIndex).ToHashSet();
        var idSets = criteria.Criteria
            .Select((criterion, index) => broken.Contains(index) ? NoNodeIds() : NodeIdsMatching(criterion))
            .ToList();

        if (criteria.Match == CollectionMatch.All)
        {
            foreach (var ids in idSets)
            {
                nodes = nodes.Where(node => ids.Contains(node.Id));
            }

            return nodes;
        }

        var anyIds = idSets.Aggregate((left, right) => left.Union(right));
        return nodes.Where(node => anyIds.Contains(node.Id));
    }

    private IQueryable<Guid> NoNodeIds() => db.Set<Node>().Where(_ => false).Select(node => node.Id);

    private IQueryable<Guid> NodeIdsMatching(CollectionCriterion criterion)
    {
        switch (criterion.Type)
        {
            case CollectionCriterionType.Type:
                var typeId = criterion.TypeId!.Value;
                return db.Set<Node>().Where(node => node.TypeId == typeId).Select(node => node.Id);

            case CollectionCriterionType.Tag:
                var tagId = criterion.TagId!.Value;
                return db.Set<NodeTag>().Where(link => link.TagId == tagId).Select(link => link.NodeId);

            case CollectionCriterionType.Relation:
                var otherId = criterion.NodeId!.Value;
                var kind = criterion.RelationKind;
                return filter.Relations()
                    .Where(relation => relation.Assertion == RelationAssertion.Hard
                        && (kind == null || relation.Kind == kind)
                        && (relation.SourceNodeId == otherId || relation.TargetNodeId == otherId))
                    .Select(relation => relation.SourceNodeId == otherId ? relation.TargetNodeId : relation.SourceNodeId);

            default:
                return NodeIdsWithProperty(criterion);
        }
    }

    // Values live in JSONB keyed by property id (DA-015): containment uses the GIN index, and
    // the value is the one the Property stores, so equal means the same stored JSON.
    private IQueryable<Guid> NodeIdsWithProperty(CollectionCriterion criterion)
    {
        var key = criterion.PropertyId!.Value.ToString("D");
        var value = criterion.Value?.GetRawText();
        return criterion.Op switch
        {
            CollectionCriterionOperator.Equals => db.Database.SqlQuery<Guid>(
                $"SELECT n.id AS \"Value\" FROM nodes n WHERE n.properties @> jsonb_build_object({key}::text, {value}::jsonb)"),
            CollectionCriterionOperator.Contains => db.Database.SqlQuery<Guid>(
                $"SELECT n.id AS \"Value\" FROM nodes n WHERE n.properties @> jsonb_build_object({key}::text, jsonb_build_array({value}::jsonb))"),
            _ => db.Database.SqlQuery<Guid>(
                $"SELECT n.id AS \"Value\" FROM nodes n WHERE NOT jsonb_exists(n.properties, {key}::text) OR n.properties -> {key}::text = 'null'::jsonb"),
        };
    }

    /// <summary>What the criteria of these Collections refer to, loaded in one query per kind (DB-022).</summary>
    private async Task<ICollectionReferences> ReferencesOfAsync(IReadOnlyList<Collection> collections, CancellationToken token)
    {
        var criteria = collections.SelectMany(collection => collection.Criteria?.Criteria ?? []).ToList();
        var typeIds = criteria.Where(item => item.TypeId is not null).Select(item => item.TypeId!.Value).Distinct().ToList();
        var tagIds = criteria.Where(item => item.TagId is not null).Select(item => item.TagId!.Value).Distinct().ToList();
        var propertyIds = criteria.Where(item => item.PropertyId is not null).Select(item => item.PropertyId!.Value).Distinct().ToList();
        var nodeIds = criteria.Where(item => item.NodeId is not null).Select(item => item.NodeId!.Value).Distinct().ToList();

        var types = typeIds.Count == 0 ? [] : await db.Set<NodeType>().AsNoTracking().Where(type => typeIds.Contains(type.Id)).Select(type => type.Id).ToListAsync(token);
        var tags = tagIds.Count == 0 ? [] : await db.Set<Tag>().AsNoTracking().Where(tag => tagIds.Contains(tag.Id)).Select(tag => tag.Id).ToListAsync(token);
        var definitions = propertyIds.Count == 0 ? [] : await db.Set<PropertyDefinition>().AsNoTracking().Where(definition => propertyIds.Contains(definition.Id)).ToListAsync(token);
        var nodes = nodeIds.Count == 0 ? [] : await filter.Nodes().Where(node => nodeIds.Contains(node.Id)).Select(node => node.Id).ToListAsync(token);

        return new LoadedCollectionReferences([.. types], [.. tags], definitions.ToDictionary(definition => definition.Id), [.. nodes]);
    }

    private sealed class LoadedCollectionReferences(
        HashSet<Guid> types,
        HashSet<Guid> tags,
        Dictionary<Guid, PropertyDefinition> definitions,
        HashSet<Guid> nodes) : ICollectionReferences
    {
        public bool HasType(Guid typeId) => types.Contains(typeId);

        public bool HasTag(Guid tagId) => tags.Contains(tagId);

        public PropertyDefinition? DefinitionOf(Guid propertyDefinitionId) => definitions.GetValueOrDefault(propertyDefinitionId);

        public bool HasNode(Guid nodeId) => nodes.Contains(nodeId);
    }
}
