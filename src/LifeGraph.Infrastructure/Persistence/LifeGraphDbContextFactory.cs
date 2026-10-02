using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace LifeGraph.Infrastructure.Persistence;

/// <summary>
/// Used by <c>dotnet ef</c>. Migrations run as the migration owner role, never as the
/// application role (DB-060), so this reads its own connection string.
/// </summary>
public sealed class LifeGraphDbContextFactory : IDesignTimeDbContextFactory<LifeGraphDbContext>
{
    public const string ConnectionStringVariable = "ConnectionStrings__Migrations";

    public LifeGraphDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionStringVariable)
            ?? throw new InvalidOperationException(
                $"Set {ConnectionStringVariable} to the migration owner's connection string.");

        var options = LifeGraphDbContextOptions.Configure(new DbContextOptionsBuilder<LifeGraphDbContext>(), connectionString);
        return new LifeGraphDbContext(options.Options, new AnonymousAccountContext());
    }
}
