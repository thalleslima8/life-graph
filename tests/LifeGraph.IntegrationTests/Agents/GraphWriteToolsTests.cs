using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LifeGraph.Accounts.Contracts;
using LifeGraph.Accounts.Issuer;
using LifeGraph.Agents;
using LifeGraph.Agents.Mcp;
using LifeGraph.Graph.Contracts;
using LifeGraph.IntegrationTests.Graph;
using LifeGraph.IntegrationTests.Infrastructure;
using ModelContextProtocol.Client;
using Npgsql;

namespace LifeGraph.IntegrationTests.Agents;

/// <summary>
/// The E5 write tools over MCP, as connected agents call them, under the central agent write
/// policy: Write applied at once (DA-040), Sensitive, Type changes, bulk and the sliding window
/// as proposals (DA-041, DA-042, DA-046, DA-132), the protections of apply_changes (DA-043) and
/// the person's decision in Agent Changes.
/// </summary>
public sealed class GraphWriteToolsTests(PostgresDatabase database) : IAsyncLifetime
{
    private const string Email = "ada@example.test";
    private const string OtherClientId = "other-agent";
    private const string ThirdClientId = "third-agent";

    private readonly LifeGraphApiFactory _factory = new(database, SettingsWithThreeClients());
    private GraphWriteHarness _graph = null!;
    private Guid _accountId;
    private SpaClient _person = null!;
    private McpClient _agent = null!;
    private Guid _agentId;
    private Guid _root;

    public async ValueTask InitializeAsync()
    {
        await database.ResetAsync();
        _accountId = (await TestAccounts.ProvisionConfirmedAsync(_factory, Email)).AccountId;
        _graph = new GraphWriteHarness(database, _factory);
        _person = await AgentAuthorization.SignedInBrowserAsync(_factory, Email);
        _root = await _graph.AccountRootIdAsync(_accountId);
        (_agent, _agentId) = await ConnectAgentAsync(AgentAuthorization.ClientId);
    }

    public async ValueTask DisposeAsync()
    {
        await _agent.DisposeAsync();
        _person.Dispose();
        await _factory.DisposeAsync();
    }

    // DA-194: the server tells every agent how to anchor, and create_node and suggest_changes take the anchor.
    [Fact]
    public async Task The_server_instructions_tell_how_to_anchor_and_the_creating_tools_take_an_anchor()
    {
        var tools = await _agent.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(AgentsModule.ServerInstructions, _agent.ServerInstructions);
        var createNode = Assert.Single(tools, tool => tool.Name == GraphWriteTools.CreateNodeName);
        Assert.True(createNode.JsonSchema.GetProperty("properties").TryGetProperty("anchor", out _));
        Assert.Contains("anchor", Assert.Single(tools, tool => tool.Name == GraphWriteTools.SuggestChangesName).Description, StringComparison.Ordinal);
    }

    // DA-192: create_node without anchor is refused and tells the agent the root's id.
    [Fact]
    public async Task Create_node_without_anchor_is_refused_with_the_root_id()
    {
        var refused = await AgentMcp.RefusalAsync(_agent, GraphWriteTools.CreateNodeName, new { title = "Solta" });

        Assert.Contains("needs an anchor", refused, StringComparison.Ordinal);
        Assert.Contains(_root.ToString(), refused, StringComparison.Ordinal);
    }

    // DA-040, DA-049, DA-133: creations are applied at once, as the agent's own, and consume no window.
    [Fact]
    public async Task An_agent_creates_a_node_and_relates_it_applied_at_once_as_its_own_change()
    {
        var mine = await PersonsNodeAsync("Meditations");

        using var created = await CallAsync(GraphWriteTools.CreateNodeName, new { title = "Marcus Aurelius", suggestedType = "Person", anchor = new { nodeId = _root } });
        var nodeId = SingleCreated(created);
        using var related = await CallAsync(GraphWriteTools.CreateRelationName, new { sourceNodeId = mine, targetNodeId = nodeId, kind = "written_by" });

        Assert.Equal("applied", created.RootElement.GetProperty("status").GetString());
        Assert.Equal("applied", related.RootElement.GetProperty("status").GetString());
        var changeSet = created.RootElement.GetProperty("changeSetId").GetGuid();
        Assert.Equal($"agent_identity|{_agentId}|mcp|0", await _graph.ScalarAsync<string>(
            "SELECT actor_kind || '|' || agent_identity_id || '|' || channel || '|' || window_operations FROM changesets WHERE id = @id",
            new NpgsqlParameter("id", changeSet)));
        Assert.Equal($"{_agentId}|Person|true", await _graph.ScalarAsync<string>(
            "SELECT created_by_agent_identity_id || '|' || suggested_type || '|' || (inbox_entered_at IS NOT NULL) FROM nodes WHERE id = @id",
            new NpgsqlParameter("id", nodeId)));
        Assert.Equal("agent", await _graph.ScalarAsync<string>(
            "SELECT origin FROM relations WHERE source_node_id = @id", new NpgsqlParameter("id", mine)));

        // DA-049: the Inbox shows the hint to the person.
        using var inbox = await JsonAsync("/api/nodes?inInbox=true");
        var captured = Assert.Single(inbox.RootElement.GetProperty("data").EnumerateArray());
        Assert.Equal((nodeId, "Person"), (captured.GetProperty("id").GetGuid(), captured.GetProperty("suggestedType").GetString()));
    }

    // DA-040, DA-132: editing and unrelating the person's entities is applied, and consumes the window.
    [Fact]
    public async Task An_agent_edits_and_unrelates_the_persons_entities_at_once_each_counted_in_the_window()
    {
        var book = Guid.CreateVersion7();
        var author = Guid.CreateVersion7();
        var writtenBy = Guid.CreateVersion7();
        await _graph.WrittenAsync(_accountId, new CreateNode(book, "Letters"), new CreateNode(author, "Seneca"), new CreateRelation(writtenBy, book, author, "written_by"));

        using var edited = await CallAsync(GraphWriteTools.UpdateNodeName, new { nodeId = book, expectedVersion = 1, title = "Letters from a Stoic" });
        using var unrelated = await CallAsync(GraphWriteTools.RemoveRelationName, new { relationId = writtenBy });

        Assert.Equal("applied", edited.RootElement.GetProperty("status").GetString());
        Assert.Equal("applied", unrelated.RootElement.GetProperty("status").GetString());
        Assert.Equal("Letters from a Stoic", await _graph.ScalarAsync<string>("SELECT title FROM nodes WHERE id = @id", new NpgsqlParameter("id", book)));
        Assert.Equal(2L, await _graph.ScalarAsync<long>("SELECT sum(window_operations) FROM changesets WHERE agent_identity_id = @id", new NpgsqlParameter("id", _agentId)));
    }

    // DA-050: an edit carries the version the agent read; a stale one changes nothing.
    [Fact]
    public async Task An_edit_at_a_stale_version_changes_nothing()
    {
        var node = await PersonsNodeAsync("Draft");
        await _graph.WrittenAsync(_accountId, new UpdateNode(node, 1) { Title = "Edited by the person" });

        var refused = await AgentMcp.RefusalAsync(_agent, GraphWriteTools.UpdateNodeName, new { nodeId = node, expectedVersion = 1, title = "Agent's title" });

        Assert.Contains("changed after it was read", refused, StringComparison.Ordinal);
        Assert.Equal("Edited by the person", await _graph.ScalarAsync<string>("SELECT title FROM nodes WHERE id = @id", new NpgsqlParameter("id", node)));
    }

    // DA-042, DA-133: the Type of the person's Node is a proposal; of the agent's own Node, applied.
    [Fact]
    public async Task Changing_the_type_of_the_persons_node_is_proposed_and_of_its_own_node_applied()
    {
        var person = Guid.CreateVersion7();
        await _graph.WrittenAsync(_accountId, new CreateType(person, "Person"));
        var mine = await PersonsNodeAsync("Seneca");
        using var created = await CallAsync(GraphWriteTools.CreateNodeName, new { title = "Epictetus", anchor = new { nodeId = _root } });
        var own = SingleCreated(created);

        using var onMine = await CallAsync(GraphWriteTools.UpdateNodeName, new { nodeId = mine, expectedVersion = 1, typeId = person });
        using var onOwn = await CallAsync(GraphWriteTools.UpdateNodeName, new { nodeId = own, expectedVersion = 1, typeId = person });

        Assert.Equal("pending_approval", onMine.RootElement.GetProperty("status").GetString());
        Assert.True(onMine.RootElement.GetProperty("requiresUser").GetBoolean());
        Assert.Equal(["type_change"], Strings(onMine.RootElement.GetProperty("reasons")));
        Assert.Equal(GraphWriteTools.WaitsForThePersonGuidance, onMine.RootElement.GetProperty("guidance").GetString());
        Assert.Equal("applied", onOwn.RootElement.GetProperty("status").GetString());
        Assert.Equal(0L, await _graph.ScalarAsync<long>("SELECT count(*) FROM nodes WHERE id = @id AND type_id IS NOT NULL", new NpgsqlParameter("id", mine)));
    }

    // DA-042, DA-049: typing its own untyped Node leaves it in the Inbox; only the person takes it out.
    [Fact]
    public async Task Typing_its_own_inbox_node_leaves_it_in_the_inbox()
    {
        var person = Guid.CreateVersion7();
        await _graph.WrittenAsync(_accountId, new CreateType(person, "Person"));
        using var created = await CallAsync(GraphWriteTools.CreateNodeName, new { title = "Epictetus", anchor = new { nodeId = _root } });
        var own = SingleCreated(created);

        using var typed = await CallAsync(GraphWriteTools.UpdateNodeName, new { nodeId = own, expectedVersion = 1, typeId = person });

        Assert.Equal("applied", typed.RootElement.GetProperty("status").GetString());
        Assert.Equal(1L, await _graph.ScalarAsync<long>(
            "SELECT count(*) FROM nodes WHERE id = @id AND type_id = @type AND inbox_entered_at IS NOT NULL",
            new NpgsqlParameter("id", own),
            new NpgsqlParameter("type", person)));
    }

    // DA-133: once the person writes to the agent's Node it is the person's for good, Undo or not.
    [Fact]
    public async Task A_node_the_person_wrote_to_is_no_longer_the_agents_own_even_after_undo()
    {
        var person = Guid.CreateVersion7();
        await _graph.WrittenAsync(_accountId, new CreateType(person, "Person"));
        using var created = await CallAsync(GraphWriteTools.CreateNodeName, new { title = "Epictetus", anchor = new { nodeId = _root } });
        var own = SingleCreated(created);
        var edit = await _graph.WrittenAsync(_accountId, new UpdateNode(own, 1) { Body = "The person's notes" });
        Assert.True((await _graph.UndoAsync(_accountId, edit)).IsSuccess);

        using var typed = await CallAsync(GraphWriteTools.UpdateNodeName, new { nodeId = own, expectedVersion = 3, typeId = person });

        Assert.Equal("pending_approval", typed.RootElement.GetProperty("status").GetString());
        Assert.Equal(0L, await _graph.ScalarAsync<long>(
            "SELECT count(*) FROM nodes WHERE id = @id AND created_by_agent_identity_id IS NOT NULL", new NpgsqlParameter("id", own)));
    }

    // DA-035: what the agent may not see is not found for its writes too.
    [Fact]
    public async Task Hidden_nodes_and_types_are_not_found_for_agent_writes()
    {
        var hidden = await PersonsNodeAsync("Diagnosis");
        var visible = await PersonsNodeAsync("Notes");
        var health = Guid.CreateVersion7();
        await _graph.WrittenAsync(_accountId, new UpdateNode(hidden, 1) { HiddenFromAgents = true }, new CreateType(health, "Health"), new SetTypeHiddenFromAgents(health, true));

        var edit = await AgentMcp.RefusalAsync(_agent, GraphWriteTools.UpdateNodeName, new { nodeId = hidden, expectedVersion = 2, title = "x" });
        var relate = await AgentMcp.RefusalAsync(_agent, GraphWriteTools.CreateRelationName, new { sourceNodeId = visible, targetNodeId = hidden, kind = "about" });
        var typed = await AgentMcp.RefusalAsync(_agent, GraphWriteTools.CreateNodeName, new { title = "Exam", typeId = health, anchor = new { nodeId = _root } });

        Assert.StartsWith("The node was not found.", edit, StringComparison.Ordinal);
        Assert.StartsWith("The node was not found.", relate, StringComparison.Ordinal);
        Assert.StartsWith("The type was not found.", typed, StringComparison.Ordinal);
    }

    // DA-130: the same request finds the same pending proposal, which the agent is told it may apply
    // itself (no protection keeps it for the person); get_proposal tells only the status.
    [Fact]
    public async Task Suggesting_twice_finds_the_same_proposal_and_get_proposal_tells_only_its_status()
    {
        var node = await PersonsNodeAsync("Draft");
        var operations = new[] { new { op = "update_node", nodeId = node, expectedVersion = 1, title = "Better title" } };

        using var first = await CallAsync(GraphWriteTools.SuggestChangesName, new { operations });
        using var second = await CallAsync(GraphWriteTools.SuggestChangesName, new { operations });
        var proposal = ProposalOf(first);
        using var status = await CallAsync(GraphWriteTools.GetProposalName, new { proposalId = proposal });
        var (otherAgent, _) = await ConnectAgentAsync(OtherClientId);
        await using var _ = otherAgent;
        var fromOther = await AgentMcp.RefusalAsync(otherAgent, GraphWriteTools.GetProposalName, new { proposalId = proposal });

        Assert.False(first.RootElement.GetProperty("deduplicated").GetBoolean());
        Assert.False(first.RootElement.GetProperty("requiresUser").GetBoolean());
        Assert.Equal(GraphWriteTools.AgentMayApplyGuidance, first.RootElement.GetProperty("guidance").GetString());
        Assert.Equal(proposal, ProposalOf(second));
        Assert.True(second.RootElement.GetProperty("deduplicated").GetBoolean());
        Assert.Equal("proposed", status.RootElement.GetProperty("status").GetString());
        Assert.False(status.RootElement.TryGetProperty("operations", out var _));
        Assert.Equal("The proposal was not found.", fromOther);
        Assert.Equal("Draft", await _graph.ScalarAsync<string>("SELECT title FROM nodes WHERE id = @id", new NpgsqlParameter("id", node)));
    }

    // DA-130: ten pending proposals per agent; withdrawing one frees its place.
    [Fact]
    public async Task The_pending_queue_stops_the_agent_at_ten_and_a_withdrawal_frees_a_place()
    {
        var proposals = new List<Guid>();
        for (var index = 0; index < 10; index++)
        {
            using var suggested = await CallAsync(GraphWriteTools.SuggestChangesName, new { operations = new[] { new { op = "create_node", title = $"Idea {index}", anchor = new { nodeId = _root } } } });
            proposals.Add(ProposalOf(suggested));
        }

        var full = await AgentMcp.RefusalAsync(_agent, GraphWriteTools.SuggestChangesName, new { operations = new[] { new { op = "create_node", title = "Idea 10", anchor = new { nodeId = _root } } } });
        using var again = await CallAsync(GraphWriteTools.SuggestChangesName, new { operations = new[] { new { op = "create_node", title = "Idea 0", anchor = new { nodeId = _root } } } });
        using var withdrawn = await CallAsync(GraphWriteTools.WithdrawProposalName, new { proposalId = proposals[0] });
        using var fits = await CallAsync(GraphWriteTools.SuggestChangesName, new { operations = new[] { new { op = "create_node", title = "Idea 10", anchor = new { nodeId = _root } } } });

        Assert.Contains("already has 10 proposals", full, StringComparison.Ordinal);
        Assert.True(again.RootElement.GetProperty("deduplicated").GetBoolean());
        Assert.Equal("withdrawn", withdrawn.RootElement.GetProperty("status").GetString());
        Assert.Equal("pending_approval", fits.RootElement.GetProperty("status").GetString());
    }

    // DA-048: a proposal nobody decided expires after seven days, by its job.
    [Fact]
    public async Task A_proposal_expires_after_seven_days_by_its_job()
    {
        using var suggested = await CallAsync(GraphWriteTools.SuggestChangesName, new { operations = new[] { new { op = "create_node", title = "Idea", anchor = new { nodeId = _root } } } });
        var proposal = ProposalOf(suggested);

        await ExpireProposalsAsync();
        await _graph.RunDueJobsAsync();

        Assert.Equal("expired", await _graph.ScalarAsync<string>("SELECT status FROM change_proposals WHERE id = @id", new NpgsqlParameter("id", proposal)));
        using var status = await CallAsync(GraphWriteTools.GetProposalName, new { proposalId = proposal });
        Assert.Equal("expired", status.RootElement.GetProperty("status").GetString());
    }

    // Testes obrigatórios — DA-043 item 1: another agent's proposal is not found.
    [Fact]
    public async Task Applying_another_agents_proposal_is_not_found()
    {
        var proposal = await SuggestAsync(new { op = "create_node", title = "Idea", anchor = new { nodeId = _root } });
        var (otherAgent, _) = await ConnectAgentAsync(OtherClientId);
        await using var _ = otherAgent;

        var refused = await AgentMcp.RefusalAsync(otherAgent, GraphWriteTools.ApplyChangesName, new { proposalId = proposal, idempotencyKey = "k1" });

        Assert.Equal("The proposal was not found.", refused);
    }

    // DA-043 item 2: only a pending proposal that has not expired is applied.
    [Fact]
    public async Task Applying_a_withdrawn_or_expired_proposal_is_refused()
    {
        var withdrawn = await SuggestAsync(new { op = "create_node", title = "Withdrawn", anchor = new { nodeId = _root } });
        using var _ = await CallAsync(GraphWriteTools.WithdrawProposalName, new { proposalId = withdrawn });
        var expiring = await SuggestAsync(new { op = "create_node", title = "Expiring", anchor = new { nodeId = _root } });
        await ExpireProposalsAsync();

        var onWithdrawn = await AgentMcp.RefusalAsync(_agent, GraphWriteTools.ApplyChangesName, new { proposalId = withdrawn, idempotencyKey = "k1" });
        var onExpired = await AgentMcp.RefusalAsync(_agent, GraphWriteTools.ApplyChangesName, new { proposalId = expiring, idempotencyKey = "k2" });

        Assert.Contains("no longer pending", onWithdrawn, StringComparison.Ordinal);
        Assert.Contains("no longer pending", onExpired, StringComparison.Ordinal);
    }

    // DA-048: Purge empties a pending proposal that names the Node and withdraws it, so neither
    // the person nor the agent applies what is left of it.
    [Fact]
    public async Task A_pending_proposal_emptied_by_purge_cannot_be_applied_by_the_person_nor_the_agent()
    {
        var node = await PersonsNodeAsync("Secret plan");
        var proposal = await SuggestAsync(new { op = "update_node", nodeId = node, expectedVersion = 1, title = "Edited" });
        await _graph.WrittenAsync(_accountId, new DeleteNode(node, 1));
        await _graph.MakePurgesDueAsync(node);
        await _graph.RunDueJobsAsync();
        var changeSets = await _graph.CountAsync("changesets");

        var byAgent = await AgentMcp.RefusalAsync(_agent, GraphWriteTools.ApplyChangesName, new { proposalId = proposal, idempotencyKey = "k1" });
        var byPerson = await _person.PostAsync($"/api/proposals/{proposal}/approval", new { });

        Assert.Contains("no longer pending", byAgent, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, byPerson.StatusCode);
        Assert.Contains("agents.proposal_not_pending", await byPerson.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.Ordinal);
        Assert.Equal("withdrawn|[]", await _graph.ScalarAsync<string>(
            "SELECT status || '|' || operations::text FROM change_proposals WHERE id = @id", new NpgsqlParameter("id", proposal)));
        Assert.Equal(changeSets, await _graph.CountAsync("changesets"));
    }

    // DA-043 item 3: a proposal with a Sensitive operation is the person's only.
    [Fact]
    public async Task Applying_a_proposal_with_a_delete_is_for_the_person_only()
    {
        var node = await PersonsNodeAsync("Old note");
        using var suggested = await CallAsync(GraphWriteTools.SuggestChangesName, new { operations = new[] { new { op = "delete_node", nodeId = node, expectedVersion = 1 } } });

        var refused = await AgentMcp.RefusalAsync(_agent, GraphWriteTools.ApplyChangesName, new { proposalId = ProposalOf(suggested), idempotencyKey = "k1" });

        Assert.True(suggested.RootElement.GetProperty("requiresUser").GetBoolean());
        Assert.Equal(["sensitive", "suggested"], Strings(suggested.RootElement.GetProperty("reasons")).Order(StringComparer.Ordinal));
        Assert.StartsWith("Only the person can approve this proposal", refused, StringComparison.Ordinal);
        Assert.Equal(0L, await _graph.ScalarAsync<long>("SELECT count(*) FROM nodes WHERE id = @id AND deleted_at IS NOT NULL", new NpgsqlParameter("id", node)));
    }

    // Testes obrigatórios — DA-043 item 4: made with the toggle on, it stays the person's after turning it off.
    [Fact]
    public async Task A_proposal_made_with_approval_required_does_not_unlock_when_the_toggle_is_turned_off()
    {
        Assert.Equal(HttpStatusCode.OK, (await SetRequiresApprovalAsync(true)).StatusCode);
        using var written = await CallAsync(GraphWriteTools.CreateNodeName, new { title = "Idea", anchor = new { nodeId = _root } });
        var proposal = ProposalOf(written);
        Assert.Equal(["approval_required"], Strings(written.RootElement.GetProperty("reasons")));

        Assert.Equal(HttpStatusCode.OK, (await SetRequiresApprovalAsync(false)).StatusCode);
        var refused = await AgentMcp.RefusalAsync(_agent, GraphWriteTools.ApplyChangesName, new { proposalId = proposal, idempotencyKey = "k1" });

        Assert.StartsWith("Only the person can approve this proposal", refused, StringComparison.Ordinal);
        Assert.Equal(0L, await _graph.CountAsync("nodes"));
        Assert.True(await RequiresUserAsync(proposal));
    }

    // DA-043 item 4: a suggestion the agent tries to apply while the toggle is on is marked
    // requires_user, and the mark is stored although the attempt is refused; turning the toggle
    // off later does not unlock it.
    [Fact]
    public async Task A_suggestion_tried_with_approval_required_stays_the_persons_after_the_toggle_is_turned_off()
    {
        var proposal = await SuggestAsync(new { op = "create_node", @ref = "idea", title = "Idea", anchor = new { nodeId = _root } });
        Assert.False(await RequiresUserAsync(proposal));

        Assert.Equal(HttpStatusCode.OK, (await SetRequiresApprovalAsync(true)).StatusCode);
        var refusedOn = await AgentMcp.RefusalAsync(_agent, GraphWriteTools.ApplyChangesName, new { proposalId = proposal, idempotencyKey = "k1" });
        Assert.StartsWith("Only the person can approve this proposal", refusedOn, StringComparison.Ordinal);
        Assert.True(await RequiresUserAsync(proposal));

        Assert.Equal(HttpStatusCode.OK, (await SetRequiresApprovalAsync(false)).StatusCode);
        var refusedOff = await AgentMcp.RefusalAsync(_agent, GraphWriteTools.ApplyChangesName, new { proposalId = proposal, idempotencyKey = "k2" });

        Assert.StartsWith("Only the person can approve this proposal", refusedOff, StringComparison.Ordinal);
        Assert.True(await RequiresUserAsync(proposal));
        Assert.Equal(0L, await _graph.CountAsync("nodes"));
    }

    // DA-043 item 5: the base version is checked again when applying.
    [Fact]
    public async Task Applying_after_the_node_changed_is_a_conflict()
    {
        var node = await PersonsNodeAsync("Draft");
        var proposal = await SuggestAsync(new { op = "update_node", nodeId = node, expectedVersion = 1, title = "Agent's title" });
        await _graph.WrittenAsync(_accountId, new UpdateNode(node, 1) { Title = "Person's title" });

        var refused = await AgentMcp.RefusalAsync(_agent, GraphWriteTools.ApplyChangesName, new { proposalId = proposal, idempotencyKey = "k1" });

        Assert.Contains("changed after it was read", refused, StringComparison.Ordinal);
        Assert.Equal("Person's title", await _graph.ScalarAsync<string>("SELECT title FROM nodes WHERE id = @id", new NpgsqlParameter("id", node)));
    }

    // DA-043 item 6: the hidden filter runs again: a target hidden since is not found.
    [Fact]
    public async Task Applying_after_the_target_was_hidden_is_not_found()
    {
        var node = await PersonsNodeAsync("Draft");
        var other = await PersonsNodeAsync("Other");
        var proposal = await SuggestAsync(new { op = "create_relation", sourceNodeId = node, targetNodeId = other, kind = "about" });
        await _graph.WrittenAsync(_accountId, new UpdateNode(other, 1) { HiddenFromAgents = true });

        var refused = await AgentMcp.RefusalAsync(_agent, GraphWriteTools.ApplyChangesName, new { proposalId = proposal, idempotencyKey = "k1" });

        Assert.StartsWith("The node was not found.", refused, StringComparison.Ordinal);
        Assert.Equal(0L, await _graph.ScalarAsync<long>("SELECT count(*) FROM relations"));
    }

    // DA-043 item 7: the same key answers the same, the new Nodes and their versions included,
    // and nothing is applied twice.
    [Fact]
    public async Task Applying_again_with_the_same_key_answers_the_same_and_applies_once()
    {
        var mine = await PersonsNodeAsync("Mine");
        using var suggested = await CallAsync(GraphWriteTools.SuggestChangesName, new
        {
            operations = new object[]
            {
                new { op = "create_node", title = "Idea", anchor = new { nodeId = _root } },
                new { op = "create_node", @ref = "seneca", title = "Seneca", anchor = new { nodeId = _root } },
                new { op = "create_relation", sourceNodeId = mine, targetRef = "seneca", kind = "about" },
            },
        });
        var proposal = ProposalOf(suggested);

        using var first = await CallAsync(GraphWriteTools.ApplyChangesName, new { proposalId = proposal, idempotencyKey = "k1" });
        using var retry = await CallAsync(GraphWriteTools.ApplyChangesName, new { proposalId = proposal, idempotencyKey = "k1" });
        var otherKey = await AgentMcp.RefusalAsync(_agent, GraphWriteTools.ApplyChangesName, new { proposalId = proposal, idempotencyKey = "k2" });

        Assert.Equal(GraphWriteTools.AppliedStatus, first.RootElement.GetProperty("status").GetString());
        Assert.Equal(first.RootElement.GetRawText(), retry.RootElement.GetRawText());
        Assert.Equal(["operations[0]", "seneca"], first.RootElement.GetProperty("createdNodes").EnumerateObject().Select(created => created.Name).Order(StringComparer.Ordinal));
        Assert.Contains("no longer pending", otherKey, StringComparison.Ordinal);
        Assert.Equal(3L, await _graph.CountAsync("nodes"));
        using var status = await CallAsync(GraphWriteTools.GetProposalName, new { proposalId = proposal });
        Assert.Equal("applied", status.RootElement.GetProperty("status").GetString());
    }

    // DA-043 item 7: applied with nothing to change, a retry with the key still answers "applied".
    [Fact]
    public async Task A_retry_of_a_proposal_applied_with_no_change_answers_the_same()
    {
        var node = await PersonsNodeAsync("Same title");
        var proposal = await SuggestAsync(new { op = "update_node", nodeId = node, expectedVersion = 1, title = "Same title" });

        using var first = await CallAsync(GraphWriteTools.ApplyChangesName, new { proposalId = proposal, idempotencyKey = "k1" });
        using var retry = await CallAsync(GraphWriteTools.ApplyChangesName, new { proposalId = proposal, idempotencyKey = "k1" });

        Assert.Equal(GraphWriteTools.AppliedStatus, first.RootElement.GetProperty("status").GetString());
        Assert.False(first.RootElement.TryGetProperty("changeSetId", out _));
        Assert.Equal(first.RootElement.GetRawText(), retry.RootElement.GetRawText());
        Assert.Equal("applied", await _graph.ScalarAsync<string>("SELECT status FROM change_proposals WHERE id = @id", new NpgsqlParameter("id", proposal)));
    }

    // DA-043 item 7, DA-130: an application that turned out to need the person answered
    // pending; a retry with the key answers pending again, never a tool error, even after the
    // person decided. Another key gets the refusal.
    [Fact]
    public async Task A_retry_of_an_application_left_to_the_person_answers_pending_again()
    {
        var nodes = await PersonsNodesAsync(21);
        var proposal = await SuggestAsync(new { op = "update_node", nodeId = nodes[20], expectedVersion = 1, title = "Over the window" });
        await SpendWindowAsync(_agent, nodes[..20]);

        using var first = await CallAsync(GraphWriteTools.ApplyChangesName, new { proposalId = proposal, idempotencyKey = "k1" });
        using var retry = await CallAsync(GraphWriteTools.ApplyChangesName, new { proposalId = proposal, idempotencyKey = "k1" });
        Assert.Equal(HttpStatusCode.OK, (await _person.PostAsync($"/api/proposals/{proposal}/approval", new { })).StatusCode);
        using var afterApproval = await CallAsync(GraphWriteTools.ApplyChangesName, new { proposalId = proposal, idempotencyKey = "k1" });
        var otherKey = await AgentMcp.RefusalAsync(_agent, GraphWriteTools.ApplyChangesName, new { proposalId = proposal, idempotencyKey = "k2" });

        Assert.Equal(GraphWriteTools.PendingApprovalStatus, first.RootElement.GetProperty("status").GetString());
        Assert.Equal(proposal, ProposalOf(first));
        Assert.Contains("window", Strings(first.RootElement.GetProperty("reasons")));
        Assert.Equal(first.RootElement.GetRawText(), retry.RootElement.GetRawText());
        Assert.Equal(first.RootElement.GetRawText(), afterApproval.RootElement.GetRawText());
        Assert.Contains("no longer pending", otherKey, StringComparison.Ordinal);
        Assert.Equal("Over the window", await _graph.ScalarAsync<string>("SELECT title FROM nodes WHERE id = @id", new NpgsqlParameter("id", nodes[20])));
    }

    // DA-043 item 8: content the person rejected cannot be sent again.
    [Fact]
    public async Task Content_the_person_rejected_cannot_be_sent_again()
    {
        var proposal = await SuggestAsync(new { op = "create_node", title = "Spam", anchor = new { nodeId = _root } });
        Assert.Equal(HttpStatusCode.OK, (await _person.PostAsync($"/api/proposals/{proposal}/rejection", new { })).StatusCode);

        var again = await AgentMcp.RefusalAsync(_agent, GraphWriteTools.SuggestChangesName, new { operations = new[] { new { op = "create_node", title = "Spam", anchor = new { nodeId = _root } } } });
        var applied = await AgentMcp.RefusalAsync(_agent, GraphWriteTools.ApplyChangesName, new { proposalId = proposal, idempotencyKey = "k1" });

        Assert.Contains("already rejected these exact changes", again, StringComparison.Ordinal);
        Assert.Contains("no longer pending", applied, StringComparison.Ordinal);
    }

    // Testes obrigatórios — DA-045: 50 operations are a proposal, 51 are refused as invalid.
    [Fact]
    public async Task Fifty_operations_are_accepted_and_fifty_one_refused()
    {
        object Create(int index) => new { op = "create_node", title = $"Node {index}", anchor = new { nodeId = _root } };

        using var fifty = await CallAsync(GraphWriteTools.SuggestChangesName, new { operations = Enumerable.Range(0, 50).Select(Create).ToArray() });
        var fiftyOne = await AgentMcp.RefusalAsync(_agent, GraphWriteTools.SuggestChangesName, new { operations = Enumerable.Range(0, 51).Select(Create).ToArray() });

        Assert.Equal("pending_approval", fifty.RootElement.GetProperty("status").GetString());
        Assert.Contains("operations: Send between 1 and 50 operations.", fiftyOne, StringComparison.Ordinal);
    }

    // A refused end of a relation is keyed by a field the agent sends (sourceRef, targetNodeId...),
    // never by one the tool does not have.
    [Fact]
    public async Task A_refused_relation_end_is_keyed_by_the_fields_the_agent_sends()
    {
        var node = await PersonsNodeAsync("Draft");
        var operations = new object[]
        {
            new { op = "create_node", title = "Letters", @ref = "letters", anchor = new { nodeId = _root } },
            new { op = "create_relation", sourceRef = "unknown", targetNodeId = node, kind = "about" },
            new { op = "create_relation", sourceRef = "letters", targetNodeId = node, targetRef = "letters", kind = "about" },
        };

        var refused = await AgentMcp.RefusalAsync(_agent, GraphWriteTools.SuggestChangesName, new { operations });

        Assert.Contains("operations[1].sourceRef: No earlier operation creates a node with this ref.", refused, StringComparison.Ordinal);
        Assert.Contains("operations[2].targetNodeId: Name the node by its id or by the ref", refused, StringComparison.Ordinal);
        Assert.Contains("operations[2].targetRef: Name the node by its id or by the ref", refused, StringComparison.Ordinal);
        Assert.DoesNotContain("].source:", refused, StringComparison.Ordinal);
        Assert.DoesNotContain("].target:", refused, StringComparison.Ordinal);
    }

    // GEN-060: a proposal holds only what a direct write would accept; the field checks come
    // before the policy, so an oversized suggestion is invalid, never pending.
    [Fact]
    public async Task An_oversized_suggestion_is_refused_as_invalid_and_stores_no_proposal()
    {
        var node = await PersonsNodeAsync("Draft");
        var operations = new object[]
        {
            new { op = "create_node", title = new string('t', 501), @ref = "a", anchor = new { nodeId = _root } },
            new { op = "update_node", nodeId = node, expectedVersion = 1, body = new string('b', 100_001) },
            new { op = "create_relation", sourceNodeId = node, targetNodeId = node, kind = new string('k', 101) },
            new { op = "create_node", title = "Hint", suggestedType = new string('s', 101), anchor = new { nodeId = _root } },
        };

        var refused = await AgentMcp.RefusalAsync(_agent, GraphWriteTools.SuggestChangesName, new { operations });

        Assert.Contains("operations[0].title: Must be at most 500 characters.", refused, StringComparison.Ordinal);
        Assert.Contains("operations[1].body: Must be at most 100000 characters.", refused, StringComparison.Ordinal);
        Assert.Contains("operations[2].kind: Must be at most 100 characters.", refused, StringComparison.Ordinal);
        Assert.Contains("operations[2].targetNodeId: A Node cannot be related to itself.", refused, StringComparison.Ordinal);
        Assert.Contains("operations[3].suggestedType: Must be at most 100 characters.", refused, StringComparison.Ordinal);
        Assert.Equal(0L, await _graph.ScalarAsync<long>("SELECT count(*) FROM change_proposals"));
    }

    // GEN-060: the same for a write the policy would turn into a proposal (the toggle on), an
    // edit at a stale version included: it is checked against the Node as it is now.
    [Fact]
    public async Task An_oversized_write_that_would_become_a_proposal_is_refused_as_invalid()
    {
        var node = await PersonsNodeAsync("Draft");
        await _graph.WrittenAsync(_accountId, new UpdateNode(node, 1) { Title = "Edited by the person" });
        Assert.Equal(HttpStatusCode.OK, (await SetRequiresApprovalAsync(true)).StatusCode);

        var created = await AgentMcp.RefusalAsync(_agent, GraphWriteTools.CreateNodeName, new { title = new string('t', 501), anchor = new { nodeId = _root } });
        var edited = await AgentMcp.RefusalAsync(_agent, GraphWriteTools.UpdateNodeName, new { nodeId = node, expectedVersion = 1, title = new string('t', 501) });
        using var valid = await CallAsync(GraphWriteTools.CreateNodeName, new { title = "Idea", anchor = new { nodeId = _root } });

        Assert.Contains("title: Must be at most 500 characters.", created, StringComparison.Ordinal);
        Assert.Contains("title: Must be at most 500 characters.", edited, StringComparison.Ordinal);
        Assert.Equal("pending_approval", valid.RootElement.GetProperty("status").GetString());
        Assert.Equal(1L, await _graph.ScalarAsync<long>("SELECT count(*) FROM change_proposals"));
    }

    // Testes obrigatórios — DA-046: 20 changes to the person's entities are the agent's to apply, 21 the person's.
    [Fact]
    public async Task Twenty_changes_to_the_persons_entities_are_applied_and_twenty_one_go_to_the_person()
    {
        var nodes = await PersonsNodesAsync(21);
        var own = Enumerable.Range(0, 5).Select(index => new { op = "create_node", title = $"Own {index}", anchor = new { nodeId = _root } });

        using var twenty = await CallAsync(GraphWriteTools.SuggestChangesName, new { operations = Updates(nodes.Take(20)).Concat(own).ToArray() });
        using var applied = await CallAsync(GraphWriteTools.ApplyChangesName, new { proposalId = ProposalOf(twenty), idempotencyKey = "k1" });
        using var twentyOne = await CallAsync(GraphWriteTools.SuggestChangesName, new { operations = Updates(nodes, version: 1).ToArray() });

        Assert.False(twenty.RootElement.GetProperty("requiresUser").GetBoolean());
        Assert.Equal("applied", applied.RootElement.GetProperty("status").GetString());
        Assert.True(twentyOne.RootElement.GetProperty("requiresUser").GetBoolean());
        Assert.Contains("bulk", Strings(twentyOne.RootElement.GetProperty("reasons")));
    }

    // Testes obrigatórios — DA-047, DA-132: the window at its limit, sliding, per agent.
    [Fact]
    public async Task The_window_turns_the_twenty_first_change_into_a_proposal_per_agent_until_it_slides()
    {
        var nodes = await PersonsNodesAsync(23);
        using var twenty = await CallAsync(GraphWriteTools.SuggestChangesName, new { operations = Updates(nodes.Take(20)).ToArray() });
        using var applied = await CallAsync(GraphWriteTools.ApplyChangesName, new { proposalId = ProposalOf(twenty), idempotencyKey = "k1" });
        Assert.Equal("applied", applied.RootElement.GetProperty("status").GetString());

        using var twentyFirst = await CallAsync(GraphWriteTools.UpdateNodeName, new { nodeId = nodes[20], expectedVersion = 1, title = "Over the window" });
        var (otherAgent, _) = await ConnectAgentAsync(OtherClientId);
        await using var _ = otherAgent;
        using var byOther = await AgentMcp.CallAsync(otherAgent, GraphWriteTools.UpdateNodeName, new { nodeId = nodes[21], expectedVersion = 1, title = "Other agent" });
        // Ten minutes later, as far as the window sees (the test clock would expire the token).
        await database.ExecuteAsMigratorAsync(
            "UPDATE changesets SET created_at = created_at - interval '10 minutes 1 second' WHERE window_operations > 0",
            TestContext.Current.CancellationToken);
        using var afterSliding = await CallAsync(GraphWriteTools.UpdateNodeName, new { nodeId = nodes[22], expectedVersion = 1, title = "After the window" });

        Assert.Equal("pending_approval", twentyFirst.RootElement.GetProperty("status").GetString());
        Assert.Equal(["window"], Strings(twentyFirst.RootElement.GetProperty("reasons")));
        Assert.Equal("applied", byOther.RootElement.GetProperty("status").GetString());
        Assert.Equal("applied", afterSliding.RootElement.GetProperty("status").GetString());
    }

    // Testes obrigatórios — DA-132: the Account's ceiling (40) sums every agent's changes in the
    // window, so a third agent's first change to the person's entities becomes a proposal once
    // two others spent 20 each, while its own creations still apply.
    [Fact]
    public async Task The_account_ceiling_sums_the_changes_of_every_agent_in_the_window()
    {
        var nodes = await PersonsNodesAsync(41);
        var (otherAgent, _) = await ConnectAgentAsync(OtherClientId);
        await using var _ = otherAgent;
        var (thirdAgent, _) = await ConnectAgentAsync(ThirdClientId);
        await using var __ = thirdAgent;
        await SpendWindowAsync(_agent, nodes[..20]);
        await SpendWindowAsync(otherAgent, nodes[20..40]);

        using var overCeiling = await AgentMcp.CallAsync(thirdAgent, GraphWriteTools.UpdateNodeName, new { nodeId = nodes[40], expectedVersion = 1, title = "Over the ceiling" });
        using var created = await AgentMcp.CallAsync(thirdAgent, GraphWriteTools.CreateNodeName, new { title = "Its own", anchor = new { nodeId = _root } });

        Assert.Equal("pending_approval", overCeiling.RootElement.GetProperty("status").GetString());
        Assert.Equal(["window"], Strings(overCeiling.RootElement.GetProperty("reasons")));
        Assert.Equal("applied", created.RootElement.GetProperty("status").GetString());
        Assert.Equal(40L, await _graph.ScalarAsync<long>("SELECT sum(window_operations) FROM changesets"));
    }

    // The person approves part of a proposal: applied as the agent's change, the rest rejected,
    // consuming no window (DA-132); the GraphChangeSet screen warns where it overwrote the person.
    [Fact]
    public async Task The_person_approves_part_of_a_proposal_and_sees_where_it_overwrote_their_value()
    {
        var node = await PersonsNodeAsync("Person's title");
        using var suggested = await CallAsync(GraphWriteTools.SuggestChangesName, new
        {
            operations = new object[]
            {
                new { op = "update_node", nodeId = node, expectedVersion = 1, title = "Agent's title" },
                new { op = "delete_node", nodeId = node, expectedVersion = 1 },
            },
        });
        var proposal = ProposalOf(suggested);

        using var detail = await JsonAsync($"/api/proposals/{proposal}");
        var approved = await _person.PostAsync($"/api/proposals/{proposal}/approval", new { operations = new[] { 0 } });

        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
        var preview = detail.RootElement.GetProperty("operations")[0].GetProperty("fields")[0];
        Assert.Equal(("title", true), (preview.GetProperty("field").GetString(), preview.GetProperty("overwritesYourValue").GetBoolean()));
        Assert.True(detail.RootElement.GetProperty("operations")[1].GetProperty("isSensitive").GetBoolean());

        var changeSet = await ChangeSetIdAsync(approved);
        using var screen = await JsonAsync($"/api/changesets/{changeSet}");
        var field = screen.RootElement.GetProperty("entries")[0].GetProperty("fields").EnumerateArray().Single(entry => entry.GetProperty("field").GetString() == "title");
        Assert.True(field.GetProperty("overwritesYourValue").GetBoolean());
        Assert.Equal(proposal, screen.RootElement.GetProperty("changeSet").GetProperty("proposalId").GetGuid());
        Assert.Equal($"applied|{{1}}|0", await _graph.ScalarAsync<string>(
            "SELECT p.status || '|' || p.rejected_operations::text || '|' || c.window_operations FROM change_proposals p JOIN changesets c ON c.id = p.changeset_id WHERE p.id = @id",
            new NpgsqlParameter("id", proposal)));
        Assert.Equal(0L, await _graph.ScalarAsync<long>("SELECT count(*) FROM nodes WHERE deleted_at IS NOT NULL"));
    }

    // DA-135: keeping a relation without the new node it needs is refused, naming it, and applies nothing.
    // "Sobrescreveu um valor seu" asks who last set each field before the change, however many
    // entries ago, and never counts a later edit: the person's creation is still the setter of
    // a title the agent's first edit left alone; the agent's own value is not the person's.
    [Fact]
    public async Task The_overwrite_warning_names_the_last_setter_of_each_field_before_the_change()
    {
        var node = Guid.CreateVersion7();
        await _graph.WrittenAsync(_accountId, new CreateNode(node, "Person's title", Body: "Person's body"));
        using var first = await CallAsync(GraphWriteTools.UpdateNodeName, new { nodeId = node, expectedVersion = 1, body = "Agent's body" });
        using var second = await CallAsync(GraphWriteTools.UpdateNodeName, new { nodeId = node, expectedVersion = 2, title = "Agent's title", body = "Agent's second body" });
        await _graph.WrittenAsync(_accountId, new UpdateNode(node, 3) { Title = "Person's new title" });
        var proposal = await SuggestAsync(new { op = "update_node", nodeId = node, expectedVersion = 4, title = "Proposed title", body = "Proposed body" });

        Assert.Equal(new Dictionary<string, bool> { ["body"] = true }, await WarningsOfChangeSetAsync(first));
        Assert.Equal(new Dictionary<string, bool> { ["body"] = false, ["title"] = true }, await WarningsOfChangeSetAsync(second));
        using var detail = await JsonAsync($"/api/proposals/{proposal}");
        Assert.Equal(
            new Dictionary<string, bool> { ["body"] = false, ["title"] = true },
            Warnings(detail.RootElement.GetProperty("operations")[0].GetProperty("fields")));
    }

    [Fact]
    public async Task Approving_a_relation_without_its_new_node_is_refused_and_applies_nothing()
    {
        var node = await PersonsNodeAsync("Existing");
        using var suggested = await CallAsync(GraphWriteTools.SuggestChangesName, new
        {
            operations = new object[]
            {
                new { op = "create_node", title = "New", @ref = "n", anchor = new { nodeId = _root } },
                new { op = "create_relation", sourceRef = "n", targetNodeId = node, kind = "about" },
                new { op = "delete_node", nodeId = node, expectedVersion = 1 },
            },
        });
        var proposal = ProposalOf(suggested);

        using var detail = await JsonAsync($"/api/proposals/{proposal}");
        var refused = await _person.PostAsync($"/api/proposals/{proposal}/approval", new { operations = new[] { 1 } });

        Assert.Equal([0, 1, 0], detail.RootElement.GetProperty("operations").EnumerateArray().Select(operation => operation.GetProperty("dependsOn").GetArrayLength()));
        Assert.Equal(0, detail.RootElement.GetProperty("operations")[1].GetProperty("dependsOn")[0].GetInt32());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, refused.StatusCode);
        using var problem = JsonDocument.Parse(await refused.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal("agents.operation_dependency", problem.RootElement.GetProperty("code").GetString());
        Assert.True(problem.RootElement.GetProperty("errors").TryGetProperty("operations[1]", out _));
        Assert.Equal("proposed|1", await _graph.ScalarAsync<string>(
            "SELECT p.status || '|' || (SELECT count(*) FROM nodes WHERE system_key IS NULL) FROM change_proposals p WHERE p.id = @id", new NpgsqlParameter("id", proposal)));
    }

    // DA-135: approving is reviewing; operations left out do not count as rejected content (DA-043 item 8).
    [Fact]
    public async Task An_approved_change_is_born_reviewed_and_what_was_left_out_can_be_proposed_again()
    {
        var node = await PersonsNodeAsync("Keep");
        object[] operations =
        [
            new { op = "create_node", title = "Wanted", anchor = new { nodeId = _root } },
            new { op = "delete_node", nodeId = node, expectedVersion = 1 },
        ];
        using var first = await CallAsync(GraphWriteTools.SuggestChangesName, new { operations });
        var approved = await _person.PostAsync($"/api/proposals/{ProposalOf(first)}/approval", new { operations = new[] { 0 } });
        var changeSet = await ChangeSetIdAsync(approved);

        using var again = await CallAsync(GraphWriteTools.SuggestChangesName, new { operations });

        Assert.Equal("pending_approval", again.RootElement.GetProperty("status").GetString());
        Assert.NotEqual(ProposalOf(first), ProposalOf(again));
        Assert.Empty(await ListedChangeSetsAsync("/api/changesets?unreviewed=true"));
        Assert.True(await _graph.ScalarAsync<bool>("SELECT reviewed_at IS NOT NULL FROM changesets WHERE id = @id", new NpgsqlParameter("id", changeSet)));
    }

    // DA-135: a version conflict names the operation by its place in the proposal, not in the part approved.
    [Fact]
    public async Task A_conflict_in_a_partial_approval_names_the_operation_of_the_proposal()
    {
        var node = await PersonsNodeAsync("Draft");
        using var suggested = await CallAsync(GraphWriteTools.SuggestChangesName, new
        {
            operations = new object[]
            {
                new { op = "create_node", title = "Unrelated", anchor = new { nodeId = _root } },
                new { op = "delete_node", nodeId = node, expectedVersion = 1 },
            },
        });
        await _graph.WrittenAsync(_accountId, new UpdateNode(node, 1) { Title = "Edited by the person" });

        var refused = await _person.PostAsync($"/api/proposals/{ProposalOf(suggested)}/approval", new { operations = new[] { 1 } });

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        using var problem = JsonDocument.Parse(await refused.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal("graph.node_version_conflict", problem.RootElement.GetProperty("code").GetString());
        Assert.Contains(problem.RootElement.GetProperty("details").EnumerateObject(), detail => detail.Name.StartsWith("operations[1]", StringComparison.Ordinal));
    }

    // DA-135: the screens say who the agent is, whether Undo is still possible, and when Purge emptied a proposal.
    [Fact]
    public async Task The_screens_name_the_agent_tell_the_undo_state_and_flag_purged_content()
    {
        using var created = await CallAsync(GraphWriteTools.CreateNodeName, new { title = "Agent's", anchor = new { nodeId = _root } });
        var changeSet = created.RootElement.GetProperty("changeSetId").GetGuid();
        using var second = await CallAsync(GraphWriteTools.CreateNodeName, new { title = "Agent's second", anchor = new { nodeId = _root } });
        var secondChangeSet = second.RootElement.GetProperty("changeSetId").GetGuid();
        var proposal = await SuggestAsync(new { op = "create_node", title = "Later", anchor = new { nodeId = _root } });

        using var fresh = await JsonAsync($"/api/changesets/{changeSet}");
        var undo = await _person.PostAsync($"/api/changesets/{changeSet}/undo", new { });
        var undoChangeSet = await ChangeSetIdAsync(undo);
        using var undone = await JsonAsync($"/api/changesets/{changeSet}");
        using var byPerson = await JsonAsync($"/api/changesets/{undoChangeSet}");
        await database.ExecuteAsMigratorAsync(
            $"UPDATE change_entries SET before = NULL, after = NULL, purged_at = now() WHERE changeset_id = '{secondChangeSet}'",
            TestContext.Current.CancellationToken);
        using var purged = await JsonAsync($"/api/changesets/{secondChangeSet}");
        await database.ExecuteAsMigratorAsync(
            $"UPDATE change_proposals SET operations = '[]', redacted_at = now() WHERE id = '{proposal}'",
            TestContext.Current.CancellationToken);
        using var redacted = await JsonAsync($"/api/proposals/{proposal}");

        var agent = fresh.RootElement.GetProperty("agent");
        Assert.Equal((_agentId, AgentAuthorization.ClientName, false), (agent.GetProperty("id").GetGuid(), agent.GetProperty("name").GetString(), agent.GetProperty("revoked").GetBoolean()));
        Assert.Equal("available", fresh.RootElement.GetProperty("undo").GetProperty("state").GetString());
        Assert.Equal(("undone", undoChangeSet), (undone.RootElement.GetProperty("undo").GetProperty("state").GetString(), undone.RootElement.GetProperty("undo").GetProperty("byChangeSetId").GetGuid()));
        Assert.Equal(JsonValueKind.Null, byPerson.RootElement.GetProperty("agent").ValueKind);
        Assert.Equal(("unavailable", "content_purged"), (purged.RootElement.GetProperty("undo").GetProperty("state").GetString(), purged.RootElement.GetProperty("undo").GetProperty("reason").GetString()));
        Assert.True(redacted.RootElement.GetProperty("contentPurged").GetBoolean());
        Assert.Equal(_agentId, redacted.RootElement.GetProperty("agent").GetProperty("id").GetGuid());
    }

    // DA-135: the lists of Agent Changes name the agent and tell the undo state themselves, so
    // the cards never wait on the list of connections.
    [Fact]
    public async Task The_lists_name_the_agent_and_tell_the_undo_state_of_each_change()
    {
        using var created = await CallAsync(GraphWriteTools.CreateNodeName, new { title = "Agent's", anchor = new { nodeId = _root } });
        using var purgedLater = await CallAsync(GraphWriteTools.CreateNodeName, new { title = "Agent's second", anchor = new { nodeId = _root } });
        await SuggestAsync(new { op = "create_node", title = "Later", anchor = new { nodeId = _root } });
        await database.ExecuteAsMigratorAsync(
            $"UPDATE change_entries SET before = NULL, after = NULL, purged_at = now() WHERE changeset_id = '{purgedLater.RootElement.GetProperty("changeSetId").GetGuid()}'",
            TestContext.Current.CancellationToken);

        using var feed = await JsonAsync("/api/changesets?agentsOnly=true");
        using var proposals = await JsonAsync("/api/proposals");

        var items = feed.RootElement.GetProperty("data").EnumerateArray().ToDictionary(item => item.GetProperty("id").GetGuid());
        var applied = items[created.RootElement.GetProperty("changeSetId").GetGuid()];
        Assert.Equal((AgentAuthorization.ClientName, "available"), (applied.GetProperty("agent").GetProperty("name").GetString(), applied.GetProperty("undo").GetProperty("state").GetString()));
        var purged = items[purgedLater.RootElement.GetProperty("changeSetId").GetGuid()];
        Assert.Equal("content_purged", purged.GetProperty("undo").GetProperty("reason").GetString());
        var proposal = Assert.Single(proposals.RootElement.GetProperty("data").EnumerateArray());
        Assert.Equal((_agentId, AgentAuthorization.ClientName), (proposal.GetProperty("agent").GetProperty("id").GetGuid(), proposal.GetProperty("agent").GetProperty("name").GetString()));
    }

    // DA-135: revoking the connection withdraws its pending proposals, and only its own.
    [Fact]
    public async Task Revoking_an_agent_withdraws_its_pending_proposals_in_the_same_transaction()
    {
        var mine = await SuggestAsync(new { op = "create_node", title = "Pending", anchor = new { nodeId = _root } });
        var (other, _) = await ConnectAgentAsync(OtherClientId);
        await using var otherAgent = other;
        using var othersSuggestion = await AgentMcp.CallAsync(otherAgent, GraphWriteTools.SuggestChangesName, new { operations = new[] { new { op = "create_node", title = "Other's", anchor = new { nodeId = _root } } } });

        var revoked = await _person.DeleteAsync($"/api/agent-identities/{_agentId}");
        using var detail = await JsonAsync($"/api/proposals/{mine}");

        Assert.Equal(HttpStatusCode.NoContent, revoked.StatusCode);
        Assert.Equal("withdrawn", await _graph.ScalarAsync<string>("SELECT status FROM change_proposals WHERE id = @id", new NpgsqlParameter("id", mine)));
        Assert.Equal("proposed", await _graph.ScalarAsync<string>("SELECT status FROM change_proposals WHERE id = @id", new NpgsqlParameter("id", ProposalOf(othersSuggestion))));
        Assert.True(detail.RootElement.GetProperty("agent").GetProperty("revoked").GetBoolean());
    }

    // DA-134: proposing to delete a hidden, another Account's or an unknown Node answers NotFound alike.
    [Fact]
    public async Task Proposing_to_delete_a_node_the_agent_cannot_see_is_not_found_alike()
    {
        var hidden = await PersonsNodeAsync("Hidden");
        await _graph.WrittenAsync(_accountId, new UpdateNode(hidden, 1) { HiddenFromAgents = true });
        var stranger = await _graph.CreateAccountAsync();
        var strangers = Guid.CreateVersion7();
        await _graph.WrittenAsync(stranger, new CreateNode(strangers, "Not yours"));

        var answers = new List<string>();
        foreach (var target in new[] { hidden, strangers, Guid.CreateVersion7() })
        {
            answers.Add(await AgentMcp.RefusalAsync(_agent, GraphWriteTools.SuggestChangesName, new { operations = new[] { new { op = "delete_node", nodeId = target, expectedVersion = 2 } } }));
        }

        Assert.All(answers, answer => Assert.StartsWith("The node was not found.", answer, StringComparison.Ordinal));
        Assert.Single(answers.Distinct());
        Assert.Equal(0L, await _graph.ScalarAsync<long>("SELECT count(*) FROM change_proposals"));
    }

    // Agent Changes: the agents' changes by agent, "não revisado" until the person marks them.
    [Fact]
    public async Task Agent_changes_lists_the_unreviewed_changes_of_an_agent_until_marked()
    {
        await PersonsNodeAsync("Mine");
        using var created = await CallAsync(GraphWriteTools.CreateNodeName, new { title = "Agent's", anchor = new { nodeId = _root } });
        var changeSet = created.RootElement.GetProperty("changeSetId").GetGuid();

        var before = await ListedChangeSetsAsync($"/api/changesets?agentIdentityId={_agentId}&unreviewed=true");
        Assert.Equal(HttpStatusCode.NoContent, (await _person.PostAsync($"/api/changesets/{changeSet}/review", new { })).StatusCode);
        var after = await ListedChangeSetsAsync("/api/changesets?unreviewed=true");
        var agentsOnly = await ListedChangeSetsAsync("/api/changesets?agentsOnly=true");

        Assert.Equal([changeSet], before);
        Assert.Empty(after);
        Assert.Equal([changeSet], agentsOnly);
    }

    // Testes obrigatórios — DA-041: an agent's token never approves; only the person's session does.
    [Fact]
    public async Task An_agent_token_calling_the_approval_of_a_sensitive_proposal_is_refused()
    {
        var node = await PersonsNodeAsync("Keep me");
        var proposal = await SuggestAsync(new { op = "delete_node", nodeId = node, expectedVersion = 1 });
        using var browser = await AgentAuthorization.SignedInBrowserAsync(_factory, Email);
        var tokens = await AgentAuthorization.ConnectAsync(_factory, browser);
        using var http = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/proposals/{proposal}/approval") { Content = JsonContent.Create(new { }) };
        request.Headers.Authorization = new("Bearer", tokens.AccessToken);

        var response = await http.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("proposed", await _graph.ScalarAsync<string>("SELECT status FROM change_proposals WHERE id = @id", new NpgsqlParameter("id", proposal)));
        Assert.Equal(0L, await _graph.ScalarAsync<long>("SELECT count(*) FROM nodes WHERE deleted_at IS NOT NULL"));
    }

    // DA-131: without the scope, the 403 is the same for a visible, hidden, other Account's or unknown target.
    [Fact]
    public async Task Without_write_every_target_gets_the_same_insufficient_scope_answer()
    {
        var visible = await PersonsNodeAsync("Visible");
        var hidden = await PersonsNodeAsync("Hidden");
        await _graph.WrittenAsync(_accountId, new UpdateNode(hidden, 1) { HiddenFromAgents = true });
        var stranger = await _graph.CreateAccountAsync();
        var strangers = Guid.CreateVersion7();
        await _graph.WrittenAsync(stranger, new CreateNode(strangers, "Not yours"));
        using var browser = await AgentAuthorization.SignedInBrowserAsync(_factory, Email);
        var readOnly = await AgentAuthorization.ConnectAsync(_factory, browser, scope: AgentAccess.ReadScope, clientId: OtherClientId);
        using var http = _factory.CreateClient();

        var answers = new List<(HttpStatusCode Status, string Challenge, string Body)>();
        foreach (var target in new[] { visible, hidden, strangers, Guid.CreateVersion7() })
        {
            var response = await AgentAuthorization.CallToolAsync(http, readOnly.AccessToken, GraphWriteTools.UpdateNodeName, new { nodeId = target, expectedVersion = 1, title = "x" });
            answers.Add((response.StatusCode, response.Headers.WwwAuthenticate.ToString(), WithoutTraceId(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))));
        }

        Assert.All(answers, answer => Assert.Equal(HttpStatusCode.Forbidden, answer.Status));
        Assert.Contains("insufficient_scope", answers[0].Challenge, StringComparison.Ordinal);
        Assert.True(answers.Distinct().Count() == 1, string.Join("\n", answers.Select(answer => $"{answer.Status} {answer.Challenge} {answer.Body}")));
    }

    // DA-131, DA-134: a batch's scopes are checked before any of its targets is loaded. Without
    // write, a suggest_changes batch aimed at a visible, hidden, other Account's or unknown Node,
    // and an apply_changes of another agent's or an unknown proposal, all get the same 403, and
    // nothing is proposed or applied.
    [Fact]
    public async Task Without_write_every_batch_gets_the_same_insufficient_scope_answer_whatever_its_targets()
    {
        var visible = await PersonsNodeAsync("Visible");
        var hidden = await PersonsNodeAsync("Hidden");
        await _graph.WrittenAsync(_accountId, new UpdateNode(hidden, 1) { HiddenFromAgents = true });
        var stranger = await _graph.CreateAccountAsync();
        var strangers = Guid.CreateVersion7();
        await _graph.WrittenAsync(stranger, new CreateNode(strangers, "Not yours"));
        var othersProposal = await SuggestAsync(new { op = "update_node", nodeId = visible, expectedVersion = 1, title = "Edited" });
        using var browser = await AgentAuthorization.SignedInBrowserAsync(_factory, Email);
        var readOnly = await AgentAuthorization.ConnectAsync(_factory, browser, scope: AgentAccess.ReadScope, clientId: OtherClientId);
        using var http = _factory.CreateClient();
        var calls = new[] { visible, hidden, strangers, Guid.CreateVersion7() }
            .Select(target => (Tool: GraphWriteTools.SuggestChangesName, Arguments: (object)new
            {
                operations = new object[]
                {
                    new { op = "update_node", nodeId = target, expectedVersion = 1, title = "x" },
                    new { op = "create_relation", sourceNodeId = visible, targetNodeId = target, kind = "about" },
                    new { op = "delete_node", nodeId = target, expectedVersion = 1 },
                },
            }))
            .Concat(new[] { othersProposal, Guid.CreateVersion7() }
                .Select(proposal => (Tool: GraphWriteTools.ApplyChangesName, Arguments: (object)new { proposalId = proposal, idempotencyKey = "no-write" })));

        var answers = new List<(HttpStatusCode Status, string Challenge, string Body)>();
        foreach (var (tool, arguments) in calls)
        {
            var response = await AgentAuthorization.CallToolAsync(http, readOnly.AccessToken, tool, arguments);
            answers.Add((response.StatusCode, response.Headers.WwwAuthenticate.ToString(), WithoutTraceId(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))));
        }

        Assert.All(answers, answer => Assert.Equal(HttpStatusCode.Forbidden, answer.Status));
        Assert.Contains($"scope=\"{AgentAccess.WriteScope}\"", answers[0].Challenge, StringComparison.Ordinal);
        Assert.True(answers.Distinct().Count() == 1, string.Join("\n", answers.Select(answer => $"{answer.Status} {answer.Challenge} {answer.Body}")));
        Assert.Equal(1L, await _graph.ScalarAsync<long>("SELECT count(*) FROM change_proposals"));
        Assert.Equal("proposed", await _graph.ScalarAsync<string>("SELECT status FROM change_proposals WHERE id = @id", new NpgsqlParameter("id", othersProposal)));
        Assert.Equal("Visible", await _graph.ScalarAsync<string>("SELECT title FROM nodes WHERE id = @id", new NpgsqlParameter("id", visible)));
        Assert.Equal(0L, await _graph.ScalarAsync<long>("SELECT count(*) FROM nodes WHERE deleted_at IS NOT NULL"));
        Assert.Equal(0L, await _graph.ScalarAsync<long>("SELECT count(*) FROM relations"));
    }

    // Each answer has its own trace id; everything else must be the same.
    private static string WithoutTraceId(string body)
    {
        var problem = System.Text.Json.Nodes.JsonNode.Parse(body)!.AsObject();
        problem.Remove("traceId");
        return problem.ToJsonString();
    }

    // Past its lifetime, as seven days later would leave it; moving the test clock that far
    // would also expire the agent's access token.
    private Task ExpireProposalsAsync() =>
        database.ExecuteAsMigratorAsync(
            """
            UPDATE change_proposals SET expires_at = now() - interval '1 second' WHERE status = 'proposed';
            UPDATE jobs SET run_after = now() - interval '1 second' WHERE kind = 'graph.proposal_expiry';
            """,
            TestContext.Current.CancellationToken);

    private static Dictionary<string, string> SettingsWithThreeClients()
    {
        var settings = AgentAuthorization.Settings();
        settings[$"{IssuerOptions.SectionName}:Clients:1:ClientId"] = OtherClientId;
        settings[$"{IssuerOptions.SectionName}:Clients:1:DisplayName"] = "Other Agent";
        settings[$"{IssuerOptions.SectionName}:Clients:1:RedirectUris:0"] = AgentAuthorization.RedirectUri;
        settings[$"{IssuerOptions.SectionName}:Clients:2:ClientId"] = ThirdClientId;
        settings[$"{IssuerOptions.SectionName}:Clients:2:DisplayName"] = "Third Agent";
        settings[$"{IssuerOptions.SectionName}:Clients:2:RedirectUris:0"] = AgentAuthorization.RedirectUri;
        return settings;
    }

    private async Task<(McpClient Client, Guid AgentIdentityId)> ConnectAgentAsync(string clientId)
    {
        var tokens = await AgentAuthorization.ConnectAsync(_factory, _person, clientId: clientId);
        var client = await AgentMcp.ConnectAsync(_factory, tokens.AccessToken);
        using var whoami = await AgentMcp.CallAsync(client, WhoAmITool.Name);
        return (client, whoami.RootElement.GetProperty("agentIdentityId").GetGuid());
    }

    private async Task<Dictionary<string, bool>> WarningsOfChangeSetAsync(JsonDocument applied)
    {
        using var screen = await JsonAsync($"/api/changesets/{applied.RootElement.GetProperty("changeSetId").GetGuid()}");
        return Warnings(screen.RootElement.GetProperty("entries")[0].GetProperty("fields"));
    }

    private static Dictionary<string, bool> Warnings(JsonElement fields) =>
        fields.EnumerateArray().ToDictionary(field => field.GetProperty("field").GetString()!, field => field.GetProperty("overwritesYourValue").GetBoolean());

    // Twenty changes to the person's Nodes, applied as the agent's own autonomous write.
    private static async Task SpendWindowAsync(McpClient agent, Guid[] nodes)
    {
        using var suggested = await AgentMcp.CallAsync(agent, GraphWriteTools.SuggestChangesName, new { operations = Updates(nodes).ToArray() });
        using var applied = await AgentMcp.CallAsync(agent, GraphWriteTools.ApplyChangesName, new { proposalId = ProposalOf(suggested), idempotencyKey = "spend" });
        Assert.Equal("applied", applied.RootElement.GetProperty("status").GetString());
    }

    private Task<bool> RequiresUserAsync(Guid proposal) =>
        _graph.ScalarAsync<bool>("SELECT requires_user FROM change_proposals WHERE id = @id", new NpgsqlParameter("id", proposal));

    private Task<HttpResponseMessage> SetRequiresApprovalAsync(bool required) =>
        _person.SendAsync(HttpMethod.Patch, $"/api/agent-identities/{_agentId}", new { requiresApproval = required });

    private Task<JsonDocument> CallAsync(string toolName, object arguments) => AgentMcp.CallAsync(_agent, toolName, arguments);

    private async Task<Guid> SuggestAsync(object operation)
    {
        using var suggested = await CallAsync(GraphWriteTools.SuggestChangesName, new { operations = new[] { operation } });
        return ProposalOf(suggested);
    }

    private async Task<Guid> PersonsNodeAsync(string title)
    {
        var node = Guid.CreateVersion7();
        await _graph.WrittenAsync(_accountId, new CreateNode(node, title));
        return node;
    }

    private async Task<Guid[]> PersonsNodesAsync(int count)
    {
        var nodes = Enumerable.Range(0, count).Select(_ => Guid.CreateVersion7()).ToArray();
        await _graph.WrittenAsync(_accountId, [.. nodes.Select((node, index) => (GraphOperation)new CreateNode(node, $"Note {index}"))]);
        return nodes;
    }

    private static IEnumerable<object> Updates(IEnumerable<Guid> nodes, int version = 1) =>
        nodes.Select((node, index) => (object)new { op = "update_node", nodeId = node, expectedVersion = version, title = $"Edited {index}" });

    private async Task<JsonDocument> JsonAsync(string path)
    {
        var response = await _person.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    private async Task<Guid[]> ListedChangeSetsAsync(string path)
    {
        using var feed = await JsonAsync(path);
        return [.. feed.RootElement.GetProperty("data").EnumerateArray().Select(item => item.GetProperty("id").GetGuid())];
    }

    private static async Task<Guid> ChangeSetIdAsync(HttpResponseMessage response)
    {
        using var receipt = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        return receipt.RootElement.GetProperty("changeSetId").GetGuid();
    }

    private static Guid SingleCreated(JsonDocument answer) =>
        answer.RootElement.GetProperty("createdNodes").EnumerateObject().Single().Value.GetGuid();

    private static Guid ProposalOf(JsonDocument answer) => answer.RootElement.GetProperty("proposalId").GetGuid();

    private static string[] Strings(JsonElement array) => [.. array.EnumerateArray().Select(item => item.GetString()!)];
}
