namespace LifeGraph.Accounts.Contracts;

/// <summary>
/// The plan of the Account in context and the limits that come with it (DA-260). The Accounts
/// module owns the plan; Graph, Agents and Sharing ask through this port. A limit stops only
/// new creation, never access to what exists (DA-086).
/// </summary>
public interface IEntitlements
{
    /// <summary>
    /// Reads the plan inside the caller's Account transaction, so a check and the write it guards
    /// see the same plan. An Account without an assigned plan is on <see cref="AccountPlan.Free"/>.
    /// </summary>
    Task<AccountEntitlements> OfCurrentAccountAsync(CancellationToken cancellationToken);

    /// <summary>
    /// The plan of the Account in context as its holder sees it, for the export (DA-298): read
    /// inside the caller's Account transaction, like <see cref="OfCurrentAccountAsync"/>.
    /// </summary>
    Task<AccountPlanAssignmentView> PlanAssignmentOfCurrentAccountAsync(CancellationToken cancellationToken);
}

/// <summary>The plans of the MVP, assigned by hand and never billed (DA-260).</summary>
public enum AccountPlan
{
    Free,
    Premium,
}

/// <summary>The way a plan change was made. Today only the owner's CLI changes a plan (DA-260, DA-267).</summary>
public enum AccountPlanOrigin
{
    /// <summary>The <c>accounts plan</c> command, run by the owner as the provisioning role.</summary>
    Cli,
}

/// <param name="Origin">The way the last plan change was made; <c>null</c> for an Account never given a plan.</param>
/// <param name="UpdatedAt">When the last plan change was made; <c>null</c> for an Account never given a plan.</param>
public sealed record AccountPlanAssignmentView(AccountPlan Plan, AccountPlanOrigin? Origin, DateTimeOffset? UpdatedAt)
{
    /// <summary>An Account without an assigned plan: on <see cref="AccountPlan.Free"/>, with no change recorded.</summary>
    public static AccountPlanAssignmentView Unassigned { get; } = new(AccountPlan.Free, null, null);
}

public sealed record AccountEntitlements(AccountPlan Plan, PlanLimits Limits);

/// <summary>
/// How many of each kind a plan holds (global_v2 §55). Only live rows count, and what the
/// system creates never does (DA-264).
/// </summary>
/// <param name="AgentConnections">Active AgentIdentities (DA-262).</param>
/// <param name="AgentWriteOperationsPerMonth">Operations the Account's agents apply in a UTC calendar month (DA-261).</param>
public sealed record PlanLimits(
    int Nodes,
    int Relations,
    int Resources,
    int Collections,
    int CustomTypes,
    int CustomProperties,
    int AgentConnections,
    int AgentWriteOperationsPerMonth,
    int SharedGraphViews,
    int NodesPerSharedGraphView);
