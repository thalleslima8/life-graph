using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using LifeGraph.Graph.Contracts;
using LifeGraph.Infrastructure.Errors;
using LifeGraph.Infrastructure.Identity;
using LifeGraph.IntegrationTests.Graph;
using LifeGraph.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Npgsql;

namespace LifeGraph.IntegrationTests.Sharing;

/// <summary>
/// The ShareVisitor over HTTP on the share origin (DA-076, DA-077, DA-215, DA-217, DA-219,
/// DA-221): only the live members of a live share, never a Node outside it by any endpoint or
/// parameter, never a hidden Node or the Raiz da Conta, only allowlisted properties, and every
/// link it cannot open the same not found, uncached and unindexed.
/// </summary>
public sealed class SharedGraphViewVisitorTests(PostgresDatabase database) : IAsyncLifetime
{
    private readonly LifeGraphApiFactory _factory = new(database);
    private GraphWriteHarness _graph = null!;
    private Guid _account;

    public async ValueTask InitializeAsync()
    {
        await database.ResetAsync();
        _graph = new GraphWriteHarness(database, _factory);
        _account = await _graph.CreateAccountAsync();
    }

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    [Fact]
    public async Task The_visitor_sees_only_live_members_and_no_id_outside_the_share_by_any_endpoint()
    {
        var translator = await DefineAsync("Translator");
        var pages = await DefineAsync("Pages");
        var book = Guid.CreateVersion7();
        await _graph.WrittenAsync(_account, new CreateType(book, "Book", [translator, pages]));
        var root = Guid.CreateVersion7();
        await _graph.WrittenAsync(_account, new CreateNode(root, "Meditations", "Notes to himself", book, new Dictionary<Guid, JsonElement>
        {
            [translator] = JsonSerializer.SerializeToElement("Hays"),
            [pages] = JsonSerializer.SerializeToElement("300"),
        }));
        var seneca = await CreateNodeAsync("Seneca");
        var diary = await CreateNodeAsync("Diary");
        var letters = await CreateNodeAsync("Letters");
        var shown = await RelateAsync(root, seneca);
        await RelateAsync(root, diary);
        await RelateAsync(root, letters);
        var accountRoot = await _graph.AccountRootIdAsync(_account);
        await RelateAsync(root, accountRoot);
        var token = await ShareAsync(new CreateSharedGraphView(root, 1, [root, seneca, diary, letters], [translator], false, true));

        // After the share: a member hidden, one deleted, one linked to a Node outside the share.
        var outsider = await CreateNodeAsync("Outsider");
        var toOutsider = await RelateAsync(seneca, outsider);
        await _graph.WrittenAsync(_account, new UpdateNode(diary, 1) { HiddenFromAgents = true });
        await _graph.WrittenAsync(_account, new DeleteNode(letters, 1));
        var otherAccount = await _graph.CreateAccountAsync();
        var foreign = Guid.CreateVersion7();
        await _graph.WrittenAsync(otherAccount, new CreateNode(foreign, "Foreign"));

        using var visitor = VisitorClient(token);
        using var graph = await JsonOfAsync(await visitor.GetAsync("/api/shared", TestContext.Current.CancellationToken));
        Assert.Equal(["nodes", "publicRef", "relations", "rootNodeId"], graph.RootElement.EnumerateObject().Select(field => field.Name).Order());
        Assert.Equal([root, seneca], graph.RootElement.GetProperty("nodes").EnumerateArray().Select(node => node.GetProperty("id").GetGuid()));
        Assert.Equal([shown], graph.RootElement.GetProperty("relations").EnumerateArray().Select(relation => relation.GetProperty("id").GetGuid()));

        using var detail = await JsonOfAsync(await visitor.GetAsync($"/api/shared/nodes/{root}", TestContext.Current.CancellationToken));
        Assert.Equal(["Translator"], detail.RootElement.GetProperty("properties").EnumerateArray().Select(property => property.GetProperty("name").GetString()));
        Assert.Empty(detail.RootElement.GetProperty("tags").EnumerateArray());
        Assert.Equal([shown], detail.RootElement.GetProperty("relations").EnumerateArray().Select(relation => relation.GetProperty("id").GetGuid()));
        using var senecaDetail = await JsonOfAsync(await visitor.GetAsync($"/api/shared/nodes/{seneca}", TestContext.Current.CancellationToken));
        Assert.DoesNotContain(toOutsider, senecaDetail.RootElement.GetProperty("relations").EnumerateArray().Select(relation => relation.GetProperty("id").GetGuid()));

        foreach (var unreachable in new[] { diary, letters, outsider, accountRoot, foreign, toOutsider, Guid.CreateVersion7() })
        {
            var refused = await visitor.GetAsync($"/api/shared/nodes/{unreachable}", TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.NotFound, refused.StatusCode);
            Assert.Equal(CommonErrors.NotFound.Code, await ProblemCode.ReadAsync(refused));
        }

        // No visitor route takes it: the platform's fallback answers, as for any unknown path (API-080).
        var malformed = await visitor.GetAsync("/api/shared/nodes/not-a-guid", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, malformed.StatusCode);
    }

    [Fact]
    public async Task The_visitor_always_sees_a_resources_link_and_never_its_other_system_properties()
    {
        var resource = Guid.CreateVersion7();
        await _graph.WrittenAsync(_account, new CaptureResource(resource, "https://example.test/letters?id=7") { Title = "Letters" });
        var note = await CreateNodeAsync("Note");
        await RelateAsync(resource, note);
        var token = await ShareAsync(new CreateSharedGraphView(resource, 1, [resource, note], null, false, true));

        using var visitor = VisitorClient(token);
        using var resourceDetail = await JsonOfAsync(await visitor.GetAsync($"/api/shared/nodes/{resource}", TestContext.Current.CancellationToken));
        using var noteDetail = await JsonOfAsync(await visitor.GetAsync($"/api/shared/nodes/{note}", TestContext.Current.CancellationToken));

        Assert.Equal("https://example.test/letters?id=7", resourceDetail.RootElement.GetProperty("resourceUrl").GetString());
        Assert.Empty(resourceDetail.RootElement.GetProperty("properties").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, noteDetail.RootElement.GetProperty("resourceUrl").ValueKind);
    }

    [Fact]
    public async Task A_share_of_twelve_nodes_shows_exactly_them_until_it_is_revoked()
    {
        // The epic's exit criterion: twelve chosen Nodes, nothing else, and revoking drops the link at once.
        var root = await CreateNodeAsync("Como estou aprendendo Estoicismo");
        var chosen = new List<Guid> { root };
        for (var index = 1; index < 12; index++)
        {
            var member = await CreateNodeAsync($"Member {index}");
            await RelateAsync(root, member);
            chosen.Add(member);
        }

        var leftOut = await CreateNodeAsync("Left out");
        await RelateAsync(root, leftOut);
        var created = await _graph.AsAsync(new AuthenticatedPrincipal(_account, PrincipalType.Human), services => services.GetRequiredService<ISharedGraphViews>()
            .CreateAsync(new CreateSharedGraphView(root, 1, chosen, null, false, true), TestContext.Current.CancellationToken));
        Assert.True(created.IsSuccess, created.Error?.Code);

        using var visitor = VisitorClient(created.Value!.Token);
        using var graph = await JsonOfAsync(await visitor.GetAsync("/api/shared", TestContext.Current.CancellationToken));
        Assert.Equal(chosen.Order(), graph.RootElement.GetProperty("nodes").EnumerateArray().Select(node => node.GetProperty("id").GetGuid()).Order());
        Assert.Equal(11, graph.RootElement.GetProperty("relations").GetArrayLength());

        var revoked = await _graph.AsAsync(new AuthenticatedPrincipal(_account, PrincipalType.Human), services => services.GetRequiredService<ISharedGraphViews>()
            .RevokeAsync(created.Value.Id, TestContext.Current.CancellationToken));
        Assert.True(revoked.IsSuccess);
        AssertUnavailable(await visitor.GetAsync("/api/shared", TestContext.Current.CancellationToken));
        AssertUnavailable(await visitor.GetAsync($"/api/shared/nodes/{chosen[1]}", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Tags_are_shown_only_when_the_share_shows_them()
    {
        var root = await CreateNodeAsync("Stoicism");
        var tag = Guid.CreateVersion7();
        await _graph.WrittenAsync(_account, new CreateTag(tag, "philosophy"));
        await _graph.WrittenAsync(_account, new TagNode(root, tag));
        var hidden = await ShareAsync(new CreateSharedGraphView(root, 1, [root], null, false, true));
        var shownTags = await ShareAsync(new CreateSharedGraphView(root, 1, [root], null, true, true));

        using var withoutTags = VisitorClient(hidden);
        using var withTags = VisitorClient(shownTags);
        using var off = await JsonOfAsync(await withoutTags.GetAsync($"/api/shared/nodes/{root}", TestContext.Current.CancellationToken));
        using var on = await JsonOfAsync(await withTags.GetAsync($"/api/shared/nodes/{root}", TestContext.Current.CancellationToken));

        Assert.Empty(off.RootElement.GetProperty("tags").EnumerateArray());
        Assert.Equal(["philosophy"], on.RootElement.GetProperty("tags").EnumerateArray().Select(name => name.GetString()));
    }

    [Fact]
    public async Task Every_link_the_visitor_cannot_open_is_the_same_uncached_unindexed_not_found()
    {
        var blocked = await ShareAsync(await CreateNodeAsync("Blocked"));
        await database.ExecuteAsMigratorAsync("UPDATE shared_graph_view_directory SET blocked_at = now()", TestContext.Current.CancellationToken);
        var hiddenRoot = await CreateNodeAsync("Hidden root");
        var hiddenToken = await ShareAsync(new CreateSharedGraphView(hiddenRoot, 1, [hiddenRoot], null, false, true));
        await _graph.WrittenAsync(_account, new UpdateNode(hiddenRoot, 1) { HiddenFromAgents = true });
        var deletedRoot = await CreateNodeAsync("Deleted root");
        var deletedToken = await ShareAsync(new CreateSharedGraphView(deletedRoot, 1, [deletedRoot], null, false, true));
        await _graph.WrittenAsync(_account, new DeleteNode(deletedRoot, 1));

        foreach (var token in new[] { blocked, hiddenToken, deletedToken, "not-a-token", new string('A', 43) })
        {
            using var visitor = VisitorClient(token);
            var refused = await visitor.GetAsync("/api/shared", TestContext.Current.CancellationToken);
            AssertUnavailable(refused);
            Assert.Equal(SharingErrors.UnavailableMessage, (await JsonOfAsync(refused)).RootElement.GetProperty("detail").GetString());
        }

        using var anonymous = _factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = LifeGraphApiFactory.ShareOrigin });
        AssertUnavailable(await anonymous.GetAsync("/api/shared", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task The_visitor_api_answers_only_on_the_share_host()
    {
        var root = await CreateNodeAsync("Stoicism");
        var token = await ShareAsync(new CreateSharedGraphView(root, 1, [root], null, false, true));

        using var onAppHost = _factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = LifeGraphApiFactory.Origin });
        onAppHost.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var appHost = await onAppHost.GetAsync("/api/shared", TestContext.Current.CancellationToken);

        // No visitor route on the app host (DA-215): the token is no session, so the fallback refuses it.
        Assert.Equal(HttpStatusCode.Unauthorized, appHost.StatusCode);
    }

    [Fact]
    public async Task A_session_cookie_sent_to_the_share_host_never_authenticates()
    {
        const string email = "visitor-cookie@example.test";
        await TestAccounts.ProvisionConfirmedAsync(_factory, email);
        using var spa = new SpaClient(_factory.CreateClient());
        var login = await spa.LoginAsync(email, TestAccounts.Password);
        var sessionCookie = Assert.Single(login.Headers.GetValues("Set-Cookie"), cookie => cookie.StartsWith("__Host-lifegraph-session=", StringComparison.Ordinal));
        var cookieHeader = sessionCookie.Split(';')[0];

        var onAppHost = await SessionWithCookieAsync(LifeGraphApiFactory.Origin, "/api/sessions/current", cookieHeader);
        var onShareHost = await SessionWithCookieAsync(LifeGraphApiFactory.ShareOrigin, "/api/shared", cookieHeader);
        var withoutCookie = await SessionWithCookieAsync(LifeGraphApiFactory.ShareOrigin, "/api/shared", cookieHeader: null);

        // The same cookie opens the session on the app host; on the share host it is dropped, so the
        // request behaves as one without a cookie (DA-215).
        Assert.Equal(HttpStatusCode.OK, onAppHost.StatusCode);
        AssertUnavailable(onShareHost);
        Assert.Equal(withoutCookie.StatusCode, onShareHost.StatusCode);
        Assert.Equal(await ProblemCode.ReadAsync(withoutCookie), await ProblemCode.ReadAsync(onShareHost));
    }

    [Fact]
    public async Task A_visit_is_recorded_once_per_utc_day_and_the_token_never_reaches_a_log()
    {
        var root = await CreateNodeAsync("Stoicism");
        var token = await ShareAsync(new CreateSharedGraphView(root, 1, [root], null, false, true));
        await using var web = _factory.WithWebHostBuilder(builder => builder.ConfigureLogging(logging => logging
            .AddFakeLogging()
            .AddFilter<FakeLoggerProvider>(category: null, LogLevel.Trace)));
        using var visitor = web.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = LifeGraphApiFactory.ShareOrigin });
        visitor.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var first = await visitor.GetAsync("/api/shared", TestContext.Current.CancellationToken);
        await visitor.GetAsync("/api/shared", TestContext.Current.CancellationToken);
        AssertVisitorHeaders(first);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        // A second visit the same UTC day writes nothing (DA-220, DA-239).
        Assert.Equal(1L, await database.QueryScalarAsMigratorAsync<long>("SELECT access_day_count::bigint FROM shared_graph_views"));
        Assert.Equal(
            DateOnly.FromDateTime(_factory.Clock.GetUtcNow().UtcDateTime),
            await database.QueryScalarAsMigratorAsync<DateOnly>("SELECT last_accessed_on FROM shared_graph_views"));

        _factory.Clock.Advance(TimeSpan.FromDays(1));
        await visitor.GetAsync("/api/shared", TestContext.Current.CancellationToken);

        Assert.Equal(2L, await database.QueryScalarAsMigratorAsync<long>("SELECT access_day_count::bigint FROM shared_graph_views"));
        Assert.Equal(
            DateOnly.FromDateTime(_factory.Clock.GetUtcNow().UtcDateTime),
            await database.QueryScalarAsMigratorAsync<DateOnly>("SELECT last_accessed_on FROM shared_graph_views"));
        var logged = web.Services.GetFakeLogCollector().GetSnapshot()
            .Select(record => string.Join('\n', [record.Message, .. record.StructuredState?.Select(pair => $"{pair.Key}={pair.Value}") ?? [], record.Exception?.ToString() ?? string.Empty]))
            .ToList();
        Assert.Contains(logged, text => text.Contains("/api/shared", StringComparison.Ordinal));
        Assert.DoesNotContain(logged, text => text.Contains(token, StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_visit_day_that_cannot_be_written_still_serves_the_visit()
    {
        var root = await CreateNodeAsync("Stoicism");
        var token = await ShareAsync(root);
        using var visitor = _factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = LifeGraphApiFactory.ShareOrigin });
        visitor.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        // Any write of the visit day breaks this constraint, so the record fails in the database.
        await database.ExecuteAsMigratorAsync(
            "ALTER TABLE shared_graph_views ADD CONSTRAINT ck_test_no_visit_day CHECK (last_accessed_on IS NULL) NOT VALID",
            TestContext.Current.CancellationToken);
        try
        {
            var visit = await visitor.GetAsync("/api/shared", TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.OK, visit.StatusCode);
            Assert.Equal(0L, await database.QueryScalarAsMigratorAsync<long>("SELECT access_day_count::bigint FROM shared_graph_views"));
        }
        finally
        {
            await database.ExecuteAsMigratorAsync(
                "ALTER TABLE shared_graph_views DROP CONSTRAINT ck_test_no_visit_day",
                TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task Sharing_turned_off_opens_no_link()
    {
        var root = await CreateNodeAsync("Stoicism");
        var token = await ShareAsync(new CreateSharedGraphView(root, 1, [root], null, false, true));
        await using var off = new LifeGraphApiFactory(database, new Dictionary<string, string> { ["Sharing:Enabled"] = "false" });
        using var visitor = off.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = LifeGraphApiFactory.ShareOrigin });
        visitor.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        AssertUnavailable(await visitor.GetAsync("/api/shared", TestContext.Current.CancellationToken));
        Assert.Equal(0L, await database.QueryScalarAsMigratorAsync<long>("SELECT access_day_count::bigint FROM shared_graph_views"));
    }

    [Theory]
    [InlineData("PerTokenPermitLimit", true)]
    [InlineData("PerAddressPermitLimit", false)]
    public async Task The_visitor_is_held_to_a_budget_per_link_and_per_address(string budget, bool sameToken)
    {
        var root = await CreateNodeAsync("Stoicism");
        var first = await ShareAsync(new CreateSharedGraphView(root, 1, [root], null, false, true));
        var second = sameToken ? first : await ShareAsync(new CreateSharedGraphView(root, 1, [root], null, false, true));
        await using var limited = new LifeGraphApiFactory(database, new Dictionary<string, string>
        {
            ["Sharing:RateLimits:PerTokenPermitLimit"] = "1000",
            ["Sharing:RateLimits:PerAddressPermitLimit"] = "1000",
            [$"Sharing:RateLimits:{budget}"] = "2",
        });

        var answers = new List<HttpResponseMessage>();
        foreach (var token in new[] { first, second, first })
        {
            using var visitor = limited.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = LifeGraphApiFactory.ShareOrigin });
            visitor.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            answers.Add(await visitor.GetAsync("/api/shared", TestContext.Current.CancellationToken));
        }

        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.TooManyRequests], answers.Select(answer => answer.StatusCode));
        Assert.NotNull(answers[2].Headers.RetryAfter);
    }

    private static void AssertUnavailable(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        AssertVisitorHeaders(response);
    }

    private static void AssertVisitorHeaders(HttpResponseMessage response)
    {
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal("no-referrer", string.Join(',', response.Headers.GetValues("Referrer-Policy")));
        Assert.Contains("noindex", string.Join(',', response.Headers.GetValues("X-Robots-Tag")), StringComparison.Ordinal);
    }

    private static async Task<JsonDocument> JsonOfAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

    private async Task<HttpResponseMessage> SessionWithCookieAsync(Uri origin, string path, string? cookieHeader)
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = origin, HandleCookies = false });
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (cookieHeader is not null)
        {
            request.Headers.Add("Cookie", cookieHeader);
        }

        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private HttpClient VisitorClient(string token)
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = LifeGraphApiFactory.ShareOrigin });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private Task<string> ShareAsync(Guid root) => ShareAsync(new CreateSharedGraphView(root, 1, [root], null, false, true));

    private async Task<string> ShareAsync(CreateSharedGraphView request)
    {
        var shared = request;
        var created = await _graph.AsAsync(new AuthenticatedPrincipal(_account, PrincipalType.Human), services => services.GetRequiredService<ISharedGraphViews>()
            .CreateAsync(shared, TestContext.Current.CancellationToken));
        Assert.True(created.IsSuccess, created.Error?.Code);
        return created.Value!.Token;
    }

    private async Task<Guid> CreateNodeAsync(string title)
    {
        var id = Guid.CreateVersion7();
        await _graph.WrittenAsync(_account, new CreateNode(id, title));
        return id;
    }

    private async Task<Guid> RelateAsync(Guid source, Guid target)
    {
        var id = Guid.CreateVersion7();
        await _graph.WrittenAsync(_account, new CreateRelation(id, source, target, "about"));
        return id;
    }

    private async Task<Guid> DefineAsync(string name)
    {
        var id = Guid.CreateVersion7();
        await _graph.WrittenAsync(_account, new DefineProperty(id, name, PropertyValueKind.Text));
        return id;
    }
}
