using LifeGraph.Graph.Contracts;
using LifeGraph.Graph.Domain;
using LifeGraph.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LifeGraph.Graph.Application;

/// <summary>
/// How much of each plan-limited kind the Account in context holds (E13), counted live from the
/// rows, so there is no counter to drift. Only live rows count, and what the system creates never
/// does (DA-264): the Raiz da Conta (DA-204), the system Types and Property Definitions, Soft
/// Relations and the Hard Relations that touch the root (DA-268). Runs inside the caller's Account transaction, so a check sees the rows its write sees.
/// </summary>
internal sealed class GraphUsage(LifeGraphDbContext db)
{
    public async Task<GraphUsageCounts> CountAsync(CancellationToken cancellationToken)
    {
        var liveNodes = db.Set<Node>().Where(node => node.DeletedAt == null);

        // A Resource is a Node too, so it counts in both (DA-264).
        var nodes = await liveNodes.Where(Node.IsNotAccountRootPredicate).CountAsync(cancellationToken);
        var resources = await liveNodes
            .Where(node => db.Set<NodeType>().Any(type => type.Id == node.TypeId && type.SystemKey == SystemOntology.ResourceTypeKey))
            .CountAsync(cancellationToken);

        // An accepted Soft Relation became a Hard one, so it counts (DA-264). A Hard Relation that
        // touches the Raiz da Conta is system plumbing and does not; an anchor to an ordinary Node
        // counts like any other Relation (DA-268).
        var accountRoots = db.Set<Node>().Where(Node.IsAccountRootPredicate).Select(node => node.Id);
        var relations = await db.Set<Relation>()
            .Where(relation => relation.DeletedAt == null && relation.Assertion == RelationAssertion.Hard)
            .Where(relation => !accountRoots.Contains(relation.SourceNodeId) && !accountRoots.Contains(relation.TargetNodeId))
            .CountAsync(cancellationToken);

        var collections = await db.Set<Collection>().CountAsync(cancellationToken);
        var customTypes = await db.Set<NodeType>().CountAsync(type => type.SystemKey == null, cancellationToken);

        // Archived ones count: every Property Definition of the person can still be deleted
        // (CheckDeletable refuses only system ones), so the limit is never a trap (DA-264).
        var customProperties = await db.Set<PropertyDefinition>().CountAsync(definition => definition.SystemKey == null, cancellationToken);

        return new GraphUsageCounts(nodes, relations, resources, collections, customTypes, customProperties);
    }
}

internal sealed record GraphUsageCounts(int Nodes, int Relations, int Resources, int Collections, int CustomTypes, int CustomProperties);
