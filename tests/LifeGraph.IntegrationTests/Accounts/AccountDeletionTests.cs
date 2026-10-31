using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using LifeGraph.Accounts;
using LifeGraph.Infrastructure.Accounts;
using LifeGraph.Infrastructure.Purging;
using LifeGraph.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace LifeGraph.IntegrationTests.Accounts;

/// <summary>
/// Deleting the Account (DA-082): the password typed again (DA-249) and the e-mail typed as
/// confirmation (DA-251), then the whole purge, the Identity user and the Account in one
/// transaction (DA-252), with access dying at the commit and the notice sent after it (DA-250).
/// </summary>
public sealed class AccountDeletionTests(PostgresDatabase database) : IAsyncLifetime
{
    private const string Email = "ada@example.test";
    private const string OtherEmail = "grace@example.test";
    private const string DeletionPath = "/api/account/deletion";

    private readonly LifeGraphApiFactory _factory = new(database, AgentAuthorization.Settings());
    private Guid _account;
    private Guid _otherAccount;

    public async ValueTask InitializeAsync()
    {
        await database.ResetAsync();
        _account = (await TestAccounts.ProvisionConfirmedAsync(_factory, Email)).AccountId;
        _otherAccount = (await TestAccounts.ProvisionConfirmedAsync(_factory, OtherEmail)).AccountId;
    }

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    [Fact]
    public async Task Deleting_with_the_password_and_the_typed_email_ends_every_access_and_frees_the_email()
    {
        using var browser = await AgentAuthorization.SignedInBrowserAsync(_factory, Email);
        var agentTokens = await AgentAuthorization.ConnectAsync(_factory, browser);
        var shareToken = await SharedLinkTokenAsync(browser);
        using var agentClient = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await AgentAuthorization.CallMcpAsync(agentClient, agentTokens.AccessToken)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await VisitShareAsync(shareToken)).StatusCode);

        var deletion = await browser.PostAsync(DeletionPath, new { password = TestAccounts.Password, confirmation = "  ADA@Example.test " });

        Assert.Equal(HttpStatusCode.OK, deletion.StatusCode);
        var body = await deletion.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal(JsonValueKind.String, body.GetProperty("deletedAt").ValueKind);
        var receipt = body.GetProperty("receipt");
        var reference = receipt.GetProperty("reference").GetString()!;
        Assert.Equal(body.GetProperty("deletedAt").GetDateTimeOffset(), receipt.GetProperty("date").GetDateTimeOffset());

        // Access dies with the commit: the session, the agent's access and refresh tokens, the share link.
        Assert.Equal(HttpStatusCode.Unauthorized, (await browser.GetAsync("/api/sessions/current")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await AgentAuthorization.CallMcpAsync(agentClient, agentTokens.AccessToken)).StatusCode);
        Assert.False((await AgentAuthorization.RefreshAsync(agentClient, agentTokens.RefreshToken!)).IsSuccessStatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await VisitShareAsync(shareToken)).StatusCode);

        // Nothing of the Account is left, the other Account is untouched.
        Assert.Equal(0L, await CountByIdAsync("accounts", "id", _account));
        Assert.Equal(0L, await CountByIdAsync("users", "account_id", _account));
        Assert.Equal(0L, await CountByIdAsync("jobs", "account_id", _account));
        Assert.Equal(0L, await CountByIdAsync("nodes", "account_id", _account));
        Assert.Equal(0L, await CountByIdAsync("agent_identities", "account_id", _account));
        Assert.Equal(0L, await database.QueryScalarAsMigratorAsync<long>("SELECT count(*) FROM oidc_tokens"));
        Assert.Equal(1L, await CountByIdAsync("accounts", "id", _otherAccount));
        Assert.Equal(1L, await CountByIdAsync("users", "account_id", _otherAccount));

        // The notice went to the address read before the deletion; the address is free again.
        var notice = _factory.Mailer.LastTo(Email);
        Assert.Equal("Your Life Graph account was deleted", notice.Subject);
        Assert.Contains("ChatGPT or Claude", notice.TextBody, StringComparison.Ordinal);
        Assert.Contains($"Deletion receipt: {reference},", notice.TextBody, StringComparison.Ordinal);

        // The ledger row was committed with the purge (DA-084): the Account's id only as its hash,
        // the receipt's random reference, and no e-mail anywhere in it (DA-253).
        Assert.Equal(1L, await database.QueryScalarAsMigratorAsync<long>(
            "SELECT count(*) FROM purge_ledger_entries WHERE subject_kind = 'account' AND subject_hash = @hash AND hash_version = 1 AND receipt_reference = @reference",
            new NpgsqlParameter("hash", PurgeLedgerEntry.SubjectHashOf(PurgedSubjectKind.Account, _account)),
            new NpgsqlParameter("reference", reference)));
        Assert.Equal(0L, await database.QueryScalarAsMigratorAsync<long>(
            "SELECT count(*) FROM purge_ledger_entries l WHERE row_to_json(l)::text ILIKE '%ada@%' OR row_to_json(l)::text LIKE '%' || @account || '%'",
            new NpgsqlParameter("account", _account.ToString())));
        var again = await TestAccounts.ProvisionConfirmedAsync(_factory, Email);
        Assert.NotEqual(_account, again.AccountId);
    }

    // DA-252: the purge runs inside the request, measured on a large seeded Account. The budget is
    // generous on purpose: a shared CI runner may be several times slower than a dev machine. A
    // purge past it is the signal to move the deletion to a job, as DA-252 says.
    [Fact]
    public async Task Deleting_a_large_account_finishes_within_the_request_budget()
    {
        const int nodeCount = 10_000;
        var budget = TimeSpan.FromSeconds(30);
        await SeedLargeGraphAsync(_account, nodeCount);
        using var spa = await TestAccounts.SignedInAsync(_factory, Email);

        var clock = Stopwatch.StartNew();
        var deletion = await spa.PostAsync(DeletionPath, new { password = TestAccounts.Password, confirmation = Email });
        clock.Stop();

        Assert.Equal(HttpStatusCode.OK, deletion.StatusCode);
        Assert.Equal(0L, await CountByIdAsync("nodes", "account_id", _account));
        Assert.Equal(0L, await CountByIdAsync("relations", "account_id", _account));
        Assert.True(clock.Elapsed < budget, $"The deletion took {clock.Elapsed.TotalMilliseconds:F0} ms, over the {budget.TotalMilliseconds:F0} ms budget.");
    }

    [Fact]
    public async Task A_wrong_password_is_refused_and_nothing_is_deleted()
    {
        using var spa = await TestAccounts.SignedInAsync(_factory, Email);
        var sentBefore = _factory.Mailer.CountTo(Email);

        var deletion = await spa.PostAsync(DeletionPath, new { password = "not the password", confirmation = Email });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, deletion.StatusCode);
        Assert.Equal(AccountsErrors.ReauthenticationFailed.Code, await ProblemCode.ReadAsync(deletion));
        await AssertNothingDeletedAsync(spa, sentBefore);
    }

    [Fact]
    public async Task A_confirmation_other_than_the_accounts_email_is_refused_and_nothing_is_deleted()
    {
        using var spa = await TestAccounts.SignedInAsync(_factory, Email);
        var sentBefore = _factory.Mailer.CountTo(Email);

        var deletion = await spa.PostAsync(DeletionPath, new { password = TestAccounts.Password, confirmation = OtherEmail });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, deletion.StatusCode);
        Assert.Equal(AccountsErrors.DeletionConfirmationMismatch.Code, await ProblemCode.ReadAsync(deletion));
        await AssertNothingDeletedAsync(spa, sentBefore);
    }

    [Fact]
    public async Task Missing_fields_are_refused_before_any_check()
    {
        using var spa = await TestAccounts.SignedInAsync(_factory, Email);

        var deletion = await spa.PostAsync(DeletionPath, new { password = "", confirmation = (string?)null });

        Assert.Equal(HttpStatusCode.BadRequest, deletion.StatusCode);
        Assert.Equal(1L, await CountByIdAsync("accounts", "id", _account));
    }

    [Fact]
    public async Task Guessing_the_password_runs_out_of_attempts()
    {
        using var spa = await TestAccounts.SignedInAsync(_factory, Email);
        for (var attempt = 0; attempt < 5; attempt++)
        {
            await spa.PostAsync(DeletionPath, new { password = $"guess {attempt}", confirmation = Email });
        }

        var refused = await spa.PostAsync(DeletionPath, new { password = TestAccounts.Password, confirmation = Email });

        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
        Assert.NotNull(refused.Headers.RetryAfter);
        Assert.Equal(1L, await CountByIdAsync("accounts", "id", _account));
    }

    [Fact]
    public async Task Without_the_csrf_token_nothing_is_deleted()
    {
        using var spa = await TestAccounts.SignedInAsync(_factory, Email);

        var deletion = await spa.SendAsync(HttpMethod.Post, DeletionPath, new { password = TestAccounts.Password, confirmation = Email }, csrfToken: "forged");

        Assert.Equal(HttpStatusCode.BadRequest, deletion.StatusCode);
        Assert.Equal(1L, await CountByIdAsync("accounts", "id", _account));
    }

    [Fact]
    public async Task Neither_an_anonymous_caller_nor_an_agent_deletes_the_account()
    {
        using var browser = await AgentAuthorization.SignedInBrowserAsync(_factory, Email);
        var agentTokens = await AgentAuthorization.ConnectAsync(_factory, browser);
        using var anonymous = new SpaClient(_factory.CreateClient());
        using var agent = new SpaClient(_factory.CreateClient());
        agent.Http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", agentTokens.AccessToken);
        var request = new { password = TestAccounts.Password, confirmation = Email };

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsync(DeletionPath, request)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await agent.PostAsync(DeletionPath, request)).StatusCode);
        Assert.Equal(1L, await CountByIdAsync("accounts", "id", _account));

        // No MCP tool deletes the Account (DA-082).
        await using var mcp = await AgentMcp.ConnectAsync(_factory, agentTokens.AccessToken);
        var tools = await mcp.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.DoesNotContain(tools, tool => tool.Name.Contains("account", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task A_failing_participant_rolls_the_whole_deletion_back()
    {
        await using var failing = new LifeGraphApiFactory(
            database,
            AgentAuthorization.Settings(),
            services => services.AddScoped<IAccountPurgeParticipant, FailingParticipant>());
        using var browser = await AgentAuthorization.SignedInBrowserAsync(failing, Email);
        await AgentAuthorization.ConnectAsync(failing, browser);

        var deletion = await browser.PostAsync(DeletionPath, new { password = TestAccounts.Password, confirmation = Email });

        Assert.Equal(HttpStatusCode.InternalServerError, deletion.StatusCode);
        Assert.Equal(1L, await CountByIdAsync("accounts", "id", _account));
        Assert.Equal(1L, await CountByIdAsync("users", "account_id", _account));
        Assert.Equal(1L, await CountByIdAsync("agent_identities", "account_id", _account));
        Assert.Equal(1L, await database.QueryScalarAsMigratorAsync<long>("SELECT count(*) FROM oidc_authorizations"));
        Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync("/api/sessions/current")).StatusCode);
        Assert.Equal(0, failing.Mailer.Sent.Count(email => email.Subject.Contains("deleted", StringComparison.Ordinal)));
        Assert.Equal(0L, await database.QueryScalarAsMigratorAsync<long>("SELECT count(*) FROM purge_ledger_entries"));
    }

    [Fact]
    public async Task A_notice_that_cannot_be_sent_does_not_undo_the_deletion()
    {
        using var spa = await TestAccounts.SignedInAsync(_factory, Email);
        _factory.Mailer.IsFailing = true;

        var deletion = await spa.PostAsync(DeletionPath, new { password = TestAccounts.Password, confirmation = Email });

        Assert.Equal(HttpStatusCode.OK, deletion.StatusCode);
        Assert.Equal(0L, await CountByIdAsync("accounts", "id", _account));
    }

    private async Task AssertNothingDeletedAsync(SpaClient spa, int sentBefore)
    {
        Assert.Equal(1L, await CountByIdAsync("accounts", "id", _account));
        Assert.Equal(1L, await CountByIdAsync("users", "account_id", _account));
        Assert.Equal(HttpStatusCode.OK, (await spa.GetAsync("/api/sessions/current")).StatusCode);
        Assert.Equal(sentBefore, _factory.Mailer.CountTo(Email));
    }

    // Nodes 1..n in a chain, each linked to the next, plus one Relation per Node to a scattered one:
    // about 2n Relations, as many rows as a heavy MVP Account.
    private Task SeedLargeGraphAsync(Guid accountId, int nodeCount) =>
        database.ExecuteAsMigratorAsync(
            """
            WITH seeded AS (
                SELECT position, md5(@account::text || '/' || position)::uuid AS id FROM generate_series(1, @count) position
            )
            INSERT INTO nodes (id, account_id, title, body, properties, version, created_at, updated_at)
            SELECT id, @account, 'Seeded ' || position, '', '{}', 1, now(), now() FROM seeded;

            WITH seeded AS (
                SELECT position, md5(@account::text || '/' || position)::uuid AS id FROM generate_series(1, @count) position
            )
            INSERT INTO relations (id, account_id, source_node_id, target_node_id, kind, assertion, origin, strength, created_at, updated_at)
            SELECT gen_random_uuid(), @account, pair.source_id, pair.target_id, 'related_to', 'hard', 'user', 1.0, now(), now()
            FROM (
                SELECT DISTINCT s.id AS source_id, o.id AS target_id
                FROM seeded s
                JOIN seeded o ON o.position = s.position + 1 OR o.position = 1 + (s.position * 7919) % @count
                WHERE o.id <> s.id
            ) pair;

            ANALYZE nodes;
            ANALYZE relations;
            """,
            TestContext.Current.CancellationToken,
            new NpgsqlParameter("account", accountId),
            new NpgsqlParameter("count", nodeCount));

    private Task<long> CountByIdAsync(string table, string column, Guid id) =>
        database.QueryScalarAsMigratorAsync<long>($"SELECT count(*) FROM {table} WHERE {column} = @id", new NpgsqlParameter("id", id));

    private static async Task<string> SharedLinkTokenAsync(SpaClient spa)
    {
        var node = await spa.PostAsync("/api/nodes", new { title = "Stoicism" });
        Assert.Equal(HttpStatusCode.Created, node.StatusCode);
        var receipt = await node.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        var root = receipt.GetProperty("changes")[0].GetProperty("entityId").GetGuid();
        var created = await spa.PostAsync(
            "/api/shared-graph-views",
            new { rootNodeId = root, maxDepth = 1, nodeIds = new[] { root }, propertyIds = Array.Empty<Guid>(), includeTags = false, legalNoticeAccepted = true });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var body = await created.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        return new Uri(body.GetProperty("shareUrl").GetString()!).Fragment.TrimStart('#');
    }

    private async Task<HttpResponseMessage> VisitShareAsync(string token)
    {
        using var visitor = _factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = LifeGraphApiFactory.ShareOrigin });
        visitor.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await visitor.GetAsync("/api/shared", TestContext.Current.CancellationToken);
    }

    private sealed class FailingParticipant : IAccountPurgeParticipant
    {
        public Task PurgeAsync(Guid accountId, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("A later participant failed.");
    }
}
