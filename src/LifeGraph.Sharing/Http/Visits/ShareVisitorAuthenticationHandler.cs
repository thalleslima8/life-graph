using System.Security.Claims;
using System.Text.Encodings.Web;
using LifeGraph.Graph.Contracts;
using LifeGraph.Http;
using LifeGraph.Infrastructure.Errors;
using LifeGraph.Infrastructure.Identity;
using Limaj.Framework.Web.Http;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace LifeGraph.Sharing.Http.Visits;

/// <summary>
/// Makes the bearer of a link token a ShareVisitor of the share it opens (DA-217, DA-221): its
/// Account, for the RLS, and its SharedGraphView, for the central filter. Never a session. An
/// absent, malformed, unknown or blocked token, or sharing turned off, is the same not found,
/// so the visitor cannot tell one from another.
/// </summary>
internal sealed class ShareVisitorAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    ISharedGraphViews shares,
    IOptionsMonitor<SharingOptions> sharing) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    private const string BearerPrefix = "Bearer ";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!sharing.CurrentValue.Enabled || PresentedToken(Request) is not { } presented)
        {
            return AuthenticateResult.NoResult();
        }

        if (await shares.ResolveVisitorAsync(presented, Context.RequestAborted) is not { } visitor)
        {
            return AuthenticateResult.NoResult();
        }

        var identity = new ClaimsIdentity(
            [
                new Claim(LifeGraphClaimTypes.AccountId, visitor.AccountId.ToString()),
                new Claim(LifeGraphClaimTypes.PrincipalType, PrincipalClaims.ToClaimValue(PrincipalType.ShareVisitor)),
                new Claim(LifeGraphClaimTypes.SharedGraphViewId, visitor.SharedGraphViewId.ToString()),
            ],
            Scheme.Name);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties) => UnavailableAsync();

    protected override Task HandleForbiddenAsync(AuthenticationProperties properties) => UnavailableAsync();

    /// <summary>The token a request presents, or <c>null</c>; also the key of the per-link budget.</summary>
    internal static string? PresentedToken(HttpRequest request)
    {
        var authorization = request.Headers[HeaderNames.Authorization];
        return authorization.Count == 1
            && authorization[0] is { } value
            && value.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase)
            && value.Length > BearerPrefix.Length
            ? value[BearerPrefix.Length..].Trim()
            : null;
    }

    private Task UnavailableAsync()
    {
        var responder = Context.RequestServices.GetRequiredService<IHttpResultResponder>();
        return responder.Fail(CommonErrors.NotFound.ToError(SharingErrors.UnavailableMessage)).ExecuteAsync(Context);
    }
}
