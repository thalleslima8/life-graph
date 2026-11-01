using LifeGraph.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace LifeGraph.Infrastructure.Accounts;

/// <summary>A service scope that acts for one Account, so its transactions run under that Account's RLS.</summary>
public static class AccountScope
{
    /// <summary>
    /// For code with no signed-in principal of that Account: the setup at sign-in (DA-149) and the
    /// owner's CLI (DA-267). Fails when the scope would act for another Account.
    /// </summary>
    public static AsyncServiceScope CreateAccountScope(this IServiceScopeFactory scopeFactory, Guid accountId)
    {
        var scope = scopeFactory.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<BackgroundAccount>().Enter(accountId);
        if (scope.ServiceProvider.GetRequiredService<IAccountContext>().AccountId != accountId)
        {
            scope.Dispose();
            throw new InvalidOperationException("The scope acts for another Account.");
        }

        return scope;
    }
}
