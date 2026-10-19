using LifeGraph.Graph.Application;
using LifeGraph.Graph.Contracts;
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
/// The agent write use cases called directly, with no MCP adapter in between: what an agent
/// sees of the proposals comes from the central read filter, and the connection's toggle and
/// revocation are read by the use case itself, whatever a caller would send.
/// </summary>
public sealed class AgentGraphWriterTests(PostgresDatabase database) : IAsyncLifetime
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

    // CLAUDE.md, DA-043 item 1: every read of a ChangeProposal goes through the central filter.
    [Fact]
    public async Task The_person_sees_every_proposal_an_agent_only_its_own_and_any_other_principal_none()
    {
        var first = await _graph.CreateAgentIdentityAsync(_account);
        var second = await _graph.CreateAgentIdentityAsync(_account);
        var firstsProposal = await SuggestAsync(first, "First's idea");
        var secondsProposal = await SuggestAsync(second, "Second's idea");

        var byPerson = await VisibleProposalsAsync(new AuthenticatedPrincipal(_account, PrincipalType.Human));
        var byFirst = await VisibleProposalsAsync(new AuthenticatedPrincipal(_account, PrincipalType.AgentIdentity, first));
        var byVisitor = await VisibleProposalsAsync(new AuthenticatedPrincipal(_account, PrincipalType.ShareVisitor));
        var byAgentWithoutConnection = await VisibleProposalsAsync(new AuthenticatedPrincipal(_account, PrincipalType.AgentIdentity));

        Assert.Equal(new[] { firstsProposal, secondsProposal }.Order(), byPerson.Order());
        Assert.Equal([firstsProposal], byFirst);
        Assert.Empty(byVisitor);
        Assert.Empty(byAgentWithoutConnection);
    }

    // DA-043 item 1: another agent's proposal is not found, whatever the agent does with it.
    [Fact]
    public async Task Another_agents_proposal_is_not_found_for_status_withdrawal_or_application()
    {
        var first = await _graph.CreateAgentIdentityAsync(_account);
        var second = await _graph.CreateAgentIdentityAsync(_account);
        var firstsProposal = await SuggestAsync(first, "First's idea");

        var status = await _graph.AsAgentAsync(_account, second, writer => writer.GetProposalStatusAsync(firstsProposal, TestContext.Current.CancellationToken));
        var withdrawn = await _graph.AsAgentAsync(_account, second, writer => writer.WithdrawProposalAsync(firstsProposal, TestContext.Current.CancellationToken));
        var applied = await _graph.AsAgentAsync(_account, second, writer => writer.ApplyProposalAsync(firstsProposal, "k1", TestContext.Current.CancellationToken));

        Assert.Equal(AgentWriteErrors.ProposalNotFound.Code, status.Error!.Code);
        Assert.Equal(AgentWriteErrors.ProposalNotFound.Code, withdrawn.Error!.Code);
        Assert.Equal(AgentWriteErrors.ProposalNotFound.Code, applied.Error!.Code);
        Assert.Equal("proposed", await ProposalStatusAsync(firstsProposal));
    }

    // DA-044: the use case reads the toggle from the connection; no caller can turn it off.
    [Fact]
    public async Task With_the_toggle_on_a_direct_write_waits_for_the_person_whatever_the_caller()
    {
        var agent = await _graph.CreateAgentIdentityAsync(_account, requiresApproval: true);

        var written = await _graph.AsAgentAsync(_account, agent, writer =>
            writer.WriteAsync(new AgentWriteRequest([new AgentCreateNode("Idea")]), TestContext.Current.CancellationToken));

        var pending = Assert.IsType<ChangeOutcome.PendingApproval>(written.Value);
        Assert.True(pending.RequiresUser);
        Assert.Equal([ProposalReason.ApprovalRequired], pending.Reasons);
        Assert.Equal(0, await _graph.CountAsync("nodes"));
    }

    // DA-043 item 4, DA-044: turned on after the suggestion, the toggle still stops the agent's own application.
    [Fact]
    public async Task With_the_toggle_turned_on_later_the_agent_cannot_apply_its_suggestion()
    {
        var agent = await _graph.CreateAgentIdentityAsync(_account);
        var proposal = await SuggestAsync(agent, "Idea");
        await database.ExecuteAsMigratorAsync(
            "UPDATE agent_identities SET requires_approval_for_writes = true WHERE id = @id",
            TestContext.Current.CancellationToken,
            new NpgsqlParameter("id", agent));

        var applied = await _graph.AsAgentAsync(_account, agent, writer => writer.ApplyProposalAsync(proposal, "k1", TestContext.Current.CancellationToken));

        Assert.Equal(AgentWriteErrors.ProposalRequiresUser.Code, applied.Error!.Code);
        Assert.True(await _graph.ScalarAsync<bool>("SELECT requires_user FROM change_proposals WHERE id = @id", new NpgsqlParameter("id", proposal)));
        Assert.Equal(0, await _graph.CountAsync("nodes"));
    }

    // DA-105: a connection revoked after its request was authenticated gets a catalog code, and nothing is stored.
    [Fact]
    public async Task A_connection_revoked_since_the_request_was_authenticated_is_refused_as_unauthorized()
    {
        var agent = await _graph.CreateAgentIdentityAsync(_account);
        var proposal = await SuggestAsync(agent, "Idea");
        await database.ExecuteAsMigratorAsync(
            "UPDATE agent_identities SET revoked_at = now() WHERE id = @id",
            TestContext.Current.CancellationToken,
            new NpgsqlParameter("id", agent));

        var written = await _graph.AsAgentAsync(_account, agent, writer =>
            writer.WriteAsync(new AgentWriteRequest([new AgentCreateNode("Another")]), TestContext.Current.CancellationToken));
        var suggested = await _graph.AsAgentAsync(_account, agent, writer =>
            writer.SuggestAsync(new AgentWriteRequest([new AgentCreateNode("Another")]), TestContext.Current.CancellationToken));
        var applied = await _graph.AsAgentAsync(_account, agent, writer => writer.ApplyProposalAsync(proposal, "k1", TestContext.Current.CancellationToken));
        var withdrawn = await _graph.AsAgentAsync(_account, agent, writer => writer.WithdrawProposalAsync(proposal, TestContext.Current.CancellationToken));

        Assert.All([written.Error!, suggested.Error!, applied.Error!, withdrawn.Error!], error =>
        {
            Assert.Equal(CommonErrors.Unauthorized.Code, error.Code);
            Assert.Equal(AgentGraphWriter.ConnectionRevokedMessage, error.Message);
        });
        Assert.Equal(0, await _graph.CountAsync("nodes"));
        Assert.Equal(1, await _graph.CountAsync("change_proposals"));
        Assert.Equal("proposed", await ProposalStatusAsync(proposal));
    }

    // DA-043 item 7, DA-035: the same key answers the same, less what the agent no longer sees:
    // a Node hidden from agents since, and the Relation that touches it, are left out; a Node
    // the person deleted since is still named, as the first answer named it.
    [Fact]
    public async Task A_retry_with_the_same_key_leaves_out_a_node_hidden_from_agents_since()
    {
        var agent = await _graph.CreateAgentIdentityAsync(_account);
        var suggested = await _graph.AsAgentAsync(_account, agent, writer => writer.SuggestAsync(
            new AgentWriteRequest(
            [
                new AgentCreateNode("Kept", Ref: "kept"),
                new AgentCreateNode("Deleted", Ref: "deleted"),
                new AgentCreateNode("Hidden", Ref: "hidden"),
                new AgentCreateRelation(new NodeRef(null, "kept"), new NodeRef(null, "hidden"), "about"),
            ]),
            TestContext.Current.CancellationToken));
        var proposal = Assert.IsType<ChangeOutcome.PendingApproval>(suggested.Value).ProposalId;
        var first = Assert.IsType<ChangeOutcome.Applied>((await ApplyAsync(agent, proposal, "k1")).Value);
        var (kept, deleted, hidden) = (first.CreatedNodes["kept"], first.CreatedNodes["deleted"], first.CreatedNodes["hidden"]);
        var relation = Assert.Single(first.Receipt.Changes, change => change.Kind == GraphEntityKind.Relation).EntityId;
        await _graph.WrittenAsync(_account, new DeleteNode(deleted, 1), new UpdateNode(hidden, 1) { HiddenFromAgents = true });

        var retry = Assert.IsType<ChangeOutcome.Applied>((await ApplyAsync(agent, proposal, "k1")).Value);

        Assert.Equal(first.Receipt.ChangeSetId, retry.Receipt.ChangeSetId);
        Assert.Equal([kept, deleted, hidden, relation], first.Receipt.Changes.Select(change => change.EntityId));
        Assert.Equal([kept, deleted], retry.Receipt.Changes.Select(change => change.EntityId));
        Assert.Equal(["deleted", "kept"], retry.CreatedNodes.Keys.Order(StringComparer.Ordinal));
        Assert.DoesNotContain(hidden, retry.CreatedNodes.Values);
    }

    // DA-043 item 7, DA-048: Purge empties an applied proposal's operations when it purges a Node
    // they named or one they created; a retry with the key still names the new Nodes that are
    // there, by the name the first answer gave them, and forgets the purged one's.
    [Fact]
    public async Task A_retry_after_purge_still_names_the_new_nodes_that_are_there()
    {
        var target = Guid.CreateVersion7();
        await _graph.WrittenAsync(_account, new CreateNode(target, "Seneca"));
        var agent = await _graph.CreateAgentIdentityAsync(_account);
        var suggested = await _graph.AsAgentAsync(_account, agent, writer => writer.SuggestAsync(
            new AgentWriteRequest(
            [
                new AgentCreateNode("Letters", Ref: "letters"),
                new AgentCreateNode("Secret note", Ref: "note"),
                new AgentCreateRelation(new NodeRef(null, "letters"), NodeRef.Of(target), "written_by"),
            ]),
            TestContext.Current.CancellationToken));
        var proposal = Assert.IsType<ChangeOutcome.PendingApproval>(suggested.Value).ProposalId;
        var first = Assert.IsType<ChangeOutcome.Applied>((await ApplyAsync(agent, proposal, "k1")).Value);
        var (letters, note) = (first.CreatedNodes["letters"], first.CreatedNodes["note"]);
        await _graph.WrittenAsync(_account, new DeleteNode(target, 1), new DeleteNode(note, 1));
        await _graph.MakePurgesDueAsync();
        await _graph.RunDueJobsAsync();

        var retry = Assert.IsType<ChangeOutcome.Applied>((await ApplyAsync(agent, proposal, "k1")).Value);

        Assert.Equal(first.Receipt.ChangeSetId, retry.Receipt.ChangeSetId);
        Assert.Equal(("letters", letters), Assert.Single(retry.CreatedNodes.Select(created => (created.Key, created.Value))));
        Assert.Equal([letters], retry.Receipt.Changes.Select(change => change.EntityId));
        Assert.Equal("[]|true|letters", await _graph.ScalarAsync<string>(
            "SELECT operations::text || '|' || (redacted_at IS NOT NULL) || '|' || (SELECT string_agg(name, ',') FROM jsonb_object_keys(created_nodes) AS name) FROM change_proposals WHERE id = @id",
            new NpgsqlParameter("id", proposal)));
    }

    // DB-004: the database refuses a reason the code does not know, spelled any other way, and a
    // negative operation index.
    [Theory]
    [InlineData("ARRAY['APPROVALREQUIRED']", "ARRAY[]::integer[]", "ck_change_proposals_reasons")]
    [InlineData("ARRAY['suggested', 'unknown']", "ARRAY[]::integer[]", "ck_change_proposals_reasons")]
    [InlineData("ARRAY['suggested']", "ARRAY[0, -1]", "ck_change_proposals_rejected_operations")]
    public async Task The_database_refuses_an_unknown_reason_and_a_negative_operation(string reasons, string rejectedOperations, string constraint)
    {
        var agent = await _graph.CreateAgentIdentityAsync(_account);

        var refused = await Assert.ThrowsAsync<PostgresException>(() => database.ExecuteAsMigratorAsync(
            $$"""
            INSERT INTO change_proposals (id, account_id, agent_identity_id, status, operations, operation_count, content_hash, requires_user,
                created_at, updated_at, expires_at, reasons, rejected_operations, target_node_ids, created_nodes)
            VALUES (@id, @account, @agent, 'proposed', '[]', 1, 'hash', false, now(), now(), now() + interval '7 days',
                {{reasons}}, {{rejectedOperations}}, ARRAY[]::uuid[], '{}')
            """,
            TestContext.Current.CancellationToken,
            new NpgsqlParameter("id", Guid.CreateVersion7()),
            new NpgsqlParameter("account", _account),
            new NpgsqlParameter("agent", agent)));

        Assert.Equal((PostgresErrorCodes.CheckViolation, constraint), (refused.SqlState, refused.ConstraintName));
    }

    private Task<Result<ChangeOutcome>> ApplyAsync(Guid agentIdentityId, Guid proposal, string idempotencyKey) =>
        _graph.AsAgentAsync(_account, agentIdentityId, writer => writer.ApplyProposalAsync(proposal, idempotencyKey, TestContext.Current.CancellationToken));

    private async Task<Guid> SuggestAsync(Guid agentIdentityId, string title)
    {
        var suggested = await _graph.AsAgentAsync(_account, agentIdentityId, writer =>
            writer.SuggestAsync(new AgentWriteRequest([new AgentCreateNode(title)]), TestContext.Current.CancellationToken));
        return Assert.IsType<ChangeOutcome.PendingApproval>(suggested.Value).ProposalId;
    }

    private Task<List<Guid>> VisibleProposalsAsync(AuthenticatedPrincipal principal) =>
        _graph.AsAsync(principal, services =>
        {
            var filter = services.GetRequiredService<GraphReadFilter>();
            var db = services.GetRequiredService<LifeGraphDbContext>();
            return db.InAccountTransactionAsync(
                token => filter.Proposals().Select(proposal => proposal.Id).ToListAsync(token),
                TestContext.Current.CancellationToken);
        });

    private Task<string> ProposalStatusAsync(Guid proposal) =>
        _graph.ScalarAsync<string>("SELECT status FROM change_proposals WHERE id = @id", new NpgsqlParameter("id", proposal));
}
