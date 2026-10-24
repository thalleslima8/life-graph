namespace LifeGraph.Accounts.Provisioning;

public abstract record AccountProvisioningOutcome
{
    private AccountProvisioningOutcome()
    {
    }

    /// <summary>A new Account and its pending user.</summary>
    /// <param name="LinkSent">
    /// False when the e-mail with the set-password link failed; resend it before the user can sign in.
    /// </param>
    public sealed record Created(Guid AccountId, Guid UserId, bool LinkSent) : AccountProvisioningOutcome;

    /// <summary>The e-mail already belonged to a pending account: nothing was created, the link went out again.</summary>
    public sealed record Pending(Guid AccountId, Guid UserId, bool LinkSent) : AccountProvisioningOutcome;

    /// <summary>The e-mail belongs to an account whose user already set a password; nothing was sent.</summary>
    public sealed record AlreadyActive : AccountProvisioningOutcome;

    /// <summary>No account has this e-mail (resend only).</summary>
    public sealed record NotFound : AccountProvisioningOutcome;

    /// <param name="ErrorCodes">Identity error codes (e.g. <c>InvalidEmail</c>).</param>
    public sealed record Rejected(IReadOnlyList<string> ErrorCodes) : AccountProvisioningOutcome;
}
