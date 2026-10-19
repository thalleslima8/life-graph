using System.Linq.Expressions;
using System.Security.Cryptography;
using System.Text;
using LifeGraph.Graph.Contracts;

namespace LifeGraph.Graph.Domain;

/// <summary>
/// The ChangeProposal of an agent (E5, DA-136): the operations it asked for, kept until the
/// person (or, when allowed, the agent itself) applies them, the person rejects them, the agent
/// withdraws them or they expire (DA-048). It holds operations, not recorded changes: what they
/// change is only known when they are applied, against the graph as it is then. Applied, it
/// becomes an ordinary GraphChangeSet, which it points to.
/// </summary>
public sealed class ChangeProposal
{
    public const int IdempotencyKeyMaxLength = 200;

    /// <summary>The length of <see cref="ContentHash"/>: a SHA-256 in hex, two characters a byte.</summary>
    public const int ContentHashLength = SHA256.HashSizeInBytes * 2;

    /// <summary>The field behind <see cref="Reasons"/>, which the persistence maps by name.</summary>
    internal const string ReasonsField = nameof(_reasons);

    /// <summary>The field behind <see cref="TargetNodeIds"/>, which the persistence maps and queries by name.</summary>
    internal const string TargetNodeIdsField = nameof(_targetNodeIds);

    /// <summary>The field behind <see cref="RejectedOperations"/>, which the persistence maps by name.</summary>
    internal const string RejectedOperationsField = nameof(_rejectedOperations);

    /// <summary>The field behind <see cref="CreatedNodes"/>, which the persistence maps by name.</summary>
    internal const string CreatedNodesField = nameof(_createdNodes);

    private List<ProposalReason> _reasons = [];
    private List<Guid> _targetNodeIds = [];
    private List<int> _rejectedOperations = [];
    private Dictionary<string, Guid> _createdNodes = new(StringComparer.Ordinal);

    private ChangeProposal()
    {
        Operations = "[]";
        ContentHash = string.Empty;
    }

    public Guid Id { get; private set; }

    public Guid AccountId { get; private set; }

    public Guid AgentIdentityId { get; private set; }

    /// <summary>The stored status; a pending one past <see cref="ExpiresAt"/> reads as expired (<see cref="StatusAt"/>).</summary>
    public ProposalStatus Status { get; private set; }

    /// <summary>The operations, as canonical JSON; emptied when Purge removes an entity they name.</summary>
    public string Operations { get; private set; }

    public int OperationCount { get; private set; }

    /// <summary>
    /// The hash of the canonical operations, base versions included: the same request from the
    /// same agent finds the pending proposal (DA-130), and content the person rejected is
    /// recognized when sent again (DA-043 item 8).
    /// </summary>
    public string ContentHash { get; private set; }

    /// <summary>Only the person can approve it, for good (DA-043 items 3 and 4): it never changes once set.</summary>
    public bool RequiresUser { get; private set; }

    public IReadOnlyList<ProposalReason> Reasons => _reasons;

    /// <summary>
    /// The Nodes whose content the operations hold, what Purge looks for (DA-021): the existing
    /// Nodes they name and, once applied, the Nodes they created.
    /// </summary>
    public IReadOnlyList<Guid> TargetNodeIds => _targetNodeIds;

    /// <summary>The operations the person left out when approving part of it.</summary>
    public IReadOnlyList<int> RejectedOperations => _rejectedOperations;

    /// <summary>
    /// The Nodes the agent's own application created, by the name its answer gave them (their
    /// <see cref="AgentCreateNode.Ref"/>, or their operation's place), so a retry with its key
    /// names them again (DA-043 item 7). Kept when Purge empties the operations, less the name of
    /// a Node purged itself: a name is the new Node's own, not the content of another.
    /// </summary>
    public IReadOnlyDictionary<string, Guid> CreatedNodes => _createdNodes;

    public string? Source { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    /// <summary>The last change of the row: a decision, the person-only mark or a redaction (DB-007).</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    public DateTimeOffset? DecidedAt { get; private set; }

    /// <summary>The GraphChangeSet it became.</summary>
    public Guid? ChangeSetId { get; private set; }

    /// <summary>
    /// The key of the agent's <c>apply_changes</c> that settled its answer (DA-043 item 7): the
    /// agent applied it, or found it needs the person. Applying again with it answers the same
    /// (<see cref="AnswerTo"/>), even after the person decided.
    /// </summary>
    public string? IdempotencyKey { get; private set; }

    /// <summary>When Purge emptied the operations (DA-048).</summary>
    public DateTimeOffset? RedactedAt { get; private set; }

    /// <summary>The <see cref="ContentHash"/> of operations in their canonical form: their SHA-256, in hex.</summary>
    public static string ContentHashOf(string canonicalOperations) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalOperations)));

    public static ChangeProposal Propose(ProposalDraft draft, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(draft);
        return new ChangeProposal
        {
            Id = Guid.CreateVersion7(now),
            AccountId = draft.AccountId,
            AgentIdentityId = draft.AgentIdentityId,
            Status = ProposalStatus.Proposed,
            Operations = draft.Operations,
            OperationCount = draft.OperationCount,
            ContentHash = draft.ContentHash,
            RequiresUser = draft.RequiresUser,
            _reasons = [.. draft.Reasons.Distinct().Order()],
            _targetNodeIds = [.. draft.TargetNodeIds.Distinct()],
            Source = string.IsNullOrWhiteSpace(draft.Source) ? null : draft.Source.Trim(),
            CreatedAt = now,
            UpdatedAt = now,
            ExpiresAt = now + draft.Lifetime,
        };
    }

    /// <summary>The status as of <paramref name="now"/>: a pending proposal past its lifetime is expired, whether or not the job ran yet.</summary>
    public ProposalStatus StatusAt(DateTimeOffset now) =>
        Status == ProposalStatus.Proposed && now >= ExpiresAt ? ProposalStatus.Expired : Status;

    public bool IsPendingAt(DateTimeOffset now) => StatusAt(now) == ProposalStatus.Proposed;

    /// <summary>
    /// <see cref="IsPendingAt"/> as a query: the one predicate of a pending proposal, for the
    /// queue, the dedupe, the revocation and the Agent Changes filter (GEN-021).
    /// </summary>
    public static Expression<Func<ChangeProposal, bool>> PendingAt(DateTimeOffset now) =>
        proposal => proposal.Status == ProposalStatus.Proposed && proposal.ExpiresAt > now;

    /// <summary><see cref="StatusAt"/> reading <see cref="ProposalStatus.Expired"/>, as a query: expired, or pending past its lifetime.</summary>
    public static Expression<Func<ChangeProposal, bool>> ExpiredAt(DateTimeOffset now) =>
        proposal => proposal.Status == ProposalStatus.Expired
            || (proposal.Status == ProposalStatus.Proposed && proposal.ExpiresAt <= now);

    /// <summary>
    /// When it was decided, as of <paramref name="now"/>: one past its lifetime was decided when
    /// it expired, whether or not the job ran yet (<see cref="StatusAt"/>).
    /// </summary>
    public DateTimeOffset? DecidedAtAsOf(DateTimeOffset now) =>
        StatusAt(now) == ProposalStatus.Expired ? DecidedAt ?? ExpiresAt : DecidedAt;

    /// <summary>
    /// What an earlier <c>apply_changes</c> with <paramref name="idempotencyKey"/> answered, so a
    /// retry answers the same (DA-043 item 7). The agent only applies a proposal that is not the
    /// person's, and only <see cref="LeaveToThePerson"/> keeps a key on one that is: a key on a
    /// person-only proposal was answered as pending.
    /// </summary>
    public KeyedAnswer AnswerTo(string idempotencyKey)
    {
        if (IdempotencyKey is null || IdempotencyKey != idempotencyKey)
        {
            return KeyedAnswer.None;
        }

        return RequiresUser ? KeyedAnswer.Pending : KeyedAnswer.Applied;
    }

    /// <summary>The agent applied it with <c>apply_changes</c>; a retry with the key answers <paramref name="applied"/> again.</summary>
    public void AppliedByTheAgent(AppliedChanges applied, string idempotencyKey, DateTimeOffset now)
    {
        Apply(applied, now);
        _createdNodes = new Dictionary<string, Guid>(applied.CreatedNodes, StringComparer.Ordinal);
        IdempotencyKey = idempotencyKey;
    }

    /// <summary>
    /// The person approved it; <paramref name="rejectedOperations"/> are the ones they left out.
    /// The key of an earlier <c>apply_changes</c> stays, so its retry answers the same.
    /// </summary>
    public void ApprovedByThePerson(AppliedChanges applied, IReadOnlyList<int> rejectedOperations, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(rejectedOperations);
        Apply(applied, now);
        _rejectedOperations = [.. rejectedOperations.Distinct().Order()];
    }

    /// <summary>
    /// <c>apply_changes</c> with <paramref name="idempotencyKey"/> found it needs the person now
    /// (DA-132): only the person approves it, for good, and that key's answer is that it waits.
    /// </summary>
    public void LeaveToThePerson(IEnumerable<ProposalReason> reasons, string idempotencyKey, DateTimeOffset now)
    {
        RequireUser(reasons, now);
        IdempotencyKey = idempotencyKey;
    }

    /// <summary>
    /// Makes it a proposal only the person approves, adding why; never undone (DA-043 item 4):
    /// turning the connection's toggle off later does not unlock it.
    /// </summary>
    public void RequireUser(IEnumerable<ProposalReason> reasons, DateTimeOffset now)
    {
        RequiresUser = true;
        _reasons = [.. _reasons.Concat(reasons).Distinct().Order()];
        UpdatedAt = now;
    }

    public void Reject(DateTimeOffset now)
    {
        EnsurePending(now);
        Status = ProposalStatus.Rejected;
        DecidedAt = now;
        UpdatedAt = now;
    }

    public void Withdraw(DateTimeOffset now)
    {
        EnsurePending(now);
        Status = ProposalStatus.Withdrawn;
        DecidedAt = now;
        UpdatedAt = now;
    }

    /// <returns><c>false</c> when it was no longer pending, or is not past its lifetime yet.</returns>
    public bool Expire(DateTimeOffset now)
    {
        if (Status != ProposalStatus.Proposed || now < ExpiresAt)
        {
            return false;
        }

        Status = ProposalStatus.Expired;
        DecidedAt = ExpiresAt;
        UpdatedAt = now;
        return true;
    }

    /// <summary>
    /// Empties the operations once Purge removed Nodes whose content they hold
    /// (<paramref name="purgedNodeIds"/>); the skeleton stays (DA-021, DA-048), and so do the
    /// names of the created Nodes still there (<see cref="CreatedNodes"/>). A pending one is
    /// withdrawn, so nobody applies what is left of it. Redacting again forgets more names.
    /// </summary>
    public void Redact(IReadOnlyCollection<Guid> purgedNodeIds, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(purgedNodeIds);
        if (!Expire(now) && Status == ProposalStatus.Proposed)
        {
            Status = ProposalStatus.Withdrawn;
            DecidedAt = now;
        }

        Operations = "[]";
        Source = null;
        _createdNodes = _createdNodes
            .Where(created => !purgedNodeIds.Contains(created.Value))
            .ToDictionary(created => created.Key, created => created.Value, StringComparer.Ordinal);
        RedactedAt ??= now;
        UpdatedAt = now;
    }

    // The Nodes it created hold content of its operations, so Purge finds it by them too.
    private void Apply(AppliedChanges applied, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(applied);
        EnsurePending(now);
        Status = ProposalStatus.Applied;
        ChangeSetId = applied.ChangeSetId;
        _targetNodeIds = [.. _targetNodeIds.Union(applied.CreatedNodes.Values)];
        DecidedAt = now;
        UpdatedAt = now;
    }

    private void EnsurePending(DateTimeOffset now)
    {
        if (!IsPendingAt(now))
        {
            throw new InvalidOperationException("Only a pending proposal is decided.");
        }
    }
}

/// <summary>What an earlier <c>apply_changes</c> with the same idempotency key answered (DA-043 item 7).</summary>
public enum KeyedAnswer
{
    /// <summary>No earlier call with this key: the call is new.</summary>
    None,

    /// <summary>The agent applied it.</summary>
    Applied,

    /// <summary>It turned out to need the person, and waits for them.</summary>
    Pending,
}

/// <summary>What applying a proposal recorded.</summary>
/// <param name="ChangeSetId">The GraphChangeSet it became; <c>null</c> when the graph already was as proposed.</param>
/// <param name="CreatedNodes">The new Nodes, by the name the answer gives them.</param>
public sealed record AppliedChanges(Guid? ChangeSetId, IReadOnlyDictionary<string, Guid> CreatedNodes)
{
    /// <summary>The graph already was as proposed: nothing recorded, nothing created.</summary>
    public static AppliedChanges None { get; } = new(ChangeSetId: null, new Dictionary<string, Guid>());
}

/// <summary>What a new proposal holds.</summary>
public sealed record ProposalDraft(
    Guid AccountId,
    Guid AgentIdentityId,
    string Operations,
    int OperationCount,
    string ContentHash,
    bool RequiresUser,
    IReadOnlyList<ProposalReason> Reasons,
    IReadOnlyList<Guid> TargetNodeIds,
    string? Source,
    TimeSpan Lifetime);
