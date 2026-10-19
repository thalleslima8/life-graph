using System.ComponentModel;
using System.Text.Json;
using LifeGraph.Graph.Contracts;
using LifeGraph.Infrastructure.Errors;
using Limaj.Framework.Core;

namespace LifeGraph.Agents.Mcp;

/// <summary>
/// One operation of <c>suggest_changes</c>, as the agent sends it: <see cref="Op"/> says which,
/// and only the fields of that kind count. Arguments in camelCase, as every tool's (DA-127).
/// </summary>
public sealed record SuggestedOperation
{
    public const string CreateNode = "create_node";
    public const string UpdateNode = "update_node";
    public const string CreateRelation = "create_relation";
    public const string RemoveRelation = "remove_relation";
    public const string DeleteNode = "delete_node";

    [Description("create_node, update_node, create_relation, remove_relation or delete_node.")]
    public string? Op { get; init; }

    [Description("update_node, delete_node: the Node.")]
    public Guid? NodeId { get; init; }

    [Description("update_node, delete_node: the version you read.")]
    public int? ExpectedVersion { get; init; }

    [Description("create_node, update_node.")]
    public string? Title { get; init; }

    [Description("create_node, update_node: markdown.")]
    public string? Body { get; init; }

    [Description("create_node, update_node: a Type id from list_types.")]
    public Guid? TypeId { get; init; }

    [Description("create_node, update_node: values by property id; a null value removes it.")]
    public Dictionary<Guid, JsonElement>? Properties { get; init; }

    [Description("create_node without Type: the Type you would give it.")]
    public string? SuggestedType { get; init; }

    [Description("create_node: a name for the new Node, for later relations of this proposal.")]
    public string? Ref { get; init; }

    [Description("create_relation: the starting Node's id, or sourceRef.")]
    public Guid? SourceNodeId { get; init; }

    [Description("create_relation: the ref of a Node created earlier in this proposal.")]
    public string? SourceRef { get; init; }

    [Description("create_relation: the target Node's id, or targetRef.")]
    public Guid? TargetNodeId { get; init; }

    [Description("create_relation: the ref of a Node created earlier in this proposal.")]
    public string? TargetRef { get; init; }

    [Description("create_relation: what the link means (related_to, written_by...).")]
    public string? Kind { get; init; }

    [Description("remove_relation: the Relation.")]
    public Guid? RelationId { get; init; }

    /// <summary>The operations, or the fields each one misses, keyed <c>operations[i].field</c>.</summary>
    public static Result<IReadOnlyList<AgentOperation>> ToAgentOperations(IReadOnlyList<SuggestedOperation?>? operations)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var parsed = new List<AgentOperation>();
        var list = operations ?? [];
        for (var index = 0; index < list.Count; index++)
        {
            if (list[index] is not { } operation)
            {
                errors[OperationKeys.Of(index)] = ["An operation is missing."];
                continue;
            }

            void Missing(string field) => errors[OperationKeys.Of(index, field)] = ["Required."];

            switch (operation.Op)
            {
                case CreateNode:
                    if (operation.Title is null)
                    {
                        Missing("title");
                        break;
                    }

                    parsed.Add(new AgentCreateNode(operation.Title, operation.Body, operation.TypeId, operation.Properties, operation.Ref, operation.SuggestedType));
                    break;
                case UpdateNode:
                case DeleteNode:
                    if (operation.NodeId is null)
                    {
                        Missing("nodeId");
                    }

                    if (operation.ExpectedVersion is null)
                    {
                        Missing("expectedVersion");
                    }

                    if (operation.NodeId is null || operation.ExpectedVersion is null)
                    {
                        break;
                    }

                    parsed.Add(operation.Op == DeleteNode
                        ? new AgentDeleteNode(operation.NodeId.Value, operation.ExpectedVersion.Value)
                        : new AgentUpdateNode(operation.NodeId.Value, operation.ExpectedVersion.Value)
                        {
                            Title = operation.Title,
                            Body = operation.Body,
                            Type = operation.TypeId is { } typeId ? new TypeAssignment(typeId) : null,
                            Properties = operation.Properties,
                        });
                    break;
                case CreateRelation:
                    if (operation.Kind is null)
                    {
                        Missing("kind");
                        break;
                    }

                    parsed.Add(new AgentCreateRelation(
                        new NodeRef(operation.SourceNodeId, operation.SourceRef),
                        new NodeRef(operation.TargetNodeId, operation.TargetRef),
                        operation.Kind));
                    break;
                case RemoveRelation:
                    if (operation.RelationId is not { } relationId)
                    {
                        Missing("relationId");
                        break;
                    }

                    parsed.Add(new AgentRemoveRelation(relationId));
                    break;
                default:
                    errors[OperationKeys.Of(index, "op")] = [$"Use {CreateNode}, {UpdateNode}, {CreateRelation}, {RemoveRelation} or {DeleteNode}."];
                    break;
            }
        }

        return errors.Count == 0
            ? Result<IReadOnlyList<AgentOperation>>.Ok(parsed)
            : Result<IReadOnlyList<AgentOperation>>.Fail(CommonErrors.ValidationFailed.ToError(CommonErrors.ValidationFailedMessage, errors));
    }
}
