using LifeGraph.Graph.Contracts;
using LifeGraph.Graph.Domain;
using LifeGraph.Infrastructure.Errors;
using LifeGraph.Infrastructure.Identity;
using LifeGraph.Infrastructure.Persistence;
using LifeGraph.IntegrationTests.Infrastructure;
using Limaj.Framework.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace LifeGraph.IntegrationTests.Graph;

/// <summary>
/// SharedGraphViews against a real Postgres (DA-075, DA-076, DA-219, DA-220): the proposal walks
/// Hard Relations only, never reaches a Node Oculto para agentes nor the Raiz da Conta, and the
/// creation checks every Node again on the server, holds at most three links and stores only the
/// token's hash.
/// </summary>
public sealed class SharedGraphViewTests(PostgresDatabase database) : IAsyncLifetime
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
    public async Task Proposal_walks_hard_relations_up_to_the_depth_and_leaves_out_hidden_nodes_and_the_account_root()
    {
        var stoicism = await CreateNodeAsync("Stoicism");
        var seneca = await CreateNodeAsync("Seneca");
        var letters = await CreateNodeAsync("Letters");
        var diary = await CreateNodeAsync("Diary");
        await RelateAsync(stoicism, seneca);
        await RelateAsync(seneca, letters);
        await RelateAsync(stoicism, diary);
        await RelateAsync(stoicism, await _graph.AccountRootIdAsync(_account));
        await _graph.WrittenAsync(_account, new UpdateNode(diary, 1) { HiddenFromAgents = true });

        var depthOne = Succeeded(await ProposeAsync(stoicism, null));
        var depthTwo = Succeeded(await ProposeAsync(stoicism, 2));

        Assert.Equal(SharingLimits.DefaultDepth, depthOne.MaxDepth);
        Assert.Equal([stoicism, seneca], depthOne.Nodes.Select(node => node.Id));
        Assert.Equal([0, 1], depthOne.Nodes.Select(node => node.Distance));
        Assert.Equal([stoicism, seneca, letters], depthTwo.Nodes.Select(node => node.Id));
        Assert.False(depthTwo.Truncated);
        Assert.Equal(3, depthTwo.EligibleCount);
    }

    [Fact]
    public async Task Proposal_holds_twenty_five_nodes_and_reports_how_many_were_eligible()
    {
        var hub = await CreateNodeAsync("Hub");
        for (var index = 0; index < 30; index++)
        {
            await RelateAsync(hub, await CreateNodeAsync($"Leaf {index}"));
        }

        var proposal = Succeeded(await ProposeAsync(hub, 1));

        Assert.Equal(SharingLimits.MaxNodesPerShare, proposal.Nodes.Count);
        Assert.Equal(31, proposal.EligibleCount);
        Assert.True(proposal.Truncated);
        Assert.Equal(hub, proposal.Nodes[0].Id);
    }

    [Fact]
    public async Task Proposal_offers_only_the_properties_of_the_proposed_nodes_types_without_the_resource_url()
    {
        var author = await DefineAsync("Translator");
        var unused = await DefineAsync("Unused");
        var book = Guid.CreateVersion7();
        await _graph.WrittenAsync(_account, new CreateType(book, "Book", [author]));
        await DefineAsync("Elsewhere");
        var root = await CreateNodeAsync("Meditations", book);

        var proposal = Succeeded(await ProposeAsync(root, 1));

        Assert.Equal([author], proposal.Properties.Select(property => property.Id));
        Assert.DoesNotContain(unused, proposal.Properties.Select(property => property.Id));
    }

    [Fact]
    public async Task The_account_root_is_never_a_share_root()
    {
        var root = await _graph.AccountRootIdAsync(_account);

        var proposal = await ProposeAsync(root, 1);
        var created = await CreateAsync(new CreateSharedGraphView(root, 1, [root], null, false, true));

        Assert.Equal(SharingErrors.RootNotShareable.Code, proposal.Error?.Code);
        Assert.Equal(SharingErrors.RootNotShareable.Code, created.Error?.Code);
    }

    [Fact]
    public async Task A_node_of_another_account_is_not_found()
    {
        var other = await _graph.CreateAccountAsync();
        var foreign = Guid.CreateVersion7();
        await _graph.WrittenAsync(other, new CreateNode(foreign, "Foreign"));

        var proposal = await ProposeAsync(foreign, 1);

        Assert.Equal(GraphErrors.NodeNotFound.Code, proposal.Error?.Code);
    }

    [Fact]
    public async Task An_agent_neither_proposes_nor_creates_a_share()
    {
        var root = await CreateNodeAsync("Stoicism");
        var agent = await _graph.CreateAgentIdentityAsync(_account);
        var asAgent = new AuthenticatedPrincipal(_account, PrincipalType.AgentIdentity, agent);

        var proposal = await _graph.AsAsync(asAgent, services => services.GetRequiredService<ISharedGraphViews>()
            .ProposeAsync(root, 1, TestContext.Current.CancellationToken));
        var created = await _graph.AsAsync(asAgent, services => services.GetRequiredService<ISharedGraphViews>()
            .CreateAsync(new CreateSharedGraphView(root, 1, [root], null, false, true), TestContext.Current.CancellationToken));

        Assert.Equal(CommonErrors.Forbidden.Code, proposal.Error?.Code);
        Assert.Equal(CommonErrors.Forbidden.Code, created.Error?.Code);
        Assert.Equal(0, await _graph.CountAsync("shared_graph_views"));
    }

    [Fact]
    public async Task Create_stores_the_share_its_allowlists_and_only_the_token_hash()
    {
        var author = await DefineAsync("Translator");
        var book = Guid.CreateVersion7();
        await _graph.WrittenAsync(_account, new CreateType(book, "Book", [author]));
        var root = await CreateNodeAsync("Meditations", book);
        var seneca = await CreateNodeAsync("Seneca");
        await RelateAsync(root, seneca);

        var changeSetsBefore = await _graph.CountAsync("changesets");

        var created = Succeeded(await CreateAsync(new CreateSharedGraphView(root, 1, [root, seneca], [author], true, true)));

        Assert.Equal(2, await _graph.CountAsync("shared_graph_view_nodes"));
        Assert.Equal(1, await _graph.CountAsync("shared_graph_view_properties"));
        var storedHash = await _graph.ScalarAsync<byte[]>(
            "SELECT token_hash FROM shared_graph_view_directory WHERE shared_graph_view_id = @id",
            new NpgsqlParameter("id", created.Id));
        Assert.Equal(ShareToken.HashOfPresented(created.Token), storedHash);
        Assert.Equal(changeSetsBefore, await _graph.CountAsync("changesets"));
    }

    [Fact]
    public async Task Create_rejects_a_node_outside_the_walk_a_property_outside_the_types_and_a_missing_notice()
    {
        var root = await CreateNodeAsync("Stoicism");
        var unrelated = await CreateNodeAsync("Unrelated");
        var property = await DefineAsync("Translator");

        var smuggled = await CreateAsync(new CreateSharedGraphView(root, 1, [root, unrelated], null, false, true));
        var wrongProperty = await CreateAsync(new CreateSharedGraphView(root, 1, [root], [property], false, true));
        var noNotice = await CreateAsync(new CreateSharedGraphView(root, 1, [root], null, false, false));

        Assert.Equal(SharingErrors.NodeNotEligible.Code, smuggled.Error?.Code);
        Assert.Equal(SharingErrors.PropertyNotEligible.Code, wrongProperty.Error?.Code);
        Assert.Equal(SharingErrors.LegalNoticeNotAccepted.Code, noNotice.Error?.Code);
        Assert.Equal(0, await _graph.CountAsync("shared_graph_views"));
    }

    [Fact]
    public async Task Create_accepts_an_eligible_node_past_the_twenty_five_proposed()
    {
        var hub = await CreateNodeAsync("Hub");
        for (var index = 0; index < 30; index++)
        {
            await RelateAsync(hub, await CreateNodeAsync($"Leaf {index}"));
        }

        var proposal = Succeeded(await ProposeAsync(hub, 1));
        var leftOut = await _graph.ScalarAsync<Guid>(
            "SELECT id FROM nodes WHERE account_id = @account AND system_key IS NULL AND NOT (id = ANY(@proposed)) LIMIT 1",
            new NpgsqlParameter("account", _account),
            new NpgsqlParameter("proposed", proposal.Nodes.Select(node => node.Id).ToArray()));

        var created = await CreateAsync(new CreateSharedGraphView(hub, 1, [hub, leftOut], null, false, true));

        Assert.True(created.IsSuccess, created.Error?.Code);
    }

    [Fact]
    public async Task A_fourth_link_is_refused_until_one_is_revoked()
    {
        var root = await CreateNodeAsync("Stoicism");
        for (var index = 0; index < SharingLimits.MaxActiveShares; index++)
        {
            Succeeded(await CreateAsync(new CreateSharedGraphView(root, 1, [root], null, false, true)));
        }

        var fourth = await CreateAsync(new CreateSharedGraphView(root, 1, [root], null, false, true));
        Assert.Equal(SharingErrors.LimitReached.Code, fourth.Error?.Code);

        await database.ExecuteAsMigratorAsync(
            "UPDATE shared_graph_views SET revoked_at = now(), revoked_by = 'human' WHERE id = (SELECT id FROM shared_graph_views LIMIT 1)",
            TestContext.Current.CancellationToken);
        var afterRevoke = await CreateAsync(new CreateSharedGraphView(root, 1, [root], null, false, true));
        Assert.True(afterRevoke.IsSuccess, afterRevoke.Error?.Code);
    }

    [Fact]
    public async Task Rls_keeps_another_accounts_shares_out_of_reach()
    {
        var root = await CreateNodeAsync("Stoicism");
        Succeeded(await CreateAsync(new CreateSharedGraphView(root, 1, [root], null, false, true)));
        var other = await _graph.CreateAccountAsync();

        await using var scope = _factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TestAccountContext>().ActAs(other);
        var db = scope.ServiceProvider.GetRequiredService<LifeGraphDbContext>();
        var seenByOther = await db.InAccountTransactionAsync(
            token => db.Set<SharedGraphView>().CountAsync(token),
            TestContext.Current.CancellationToken);

        Assert.Equal(0, seenByOther);
    }

    [Fact]
    public async Task Purge_of_a_member_removes_it_and_purge_of_the_root_revokes_the_share()
    {
        var root = await CreateNodeAsync("Stoicism");
        var seneca = await CreateNodeAsync("Seneca");
        var relation = await RelateAsync(root, seneca);
        var created = Succeeded(await CreateAsync(new CreateSharedGraphView(root, 1, [root, seneca], null, false, true)));

        await _graph.WrittenAsync(_account, new DeleteRelation(relation));
        await _graph.WrittenAsync(_account, new DeleteNode(seneca, 1));
        await _graph.WrittenAsync(_account, new DeleteNode(root, 1));
        await _graph.MakePurgesDueAsync();
        await _graph.RunDueJobsAsync();

        Assert.Equal(0, await _graph.CountAsync("shared_graph_view_nodes"));
        Assert.Equal(1, await _graph.ScalarAsync<long>(
            "SELECT count(*) FROM shared_graph_views WHERE id = @id AND root_node_id IS NULL AND revoked_at IS NOT NULL AND revoked_by = 'system'",
            new NpgsqlParameter("id", created.Id)));
    }

    private Task<Result<SharedGraphViewProposal>> ProposeAsync(Guid rootNodeId, int? depth) =>
        _graph.AsAsync(new AuthenticatedPrincipal(_account, PrincipalType.Human), services => services.GetRequiredService<ISharedGraphViews>()
            .ProposeAsync(rootNodeId, depth, TestContext.Current.CancellationToken));

    private Task<Result<CreatedSharedGraphView>> CreateAsync(CreateSharedGraphView request) =>
        _graph.AsAsync(new AuthenticatedPrincipal(_account, PrincipalType.Human), services => services.GetRequiredService<ISharedGraphViews>()
            .CreateAsync(request, TestContext.Current.CancellationToken));

    private static T Succeeded<T>(Result<T> result)
    {
        Assert.True(result.IsSuccess, result.Error?.Code);
        return result.Value!;
    }

    private async Task<Guid> CreateNodeAsync(string title, Guid? type = null)
    {
        var id = Guid.CreateVersion7();
        await _graph.WrittenAsync(_account, new CreateNode(id, title, TypeId: type));
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
