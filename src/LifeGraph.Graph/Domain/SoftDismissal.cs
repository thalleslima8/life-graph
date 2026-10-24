namespace LifeGraph.Graph.Domain;

/// <summary>
/// The person's Ignore of a pair of Nodes (DA-071): the pair is never inferred as a Soft
/// Relation again, by this model or any later one. The pair has no order, so the lower id is
/// always <see cref="FirstNodeId"/>.
/// </summary>
public sealed class SoftDismissal
{
    private SoftDismissal()
    {
    }

    public Guid Id { get; private set; }

    public Guid AccountId { get; private set; }

    /// <summary>The lower id of the pair.</summary>
    public Guid FirstNodeId { get; private set; }

    /// <summary>The higher id of the pair.</summary>
    public Guid SecondNodeId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static SoftDismissal Of(Guid id, Guid accountId, Guid oneNodeId, Guid otherNodeId, DateTimeOffset now)
    {
        if (oneNodeId == otherNodeId)
        {
            throw new ArgumentException("A pair needs two Nodes.", nameof(otherNodeId));
        }

        var (first, second) = oneNodeId.CompareTo(otherNodeId) < 0 ? (oneNodeId, otherNodeId) : (otherNodeId, oneNodeId);
        return new SoftDismissal { Id = id, AccountId = accountId, FirstNodeId = first, SecondNodeId = second, CreatedAt = now };
    }
}
