using System.Security.Claims;
using LifeGraph.Accounts.Domain;
using LifeGraph.Accounts.Email;
using LifeGraph.Infrastructure.Accounts;
using LifeGraph.Infrastructure.Errors;
using LifeGraph.Infrastructure.Identity;
using LifeGraph.Infrastructure.Persistence;
using LifeGraph.Infrastructure.Purging;
using Limaj.Framework.Core;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace LifeGraph.Accounts.Deletion;

/// <summary>
/// Deletes the signed-in person's Account for good (DA-082): the password typed again
/// (DA-249) and the Account's e-mail typed as confirmation (DA-251), then every purge
/// participant (DA-012), the Identity user, the Account itself and its purge ledger row in one
/// transaction (DA-084, DA-252). Access dies with the commit: the cookie is checked against the
/// user on every request, and with the user gone the check rejects the session; the grants,
/// tokens, AgentIdentities and share links are gone with the rest.
/// <para>
/// The purge runs inside the request by exception to BE-042 (long work in a job), recorded
/// as DA-252: an MVP Account is small and the data is dev-only until E14. It moves to a job
/// (an "Account being deleted" mark plus an <c>accounts.purge</c> job) when the measured
/// duration asks for it.
/// </para>
/// </summary>
internal sealed partial class AccountDeletion(
    LifeGraphDbContext db,
    UserManager<LifeGraphUser> users,
    ICurrentPrincipal currentPrincipal,
    IEnumerable<IAccountPurgeParticipant> participants,
    IAccountMailer mailer,
    TimeProvider clock,
    ILogger<AccountDeletion> logger)
{
    private const string WrongPasswordMessage = "The password is incorrect.";

    private const string ConfirmationMismatchMessage = "Type the e-mail of your account to confirm its deletion.";

    private const string NotOwnerMessage = "Only the person who owns the Account deletes it.";

    private const string SignInAgainMessage = "Sign in again.";

    private const string DeletionFailedMessage = "The account could not be deleted. Try again.";

    public async Task<Result<AccountDeleted>> DeleteAsync(
        ClaimsPrincipal session,
        string password,
        string typedConfirmation,
        CancellationToken cancellationToken)
    {
        // Only the person deletes the Account, never an agent or a share visitor (DA-082, BE-021).
        if (currentPrincipal.Authenticated is not { Type: PrincipalType.Human } principal)
        {
            return Result<AccountDeleted>.Fail(CommonErrors.Forbidden.ToError(NotOwnerMessage));
        }

        var user = await users.GetUserAsync(session);
        if (user is null || user.AccountId != principal.AccountId)
        {
            return Result<AccountDeleted>.Fail(CommonErrors.Unauthorized.ToError(SignInAgainMessage));
        }

        if (!await users.CheckPasswordAsync(user, password))
        {
            return Result<AccountDeleted>.Fail(AccountsErrors.ReauthenticationFailed.ToError(WrongPasswordMessage));
        }

        // Read before the transaction (DA-250): once it commits, the address is gone.
        var email = user.Email;
        if (!AccountDeletionConfirmation.Matches(typedConfirmation, email))
        {
            return Result<AccountDeleted>.Fail(AccountsErrors.DeletionConfirmationMismatch.ToError(
                ConfirmationMismatchMessage,
                new Dictionary<string, string[]> { [AccountDeletionFields.Confirmation] = [ConfirmationMismatchMessage] }));
        }

        var deleted = await db.InAccountResultTransactionAsync(token => PurgeAsync(user, token), cancellationToken);
        if (deleted.IsSuccess)
        {
            await SendNoticeAsync(email!, principal.AccountId, deleted.Value!, cancellationToken);
        }

        return deleted;
    }

    private async Task<Result<AccountDeleted>> PurgeAsync(LifeGraphUser user, CancellationToken cancellationToken)
    {
        var accountId = user.AccountId;

        // The Account row is locked first: a concurrent write that checks its foreign key waits
        // for this transaction and then fails, so nothing new lands while the participants run.
        await db.Database.ExecuteSqlAsync($"SELECT id FROM accounts WHERE id = {accountId} FOR UPDATE", cancellationToken);

        foreach (var participant in participants)
        {
            await participant.PurgeAsync(accountId, cancellationToken);
        }

        // The user's claims, logins and tokens go with it (Identity's cascades); its e-mail is free again.
        var userRemoval = await users.DeleteAsync(user);
        if (!userRemoval.Succeeded)
        {
            return Result<AccountDeleted>.Fail(CommonErrors.Unexpected.ToError(DeletionFailedMessage));
        }

        await db.Accounts.Where(account => account.Id == accountId).ExecuteDeleteAsync(cancellationToken);

        // The PII-free receipt, committed with the purge or not at all (DA-083, DA-084, DA-253).
        var ledgerEntry = PurgeLedgerEntry.ForAccount(accountId, clock.GetUtcNow());
        db.Add(ledgerEntry);
        await db.SaveChangesAsync(cancellationToken);
        return Result<AccountDeleted>.Ok(new AccountDeleted(ledgerEntry.PurgedAt, ledgerEntry.ReceiptReference!));
    }

    private async Task SendNoticeAsync(string email, Guid accountId, AccountDeleted deleted, CancellationToken cancellationToken)
    {
        try
        {
            await mailer.SendAsync(AccountEmail.AccountDeleted(email, deleted.DeletedAt, deleted.ReceiptReference), cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The deletion stands; the notice is lost, never queued with the address (DA-250).
            // Only the type is logged, since SMTP errors can echo the address (GEN-043).
            LogNoticeFailed(logger, accountId, exception.GetType().Name);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Account deletion notice for Account {AccountId} could not be sent ({ErrorType})")]
    private static partial void LogNoticeFailed(ILogger logger, Guid accountId, string errorType);
}

/// <summary>The outcome of a committed Account deletion, with the reference of its purge ledger row (DA-253).</summary>
internal sealed record AccountDeleted(DateTimeOffset DeletedAt, string ReceiptReference);
