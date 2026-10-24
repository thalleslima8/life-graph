namespace LifeGraph.Infrastructure.Persistence;

/// <summary>
/// A mutable row that keeps when it was created and last changed (DB-007), in UTC. The
/// <see cref="AuditTimestampsInterceptor"/> fills both on save, whichever path changed the row.
/// </summary>
public interface IAuditTimestamps
{
    DateTimeOffset CreatedAt { get; set; }

    DateTimeOffset UpdatedAt { get; set; }
}
