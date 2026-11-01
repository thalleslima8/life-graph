using LifeGraph.Graph.Contracts;
using LifeGraph.IntegrationTests.Infrastructure;

namespace LifeGraph.IntegrationTests.Graph;

/// <summary>
/// The plan's monthly write quota of the Account's agents (DA-261): an agent's own applied
/// operations consume it per UTC calendar month; past it, the whole write waits for the person,
/// and the person's writes and Undo never consume it.
/// </summary>
public sealed class AgentWriteQuotaTests(PostgresDatabase database) : IAsyncLifetime
{
    private const string UsedSql = "SELECT coalesce(sum(quota_operations), 0)::bigint FROM changesets";

    private readonly LifeGraphApiFactory _factory = new(database, new Dictionary<string, string>
    {
        ["Entitlements:Free:AgentWriteOperationsPerMonth"] = "3",
    });

    private GraphWriteHarness _graph = null!;
    private Guid _account;
    private Guid _root;

    public async ValueTask InitializeAsync()
    {
        await database.ResetAsync();
        _graph = new GraphWriteHarness(database, _factory);
        _account = await _graph.CreateAccountAsync();
        _root = await _graph.AccountRootIdAsync(_account);
    }

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    [Fact]
    public async Task Past_the_quota_the_whole_write_waits_for_the_person_and_nothing_is_applied()
    {
        var agent = await _graph.CreateAgentIdentityAsync(_account);
        var outcomes = new List<ChangeOutcome>();
        for (var write = 0; write < 4; write++)
        {
            outcomes.Add(await WriteAsAgentAsync(agent, $"Ideia {write}"));
        }

        var pending = Assert.IsType<ChangeOutcome.PendingApproval>(outcomes[^1]);
        Assert.Contains(ProposalReason.Quota, pending.Reasons);
        Assert.True(pending.RequiresUser);
        var used = await _graph.ScalarAsync<long>(UsedSql);
        Assert.InRange(used, 1L, 3L);
        Assert.Equal(outcomes.Count(outcome => outcome is ChangeOutcome.Applied), await _graph.ScalarAsync<long>("SELECT count(*) FROM changesets WHERE quota_operations > 0"));
    }

    [Fact]
    public async Task The_persons_writes_and_undo_never_consume_or_return_the_quota()
    {
        var agent = await _graph.CreateAgentIdentityAsync(_account);
        var applied = Assert.IsType<ChangeOutcome.Applied>(await WriteAsAgentAsync(agent, "Ideia"));
        var used = await _graph.ScalarAsync<long>(UsedSql);

        await _graph.WrittenAsync(_account, new CreateNode(Guid.CreateVersion7(), "Sêneca"));
        Assert.True((await _graph.UndoAsync(_account, applied.Receipt.ChangeSetId!.Value)).IsSuccess);

        Assert.Equal(used, await _graph.ScalarAsync<long>(UsedSql));
    }

    [Fact]
    public async Task What_agents_wrote_in_an_earlier_month_does_not_count()
    {
        var agent = await _graph.CreateAgentIdentityAsync(_account);
        for (var write = 0; write < 3; write++)
        {
            await WriteAsAgentAsync(agent, $"Ideia {write}");
        }

        await database.ExecuteAsMigratorAsync(
            "UPDATE changesets SET created_at = date_trunc('month', now() AT TIME ZONE 'UTC') AT TIME ZONE 'UTC' - interval '1 day' WHERE quota_operations > 0",
            TestContext.Current.CancellationToken);

        Assert.IsType<ChangeOutcome.Applied>(await WriteAsAgentAsync(agent, "Ideia nova"));
    }

    private async Task<ChangeOutcome> WriteAsAgentAsync(Guid agent, string title)
    {
        var written = await _graph.AsAgentAsync(_account, agent, writer =>
            writer.WriteAsync(new AgentWriteRequest([new AgentCreateNode(title) { Anchor = GraphWriteHarness.AgentAnchorAt(_root) }]), TestContext.Current.CancellationToken));
        Assert.True(written.IsSuccess, written.Error?.Code);
        return written.Value!;
    }
}
