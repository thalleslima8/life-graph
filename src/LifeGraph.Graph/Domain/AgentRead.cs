namespace LifeGraph.Graph.Domain;

/// <summary>
/// One read of the graph by an agent, for the read audit (DA-037): which agent, which read,
/// what it asked and which Nodes it got back, never their content. Provenance covers only
/// writes, and a read is when the data leaves for a third-party model.
/// </summary>
public sealed class AgentRead
{
    private AgentRead()
    {
        Arguments = "{}";
        ReturnedNodeIds = [];
    }

    public Guid Id { get; private set; }

    public Guid AccountId { get; private set; }

    public Guid AgentIdentityId { get; private set; }

    public AgentReadOperation Operation { get; private set; }

    /// <summary>What the agent asked, as JSON: the search words, the id or the subject, the depth.</summary>
    public string Arguments { get; private set; }

    /// <summary>Every Node the answer named, in the answer's order.</summary>
    public List<Guid> ReturnedNodeIds { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static AgentRead Record(AgentReadEntry entry, DateTimeOffset now) => new()
    {
        Id = Guid.CreateVersion7(),
        AccountId = entry.AccountId,
        AgentIdentityId = entry.AgentIdentityId,
        Operation = entry.Operation,
        Arguments = entry.Arguments,
        ReturnedNodeIds = [.. entry.ReturnedNodeIds.Distinct()],
        CreatedAt = now,
    };
}

/// <summary>What one agent read records, named at the call site so the two ids never swap.</summary>
public sealed record AgentReadEntry
{
    public required Guid AccountId { get; init; }

    public required Guid AgentIdentityId { get; init; }

    public required AgentReadOperation Operation { get; init; }

    /// <summary>What the agent asked, as JSON.</summary>
    public required string Arguments { get; init; }

    public required IEnumerable<Guid> ReturnedNodeIds { get; init; }
}

/// <summary>The reads an agent makes, named as its MCP tools.</summary>
public enum AgentReadOperation
{
    SearchGraph,
    GetNode,
    GetContext,
    ListTypes,
}
