using System.Security.Claims;
using LifeGraph.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace LifeGraph.Accounts.Identity;

/// <summary>Stamps the Account and the principal type into the session, so no request has to look them up.</summary>
internal sealed class AccountClaimsPrincipalFactory(
    UserManager<LifeGraphUser> userManager,
    IOptions<IdentityOptions> options) : UserClaimsPrincipalFactory<LifeGraphUser>(userManager, options)
{
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(LifeGraphUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);
        identity.AddClaim(new Claim(LifeGraphClaimTypes.AccountId, user.AccountId.ToString()));
        identity.AddClaim(new Claim(LifeGraphClaimTypes.PrincipalType, PrincipalClaims.ToClaimValue(PrincipalType.Human)));
        return identity;
    }
}
