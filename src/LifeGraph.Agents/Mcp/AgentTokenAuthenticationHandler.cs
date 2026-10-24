using System.Text.Encodings.Web;
using LifeGraph.Accounts.Contracts;
using LifeGraph.Infrastructure.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using ModelContextProtocol.AspNetCore.Authentication;

namespace LifeGraph.Agents.Mcp;

/// <summary>
/// The agent's way in (DA-030, DA-031): the issuer's token, validated by OpenIddict (signature,
/// lifetime, audience = the MCP endpoint), of a connection that is still active. A revoked or
/// unknown AgentIdentity does not authenticate, so the client gets 401 and authorizes again;
/// each success stamps the connection's last use. The check reads the database on every
/// request, with no cache, so a revocation counts at once (DA-121). The challenge points at the
/// Protected Resource Metadata (RFC 9728) and, for a token that was sent but is no longer good
/// (revoked, expired, another audience), says <c>error="invalid_token"</c> (RFC 6750). The
/// person's session cookie never counts here.
/// </summary>
internal sealed class AgentTokenAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IAgentIdentities agentIdentities,
    IOptionsMonitor<McpAuthenticationOptions> mcpOptions) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var token = await Context.AuthenticateAsync(AgentAccess.TokenScheme);
        if (!token.Succeeded)
        {
            return token.None ? AuthenticateResult.NoResult() : AuthenticateResult.Fail(token.Failure!);
        }

        if (PrincipalClaims.Read(token.Principal) is not { Type: PrincipalType.AgentIdentity, AgentIdentityId: { } agentIdentityId })
        {
            return AuthenticateResult.Fail("Not an agent token.");
        }

        // The lookup is scoped to the token's Account (RLS), read from the caller's principal.
        Context.User = token.Principal;
        if (AgentAccess.AuthorizationIdOf(token.Principal) is not { } authorizationId
            || !await agentIdentities.TryRecordUseAsync(agentIdentityId, authorizationId, Context.RequestAborted))
        {
            return AuthenticateResult.Fail("The connection was revoked.");
        }

        return AuthenticateResult.Success(new AuthenticationTicket(token.Principal, Scheme.Name));
    }

    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        var authentication = await HandleAuthenticateOnceSafeAsync();
        var metadata = mcpOptions.Get(AgentsModule.McpScheme).ResourceMetadataUri;
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.Headers.Append(
            HeaderNames.WWWAuthenticate,
            authentication.Failure is null
                ? $"Bearer resource_metadata=\"{metadata}\""
                : $"Bearer error=\"invalid_token\", resource_metadata=\"{metadata}\"");
    }
}
