using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace LifeGraph.Infrastructure.Persistence;

/// <summary>
/// Stamps <see cref="IAuditTimestamps"/> rows on save: both columns on insert, only
/// <c>updated_at</c> afterwards. An interceptor, not the callers, because rows such as the
/// Identity user change through framework code (UserManager) the app does not write.
/// </summary>
public sealed class AuditTimestampsInterceptor(TimeProvider clock) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Stamp(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Stamp(eventData.Context);
        return ValueTask.FromResult(result);
    }

    private void Stamp(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        var now = clock.GetUtcNow();
        foreach (var entry in context.ChangeTracker.Entries<IAuditTimestamps>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Entity.CreatedAt = now;
                    entry.Entity.UpdatedAt = now;
                    break;
                case EntityState.Modified:
                    entry.Entity.UpdatedAt = now;
                    // Identity updates by attaching the whole user, which marks every column.
                    entry.Property(entity => entity.CreatedAt).IsModified = false;
                    break;
            }
        }
    }
}
