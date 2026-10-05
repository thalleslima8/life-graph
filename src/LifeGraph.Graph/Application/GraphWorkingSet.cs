using System.Text.Json;
using LifeGraph.Graph.Contracts;
using LifeGraph.Graph.Domain;
using LifeGraph.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LifeGraph.Graph.Application;

/// <summary>
/// The entities one write touches: the stored ones its operations refer to, loaded in a few
/// queries through RLS (DB-022), plus the ones the write creates as it goes. Another
/// Account's rows are simply not there, so they read as not found (DA-104), and so are
/// deleted Nodes and Relations: a write never touches a tombstone (DA-021).
/// </summary>
internal sealed class GraphWorkingSet
{
    private readonly Dictionary<Guid, Node> _nodes;
    private readonly Dictionary<Guid, Relation> _relations;
    private readonly Dictionary<Guid, NodeType> _types;
    private readonly Dictionary<Guid, PropertyDefinition> _definitions;
    private readonly HashSet<Guid> _createdIds = [];
    private readonly HashSet<Guid> _removedIds = [];
    private readonly HashSet<string> _typeNames;
    private readonly HashSet<string> _propertyNames;
    private readonly StoredUsage _storedUsage;

    private GraphWorkingSet(
        List<Node> nodes,
        List<Relation> relations,
        List<NodeType> types,
        List<PropertyDefinition> definitions,
        List<string> takenTypeNames,
        List<string> takenPropertyNames,
        StoredUsage storedUsage)
    {
        _nodes = nodes.ToDictionary(node => node.Id);
        _relations = relations.ToDictionary(relation => relation.Id);
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

        var nodeIds = operations.SelectMany(ReferencedNodeIds).Where(id => !createdIds.Contains(id)).Distinct().ToList();
        var nodes = await db.Set<Node>()
            .Where(node => nodeIds.Contains(node.Id) && node.DeletedAt == null)
            .ToListAsync(cancellationToken);

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

        var storedUsage = await StoredUsage.LoadAsync(db, operations, nodes, types, definitions, cancellationToken);
        return new GraphWorkingSet(nodes, relations, types, definitions, takenTypeNames, takenPropertyNames, storedUsage);
    }

    /// <summary>Whether an entity with this id already is in the write; creating it twice is a conflict.</summary>
    public bool Knows(Guid id) =>
        _createdIds.Contains(id)
        || _nodes.ContainsKey(id)
        || _relations.ContainsKey(id)
        || _types.ContainsKey(id)
        || _definitions.ContainsKey(id);

    /// <summary>A live Node of the write; one deleted by an earlier operation of it is gone.</summary>
    public Node? FindNode(Guid id) => _nodes.GetValueOrDefault(id) is { IsDeleted: false } node ? node : null;

    public Relation? FindRelation(Guid id) => _relations.GetValueOrDefault(id) is { IsDeleted: false } relation ? relation : null;

    public IEnumerable<Relation> LiveRelationsTouching(Guid nodeId) =>
        _relations.Values.Where(relation => !relation.IsDeleted && relation.Touches(nodeId)).OrderBy(relation => relation.Id);

    public PropertyDefinition? FindDefinition(Guid id) =>
        _definitions.GetValueOrDefault(id) is { } definition && !_removedIds.Contains(id) ? definition : null;

    public NodeType? FindType(Guid id) => _types.GetValueOrDefault(id) is { } type && !_removedIds.Contains(id) ? type : null;

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
        var optionIds = held.Concat(stored?.ChosenValues ?? []).SelectMany(ChosenOptionIds).ToHashSet();
        return new PropertyValuesInUse(held.Count > 0 || stored is not null, optionIds);
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
        CreateType create => create.TypeId,
        DefineProperty define => define.PropertyDefinitionId,
        _ => null,
    };

    private static IEnumerable<Guid> ReferencedNodeIds(GraphOperation operation) => operation switch
    {
        UpdateNode update => [update.NodeId],
        DeleteNode delete => [delete.NodeId],
        ArchiveNode archive => [archive.NodeId],
        CreateRelation create => [create.SourceNodeId, create.TargetNodeId],
        _ => [],
    };

    private static Guid? ReferencedTypeId(GraphOperation operation) => operation switch
    {
        CreateNode create => create.TypeId,
        UpdateNode update => update.Type?.TypeId,
        RenameType rename => rename.TypeId,
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

    // A Select value is one option id, a MultiSelect value a list of them; other kinds choose none.
    private static IEnumerable<Guid> ChosenOptionIds(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String when Guid.TryParse(value.GetString(), out var optionId) => [optionId],
        JsonValueKind.Array => value.EnumerateArray()
            .Select(choice => choice.ValueKind == JsonValueKind.String && Guid.TryParse(choice.GetString(), out var id) ? id : (Guid?)null)
            .OfType<Guid>(),
        _ => [],
    };

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

        /// <summary>By Property Definition held by some other Node: the distinct values of a Select or MultiSelect, none for other kinds.</summary>
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

    private sealed record StoredHolding(int LiveCount, int DeletedCount, DateTimeOffset? LastDeletedAt);

    private sealed record HeldValues(IReadOnlyList<JsonElement> ChosenValues)
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
                .SqlQuery<string>($"SELECT DISTINCT (properties -> {key})::text AS \"Value\" FROM nodes WHERE jsonb_exists(properties, {key}) AND NOT (id = ANY ({excluded}))")
                .ToListAsync(cancellationToken);
            return new HeldValues([.. values.Select(value => JsonDocument.Parse(value).RootElement.Clone())]);
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
