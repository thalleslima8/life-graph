using LifeGraph.Accounts.Contracts;
using LifeGraph.Graph.Application;
using LifeGraph.Graph.Contracts;
using LifeGraph.Infrastructure.Identity;
using LifeGraph.IntegrationTests.Infrastructure;
using LifeGraph.IntegrationTests.Semantic;
using Limaj.Framework.Core;
using Microsoft.Extensions.DependencyInjection;

namespace LifeGraph.IntegrationTests.Graph;

/// <summary>
/// The plan's limits in the single pipeline (E13): a write that grows a kind past the limit is
/// refused whole with <c>plan.limit_reached</c>, and nothing else is ever blocked by the plan
/// (DA-086, DA-263). The Raiz da Conta and its anchors never take a place (DA-204, DA-268).
/// </summary>
public sealed class PlanLimitTests(PostgresDatabase database) : IAsyncLifetime
{
    private const string LiveNodesSql = "SELECT count(*) FROM nodes WHERE system_key IS NULL AND deleted_at IS NULL";

    private readonly LifeGraphApiFactory _factory = new(database, new Dictionary<string, string>
    {
        [$"{FreeLimits}:{nameof(PlanLimits.Nodes)}"] = "2",
        [$"{FreeLimits}:{nameof(PlanLimits.Relations)}"] = "1",
        [$"{FreeLimits}:{nameof(PlanLimits.Collections)}"] = "1",
        [$"{FreeLimits}:{nameof(PlanLimits.CustomTypes)}"] = "1",
    });

    private GraphWriteHarness _graph = null!;
    private Guid _account;

    private static string FreeLimits => "Entitlements:Free";

    public async ValueTask InitializeAsync()
    {
        await database.ResetAsync();
        _graph = new GraphWriteHarness(database, _factory);
        _account = await _graph.CreateAccountAsync();
    }

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    [Fact]
    public async Task At_the_limit_a_new_node_is_refused_whole_and_nothing_is_stored()
    {
        await _graph.WrittenAsync(_account, new CreateNode(Guid.CreateVersion7(), "Sêneca"), new CreateNode(Guid.CreateVersion7(), "Epicteto"));
        var changeSets = await _graph.CountAsync("changesets");

        var refused = await _graph.WriteAsync(_account, new CreateTag(Guid.CreateVersion7(), "estoicos"), new CreateNode(Guid.CreateVersion7(), "Marco Aurélio"));

        AssertLimitReached(refused, "nodes", 2);
        Assert.Equal(2L, await _graph.ScalarAsync<long>(LiveNodesSql));
        Assert.Equal(0L, await _graph.CountAsync("tags"));
        Assert.Equal(changeSets, await _graph.CountAsync("changesets"));
    }

    // DA-086, DA-263: over the limit (here after Undo and restore) the person still edits,
    // deletes, undoes and restores; only a new creation is refused.
    [Fact]
    public async Task Past_the_limit_editing_deleting_undoing_and_restoring_still_succeed()
    {
        var (first, second, third) = (Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7());
        await _graph.WrittenAsync(_account, new CreateNode(first, "Sêneca"), new CreateNode(second, "Epicteto"));
        var deletion = await _graph.WrittenAsync(_account, new DeleteNode(second, ExpectedVersion: 1));
        await _graph.WrittenAsync(_account, new CreateNode(third, "Marco Aurélio"));

        Assert.True((await _graph.UndoAsync(_account, deletion)).IsSuccess);
        Assert.Equal(3L, await _graph.ScalarAsync<long>(LiveNodesSql));

        Assert.True((await _graph.WriteAsync(_account, new UpdateNode(first, ExpectedVersion: 1) { Title = "Lúcio Sêneca" })).IsSuccess);
        Assert.True((await _graph.WriteAsync(_account, new DeleteNode(third, ExpectedVersion: 1))).IsSuccess);
        Assert.True((await _graph.RestoreAsync(_account, GraphEntityKind.Node, third)).IsSuccess);
        Assert.Equal(3L, await _graph.ScalarAsync<long>(LiveNodesSql));

        AssertLimitReached(await _graph.WriteAsync(_account, new CreateNode(Guid.CreateVersion7(), "Zenão")), "nodes", 2);
    }

    // DA-204, DA-268: Relations that touch the Raiz da Conta do not take a place; others do.
    [Fact]
    public async Task Relations_to_the_account_root_never_take_a_place()
    {
        var root = await _graph.AccountRootIdAsync(_account);
        var (first, second) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        await _graph.WrittenAsync(
            _account,
            new CreateNode(first, "Sêneca"),
            new CreateNode(second, "Cartas"),
            new CreateRelation(Guid.CreateVersion7(), first, root, "part_of"),
            new CreateRelation(Guid.CreateVersion7(), second, root, "part_of"),
            new CreateRelation(Guid.CreateVersion7(), second, first, "written_by"));

        var refused = await _graph.WriteAsync(_account, new CreateRelation(Guid.CreateVersion7(), first, second, "mentions"));

        AssertLimitReached(refused, "relations", 1);
    }

    // DA-204: the system creates the Raiz da Conta whatever room the plan leaves.
    [Fact]
    public async Task The_account_root_is_created_even_when_the_plan_leaves_no_room()
    {
        await using var noRoom = new LifeGraphApiFactory(database, new Dictionary<string, string>
        {
            [$"{FreeLimits}:{nameof(PlanLimits.Nodes)}"] = "0",
            [$"{FreeLimits}:{nameof(PlanLimits.Relations)}"] = "0",
        });
        var graph = new GraphWriteHarness(database, noRoom);

        var account = await graph.CreateAccountAsync();

        Assert.NotEqual(Guid.Empty, await graph.AccountRootIdAsync(account));
        AssertLimitReached(await graph.WriteAsync(account, new CreateNode(Guid.CreateVersion7(), "Sêneca")), "nodes", 0);
    }

    [Fact]
    public async Task Collections_and_custom_types_are_refused_past_their_limits()
    {
        await _graph.WrittenAsync(
            _account,
            new CreateCollection(Guid.CreateVersion7(), "Estoicos", null, false),
            new CreateType(Guid.CreateVersion7(), "Livro"));

        AssertLimitReached(await _graph.WriteAsync(_account, new CreateCollection(Guid.CreateVersion7(), "Cínicos", null, false)), "collections", 1);
        AssertLimitReached(await _graph.WriteAsync(_account, new CreateType(Guid.CreateVersion7(), "Carta")), "custom_types", 1);
    }

    // DA-086, DA-263: after a downgrade the Account is over its limit, and exporting and reading
    // the provenance of its GraphChangeSets still succeed; only a new creation is refused.
    [Fact]
    public async Task Past_the_limit_exporting_and_reading_provenance_still_succeed()
    {
        await _graph.WrittenAsync(_account, new CreateNode(Guid.CreateVersion7(), "Sêneca"), new CreateNode(Guid.CreateVersion7(), "Epicteto"));
        await using var downgraded = new LifeGraphApiFactory(database, new Dictionary<string, string>
        {
            [$"{FreeLimits}:{nameof(PlanLimits.Nodes)}"] = "1",
        });
        var graph = new GraphWriteHarness(database, downgraded);
        var person = new AuthenticatedPrincipal(_account, PrincipalType.Human);
        var cancellationToken = TestContext.Current.CancellationToken;

        var export = await graph.AsAsync(person, services => services.GetRequiredService<AccountExports>().RequestAsync(cancellationToken));
        var provenance = await graph.AsAsync(person, services =>
            services.GetRequiredService<GraphReads>().ListChangeSetsAsync(null, null, null, new ChangeSetFeedFilter(), cancellationToken));

        Assert.True(export.IsSuccess);
        Assert.Equal(1L, await database.QueryScalarAsMigratorAsync<long>("SELECT count(*) FROM account_exports"));
        Assert.True(provenance.IsSuccess);
        Assert.NotEmpty(provenance.Value!.Data);
        AssertLimitReached(await graph.WriteAsync(_account, new CreateNode(Guid.CreateVersion7(), "Marco Aurélio")), "nodes", 1);
    }

    // DA-264: accepting a Soft Relation creates a Relation, so at the limit it is refused and
    // the suggestion stays for later.
    [Fact]
    public async Task At_the_limit_accepting_a_soft_relation_is_refused_and_the_suggestion_kept()
    {
        await using var noRelations = new LifeGraphApiFactory(database, new Dictionary<string, string>
        {
            [$"{FreeLimits}:{nameof(PlanLimits.Relations)}"] = "0",
        });
        var graph = new GraphWriteHarness(database, noRelations);
        var embedder = noRelations.Services.GetRequiredService<DeterministicEmbedder>();
        var account = await graph.CreateAccountAsync();
        var (first, second) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        embedder.PinAtAngle("Marco Aurélio", 0.0);
        embedder.PinAtAngle("Estoicismo", 0.1);
        await graph.WrittenAsync(account, new CreateNode(first, "Marco Aurélio"), new CreateNode(second, "Estoicismo"));
        for (var batch = 0; batch < 10 && await graph.RunDueJobsAsync() > 0; batch++)
        {
        }

        var suggestion = await graph.ScalarAsync<Guid>("SELECT id FROM relations WHERE assertion = 'soft'");
        var relations = await graph.CountAsync("relations");

        AssertLimitReached(await graph.WriteAsync(account, new AcceptSoftRelation(second, suggestion, Guid.CreateVersion7())), "relations", 0);
        Assert.Equal(relations, await graph.CountAsync("relations"));
        Assert.Equal(suggestion, await graph.ScalarAsync<Guid>("SELECT id FROM relations WHERE assertion = 'soft'"));
    }

    // The plan lock: two writes cannot both take the last place.
    [Fact]
    public async Task Two_parallel_creates_cannot_both_take_the_last_place()
    {
        await _graph.WrittenAsync(_account, new CreateNode(Guid.CreateVersion7(), "Sêneca"));

        var outcomes = await Task.WhenAll(
            _graph.WriteAsync(_account, new CreateNode(Guid.CreateVersion7(), "Epicteto")),
            _graph.WriteAsync(_account, new CreateNode(Guid.CreateVersion7(), "Marco Aurélio")));

        Assert.Single(outcomes, outcome => outcome.IsSuccess);
        AssertLimitReached(Assert.Single(outcomes, outcome => !outcome.IsSuccess), "nodes", 2);
        Assert.Equal(2L, await _graph.ScalarAsync<long>(LiveNodesSql));
    }

    private static void AssertLimitReached(Result<GraphWriteReceipt> written, string kind, int limit)
    {
        Assert.False(written.IsSuccess);
        Assert.Equal(PlanErrors.LimitReached.Code, written.Error!.Code);
        Assert.Equal([kind], written.Error.Details![PlanErrors.KindDetail]);
        Assert.Equal([limit.ToString(System.Globalization.CultureInfo.InvariantCulture)], written.Error.Details[PlanErrors.LimitDetail]);
    }
}
