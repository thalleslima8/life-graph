using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using LifeGraph.Graph.Contracts;
using LifeGraph.Infrastructure.Persistence;
using LifeGraph.IntegrationTests.Infrastructure;
using LifeGraph.Sharing.Operations;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;

namespace LifeGraph.IntegrationTests.Sharing;

/// <summary>
/// The owner's list of links, revoking one or all, the "compartilhado" badge's Nodes and the
/// operator's kill-switch (DA-078, DA-214, DA-217). Revoking takes effect on the very next visit,
/// and an invalid, revoked or blocked link answers the visitor exactly alike (DA-079).
/// </summary>
public sealed class SharedGraphViewRevocationTests(PostgresDatabase database) : IAsyncLifetime
{
    private const string EmailA = "ada@example.test";
    private const string EmailB = "grace@example.test";

    // What legitimately differs between two answers: the request's own trace and the clock.
    private static readonly string[] PerRequestFields = ["traceId", "correlationId", "requestId"];
    private static readonly string[] PerRequestHeaders = ["Date", "traceparent", "X-Correlation-Id", "X-Request-Id"];

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
    public async Task The_owner_lists_and_reads_its_links_and_another_account_sees_none_of_them()
    {
        var root = await CreatedNodeAsync("Stoicism");
        var (id, token) = await ShareAsync(root);
        using var other = await TestAccounts.SignedInAsync(_factory, EmailB);

        var list = await JsonOfAsync(await _spa.GetAsync("/api/shared-graph-views"));
        var read = await _spa.GetAsync($"/api/shared-graph-views/{id}");
        var foreignRead = await other.GetAsync($"/api/shared-graph-views/{id}");
        var foreignList = await JsonOfAsync(await other.GetAsync("/api/shared-graph-views"));
        var foreignRevoke = await other.PostAsync($"/api/shared-graph-views/{id}/revocation", new { });

        var listed = Assert.Single(list.RootElement.GetProperty("data").EnumerateArray());
        Assert.Equal(id, listed.GetProperty("id").GetGuid());
        Assert.Equal("Stoicism", listed.GetProperty("rootTitle").GetString());
        Assert.Equal(1, listed.GetProperty("nodeCount").GetInt32());
        Assert.Equal("active", listed.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, listed.GetProperty("lastAccessedOn").ValueKind);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.Equal(id, (await JsonOfAsync(read)).RootElement.GetProperty("id").GetGuid());
        Assert.Equal(HttpStatusCode.NotFound, foreignRead.StatusCode);
        Assert.Empty(foreignList.RootElement.GetProperty("data").EnumerateArray());
        Assert.Equal(HttpStatusCode.NotFound, foreignRevoke.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await VisitAsync(token)).StatusCode);
    }

    [Fact]
    public async Task Revoking_a_link_answers_its_next_visit_exactly_like_an_unknown_link()
    {
        var root = await CreatedNodeAsync("Stoicism");
        var (id, token) = await ShareAsync(root);
        Assert.Equal(HttpStatusCode.OK, (await VisitAsync(token)).StatusCode);

        var revoked = await _spa.PostAsync($"/api/shared-graph-views/{id}/revocation", new { });
        var again = await _spa.PostAsync($"/api/shared-graph-views/{id}/revocation", new { });

        Assert.Equal((HttpStatusCode.NoContent, HttpStatusCode.NoContent), (revoked.StatusCode, again.StatusCode));
        await AssertAnswersLikeAnUnknownLinkAsync(token);
        var summary = (await JsonOfAsync(await _spa.GetAsync($"/api/shared-graph-views/{id}"))).RootElement;
        Assert.Equal("revoked", summary.GetProperty("status").GetString());
        Assert.NotEqual(JsonValueKind.Null, summary.GetProperty("revokedAt").ValueKind);
    }

    [Fact]
    public async Task Revoking_all_closes_every_link_at_once_and_clears_the_badge()
    {
        var first = await CreatedNodeAsync("Stoicism");
        var second = await CreatedNodeAsync("Epicureanism");
        var (_, firstToken) = await ShareAsync(first);
        var (_, secondToken) = await ShareAsync(second);
        var badgeBefore = await SharedNodeIdsAsync();

        var revoked = await _spa.PostAsync("/api/shared-graph-views/revocation", new { });

        Assert.Equal(HttpStatusCode.NoContent, revoked.StatusCode);
        Assert.Equal(new[] { first, second }.Order(), badgeBefore.Order());
        Assert.Empty(await SharedNodeIdsAsync());
        await AssertAnswersLikeAnUnknownLinkAsync(firstToken);
        await AssertAnswersLikeAnUnknownLinkAsync(secondToken);
        var list = await JsonOfAsync(await _spa.GetAsync("/api/shared-graph-views"));
        Assert.All(list.RootElement.GetProperty("data").EnumerateArray(), link => Assert.Equal("revoked", link.GetProperty("status").GetString()));
    }

    [Fact]
    public async Task The_operator_blocks_a_link_by_its_public_reference_and_its_visitor_gets_the_unknown_link_answer()
    {
        var root = await CreatedNodeAsync("Stoicism");
        var (id, token) = await ShareAsync(root);
        var publicRef = (await JsonOfAsync(await VisitAsync(token))).RootElement.GetProperty("publicRef").GetString()!;
        await using var operatorHost = new LifeGraphApiFactory(database, new Dictionary<string, string>
        {
            [$"ConnectionStrings:{PersistenceServiceCollectionExtensions.ConnectionStringName}"] = database.OperatorConnectionString,
        });

        var (disabled, disabledOutput) = await RunSharesAsync(operatorHost, "shares", "disable", publicRef);
        var (again, _) = await RunSharesAsync(operatorHost, "shares", "disable", publicRef);
        var (unknown, _) = await RunSharesAsync(operatorHost, "shares", "disable", "shr_unknown");
        var (usage, _) = await RunSharesAsync(operatorHost, "shares", "disable");

        Assert.Equal(
            [SharesCommandLine.Success, SharesCommandLine.Success, SharesCommandLine.Failure, SharesCommandLine.UsageError],
            new[] { disabled, again, unknown, usage });
        Assert.DoesNotContain(token, disabledOutput, StringComparison.Ordinal);
        await AssertAnswersLikeAnUnknownLinkAsync(token);
        var summary = (await JsonOfAsync(await _spa.GetAsync($"/api/shared-graph-views/{id}"))).RootElement;
        Assert.Equal("disabled_by_platform", summary.GetProperty("status").GetString());
        Assert.Empty(await SharedNodeIdsAsync());
    }

    [Fact]
    public async Task The_operator_role_reaches_only_the_public_reference_and_the_block()
    {
        var grants = new List<string>();
        await using (var connection = new NpgsqlConnection(database.MigratorConnectionString))
        {
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            await using var command = new NpgsqlCommand(
                """
                SELECT table_name || '.' || column_name || ':' || privilege_type
                FROM information_schema.column_privileges
                WHERE grantee = @role
                ORDER BY 1
                """,
                connection);
            command.Parameters.AddWithValue("role", DatabaseRoles.Operator);
            await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
            while (await reader.ReadAsync(TestContext.Current.CancellationToken))
            {
                grants.Add(reader.GetString(0));
            }
        }

        Assert.Equal(
            [
                "shared_graph_view_directory.blocked_at:SELECT",
                "shared_graph_view_directory.blocked_at:UPDATE",
                "shared_graph_view_directory.public_ref:SELECT",
            ],
            grants);
        foreach (var query in new[] { "SELECT token_hash FROM shared_graph_view_directory", "SELECT count(*) FROM shared_graph_views", "SELECT count(*) FROM nodes" })
        {
            var violation = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsOperatorAsync(query));
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, violation.SqlState);
        }
    }

    /// <summary>Status, body (but its trace) and headers (but the date and trace) equal to an unknown token's.</summary>
    private async Task AssertAnswersLikeAnUnknownLinkAsync(string token)
    {
        var unknown = await VisitAsync(new string('A', token.Length));
        var closed = await VisitAsync(token);

        Assert.Equal(HttpStatusCode.NotFound, closed.StatusCode);
        Assert.Equal(unknown.StatusCode, closed.StatusCode);
        Assert.Equal(await ComparableBodyAsync(unknown), await ComparableBodyAsync(closed));
        Assert.Equal(ComparableHeaders(unknown), ComparableHeaders(closed));
    }

    private static async Task<string> ComparableBodyAsync(HttpResponseMessage response)
    {
        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!.AsObject();
        foreach (var field in PerRequestFields)
        {
            body.Remove(field);
        }

        return body.ToJsonString();
    }

    private static List<string> ComparableHeaders(HttpResponseMessage response) =>
        response.Headers.Concat(response.Content.Headers)
            .Where(header => !PerRequestHeaders.Contains(header.Key, StringComparer.OrdinalIgnoreCase))
            .Select(header => $"{header.Key}: {string.Join(',', header.Value)}")
            .Order(StringComparer.Ordinal)
            .ToList();

    private async Task<HttpResponseMessage> VisitAsync(string token)
    {
        using var visitor = _factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = LifeGraphApiFactory.ShareOrigin });
        visitor.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await visitor.GetAsync("/api/shared", TestContext.Current.CancellationToken);
    }

    private async Task<(Guid Id, string Token)> ShareAsync(Guid root)
    {
        var created = await _spa.PostAsync(
            "/api/shared-graph-views",
            new { rootNodeId = root, maxDepth = 1, nodeIds = new[] { root }, propertyIds = Array.Empty<Guid>(), includeTags = false, legalNoticeAccepted = true });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var body = await created.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        var id = body.GetProperty("id").GetGuid();
        var token = new Uri(body.GetProperty("shareUrl").GetString()!).Fragment.TrimStart('#');
        return (id, token);
    }

    private async Task<List<Guid>> SharedNodeIdsAsync()
    {
        var page = await JsonOfAsync(await _spa.GetAsync("/api/shared-graph-views/node-ids"));
        return page.RootElement.GetProperty("data").EnumerateArray().Select(id => id.GetGuid()).ToList();
    }

    private async Task<Guid> CreatedNodeAsync(string title)
    {
        var response = await _spa.PostAsync("/api/nodes", new { title });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var receipt = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        return receipt.GetProperty("changes")[0].GetProperty("entityId").GetGuid();
    }

    private static async Task<JsonDocument> JsonOfAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

    private static async Task<(int ExitCode, string Output)> RunSharesAsync(LifeGraphApiFactory host, params string[] args)
    {
        await using var output = new StringWriter();
        var exitCode = await SharesCommandLine.RunAsync(host.Services, args, output, TestContext.Current.CancellationToken);
        return (exitCode, output.ToString());
    }

    private async Task ExecuteAsOperatorAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(database.OperatorConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);
    }
}
