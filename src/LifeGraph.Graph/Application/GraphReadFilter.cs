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
/// wall. Any other principal, or none, sees nothing (a ShareVisitor arrives with E11). The
/// GraphChangeSets and the ChangeProposals are read through it too.
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
    public IQueryable<Node> Nodes() => SeenByReader(db.Set<Node>().AsNoTracking().Where(node => node.DeletedAt == null));

    /// <summary>The live Relations whose two Nodes the reader sees.</summary>
    public IQueryable<Relation> Relations()
    {
        var visibleNodes = Nodes();
        return db.Set<Relation>().AsNoTracking().Where(relation => relation.DeletedAt == null
            && visibleNodes.Any(node => node.Id == relation.SourceNodeId)
            && visibleNodes.Any(node => node.Id == relation.TargetNodeId));
    }

    /// <summary>
    /// The Nodes the reader sees, tombstones included: for an answer that repeats what a
    /// GraphChangeSet recorded, where a delete since keeps the entry and hiding from agents
    /// takes it out (DA-035). A purged Node is gone from it too.
    /// </summary>
    public IQueryable<Node> NodesWithTombstones() => SeenByReader(db.Set<Node>().AsNoTracking());

    /// <summary>The Relations, tombstones included, whose two Nodes the reader sees in <see cref="NodesWithTombstones"/>.</summary>
    public IQueryable<Relation> RelationsWithTombstones()
    {
        var seenNodes = NodesWithTombstones();
        return db.Set<Relation>().AsNoTracking().Where(relation =>
            seenNodes.Any(node => node.Id == relation.SourceNodeId)
            && seenNodes.Any(node => node.Id == relation.TargetNodeId));
    }

    /// <summary>
    /// The Tags the reader sees: the person sees all of them; an AgentIdentity sees a Tag on at
    /// least one live Node it sees, or on no live Node at all, so a Tag only Nodes Oculto para
    /// agentes have does not exist for it (DA-162); anyone else sees none.
    /// </summary>
    public IQueryable<Tag> Tags()
    {
        var tags = db.Set<Tag>().AsNoTracking();
        switch (Reader)
        {
            case GraphReader.Human:
                return tags;
            case GraphReader.AgentIdentity:
                var liveLinks = db.Set<NodeTag>().Where(link => db.Set<Node>().Any(node => node.Id == link.NodeId && node.DeletedAt == null));
                var visibleLinks = NodeTags();
                return tags.Where(tag => !liveLinks.Any(link => link.TagId == tag.Id) || visibleLinks.Any(link => link.TagId == tag.Id));
            default:
                return tags.Where(_ => false);
        }
    }

    /// <summary>
    /// The Collections the reader sees: the person sees all of them; an AgentIdentity or anyone
    /// else sees none, since Collections are the person's navigation (DA-163).
    /// </summary>
    public IQueryable<Collection> Collections()
    {
        var collections = db.Set<Collection>().AsNoTracking();
        return Reader == GraphReader.Human ? collections : collections.Where(_ => false);
    }

    /// <summary>The Tag links of the live Nodes the reader sees (DA-160).</summary>
    public IQueryable<NodeTag> NodeTags()
    {
        var visibleNodes = Nodes();
        return db.Set<NodeTag>().AsNoTracking().Where(link => visibleNodes.Any(node => node.Id == link.NodeId));
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

    /// <summary>The entries of the GraphChangeSets the reader sees (<see cref="ChangeSets"/>).</summary>
    public IQueryable<ChangeEntry> ChangeEntries() => ChangeSets().SelectMany(changeSet => changeSet.Entries);

    /// <summary>
    /// The ChangeProposals the reader sees: the person sees every one of the Account; an
    /// AgentIdentity only its own, so another agent's is not found (DA-043 item 1); any other
    /// principal, or none, sees none. A use case that decides on one asks for it tracked
    /// (<c>AsTracking</c>).
    /// </summary>
    public IQueryable<ChangeProposal> Proposals()
    {
        var proposals = db.Set<ChangeProposal>().AsNoTracking();
        return (Reader, currentPrincipal.Authenticated?.AgentIdentityId) switch
        {
            (GraphReader.Human, _) => proposals,
            (GraphReader.AgentIdentity, { } agentIdentityId) => proposals.Where(proposal => proposal.AgentIdentityId == agentIdentityId),
            _ => proposals.Where(_ => false),
        };
    }

    // What the reader sees among the Nodes: the person all, an agent none Oculto para agentes,
    // by its own flag or its Type's (DA-035), anyone else nothing.
    private IQueryable<Node> SeenByReader(IQueryable<Node> nodes) => Reader switch
    {
        GraphReader.Human => nodes,
        GraphReader.AgentIdentity => nodes.Where(node => !node.HiddenFromAgents
            && !db.Set<NodeType>().Any(type => type.Id == node.TypeId && type.HiddenFromAgents)),
        _ => nodes.Where(_ => false),
    };
}

/// <summary>Who a read is for; the central read filter decides what each one sees.</summary>
internal enum GraphReader
{
    /// <summary>The Human principal: the person who owns the Account.</summary>
    Human,

    /// <summary>An AgentIdentity: never sees what is Oculto para agentes.</summary>
    AgentIdentity,
}
