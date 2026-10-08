using LifeGraph.Graph.Contracts;

namespace LifeGraph.Graph.Domain;

/// <summary>
/// The changes one actor made to the graph in one interaction, with their Provenance: the
/// unit that is inspected and undone (DA-013). Each entry keeps the state before and after,
/// so no event sourcing is needed.
/// </summary>
public sealed class GraphChangeSet
{
    private readonly List<ChangeEntry> _entries = [];

    private GraphChangeSet()
    {
    }

    public Guid Id { get; private set; }

    public Guid AccountId { get; private set; }

    public ChangeSetStatus Status { get; private set; }

    public ChangeActorKind ActorKind { get; private set; }

    /// <summary>The agent connection behind the change; <c>null</c> for the Human.</summary>
    public Guid? AgentIdentityId { get; private set; }

    public WriteChannel Channel { get; private set; }

    public string? Source { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>The GraphChangeSet this one undoes (DA-020); <c>null</c> for any other change.</summary>
    public Guid? RevertsChangeSetId { get; private set; }

    public IReadOnlyList<ChangeEntry> Entries => _entries;

    public static GraphChangeSet Applied(Guid accountId, Provenance provenance, DateTimeOffset now) => new()
    {
        Id = Guid.CreateVersion7(now),
        AccountId = accountId,
        Status = ChangeSetStatus.Applied,
        ActorKind = provenance.Actor is GraphActor.AgentIdentity ? ChangeActorKind.AgentIdentity : ChangeActorKind.Human,
        AgentIdentityId = (provenance.Actor as GraphActor.AgentIdentity)?.AgentIdentityId,
        Channel = provenance.Channel,
        Source = string.IsNullOrWhiteSpace(provenance.Source) ? null : provenance.Source.Trim(),
        CreatedAt = now,
    };

    /// <summary>The compensating GraphChangeSet of an Undo: applied, and linked to the one it undoes (DA-020).</summary>
    public static GraphChangeSet Undoing(GraphChangeSet undone, Provenance provenance, DateTimeOffset now)
    {
        var undo = Applied(undone.AccountId, provenance, now);
        undo.RevertsChangeSetId = undone.Id;
        return undo;
    }

    /// <summary>Marks this GraphChangeSet as undone. It stays in the history, with its entries.</summary>
    public void MarkReverted()
    {
        if (Status != ChangeSetStatus.Applied)
        {
            throw new InvalidOperationException("Only an applied GraphChangeSet can be undone.");
        }

        Status = ChangeSetStatus.Reverted;
    }

    /// <summary>
    /// Forgets where the content came from once Purge emptied every entry (DA-114): before
    /// that, the Provenance of the entities still alive needs it.
    /// </summary>
    /// <returns><c>true</c> when it forgot the source now.</returns>
    public bool ForgetSourceIfFullyPurged()
    {
        if (Source is null || _entries.Count == 0 || !_entries.All(entry => entry.IsPurged))
        {
            return false;
        }

        Source = null;
        return true;
    }

    /// <param name="before">The entity's state before, as JSON; <c>null</c> when it was created.</param>
    /// <param name="after">The entity's state after, as JSON; <c>null</c> when it was removed.</param>
    public ChangeEntry Record(GraphEntityKind entityKind, Guid entityId, GraphChangeOperation operation, string? before, string? after)
    {
        var entry = new ChangeEntry(this, _entries.Count, new RecordedChange(entityKind, entityId, operation, before, after));
        _entries.Add(entry);
        return entry;
    }
}

/// <summary>One entity's change inside a GraphChangeSet.</summary>
public sealed class ChangeEntry
{
    private ChangeEntry()
    {
    }

    // The change set gives the Account, the link and the time of its UUIDv7 id (BE-013).
    internal ChangeEntry(GraphChangeSet changeSet, int sequence, RecordedChange change)
    {
        Id = Guid.CreateVersion7(changeSet.CreatedAt);
        AccountId = changeSet.AccountId;
        ChangeSetId = changeSet.Id;
        Sequence = sequence;
        EntityKind = change.EntityKind;
        EntityId = change.EntityId;
        Operation = change.Operation;
        Before = change.Before;
        After = change.After;
    }

    public Guid Id { get; private set; }

    public Guid AccountId { get; private set; }

    public Guid ChangeSetId { get; private set; }

    /// <summary>The order of the change inside its GraphChangeSet.</summary>
    public int Sequence { get; private set; }

    public GraphEntityKind EntityKind { get; private set; }

    public Guid EntityId { get; private set; }

    public GraphChangeOperation Operation { get; private set; }

    public string? Before { get; private set; }

    public string? After { get; private set; }

    /// <summary>
    /// The entry of the root this change came with: a Relation removed because its Node was
    /// deleted points at the Node's entry (DA-115). Restoring the root does not depend on it.
    /// </summary>
    public Guid? CascadeOf { get; private set; }

    public bool IsCascade => CascadeOf is not null;

    /// <summary>
    /// When Purge emptied the entry (DA-021). Only the skeleton stays: which entity, which
    /// operation, in which GraphChangeSet. A purged entry cannot be undone.
    /// </summary>
    public DateTimeOffset? PurgedAt { get; private set; }

    public bool IsPurged => PurgedAt is not null;

    /// <summary>Marks this entry as a consequence of <paramref name="root"/>, an entry of the same GraphChangeSet.</summary>
    public void CascadesFrom(ChangeEntry root)
    {
        if (root.ChangeSetId != ChangeSetId || root.Id == Id)
        {
            throw new InvalidOperationException("A cascade points at another entry of its own GraphChangeSet.");
        }

        CascadeOf = root.Id;
    }

    public void Purge(DateTimeOffset now)
    {
        Before = null;
        After = null;
        PurgedAt ??= now;
    }
}

/// <summary>
/// The states of a GraphChangeSet the code uses so far. The glossary's others (Proposed,
/// Rejected, Expired) arrive with agent proposals (E5).
/// </summary>
public enum ChangeSetStatus
{
    Applied,

    /// <summary>Undone by a later, compensating GraphChangeSet (DA-020).</summary>
    Reverted,
}

/// <summary>What one entry records: the entity, the operation and its states before and after, as JSON.</summary>
internal sealed record RecordedChange(GraphEntityKind EntityKind, Guid EntityId, GraphChangeOperation Operation, string? Before, string? After);
