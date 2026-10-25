using LifeGraph.Graph.Contracts;
using LifeGraph.Graph.Domain;
using LifeGraph.Infrastructure.Persistence;
using Limaj.Framework.Core;
using Microsoft.EntityFrameworkCore;

namespace LifeGraph.Graph.Application;

/// <summary>
/// The anchor rule of an AgentIdentity's write (DA-192, DA-199, DA-200): every Node the write
/// creates must end up reached from the Raiz da Conta. A new Node counts as anchored by its
/// <c>anchor</c> or by any Hard Relation of the same write, in either direction, to a Node that
/// already is anchored, or to another new Node of the write that is. The reach of the Nodes that
/// already exist comes from <see cref="AnchorReach"/>, over the real graph without what the write
/// deletes: a Node or a Relation the write deletes anchors nothing and bridges nothing (DA-200).
/// A capture is not a <see cref="CreateNode"/> of the write and is left out: its Resource goes to
/// the Inbox (DA-200). The rule lives in the write pipeline, so it runs
/// on the agent's write, its proposal, <c>apply_changes</c> and the person's approval alike
/// (DA-199).
/// </summary>
internal static class AnchorRule
{
    private const string LooseNodeMessage = "This new node is not anchored: pass `" + AnchorKeys.Field + "`, or relate it in this write to a node that is.";

    /// <summary>
    /// Refuses the write when one of its new Nodes would be loose: <c>graph.anchor_required</c>,
    /// whose message names the root's id and whose details name each loose operation, or
    /// <c>graph.root_missing</c> when the Account has no root (DA-202). Indexes are those of
    /// <paramref name="operations"/>.
    /// </summary>
    public static async Task<Result> CheckAsync(LifeGraphDbContext db, IReadOnlyList<GraphOperation> operations, CancellationToken cancellationToken)
    {
        if (!operations.Any(Requires))
        {
            return Result.Ok();
        }

        var (deletedNodes, deletedRelations) = DeletedBy(operations);
        var anchored = await AnchorReach.AnchoredAmongWithoutAsync(db, ExistingEnds(operations), deletedNodes, deletedRelations, cancellationToken);
        var loose = LooseOperations(operations, anchored);
        if (loose.Count == 0)
        {
            return Result.Ok();
        }

        var rootId = await db.Set<Node>().AsNoTracking()
            .Where(Node.IsAccountRootPredicate)
            .Where(node => node.DeletedAt == null)
            .Select(node => (Guid?)node.Id)
            .SingleOrDefaultAsync(cancellationToken);
        if (rootId is not { } accountRootId)
        {
            return Result.Fail(GraphErrors.RootMissing.ToError(GraphContextReads.RootMissingMessage));
        }

        var details = loose.ToDictionary(index => OperationKeys.Of(index, AnchorKeys.Field), _ => new[] { LooseNodeMessage }, StringComparer.Ordinal);
        return Result.Fail(GraphErrors.AnchorRequired.ToError(AnchorRequiredMessage(accountRootId), details));
    }

    /// <summary>How to fix a loose Node, with the id of the root to anchor at when nothing more specific fits (DA-192).</summary>
    public static string AnchorRequiredMessage(Guid accountRootId) =>
        $"Every node you create needs an anchor. Search for the most specific node it belongs under and pass its id in `{AnchorKeys.Field}`;"
        + " if the subject has no node yet, create that node first, anchored, and anchor this one under it."
        + $" To anchor at the account root, pass {AnchorKeys.NodeId} {accountRootId}. Nothing was applied.";

    /// <summary>
    /// The Nodes the write names that it does not create: the ends of its anchors and Relations,
    /// whose reach of the root <see cref="LooseOperations"/> needs.
    /// </summary>
    public static IReadOnlyList<Guid> ExistingEnds(IReadOnlyList<GraphOperation> operations)
    {
        var created = CreatedNodeIds(operations);
        return [.. Links(operations).SelectMany(link => new[] { link.From, link.To }).Where(id => !created.Contains(id)).Distinct()];
    }

    /// <summary>
    /// The Nodes and Relations the write deletes, which the reach of the Nodes that exist leaves
    /// out: the graph the new Nodes hang from is the one after the write.
    /// </summary>
    public static (IReadOnlyList<Guid> Nodes, IReadOnlyList<Guid> Relations) DeletedBy(IReadOnlyList<GraphOperation> operations) =>
        ([.. operations.OfType<DeleteNode>().Select(delete => delete.NodeId).Distinct()],
         [.. operations.OfType<DeleteRelation>().Select(delete => delete.RelationId).Distinct()]);

    /// <summary>
    /// The indexes of the <see cref="CreateNode"/> operations the rule asks an anchor of that the
    /// write leaves loose, given which of its <see cref="ExistingEnds"/> are anchored now.
    /// </summary>
    public static IReadOnlyList<int> LooseOperations(IReadOnlyList<GraphOperation> operations, IReadOnlySet<Guid> anchoredExisting)
    {
        var deleted = operations.OfType<DeleteNode>().Select(delete => delete.NodeId).ToHashSet();
        var neighbours = new Dictionary<Guid, List<Guid>>();
        foreach (var (from, to) in Links(operations).Where(link => !deleted.Contains(link.From) && !deleted.Contains(link.To)))
        {
            NeighboursOf(neighbours, from).Add(to);
            NeighboursOf(neighbours, to).Add(from);
        }

        var reached = anchoredExisting.Where(id => !deleted.Contains(id)).ToHashSet();
        var pending = new Queue<Guid>(reached);
        while (pending.TryDequeue(out var current))
        {
            foreach (var next in neighbours.GetValueOrDefault(current) ?? [])
            {
                if (reached.Add(next))
                {
                    pending.Enqueue(next);
                }
            }
        }

        return [.. Enumerable.Range(0, operations.Count)
            .Where(index => Requires(operations[index]) && !reached.Contains(((CreateNode)operations[index]).NodeId))];
    }

    // A Node of the write the rule asks an anchor of; the root itself and a capture's Resource are not.
    private static bool Requires(GraphOperation operation) =>
        operation is CreateNode { MakesAccountRoot: false, MadeByCapture: false };

    private static HashSet<Guid> CreatedNodeIds(IReadOnlyList<GraphOperation> operations) =>
        operations.OfType<CreateNode>().Select(create => create.NodeId).ToHashSet();

    // The Hard Relations the write creates, an anchor among them; the direction does not count (DA-191).
    private static IEnumerable<(Guid From, Guid To)> Links(IReadOnlyList<GraphOperation> operations) =>
        operations.SelectMany(operation => operation switch
        {
            CreateNode { Anchor: { } anchor } create => [(create.NodeId, anchor.NodeId)],
            CreateRelation relate => new[] { (relate.SourceNodeId, relate.TargetNodeId) },
            _ => [],
        });

    private static List<Guid> NeighboursOf(Dictionary<Guid, List<Guid>> neighbours, Guid nodeId)
    {
        if (!neighbours.TryGetValue(nodeId, out var list))
        {
            list = [];
            neighbours[nodeId] = list;
        }

        return list;
    }
}
