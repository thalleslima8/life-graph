using LifeGraph.Graph.Contracts;
using Limaj.Framework.Core;

namespace LifeGraph.Graph.Domain;

/// <summary>
/// A directed link between two Nodes. <see cref="Assertion"/> (hard or soft) is separate from
/// <see cref="Origin"/> (who originated it): a Relation an agent asserts is Hard with origin
/// agent (DA-014). Soft Relations come only from the system, inferred by the Semantic Layer
/// (DA-070, ADR 0011), and say how they were inferred.
/// </summary>
public sealed class Relation
{
    /// <summary>A Hard Relation is asserted at full strength.</summary>
    public const double AssertedStrength = 1.0;

    /// <summary>The kind of every Soft Relation, reserved to the system: similarity has no direction nor meaning of its own (DA-177).</summary>
    public const string SoftKind = "related_to";

    /// <summary>How a Soft Relation is inferred today: the cosine similarity of two Nodes' embeddings (DA-070).</summary>
    public const string EmbeddingSimilarityMethod = "embedding_similarity";

    public const int InferenceMethodMaxLength = 64;

    public const int InferenceModelIdMaxLength = 200;

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

    /// <summary>How a Soft Relation was inferred (its explanation, with the model and the score); <c>null</c> for a Hard one.</summary>
    public string? InferenceMethod { get; private set; }

    /// <summary>The embedding model whose similarity inferred a Soft Relation; <c>null</c> for a Hard one.</summary>
    public string? InferenceModelId { get; private set; }

    public bool Touches(Guid nodeId) => SourceNodeId == nodeId || TargetNodeId == nodeId;

    /// <summary>Why the system inferred it (DA-070); <c>null</c> for a Hard Relation.</summary>
    public SoftRelationExplanation? ExplainInference() =>
        Assertion == RelationAssertion.Soft && InferenceMethod is { } method && InferenceModelId is { } modelId && Confidence is { } score
            ? new SoftRelationExplanation(method, modelId, score)
            : null;

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

    /// <summary>
    /// A Soft Relation between two Nodes whose embeddings by the model are similar (DA-070): the
    /// pair has no order, so the lower id is always the source (DA-177), and the score is both its
    /// confidence and its strength. It never enters a GraphChangeSet.
    /// </summary>
    public static Relation InferSoft(Guid id, Guid accountId, Guid firstNodeId, Guid secondNodeId, string modelId, double score, DateTimeOffset now)
    {
        if (firstNodeId == secondNodeId)
        {
            throw new ArgumentException("A Node cannot be related to itself.", nameof(secondNodeId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);
        var (source, target) = firstNodeId.CompareTo(secondNodeId) < 0 ? (firstNodeId, secondNodeId) : (secondNodeId, firstNodeId);
        var relation = new Relation
        {
            Id = id,
            AccountId = accountId,
            SourceNodeId = source,
            TargetNodeId = target,
            Kind = SoftKind,
            Assertion = RelationAssertion.Soft,
            Origin = RelationOrigin.System,
            InferenceMethod = EmbeddingSimilarityMethod,
            CreatedAt = now,
        };
        relation.Rescore(modelId, score, now);
        return relation;
    }

    /// <summary>The score of a Soft Relation computed again, by the same model or a new one.</summary>
    public void Rescore(string modelId, double score, DateTimeOffset now)
    {
        if (Assertion != RelationAssertion.Soft)
        {
            throw new InvalidOperationException("Only a Soft Relation has a score.");
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(score, 0.0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(score, 1.0);
        InferenceModelId = modelId;
        Confidence = score;
        Strength = score;
        UpdatedAt = now;
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
