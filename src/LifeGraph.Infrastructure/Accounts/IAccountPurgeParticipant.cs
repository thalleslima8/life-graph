namespace LifeGraph.Infrastructure.Accounts;

/// <summary>
/// Extension point of the account purge (DA-012). Every module that stores data for an
/// Account registers one participant that erases that data, its history and anything
/// derived from it. Account deletion (E11) runs all participants before removing the
/// Account itself, so a module added later cannot be forgotten by the purge.
/// </summary>
public interface IAccountPurgeParticipant
{
    Task PurgeAsync(Guid accountId, CancellationToken cancellationToken);
}
