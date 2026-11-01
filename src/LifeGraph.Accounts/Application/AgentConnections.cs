using System.Buffers.Text;
using LifeGraph.Accounts.Contracts;
using LifeGraph.Accounts.Domain;
using LifeGraph.Accounts.Issuer;
using LifeGraph.Infrastructure.Agents;
using LifeGraph.Infrastructure.Errors;
using LifeGraph.Infrastructure.Identity;
using LifeGraph.Infrastructure.Persistence;
using Limaj.Framework.Core;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace LifeGraph.Accounts.Application;

/// <summary>
/// The person's agent connections (DA-030): a consent creates or reuses the AgentIdentity of
/// the client and gives it a new grant; renaming and revoking happen in "Agentes conectados".
/// A grant and its AgentIdentity change in one transaction, so a revoked connection never
/// keeps a live grant.
/// </summary>
internal sealed class AgentConnections(
    LifeGraphDbContext db,
    ICurrentPrincipal currentPrincipal,
    IOpenIddictApplicationManager applications,
    IOpenIddictAuthorizationManager authorizations,
    IOpenIddictTokenManager tokens,
    ClientDiscontinuation discontinuation,
    IEnumerable<IAgentRevocationParticipant> revocationParticipants,
    IEntitlements entitlements,
    TimeProvider clock) : IAgentIdentities
{
    public const int DefaultPageSize = 50;
    public const int MaxPageSize = 100;

    /// <summary>The consent page's field with the scopes the person checked.</summary>
    public const string GrantedScopeField = "granted_scope";

    /// <summary>A cursor is a base64url UUID (22 characters); anything far longer is not decoded at all.</summary>
    private const int CursorMaxLength = 64;

    private const string NotFoundMessage = "The agent connection was not found.";

    private const string ConflictMessage = "The agent connection kept changing while it was saved. Try again.";

    /// <summary>
    /// How many times a change is applied to a row that changed under it. The only frequent
    /// writer is the last-use stamp, at most once a minute (<see cref="AgentIdentity.LastUseResolution"/>),
    /// so the second attempt is the one that should succeed.
    /// </summary>
    private const int MaxRaceAttempts = 3;

    /// <summary>
    /// The signed-in person's consent to the client: a new grant behind the client's
    /// AgentIdentity, created on first use, with the scopes the person checked among those
    /// offered for the requested ones (DA-122). A checked scope that was not offered is a
    /// validation failure and grants nothing.
    /// </summary>
    public async Task<Result<AgentGrant>> ConnectAsync(AgentConsent consent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(consent);
        var (userId, clientId, requestedScopes, checkedScopes, requiresApprovalForWrites) = consent;
        if (currentPrincipal.Authenticated is not { Type: PrincipalType.Human } human)
        {
            return Result<AgentGrant>.Fail(CommonErrors.Unauthorized.ToError("Sign in to connect an agent."));
        }

        if (ConsentScopes.Chosen(ConsentScopes.Offered(requestedScopes), checkedScopes) is not { } scopes)
        {
            return Result<AgentGrant>.Fail(CommonErrors.ValidationFailed.ToError(
                CommonErrors.ValidationFailedMessage,
                new Dictionary<string, string[]> { [GrantedScopeField] = ["A checked scope was not offered."] }));
        }

        var application = await applications.FindByClientIdAsync(clientId, cancellationToken);
        if (application is null)
        {
            return Result<AgentGrant>.Fail(CommonErrors.NotFound.ToError("The client was not found."));
        }

        var applicationId = await applications.GetIdAsync(application, cancellationToken);
        var declaredName = await applications.GetDisplayNameAsync(application, cancellationToken);

        // A consent that fails takes its new grant with it: no Valid authorization is left
        // without an AgentIdentity, and a reconnect keeps the old grant untouched (DA-126).
        return await db.InAccountResultTransactionAsync(async token =>
        {
            if (await ConnectionLimitReachedAsync(human.AccountId, clientId, token) is { } limitReached)
            {
                return Result<AgentGrant>.Fail(limitReached);
            }

            var now = clock.GetUtcNow();
            var descriptor = new OpenIddictAuthorizationDescriptor
            {
                ApplicationId = applicationId,
                Subject = userId.ToString(),
                Type = AuthorizationTypes.Permanent,
                Status = Statuses.Valid,
                CreationDate = now,
            };
            descriptor.Scopes.UnionWith(scopes);
            var authorization = await authorizations.CreateAsync(descriptor, token);
            var authorizationId = Guid.Parse((await authorizations.GetIdAsync(authorization, token))!);

            var identity = await AttachGrantAsync(
                new ClientGrant(human.AccountId, clientId, declaredName, scopes, authorizationId, requiresApprovalForWrites),
                now,
                token);
            if (identity is null)
            {
                return Result<AgentGrant>.Fail(AccountsErrors.AgentIdentityConflict.ToError(ConflictMessage));
            }

            return Result<AgentGrant>.Ok(new AgentGrant(identity.Id, authorizationId, [.. identity.Scopes]));
        }, cancellationToken);
    }

    /// <summary>
    /// The plan's limit of Agent Connections (DA-262), checked under the Account's connection lock
    /// so two consents cannot both take the last place: only an active AgentIdentity counts, and
    /// reconnecting a client that is still connected is never a new connection. Reactivating a
    /// revoked one takes a place like a first consent. <c>null</c> when the consent may go on.
    /// </summary>
    private async Task<Error?> ConnectionLimitReachedAsync(Guid accountId, string clientId, CancellationToken cancellationToken)
    {
        await db.Database.ExecuteSqlAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({"lifegraph.agent_connections:" + accountId.ToString()}, 0))",
            cancellationToken);
        return await ReachedConnectionLimitAsync(clientId, cancellationToken) is { } limit
            ? PlanErrors.LimitReachedError(PlanLimitKind.AgentConnections, limit)
            : null;
    }

    /// <summary>
    /// The plan's limit of Agent Connections when a consent to <paramref name="clientId"/> would
    /// pass it, so the consent page explains the limit and offers no Allow (DA-262); <c>null</c>
    /// when the consent may go on. Display only: the grant checks again under the lock.
    /// </summary>
    public async Task<int?> ConnectionLimitBlockingAsync(string clientId, CancellationToken cancellationToken)
    {
        if (currentPrincipal.Authenticated is not { Type: PrincipalType.Human })
        {
            return null;
        }

        return await db.InAccountTransactionAsync(token => ReachedConnectionLimitAsync(clientId, token), cancellationToken);
    }

    // Only an active AgentIdentity counts, and a client still connected reconnects freely (DA-262).
    private async Task<int?> ReachedConnectionLimitAsync(string clientId, CancellationToken cancellationToken)
    {
        var active = db.Set<AgentIdentity>().AsNoTracking().Where(identity => identity.RevokedAt == null);
        if (await active.AnyAsync(identity => identity.ClientId == clientId, cancellationToken))
        {
            return null;
        }

        var limit = (await entitlements.OfCurrentAccountAsync(cancellationToken)).Limits.AgentConnections;
        return await active.CountAsync(cancellationToken) >= limit ? limit : null;
    }

    /// <summary>
    /// "Exigir aprovação para escritas" of the signed-in person's connection to
    /// <paramref name="clientId"/>, revoked or not, as the consent page shows it (DA-044):
    /// a reconnect starts from the current choice. <c>false</c> for a client never connected.
    /// </summary>
    public async Task<bool> RequiresApprovalForWritesAsync(string clientId, CancellationToken cancellationToken)
    {
        if (currentPrincipal.Authenticated is not { Type: PrincipalType.Human } human)
        {
            return false;
        }

        return await db.InAccountTransactionAsync(
            token => db.Set<AgentIdentity>().AsNoTracking()
                .Where(entity => entity.AccountId == human.AccountId && entity.ClientId == clientId)
                .Select(entity => entity.RequiresApprovalForWrites)
                .SingleOrDefaultAsync(token),
            cancellationToken);
    }

    /// <summary>The active connections, newest first, by cursor (API-060).</summary>
    public async Task<Result<AgentIdentityPage>> ListAsync(string? cursor, int? limit, CancellationToken cancellationToken)
    {
        var pageSize = limit ?? DefaultPageSize;
        Guid? after = null;
        var errors = new Dictionary<string, string[]>();
        if (pageSize is < 1 or > MaxPageSize)
        {
            errors["limit"] = [$"Must be between 1 and {MaxPageSize}."];
        }

        if (cursor is not null)
        {
            if (DecodeCursor(cursor) is { } decoded)
            {
                after = decoded;
            }
            else
            {
                errors["cursor"] = ["Not a cursor of this list."];
            }
        }

        if (errors.Count > 0)
        {
            return Result<AgentIdentityPage>.Fail(CommonErrors.ValidationFailed.ToError(CommonErrors.ValidationFailedMessage, errors));
        }

        var rows = await db.InAccountTransactionAsync(
            token => db.Set<AgentIdentity>()
                .AsNoTracking()
                .Where(identity => identity.RevokedAt == null)
                .Where(identity => after == null || identity.Id.CompareTo(after.Value) < 0)
                .OrderByDescending(identity => identity.Id)
                .Take(pageSize + 1)
                .ToListAsync(token),
            cancellationToken);

        var shown = rows.Take(pageSize).ToList();
        var discontinued = await discontinuation.DiscontinuedAmongAsync(shown.Select(identity => identity.ClientId), cancellationToken);
        var page = shown.Select(identity => ToView(identity, discontinued.Contains(identity.ClientId))).ToList();
        var nextCursor = rows.Count > pageSize ? EncodeCursor(page[^1].Id) : null;
        return Result<AgentIdentityPage>.Ok(new AgentIdentityPage(page, new AgentIdentityPageInfo(nextCursor)));
    }

    public Task<int> CountActiveOfCurrentAccountAsync(CancellationToken cancellationToken) =>
        db.Set<AgentIdentity>().AsNoTracking().CountAsync(identity => identity.RevokedAt == null, cancellationToken);

    public Task<Result<AgentIdentityView>> GetAsync(Guid agentIdentityId, CancellationToken cancellationToken) =>
        db.InAccountTransactionAsync(async token =>
        {
            var identity = await FindActiveAsync(agentIdentityId, token);
            return identity is null ? NotFound<AgentIdentityView>() : Result<AgentIdentityView>.Ok(await ViewOfAsync(identity, token));
        }, cancellationToken);

    /// <summary>
    /// Renames the connection, turns "exigir aprovação para escritas" on or off (DA-044), or both,
    /// in one transaction: nothing is saved unless every part is valid. Proposals made while the
    /// toggle was on stay the person's to approve. The name is required unless only the toggle
    /// changes.
    /// </summary>
    public Task<Result<AgentIdentityView>> UpdateAsync(Guid agentIdentityId, string? name, bool? requiresApproval, CancellationToken cancellationToken) =>
        db.InAccountResultTransactionAsync(async token =>
        {
            var now = clock.GetUtcNow();
            var changed = await ChangeActiveAsync(
                agentIdentityId,
                identity =>
                {
                    if (name is not null || requiresApproval is null)
                    {
                        var renamed = identity.Rename(name, now);
                        if (!renamed.IsSuccess)
                        {
                            return renamed;
                        }
                    }

                    if (requiresApproval is { } required)
                    {
                        identity.RequireApprovalForWrites(required, now);
                    }

                    return Result<bool>.Ok(true);
                },
                token);
            return changed.IsSuccess
                ? Result<AgentIdentityView>.Ok(await ViewOfAsync(changed.Value!.Identity, token))
                : Result<AgentIdentityView>.Fail(changed.Error!);
        }, cancellationToken);

    /// <summary>
    /// Ends the connection now: its grant, its refresh tokens and its access tokens (DA-031),
    /// and withdraws what it left pending, in the same transaction (DA-135).
    /// </summary>
    public Task<Result> RevokeAsync(Guid agentIdentityId, CancellationToken cancellationToken) =>
        db.InAccountTransactionAsync(async token =>
        {
            var now = clock.GetUtcNow();
            var revoked = await ChangeActiveAsync(agentIdentityId, identity => Result<Guid?>.Ok(identity.Revoke(now)), token);
            if (!revoked.IsSuccess)
            {
                return Result.Fail(revoked.Error!);
            }

            // What the agent left pending is settled in this same transaction (DA-135).
            foreach (var participant in revocationParticipants)
            {
                await participant.AgentRevokedAsync(revoked.Value!.Identity.AccountId, agentIdentityId, now, token);
            }

            if (revoked.Value!.Outcome is { } authorizationId)
            {
                await RevokeGrantAsync(authorizationId, token);
            }

            return Result.Ok();
        }, cancellationToken);

    public Task<bool> TryRecordUseAsync(Guid agentIdentityId, Guid authorizationId, CancellationToken cancellationToken) =>
        db.InAccountTransactionAsync(async token =>
        {
            var identity = await FindActiveAsync(agentIdentityId, token);
            if (identity is null || identity.AuthorizationId != authorizationId || await IsDiscontinuedAsync(identity.ClientId, token))
            {
                return false;
            }

            if (identity.RecordUse(clock.GetUtcNow()))
            {
                try
                {
                    await db.SaveChangesAsync(token);
                }
                catch (DbUpdateConcurrencyException)
                {
                    // Another call of the same agent stamped it, or the row changed (renamed,
                    // reconnected, revoked) since it was read: the stamp is dropped and the
                    // connection checked again as it is now.
                    db.ChangeTracker.Clear();
                    var current = await FindActiveAsync(agentIdentityId, token);
                    return current is not null && current.AuthorizationId == authorizationId;
                }
            }

            return true;
        }, cancellationToken);

    /// <summary>
    /// Gives the client's AgentIdentity in the Account the new grant, creating it on first use,
    /// and saves it before the replaced grant is revoked, so the issuer's own saves never carry
    /// it. A parallel first consent of the same client (the unique index) or a write since the
    /// read (the last-use stamp, a rename) makes it read the row again and reconnect that one:
    /// the later consent wins, as if they had come one after the other.
    /// </summary>
    /// <returns><c>null</c> when other writers kept winning the race.</returns>
    private async Task<AgentIdentity?> AttachGrantAsync(ClientGrant grant, DateTimeOffset now, CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= MaxRaceAttempts; attempt++)
        {
            var identity = await db.Set<AgentIdentity>()
                .SingleOrDefaultAsync(entity => entity.AccountId == grant.AccountId && entity.ClientId == grant.ClientId, cancellationToken);
            Guid? replaced = null;
            if (identity is null)
            {
                identity = AgentIdentity.Connect(Guid.CreateVersion7(), grant, now);
                db.Add(identity);
            }
            else
            {
                replaced = identity.Reconnect(grant, now);
            }

            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException exception) when (exception is DbUpdateConcurrencyException || IsUniqueViolation(exception))
            {
                // EF rolled the transaction back to its savepoint, so it is still usable.
                db.ChangeTracker.Clear();
                continue;
            }

            if (replaced is { } replacedAuthorizationId)
            {
                await RevokeGrantAsync(replacedAuthorizationId, cancellationToken);
            }

            return identity;
        }

        return null;
    }

    /// <summary>
    /// Applies <paramref name="change"/> to the active AgentIdentity and saves it. A write since
    /// the read (the last-use stamp of an agent call, another rename) loses nothing: the row is
    /// read again, as it is now, and the change applied to it; only when other writers keep
    /// winning is it a conflict (API-030).
    /// </summary>
    private async Task<Result<(AgentIdentity Identity, T Outcome)>> ChangeActiveAsync<T>(
        Guid agentIdentityId,
        Func<AgentIdentity, Result<T>> change,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= MaxRaceAttempts; attempt++)
        {
            var identity = await FindActiveAsync(agentIdentityId, cancellationToken);
            if (identity is null)
            {
                return NotFound<(AgentIdentity, T)>();
            }

            var changed = change(identity);
            if (!changed.IsSuccess)
            {
                return Result<(AgentIdentity, T)>.Fail(changed.Error!);
            }

            try
            {
                await db.SaveChangesAsync(cancellationToken);
                return Result<(AgentIdentity, T)>.Ok((identity, changed.Value!));
            }
            catch (DbUpdateConcurrencyException)
            {
                db.ChangeTracker.Clear();
            }
        }

        return Result<(AgentIdentity, T)>.Fail(AccountsErrors.AgentIdentityConflict.ToError(ConflictMessage));
    }

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };

    // RLS keeps the lookup inside the caller's Account; another Account's id is just absent.
    private Task<AgentIdentity?> FindActiveAsync(Guid agentIdentityId, CancellationToken cancellationToken) =>
        db.Set<AgentIdentity>().SingleOrDefaultAsync(
            identity => identity.Id == agentIdentityId && identity.RevokedAt == null,
            cancellationToken);

    private async Task RevokeGrantAsync(Guid authorizationId, CancellationToken cancellationToken)
    {
        var authorization = await authorizations.FindByIdAsync(authorizationId.ToString(), cancellationToken);
        if (authorization is not null)
        {
            await authorizations.TryRevokeAsync(authorization, cancellationToken);
        }

        await tokens.RevokeByAuthorizationIdAsync(authorizationId.ToString(), cancellationToken);
    }

    private async Task<AgentIdentityView> ViewOfAsync(AgentIdentity identity, CancellationToken cancellationToken) =>
        ToView(identity, await IsDiscontinuedAsync(identity.ClientId, cancellationToken));

    // Only the product's scopes: offline_access is part of every grant and says nothing (DA-125).
    private static AgentIdentityView ToView(AgentIdentity identity, bool isDiscontinued) =>
        new(
            identity.Id,
            identity.Name,
            identity.ClientName,
            identity.ClientId,
            [.. identity.Scopes.Where(AgentAccess.Scopes.Contains)],
            isDiscontinued ? AgentIdentityStatus.Discontinued : AgentIdentityStatus.Active,
            identity.CreatedAt,
            identity.LastUsedAt,
            identity.RequiresApprovalForWrites);

    private Task<bool> IsDiscontinuedAsync(string clientId, CancellationToken cancellationToken) =>
        discontinuation.IsDiscontinuedAsync(clientId, cancellationToken);

    private static Result<T> NotFound<T>() => Result<T>.Fail(AccountsErrors.AgentIdentityNotFound.ToError(NotFoundMessage));

    private static string EncodeCursor(Guid id) => Base64Url.EncodeToString(id.ToByteArray(bigEndian: true));

    private static Guid? DecodeCursor(string cursor)
    {
        if (cursor.Length > CursorMaxLength)
        {
            return null;
        }

        try
        {
            var bytes = Base64Url.DecodeFromChars(cursor);
            return bytes.Length == 16 ? new Guid(bytes, bigEndian: true) : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }
}

/// <summary>The person's decision on the consent page.</summary>
/// <param name="UserId">The signed-in person.</param>
/// <param name="RequestedScopes">What the client asked for; only the product's scopes among them are offered.</param>
/// <param name="CheckedScopes">The scopes the person checked.</param>
/// <param name="RequiresApprovalForWrites">"Exigir aprovação para escritas" (DA-044); <c>null</c> when the page did not offer it, which keeps the connection's current choice.</param>
public sealed record AgentConsent(
    Guid UserId,
    string ClientId,
    IEnumerable<string> RequestedScopes,
    IEnumerable<string?> CheckedScopes,
    bool? RequiresApprovalForWrites);

/// <summary>What a consent produced: the AgentIdentity, the grant behind it and its scopes.</summary>
public sealed record AgentGrant(Guid AgentIdentityId, Guid AuthorizationId, IReadOnlyList<string> Scopes);

public sealed record AgentIdentityPage(IReadOnlyList<AgentIdentityView> Data, AgentIdentityPageInfo Page);

/// <param name="NextCursor">Opaque; <c>null</c> on the last page.</param>
public sealed record AgentIdentityPageInfo(string? NextCursor);
