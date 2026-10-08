using LifeGraph.Infrastructure.Errors;
using Limaj.Framework.Core;

namespace LifeGraph.Accounts.Domain;

/// <summary>
/// A connection of an external agent: the OAuth grant the person authorized, with its scopes
/// (DA-030). One per client and Account, so authorizing the same client again (the same
/// CIMD URL) reuses it, and with it the history its changes are attributed to. The person
/// can rename and revoke it; the client's declared name is display only.
/// </summary>
public sealed class AgentIdentity
{
    public const int NameMaxLength = 100;
    public const int ClientNameMaxLength = 200;
    public const int ClientIdMaxLength = 2048;

    /// <summary>The last use is stamped at most this often, so every agent call does not write a row.</summary>
    public static readonly TimeSpan LastUseResolution = TimeSpan.FromMinutes(1);

    private List<string> _scopes = [];

    private AgentIdentity()
    {
        ClientId = string.Empty;
        ClientName = string.Empty;
        Name = string.Empty;
    }

    public Guid Id { get; private set; }

    public Guid AccountId { get; private set; }

    /// <summary>The OAuth client: a pre-registered id or the URL of its metadata document (DA-029).</summary>
    public string ClientId { get; private set; }

    /// <summary>The name the client declared, cleaned for display; never trusted.</summary>
    public string ClientName { get; private set; }

    /// <summary>What the person calls it.</summary>
    public string Name { get; private set; }

    /// <summary>The issuer's authorization behind it; replaced on every new consent.</summary>
    public Guid? AuthorizationId { get; private set; }

    public IReadOnlyList<string> Scopes => _scopes;

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public DateTimeOffset? LastUsedAt { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public bool IsActive => RevokedAt is null;

    /// <summary>The first consent of this client in the Account.</summary>
    public static AgentIdentity Connect(
        Guid id,
        Guid accountId,
        string clientId,
        string? declaredClientName,
        IEnumerable<string> scopes,
        Guid authorizationId,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);

        var clientName = DisplayNameOf(declaredClientName, clientId);
        return new AgentIdentity
        {
            Id = id,
            AccountId = accountId,
            ClientId = clientId,
            ClientName = clientName,
            Name = Truncate(clientName, NameMaxLength),
            AuthorizationId = authorizationId,
            _scopes = [.. scopes.Distinct(StringComparer.Ordinal)],
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    /// <summary>
    /// A new consent of a client already known to the Account: the same identity, active again,
    /// with the new grant. The person's name for it stays.
    /// </summary>
    /// <returns>The authorization the new one replaces, to revoke; <c>null</c> when there was none.</returns>
    public Guid? Reconnect(string? declaredClientName, IEnumerable<string> scopes, Guid authorizationId, DateTimeOffset now)
    {
        var replaced = AuthorizationId == authorizationId ? null : AuthorizationId;
        ClientName = DisplayNameOf(declaredClientName, ClientId);
        _scopes = [.. scopes.Distinct(StringComparer.Ordinal)];
        AuthorizationId = authorizationId;
        RevokedAt = null;
        UpdatedAt = now;
        return replaced;
    }

    /// <returns><c>true</c> when the name changed.</returns>
    public Result<bool> Rename(string? name, DateTimeOffset now)
    {
        var trimmed = name?.Trim() ?? string.Empty;
        string? problem = trimmed.Length switch
        {
            0 => "Required.",
            > NameMaxLength => $"Must be at most {NameMaxLength} characters.",
            _ when trimmed.Any(char.IsControl) => "Must not contain control characters.",
            _ => null,
        };

        if (problem is not null)
        {
            return Result<bool>.Fail(CommonErrors.ValidationFailed.ToError(
                CommonErrors.ValidationFailedMessage,
                new Dictionary<string, string[]> { ["name"] = [problem] }));
        }

        if (trimmed == Name)
        {
            return Result<bool>.Ok(false);
        }

        Name = trimmed;
        UpdatedAt = now;
        return Result<bool>.Ok(true);
    }

    /// <summary>Ends the connection. The row stays: what the agent did is still attributed to it.</summary>
    /// <returns>The authorization to revoke with it; <c>null</c> when it was already revoked.</returns>
    public Guid? Revoke(DateTimeOffset now)
    {
        if (!IsActive)
        {
            return null;
        }

        var authorizationId = AuthorizationId;
        AuthorizationId = null;
        RevokedAt = now;
        UpdatedAt = now;
        return authorizationId;
    }

    /// <returns><c>true</c> when the stamp moved, so the row must be saved.</returns>
    public bool RecordUse(DateTimeOffset now)
    {
        if (LastUsedAt is { } last && now - last < LastUseResolution)
        {
            return false;
        }

        LastUsedAt = now;
        return true;
    }

    /// <summary>
    /// What to show for a name the client declared: one line, no control characters, within
    /// the limit; the client id when it declared nothing usable.
    /// </summary>
    public static string DisplayNameOf(string? declaredName, string clientId)
    {
        ArgumentNullException.ThrowIfNull(clientId);

        var cleaned = new string((declaredName ?? string.Empty).Select(character => char.IsControl(character) ? ' ' : character).ToArray()).Trim();
        if (cleaned.Length == 0)
        {
            cleaned = Uri.TryCreate(clientId, UriKind.Absolute, out var url) ? url.Host : clientId;
        }

        return Truncate(cleaned, ClientNameMaxLength);
    }

    private static string Truncate(string value, int maxLength) => value.Length <= maxLength ? value : value[..maxLength].TrimEnd();
}
