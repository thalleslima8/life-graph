using LifeGraph.Graph.Contracts;
using Limaj.Framework.Core;

namespace LifeGraph.Resources.Contracts;

/// <summary>
/// Capture of a link by the current AgentIdentity (DA-156): the link is checked, the capture
/// takes from the Account's capture budget, which the person and every agent share, and goes
/// through the agent write policy (DA-044, DA-046, DA-132), so it is applied as an undoable
/// GraphChangeSet or becomes a proposal. A pending proposal of the same link is answered again.
/// </summary>
public interface IAgentResourceCaptures
{
    Task<Result<ChangeOutcome>> CaptureAsync(AgentResourceCaptureRequest request, CancellationToken cancellationToken);
}

/// <param name="Url">The link, http or https, without credentials (DA-150).</param>
/// <param name="Title">The title of a new Resource; without one, the fetched title fills it (DA-151).</param>
/// <param name="Source">Where the link came from, kept in the Provenance.</param>
/// <param name="RelatedNodeId">A Node to relate the Resource to, in the same GraphChangeSet.</param>
/// <param name="RelationKind">The kind of that Relation; <see cref="CaptureResource.DefaultRelationKind"/> when not given.</param>
public sealed record AgentResourceCaptureRequest(
    string? Url,
    string? Title = null,
    string? Source = null,
    Guid? RelatedNodeId = null,
    string? RelationKind = null);
