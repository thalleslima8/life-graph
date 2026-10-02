using Microsoft.EntityFrameworkCore;

namespace LifeGraph.Infrastructure.Persistence;

public sealed class LifeGraphDbContext(
    DbContextOptions<LifeGraphDbContext> options,
    IAccountContext accountContext) : DbContext(options)
{
    internal Guid? AccountId => accountContext.AccountId;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("vector");
        modelBuilder.HasPostgresExtension("unaccent");
    }
}
