using LifeGraph.Graph.Contracts;
using LifeGraph.Infrastructure.Errors;
using Limaj.Framework.Core;

namespace LifeGraph.Graph.Application;

/// <summary>
/// The person's choice of operations when approving a proposal (DA-135): which operations
/// each one needs, and whether a choice can be applied at all. The server is the authority:
/// a relation kept without the <c>create_node</c> whose ref it uses is refused, and nothing
/// is applied.
/// </summary>
internal static class ProposalApproval
{
    /// <summary>For each operation, the <c>create_node</c> operations whose ref it uses (as a relation's end or an anchor), by index.</summary>
    public static IReadOnlyList<IReadOnlyList<int>> DependenciesOf(IReadOnlyList<AgentOperation> operations)
    {
        var creators = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var index = 0; index < operations.Count; index++)
        {
            if (operations[index] is AgentCreateNode { Ref: { } reference })
            {
                creators.TryAdd(reference, index);
            }
        }

        return [.. operations.Select(operation => (IReadOnlyList<int>)[.. RefsOf(operation)
            .OfType<string>()
            .Select(reference => creators.TryGetValue(reference, out var creator) ? creator : -1)
            .Where(creator => creator >= 0)
            .Distinct()
            .Order()])];
    }

    // The refs an operation uses: the ends of a relation, or the anchor of a new Node (DA-192).
    private static IEnumerable<string?> RefsOf(AgentOperation operation) => operation switch
    {
        AgentCreateRelation relate => [relate.Source.Ref, relate.Target.Ref],
        AgentCreateNode { Anchor: { } anchor } => [anchor.Target.Ref],
        _ => [],
    };

    /// <summary>
    /// The indexes the person approves (<c>null</c> for all): in range and at least one (a
    /// validation failure), and no operation kept without one it depends on
    /// (<c>agents.operation_dependency</c>, naming the operations kept).
    /// </summary>
    public static Result<IReadOnlyList<int>> Choose(IReadOnlyList<AgentOperation> operations, IReadOnlyList<int>? indexes)
    {
        if (indexes is null)
        {
            return Result<IReadOnlyList<int>>.Ok([.. Enumerable.Range(0, operations.Count)]);
        }

        var chosen = indexes.Distinct().Order().ToList();
        string? problem = chosen.Count == 0
            ? "Choose at least one operation, or reject the proposal."
            : chosen.Any(index => index < 0 || index >= operations.Count)
                ? $"Each must be between 0 and {operations.Count - 1}."
                : null;
        if (problem is not null)
        {
            return Result<IReadOnlyList<int>>.Fail(CommonErrors.ValidationFailed.ToError(
                CommonErrors.ValidationFailedMessage,
                new Dictionary<string, string[]> { [OperationKeys.List] = [problem] }));
        }

        var dependencies = DependenciesOf(operations);
        var chosenSet = chosen.ToHashSet();
        var orphans = chosen
            .Select(index => (Index: index, Missing: dependencies[index].Where(creator => !chosenSet.Contains(creator)).ToList()))
            .Where(orphan => orphan.Missing.Count > 0)
            .ToDictionary(
                orphan => OperationKeys.Of(orphan.Index),
                orphan => new[] { $"It uses the node created by operation {string.Join(", ", orphan.Missing)}, which is left out. Choose that operation too." },
                StringComparer.Ordinal);
        return orphans.Count == 0
            ? Result<IReadOnlyList<int>>.Ok(chosen)
            : Result<IReadOnlyList<int>>.Fail(AgentWriteErrors.OperationDependency.ToError(
                "A chosen operation depends on one that is left out. Nothing was applied.",
                orphans));
    }
}
