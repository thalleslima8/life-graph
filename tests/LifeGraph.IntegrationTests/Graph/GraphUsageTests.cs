using LifeGraph.Graph.Application;
using LifeGraph.Graph.Contracts;
using LifeGraph.Graph.Domain;
using LifeGraph.Infrastructure.Persistence;
using LifeGraph.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace LifeGraph.IntegrationTests.Graph;

/// <summary>
/// The usage the plan limits compare with (E13): live rows of the person only (DA-264), never the
/// Raiz da Conta (DA-204), the system ontology, Soft Relations or the Relations that touch the root
/// (DA-268), and only the Account in context.
/// </summary>
public sealed class GraphUsageTests(PostgresDatabase database) : IAsyncLifetime
{
    private readonly LifeGraphApiFactory _factory = new(database);
    private GraphWriteHarness _graph = null!;

    public async ValueTask InitializeAsync()
    {
        await database.ResetAsync();
        _graph = new GraphWriteHarness(database, _factory);
    }

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    [Fact]
    public async Task A_new_account_with_its_root_and_system_ontology_uses_nothing()
    {
        var accountId = await _graph.CreateAccountAsync();
        await _graph.AccountRootIdAsync(accountId);

        var usage = await CountAsync(accountId);

        Assert.Equal(new GraphUsageCounts(0, 0, 0, 0, 0, 0), usage);
    }

    [Fact]
    public async Task Each_kind_counts_what_the_person_created()
    {
        var accountId = await _graph.CreateAccountAsync();
        var (first, second, definition) = (Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7());
        await _graph.WrittenAsync(
            accountId,
            new CreateNode(first, "Sêneca"),
            new CreateNode(second, "Cartas a Lucílio"),
            new CreateRelation(Guid.CreateVersion7(), second, first, "written_by"),
            new DefineProperty(definition, "Ano", PropertyValueKind.Number),
            new CreateType(Guid.CreateVersion7(), "Livro", [definition]),
            new CreateCollection(Guid.CreateVersion7(), "Estoicos", null, false),
            new CaptureResource(Guid.CreateVersion7(), "https://example.test/cartas"));

        var usage = await CountAsync(accountId);

        // The Resource is a Node too (DA-264): three Nodes, one of them a Resource.
        Assert.Equal(new GraphUsageCounts(Nodes: 3, Relations: 1, Resources: 1, Collections: 1, CustomTypes: 1, CustomProperties: 1), usage);
    }

    [Fact]
    public async Task Deleted_nodes_and_relations_and_soft_relations_do_not_count()
    {
        var accountId = await _graph.CreateAccountAsync();
        var (kept, deleted, soft) = (Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7());
        var (deletedRelation, softRelation) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        await _graph.WrittenAsync(
            accountId,
            new CreateNode(kept, "Fica"),
            new CreateNode(deleted, "Sai"),
            new CreateNode(soft, "Parecido"),
            new CreateRelation(deletedRelation, kept, soft, "mentions"),
            new CreateRelation(softRelation, soft, kept, "mentions"));
        await _graph.WrittenAsync(accountId, new DeleteRelation(deletedRelation), new DeleteNode(deleted, ExpectedVersion: 1));
        await MakeSoftAsync(softRelation);

        var usage = await CountAsync(accountId);

        Assert.Equal((2, 0), (usage.Nodes, usage.Relations));
    }

    [Fact]
    public async Task Relations_that_touch_the_account_root_do_not_count_but_anchors_to_ordinary_nodes_do()
    {
        var accountId = await _graph.CreateAccountAsync();
        var root = await _graph.AccountRootIdAsync(accountId);
        var (existing, anchored) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        await _graph.WrittenAsync(
            accountId,
            new CreateNode(existing, "Estoicismo"),
            new CreateRelation(Guid.CreateVersion7(), existing, root, "part_of"));
        await _graph.WrittenAsync(
            accountId,
            new CreateNode(anchored, "Sêneca"),
            new CreateRelation(Guid.CreateVersion7(), root, anchored, "mentions"),
            new CreateRelation(Guid.CreateVersion7(), anchored, existing, "part_of"));

        var usage = await CountAsync(accountId);

        // Both Relations to the root are system plumbing; the anchor between two ordinary Nodes counts (DA-268).
        Assert.Equal((2, 1), (usage.Nodes, usage.Relations));
    }

    [Fact]
    public async Task Archived_property_definitions_count_because_they_can_still_be_deleted()
    {
        var accountId = await _graph.CreateAccountAsync();
        var definition = Guid.CreateVersion7();
        await _graph.WrittenAsync(accountId, new DefineProperty(definition, "Ano", PropertyValueKind.Number));
        await _graph.WrittenAsync(accountId, new UpdatePropertyDefinition(definition) { Archived = true });

        var usage = await CountAsync(accountId);

        Assert.Equal(1, usage.CustomProperties);
    }

    [Fact]
    public async Task Another_accounts_rows_never_count()
    {
        var accountId = await _graph.CreateAccountAsync();
        var other = await _graph.CreateAccountAsync();
        await _graph.WrittenAsync(other, new CreateNode(Guid.CreateVersion7(), "Nota da Grace"));

        var usage = await CountAsync(accountId);

        Assert.Equal(0, usage.Nodes);
    }

    private async Task<GraphUsageCounts> CountAsync(Guid accountId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TestAccountContext>().ActAs(accountId);
        var db = scope.ServiceProvider.GetRequiredService<LifeGraphDbContext>();
        var usage = scope.ServiceProvider.GetRequiredService<GraphUsage>();
        return await db.InAccountTransactionAsync(usage.CountAsync, TestContext.Current.CancellationToken);
    }

    private Task MakeSoftAsync(Guid relationId) =>
        database.ExecuteAsMigratorAsync(
            """
            UPDATE relations
            SET assertion = 'soft', origin = 'system', confidence = 0.7, strength = 0.4, kind = @kind,
                inference_method = @method, inference_model_id = 'test-model',
                source_node_id = LEAST(source_node_id, target_node_id),
                target_node_id = GREATEST(source_node_id, target_node_id)
            WHERE id = @soft
            """,
            TestContext.Current.CancellationToken,
            new NpgsqlParameter("kind", Relation.SoftKind),
            new NpgsqlParameter("method", Relation.EmbeddingSimilarityMethod),
            new NpgsqlParameter("soft", relationId));
}
