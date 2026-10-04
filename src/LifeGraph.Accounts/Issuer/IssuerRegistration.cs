using System.Security.Cryptography;
using LifeGraph.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using OpenIddict.Server;
using OpenIddict.Server.AspNetCore;

namespace LifeGraph.Accounts.Issuer;

/// <summary>
/// The OAuth 2.1 issuer, in process with the human login (DA-009, ADR 0006). E1 only
/// stands it up: no client is registered and the authorization and token endpoints are
/// not handled yet. E3 adds the clients, the consent page and the endpoint handlers.
/// </summary>
internal static class IssuerRegistration
{
    public const string AuthorizationEndpointPath = "connect/authorize";
    public const string TokenEndpointPath = "connect/token";

    public const string MissingKeysMessage =
        "The OAuth issuer has no signing or encryption key: set Accounts:Issuer:UseEphemeralKeys to true. " +
        "Persistent keys arrive in E3 (DA-028).";

    private const int EphemeralRsaKeySize = 2048;

    public static IServiceCollection AddLifeGraphIssuer(this IServiceCollection services)
    {
        // Without a key OpenIddict only fails on the first request, and then every request
        // (health included) answers 500. Ephemeral keys are the only key source until E3 adds
        // persistent ones (DA-028), so the host refuses to start without them (GEN-052).
        services.AddOptions<IssuerOptions>()
            .BindConfiguration(IssuerOptions.SectionName)
            .Validate(issuer => issuer.UseEphemeralKeys, MissingKeysMessage)
            .ValidateOnStart();

        services.AddOpenIddict()
            .AddCore(core => core
                .UseEntityFrameworkCore()
                .UseDbContext<LifeGraphDbContext>()
                .ReplaceDefaultEntities<Guid>())
            .AddServer(server =>
            {
                server.SetAuthorizationEndpointUris(AuthorizationEndpointPath)
                    .SetTokenEndpointUris(TokenEndpointPath);

                server.AllowAuthorizationCodeFlow()
                    .RequireProofKeyForCodeExchange()
                    .AllowRefreshTokenFlow();

                // OAuth 2.1 and the MCP authorization spec allow only S256.
                server.Configure(options => options.CodeChallengeMethods.Remove(OpenIddictConstants.CodeChallengeMethods.Plain));

                server.UseAspNetCore();
            });

        // The host switches come from configuration (GEN-051), so they apply when the options
        // are built, not when the services are registered.
        services.AddOptions<OpenIddictServerOptions>()
            .Configure<IOptions<IssuerOptions>>((server, issuer) =>
            {
                if (issuer.Value.UseEphemeralKeys)
                {
                    // Same algorithms OpenIddict's AddEphemeral*Key helpers use.
                    server.SigningCredentials.Add(new SigningCredentials(
                        new RsaSecurityKey(RSA.Create(EphemeralRsaKeySize)), SecurityAlgorithms.RsaSha256));
                    server.EncryptionCredentials.Add(new EncryptingCredentials(
                        new RsaSecurityKey(RSA.Create(EphemeralRsaKeySize)), SecurityAlgorithms.RsaOAEP, SecurityAlgorithms.Aes256CbcHmacSha512));
                }
            });

        services.AddOptions<OpenIddictServerAspNetCoreOptions>()
            .Configure<IOptions<IssuerOptions>>((aspNetCore, issuer) =>
                aspNetCore.DisableTransportSecurityRequirement = issuer.Value.AllowHttp);

        return services;
    }
}
