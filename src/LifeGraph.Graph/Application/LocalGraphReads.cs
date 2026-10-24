using System.ComponentModel.DataAnnotations;
using LifeGraph.Graph.Contracts;
using LifeGraph.Graph.Domain;
using LifeGraph.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace LifeGraph.Graph.Application;

/// <summary>
/// The local graph the person explores in the 2.5D view (DA-138): the walk of the agents'
/// context (<see cref="ContextWalk"/>, DA-036) from one Node, through the central read filter,
/// following Hard Relations and, from the starting Node only, Soft ones (DA-179). Slim, with no
/// body, and not audited: only an agent's read is (DA-037). The Nodes kept are the best ranked;
/// the edges are every visible Hard Relation among them and the Soft ones of the starting Node,
/// with why each was inferred (DA-128, DA-179). Each Node carries its Hard degree in the
/// whole graph (DA-139), and each Node and Relation whether an agent GraphChangeSet not yet
/// reviewed changed it (DA-141), both counted in batch over what the reader sees.
/// </summary>
internal sealed class LocalGraphReads(LifeGraphDbContext db, GraphReadFilter filter, IOptions<LocalGraphOptions> options)
{
    /// <returns><c>null</c> when the reader does not see the starting Node.</returns>
    public async Task<LocalGraph?> ExpandAsync(GraphReader reader, Guid rootNodeId, int depth, CancellationToken cancellationToken)
    {
        var scope = new ContextScope(rootNodeId, depth, IncludeSoft: true);
        var steps = await ContextWalk.WalkAsync(db, reader, scope, cancellationToken);
        if (steps.Count == 0)
        {
            return null;
        }

        var limits = options.Value;
        var ranked = steps.GroupBy(step => step.NodeId)
            .Select(stepsToNode => stepsToNode.Min(WalkStep.Ranking)!)
            .Order(WalkStep.Ranking)
            .Take(limits.MaxNodes)
            .ToList();
        var nodeIds = ranked.Select(step => step.NodeId).ToList();
        var headings = await filter.Nodes()
            .Where(node => nodeIds.Contains(node.Id))
            .Select(node => new { node.Id, node.Title, node.TypeId })
            .ToDictionaryAsync(node => node.Id, cancellationToken);
        var reach = await ContextWalk.CountReachAsync(db, reader, scope, cancellationToken);

        var among = filter.RelationsWithSoft().Where(relation =>
            nodeIds.Contains(relation.SourceNodeId)
            && nodeIds.Contains(relation.TargetNodeId)
            && (relation.Assertion == RelationAssertion.Hard || relation.SourceNodeId == rootNodeId || relation.TargetNodeId == rootNodeId));
        var edgeCount = await among.CountAsync(cancellationToken);
        var relations = await among.Ranked().Take(limits.MaxEdges).ToListAsync(cancellationToken);

        var degrees = await HardDegreesAsync(nodeIds, cancellationToken);
        var changedByAgents = await ChangedByUnreviewedAgentsAsync(nodeIds, [.. relations.Select(relation => relation.Id)], cancellationToken);

        var nodes = ranked.Select(step =>
        {
            var heading = headings[step.NodeId];
            return new LocalGraphNode(
                step.NodeId,
                heading.Title,
                heading.TypeId,
                step.Distance,
                degrees.GetValueOrDefault(step.NodeId),
                changedByAgents.Contains(step.NodeId));
        }).ToList();
        var edges = relations.Select(relation => new LocalGraphEdge(
            relation.Id,
            relation.SourceNodeId,
            relation.TargetNodeId,
            relation.Kind,
            relation.Assertion,
            relation.Origin,
            relation.Strength,
            changedByAgents.Contains(relation.Id),
            relation.ExplainInference())).ToList();
        var omittedNodes = Math.Max(0, reach - nodes.Count);
        var omittedEdges = edgeCount - edges.Count;
        return new LocalGraph(rootNodeId, nodes, edges, omittedNodes > 0 || omittedEdges > 0, omittedNodes, omittedEdges);
    }

    // Over the whole graph, not the subgraph: a Node keeps its size whatever the depth or the
    // cut (DA-139). A Relation to a Node the reader does not see is not counted.
    private async Task<Dictionary<Guid, int>> HardDegreesAsync(List<Guid> nodeIds, CancellationToken cancellationToken)
    {
        var hard = filter.Relations().Where(relation => relation.Assertion == RelationAssertion.Hard);
        return await hard.Where(relation => nodeIds.Contains(relation.SourceNodeId)).Select(relation => relation.SourceNodeId)
            .Concat(hard.Where(relation => nodeIds.Contains(relation.TargetNodeId)).Select(relation => relation.TargetNodeId))
            .GroupBy(nodeId => nodeId)
            .Select(ends => new { NodeId = ends.Key, Degree = ends.Count() })
            .ToDictionaryAsync(row => row.NodeId, row => row.Degree, cancellationToken);
    }

    // The same "não revisado" as Agent Changes (DA-135), less what an Undo already settled. An
    // edit by the person afterwards does not clear it: editing is not reviewing (DA-141).
    private async Task<HashSet<Guid>> ChangedByUnreviewedAgentsAsync(List<Guid> nodeIds, List<Guid> relationIds, CancellationToken cancellationToken)
    {
        var changed = await filter.ChangeSets()
            .Where(changeSet => changeSet.ActorKind == ChangeActorKind.AgentIdentity
                && changeSet.ReviewedAt == null
                && changeSet.Status == ChangeSetStatus.Applied)
            .SelectMany(changeSet => changeSet.Entries)
            .Where(entry => (entry.EntityKind == GraphEntityKind.Node && nodeIds.Contains(entry.EntityId))
                || (entry.EntityKind == GraphEntityKind.Relation && relationIds.Contains(entry.EntityId)))
            .Select(entry => entry.EntityId)
            .Distinct()
            .ToListAsync(cancellationToken);
        return [.. changed];
    }
}

/// <summary>The depths of the local graph (DA-138).</summary>
public static class LocalGraphLimits
{
    public const int MinDepth = 1;

    public const int MaxDepth = 3;

    public const int DefaultDepth = 2;

    /// <summary>The ceiling no configuration goes past (DA-138).</summary>
    public const int NodesCeiling = 1_000;

    /// <summary>The ceiling no configuration goes past (DA-138).</summary>
    public const int EdgesCeiling = 3_000;
}

/// <summary>
/// The caps of the local graph (DA-138). The default is the load measured at 60 FPS by DA-137
/// (1 000 Nodes / 1 500 edges), under the ceiling of <see cref="LocalGraphLimits"/>.
/// </summary>
public sealed class LocalGraphOptions
{
    public const string SectionName = "Graph:LocalGraph";

    [Range(1, LocalGraphLimits.NodesCeiling)]
    public int MaxNodes { get; set; } = 1_000;

    [Range(1, LocalGraphLimits.EdgesCeiling)]
    public int MaxEdges { get; set; } = 1_500;
}
