using LifeGraph.Accounts.Contracts;
using LifeGraph.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using OpenIddict.Server;
using OpenIddict.Server.AspNetCore;
using OpenIddict.Validation;
using static OpenIddict.Server.OpenIddictServerEvents;

namespace LifeGraph.Accounts.Issuer;

/// <summary>
/// The OAuth 2.1 issuer of the agents, in process with the human login (DA-009, ADR 0006):
/// authorization code + PKCE (S256 only) and rotating refresh tokens for public clients, no
/// client secrets and no DCR (DA-029). Every access token is a short-lived JWT bound to the
/// MCP endpoint's canonical URI (RFC 8707, DA-031); the same process validates it.
/// </summary>
internal static class IssuerRegistration
{
    public const string AuthorizationEndpointPath = "connect/authorize";
    public const string TokenEndpointPath = "connect/token";

    /// <summary>RFC 8414 for OAuth clients (MCP), OpenID discovery for the rest; one document.</summary>
    public static readonly string[] DiscoveryPaths = [".well-known/oauth-authorization-server", ".well-known/openid-configuration"];

    /// <summary>The flag of the Client ID Metadata Document draft, read by MCP clients (DA-029).</summary>
    public const string ClientIdMetadataDocumentSupported = "client_id_metadata_document_supported";

    public const string MissingKeysMessage =
        "The OAuth issuer needs exactly one key source: set Accounts:Issuer:KeysDirectory to a directory outside " +
        "the repository (DA-028), or Accounts:Issuer:UseEphemeralKeys to true for throwaway keys.";

    public const string InvalidIssuerMessage =
        "Set Accounts:Issuer:Issuer to the absolute origin agents reach this host at (e.g. https://agents.example.com/), " +
        "with no path, query or fragment; plain http only with Accounts:Issuer:AllowHttp.";

    public const string InvalidClientsMessage =
        "Every Accounts:Issuer:Clients entry needs a ClientId that is not a URL and at least one RedirectUri, " +
        "each https or a loopback http address, with no fragment.";

    public const string AutoConsentOutsideTestingMessage =
        "Accounts:Issuer:AutoConsent skips the consent page and is only for the Testing environment (DA-033).";

    public static IServiceCollection AddLifeGraphIssuer(this IServiceCollection services)
    {
        // A missing key or issuer only fails on the first request, and then every request
        // (health included) answers 500, so the host refuses to start instead (GEN-052).
        services.AddOptions<IssuerOptions>()
            .BindConfiguration(IssuerOptions.SectionName)
            .Validate(issuer => issuer.UseEphemeralKeys ^ !string.IsNullOrWhiteSpace(issuer.KeysDirectory), MissingKeysMessage)
            .Validate(IsValidIssuer, InvalidIssuerMessage)
            .Validate(issuer => issuer.Clients.All(IsValidClient), InvalidClientsMessage)
            .Validate<IHostEnvironment>(
                (issuer, environment) => !issuer.AutoConsent || environment.IsEnvironment(IssuerOptions.TestingEnvironment),
                AutoConsentOutsideTestingMessage)
            .ValidateOnStart();

        services.AddSingleton<IAgentIssuer, ConfiguredAgentIssuer>();

        services.AddOpenIddict()
            .AddCore(core =>
            {
                core.UseEntityFrameworkCore()
                    .UseDbContext<LifeGraphDbContext>()
                    .ReplaceDefaultEntities<Guid>();
                core.ReplaceApplicationManager(typeof(LoopbackAwareApplicationManager<>));
            })
            .AddServer(server =>
            {
                server.SetAuthorizationEndpointUris(AuthorizationEndpointPath)
                    .SetTokenEndpointUris(TokenEndpointPath)
                    .SetConfigurationEndpointUris(DiscoveryPaths);

                server.AllowAuthorizationCodeFlow()
                    .RequireProofKeyForCodeExchange()
                    .AllowRefreshTokenFlow();

                // The agent's offline access is part of every grant: refresh tokens rotate on
                // each use and die with the grant (DA-031).
                server.RegisterScopes([.. AgentAccess.Scopes, OpenIddictConstants.Scopes.OfflineAccess]);

                // The consent decides what is granted: the scopes the person checked among
                // those offered (ConsentScopes.Offered). Unknown ones (openid, profile) are
                // ignored instead of failing the connection of a real client.
                server.DisableScopeValidation();
                server.IgnoreScopePermissions();

                server.Configure(options =>
                {
                    // OAuth 2.1 and the MCP authorization spec allow only S256.
                    options.CodeChallengeMethods.Remove(OpenIddictConstants.CodeChallengeMethods.Plain);
                });

                server.AddEventHandler<HandleConfigurationRequestContext>(handler => handler
                    .UseInlineHandler(AdvertisePublicClientsAsync)
                    .SetOrder(OpenIddictServerHandlers.Discovery.AttachAdditionalMetadata.Descriptor.Order + 1_000)
                    .SetType(OpenIddictServerHandlerType.Custom));

                server.UseAspNetCore()
                    .EnableAuthorizationEndpointPassthrough()
                    .EnableTokenEndpointPassthrough();
            })
            .AddValidation(validation =>
            {
                validation.UseLocalServer();

                // A revoked grant stops its access tokens on the next request, not when they expire (DA-031).
                validation.EnableAuthorizationEntryValidation();
                validation.UseAspNetCore();
            });

        // The host switches come from configuration (GEN-051), so they apply when the options
        // are built, not when the services are registered.
        services.AddOptions<OpenIddictServerOptions>()
            .Configure<IOptions<IssuerOptions>, IAgentIssuer>((server, issuer, agentIssuer) =>
            {
                server.Issuer = agentIssuer.Issuer;
                server.AccessTokenLifetime = issuer.Value.AccessTokenLifetime;

                // The only resource tokens are issued for (RFC 8707): the MCP endpoint (DA-031).
                server.Resources.Add(agentIssuer.McpResource);

                var (signing, encryption) = issuer.Value.UseEphemeralKeys
                    ? (IssuerKeys.Ephemeral(), IssuerKeys.Ephemeral())
                    : IssuerKeys.LoadOrCreate(issuer.Value.KeysDirectory!);

                // Same algorithms OpenIddict's own key helpers use.
                server.SigningCredentials.Add(new SigningCredentials(signing, SecurityAlgorithms.RsaSha256));
                server.EncryptionCredentials.Add(new EncryptingCredentials(
                    encryption, SecurityAlgorithms.RsaOAEP, SecurityAlgorithms.Aes256CbcHmacSha512));
            });

        services.AddOptions<OpenIddictValidationOptions>()
            .Configure<IAgentIssuer>((validation, agentIssuer) => validation.Audiences.Add(agentIssuer.McpResource.AbsoluteUri));

        services.AddOptions<OpenIddictServerAspNetCoreOptions>()
            .Configure<IOptions<IssuerOptions>>((aspNetCore, issuer) =>
                aspNetCore.DisableTransportSecurityRequirement = issuer.Value.AllowHttp);

        services.AddHostedService<PreRegisteredClientsSynchronizer>();
        services.AddIssuerLogFloor();

        return services;
    }

    // Every client is public and authenticates with PKCE only (DA-029), and a client may be
    // identified by the URL of its metadata document (CIMD).
    private static ValueTask AdvertisePublicClientsAsync(HandleConfigurationRequestContext context)
    {
        context.TokenEndpointAuthenticationMethods.Clear();
        context.TokenEndpointAuthenticationMethods.Add(OpenIddictConstants.ClientAuthenticationMethods.None);
        context.Metadata[ClientIdMetadataDocumentSupported] = true;
        return ValueTask.CompletedTask;
    }

    private static bool IsValidIssuer(IssuerOptions issuer) =>
        issuer.Issuer is { IsAbsoluteUri: true } origin
        && (origin.Scheme == Uri.UriSchemeHttps || (origin.Scheme == Uri.UriSchemeHttp && issuer.AllowHttp))
        && origin.AbsolutePath == "/"
        && string.IsNullOrEmpty(origin.Query)
        && string.IsNullOrEmpty(origin.Fragment)
        && string.IsNullOrEmpty(origin.UserInfo);

    private static bool IsValidClient(PreRegisteredClient client) =>
        !string.IsNullOrWhiteSpace(client.ClientId)
        && !Uri.TryCreate(client.ClientId, UriKind.Absolute, out _)
        && client.RedirectUris.Count > 0
        && client.RedirectUris.All(RedirectUriMatching.IsAcceptableRedirect);
}

/// <summary>The issuer and the MCP resource, from the validated <see cref="IssuerOptions"/>.</summary>
internal sealed class ConfiguredAgentIssuer(IOptions<IssuerOptions> options) : IAgentIssuer
{
    public Uri Issuer => options.Value.Issuer!;

    public Uri McpResource => new(Issuer, AgentAccess.McpPath.TrimStart('/'));
}
