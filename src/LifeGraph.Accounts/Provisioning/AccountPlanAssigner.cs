using LifeGraph.Accounts.Contracts;
using LifeGraph.Accounts.Domain;
using LifeGraph.Infrastructure.Accounts;
using LifeGraph.Infrastructure.Identity;
using LifeGraph.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LifeGraph.Accounts.Provisioning;

/// <summary>
/// Gives an Account its plan by hand, with no billing (DA-260). Runs only in the owner's CLI, as
/// the provisioning role, inside a scope that acts for that one Account: the role reaches the
/// plan of the Account it names and no other (DA-267).
/// </summary>
public sealed partial class AccountPlanAssigner(
    UserManager<LifeGraphUser> userManager,
    IServiceScopeFactory scopeFactory,
    TimeProvider clock,
    ILogger<AccountPlanAssigner> logger)
{
    /// <returns>The Account the plan now applies to; <c>null</c> when no user has the e-mail.</returns>
    public async Task<Guid?> AssignAsync(string email, AccountPlan plan, CancellationToken cancellationToken)
    {
        if (await userManager.FindByEmailAsync(email.Trim()) is not { } user)
        {
            return null;
        }

        await using var scope = scopeFactory.CreateAccountScope(user.AccountId);
        var db = scope.ServiceProvider.GetRequiredService<LifeGraphDbContext>();
        await db.InAccountTransactionAsync(
            async token =>
            {
                var now = clock.GetUtcNow();
                var assignment = await db.Set<AccountPlanAssignment>().SingleOrDefaultAsync(token);
                if (assignment is null)
                {
                    db.Add(AccountPlanAssignment.Assign(user.AccountId, plan, AccountPlanOrigin.Cli, now));
                }
                else
                {
                    assignment.Reassign(plan, AccountPlanOrigin.Cli, now);
                }

                await db.SaveChangesAsync(token);
                return true;
            },
            cancellationToken);

        LogPlanAssigned(logger, user.AccountId, plan);
        return user.AccountId;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Account {AccountId} is now on plan {Plan}")]
    private static partial void LogPlanAssigned(ILogger logger, Guid accountId, AccountPlan plan);
}
