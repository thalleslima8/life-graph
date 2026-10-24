using LifeGraph.Accounts.Domain;
using LifeGraph.Infrastructure.Accounts;
using LifeGraph.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;

namespace LifeGraph.Accounts.Persistence;

/// <summary>
/// The agent connections' part of deleting an Account (DA-012): the AgentIdentities and the
/// issuer's grants and tokens of the Account's user, gone at once, found only by the user's
/// subject (DA-119). The clients (<c>oidc_applications</c>) are global and stay. Runs inside the
/// Account's transaction, as every participant, so a later participant's failure undoes it too.
/// </summary>
internal sealed class AgentIdentitiesAccountPurge(
    LifeGraphDbContext db,
    IOpenIddictAuthorizationManager authorizations,
    IOpenIddictTokenManager tokens) : IAccountPurgeParticipant
{
    public async Task PurgeAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var userIds = await db.Users.Where(user => user.AccountId == accountId).Select(user => user.Id).ToListAsync(cancellationToken);
        foreach (var userId in userIds)
        {
            // Deleting a grant deletes its tokens with it (OpenIddict's store).
            var grants = await authorizations.FindBySubjectAsync(userId.ToString(), cancellationToken).ToListAsync(cancellationToken);
            foreach (var authorization in grants)
            {
                await authorizations.DeleteAsync(authorization, cancellationToken);
            }

            // Any token issued outside a grant.
            var loose = await tokens.FindBySubjectAsync(userId.ToString(), cancellationToken).ToListAsync(cancellationToken);
            foreach (var token in loose)
            {
                await tokens.DeleteAsync(token, cancellationToken);
            }
        }

        await db.Set<AgentIdentity>().Where(identity => identity.AccountId == accountId).ExecuteDeleteAsync(cancellationToken);
    }
}
