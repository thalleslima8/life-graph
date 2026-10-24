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

    /// <summary>
    /// The application itself, in a job of the queue (DA-153): the Resource fetch. It writes
    /// only through the job's entry of the pipeline (<see cref="IResourceFetchGraph"/>), never through
    /// <see cref="IGraphWriter"/>, and never makes a Node the person's (DA-133).
    /// </summary>
    public sealed record System : GraphActor;
}

/// <summary>The kind of <see cref="GraphActor"/> a GraphChangeSet records.</summary>
public enum ChangeActorKind
{
    Human,
    AgentIdentity,
    System,
}

/// <summary>The way the write came in.</summary>
public enum WriteChannel
{
    Ui,
    Api,
    Mcp,
    Import,

    /// <summary>A job of the queue, with <see cref="GraphActor.System"/> (DA-153).</summary>
    System,
}
