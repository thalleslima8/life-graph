namespace LifeGraph.Infrastructure.Persistence;

/// <summary>
/// The Account a background unit of work acts for, when no principal is signed in: a job
/// of the queue runs for the Account that owns it (DA-113). Scoped and set once per scope,
/// only by the job runner; a signed-in principal always wins over it, so it can never switch
/// a caller to another Account.
/// </summary>
public sealed class BackgroundAccount
{
    public Guid? AccountId { get; private set; }

    internal void Enter(Guid accountId)
    {
        if (AccountId is not null)
        {
            throw new InvalidOperationException("This scope already acts for an Account.");
        }

        AccountId = accountId;
    }
}
