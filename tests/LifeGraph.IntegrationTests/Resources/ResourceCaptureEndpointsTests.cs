using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LifeGraph.Graph.Contracts;
using LifeGraph.IntegrationTests.Infrastructure;
using LifeGraph.Resources.Application;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace LifeGraph.IntegrationTests.Resources;

/// <summary>
/// Capture by paste as the SPA uses it (DA-062, DA-154, DA-156): one Resource per canonical
/// link and Account, every capture in the Inbox, an invalid link a 422 on <c>url</c> (DA-150)
/// and the Account's capture budget a 429.
/// </summary>
public sealed class ResourceCaptureEndpointsTests(PostgresDatabase database) : IAsyncLifetime
{
    private const string EmailA = "ada@example.test";
    private const string EmailB = "grace@example.test";

    private readonly LifeGraphApiFactory _factory = new(database);
    private SpaClient _spa = null!;

    public async ValueTask InitializeAsync()
    {
        await database.ResetAsync();
        await TestAccounts.ProvisionConfirmedAsync(_factory, EmailA);
        await TestAccounts.ProvisionConfirmedAsync(_factory, EmailB);
        _spa = await TestAccounts.SignedInAsync(_factory, EmailA);
    }

    public async ValueTask DisposeAsync()
    {
        _spa.Dispose();
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task The_same_link_twice_keeps_one_resource_and_a_tracking_variant_is_the_same_link()
    {
        var first = await _spa.PostAsync("/api/resources", new { url = "https://example.test/article?id=7", title = "Article", source = "clipboard" });
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var created = await CapturedAsync(first);
        Assert.Equal($"/api/nodes/{created.NodeId}", first.Headers.Location?.OriginalString);
        Assert.True(created.Created);
        Assert.NotNull(created.ChangeSetId);

        var again = await _spa.PostAsync("/api/resources", new { url = "https://EXAMPLE.test/article?utm_source=mail&id=7" });
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Equal(created with { Created = false, AlreadyExists = true, AlreadyInInbox = true, ChangeSetId = null }, await CapturedAsync(again));

        var other = await CapturedAsync(await _spa.PostAsync("/api/resources", new { url = "https://example.test/article?id=8" }));
        Assert.True(other.Created);
        Assert.NotEqual(created.NodeId, other.NodeId);
        Assert.Equal(2L, await database.QueryScalarAsMigratorAsync<long>("SELECT count(*) FROM nodes WHERE canonical_url IS NOT NULL"));
    }

    [Fact]
    public async Task Another_account_keeps_its_own_resource_for_the_same_link()
    {
        var mine = await CapturedAsync(await _spa.PostAsync("/api/resources", new { url = "https://example.test/shared" }));
        using var grace = await TestAccounts.SignedInAsync(_factory, EmailB);

        var theirs = await grace.PostAsync("/api/resources", new { url = "https://example.test/shared" });

        Assert.Equal(HttpStatusCode.Created, theirs.StatusCode);
        Assert.NotEqual(mine.NodeId, (await CapturedAsync(theirs)).NodeId);
    }

    [Theory]
    [InlineData("not a link")]
    [InlineData("ftp://example.test/file")]
    [InlineData("https://ada:secret@example.test/private")]
    [InlineData("")]
    public async Task An_invalid_link_answers_422_on_url_and_keeps_nothing(string url)
    {
        var refused = await _spa.PostAsync("/api/resources", new { url });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, refused.StatusCode);
        using var problem = JsonDocument.Parse(await refused.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal(GraphErrors.ResourceUrlInvalid.Code, problem.RootElement.GetProperty("code").GetString());
        Assert.Equal(["url"], problem.RootElement.GetProperty("errors").EnumerateObject().Select(field => field.Name));
        Assert.DoesNotContain("secret", problem.RootElement.GetRawText(), StringComparison.Ordinal);
        Assert.Equal(0L, await database.QueryScalarAsMigratorAsync<long>("SELECT count(*) FROM nodes WHERE canonical_url IS NOT NULL"));
    }

    [Fact]
    public async Task A_deleted_resource_comes_back_and_an_archived_one_returns_to_the_inbox()
    {
        var created = await CapturedAsync(await _spa.PostAsync("/api/resources", new { url = "https://example.test/again" }));
        Assert.Equal(HttpStatusCode.OK, (await _spa.DeleteAsync($"/api/nodes/{created.NodeId}?version=1")).StatusCode);

        var restored = await CapturedAsync(await _spa.PostAsync("/api/resources", new { url = "https://example.test/again" }));
        Assert.Equal((created.NodeId, false, true, false), (restored.NodeId, restored.Created, restored.AlreadyExists, restored.AlreadyInInbox));
        Assert.NotNull(restored.ChangeSetId);
        Assert.Equal(HttpStatusCode.OK, (await _spa.GetAsync($"/api/nodes/{created.NodeId}")).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await _spa.PostAsync($"/api/nodes/{created.NodeId}/archival", new { })).StatusCode);
        var returned = await CapturedAsync(await _spa.PostAsync("/api/resources", new { url = "https://example.test/again" }));
        Assert.Equal((created.NodeId, true, false), (returned.NodeId, returned.AlreadyExists, returned.AlreadyInInbox));

        var unchanged = await CapturedAsync(await _spa.PostAsync("/api/resources", new { url = "https://example.test/again" }));
        Assert.Equal((true, null), (unchanged.AlreadyInInbox, unchanged.ChangeSetId));
    }

    [Fact]
    public async Task The_accounts_capture_budget_answers_429_with_retry_after()
    {
        var first = await CapturedAsync(await _spa.PostAsync("/api/resources", new { url = "https://example.test/one" }));
        var accountId = await database.QueryScalarAsMigratorAsync<Guid>(
            "SELECT account_id FROM nodes WHERE id = @id", new NpgsqlParameter("id", first.NodeId!.Value));
        var budget = _factory.Services.GetRequiredService<CaptureRateLimit>();
        while (budget.TryTake(accountId))
        {
        }

        var refused = await _spa.PostAsync("/api/resources", new { url = "https://example.test/two" });

        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
        Assert.NotNull(refused.Headers.RetryAfter);
    }

    [Fact]
    public async Task Editing_the_link_follows_its_canonical_form_and_a_link_kept_by_another_resource_answers_422()
    {
        var first = await CapturedAsync(await _spa.PostAsync("/api/resources", new { url = "https://example.test/first" }));
        var second = await CapturedAsync(await _spa.PostAsync("/api/resources", new { url = "https://example.test/second" }));
        var urlProperty = await UrlPropertyOfAsync(first.NodeId!.Value);

        var edited = await _spa.SendAsync(HttpMethod.Patch, $"/api/nodes/{second.NodeId}", new
        {
            version = 1,
            properties = new Dictionary<Guid, string> { [urlProperty] = "https://EXAMPLE.test/moved?utm_medium=mail" },
        });
        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
        Assert.Equal("https://example.test/moved", await CanonicalUrlOfAsync(second.NodeId!.Value));

        var taken = await _spa.SendAsync(HttpMethod.Patch, $"/api/nodes/{second.NodeId}", new
        {
            version = 2,
            properties = new Dictionary<Guid, string> { [urlProperty] = "https://example.test/first?utm_source=x" },
        });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, taken.StatusCode);
        using var problem = JsonDocument.Parse(await taken.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal(GraphErrors.ResourceUrlTaken.Code, problem.RootElement.GetProperty("code").GetString());
        Assert.Equal("https://example.test/moved", await CanonicalUrlOfAsync(second.NodeId!.Value));
        Assert.Equal("https://example.test/first", await CanonicalUrlOfAsync(first.NodeId!.Value));
    }

    // DA-150: canonical_url and fetch_status are the system's, resource_kind keeps to the list of DA-056.
    [Fact]
    public async Task Editing_a_system_value_or_a_kind_outside_the_list_answers_400_on_each_field_and_a_listed_kind_is_kept()
    {
        var created = await CapturedAsync(await _spa.PostAsync("/api/resources", new { url = "https://example.test/kinds" }));
        var nodeId = created.NodeId!.Value;
        var canonicalProperty = await SystemPropertyOfAsync(nodeId, SystemOntology.ResourcePropertyKeys.CanonicalUrl);
        var fetchStatusProperty = await FetchStatusPropertyOfAsync(nodeId);
        var kindProperty = await SystemPropertyOfAsync(nodeId, SystemOntology.ResourcePropertyKeys.ResourceKind);

        var refused = await _spa.SendAsync(HttpMethod.Patch, $"/api/nodes/{nodeId}", new
        {
            version = 1,
            properties = new Dictionary<Guid, string>
            {
                [canonicalProperty] = "https://example.test/other",
                [fetchStatusProperty] = SystemOntology.FetchStatuses.Fetched,
                [kindProperty] = "anything",
            },
        });

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        using var problem = JsonDocument.Parse(await refused.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var fields = problem.RootElement.GetProperty("errors").EnumerateObject().Select(field => field.Name).ToList();
        Assert.Contains(fields, field => field.EndsWith($"properties.{canonicalProperty}", StringComparison.Ordinal));
        Assert.Contains(fields, field => field.EndsWith($"properties.{fetchStatusProperty}", StringComparison.Ordinal));
        Assert.Contains(fields, field => field.EndsWith($"properties.{kindProperty}", StringComparison.Ordinal));
        Assert.Equal(SystemOntology.FetchStatuses.Pending, await TextValueOfAsync(nodeId, fetchStatusProperty));

        var kept = await _spa.SendAsync(HttpMethod.Patch, $"/api/nodes/{nodeId}", new
        {
            version = 1,
            properties = new Dictionary<Guid, string> { [kindProperty] = "video" },
        });

        Assert.Equal(HttpStatusCode.OK, kept.StatusCode);
        Assert.Equal("video", await TextValueOfAsync(nodeId, kindProperty));
    }

    [Fact]
    public async Task Capturing_again_a_link_whose_fetch_failed_fetches_it_again_and_a_pending_one_does_not()
    {
        var created = await CapturedAsync(await _spa.PostAsync("/api/resources", new { url = "https://example.test/flaky" }));
        var nodeId = created.NodeId!.Value;
        Assert.Equal(1L, await FetchJobsOfAsync(nodeId));

        await CapturedAsync(await _spa.PostAsync("/api/resources", new { url = "https://example.test/flaky" }));
        Assert.Equal(1L, await FetchJobsOfAsync(nodeId));

        await database.QueryScalarAsMigratorAsync<int>(
            "UPDATE nodes SET properties = jsonb_set(properties, ARRAY[@property], to_jsonb(@failed::text)) WHERE id = @id RETURNING 1",
            new NpgsqlParameter("property", (await FetchStatusPropertyOfAsync(nodeId)).ToString()),
            new NpgsqlParameter("failed", SystemOntology.FetchStatuses.Failed),
            new NpgsqlParameter("id", nodeId));

        await CapturedAsync(await _spa.PostAsync("/api/resources", new { url = "https://example.test/flaky" }));
        Assert.Equal(2L, await FetchJobsOfAsync(nodeId));
    }

    private Task<Guid> UrlPropertyOfAsync(Guid nodeId) => SystemPropertyOfAsync(nodeId, SystemOntology.ResourcePropertyKeys.Url);

    private Task<Guid> FetchStatusPropertyOfAsync(Guid nodeId) => SystemPropertyOfAsync(nodeId, SystemOntology.ResourcePropertyKeys.FetchStatus);

    private Task<Guid> SystemPropertyOfAsync(Guid nodeId, string key) => database.QueryScalarAsMigratorAsync<Guid>(
        "SELECT p.id FROM property_definitions p JOIN nodes n ON n.account_id = p.account_id WHERE n.id = @id AND p.system_key = @key",
        new NpgsqlParameter("id", nodeId),
        new NpgsqlParameter("key", key));

    private Task<string> TextValueOfAsync(Guid nodeId, Guid propertyId) => database.QueryScalarAsMigratorAsync<string>(
        "SELECT properties ->> @property FROM nodes WHERE id = @id",
        new NpgsqlParameter("property", propertyId.ToString()),
        new NpgsqlParameter("id", nodeId));

    private Task<string> CanonicalUrlOfAsync(Guid nodeId) => database.QueryScalarAsMigratorAsync<string>(
        "SELECT canonical_url FROM nodes WHERE id = @id", new NpgsqlParameter("id", nodeId));

    private Task<long> FetchJobsOfAsync(Guid nodeId) => database.QueryScalarAsMigratorAsync<long>(
        "SELECT count(*) FROM jobs WHERE kind = @kind AND payload ->> 'nodeId' = @node",
        new NpgsqlParameter("kind", IResourceFetchGraph.JobKind),
        new NpgsqlParameter("node", nodeId.ToString()));

    private static async Task<CaptureResourceResponse> CapturedAsync(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, $"Capture answered {(int)response.StatusCode}.");
        return (await response.Content.ReadFromJsonAsync<CaptureResourceResponse>(TestContext.Current.CancellationToken))!;
    }
}
