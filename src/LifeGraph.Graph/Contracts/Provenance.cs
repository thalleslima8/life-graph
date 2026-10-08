namespace LifeGraph.Graph.Contracts;

/// <summary>
/// Who writes, by which way and from which source (DA-013, DA-112). Every write port call
/// carries it, so no module can create a GraphChangeSet without Provenance.
/// </summary>
/// <param name="Source">Where the content came from (a URL, a file name, a conversation), when there is one. Public to the user, never logged.</param>
public sealed record Provenance(GraphActor Actor, WriteChannel Channel, string? Source = null)
{
    public const int SourceMaxLength = 500;
}

/// <summary>The principal behind a write. A ShareVisitor only reads, so it is not one.</summary>
public abstract record GraphActor
{
    private GraphActor()
    {
    }

    /// <summary>The person who owns the Account.</summary>
    public sealed record Human : GraphActor;

    /// <summary>An authorized agent connection, acting on behalf of the Account.</summary>
    public sealed record AgentIdentity(Guid AgentIdentityId) : GraphActor;
}

/// <summary>The kind of <see cref="GraphActor"/> a GraphChangeSet records.</summary>
public enum ChangeActorKind
{
    Human,
    AgentIdentity,
}

/// <summary>The way the write came in.</summary>
public enum WriteChannel
{
    Ui,
    Api,
    Mcp,
    Import,
}
