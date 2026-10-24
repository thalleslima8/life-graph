using System.Diagnostics;
using System.Net;
using System.Text.Json;
using LifeGraph.Accounts.Contracts;
using LifeGraph.Accounts.Issuer;
using LifeGraph.Agents.Mcp;
using LifeGraph.Graph.Contracts;
using LifeGraph.IntegrationTests.Graph;
using LifeGraph.IntegrationTests.Infrastructure;
using LifeGraph.Resources.Application;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using Npgsql;

namespace LifeGraph.IntegrationTests.Agents;

/// <summary>
/// <c>capture_resource</c> as a connected agent calls it (DA-156): one GraphChangeSet for the
/// Resource and its Relation, the same Resource on every capture of the link, a related Node
/// the agent may not see not found, the neutral answer for a link kept by a Node hidden from
/// agents (DA-154), proposals with the connection's toggle on (DA-044) and the write scope.
/// </summary>
public sealed class ResourceToolsTests(PostgresDatabase database) : IAsyncLifetime
{
    private const string Email = "ada@example.test";
    private const string OtherClientId = "other-agent";

    private readonly LifeGraphApiFactory _factory = new(database, SettingsWithTwoClients());
    private GraphWriteHarness _graph = null!;
    private Guid _accountId;
    private SpaClient _person = null!;
    private McpClient _agent = null!;
    private Guid _agentId;

    public async ValueTask InitializeAsync()
    {
        await database.ResetAsync();
        _accountId = (await TestAccounts.ProvisionConfirmedAsync(_factory, Email)).AccountId;
        _graph = new GraphWriteHarness(database, _factory);
        _person = await AgentAuthorization.SignedInBrowserAsync(_factory, Email);
        var tokens = await AgentAuthorization.ConnectAsync(_factory, _person);
        _agent = await AgentMcp.ConnectAsync(_factory, tokens.AccessToken);
        using var whoami = await AgentMcp.CallAsync(_agent, WhoAmITool.Name);
        _agentId = whoami.RootElement.GetProperty("agentIdentityId").GetGuid();
    }

    public async ValueTask DisposeAsync()
    {
        await _agent.DisposeAsync();
        _person.Dispose();
        await _factory.DisposeAsync();
    }

    // Exit criterion: three links saved and one related, each undoable as one change.
    [Fact]
    public async Task An_agent_captures_links_into_the_inbox_and_relates_one_in_the_same_changeset()
    {
        var book = await PersonsNodeAsync("Meditations");

        using var related = await CaptureAsync(new { url = "https://example.test/stoicism", source = "conversation", relatedNodeId = book, relationKind = "about" });
        using var second = await CaptureAsync(new { url = "https://youtu.be/dQw4w9WgXcQ" });
        using var third = await CaptureAsync(new { url = "https://example.test/paper.pdf", title = "The paper" });

        var resource = related.RootElement.GetProperty("nodeId").GetGuid();
        var changeSet = related.RootElement.GetProperty("changeSetId").GetGuid();
        Assert.Equal(("applied", true, false, false), Flags(related));
        Assert.Equal(2L, await _graph.ScalarAsync<long>("SELECT count(*) FROM change_entries WHERE changeset_id = @id", new NpgsqlParameter("id", changeSet)));
        Assert.Equal($"{book}|about", await _graph.ScalarAsync<string>(
            "SELECT target_node_id || '|' || kind FROM relations WHERE source_node_id = @id", new NpgsqlParameter("id", resource)));
        Assert.Equal(_agentId, await _graph.ScalarAsync<Guid>(
            "SELECT agent_identity_id FROM changesets WHERE id = @id", new NpgsqlParameter("id", changeSet)));
        Assert.Equal(3L, await _graph.ScalarAsync<long>("SELECT count(*) FROM nodes WHERE canonical_url IS NOT NULL AND inbox_entered_at IS NOT NULL"));
        Assert.Equal("The paper", await _graph.ScalarAsync<string>(
            "SELECT title FROM nodes WHERE id = @id", new NpgsqlParameter("id", third.RootElement.GetProperty("nodeId").GetGuid())));
        Assert.True(second.RootElement.GetProperty("created").GetBoolean());

        var undone = await _person.PostAsync($"/api/changesets/{changeSet}/undo", new { });
        Assert.Equal(HttpStatusCode.OK, undone.StatusCode);
        Assert.Equal(0L, await _graph.ScalarAsync<long>("SELECT count(*) FROM relations WHERE source_node_id = @id AND deleted_at IS NULL", new NpgsqlParameter("id", resource)));
    }

    // The epic's exit criterion: "keep these 3 links and relate them to Comprar casa" gives three
    // Resources with no duplicate, related, in the Inbox and listed in Agent Changes.
    [Fact]
    public async Task Three_links_kept_for_a_node_become_three_related_resources_in_the_inbox_listed_in_agent_changes()
    {
        var house = await PersonsNodeAsync("Comprar casa");
        string[] links = ["https://example.test/financing", "https://youtu.be/dQw4w9WgXcQ", "https://example.test/neighborhoods.pdf"];

        var resources = new List<Guid>();
        var changeSets = new List<Guid>();
        foreach (var link in links)
        {
            using var answer = await CaptureAsync(new { url = link, relatedNodeId = house, relationKind = "about" });
            resources.Add(answer.RootElement.GetProperty("nodeId").GetGuid());
            changeSets.Add(answer.RootElement.GetProperty("changeSetId").GetGuid());
        }

        using var repeated = await CaptureAsync(new { url = "https://www.youtube.com/watch?v=dQw4w9WgXcQ&utm_source=chat", relatedNodeId = house, relationKind = "about" });
        var feed = await _person.GetAsync($"/api/changesets?agentIdentityId={_agentId}");
        using var agentChanges = JsonDocument.Parse(await feed.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var listed = agentChanges.RootElement.GetProperty("data").EnumerateArray().Select(item => item.GetProperty("id").GetGuid()).ToList();

        Assert.Equal(3, resources.Distinct().Count());
        Assert.Equal(resources[1], repeated.RootElement.GetProperty("nodeId").GetGuid());
        Assert.Equal(3L, await _graph.ScalarAsync<long>("SELECT count(*) FROM nodes WHERE canonical_url IS NOT NULL"));
        Assert.Equal(3L, await _graph.ScalarAsync<long>(
            "SELECT count(*) FROM changesets WHERE agent_identity_id = @agent", new NpgsqlParameter("agent", _agentId)));
        Assert.Equal(3L, await _graph.ScalarAsync<long>(
            "SELECT count(*) FROM relations WHERE target_node_id = @house", new NpgsqlParameter("house", house)));
        Assert.Equal(3L, await _graph.ScalarAsync<long>(
            "SELECT count(*) FROM nodes WHERE canonical_url IS NOT NULL AND inbox_entered_at IS NOT NULL AND deleted_at IS NULL"));
        Assert.Equal(3L, await _graph.ScalarAsync<long>(
            "SELECT count(DISTINCT source_node_id) FROM relations WHERE target_node_id = @house AND kind = 'about' AND deleted_at IS NULL",
            new NpgsqlParameter("house", house)));
        Assert.Equal(HttpStatusCode.OK, feed.StatusCode);
        Assert.All(changeSets, changeSet => Assert.Contains(changeSet, listed));
    }

    [Fact]
    public async Task Capturing_a_kept_link_again_answers_the_same_resource_and_changes_nothing()
    {
        using var first = await CaptureAsync(new { url = "https://example.test/again?id=1" });
        using var again = await CaptureAsync(new { url = "https://EXAMPLE.test/again?utm_campaign=x&id=1" });

        Assert.Equal(first.RootElement.GetProperty("nodeId").GetGuid(), again.RootElement.GetProperty("nodeId").GetGuid());
        Assert.Equal(("applied", false, true, true), Flags(again));
        Assert.False(again.RootElement.TryGetProperty("changeSetId", out _));
        Assert.Equal(1L, await _graph.ScalarAsync<long>("SELECT count(*) FROM changesets WHERE agent_identity_id = @agent", new NpgsqlParameter("agent", _agentId)));
    }

    // DA-156: a related Node hidden from agents, of another Account or unknown is not found, and nothing is kept.
    [Fact]
    public async Task A_related_node_the_agent_may_not_see_is_not_found_and_nothing_is_captured()
    {
        var hidden = await PersonsNodeAsync("Hidden");
        await _graph.WrittenAsync(_accountId, new UpdateNode(hidden, 1) { HiddenFromAgents = true });
        var stranger = await _graph.CreateAccountAsync();
        var strangers = Guid.CreateVersion7();
        await _graph.WrittenAsync(stranger, new CreateNode(strangers, "Not yours"));

        var refusals = new List<string>();
        foreach (var target in new[] { hidden, strangers, Guid.CreateVersion7() })
        {
            refusals.Add(await AgentMcp.RefusalAsync(_agent, ResourceTools.CaptureResourceName, new { url = "https://example.test/x", relatedNodeId = target }));
        }

        Assert.Single(refusals.Distinct());
        Assert.Contains("not found", refusals[0], StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0L, await _graph.ScalarAsync<long>("SELECT count(*) FROM nodes WHERE canonical_url IS NOT NULL"));
    }

    // DA-154: the link of a Node hidden from agents is not created again, the Node is not named
    // and no Relation is made; the answer has the fields of a new capture, without the ids,
    // and takes no telling time apart from one.
    [Fact]
    public async Task A_link_kept_by_a_node_hidden_from_agents_answers_neutrally_without_naming_it()
    {
        var book = await PersonsNodeAsync("Meditations");
        var kept = await _person.PostAsync("/api/resources", new { url = "https://example.test/private" });
        using var keptBody = JsonDocument.Parse(await kept.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var hiddenResource = keptBody.RootElement.GetProperty("nodeId").GetGuid();
        await _graph.WrittenAsync(_accountId, new UpdateNode(hiddenResource, 1) { HiddenFromAgents = true });

        var neutralText = await AgentMcp.TextAsync(_agent, ResourceTools.CaptureResourceName, new { url = "https://example.test/private?utm_source=x", relatedNodeId = book });
        var newText = await AgentMcp.TextAsync(_agent, ResourceTools.CaptureResourceName, new { url = "https://example.test/public" });

        Assert.DoesNotContain(hiddenResource.ToString(), neutralText, StringComparison.OrdinalIgnoreCase);
        using var neutral = JsonDocument.Parse(neutralText);
        using var fresh = JsonDocument.Parse(newText);
        Assert.Equal(
            FieldNames(fresh).Except(["nodeId", "changeSetId"]),
            FieldNames(neutral));
        Assert.Equal(("applied", false, false, false), Flags(neutral));
        Assert.Equal(0L, await _graph.ScalarAsync<long>("SELECT count(*) FROM relations"));
        Assert.Equal(1L, await _graph.ScalarAsync<long>("SELECT count(*) FROM nodes WHERE canonical_url = 'https://example.test/private'"));

        var (neutralTime, newTime) = await PairedMedianDurationsAsync(
            ResourceTools.CaptureResourceName,
            index => new { url = $"https://example.test/private?utm_term={index}" },
            index => new { url = $"https://example.test/new-{index}" });
        AssertAlike(neutralTime, newTime, "A neutral capture", "a new one");
    }

    // DA-154: an agent moving a Resource's link onto one another Resource keeps is told the link cannot be kept, never that it is taken, hidden keeper or not.
    [Fact]
    public async Task An_agent_moving_a_link_onto_one_a_hidden_node_keeps_gets_the_neutral_answer_of_a_visible_one()
    {
        var kept = await _person.PostAsync("/api/resources", new { url = "https://example.test/private" });
        using var keptBody = JsonDocument.Parse(await kept.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var hiddenResource = keptBody.RootElement.GetProperty("nodeId").GetGuid();
        await _graph.WrittenAsync(_accountId, new UpdateNode(hiddenResource, 1) { HiddenFromAgents = true });
        Assert.True((await _person.PostAsync("/api/resources", new { url = "https://example.test/shown" })).IsSuccessStatusCode);
        using var captured = await CaptureAsync(new { url = "https://example.test/mine" });
        var mine = captured.RootElement.GetProperty("nodeId").GetGuid();
        var urlProperty = await SystemPropertyOfAsync(SystemOntology.ResourcePropertyKeys.Url);
        object MoveTo(string url) => new { nodeId = mine, expectedVersion = 1, properties = new Dictionary<Guid, string> { [urlProperty] = url } };

        var ontoHidden = await AgentMcp.RefusalAsync(_agent, GraphWriteTools.UpdateNodeName, MoveTo("https://example.test/private?utm_source=x"));
        var ontoShown = await AgentMcp.RefusalAsync(_agent, GraphWriteTools.UpdateNodeName, MoveTo("https://example.test/shown?utm_source=x"));

        Assert.Equal(ontoShown, ontoHidden);
        Assert.Equal("This link cannot be kept as a Resource.", ontoHidden);
        Assert.DoesNotContain(hiddenResource.ToString(), ontoHidden, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("https://example.test/mine", await _graph.ScalarAsync<string>("SELECT canonical_url FROM nodes WHERE id = @id", new NpgsqlParameter("id", mine)));

        var (hiddenTime, shownTime) = await PairedMedianDurationsAsync(
            GraphWriteTools.UpdateNodeName,
            index => MoveTo($"https://example.test/private?utm_term={index}"),
            index => MoveTo($"https://example.test/shown?utm_term={index}"));
        AssertAlike(hiddenTime, shownTime, "Onto a hidden Node's link", "onto a visible one's");
    }

    // DA-044, DA-156: with the toggle on, a capture is a proposal, and a variant of the same link finds it.
    [Fact]
    public async Task With_approval_required_a_capture_is_proposed_once_per_link()
    {
        Assert.Equal(HttpStatusCode.OK, (await _person.SendAsync(HttpMethod.Patch, $"/api/agent-identities/{_agentId}", new { requiresApproval = true })).StatusCode);

        using var proposed = await CaptureAsync(new { url = "https://example.test/later?id=3" });
        using var variant = await CaptureAsync(new { url = "https://example.test/later?id=3&utm_medium=chat" });

        Assert.Equal("pending_approval", proposed.RootElement.GetProperty("status").GetString());
        Assert.Equal(proposed.RootElement.GetProperty("proposalId").GetGuid(), variant.RootElement.GetProperty("proposalId").GetGuid());
        Assert.True(variant.RootElement.GetProperty("deduplicated").GetBoolean());
        Assert.Equal(0L, await _graph.ScalarAsync<long>("SELECT count(*) FROM nodes WHERE canonical_url IS NOT NULL"));

        var proposalId = proposed.RootElement.GetProperty("proposalId").GetGuid();
        var detail = await _person.GetAsync($"/api/proposals/{proposalId}");
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        using var screen = JsonDocument.Parse(await detail.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var operation = Assert.Single(screen.RootElement.GetProperty("operations").EnumerateArray());
        Assert.Equal("capture_resource", operation.GetProperty("op").GetString());
    }

    // DA-156: the pending proposal of a link is found by the link alone, whatever title or Relation comes with it.
    [Fact]
    public async Task With_approval_required_the_same_link_with_another_title_or_relation_finds_the_pending_proposal()
    {
        Assert.Equal(HttpStatusCode.OK, (await _person.SendAsync(HttpMethod.Patch, $"/api/agent-identities/{_agentId}", new { requiresApproval = true })).StatusCode);
        var house = await PersonsNodeAsync("Comprar casa");

        using var proposed = await CaptureAsync(new { url = "https://example.test/listing" });
        using var again = await CaptureAsync(new { url = "https://example.test/listing?utm_source=x", title = "Listing", relatedNodeId = house, relationKind = "about" });

        Assert.Equal(proposed.RootElement.GetProperty("proposalId").GetGuid(), again.RootElement.GetProperty("proposalId").GetGuid());
        Assert.True(again.RootElement.GetProperty("deduplicated").GetBoolean());
        Assert.Equal(1L, await _graph.ScalarAsync<long>(
            "SELECT count(*) FROM change_proposals WHERE agent_identity_id = @agent", new NpgsqlParameter("agent", _agentId)));
    }

    // DA-156: the Account's capture budget holds for agents too.
    [Fact]
    public async Task The_accounts_capture_budget_refuses_an_agents_capture_and_keeps_nothing()
    {
        var budget = _factory.Services.GetRequiredService<CaptureRateLimit>();
        while (budget.TryTake(_accountId))
        {
        }

        var refusal = await AgentMcp.RefusalAsync(_agent, ResourceTools.CaptureResourceName, new { url = "https://example.test/over-budget" });

        Assert.Equal(CaptureAdmission.TooManyCapturesMessage, refusal);
        Assert.Equal(0L, await _graph.ScalarAsync<long>("SELECT count(*) FROM nodes WHERE canonical_url IS NOT NULL"));
    }

    // DA-046, DA-132: a capture that brings back the person's Resource counts as a change to their entity; a new one does not.
    [Fact]
    public async Task A_capture_restoring_the_persons_resource_counts_toward_the_window_and_a_new_one_does_not()
    {
        var kept = await _person.PostAsync("/api/resources", new { url = "https://example.test/persons" });
        using var keptBody = JsonDocument.Parse(await kept.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var persons = keptBody.RootElement.GetProperty("nodeId").GetGuid();
        await _graph.WrittenAsync(_accountId, new DeleteNode(persons, 1));

        using var restored = await CaptureAsync(new { url = "https://example.test/persons" });
        using var fresh = await CaptureAsync(new { url = "https://example.test/agents-own" });

        Assert.Equal(persons, restored.RootElement.GetProperty("nodeId").GetGuid());
        Assert.Equal(1, await WindowOperationsOfAsync(restored.RootElement.GetProperty("changeSetId").GetGuid()));
        Assert.Equal(0, await WindowOperationsOfAsync(fresh.RootElement.GetProperty("changeSetId").GetGuid()));
    }

    [Fact]
    public async Task An_invalid_link_is_refused_and_nothing_is_kept()
    {
        var refusal = await AgentMcp.RefusalAsync(_agent, ResourceTools.CaptureResourceName, new { url = "https://ada:secret@example.test/private" });

        Assert.DoesNotContain("secret", refusal, StringComparison.Ordinal);
        Assert.Equal(0L, await _graph.ScalarAsync<long>("SELECT count(*) FROM nodes WHERE canonical_url IS NOT NULL"));
    }

    // DA-150: an agent may not write canonical_url nor fetch_status, nor a kind outside the list of DA-056.
    [Fact]
    public async Task An_agent_edit_of_a_system_value_or_a_kind_outside_the_list_is_refused()
    {
        using var captured = await CaptureAsync(new { url = "https://example.test/agent-kinds" });
        var nodeId = captured.RootElement.GetProperty("nodeId").GetGuid();
        var canonicalProperty = await SystemPropertyOfAsync(SystemOntology.ResourcePropertyKeys.CanonicalUrl);
        var fetchStatusProperty = await SystemPropertyOfAsync(SystemOntology.ResourcePropertyKeys.FetchStatus);
        var kindProperty = await SystemPropertyOfAsync(SystemOntology.ResourcePropertyKeys.ResourceKind);

        var canonical = await AgentMcp.RefusalAsync(_agent, GraphWriteTools.UpdateNodeName, new
        {
            nodeId,
            expectedVersion = 1,
            properties = new Dictionary<Guid, string> { [canonicalProperty] = "https://example.test/other" },
        });
        var fetchStatus = await AgentMcp.RefusalAsync(_agent, GraphWriteTools.UpdateNodeName, new
        {
            nodeId,
            expectedVersion = 1,
            properties = new Dictionary<Guid, string> { [fetchStatusProperty] = SystemOntology.FetchStatuses.Fetched },
        });
        var kind = await AgentMcp.RefusalAsync(_agent, GraphWriteTools.UpdateNodeName, new
        {
            nodeId,
            expectedVersion = 1,
            properties = new Dictionary<Guid, string> { [kindProperty] = "anything" },
        });

        Assert.Contains($"properties.{canonicalProperty}", canonical, StringComparison.Ordinal);
        Assert.Contains($"properties.{fetchStatusProperty}", fetchStatus, StringComparison.Ordinal);
        Assert.Contains($"properties.{kindProperty}", kind, StringComparison.Ordinal);
        Assert.Equal(1, await _graph.ScalarAsync<int>("SELECT version FROM nodes WHERE id = @id", new NpgsqlParameter("id", nodeId)));
        Assert.Equal(SystemOntology.FetchStatuses.Pending, await _graph.ScalarAsync<string>(
            "SELECT properties ->> @property FROM nodes WHERE id = @id",
            new NpgsqlParameter("property", fetchStatusProperty.ToString()),
            new NpgsqlParameter("id", nodeId)));
    }

    // DA-122: capture writes, so it needs lifegraph.write before anything is read.
    [Fact]
    public async Task Without_write_the_capture_gets_insufficient_scope()
    {
        using var browser = await AgentAuthorization.SignedInBrowserAsync(_factory, Email);
        var readOnly = await AgentAuthorization.ConnectAsync(_factory, browser, scope: AgentAccess.ReadScope, clientId: OtherClientId);
        using var http = _factory.CreateClient();

        var response = await AgentAuthorization.CallToolAsync(http, readOnly.AccessToken, ResourceTools.CaptureResourceName, new { url = "https://example.test/x" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("insufficient_scope", response.Headers.WwwAuthenticate.ToString(), StringComparison.Ordinal);
        Assert.Equal(0L, await _graph.ScalarAsync<long>("SELECT count(*) FROM nodes WHERE canonical_url IS NOT NULL"));
    }

    // The two calls alternate, so a slower moment of the machine weighs on both medians alike.
    private async Task<(TimeSpan First, TimeSpan Second)> PairedMedianDurationsAsync(
        string toolName,
        Func<int, object> first,
        Func<int, object> second)
    {
        var firstDurations = new List<TimeSpan>();
        var secondDurations = new List<TimeSpan>();
        for (var index = 0; index < 5; index++)
        {
            firstDurations.Add(await DurationAsync(toolName, first(index)));
            secondDurations.Add(await DurationAsync(toolName, second(index)));
        }

        return (Median(firstDurations), Median(secondDurations));
    }

    private async Task<TimeSpan> DurationAsync(string toolName, object arguments)
    {
        var watch = Stopwatch.StartNew();
        await AgentMcp.TextAsync(_agent, toolName, arguments);
        return watch.Elapsed;
    }

    private static TimeSpan Median(List<TimeSpan> durations) => durations.Order().ElementAt(durations.Count / 2);

    // Alike: within 250 ms, or within half of the slower one when the whole machine is slow.
    private static void AssertAlike(TimeSpan first, TimeSpan second, string firstLabel, string secondLabel)
    {
        var tolerance = TimeSpan.FromTicks(Math.Max(TimeSpan.FromMilliseconds(250).Ticks, Math.Max(first.Ticks, second.Ticks) / 2));
        Assert.True(
            (first - second).Duration() < tolerance,
            $"{firstLabel} took {first.TotalMilliseconds} ms, {secondLabel} {second.TotalMilliseconds} ms.");
    }

    private Task<Guid> SystemPropertyOfAsync(string key) => _graph.ScalarAsync<Guid>(
        "SELECT id FROM property_definitions WHERE account_id = @account AND system_key = @key",
        new NpgsqlParameter("account", _accountId),
        new NpgsqlParameter("key", key));

    private Task<int> WindowOperationsOfAsync(Guid changeSetId) => _graph.ScalarAsync<int>(
        "SELECT window_operations FROM changesets WHERE id = @id", new NpgsqlParameter("id", changeSetId));

    private Task<JsonDocument> CaptureAsync(object arguments) => AgentMcp.CallAsync(_agent, ResourceTools.CaptureResourceName, arguments);

    private async Task<Guid> PersonsNodeAsync(string title)
    {
        var node = Guid.CreateVersion7();
        await _graph.WrittenAsync(_accountId, new CreateNode(node, title));
        return node;
    }

    private static (string?, bool, bool, bool) Flags(JsonDocument answer) => (
        answer.RootElement.GetProperty("status").GetString(),
        answer.RootElement.GetProperty("created").GetBoolean(),
        answer.RootElement.GetProperty("alreadyExists").GetBoolean(),
        answer.RootElement.GetProperty("alreadyInInbox").GetBoolean());

    private static IEnumerable<string> FieldNames(JsonDocument answer) =>
        answer.RootElement.EnumerateObject().Select(field => field.Name).Order(StringComparer.Ordinal);

    private static Dictionary<string, string> SettingsWithTwoClients()
    {
        var settings = AgentAuthorization.Settings();
        settings[$"{IssuerOptions.SectionName}:Clients:1:ClientId"] = OtherClientId;
        settings[$"{IssuerOptions.SectionName}:Clients:1:DisplayName"] = "Other Agent";
        settings[$"{IssuerOptions.SectionName}:Clients:1:RedirectUris:0"] = AgentAuthorization.RedirectUri;
        return settings;
    }
}
