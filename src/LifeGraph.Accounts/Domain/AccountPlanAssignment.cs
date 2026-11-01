using LifeGraph.Accounts.Contracts;

namespace LifeGraph.Accounts.Domain;

/// <summary>
/// The plan an operator gave an Account by hand (DA-260). No row means
/// <see cref="AccountPlan.Free"/>, so a new Account needs none. The web process only reads it:
/// only the owner's CLI writes it (DA-267). Each change records when and by which way it was made.
/// </summary>
public sealed class AccountPlanAssignment
{
    private AccountPlanAssignment(Guid accountId, AccountPlan plan, AccountPlanOrigin origin, DateTimeOffset updatedAt)
    {
        AccountId = accountId;
        Plan = plan;
        Origin = origin;
        UpdatedAt = updatedAt;
    }

    public Guid AccountId { get; private set; }

    public AccountPlan Plan { get; private set; }

    /// <summary>The way the last plan change was made.</summary>
    public AccountPlanOrigin Origin { get; private set; }

    /// <summary>When the last plan change was made.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    public static AccountPlanAssignment Assign(Guid accountId, AccountPlan plan, AccountPlanOrigin origin, DateTimeOffset now) =>
        new(accountId, plan, origin, now);

    /// <returns>Whether the plan changed; assigning the same plan again keeps its origin and date.</returns>
    public bool Reassign(AccountPlan plan, AccountPlanOrigin origin, DateTimeOffset now)
    {
        if (plan == Plan)
        {
            return false;
        }

        Plan = plan;
        Origin = origin;
        UpdatedAt = now;
        return true;
    }
}
