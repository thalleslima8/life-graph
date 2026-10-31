using System.ComponentModel;
using Limaj.Framework.Core;

namespace LifeGraph.Graph.Contracts;

/// <summary>
/// Reads a link as the address of a Resource: the Resources module owns the rules (DA-056,
/// DA-060, DA-150) and the pipeline asks them whenever a Resource gets a <c>url</c>, so the
/// <c>canonical_url</c> it keeps always follows the link (DA-154). It owns the closed list of
/// <c>resource_kind</c> too, which the pipeline checks on every write of it (DA-056, DA-150).
/// </summary>
public interface IResourceAddressRules
{
    /// <returns>The address, or a validation error that names <paramref name="field"/>.</returns>
    Result<ResourceAddress> Read(string? url, string field);

    /// <summary>The wire names a <c>resource_kind</c> may be written with (DA-056).</summary>
    IReadOnlyList<string> ResourceKinds { get; }
}

/// <param name="Url">The link as given, without surrounding whitespace.</param>
/// <param name="CanonicalUrl">The idempotency key of the capture (DA-060).</param>
/// <param name="ResourceKind">The wire name of the kind the address alone suggests (DA-056).</param>
public sealed record ResourceAddress(string Url, string CanonicalUrl, string ResourceKind, string? Provider);

/// <summary>
/// Captures a link into the Inbox as a Resource (DA-062), idempotent by canonical URL (DA-060,
/// DA-154, DA-156). The pipeline resolves it against the Account in the write's transaction:
/// a new link creates the Resource as <see cref="NodeId"/>; a link already kept answers that
/// Resource, back into the Inbox when it had left it or was deleted; a link of a Node hidden
/// from agents, captured by an agent, changes nothing and names no Node. The Relation to
/// <see cref="RelatedNodeId"/>, when given, is part of the same GraphChangeSet.
/// </summary>
/// <param name="NodeId">The id of the Resource when the link is new.</param>
public sealed record CaptureResource(Guid NodeId, string Url) : GraphOperation
{
    public const string DefaultRelationKind = "related_to";

    /// <summary>The title of a new Resource; without one, the link's host and path until the fetch fills it.</summary>
    public string? Title { get; init; }

    /// <summary>A Node to relate the Resource to (source: the Resource).</summary>
    public Guid? RelatedNodeId { get; init; }

    /// <summary>The kind of that Relation; <see cref="DefaultRelationKind"/> when not given.</summary>
    public string? RelationKind { get; init; }

    /// <summary>The id of that Relation when it is new.</summary>
    public Guid? RelationId { get; init; }
}

/// <summary>What one <see cref="CaptureResource"/> found (DA-156).</summary>
/// <param name="NodeId">The Resource; <c>null</c> in the neutral answer to an agent (DA-154).</param>
/// <param name="Created">The link was new and the Resource was created.</param>
/// <param name="AlreadyExists">The Account already kept the link.</param>
/// <param name="AlreadyInInbox">It was kept in the Inbox already, so nothing changed there.</param>
public sealed record ResourceCaptured(Guid? NodeId, bool Created, bool AlreadyExists, bool AlreadyInInbox)
{
    /// <summary>
    /// Whether the Resource is linked to the Raiz da Conta by Hard Relations once the capture is
    /// stored (DA-191, DA-195): computed for an agent's capture that names the Resource only
    /// (DA-205); absent otherwise, never meaning false.
    /// </summary>
    [Description("Whether the Resource is linked to the account root by Hard Relations once the capture is stored. Computed only for an agent's capture; absent means not computed, never false.")]
    public bool? Anchored { get; init; }
}
