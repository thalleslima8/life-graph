using LifeGraph.Infrastructure.Identity;
using Microsoft.AspNetCore.Http;

namespace LifeGraph.Accounts.Identity;

/// <summary>Reads the principal of the current HTTP request; outside a request (CLI) there is none.</summary>
internal sealed class HttpCurrentPrincipal(IHttpContextAccessor httpContextAccessor) : ICurrentPrincipal
{
    public AuthenticatedPrincipal? Authenticated => PrincipalClaims.Read(httpContextAccessor.HttpContext?.User);
}
