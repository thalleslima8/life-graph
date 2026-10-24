using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using LifeGraph.Graph.Contracts;
using LifeGraph.Infrastructure.Accounts;
using LifeGraph.Infrastructure.Errors;
using LifeGraph.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace LifeGraph.IntegrationTests.Graph;

/// <summary>
/// The system Type <c>Resource</c> and its Properties (DA-149, DA-150): created on sign-in,
/// idempotent and failing closed (DA-158), locked and with reserved names (DA-057), outside
/// GraphChangeSets and Undo, and isolated per Account by RLS.
/// </summary>
public sealed class SystemOntologyTests(PostgresDatabase database) : IAsyncLifetime
{
    private const string Email = "system-ontology@example.com";
    private const string OtherEmail = "system-ontology-other@example.com";

    private readonly LifeGraphApiFactory _factory = new(database);
    private GraphWriteHarness _graph = null!;

    public async ValueTask InitializeAsync()
    {
        await database.ResetAsync();
        _graph = new GraphWriteHarness(database, _factory);
    }

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    [Fact]
    public async Task Provisioning_creates_no_system_rows_and_the_first_sign_in_does_while_the_next_changes_nothing()
    {
        var created = await TestAccounts.ProvisionConfirmedAsync(_factory, Email);
        var beforeSignIn = await SystemRowsAsync(created.AccountId);

        (await TestAccounts.SignedInAsync(_factory, Email)).Dispose();

        var afterFirst = await SystemRowsAsync(created.AccountId);
        using var second = await TestAccounts.SignedInAsync(_factory, Email);
        var afterSecond = await SystemRowsAsync(created.AccountId);

        Assert.True(beforeSignIn is "" || beforeSignIn == afterFirst);
        Assert.Equal(1 + SystemOntology.ResourceProperties.Count, afterFirst.Split(',').Length);
        Assert.Equal(afterFirst, afterSecond);
        Assert.Equal(0, await _graph.CountAsync("changesets"));
    }

    [Fact]
    public async Task Concurrent_setups_of_a_new_account_leave_one_set_of_system_rows()
    {
        var accountId = Guid.CreateVersion7();
        await database.ExecuteAsMigratorAsync(
            "INSERT INTO accounts (id, created_at) VALUES (@id, now())",
            TestContext.Current.CancellationToken,
            new NpgsqlParameter("id", accountId));
        var setup = _factory.Services.GetRequiredService<AccountSetup>();

        await Task.WhenAll(Enumerable.Range(0, 4)
            .Select(_ => Task.Run(() => setup.EnsureAsync(accountId, TestContext.Current.CancellationToken))));

        Assert.Equal(1, await _graph.ScalarAsync<long>(
            "SELECT count(*) FROM types WHERE account_id = @account AND system_key IS NOT NULL",
            new NpgsqlParameter("account", accountId)));
        Assert.Equal(SystemOntology.ResourceProperties.Count, await _graph.ScalarAsync<long>(
            "SELECT count(*) FROM type_properties tp JOIN types t ON t.id = tp.type_id WHERE t.account_id = @account AND t.system_key = 'resource'",
            new NpgsqlParameter("account", accountId)));
    }

    // DA-158: sign-ins racing on a new Account run the setup through the sign-in itself.
    [Fact]
    public async Task Concurrent_first_sign_ins_of_an_account_leave_one_set_of_system_rows()
    {
        var created = await TestAccounts.ProvisionConfirmedAsync(_factory, Email);

        var sessions = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => TestAccounts.SignedInAsync(_factory, Email)));
        foreach (var session in sessions)
        {
            session.Dispose();
        }

        Assert.Equal(1 + SystemOntology.ResourceProperties.Count, (await SystemRowsAsync(created.AccountId)).Split(',').Length);
        Assert.Equal(1, await _graph.ScalarAsync<long>(
            "SELECT count(*) FROM types WHERE account_id = @account AND system_key IS NOT NULL",
            new NpgsqlParameter("account", created.AccountId)));
    }

    // DA-158: the setup runs before the session cookie is issued, so a failure issues none. The
    // host's error handler clears the response's headers, so the test records what the session's
    // cookie manager was asked to write instead of reading them.
    [Fact]
    public async Task A_failing_setup_fails_the_sign_in_and_leaves_no_session()
    {
        await TestAccounts.ProvisionConfirmedAsync(_factory, Email);
        var issuedCookies = new SessionCookieRecorder();
        await using var failing = _factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.AddScoped<IAccountSetupParticipant, FailingSetupParticipant>();
            services.PostConfigure<CookieAuthenticationOptions>(IdentityConstants.ApplicationScheme, options =>
                options.CookieManager = issuedCookies.Recording(options.CookieManager ?? new ChunkingCookieManager()));
        }));
        using var spa = new SpaClient(failing.CreateClient());

        var login = await spa.LoginAsync(Email, TestAccounts.Password);
        var afterwards = await spa.GetAsync("/api/types");

        Assert.Equal(HttpStatusCode.InternalServerError, login.StatusCode);
        Assert.Empty(issuedCookies.Appended);
        Assert.Equal(HttpStatusCode.Unauthorized, afterwards.StatusCode);
    }

    [Fact]
    public async Task System_entries_are_locked_and_their_names_reserved_in_any_case_and_nothing_is_recorded()
    {
        var accountId = await _graph.CreateAccountAsync();
        var resource = await SystemTypeAsync(accountId);
        var url = await SystemPropertyAsync(accountId, SystemOntology.ResourcePropertyKeys.Url);
        var bookId = Guid.CreateVersion7();
        await _graph.WrittenAsync(accountId, new CreateType(bookId, "Book"));
        var changeSetsBefore = await _graph.CountAsync("changesets");

        var refusals = new (string Code, GraphOperation Operation)[]
        {
            (GraphErrors.SystemEntryLocked.Code, new RenameType(resource, "Link")),
            (GraphErrors.SystemEntryLocked.Code, new DeleteType(resource)),
            (GraphErrors.SystemEntryLocked.Code, new DetachProperty(resource, url)),
            (GraphErrors.SystemEntryLocked.Code, new DeletePropertyDefinition(url)),
            (GraphErrors.SystemEntryLocked.Code, new UpdatePropertyDefinition(url) { Name = "link" }),
            (GraphErrors.SystemEntryLocked.Code, new AttachProperty(bookId, url)),
            (GraphErrors.NameReserved.Code, new CreateType(Guid.CreateVersion7(), " rEsOuRcE ")),
            (GraphErrors.NameReserved.Code, new DefineProperty(Guid.CreateVersion7(), "URL", PropertyValueKind.Text)),
            (GraphErrors.NameReserved.Code, new RenameType(bookId, "RESOURCE")),
        };

        foreach (var (code, operation) in refusals)
        {
            var refused = await _graph.WriteAsync(accountId, operation);
            Assert.Equal(code, refused.Error?.Code);
        }

        Assert.Equal(changeSetsBefore, await _graph.CountAsync("changesets"));
        Assert.Equal("Resource", await _graph.ScalarAsync<string>("SELECT name FROM types WHERE id = @id", new NpgsqlParameter("id", resource)));
        Assert.Equal(SystemOntology.ResourceProperties.Count, await _graph.ScalarAsync<long>(
            "SELECT count(*) FROM type_properties WHERE type_id = @id", new NpgsqlParameter("id", resource)));
    }

    [Fact]
    public async Task A_user_property_attaches_to_the_resource_type_holds_validated_values_and_detaches()
    {
        var accountId = await _graph.CreateAccountAsync();
        var resource = await SystemTypeAsync(accountId);
        var url = await SystemPropertyAsync(accountId, SystemOntology.ResourcePropertyKeys.Url);
        var ratingId = Guid.CreateVersion7();
        await _graph.WrittenAsync(accountId, new DefineProperty(ratingId, "Rating", PropertyValueKind.Number));

        var attached = await _graph.WrittenAsync(accountId, new AttachProperty(resource, ratingId));
        var nodeId = Guid.CreateVersion7();
        var refused = await _graph.WriteAsync(accountId, new CreateNode(
            nodeId, "A page", TypeId: resource, Properties: new Dictionary<Guid, JsonElement>
            {
                [url] = JsonSerializer.SerializeToElement("https://example.com/a"),
                [ratingId] = JsonSerializer.SerializeToElement("five"),
            }));
        await _graph.WrittenAsync(accountId, new CreateNode(
            nodeId, "A page", TypeId: resource, Properties: new Dictionary<Guid, JsonElement>
            {
                [url] = JsonSerializer.SerializeToElement("https://example.com/a"),
                [ratingId] = JsonSerializer.SerializeToElement(5),
            }));
        var detached = await _graph.WriteAsync(accountId, new DetachProperty(resource, ratingId));

        Assert.NotEqual(Guid.Empty, attached);
        Assert.Equal(CommonErrors.ValidationFailed.Code, refused.Error?.Code);
        Assert.True(detached.IsSuccess);
        Assert.Equal(SystemOntology.ResourceProperties.Count, await _graph.ScalarAsync<long>(
            "SELECT count(*) FROM type_properties WHERE type_id = @id", new NpgsqlParameter("id", resource)));
        Assert.Equal(5, await _graph.ScalarAsync<int>(
            "SELECT (properties ->> @rating)::int FROM nodes WHERE id = @id",
            new NpgsqlParameter("rating", ratingId.ToString()),
            new NpgsqlParameter("id", nodeId)));
    }

    [Fact]
    public async Task Undoing_the_creation_of_a_node_of_the_resource_type_leaves_the_type_and_its_properties()
    {
        var accountId = await _graph.CreateAccountAsync();
        var resource = await SystemTypeAsync(accountId);
        var created = await _graph.WrittenAsync(accountId, new CreateNode(Guid.CreateVersion7(), "A page", TypeId: resource));

        Assert.True((await _graph.UndoAsync(accountId, created)).IsSuccess);

        Assert.Equal(1 + SystemOntology.ResourceProperties.Count, (await SystemRowsAsync(accountId)).Split(',').Length);
        Assert.Equal(0, await _graph.ScalarAsync<long>(
            "SELECT count(*) FROM change_entries WHERE entity_id = @id", new NpgsqlParameter("id", resource)));
    }

    [Fact]
    public async Task An_account_lists_only_its_own_system_type_and_a_locked_entry_answers_422()
    {
        var mine = await TestAccounts.ProvisionConfirmedAsync(_factory, Email);
        var other = await TestAccounts.ProvisionConfirmedAsync(_factory, OtherEmail);
        (await TestAccounts.SignedInAsync(_factory, OtherEmail)).Dispose();

        using var spa = await TestAccounts.SignedInAsync(_factory, Email);
        var list = await spa.GetAsync("/api/types");
        using var body = JsonDocument.Parse(await list.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var system = Assert.Single(body.RootElement.GetProperty("data").EnumerateArray(), type => type.GetProperty("isSystem").GetBoolean());
        var resource = system.GetProperty("id").GetGuid();
        var deleted = await spa.DeleteAsync($"/api/types/{resource}");
        var reserved = await spa.PostAsync("/api/types", new { name = "resource" });

        Assert.Equal(await SystemTypeAsync(mine.AccountId), resource);
        Assert.NotEqual(await SystemTypeAsync(other.AccountId), resource);
        Assert.Equal(SystemOntology.ResourceTypeName, system.GetProperty("name").GetString());
        Assert.Equal(
            [HttpStatusCode.UnprocessableEntity, HttpStatusCode.UnprocessableEntity],
            [deleted.StatusCode, reserved.StatusCode]);
        Assert.Equal(GraphErrors.SystemEntryLocked.Code, await ProblemCode.ReadAsync(deleted));
        Assert.Equal(GraphErrors.NameReserved.Code, await ProblemCode.ReadAsync(reserved));
    }

    private Task<Guid> SystemTypeAsync(Guid accountId) =>
        _graph.ScalarAsync<Guid>(
            "SELECT id FROM types WHERE account_id = @account AND system_key = 'resource'",
            new NpgsqlParameter("account", accountId));

    private Task<Guid> SystemPropertyAsync(Guid accountId, string key) =>
        _graph.ScalarAsync<Guid>(
            "SELECT id FROM property_definitions WHERE account_id = @account AND system_key = @key",
            new NpgsqlParameter("account", accountId),
            new NpgsqlParameter("key", key));

    /// <summary>The Account's system rows, ids and update times included, so any change shows.</summary>
    private Task<string> SystemRowsAsync(Guid accountId) =>
        _graph.ScalarAsync<string>(
            """
            SELECT coalesce(string_agg(row, ',' ORDER BY row), '') FROM (
                SELECT id::text || system_key || updated_at::text AS row FROM types WHERE account_id = @account AND system_key IS NOT NULL
                UNION ALL
                SELECT id::text || system_key || updated_at::text FROM property_definitions WHERE account_id = @account AND system_key IS NOT NULL
            ) AS rows
            """,
            new NpgsqlParameter("account", accountId));

    /// <summary>The session cookies the sign-in asked to write, whatever happens to the response later.</summary>
    private sealed class SessionCookieRecorder
    {
        private readonly ConcurrentQueue<string> _appended = new();

        public IReadOnlyCollection<string> Appended => _appended;

        public ICookieManager Recording(ICookieManager inner) => new RecordingCookieManager(inner, _appended);

        private sealed class RecordingCookieManager(ICookieManager inner, ConcurrentQueue<string> appended)
            : ICookieManager
        {
            public string? GetRequestCookie(HttpContext context, string key) => inner.GetRequestCookie(context, key);

            public void AppendResponseCookie(HttpContext context, string key, string? value, CookieOptions options)
            {
                appended.Enqueue(key);
                inner.AppendResponseCookie(context, key, value, options);
            }

            public void DeleteCookie(HttpContext context, string key, CookieOptions options) =>
                inner.DeleteCookie(context, key, options);
        }
    }

    private sealed class FailingSetupParticipant : IAccountSetupParticipant
    {
        public Task EnsureAsync(Guid accountId, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Setup failed on purpose.");
    }
}
