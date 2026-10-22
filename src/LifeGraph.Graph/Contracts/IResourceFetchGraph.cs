using System.Text.Json;
using Limaj.Framework.Core;

namespace LifeGraph.Graph.Contracts;

/// <summary>
/// The graph as the Resource fetch job sees it (DA-152, DA-153): the job's entry of the single
/// write pipeline. The pipeline schedules a fetch, in the transaction of the write, when a
/// Resource gets a new <c>url</c>; an Undo never does. The fetch writes as
/// <see cref="GraphActor.System"/> on <see cref="WriteChannel.System"/>, inside the transaction
/// the job runner opened, so its GraphChangeSet and the job's done mark are stored together.
/// Only the Resource fetch may use it: an architecture test keeps it out of every other type.
/// </summary>
public interface IResourceFetchGraph
{
    /// <summary>The kind of the fetch job, run by the Resources module.</summary>
    public const string JobKind = "resources.fetch";

    /// <summary>
    /// The live Resource with this id, or <c>null</c> when it is gone or no longer a Resource.
    /// It reads in the open transaction, or in a short one of its own when none is open.
    /// </summary>
    Task<ResourceToFetch?> FindAsync(Guid nodeId, CancellationToken cancellationToken);

    /// <summary>
    /// Writes the fetch's result through the pipeline, with its own GraphChangeSet, inside the
    /// open transaction of the current Account; it never opens one, and refuses to run without
    /// one. A failed write leaves nothing tracked.
    /// </summary>
    Task<Result<GraphWriteReceipt>> WriteAsSystemAsync(IReadOnlyList<GraphOperation> operations, CancellationToken cancellationToken);
}

/// <summary>A Resource as the fetch needs it.</summary>
/// <param name="Version">The version to write against (DA-022).</param>
/// <param name="Url">The <c>url</c> value; <c>null</c> when the Resource has none.</param>
/// <param name="PropertyIds">The Property Definition id of each system Property, by its key (<see cref="SystemOntology.ResourcePropertyKeys"/>).</param>
/// <param name="HumanEditedFields">
/// What the person has changed (DA-151): <see cref="TitleField"/>, <see cref="BodyField"/> and
/// system Property keys. The fetch fills only the other fields.
/// </param>
public sealed record ResourceToFetch(
    Guid NodeId,
    int Version,
    string? Url,
    IReadOnlyDictionary<string, Guid> PropertyIds,
    IReadOnlySet<string> HumanEditedFields)
{
    public const string TitleField = "title";

    public const string BodyField = "body";
}

/// <summary>What a fetch job carries: the Resource's id, never its URL (GEN-043, DA-113).</summary>
public sealed record ResourceFetchPayload(Guid NodeId)
{
    public string ToJson() => JsonSerializer.Serialize(this, JsonSerializerOptions.Web);

    public static ResourceFetchPayload Parse(string json) =>
        JsonSerializer.Deserialize<ResourceFetchPayload>(json, JsonSerializerOptions.Web)
        ?? throw new JsonException("Empty fetch payload.");
}
