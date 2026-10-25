using System.Text.Json;
using LifeGraph.Graph.Contracts;
using LifeGraph.Graph.Domain;
using LifeGraph.Graph.Persistence;
using LifeGraph.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LifeGraph.Graph.Application;

/// <summary>
/// The entities one write touches: the stored ones its operations refer to, loaded in a few
/// queries through RLS (DB-022), plus the ones the write creates as it goes. Another
/// Account's rows are simply not there, so they read as not found (DA-104), and so are
/// deleted Nodes and Relations: a write never touches a tombstone (DA-021).
/// </summary>
internal sealed class GraphWorkingSet : ICollectionReferences
{
    private readonly Dictionary<Guid, Node> _nodes;
    private readonly Dictionary<Guid, Relation> _relations;
    private readonly Dictionary<Guid, Relation> _softRelations;
    private readonly Dictionary<Guid, NodeType> _types;
    private readonly Dictionary<Guid, PropertyDefinition> _definitions;
    private readonly Dictionary<Guid, Tag> _tags;
    private readonly Dictionary<Guid, NodeTag> _links;
    private readonly Dictionary<Guid, Collection> _collections;
    private readonly Dictionary<Guid, CollectionMember> _members;
    private readonly HashSet<Guid> _createdIds = [];
    private readonly HashSet<Guid> _removedIds = [];
    private readonly HashSet<string> _typeNames;
    private readonly HashSet<string> _propertyNames;
    private readonly HashSet<string> _tagNameKeys;
    private readonly StoredUsage _storedUsage;
    private readonly HashSet<Guid> _definitionsOverRewriteCap;

    private GraphWorkingSet(
        List<Node> nodes,
        List<Relation> relations,
        List<Relation> softRelations,
        List<NodeType> types,
        List<PropertyDefinition> definitions,
        TagsInWrite tags,
        CollectionsInWrite collections,
        List<string> takenTypeNames,
        List<string> takenPropertyNames,
        StoredUsage storedUsage,
        HashSet<Guid> definitionsOverRewriteCap)
    {
        _definitionsOverRewriteCap = definitionsOverRewriteCap;
        _tags = tags.Tags.ToDictionary(tag => tag.Id);
        _links = tags.Links.ToDictionary(link => link.Id);
        _tagNameKeys = tags.TakenNameKeys.ToHashSet(StringComparer.Ordinal);
        _collections = collections.Collections.ToDictionary(collection => collection.Id);
        _members = collections.Members.ToDictionary(member => member.Id);
        _nodes = nodes.ToDictionary(node => node.Id);
        _relations = relations.ToDictionary(relation => relation.Id);
        _softRelations = softRelations.ToDictionary(relation => relation.Id);
        _types = types.ToDictionary(type => type.Id);
        _definitions = definitions.ToDictionary(definition => definition.Id);
        _typeNames = takenTypeNames.ToHashSet(StringComparer.Ordinal);
        _propertyNames = takenPropertyNames.ToHashSet(StringComparer.Ordinal);
        _storedUsage = storedUsage;
    }

    public static async Task<GraphWorkingSet> LoadAsync(
        LifeGraphDbContext db,
        IReadOnlyList<GraphOperation> operations,
        CancellationToken cancellationToken)
    {
        var createdIds = operations.Select(CreatedId).OfType<Guid>().ToHashSet();

        // An Accept or Ignore names a Soft Relation; its two Nodes come with it (DA-071).
        var softRelationIds = operations.Select(ReferencedSoftRelationId).OfType<Guid>().ToList();
        var softRelations = softRelationIds.Count == 0
            ? []
            : await RelationReadGate.PendingSoftRelations(db).Where(relation => softRelationIds.Contains(relation.Id)).ToListAsync(cancellationToken);

        var nodeIds = operations.SelectMany(ReferencedNodeIds)
            .Concat(CriteriaOf(operations).Select(criterion => criterion.NodeId).OfType<Guid>())
            .Concat(softRelations.SelectMany(relation => new[] { relation.SourceNodeId, relation.TargetNodeId }))
            .Where(id => !createdIds.Contains(id)).Distinct().ToList();
        // A capture brings a Resource back from its tombstone (DA-154): the one write that
        // touches a deleted Node.
        var returningIds = operations.OfType<ReturnToInbox>().Select(back => back.NodeId).ToList();
        var nodes = await db.Set<Node>()
            .Where(node => (nodeIds.Contains(node.Id) && node.DeletedAt == null) || returningIds.Contains(node.Id))
            .ToListAsync(cancellationToken);
        var rewrites = await ValueRewrites.LoadAsync(db, operations, nodes, cancellationToken);

        // A deleted Node takes the Relations that touch it along (DA-021).
        var relationIds = operations.OfType<DeleteRelation>().Select(delete => delete.RelationId).ToList();
        var deletedNodeIds = operations.OfType<DeleteNode>().Select(delete => delete.NodeId).ToList();
        var relations = relationIds.Count == 0 && deletedNodeIds.Count == 0
            ? []
            : await db.Set<Relation>()
                .Where(relation => relation.DeletedAt == null
                    && (relationIds.Contains(relation.Id)
                        || deletedNodeIds.Contains(relation.SourceNodeId)
                        || deletedNodeIds.Contains(relation.TargetNodeId)))
                .ToListAsync(cancellationToken);

        var typeIds = operations.Select(ReferencedTypeId)
            .Concat(CriteriaOf(operations).Select(criterion => criterion.TypeId))
            .Concat(nodes.Select(node => node.TypeId))
            .OfType<Guid>()
            .Where(id => !createdIds.Contains(id))
            .Distinct()
            .ToList();
        var types = await db.Set<NodeType>()
            .Include(type => type.Properties)
            .Where(type => typeIds.Contains(type.Id))
            .ToListAsync(cancellationToken);

        var definitionIds = types.SelectMany(type => type.Properties.Select(property => property.PropertyDefinitionId))
            .Concat(operations.OfType<CreateType>().SelectMany(create => create.PropertyDefinitionIds ?? []))
            .Concat(operations.Select(ReferencedDefinitionId).OfType<Guid>())
            .Concat(CriteriaOf(operations).Select(criterion => criterion.PropertyId).OfType<Guid>())
            .Where(id => !createdIds.Contains(id))
            .Distinct()
            .ToList();
        var definitions = await db.Set<PropertyDefinition>()
            .Where(definition => definitionIds.Contains(definition.Id))
            .ToListAsync(cancellationToken);

        // Names are unique per Account (the unique indexes stay the guard against a race).
        var typeNames = operations.Select(NewTypeName).OfType<string>().ToList();
        var takenTypeNames = typeNames.Count == 0
            ? []
            : await db.Set<NodeType>().Where(type => typeNames.Contains(type.Name)).Select(type => type.Name).ToListAsync(cancellationToken);
        var propertyNames = operations.Select(NewPropertyName).OfType<string>().ToList();
        var takenPropertyNames = propertyNames.Count == 0
            ? []
            : await db.Set<PropertyDefinition>()
                .Where(definition => propertyNames.Contains(definition.Name))
                .Select(definition => definition.Name)
                .ToListAsync(cancellationToken);

        var tags = await TagsInWrite.LoadAsync(db, operations, createdIds, cancellationToken);
        var collections = await CollectionsInWrite.LoadAsync(db, operations, createdIds, cancellationToken);
        var storedUsage = await StoredUsage.LoadAsync(db, operations, nodes, types, definitions, cancellationToken);
        return new GraphWorkingSet(nodes, relations, softRelations, types, definitions, tags, collections, takenTypeNames, takenPropertyNames, storedUsage, rewrites.OverCap);
    }

    /// <summary>
    /// The Nodes holding a value for the Property Definition that the write rewrites (a kind
    /// change, DA-165) or clears (a delete, DA-168), tombstones included, or <c>null</c> when
    /// more than <see cref="GraphLimits.PropertyRewriteMaxNodes"/> hold one.
    /// </summary>
    public IReadOnlyList<Node>? NodesHoldingToRewrite(Guid definitionId) =>
        _definitionsOverRewriteCap.Contains(definitionId)
            ? null
            : [.. _nodes.Values.Where(node => node.Properties.Holds(definitionId)).OrderBy(node => node.Id)];

    /// <summary>Whether an entity with this id already is in the write; creating it twice is a conflict.</summary>
    public bool Knows(Guid id) =>
        _createdIds.Contains(id)
        || _nodes.ContainsKey(id)
        || _relations.ContainsKey(id)
        || _types.ContainsKey(id)
        || _definitions.ContainsKey(id)
        || _tags.ContainsKey(id)
        || _collections.ContainsKey(id);

    /// <summary>A live Node of the write; one deleted by an earlier operation of it is gone.</summary>
    public Node? FindNode(Guid id) => _nodes.GetValueOrDefault(id) is { IsDeleted: false } node ? node : null;

    /// <summary>A Node of the write, a tombstone included: only for <see cref="ReturnToInbox"/>.</summary>
    public Node? FindNodeEvenDeleted(Guid id) => _nodes.GetValueOrDefault(id);

    public Relation? FindRelation(Guid id) => _relations.GetValueOrDefault(id) is { IsDeleted: false } relation ? relation : null;

    /// <summary>
    /// A Soft Relation of the Node the person may still Accept or Ignore: one whose pair has no
    /// live Hard Relation, and that this write has not already taken.
    /// </summary>
    public Relation? FindSoftRelation(Guid nodeId, Guid softRelationId) =>
        _softRelations.GetValueOrDefault(softRelationId) is { } relation && relation.Touches(nodeId) && !_removedIds.Contains(relation.Id)
            ? relation
            : null;

    /// <summary>Whether a live Hard Relation joins the two Nodes, either way, as this write leaves it.</summary>
    public bool HasHardRelationBetween(Guid oneNodeId, Guid otherNodeId) =>
        _relations.Values.Any(relation => !relation.IsDeleted && relation.Touches(oneNodeId) && relation.Touches(otherNodeId));

    public IEnumerable<Relation> LiveRelationsTouching(Guid nodeId) =>
        _relations.Values.Where(relation => !relation.IsDeleted && relation.Touches(nodeId)).OrderBy(relation => relation.Id);

    public PropertyDefinition? FindDefinition(Guid id) =>
        _definitions.GetValueOrDefault(id) is { } definition && !_removedIds.Contains(id) ? definition : null;

    public NodeType? FindType(Guid id) => _types.GetValueOrDefault(id) is { } type && !_removedIds.Contains(id) ? type : null;

    public Tag? FindTag(Guid id) => _tags.GetValueOrDefault(id) is { } tag && !_removedIds.Contains(id) ? tag : null;

    /// <summary>The Node's link to the Tag, as the write left it so far.</summary>
    public NodeTag? FindLink(Guid nodeId, Guid tagId) =>
        _links.Values.FirstOrDefault(link => link.NodeId == nodeId && link.TagId == tagId && !_removedIds.Contains(link.Id));

    /// <summary>Every link of the Tag, a deleted Node's included, in a stable order.</summary>
    public IEnumerable<NodeTag> LinksOf(Guid tagId) =>
        _links.Values.Where(link => link.TagId == tagId && !_removedIds.Contains(link.Id)).OrderBy(link => link.Id);

    public Collection? FindCollection(Guid id) =>
        _collections.GetValueOrDefault(id) is { } collection && !_removedIds.Contains(id) ? collection : null;

    /// <summary>The Collections whose criteria name the Property Definition, in a stable order (DA-171).</summary>
    public IEnumerable<Collection> CollectionsWithCriteriaOn(Guid definitionId) =>
        _collections.Values
            .Where(collection => !_removedIds.Contains(collection.Id)
                && collection.Criteria?.Criteria.Any(criterion => criterion.PropertyId == definitionId) == true)
            .OrderBy(collection => collection.Id);

    /// <summary>The Node's membership in the Collection, as the write left it so far.</summary>
    public CollectionMember? FindMember(Guid collectionId, Guid nodeId) =>
        _members.Values.FirstOrDefault(member => member.CollectionId == collectionId && member.NodeId == nodeId && !_removedIds.Contains(member.Id));

    /// <summary>Every membership of the Collection, a deleted Node's included, in a stable order.</summary>
    public IEnumerable<CollectionMember> MembersOf(Guid collectionId) =>
        _members.Values.Where(member => member.CollectionId == collectionId && !_removedIds.Contains(member.Id)).OrderBy(member => member.Id);

    public bool HasType(Guid typeId) => FindType(typeId) is not null;

    public bool HasTag(Guid tagId) => FindTag(tagId) is not null;

    public PropertyDefinition? DefinitionOf(Guid propertyDefinitionId) => FindDefinition(propertyDefinitionId);

    public bool HasNode(Guid nodeId) => FindNode(nodeId) is not null;

    /// <summary>Takes the folded name for a Tag, unless the Account or this write already uses it (DA-161).</summary>
    public bool TryTakeTagNameKey(string nameKey) => _tagNameKeys.Add(nameKey);

    public void ReleaseTagNameKey(string nameKey) => _tagNameKeys.Remove(nameKey);

    /// <summary>The Type with its definitions, or <c>null</c> when the Type is not in this Account.</summary>
    public TypeSchema? FindSchema(Guid typeId) => FindType(typeId) is { } type ? new TypeSchema(type, _definitions) : null;

    /// <summary>Takes the name for a new Type, unless the Account or this write already uses it.</summary>
    public bool TryTakeTypeName(string name) => _typeNames.Add(name);

    public bool TryTakePropertyName(string name) => _propertyNames.Add(name);

    /// <summary>Frees the name a renamed or removed Type had, for a later operation of the write.</summary>
    public void ReleaseTypeName(string name) => _typeNames.Remove(name);

    public void ReleasePropertyName(string name) => _propertyNames.Remove(name);

    /// <summary>The Nodes that have the Type: the ones of this write as they are now, and the stored ones, tombstones included.</summary>
    public NodeUsage UsageOfType(Guid typeId) =>
        NodeUsage.Of(_nodes.Values.Where(node => node.TypeId == typeId))
            .Plus(_storedUsage.TypesOfOtherNodes.GetValueOrDefault(typeId, NodeUsage.None));

    /// <summary>The Nodes that hold a value for the Property Definition, as <see cref="UsageOfType"/>; only for one the write removes.</summary>
    public NodeUsage UsageOfDefinition(Guid definitionId) =>
        NodeUsage.Of(_nodes.Values.Where(node => node.Properties.Holds(definitionId)))
            .Plus(_storedUsage.NodesHoldingRemovedDefinitions.GetValueOrDefault(definitionId, NodeUsage.None));

    /// <summary>Whether a Type of this write, as it is now, or a stored one attaches the Property Definition.</summary>
    public bool IsAttached(Guid definitionId) =>
        _types.Values.Any(type => !_removedIds.Contains(type.Id) && type.Attaches(definitionId))
        || _storedUsage.DefinitionsAttachedByOtherTypes.Contains(definitionId);

    /// <summary>What Nodes hold for the Property Definition: the ones of this write as they are now, and the stored ones, tombstones included.</summary>
    public PropertyValuesInUse ValuesOf(Guid definitionId)
    {
        var held = _nodes.Values
            .Where(node => node.Properties.Holds(definitionId))
            .Select(node => node.Properties.Values[definitionId])
            .ToList();
        var stored = _storedUsage.ValuesOfOtherNodes.GetValueOrDefault(definitionId);
        var counts = new Dictionary<Guid, int>();
        foreach (var (value, nodeCount) in held.Select(value => (value, 1)).Concat(stored?.ChosenValues ?? []))
        {
            foreach (var optionId in PropertyDefinition.ChosenOptionIds(value))
            {
                counts[optionId] = counts.GetValueOrDefault(optionId) + nodeCount;
            }
        }

        return new PropertyValuesInUse(held.Count > 0 || stored is not null, counts);
    }

    public void Removed(NodeType type)
    {
        _removedIds.Add(type.Id);
        ReleaseTypeName(type.Name);
    }

    public void Removed(PropertyDefinition definition)
    {
        _removedIds.Add(definition.Id);
        ReleasePropertyName(definition.Name);
    }

    public void Removed(Tag tag)
    {
        _removedIds.Add(tag.Id);
        ReleaseTagNameKey(tag.NameKey);
    }

    public void Removed(NodeTag link) => _removedIds.Add(link.Id);

    /// <summary>A Soft Relation an Accept or Ignore took: removed with no tombstone and no entry (DA-070).</summary>
    public void RemovedSoft(Relation softRelation) => _removedIds.Add(softRelation.Id);

    public void Removed(Collection collection) => _removedIds.Add(collection.Id);

    public void Removed(CollectionMember member) => _removedIds.Add(member.Id);

    public void Created(Collection collection) => Track(collection.Id, () => _collections.Add(collection.Id, collection));

    public void Created(CollectionMember member) => Track(member.Id, () => _members.Add(member.Id, member));

    public void Created(Tag tag) => Track(tag.Id, () => _tags.Add(tag.Id, tag));

    public void Created(NodeTag link) => Track(link.Id, () => _links.Add(link.Id, link));

    public void Created(Node node) => Track(node.Id, () => _nodes.Add(node.Id, node));

    public void Created(NodeType type) => Track(type.Id, () => _types.Add(type.Id, type));

    public void Created(PropertyDefinition definition) => Track(definition.Id, () => _definitions.Add(definition.Id, definition));

    public void Created(Relation relation) => Track(relation.Id, () => _relations.Add(relation.Id, relation));

    private void Track(Guid id, Action add)
    {
        _createdIds.Add(id);
        add();
    }

    private static Guid? CreatedId(GraphOperation operation) => operation switch
    {
        CreateNode create => create.NodeId,
        CreateRelation create => create.RelationId,
        AcceptSoftRelation accept => accept.RelationId,
        CreateType create => create.TypeId,
        DefineProperty define => define.PropertyDefinitionId,
        CreateTag create => create.TagId,
        CreateCollection create => create.CollectionId,
        _ => null,
    };

    // The criteria a write sends refer to Types, Tags, Property Definitions and Nodes it must find.
    private static IEnumerable<CollectionCriterion> CriteriaOf(IReadOnlyList<GraphOperation> operations) =>
        operations.Select(operation => operation switch
            {
                CreateCollection create => create.Criteria,
                UpdateCollection update => update.Criteria,
                _ => null,
            })
            .OfType<CollectionCriteria>()
            .SelectMany(criteria => criteria.Criteria ?? []);

    private static IEnumerable<Guid> ReferencedNodeIds(GraphOperation operation) => operation switch
    {
        CreateNode { Anchor: { } anchor } => [anchor.NodeId],
        UpdateNode update => [update.NodeId],
        DeleteNode delete => [delete.NodeId],
        ArchiveNode archive => [archive.NodeId],
        ReturnToInbox back => [back.NodeId],
        CreateRelation create => [create.SourceNodeId, create.TargetNodeId],
        TagNode tag => [tag.NodeId],
        UntagNode untag => [untag.NodeId],
        AddToCollection add => [add.NodeId],
        RemoveFromCollection remove => [remove.NodeId],
        _ => [],
    };

    private static Guid? ReferencedSoftRelationId(GraphOperation operation) => operation switch
    {
        AcceptSoftRelation accept => accept.SoftRelationId,
        IgnoreSoftRelation ignore => ignore.SoftRelationId,
        _ => null,
    };

    private static Guid? ReferencedTypeId(GraphOperation operation) => operation switch
    {
        CreateNode create => create.TypeId,
        UpdateNode update => update.Type?.TypeId,
        RenameType rename => rename.TypeId,
        SetTypeHiddenFromAgents setHidden => setHidden.TypeId,
        AttachProperty attach => attach.TypeId,
        DetachProperty detach => detach.TypeId,
        DeleteType delete => delete.TypeId,
        _ => null,
    };

    private static Guid? ReferencedDefinitionId(GraphOperation operation) => operation switch
    {
        AttachProperty attach => attach.PropertyDefinitionId,
        UpdatePropertyDefinition update => update.PropertyDefinitionId,
        DeletePropertyDefinition delete => delete.PropertyDefinitionId,
        _ => null,
    };

    private static string? NewTypeName(GraphOperation operation) => operation switch
    {
        CreateType create => create.Name?.Trim(),
        RenameType rename => rename.Name?.Trim(),
        _ => null,
    };

    private static string? NewPropertyName(GraphOperation operation) => operation switch
    {
        DefineProperty define => define.Name?.Trim(),
        UpdatePropertyDefinition update => update.Name?.Trim(),
        _ => null,
    };

    /// <summary>
    /// The stored Tags the write refers to, their links it may change (every link of a Tag it
    /// deletes, tombstones included), and the folded names it would take that are taken.
    /// </summary>
    /// <summary>
    /// The stored Collections the write refers to and the memberships it may change: the pair
    /// it adds or removes, and every membership of a Collection it deletes, tombstones included.
    /// A kind change also loads every Collection whose criteria name that Property, so they
    /// follow it in the same transaction (DA-171).
    /// </summary>
    private sealed record CollectionsInWrite(List<Collection> Collections, List<CollectionMember> Members)
    {
        public static async Task<CollectionsInWrite> LoadAsync(
            LifeGraphDbContext db,
            IReadOnlyList<GraphOperation> operations,
            HashSet<Guid> createdIds,
            CancellationToken cancellationToken)
        {
            var collectionIds = operations.Select(ReferencedCollectionId).OfType<Guid>().Where(id => !createdIds.Contains(id)).Distinct().ToList();
            collectionIds = [.. collectionIds.Union(await WithCriteriaOnKindChangesAsync(db, operations, cancellationToken))];
            if (collectionIds.Count == 0)
            {
                return new CollectionsInWrite([], []);
            }

            var collections = await db.Set<Collection>().Where(collection => collectionIds.Contains(collection.Id)).ToListAsync(cancellationToken);
            var memberNodeIds = operations.Select(MemberNodeId).OfType<Guid>().Distinct().ToList();
            var deletedIds = operations.OfType<DeleteCollection>().Select(delete => delete.CollectionId).ToList();
            var members = await db.Set<CollectionMember>()
                .Where(member => (memberNodeIds.Contains(member.NodeId) && collectionIds.Contains(member.CollectionId))
                    || deletedIds.Contains(member.CollectionId))
                .ToListAsync(cancellationToken);
            return new CollectionsInWrite(collections, members);
        }

        // The criteria are stored as the API writes them (camelCase, ids as "D" strings), so
        // containment finds a criterion on the Property whatever its other members are.
        private static async Task<IReadOnlyList<Guid>> WithCriteriaOnKindChangesAsync(
            LifeGraphDbContext db,
            IReadOnlyList<GraphOperation> operations,
            CancellationToken cancellationToken)
        {
            var definitionIds = operations.OfType<UpdatePropertyDefinition>()
                .Where(update => update.ValueKind is not null)
                .Select(update => update.PropertyDefinitionId)
                .Distinct()
                .ToArray();
            if (definitionIds.Length == 0)
            {
                return [];
            }

            return await db.Database.SqlQuery<Guid>(
                    $"""
                    SELECT c.id AS "Value" FROM collections c
                    WHERE EXISTS (
                        SELECT 1 FROM unnest({definitionIds}) AS definition(id)
                        WHERE c.criteria @> jsonb_build_object('criteria', jsonb_build_array(jsonb_build_object('propertyId', definition.id::text))))
                    """)
                .ToListAsync(cancellationToken);
        }

        private static Guid? ReferencedCollectionId(GraphOperation operation) => operation switch
        {
            UpdateCollection update => update.CollectionId,
            DeleteCollection delete => delete.CollectionId,
            AddToCollection add => add.CollectionId,
            RemoveFromCollection remove => remove.CollectionId,
            _ => null,
        };

        private static Guid? MemberNodeId(GraphOperation operation) => operation switch
        {
            AddToCollection add => add.NodeId,
            RemoveFromCollection remove => remove.NodeId,
            _ => null,
        };
    }

    private sealed record TagsInWrite(List<Tag> Tags, List<NodeTag> Links, List<string> TakenNameKeys)
    {
        public static async Task<TagsInWrite> LoadAsync(
            LifeGraphDbContext db,
            IReadOnlyList<GraphOperation> operations,
            HashSet<Guid> createdIds,
            CancellationToken cancellationToken)
        {
            var tagIds = operations.Select(ReferencedTagId).OfType<Guid>().Where(id => !createdIds.Contains(id)).Distinct().ToList();
            var tagIdsToLoad = tagIds.Concat(CriteriaOf(operations).Select(criterion => criterion.TagId).OfType<Guid>()).Distinct().ToList();
            var tags = tagIdsToLoad.Count == 0 ? [] : await db.Set<Tag>().Where(tag => tagIdsToLoad.Contains(tag.Id)).ToListAsync(cancellationToken);

            var linkedNodeIds = operations.Select(LinkedNodeId).OfType<Guid>().Distinct().ToList();
            var deletedTagIds = operations.OfType<DeleteTag>().Select(delete => delete.TagId).ToList();
            var links = linkedNodeIds.Count == 0 && deletedTagIds.Count == 0
                ? []
                : await db.Set<NodeTag>()
                    .Where(link => (linkedNodeIds.Contains(link.NodeId) && tagIds.Contains(link.TagId)) || deletedTagIds.Contains(link.TagId))
                    .ToListAsync(cancellationToken);

            // Names are unique per Account (the unique index stays the guard against a race).
            var nameKeys = operations.Select(NewTagNameKey).OfType<string>().Distinct().ToList();
            var takenNameKeys = nameKeys.Count == 0
                ? []
                : await db.Set<Tag>().Where(tag => nameKeys.Contains(tag.NameKey)).Select(tag => tag.NameKey).ToListAsync(cancellationToken);
            return new TagsInWrite(tags, links, takenNameKeys);
        }

        private static Guid? ReferencedTagId(GraphOperation operation) => operation switch
        {
            RenameTag rename => rename.TagId,
            DeleteTag delete => delete.TagId,
            TagNode tag => tag.TagId,
            UntagNode untag => untag.TagId,
            _ => null,
        };

        private static Guid? LinkedNodeId(GraphOperation operation) => operation switch
        {
            TagNode tag => tag.NodeId,
            UntagNode untag => untag.NodeId,
            _ => null,
        };

        private static string? NewTagNameKey(GraphOperation operation) => operation switch
        {
            CreateTag create => KeyOf(create.Name),
            RenameTag rename => KeyOf(rename.Name),
            _ => null,
        };

        private static string? KeyOf(string? name)
        {
            var errors = new FieldErrors();
            var tagName = TagName.Read(errors, "name", name);
            return errors.IsEmpty ? tagName.Key : null;
        }
    }

    /// <summary>
    /// Loads, tracked and tombstones included, the Nodes holding a value for a Property
    /// Definition whose values the write may rewrite or clear: one whose kind changes (DA-165)
    /// or one deleted with the count the person confirmed (DA-168). Past the cap, none is
    /// loaded and the definition is marked over it.
    /// </summary>
    private sealed record ValueRewrites(HashSet<Guid> OverCap)
    {
        public static async Task<ValueRewrites> LoadAsync(
            LifeGraphDbContext db,
            IReadOnlyList<GraphOperation> operations,
            List<Node> nodes,
            CancellationToken cancellationToken)
        {
            var definitionIds = operations.OfType<UpdatePropertyDefinition>().Where(update => update.ValueKind is not null).Select(update => update.PropertyDefinitionId)
                .Concat(operations.OfType<DeletePropertyDefinition>().Where(delete => delete.ExpectedValueCount is not null).Select(delete => delete.PropertyDefinitionId))
                .Distinct();
            var overCap = new HashSet<Guid>();
            foreach (var definitionId in definitionIds)
            {
                var key = definitionId.ToString();
                var loadedIds = nodes.Select(node => node.Id).ToArray();
                var holdingIds = await db.Database
                    .SqlQuery<Guid>($"SELECT id AS \"Value\" FROM nodes WHERE jsonb_exists(properties, {key}) AND NOT (id = ANY ({loadedIds}))")
                    .OrderBy(id => id)
                    .Take(GraphLimits.PropertyRewriteMaxNodes + 1)
                    .ToListAsync(cancellationToken);
                if (holdingIds.Count + nodes.Count(node => node.Properties.Holds(definitionId)) > GraphLimits.PropertyRewriteMaxNodes)
                {
                    overCap.Add(definitionId);
                    continue;
                }

                var holding = await db.Set<Node>()
                    .Where(node => holdingIds.Contains(node.Id))
                    .OrderBy(node => node.Id)
                    .ToListAsync(cancellationToken);
                nodes.AddRange(holding);
            }

            return new ValueRewrites(overCap);
        }
    }

    /// <summary>
    /// What the stored rows outside the working set hold of the Types and Property Definitions
    /// the write removes or changes, deleted Nodes included: the working set answers for its
    /// own entities as they change, the database for the rest.
    /// </summary>
    private sealed class StoredUsage
    {
        public Dictionary<Guid, NodeUsage> TypesOfOtherNodes { get; private init; } = [];

        public Dictionary<Guid, NodeUsage> NodesHoldingRemovedDefinitions { get; private init; } = [];

        public HashSet<Guid> DefinitionsAttachedByOtherTypes { get; private init; } = [];

        /// <summary>By Property Definition held by some other Node: the distinct values of a Select or MultiSelect with how many Nodes hold each, none for other kinds.</summary>
        public Dictionary<Guid, HeldValues> ValuesOfOtherNodes { get; private init; } = [];

        public static async Task<StoredUsage> LoadAsync(
            LifeGraphDbContext db,
            IReadOnlyList<GraphOperation> operations,
            List<Node> nodes,
            List<NodeType> types,
            List<PropertyDefinition> definitions,
            CancellationToken cancellationToken)
        {
            var loadedNodeIds = nodes.Select(node => node.Id).ToList();
            var loadedTypeIds = types.Select(type => type.Id).ToList();

            var removedTypeIds = operations.OfType<DeleteType>().Select(delete => delete.TypeId).ToList();
            var typesOfOtherNodes = removedTypeIds.Count == 0
                ? []
                : await db.Set<Node>()
                    .Where(node => node.TypeId != null && removedTypeIds.Contains(node.TypeId.Value) && !loadedNodeIds.Contains(node.Id))
                    .GroupBy(node => node.TypeId!.Value)
                    .Select(group => new
                    {
                        TypeId = group.Key,
                        LiveCount = group.Count(node => node.DeletedAt == null),
                        DeletedCount = group.Count(node => node.DeletedAt != null),
                        LastDeletedAt = group.Max(node => node.DeletedAt),
                    })
                    .ToListAsync(cancellationToken);

            var removedDefinitionIds = operations.OfType<DeletePropertyDefinition>().Select(delete => delete.PropertyDefinitionId).ToList();
            var attachedByOtherTypes = removedDefinitionIds.Count == 0
                ? []
                : await db.Set<TypeProperty>()
                    .Where(property => removedDefinitionIds.Contains(property.PropertyDefinitionId) && !loadedTypeIds.Contains(property.TypeId))
                    .Select(property => property.PropertyDefinitionId)
                    .Distinct()
                    .ToListAsync(cancellationToken);

            var checkedDefinitionIds = removedDefinitionIds
                .Concat(operations.OfType<UpdatePropertyDefinition>().Select(update => update.PropertyDefinitionId))
                .Distinct();
            var holdingRemoved = new Dictionary<Guid, NodeUsage>();
            foreach (var definitionId in removedDefinitionIds.Distinct())
            {
                holdingRemoved[definitionId] = await NodesHoldingAsync(db, definitionId, loadedNodeIds, cancellationToken);
            }

            var valuesOfOtherNodes = new Dictionary<Guid, HeldValues>();
            foreach (var definition in definitions.Where(definition => checkedDefinitionIds.Contains(definition.Id)))
            {
                if (await HeldValues.LoadAsync(db, definition, loadedNodeIds, cancellationToken) is { } held)
                {
                    valuesOfOtherNodes[definition.Id] = held;
                }
            }

            return new StoredUsage
            {
                TypesOfOtherNodes = typesOfOtherNodes.ToDictionary(
                    usage => usage.TypeId,
                    usage => new NodeUsage(usage.LiveCount, usage.DeletedCount, usage.LastDeletedAt)),
                NodesHoldingRemovedDefinitions = holdingRemoved,
                DefinitionsAttachedByOtherTypes = [.. attachedByOtherTypes],
                ValuesOfOtherNodes = valuesOfOtherNodes,
            };
        }
    }

    // RLS keeps the query in the Account.
    private static async Task<NodeUsage> NodesHoldingAsync(
        LifeGraphDbContext db,
        Guid definitionId,
        List<Guid> excludedNodeIds,
        CancellationToken cancellationToken)
    {
        var key = definitionId.ToString();
        var excluded = excludedNodeIds.ToArray();
        var holding = await db.Database
            .SqlQuery<StoredHolding>($"""
                SELECT count(*) FILTER (WHERE deleted_at IS NULL)::int AS live_count,
                       count(*) FILTER (WHERE deleted_at IS NOT NULL)::int AS deleted_count,
                       max(deleted_at) AS last_deleted_at
                FROM nodes WHERE jsonb_exists(properties, {key}) AND NOT (id = ANY ({excluded}))
                """)
            .SingleAsync(cancellationToken);
        return new NodeUsage(holding.LiveCount, holding.DeletedCount, holding.LastDeletedAt);
    }

    private sealed record StoredValue(string Value, int NodeCount);

    private sealed record StoredHolding(int LiveCount, int DeletedCount, DateTimeOffset? LastDeletedAt);

    private sealed record HeldValues(IReadOnlyList<(JsonElement Value, int NodeCount)> ChosenValues)
    {
        // RLS keeps the queries in the Account; only a Select or MultiSelect needs the values.
        public static async Task<HeldValues?> LoadAsync(
            LifeGraphDbContext db,
            PropertyDefinition definition,
            List<Guid> excludedNodeIds,
            CancellationToken cancellationToken)
        {
            var key = definition.Id.ToString();
            var excluded = excludedNodeIds.ToArray();
            var anyValue = await db.Database
                .SqlQuery<bool>($"SELECT EXISTS (SELECT 1 FROM nodes WHERE jsonb_exists(properties, {key}) AND NOT (id = ANY ({excluded}))) AS \"Value\"")
                .SingleAsync(cancellationToken);
            if (!anyValue)
            {
                return null;
            }

            if (definition.ValueKind is not (PropertyValueKind.Select or PropertyValueKind.MultiSelect))
            {
                return new HeldValues([]);
            }

            var values = await db.Database
                .SqlQuery<StoredValue>($"""
                    SELECT (properties -> {key})::text AS value, count(*)::int AS node_count
                    FROM nodes WHERE jsonb_exists(properties, {key}) AND NOT (id = ANY ({excluded}))
                    GROUP BY 1
                    """)
                .ToListAsync(cancellationToken);
            return new HeldValues([.. values.Select(stored => (JsonDocument.Parse(stored.Value).RootElement.Clone(), stored.NodeCount))]);
        }
    }
}

/// <summary>How many Nodes use a Type or a Property Definition, live and deleted, and the latest delete among them.</summary>
internal sealed record NodeUsage(int LiveCount, int DeletedCount, DateTimeOffset? LastDeletedAt)
{
    public static readonly NodeUsage None = new(0, 0, null);

    public bool IsInUse => LiveCount + DeletedCount > 0;

    public static NodeUsage Of(IEnumerable<Node> nodes)
    {
        var all = nodes.ToList();
        var deleted = all.Where(node => node.IsDeleted).ToList();
        return new NodeUsage(all.Count - deleted.Count, deleted.Count, deleted.Max(node => node.DeletedAt));
    }

    public NodeUsage Plus(NodeUsage other) => new(
        LiveCount + other.LiveCount,
        DeletedCount + other.DeletedCount,
        Latest(LastDeletedAt, other.LastDeletedAt));

    private static DateTimeOffset? Latest(DateTimeOffset? first, DateTimeOffset? second) =>
        first is null ? second : second is null ? first : first > second ? first : second;
}
