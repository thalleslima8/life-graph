using System.Net;
using LifeGraph.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;

namespace LifeGraph.IntegrationTests.Agents;

/// <summary>
/// DA-122: a grant is exactly what the person checked, a new consent replaces it, and a tool that
/// changes the graph needs <c>lifegraph.write</c> (MCP step-up: 403 <c>insufficient_scope</c>).
/// E3 has no write tool yet, so the test host adds one that is not read-only.
/// </summary>
public sealed class AgentScopeTests(PostgresDatabase database) : IAsyncLifetime
{
    private const string Email = "ada@example.test";
    private const string WriteTool = "test_write";

    private readonly LifeGraphApiFactory _factory = new(
        database,
        AgentAuthorization.Settings(),
        services => services.AddSingleton(McpServerTool.Create(
            () => "written",
            new McpServerToolCreateOptions { Name = WriteTool, ReadOnly = false, Destructive = false })));

    public async ValueTask InitializeAsync()
    {
        await database.ResetAsync();
        await TestAccounts.ProvisionConfirmedAsync(_factory, Email);
    }

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    [Fact]
    public async Task A_write_tool_answers_with_write_and_steps_up_without_it()
    {
        using var browser = await AgentAuthorization.SignedInBrowserAsync(_factory, Email);
        var readOnly = await AgentAuthorization.ConnectAsync(_factory, browser, scope: "lifegraph.read");
        using var agent = _factory.CreateClient();

        var refused = await AgentAuthorization.CallToolAsync(agent, readOnly.AccessToken, WriteTool);
        var readTool = await AgentAuthorization.CallToolAsync(agent, readOnly.AccessToken, "whoami");

        AssertInsufficientScope(refused);
        Assert.Equal(HttpStatusCode.OK, readTool.StatusCode);

        var readWrite = await AgentAuthorization.ConnectAsync(_factory, browser, scope: "lifegraph.read lifegraph.write");
        Assert.Equal(HttpStatusCode.OK, (await AgentAuthorization.CallToolAsync(agent, readWrite.AccessToken, WriteTool)).StatusCode);
    }

    // DA-122: a write call cannot hide in a JSON-RPC batch behind a read one.
    [Fact]
    public async Task A_batch_with_a_write_call_steps_up_without_write()
    {
        using var browser = await AgentAuthorization.SignedInBrowserAsync(_factory, Email);
        var readOnly = await AgentAuthorization.ConnectAsync(_factory, browser, scope: "lifegraph.read");
        using var agent = _factory.CreateClient();

        AssertInsufficientScope(await AgentAuthorization.CallToolsInBatchAsync(agent, readOnly.AccessToken, "whoami", WriteTool));
    }

    // DA-122: reauthorizing replaces the scopes (never unites them) and ends the previous grant.
    [Fact]
    public async Task Consenting_again_without_write_replaces_the_grant_and_ends_the_old_one()
    {
        using var browser = await AgentAuthorization.SignedInBrowserAsync(_factory, Email);
        var withWrite = await AgentAuthorization.ConnectAsync(_factory, browser, scope: "lifegraph.read lifegraph.write");

        var withoutWrite = await AgentAuthorization.ConnectAsync(_factory, browser, scope: "lifegraph.read");

        using var agent = _factory.CreateClient();
        var oldRefresh = await AgentAuthorization.RefreshAsync(agent, withWrite.RefreshToken!);
        Assert.Equal(HttpStatusCode.BadRequest, oldRefresh.StatusCode);
        Assert.Contains("invalid_grant", await oldRefresh.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Unauthorized, (await AgentAuthorization.CallToolAsync(agent, withWrite.AccessToken, WriteTool)).StatusCode);
        AssertInsufficientScope(await AgentAuthorization.CallToolAsync(agent, withoutWrite.AccessToken, WriteTool));
        Assert.Equal(["lifegraph.read"], await database.QueryScalarAsMigratorAsync<string[]>("SELECT scopes FROM agent_identities"));
    }

    private static void AssertInsufficientScope(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var challenge = Assert.Single(response.Headers.WwwAuthenticate);
        Assert.Equal("Bearer", challenge.Scheme);
        Assert.Contains("error=\"insufficient_scope\"", challenge.Parameter, StringComparison.Ordinal);
        Assert.Contains("scope=\"lifegraph.write\"", challenge.Parameter, StringComparison.Ordinal);
    }
}
