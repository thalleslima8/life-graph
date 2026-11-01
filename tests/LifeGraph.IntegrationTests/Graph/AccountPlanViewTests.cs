using System.Net;
using System.Text.Json;
using LifeGraph.Accounts.Provisioning;
using LifeGraph.IntegrationTests.Infrastructure;

namespace LifeGraph.IntegrationTests.Graph;

/// <summary>
/// <c>GET /api/account/plan</c> (E13, DA-266): the plan, every limit with what the Account uses of
/// it and the agent-write quota with its reset date, counted as the limits count (DA-264). No
/// price and no offer: only where the Account stands (DA-260).
/// </summary>
public sealed class AccountPlanViewTests(PostgresDatabase database) : IAsyncLifetime
{
    private const string PlanPath = "/api/account/plan";
    private const string Email = "plano@lifegraph.test";

    private readonly LifeGraphApiFactory _factory = new(database, AgentAuthorization.Settings());

    public async ValueTask InitializeAsync() => await database.ResetAsync();

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    [Fact]
    public async Task A_new_account_is_on_free_with_the_section_55_limits_nothing_used_and_the_quota_resetting_next_month()
    {
        await TestAccounts.ProvisionConfirmedAsync(_factory, Email);
        using var browser = await AgentAuthorization.SignedInBrowserAsync(_factory, Email);

        var plan = await PlanAsync(browser);

        Assert.Equal("free", plan.GetProperty("plan").GetString());
        var limits = LimitsOf(plan);
        Assert.Equal(
            new Dictionary<string, (int, int?)>
            {
                ["nodes"] = (0, 500),
                ["relations"] = (0, 2_000),
                ["resources"] = (0, 100),
                ["collections"] = (0, 10),
                ["custom_types"] = (0, 5),
                ["custom_properties"] = (0, 20),
                ["agent_connections"] = (0, 1),
                ["shared_graph_views"] = (0, 3),
            },
            limits);
        Assert.Equal(25, plan.GetProperty("nodesPerSharedGraphView").GetInt32());
        var agentWrites = plan.GetProperty("agentWrites");
        Assert.Equal(0, agentWrites.GetProperty("used").GetInt32());
        Assert.Equal(100, agentWrites.GetProperty("max").GetInt32());
        var now = DateTimeOffset.UtcNow;
        var nextMonth = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero).AddMonths(1);
        Assert.Equal(nextMonth, agentWrites.GetProperty("resetsAt").GetDateTimeOffset());
    }

    [Fact]
    public async Task Usage_counts_the_persons_nodes_and_connections_but_not_the_account_root_and_premium_shows_its_limits()
    {
        await TestAccounts.ProvisionConfirmedAsync(_factory, Email);
        using var browser = await AgentAuthorization.SignedInBrowserAsync(_factory, Email);
        Assert.Equal(HttpStatusCode.Created, (await browser.PostAsync("/api/nodes", new { title = "Stoicism" })).StatusCode);
        await AgentAuthorization.ConnectAsync(_factory, browser);

        var free = LimitsOf(await PlanAsync(browser));
        Assert.Equal((1, (int?)500), free["nodes"]);
        Assert.Equal((1, (int?)1), free["agent_connections"]);

        var changed = await AccountsCommandLine.RunAsync(
            _factory.Provisioning.Services, ["accounts", "plan", "--email", Email, "--plan", "premium"], TextWriter.Null, TestContext.Current.CancellationToken);
        Assert.Equal(AccountsCommandLine.Success, changed);
        var premium = await PlanAsync(browser);

        Assert.Equal("premium", premium.GetProperty("plan").GetString());
        Assert.Equal((1, (int?)10_000), LimitsOf(premium)["nodes"]);
        Assert.Equal(250, premium.GetProperty("nodesPerSharedGraphView").GetInt32());
    }

    [Fact]
    public async Task Without_a_session_the_plan_is_refused()
    {
        using var anonymous = AgentAuthorization.Browser(_factory);

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(PlanPath, TestContext.Current.CancellationToken)).StatusCode);
    }

    private static async Task<JsonElement> PlanAsync(SpaClient browser)
    {
        var response = await browser.GetAsync(PlanPath);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).RootElement.Clone();
    }

    private static Dictionary<string, (int, int?)> LimitsOf(JsonElement plan) =>
        plan.GetProperty("limits").EnumerateArray().ToDictionary(
            limit => limit.GetProperty("kind").GetString()!,
            limit => (limit.GetProperty("used").GetInt32(), limit.GetProperty("max").ValueKind == JsonValueKind.Null ? (int?)null : limit.GetProperty("max").GetInt32()));
}
