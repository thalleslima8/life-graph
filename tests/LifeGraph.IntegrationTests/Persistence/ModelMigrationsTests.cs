using LifeGraph.Graph.Domain;
using LifeGraph.Infrastructure.Persistence;
using LifeGraph.IntegrationTests.Infrastructure;
using LifeGraph.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LifeGraph.IntegrationTests.Persistence;

/// <summary>
/// One context, modelled by contributors that the web host takes from DI and the design-time
/// factory lists by hand (DA-111). A module wired into one but not the other, or a model
/// change without a migration, fails here instead of in production.
/// </summary>
public sealed class ModelMigrationsTests(PostgresDatabase database) : IAsyncLifetime
{
    private readonly LifeGraphApiFactory _factory = new(database);

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    [Fact]
    public async Task The_design_time_model_has_no_change_missing_from_the_migrations()
    {
        await using var context = LifeGraphDbContextFactory.Create(database.MigratorConnectionString);

        Assert.False(context.Database.HasPendingModelChanges());
    }

    [Fact]
    public async Task The_runtime_model_has_no_change_missing_from_the_migrations()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<LifeGraphDbContext>();

        Assert.False(context.Database.HasPendingModelChanges());
    }

    [Fact]
    public void The_host_and_the_design_time_factory_use_the_same_contributors()
    {
        var registered = _factory.Services.GetServices<IModelContributor>().Select(contributor => contributor.GetType());
        var listed = LifeGraphDbContextFactory.ModelContributors.Select(contributor => contributor.GetType());

        Assert.Equal(listed.OrderBy(type => type.FullName), registered.OrderBy(type => type.FullName));
    }

    [Fact]
    public async Task A_context_without_a_contributor_gets_its_own_model_not_a_cached_one()
    {
        await using var designTime = LifeGraphDbContextFactory.Create(database.MigratorConnectionString);
        var options = LifeGraphDbContextOptions.Configure(new DbContextOptionsBuilder<LifeGraphDbContext>(), database.MigratorConnectionString);
        await using var withoutGraph = new LifeGraphDbContext(options.Options, new AnonymousAccountContext(), []);

        Assert.NotNull(designTime.Model.FindEntityType(typeof(Node)));
        Assert.Null(withoutGraph.Model.FindEntityType(typeof(Node)));
        Assert.True(withoutGraph.Database.HasPendingModelChanges());
    }

    // DA-022: the update only matches the version it read, so a concurrent write is refused.
    [Fact]
    public async Task The_node_version_is_a_concurrency_token()
    {
        await using var context = LifeGraphDbContextFactory.Create(database.MigratorConnectionString);

        var version = context.Model.FindEntityType(typeof(Node))!.FindProperty(nameof(Node.Version))!;

        Assert.True(version.IsConcurrencyToken);
    }
}
