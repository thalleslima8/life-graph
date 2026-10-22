using LifeGraph.Graph.Contracts;
using LifeGraph.Graph.Domain;
using LifeGraph.Infrastructure.Accounts;
using LifeGraph.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LifeGraph.Graph.Persistence;

/// <summary>
/// Creates the system Type <c>Resource</c> and its Properties an Account lacks (DA-149). Not a
/// graph write: the rows exist before any capture, so no GraphChangeSet, and Undo, Delete and
/// Purge never reach them. Idempotent; an advisory lock per Account keeps two sign-ins at
/// once from racing on the unique system keys.
/// </summary>
internal sealed class SystemOntologySetup(LifeGraphDbContext db, TimeProvider clock) : IAccountSetupParticipant
{
    public async Task EnsureAsync(Guid accountId, CancellationToken cancellationToken)
    {
        await db.Database.ExecuteSqlAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({accountId.ToString()}, 0))",
            cancellationToken);

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

        var now = clock.GetUtcNow();
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

        await db.SaveChangesAsync(cancellationToken);
    }
}
