using LifeGraph.Graph.Contracts;
using Limaj.Framework.Core;

namespace LifeGraph.Graph.Domain;

/// <summary>
/// A directed link between two Nodes. <see cref="Assertion"/> (hard or soft) is separate from
/// <see cref="Origin"/> (who originated it): a Relation an agent asserts is Hard with origin
/// agent (DA-014). Soft Relations come only from the system (E9).
/// </summary>
public sealed class Relation
{
    /// <summary>A Hard Relation is asserted at full strength.</summary>
    public const double AssertedStrength = 1.0;

    private Relation()
    {
        Kind = string.Empty;
    }

    public Guid Id { get; private set; }

    public Guid AccountId { get; private set; }

    public Guid SourceNodeId { get; private set; }

    public Guid TargetNodeId { get; private set; }

    /// <summary>What the link means (related_to, written_by...).</summary>
    public string Kind { get; private set; }

    public RelationAssertion Assertion { get; private set; }

    public RelationOrigin Origin { get; private set; }

    /// <summary>How sure the system is of a Soft Relation; <c>null</c> for a Hard one.</summary>
    public double? Confidence { get; private set; }

    public double Strength { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>When the Relation was deleted (a tombstone, DA-021); <c>null</c> while it is live.</summary>
    public DateTimeOffset? DeletedAt { get; private set; }

    public bool IsDeleted => DeletedAt is not null;

    /// <summary>The AgentIdentity that created it while no other principal wrote to it since (DA-133), as on <see cref="Node"/>.</summary>
    public Guid? CreatedByAgentIdentityId { get; private set; }

    public bool Touches(Guid nodeId) => SourceNodeId == nodeId || TargetNodeId == nodeId;

    /// <summary>Anyone but its creating AgentIdentity writing to it makes it the person's for good (DA-133).</summary>
    public void WrittenBy(Guid? writerAgentIdentityId)
    {
        if (CreatedByAgentIdentityId is { } creator && creator != writerAgentIdentityId)
        {
            CreatedByAgentIdentityId = null;
        }
    }

    public static Result<Relation> AssertHard(RelationDraft draft, DateTimeOffset now)
    {
        var (id, accountId, sourceNodeId, targetNodeId, kind, origin, _) = draft;
        var errors = new FieldErrors();
        var trimmedKind = TextInput.RequireName(errors, "kind", kind, GraphLimits.RelationKindMaxLength);
        if (sourceNodeId == targetNodeId)
        {
            errors.Add("targetNodeId", "A Node cannot be related to itself.");
        }

        if (origin == RelationOrigin.System)
        {
            errors.Add("origin", "The system only infers Soft Relations.");
        }

        return errors.ToResult(() => new Relation
        {
            Id = id,
            AccountId = accountId,
            SourceNodeId = sourceNodeId,
            TargetNodeId = targetNodeId,
            Kind = trimmedKind,
            Assertion = RelationAssertion.Hard,
            Origin = origin,
            Confidence = null,
            Strength = AssertedStrength,
            CreatedAt = now,
            UpdatedAt = now,
            CreatedByAgentIdentityId = draft.CreatedByAgentIdentityId,
        });
    }

    /// <returns><c>false</c> when it already was deleted.</returns>
    public bool Delete(DateTimeOffset now)
    {
        if (IsDeleted)
        {
            return false;
        }

        DeletedAt = now;
        UpdatedAt = now;
        return true;
    }

    /// <summary>Puts back the deletion state the history recorded (Undo, DA-020); the rest of a Relation never changes.</summary>
    public void Revert(DateTimeOffset? deletedAt, DateTimeOffset now)
    {
        DeletedAt = deletedAt;
        UpdatedAt = now;
    }
}

/// <summary>What a new Relation asserts: its two Nodes, its kind and who asserted it.</summary>
/// <param name="CreatedByAgentIdentityId">The AgentIdentity creating it, when an agent does (DA-133).</param>
public sealed record RelationDraft(
    Guid Id,
    Guid AccountId,
    Guid SourceNodeId,
    Guid TargetNodeId,
    string? Kind,
    RelationOrigin Origin,
    Guid? CreatedByAgentIdentityId = null);
