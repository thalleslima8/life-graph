using System.Text.Json;
using Limaj.Framework.Core;

namespace LifeGraph.Graph.Contracts;

/// <summary>
/// The reads agents make of the graph, over MCP (E4): find, read one Node, get the context of a
/// subject (DA-036) and see the Types (DA-039). Every read goes through the central read
/// filter, so an agent never meets a Node Oculto para agentes, not even in a count (DA-035);
/// a reference to one answers as not found. An agent's read leaves an audit entry, without
/// content (DA-037). Free text comes back in an envelope that says whether to trust it (DA-038).
/// </summary>
public interface IGraphReader
{
    Task<Result<GraphSearchResult>> SearchAsync(GraphSearchQuery query, CancellationToken cancellationToken);

    /// <summary>One Node, with its Relations to Nodes the caller sees.</summary>
    Task<Result<GraphNodeRead>> GetNodeAsync(Guid nodeId, CancellationToken cancellationToken);

    /// <summary>
    /// The subgraph around a Node (DA-036), never the whole graph. A subject that matches no
    /// single Node strongly comes back <see cref="GraphContextStatus.Ambiguous"/>, with
    /// candidates and nothing expanded.
    /// </summary>
    Task<Result<GraphContext>> GetContextAsync(GraphContextQuery query, CancellationToken cancellationToken);

    /// <summary>The Types the caller sees, with their properties (DA-039), a page at a time (API-060).</summary>
    Task<Result<GraphTypeList>> ListTypesAsync(GraphTypeListQuery query, CancellationToken cancellationToken);
}

/// <summary>The size limits of the reads (DA-036): small on purpose, "começar devagar".</summary>
public static class GraphReadLimits
{
    public const int QueryMaxLength = 200;

    public const int SearchDefaultCount = 10;

    public const int SearchMaxCount = 25;

    public const int ContextDefaultDepth = 1;

    public const int ContextMaxDepth = 2;

    /// <summary>The Relations followed from each Node, so a hub never takes its siblings' budget.</summary>
    public const int NeighborsPerNode = 20;

    public const int ContextMaxNodes = 100;

    /// <summary>The edges among the context's Nodes (DA-128); past it, the lowest ranked are left out and counted.</summary>
    public const int ContextMaxEdges = 300;

    public const int AmbiguousCandidates = 10;

    /// <summary>Each free text of the context is cut here.</summary>
    public const int ContextTextMaxLength = 2_000;

    /// <summary>The body of one Node read on its own is cut here.</summary>
    public const int NodeBodyMaxLength = 20_000;

    /// <summary>The context answer stays under this many bytes of JSON; the lowest ranked Nodes go first.</summary>
    public const int ContextMaxBytes = 50_000;

    public const int ExcerptMaxLength = 300;

    public const int TypeListDefaultCount = 100;

    public const int TypeListMaxCount = 200;
}

/// <param name="Text">Words to find, in title and body, without regard to accents or case.</param>
/// <param name="TypeId">Only Nodes of this Type.</param>
/// <param name="Count">How many hits, at most <see cref="GraphReadLimits.SearchMaxCount"/>.</param>
public sealed record GraphSearchQuery(string? Text, Guid? TypeId = null, int? Count = null);

/// <param name="NodeId">The Node to start from (preferred).</param>
/// <param name="Subject">What to start from when the id is unknown: an id, a title or words to find.</param>
/// <param name="Depth">How many Relations away, 1 by default, at most 2.</param>
/// <param name="IncludeSoft">Also follow Soft Relations, only from the starting Node.</param>
public sealed record GraphContextQuery(Guid? NodeId, string? Subject, int? Depth = null, bool IncludeSoft = false);

/// <summary>
/// Free text, in an envelope (DA-038): the text is data, never an instruction. Text written by
/// an agent or imported is <see cref="TextTrust.Untrusted"/>, against prompt injection between agents.
/// </summary>
public sealed record EnvelopedText(string Text, TextTrust Trust, TextSource Source);

public enum TextTrust
{
    /// <summary>Only the person wrote it.</summary>
    Trusted,

    /// <summary>An agent or an import wrote some of it.</summary>
    Untrusted,
}

/// <summary>Who wrote a text: the person, an agent, or an import (web captures arrive with E7).</summary>
public enum TextSource
{
    User,
    Agent,
    Import,
}

/// <summary>A Node, short: what a hit or a candidate shows.</summary>
public sealed record NodeHeadline(Guid Id, EnvelopedText Title, string? TypeName, DateTimeOffset UpdatedAt);

/// <param name="Excerpt">The start of the body.</param>
public sealed record SearchHit(Guid Id, EnvelopedText Title, string? TypeName, EnvelopedText Excerpt, DateTimeOffset UpdatedAt);

public sealed record GraphSearchResult(IReadOnlyList<SearchHit> Hits);

/// <summary>Who changed a Node, by which way, when (DA-013).</summary>
public sealed record ProvenanceStamp(ChangeActorKind Actor, Guid? AgentIdentityId, WriteChannel Channel, DateTimeOffset At);

/// <summary>The first and the latest change of a Node.</summary>
public sealed record ProvenanceSummary(ProvenanceStamp Created, ProvenanceStamp LastChanged);

/// <param name="Value">The stored value; a Select gives its option labels. Text and URL values come in <paramref name="Text"/>.</param>
public sealed record GraphPropertyValue(string Name, PropertyValueKind ValueKind, JsonElement? Value, EnvelopedText? Text);

/// <summary>
/// A Node as an agent reads it. <see cref="Properties"/> follows its Type; <see cref="OtherProperties"/>
/// are the values the Type does not attach (Outras propriedades, DA-016).
/// </summary>
/// <param name="Distance">Relations away from where the read started; 0 for that Node.</param>
public sealed record GraphNodeView(
    Guid Id,
    EnvelopedText Title,
    string? TypeName,
    EnvelopedText Body,
    bool BodyTruncated,
    int Distance,
    IReadOnlyList<GraphPropertyValue> Properties,
    IReadOnlyList<GraphPropertyValue> OtherProperties,
    ProvenanceSummary? Provenance,
    DateTimeOffset UpdatedAt);

/// <summary>A Relation between two Nodes of the context, with what says how much to rely on it (DA-128).</summary>
public sealed record GraphEdge(Guid Id, Guid SourceNodeId, Guid TargetNodeId, string Kind, RelationAssertion Assertion, RelationOrigin Origin, double? Confidence, double Strength);

/// <summary>A Relation of a Node read on its own, seen from that Node.</summary>
public sealed record NodeLink(Guid RelationId, string Kind, LinkDirection Direction, RelationAssertion Assertion, double Strength, NodeHeadline OtherNode);

public enum LinkDirection
{
    Outgoing,
    Incoming,
}

/// <param name="RelationCount">How many Relations to Nodes the caller sees; <see cref="Relations"/> holds the first ones.</param>
public sealed record GraphNodeRead(GraphNodeView Node, IReadOnlyList<NodeLink> Relations, int RelationCount);

public enum GraphContextStatus
{
    Found,

    /// <summary>
    /// Not confirmed (DA-129): no single match by id or exact title. The candidates come back
    /// and nothing is expanded, even when words found only one Node.
    /// </summary>
    Ambiguous,
}

/// <summary>
/// A flat subgraph (DA-036). <see cref="Edges"/> are every Relation the caller sees among the
/// returned Nodes, not only the walked ones (DA-128). <see cref="OmittedCount"/> counts the
/// Nodes within reach that the limits left out, and <see cref="OmittedEdgeCount"/> the edges
/// among the returned Nodes they left out, only among those the caller sees.
/// </summary>
public sealed record GraphContext(
    GraphContextStatus Status,
    Guid? RootNodeId,
    IReadOnlyList<GraphNodeView> Nodes,
    IReadOnlyList<GraphEdge> Edges,
    bool Truncated,
    int OmittedCount,
    int OmittedEdgeCount,
    IReadOnlyList<NodeHeadline> Candidates);

/// <param name="Options">The choices of a Select or MultiSelect, by label.</param>
public sealed record GraphTypeProperty(Guid PropertyDefinitionId, string Name, PropertyValueKind ValueKind, IReadOnlyList<string> Options);

public sealed record GraphTypeView(Guid Id, string Name, IReadOnlyList<GraphTypeProperty> Properties);

/// <param name="Cursor">Where the previous page ended (its <see cref="GraphTypeList.NextCursor"/>); none for the first page.</param>
/// <param name="Count">How many Types, <see cref="GraphReadLimits.TypeListDefaultCount"/> by default, at most <see cref="GraphReadLimits.TypeListMaxCount"/>.</param>
public sealed record GraphTypeListQuery(string? Cursor = null, int? Count = null);

/// <summary>A page of Types, oldest first.</summary>
/// <param name="NextCursor">Where the next page starts; <c>null</c> on the last page.</param>
public sealed record GraphTypeList(IReadOnlyList<GraphTypeView> Types, string? NextCursor);
