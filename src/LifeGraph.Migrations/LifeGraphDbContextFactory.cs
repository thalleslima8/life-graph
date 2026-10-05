using LifeGraph.Graph.Persistence;
using LifeGraph.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace LifeGraph.Migrations;

/// <summary>
/// Used by <c>dotnet ef</c>. Migrations run as the migration owner role, never as the
/// application role (DB-060), so this reads its own connection string.
/// </summary>
public sealed class LifeGraphDbContextFactory : IDesignTimeDbContextFactory<LifeGraphDbContext>
{
    public const string ConnectionStringVariable = "ConnectionStrings__Migrations";

    /// <summary>
    /// Every module that contributes to the model, listed by hand rather than scanned (DA-111).
    /// A module registered in the host but missing here makes the pending-model test fail.
    /// </summary>
    public static IReadOnlyList<IModelContributor> ModelContributors { get; } = [new GraphModelContributor()];

    public LifeGraphDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionStringVariable)
            ?? throw new InvalidOperationException(
                $"Set {ConnectionStringVariable} to the migration owner's connection string.");

        return Create(connectionString);
    }

    /// <summary>The design-time context on any connection: the test fixtures migrate with it.</summary>
    public static LifeGraphDbContext Create(string connectionString)
    {
        var options = LifeGraphDbContextOptions.Configure(new DbContextOptionsBuilder<LifeGraphDbContext>(), connectionString);
        return new LifeGraphDbContext(options.Options, new AnonymousAccountContext(), ModelContributors);
    }
}
