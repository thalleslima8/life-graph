namespace LifeGraph.Graph.Contracts;

/// <summary>
/// How error details name the anchor of a new Node (DA-192, DA-200): the field is
/// <c>anchor</c>, a field of it <c>anchor.field</c>. In an agent's write they come after the
/// operation, through <see cref="OperationKeys"/>: <c>operations[i].anchor</c>. Every place that
/// keys an anchor error goes through here, so the API, MCP and the clients agree on the names.
/// </summary>
public static class AnchorKeys
{
    /// <summary>The anchor of a new Node as a whole.</summary>
    public const string Field = "anchor";

    /// <summary>The anchor's target by id.</summary>
    public const string NodeId = Field + ".nodeId";

    /// <summary>The anchor's target by the ref of a Node created earlier in the same write.</summary>
    public const string Ref = Field + ".ref";

    /// <summary>A field of the anchor: <c>anchor.field</c>.</summary>
    public static string Of(string field) => $"{Field}.{field}";
}
