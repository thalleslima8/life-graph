using LifeGraph.Graph.Contracts;
using LifeGraph.Graph.Domain;
using LifeGraph.Infrastructure.Accounts;
using LifeGraph.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LifeGraph.Graph.Persistence;

/// <summary>
/// Creates the system Types an Account lacks: <c>Resource</c> with its Properties (DA-149) and
/// the Type of the Raiz da Conta (DA-189). Not a graph write: the rows exist before any capture,
/// so no GraphChangeSet, and Undo, Delete and Purge never reach them. Idempotent; an advisory
/// lock per Account keeps two sign-ins at once from racing on the unique system keys.
/// </summary>
internal sealed class SystemOntologySetup(LifeGraphDbContext db, TimeProvider clock) : IAccountSetupParticipant
{
    public async Task EnsureAsync(Guid accountId, CancellationToken cancellationToken)
    {
        await AccountSetupLock.TakeAsync(db, accountId, cancellationToken);

        var now = clock.GetUtcNow();
        await EnsureResourceAsync(accountId, now, cancellationToken);
        if (!await db.Set<NodeType>().AnyAsync(type => type.SystemKey == SystemOntology.AccountRootTypeKey, cancellationToken))
        {
            db.Add(NodeType.CreateSystem(Guid.CreateVersion7(now), accountId, SystemOntology.AccountRootTypeKey, SystemOntology.AccountRootTypeName, [], now));
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task EnsureResourceAsync(Guid accountId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var definitions = await db.Set<PropertyDefinition>()
            .Where(definition => definition.SystemKey != null)
            .ToDictionaryAsync(definition => definition.SystemKey!, cancellationToken);
        var resource = await db.Set<NodeType>()
            .Include(type => type.Properties)
            .SingleOrDefaultAsync(type => type.SystemKey == SystemOntology.ResourceTypeKey, cancellationToken);
        if (resource is not null && SystemOntology.ResourceProperties.All(property => definitions.ContainsKey(property.Key)))
        {
            return;
        }

        foreach (var property in SystemOntology.ResourceProperties.Where(property => !definitions.ContainsKey(property.Key)))
        {
            var definition = PropertyDefinition.DefineSystem(Guid.CreateVersion7(now), accountId, property, now);
            definitions.Add(property.Key, definition);
            db.Add(definition);
        }

        var attached = SystemOntology.ResourceProperties.Select(property => definitions[property.Key]).ToList();
        if (resource is null)
        {
            db.Add(NodeType.CreateSystem(Guid.CreateVersion7(now), accountId, SystemOntology.ResourceTypeKey, SystemOntology.ResourceTypeName, attached, now));
        }
        else
        {
            resource.AttachSystem(attached, now);
        }
    }
}

/// <summary>
/// The advisory lock of an Account's setup, held to the end of its transaction: the setups of
/// two sign-ins at once run one after the other. Taking it again in the same transaction is free.
/// </summary>
internal static class AccountSetupLock
{
    public static Task TakeAsync(LifeGraphDbContext db, Guid accountId, CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({accountId.ToString()}, 0))",
            cancellationToken);
}
