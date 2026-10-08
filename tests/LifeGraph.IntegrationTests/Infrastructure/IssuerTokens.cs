using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Server;

namespace LifeGraph.IntegrationTests.Infrastructure;

/// <summary>
/// Re-issues a real access token with one claim changed, signed and encrypted with the
/// issuer's own keys: everything about it is valid except that claim.
/// </summary>
public static class IssuerTokens
{
    public static async Task<string> WithAudienceAsync(LifeGraphApiFactory factory, string accessToken, string audience)
    {
        var server = factory.Services.GetRequiredService<IOptionsMonitor<OpenIddictServerOptions>>().CurrentValue;
        var signing = server.SigningCredentials[0];
        var encryption = server.EncryptionCredentials[0];
        var handler = new JsonWebTokenHandler();

        var read = await handler.ValidateTokenAsync(accessToken, new TokenValidationParameters
        {
            ValidateAudience = false,
            ValidateIssuer = false,
            ValidateLifetime = false,
            IssuerSigningKey = signing.Key,
            TokenDecryptionKey = encryption.Key,
        });
        Assert.True(read.IsValid, read.Exception?.ToString());

        var inner = ((JsonWebToken)read.SecurityToken).InnerToken ?? (JsonWebToken)read.SecurityToken;
        var claims = new Dictionary<string, object>(read.Claims, StringComparer.Ordinal) { ["aud"] = audience };
        return handler.CreateToken(new SecurityTokenDescriptor
        {
            Claims = claims,
            TokenType = inner.Typ,
            SigningCredentials = signing,
            EncryptingCredentials = encryption,
        });
    }
}
