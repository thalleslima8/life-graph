using System.Text.Json;
using LifeGraph.Accounts.Contracts;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace LifeGraph.Accounts.Issuer;

/// <summary>
/// The one shape of an agent client in the issuer (DA-029): public (no secret), PKCE,
/// authorization code and refresh token only, explicit consent, and tokens only for the MCP
/// resource. Pre-registered and CIMD clients differ only in where the metadata comes from.
/// </summary>
internal static class PublicClients
{
    /// <summary>Where a client's metadata came from, kept on the application.</summary>
    public const string OriginProperty = "lifegraph_origin";

    public const string ConfigurationOrigin = "configuration";
    public const string MetadataDocumentOrigin = "client_id_metadata_document";

    public static OpenIddictApplicationDescriptor Describe(
        string clientId,
        string displayName,
        IEnumerable<Uri> redirectUris,
        Uri mcpResource,
        string origin)
    {
        var descriptor = new OpenIddictApplicationDescriptor
        {
            ClientId = clientId,
            DisplayName = displayName,
            ClientType = ClientTypes.Public,
            ApplicationType = ApplicationTypes.Web,
            ConsentType = ConsentTypes.Explicit,
            Permissions =
            {
                Permissions.Endpoints.Authorization,
                Permissions.Endpoints.Token,
                Permissions.GrantTypes.AuthorizationCode,
                Permissions.GrantTypes.RefreshToken,
                Permissions.ResponseTypes.Code,
                Permissions.Prefixes.Resource + mcpResource.AbsoluteUri,
            },
            Requirements = { Requirements.Features.ProofKeyForCodeExchange },
        };

        foreach (var scope in AgentAccess.Scopes.Append(Scopes.OfflineAccess))
        {
            descriptor.Permissions.Add(Permissions.Prefixes.Scope + scope);
        }

        foreach (var redirectUri in redirectUris)
        {
            descriptor.RedirectUris.Add(redirectUri);
        }

        descriptor.Properties[OriginProperty] = JsonSerializer.SerializeToElement(origin);
        return descriptor;
    }

    public static async Task<string?> OriginOfAsync(IOpenIddictApplicationManager applications, object application, CancellationToken cancellationToken) =>
        (await applications.GetPropertiesAsync(application, cancellationToken)).TryGetValue(OriginProperty, out var origin)
        && origin.ValueKind == JsonValueKind.String
            ? origin.GetString()
            : null;

    /// <summary>Creates the client, or makes the stored one match the descriptor.</summary>
    public static async Task UpsertAsync(IOpenIddictApplicationManager applications, OpenIddictApplicationDescriptor descriptor, CancellationToken cancellationToken)
    {
        var existing = await applications.FindByClientIdAsync(descriptor.ClientId!, cancellationToken);
        if (existing is null)
        {
            await applications.CreateAsync(descriptor, cancellationToken);
            return;
        }

        await applications.UpdateAsync(existing, descriptor, cancellationToken);
    }
}
