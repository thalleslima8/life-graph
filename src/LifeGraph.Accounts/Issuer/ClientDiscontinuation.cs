using System.Text.Json;
using LifeGraph.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using OpenIddict.EntityFrameworkCore.Models;

namespace LifeGraph.Accounts.Issuer;

/// <summary>
/// A pre-registered client that is no longer in the configuration is discontinued (DA-123): the
/// authorization and token endpoints and the MCP endpoint refuse it, while the record stays,
/// because its AgentIdentities and the history attributed to them point at it. Read live from
/// the configuration, so nothing has to reach the database at startup. A client the issuer no
/// longer knows at all counts as discontinued too. Clients identified by their metadata document
/// (CIMD) never are.
/// </summary>
internal sealed class ClientDiscontinuation(
    IOpenIddictApplicationManager applications,
    LifeGraphDbContext db,
    IOptions<IssuerOptions> options)
{
    public async Task<bool> IsDiscontinuedAsync(string clientId, CancellationToken cancellationToken)
    {
        var application = await applications.FindByClientIdAsync(clientId, cancellationToken);
        return application is null
            || IsDiscontinued(clientId, await PublicClients.OriginOfAsync(applications, application, cancellationToken));
    }

    /// <summary>The discontinued ones among <paramref name="clientIds"/>, read in one query (DB-022).</summary>
    public async Task<IReadOnlySet<string>> DiscontinuedAmongAsync(IEnumerable<string> clientIds, CancellationToken cancellationToken)
    {
        var distinct = clientIds.Distinct(StringComparer.Ordinal).ToList();
        if (distinct.Count == 0)
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }

        // The issuer's tables have no RLS (DA-119): the lookup needs no Account transaction.
        var known = await db.Set<OpenIddictEntityFrameworkCoreApplication<Guid>>()
            .AsNoTracking()
            .Where(application => application.ClientId != null && distinct.Contains(application.ClientId))
            .Select(application => new { application.ClientId, application.Properties })
            .ToDictionaryAsync(application => application.ClientId!, application => application.Properties, StringComparer.Ordinal, cancellationToken);

        return distinct
            .Where(clientId => !known.TryGetValue(clientId, out var properties) || IsDiscontinued(clientId, OriginIn(properties)))
            .ToHashSet(StringComparer.Ordinal);
    }

    private bool IsDiscontinued(string clientId, string? origin) =>
        origin == PublicClients.ConfigurationOrigin && !options.Value.Clients.Any(client => client.ClientId == clientId);

    // The same property the application manager reads, from the stored JSON of the row.
    private static string? OriginIn(string? properties)
    {
        if (string.IsNullOrEmpty(properties))
        {
            return null;
        }

        using var document = JsonDocument.Parse(properties);
        return document.RootElement.ValueKind == JsonValueKind.Object
            && document.RootElement.TryGetProperty(PublicClients.OriginProperty, out var origin)
            && origin.ValueKind == JsonValueKind.String
                ? origin.GetString()
                : null;
    }
}
