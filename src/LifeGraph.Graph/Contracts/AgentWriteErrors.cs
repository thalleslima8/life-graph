using LifeGraph.Infrastructure.Errors;

namespace LifeGraph.Graph.Contracts;

/// <summary>
/// The codes of agent writes and proposals (E5). They carry the <c>agents.</c> prefix the
/// epic published for them (DA-130), though the policy lives with the GraphChangeSet, in the
/// Graph module (DA-112). The messages are public text, never Node content (DA-103).
/// </summary>
public static class AgentWriteErrors
{
    /// <summary>Unknown, of another AgentIdentity or of another Account alike (DA-043 item 1).</summary>
    public static readonly ErrorCode ProposalNotFound = ErrorCode.NotFound("agents.proposal_not_found");

    /// <summary>Already applied, rejected, withdrawn or expired (DA-043 item 2).</summary>
    public static readonly ErrorCode ProposalNotPending =
        ErrorCode.BusinessRule("agents.proposal_not_pending", ErrorRecovery.NotRecoverable);

    /// <summary>A Sensitive operation, or a proposal marked <c>requires_user</c>: only the person approves it (DA-043 items 3 and 4).</summary>
    public static readonly ErrorCode ProposalRequiresUser =
        ErrorCode.BusinessRule("agents.proposal_requires_user", ErrorRecovery.AskUser);

    /// <summary>The same content was rejected by the person before (DA-043 item 8): do not send it again.</summary>
    public static readonly ErrorCode ProposalRejectedBefore =
        ErrorCode.BusinessRule("agents.proposal_rejected_before", ErrorRecovery.NotRecoverable);

    /// <summary>The agent has as many pending proposals as allowed (DA-130): nothing was applied; the person must review first.</summary>
    public static readonly ErrorCode ProposalQueueFull =
        ErrorCode.BusinessRule("agents.proposal_queue_full", ErrorRecovery.AskUser);

    /// <summary>
    /// A partial approval keeps an operation and leaves out a <c>create_node</c> whose ref it
    /// uses (DA-135): nothing was applied; the details name the operations, <c>operations[i]</c>.
    /// </summary>
    public static readonly ErrorCode OperationDependency = ErrorCode.BusinessRule("agents.operation_dependency");

    public static IReadOnlyList<ErrorCode> All { get; } =
    [
        ProposalNotFound,
        ProposalNotPending,
        ProposalRequiresUser,
        ProposalRejectedBefore,
        ProposalQueueFull,
        OperationDependency,
    ];
}
