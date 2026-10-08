using LifeGraph.Graph.Domain;
using LifeGraph.Infrastructure.Identity;
using LifeGraph.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LifeGraph.Graph.Application;

/// <summary>
/// The central read filter of the graph: every read starts from it, so what a principal may
/// see is decided in one place. RLS already keeps each read to the caller's Account; this adds
/// what the principal sees inside it. The person sees every live Node. An AgentIdentity never
/// sees a Node Oculto para agentes, by its own flag or its Type's (DA-035): the Node does not
/// exist for it, so a Relation to it is not there either, and a traversal stops at it, as at a
/// wall. Any other principal, or none, sees nothing (a ShareVisitor arrives with E10).
/// </summary>
internal sealed class GraphReadFilter(LifeGraphDbContext db, ICurrentPrincipal currentPrincipal)
{
    /// <summary>
    /// The SQL form of <see cref="Nodes"/>, for the raw queries (search, get_context): a
    /// predicate over a <c>nodes</c> row aliased <c>n</c>, with the parameter
    /// <c>@for_agents</c>, true when the <see cref="Reader"/> is an AgentIdentity (<see cref="ContextWalk"/>
    /// sets it). A <c>NULL</c> parameter matches nothing.
    /// </summary>
    public const string VisibleNodeSql =
        """
        (n.deleted_at IS NULL
         AND @for_agents IS NOT NULL
         AND (NOT @for_agents
              OR (NOT n.hidden_from_agents
                  AND NOT EXISTS (SELECT 1 FROM types ht WHERE ht.id = n.type_id AND ht.hidden_from_agents))))
        """;

    /// <summary>Who reads: <c>null</c> for a principal that sees nothing of the graph.</summary>
    public GraphReader? Reader => currentPrincipal.Authenticated?.Type switch
    {
        PrincipalType.Human => GraphReader.Human,
        PrincipalType.AgentIdentity => GraphReader.AgentIdentity,
        _ => null,
    };

    /// <summary>The live Nodes the reader sees.</summary>
    public IQueryable<Node> Nodes()
    {
        var live = db.Set<Node>().AsNoTracking().Where(node => node.DeletedAt == null);
        return Reader switch
        {
            GraphReader.Human => live,
            GraphReader.AgentIdentity => live.Where(node => !node.HiddenFromAgents
                && !db.Set<NodeType>().Any(type => type.Id == node.TypeId && type.HiddenFromAgents)),
            _ => live.Where(_ => false),
        };
    }

    /// <summary>The live Relations whose two Nodes the reader sees.</summary>
    public IQueryable<Relation> Relations()
    {
        var visibleNodes = Nodes();
        return db.Set<Relation>().AsNoTracking().Where(relation => relation.DeletedAt == null
            && visibleNodes.Any(node => node.Id == relation.SourceNodeId)
            && visibleNodes.Any(node => node.Id == relation.TargetNodeId));
    }

    /// <summary>The Types the reader sees: an agent never sees one that hides its Nodes (DA-039).</summary>
    public IQueryable<NodeType> Types()
    {
        var types = db.Set<NodeType>().AsNoTracking();
        return Reader switch
        {
            GraphReader.Human => types,
            GraphReader.AgentIdentity => types.Where(type => !type.HiddenFromAgents),
            _ => types.Where(_ => false),
        };
    }

    /// <summary>The GraphChangeSets the reader sees: the history is the person's only (E5 shows agents' changes to the person).</summary>
    public IQueryable<GraphChangeSet> ChangeSets()
    {
        var changeSets = db.Set<GraphChangeSet>().AsNoTracking();
        return Reader == GraphReader.Human ? changeSets : changeSets.Where(_ => false);
    }
}

/// <summary>Who a read is for; the central read filter decides what each one sees.</summary>
internal enum GraphReader
{
    /// <summary>The Human principal: the person who owns the Account.</summary>
    Human,

    /// <summary>An AgentIdentity: never sees what is Oculto para agentes.</summary>
    AgentIdentity,
}
