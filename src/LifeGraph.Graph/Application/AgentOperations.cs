using System.Text.Json;
using System.Text.Json.Serialization;
using LifeGraph.Graph.Contracts;
using LifeGraph.Graph.Domain;

namespace LifeGraph.Graph.Application;

/// <summary>
/// The operations of an agent write as a proposal keeps them, and as the pipeline runs them.
/// The stored form is canonical JSON (no nulls, property values ordered by id), so the same
/// request always has the same <see cref="ChangeProposal.ContentHashOf"/>, base versions
/// included (DA-130, DA-043 item 8). New Nodes get their ids only when applied, so they never
/// weigh in the hash.
/// </summary>
internal static class AgentOperations
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerOptions.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string Serialize(IReadOnlyList<AgentOperation> operations) =>
        JsonSerializer.Serialize(operations.Select(ToStored).ToList(), Options);

    public static IReadOnlyList<AgentOperation> Deserialize(string json) =>
        [.. (JsonSerializer.Deserialize<List<StoredOperation>>(json, Options) ?? []).Select(FromStored)];

    /// <summary>The existing Nodes the operations name, for Purge to find the proposal (DA-021).</summary>
    public static IReadOnlyList<Guid> TargetNodeIds(IReadOnlyList<AgentOperation> operations) =>
        [.. operations.SelectMany(operation => operation switch
        {
            AgentUpdateNode update => [update.NodeId],
            AgentDeleteNode delete => [delete.NodeId],
            AgentCreateRelation relate => new[] { relate.Source.NodeId, relate.Target.NodeId }.OfType<Guid>(),
            _ => [],
        }).Distinct()];

    /// <summary>The name a created Node is answered by: its ref, or its operation's place.</summary>
    public static string CreatedKey(AgentCreateNode create, int index) => create.Ref ?? OperationKeys.Of(index);

    /// <summary>
    /// The pipeline's operations, with an id for each new Node and Relation. A Node an agent
    /// creates without Type goes to the Inbox, with its suggested Type as a hint (DA-049); the
    /// Node drops the hint when it has a Type.
    /// </summary>
    public static (IReadOnlyList<GraphOperation> Operations, IReadOnlyDictionary<string, Guid> CreatedNodes) ToGraphOperations(
        IReadOnlyList<AgentOperation> operations,
        DateTimeOffset now)
    {
        var created = new Dictionary<string, Guid>(StringComparer.Ordinal);
        var graphOperations = new List<GraphOperation>();
        for (var index = 0; index < operations.Count; index++)
        {
            switch (operations[index])
            {
                case AgentCreateNode create:
                    var nodeId = Guid.CreateVersion7(now);
                    created[CreatedKey(create, index)] = nodeId;
                    graphOperations.Add(new CreateNode(
                        nodeId,
                        create.Title,
                        create.Body,
                        create.TypeId,
                        create.Properties,
                        InInbox: create.TypeId is null,
                        SuggestedType: create.SuggestedType));
                    break;
                case AgentUpdateNode update:
                    graphOperations.Add(new UpdateNode(update.NodeId, update.ExpectedVersion)
                    {
                        Title = update.Title,
                        Body = update.Body,
                        Type = update.Type,
                        Properties = update.Properties,
                    });
                    break;
                case AgentCreateRelation relate:
                    graphOperations.Add(new CreateRelation(
                        Guid.CreateVersion7(now),
                        Resolve(relate.Source, created),
                        Resolve(relate.Target, created),
                        relate.Kind));
                    break;
                case AgentRemoveRelation remove:
                    graphOperations.Add(new DeleteRelation(remove.RelationId));
                    break;
                case AgentDeleteNode delete:
                    graphOperations.Add(new DeleteNode(delete.NodeId, delete.ExpectedVersion));
                    break;
                default:
                    throw new NotSupportedException($"Unknown agent operation {operations[index].GetType().Name}.");
            }
        }

        return (graphOperations, created);
    }

    private static Guid Resolve(NodeRef node, Dictionary<string, Guid> created) =>
        node.NodeId ?? created[node.Ref!];

    private static StoredOperation ToStored(AgentOperation operation) => operation switch
    {
        AgentCreateNode create => new StoredOperation(StoredKind.CreateNode)
        {
            Title = create.Title,
            Body = create.Body,
            TypeId = create.TypeId,
            Properties = Ordered(create.Properties),
            Ref = create.Ref,
            SuggestedType = create.SuggestedType,
        },
        AgentUpdateNode update => new StoredOperation(StoredKind.UpdateNode)
        {
            NodeId = update.NodeId,
            ExpectedVersion = update.ExpectedVersion,
            Title = update.Title,
            Body = update.Body,
            Type = update.Type,
            Properties = Ordered(update.Properties),
        },
        AgentCreateRelation relate => new StoredOperation(StoredKind.CreateRelation)
        {
            Source = relate.Source,
            Target = relate.Target,
            Kind = relate.Kind,
        },
        AgentRemoveRelation remove => new StoredOperation(StoredKind.RemoveRelation) { RelationId = remove.RelationId },
        AgentDeleteNode delete => new StoredOperation(StoredKind.DeleteNode) { NodeId = delete.NodeId, ExpectedVersion = delete.ExpectedVersion },
        _ => throw new NotSupportedException($"Unknown agent operation {operation.GetType().Name}."),
    };

    private static AgentOperation FromStored(StoredOperation stored) => stored.Op switch
    {
        StoredKind.CreateNode => new AgentCreateNode(stored.Title!, stored.Body, stored.TypeId, stored.Properties, stored.Ref, stored.SuggestedType),
        StoredKind.UpdateNode => new AgentUpdateNode(stored.NodeId!.Value, stored.ExpectedVersion!.Value)
        {
            Title = stored.Title,
            Body = stored.Body,
            Type = stored.Type,
            Properties = stored.Properties,
        },
        StoredKind.CreateRelation => new AgentCreateRelation(stored.Source!, stored.Target!, stored.Kind!),
        StoredKind.RemoveRelation => new AgentRemoveRelation(stored.RelationId!.Value),
        StoredKind.DeleteNode => new AgentDeleteNode(stored.NodeId!.Value, stored.ExpectedVersion!.Value),
        _ => throw new NotSupportedException($"Unknown stored operation {stored.Op}."),
    };

    private static SortedDictionary<Guid, JsonElement>? Ordered(IReadOnlyDictionary<Guid, JsonElement>? values) =>
        values is null ? null : new SortedDictionary<Guid, JsonElement>(values.ToDictionary(pair => pair.Key, pair => pair.Value.Clone()));

    [JsonConverter(typeof(JsonStringEnumConverter<StoredKind>))]
    internal enum StoredKind
    {
        CreateNode,
        UpdateNode,
        CreateRelation,
        RemoveRelation,
        DeleteNode,
    }

    internal sealed record StoredOperation(StoredKind Op)
    {
        public Guid? NodeId { get; init; }

        public int? ExpectedVersion { get; init; }

        public string? Title { get; init; }

        public string? Body { get; init; }

        public Guid? TypeId { get; init; }

        public TypeAssignment? Type { get; init; }

        public IReadOnlyDictionary<Guid, JsonElement>? Properties { get; init; }

        public string? Ref { get; init; }

        public string? SuggestedType { get; init; }

        public NodeRef? Source { get; init; }

        public NodeRef? Target { get; init; }

        public string? Kind { get; init; }

        public Guid? RelationId { get; init; }
    }
}
