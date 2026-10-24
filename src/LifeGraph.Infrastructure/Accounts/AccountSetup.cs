using LifeGraph.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace LifeGraph.Infrastructure.Accounts;

/// <summary>
/// Runs every <see cref="IAccountSetupParticipant"/> for one Account, in one transaction under
/// that Account's RLS, with the application role (DA-149). A scope of its own acts for the
/// Account, so it works before the signed-in session reaches the request.
/// </summary>
public sealed class AccountSetup(IServiceScopeFactory scopeFactory)
{
    public async Task EnsureAsync(Guid accountId, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<BackgroundAccount>().Enter(accountId);
        if (scope.ServiceProvider.GetRequiredService<IAccountContext>().AccountId != accountId)
        {
            throw new InvalidOperationException("The setup scope acts for another Account.");
        }

        var db = scope.ServiceProvider.GetRequiredService<LifeGraphDbContext>();
        var participants = scope.ServiceProvider.GetServices<IAccountSetupParticipant>().ToList();
        await db.InAccountTransactionAsync(
            async token =>
            {
                foreach (var participant in participants)
                {
                    await participant.EnsureAsync(accountId, token);
                }

                return true;
            },
            cancellationToken);
    }
}
