using System.Text;
using System.Text.Json;
using LifeGraph.Graph.Contracts;
using LifeGraph.Infrastructure.Persistence;
using LifeGraph.IntegrationTests.Graph;
using LifeGraph.IntegrationTests.Infrastructure;
using LifeGraph.Resources.Fetch;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Npgsql;

namespace LifeGraph.IntegrationTests.Resources;

/// <summary>
/// The metadata fetch (DA-152, DA-153, DA-151): scheduled by the write that sets a Resource's
/// url, fetched with no transaction open, applied as an undoable System ChangeSet that fills only
/// what no Human has edited.
/// </summary>
public sealed class ResourceFetchTests(PostgresDatabase database) : IAsyncLifetime
{
    private const string Url = "https://example.com/article";

    private readonly FakeFetcher.Script _script = new();
    private LifeGraphApiFactory _baseFactory = null!;
    private WebApplicationFactory<Program> _factory = null!;
    private GraphWriteHarness _graph = null!;
    private Guid _account;
    private Guid _resourceType;
    private Guid _urlProperty;

    public async ValueTask InitializeAsync()
    {
        await database.ResetAsync();
        _baseFactory = new LifeGraphApiFactory(database);
        _factory = _baseFactory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureLogging(logging => logging.AddFakeLogging());
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton(_script);
                services.AddScoped<IResourceFetcher, FakeFetcher>();
            });
        });
        _graph = new GraphWriteHarness(database, _baseFactory);
        _account = await _graph.CreateAccountAsync();
        _resourceType = await IdAsync("SELECT id FROM types WHERE account_id = @account AND system_key = 'resource'");
        _urlProperty = await PropertyIdAsync(SystemOntology.ResourcePropertyKeys.Url);
    }

    public async ValueTask DisposeAsync()
    {
        await _factory.DisposeAsync();
        await _baseFactory.DisposeAsync();
    }

    [Fact]
    public async Task Setting_or_changing_a_resource_url_schedules_one_fetch_and_other_edits_do_not()
    {
        var node = await CreateResourceAsync();
        Assert.Equal(1, await FetchJobsAsync(node));

        await _graph.WrittenAsync(_account, new UpdateNode(node, 1) { Title = "Renamed" });
        Assert.Equal(1, await FetchJobsAsync(node));

        await _graph.WrittenAsync(_account, new UpdateNode(node, 2) { Properties = Values((_urlProperty, "https://example.com/other")) });
        Assert.Equal(2, await FetchJobsAsync(node));
    }

    [Fact]
    public async Task A_fetch_fills_the_resource_in_a_system_changeset_with_no_transaction_open_during_the_fetch()
    {
        _script.Html = """
            <html><head><meta property="og:type" content="article"><meta property="og:title" content="Fetched title">
            <meta property="og:description" content="Fetched description"><meta property="og:site_name" content="Example"></head></html>
            """;
        var node = await CreateResourceAsync();

        await RunJobsAsync();

        Assert.Equal([false], _script.TransactionOpenDuringFetch);
        Assert.Equal("Fetched title", await NodeTextAsync(node, "title"));
        Assert.Equal("Fetched description", await NodeTextAsync(node, "body"));
        Assert.Equal("fetched", await PropertyTextAsync(node, SystemOntology.ResourcePropertyKeys.FetchStatus));
        Assert.Equal("Example", await PropertyTextAsync(node, SystemOntology.ResourcePropertyKeys.SiteName));
        Assert.Equal("system/system", await _graph.ScalarAsync<string>(
            "SELECT actor_kind || '/' || channel FROM changesets WHERE account_id = @account ORDER BY created_at DESC, id DESC LIMIT 1",
            new NpgsqlParameter("account", _account)));
        Assert.DoesNotContain(Logged(), line => line.Contains("example.com", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task A_fetch_that_fills_the_text_schedules_an_embedding_of_the_resource()
    {
        _script.Html = """<html><head><title>Fetched title</title></head></html>""";
        var node = await CreateResourceAsync();
        Assert.Equal(1, await EmbedJobsAsync(node));

        await RunJobsAsync();

        Assert.Equal("Fetched title", await NodeTextAsync(node, "title"));
        Assert.Equal(2, await EmbedJobsAsync(node));
    }

    [Fact]
    public async Task A_title_a_human_edited_is_never_overwritten_by_the_fetch()
    {
        _script.Html = """<html><head><title>Fetched title</title></head></html>""";
        var node = await CreateResourceAsync();
        await _graph.WrittenAsync(_account, new UpdateNode(node, 1) { Title = "My own title" });

        await RunJobsAsync();

        Assert.Equal("My own title", await NodeTextAsync(node, "title"));
        Assert.Equal("fetched", await PropertyTextAsync(node, SystemOntology.ResourcePropertyKeys.FetchStatus));
    }

    [Fact]
    public async Task A_failed_fetch_marks_the_resource_failed()
    {
        _script.Html = null;
        var node = await CreateResourceAsync();

        await RunJobsAsync();

        Assert.Equal("failed", await PropertyTextAsync(node, SystemOntology.ResourcePropertyKeys.FetchStatus));
        Assert.Equal(Url, await NodeTextAsync(node, "title"));
    }

    [Fact]
    public async Task A_resource_deleted_before_the_fetch_is_not_written()
    {
        _script.Html = """<html><head><title>Fetched title</title></head></html>""";
        var node = await CreateResourceAsync();
        await _graph.WrittenAsync(_account, new DeleteNode(node, 1));

        await RunJobsAsync();

        Assert.Equal(0, await SystemChangeSetsAsync());
    }

    [Fact]
    public async Task Undoing_the_fetch_restores_the_resource_and_fetches_nothing_again()
    {
        _script.Html = """<html><head><title>Fetched title</title></head></html>""";
        var node = await CreateResourceAsync();
        await RunJobsAsync();
        var fetchChangeSet = await IdAsync("SELECT id FROM changesets WHERE account_id = @account AND actor_kind = 'system'");

        var undone = await _graph.UndoAsync(_account, fetchChangeSet);

        Assert.True(undone.IsSuccess, undone.Error?.Code);
        Assert.Equal(Url, await NodeTextAsync(node, "title"));
        Assert.Equal(1, await FetchJobsAsync(node));
    }

    [Fact]
    public async Task The_public_writer_refuses_a_system_actor()
    {
        var system = new Provenance(new GraphActor.System(), WriteChannel.System);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _graph.WriteAsync(_account, system, new CreateNode(Guid.CreateVersion7(), "Forged")));
    }

    private async Task<Guid> CreateResourceAsync()
    {
        var node = Guid.CreateVersion7();
        await _graph.WrittenAsync(_account, new CreateNode(node, Url, TypeId: _resourceType, Properties: Values((_urlProperty, Url))));
        return node;
    }

    private Task<int> RunJobsAsync() =>
        _factory.Services.GetRequiredService<LifeGraph.Infrastructure.Jobs.JobRunner>().RunDueAsync(TestContext.Current.CancellationToken);

    private async Task<long> FetchJobsAsync(Guid node) => await _graph.ScalarAsync<long>(
        "SELECT count(*) FROM jobs WHERE kind = @kind AND payload ->> 'nodeId' = @node",
        new NpgsqlParameter("kind", IResourceFetchGraph.JobKind),
        new NpgsqlParameter("node", node.ToString()));

    private async Task<long> EmbedJobsAsync(Guid node) => await _graph.ScalarAsync<long>(
        "SELECT count(*) FROM jobs WHERE kind = @kind AND payload ->> 'nodeId' = @node",
        new NpgsqlParameter("kind", ISemanticGraph.EmbedJobKind),
        new NpgsqlParameter("node", node.ToString()));

    private async Task<long> SystemChangeSetsAsync() => await _graph.ScalarAsync<long>(
        "SELECT count(*) FROM changesets WHERE account_id = @account AND actor_kind = 'system'",
        new NpgsqlParameter("account", _account));

    private Task<string> NodeTextAsync(Guid node, string column) => _graph.ScalarAsync<string>(
        $"SELECT {column} FROM nodes WHERE id = @node", new NpgsqlParameter("node", node));

    private async Task<string> PropertyTextAsync(Guid node, string key) => await _graph.ScalarAsync<string>(
        "SELECT properties ->> @property FROM nodes WHERE id = @node",
        new NpgsqlParameter("property", (await PropertyIdAsync(key)).ToString()),
        new NpgsqlParameter("node", node));

    private Task<Guid> PropertyIdAsync(string key) => _graph.ScalarAsync<Guid>(
        "SELECT id FROM property_definitions WHERE account_id = @account AND system_key = @key",
        new NpgsqlParameter("account", _account),
        new NpgsqlParameter("key", key));

    private Task<Guid> IdAsync(string sql) => _graph.ScalarAsync<Guid>(sql, new NpgsqlParameter("account", _account));

    private IEnumerable<string> Logged() => _factory.Services.GetFakeLogCollector().GetSnapshot().Select(record => string.Join(
        '\n',
        [record.Message, .. record.StructuredState?.Select(pair => $"{pair.Key}={pair.Value}") ?? []]));

    private static Dictionary<Guid, JsonElement> Values(params (Guid PropertyId, object? Value)[] values) =>
        values.ToDictionary(value => value.PropertyId, value => JsonSerializer.SerializeToElement(value.Value));

    /// <summary>Answers without network and records whether the job's DbContext had a transaction open.</summary>
    private sealed class FakeFetcher(FakeFetcher.Script script, LifeGraphDbContext db) : IResourceFetcher
    {
        public sealed class Script
        {
            public string? Html { get; set; }

            public List<bool> TransactionOpenDuringFetch { get; } = [];
        }

        public Task<FetchedResponse?> FetchAsync(ResourceRequest request, CancellationToken cancellationToken)
        {
            script.TransactionOpenDuringFetch.Add(db.Database.CurrentTransaction is not null);
            return Task.FromResult(script.Html is null ? null : new FetchedResponse("text/html", "utf-8", Encoding.UTF8.GetBytes(script.Html)));
        }
    }
}
