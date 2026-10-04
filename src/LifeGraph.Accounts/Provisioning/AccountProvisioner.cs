using LifeGraph.Accounts.Email;
using LifeGraph.Accounts.Http;
using LifeGraph.Accounts.Identity;
using LifeGraph.Infrastructure.Accounts;
using LifeGraph.Infrastructure.Identity;
using LifeGraph.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace LifeGraph.Accounts.Provisioning;

/// <summary>
/// Creates a user together with its Account (1 user = 1 Account in the MVP) in one
/// transaction, with no password (DA-095): the owner never learns another person's
/// credential. The user receives a link that confirms the e-mail and sets the password;
/// until then sign-in is impossible. Provisioning an e-mail that is still pending sends
/// the link again instead of creating a second account.
/// </summary>
public sealed partial class AccountProvisioner(
    LifeGraphDbContext dbContext,
    UserManager<LifeGraphUser> userManager,
    TimeProvider clock,
    AccountEmailLinks links,
    IAccountMailer mailer,
    ILogger<AccountProvisioner> logger)
{
    public const string EmailTooLongCode = "EmailTooLong";

    public async Task<AccountProvisioningOutcome> ProvisionAsync(string email, CancellationToken cancellationToken)
    {
        var trimmedEmail = email.Trim();
        if (trimmedEmail.Length > InputLimits.EmailMaxLength)
        {
            return new AccountProvisioningOutcome.Rejected([EmailTooLongCode]);
        }

        var existing = await userManager.FindByEmailAsync(trimmedEmail);
        if (existing is not null)
        {
            return await ResendToAsync(existing, cancellationToken);
        }

        var account = Account.Open(clock);
        var user = new LifeGraphUser
        {
            Id = Guid.CreateVersion7(),
            UserName = trimmedEmail,
            Email = trimmedEmail,
            AccountId = account.Id,
        };

        var creation = await CreateAccountAndUserAsync(account, user, cancellationToken);
        if (!creation.Succeeded)
        {
            return new AccountProvisioningOutcome.Rejected([.. creation.Errors.Select(error => error.Code)]);
        }

        LogAccountProvisioned(logger, account.Id, user.Id);
        var linkSent = await TrySendEmailConfirmationAsync(user, cancellationToken);

        return new AccountProvisioningOutcome.Created(account.Id, user.Id, linkSent);
    }

    /// <summary>Sends a fresh set-password link to a pending account; active accounts use password recovery.</summary>
    public async Task<AccountProvisioningOutcome> ResendEmailConfirmationAsync(string email, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByEmailAsync(email.Trim());
        return user is null
            ? new AccountProvisioningOutcome.NotFound()
            : await ResendToAsync(user, cancellationToken);
    }

    private async Task<AccountProvisioningOutcome> ResendToAsync(LifeGraphUser user, CancellationToken cancellationToken)
    {
        if (!await EmailedLinkTokens.IsPendingAsync(userManager, user))
        {
            return new AccountProvisioningOutcome.AlreadyActive();
        }

        var linkSent = await TrySendEmailConfirmationAsync(user, cancellationToken);
        return new AccountProvisioningOutcome.Pending(user.AccountId, user.Id, linkSent);
    }

    // Users are a global directory (no RLS); the Account insert goes through the
    // accounts_provisioning policy, the only write allowed before an Account is known.
    private Task<IdentityResult> CreateAccountAndUserAsync(
        Account account,
        LifeGraphUser user,
        CancellationToken cancellationToken)
    {
        var strategy = dbContext.Database.CreateExecutionStrategy();

        return strategy.ExecuteAsync(async token =>
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(token);

            dbContext.Accounts.Add(account);
            await dbContext.SaveChangesAsync(token);

            var creation = await userManager.CreateAsync(user);
            if (!creation.Succeeded)
            {
                // The Account row goes away with the rollback; forget it in memory too.
                await transaction.RollbackAsync(token);
                dbContext.ChangeTracker.Clear();
                return creation;
            }

            await transaction.CommitAsync(token);
            return creation;
        }, cancellationToken);
    }

    private async Task<bool> TrySendEmailConfirmationAsync(LifeGraphUser user, CancellationToken cancellationToken)
    {
        var token = await EmailedLinkTokens.GenerateEmailConfirmationAsync(userManager, user);
        var email = AccountEmail.EmailConfirmation(
            user.Email!,
            links.EmailConfirmation(user.Id, token),
            IdentityRegistration.EmailedLinkLifetime);

        try
        {
            await mailer.SendAsync(email, cancellationToken);
            LogEmailConfirmationSent(logger, user.Id);
            return true;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Only the type: SMTP error messages can echo the recipient address (GEN-043).
            LogEmailConfirmationFailed(logger, user.Id, exception.GetType().Name);
            return false;
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Account {AccountId} provisioned for user {UserId}")]
    private static partial void LogAccountProvisioned(ILogger logger, Guid accountId, Guid userId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Set-password link sent to user {UserId}")]
    private static partial void LogEmailConfirmationSent(ILogger logger, Guid userId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Set-password e-mail for user {UserId} could not be sent ({ErrorType})")]
    private static partial void LogEmailConfirmationFailed(ILogger logger, Guid userId, string errorType);
}
