using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LifeGraph.Graph.Contracts;
using LifeGraph.Graph.Domain;
using LifeGraph.Infrastructure.Errors;
using LifeGraph.IntegrationTests.Infrastructure;
using Npgsql;

namespace LifeGraph.IntegrationTests.Graph;

/// <summary>
/// The REST interface of the graph as the SPA uses it: a signed-in session, the CSRF token
/// and every write through the single pipeline. Another Account's data answers 404, no
/// session 401 (DA-104, DA-109).
/// </summary>
public sealed class GraphEndpointsTests(PostgresDatabase database) : IAsyncLifetime
{
    private const string EmailA = "ada@example.test";
    private const string EmailB = "grace@example.test";

    private readonly LifeGraphApiFactory _factory = new(database);
    private SpaClient _spa = null!;
    private Guid _accountA;
    private Guid _accountB;

    public async ValueTask InitializeAsync()
    {
        await database.ResetAsync();
        _accountA = (await TestAccounts.ProvisionConfirmedAsync(_factory, EmailA)).AccountId;
        _accountB = (await TestAccounts.ProvisionConfirmedAsync(_factory, EmailB)).AccountId;
        _spa = await TestAccounts.SignedInAsync(_factory, EmailA);
    }

    public async ValueTask DisposeAsync()
    {
        _spa.Dispose();
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task A_person_creates_types_and_nodes_edits_changes_the_type_and_sees_other_properties()
    {
        var pages = await CreatedIdAsync("/api/property-definitions", new { name = "Pages", valueKind = "number" });
        var link = await CreatedIdAsync("/api/property-definitions", new { name = "Link", valueKind = "url" });
        var book = await CreatedIdAsync("/api/types", new { name = "Book", propertyDefinitionIds = new[] { pages } });
        var article = await CreatedIdAsync("/api/types", new { name = "Article", propertyDefinitionIds = new[] { link } });

        var created = await _spa.PostAsync("/api/nodes", new { title = "Meditations", typeId = book, properties = new Dictionary<Guid, object> { [pages] = 254 } });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var node = await EntityIdAsync(created);
        Assert.Equal($"/api/nodes/{node}", created.Headers.Location?.OriginalString);

        var retyped = await SendAsync(HttpMethod.Patch, $"/api/nodes/{node}", new { version = 1, type = new { id = article } });
        Assert.Equal(HttpStatusCode.OK, retyped.StatusCode);

        using var inspected = await JsonAsync($"/api/nodes/{node}");
        var root = inspected.RootElement;
        Assert.Equal(2, root.GetProperty("version").GetInt32());
        Assert.Equal("Article", root.GetProperty("typeName").GetString());
        var attached = Assert.Single(root.GetProperty("properties").EnumerateArray());
        Assert.Equal(("Link", "url", JsonValueKind.Null), (attached.GetProperty("name").GetString(), attached.GetProperty("valueKind").GetString(), attached.GetProperty("value").ValueKind));
        var other = Assert.Single(root.GetProperty("otherProperties").EnumerateArray());
        Assert.Equal(("Pages", 254), (other.GetProperty("name").GetString(), other.GetProperty("value").GetInt32()));

        // Back to Book: the value is the Type's again, by its property id (DA-016).
        await SendAsync(HttpMethod.Patch, $"/api/nodes/{node}", new { version = 2, type = new { id = book } });
        using var back = await JsonAsync($"/api/nodes/{node}");
        Assert.Equal(254, Assert.Single(back.RootElement.GetProperty("properties").EnumerateArray()).GetProperty("value").GetInt32());
        Assert.Empty(back.RootElement.GetProperty("otherProperties").EnumerateArray());
    }

    [Fact]
    public async Task The_list_filters_nodes_without_type_and_in_the_inbox_and_pages_by_cursor()
    {
        var book = await CreatedIdAsync("/api/types", new { name = "Book" });
        var typed = await CreatedIdAsync("/api/nodes", new { title = "Typed", typeId = book });
        var captured = await CreatedIdAsync("/api/nodes", new { title = "Captured", inInbox = true });
        var plain = await CreatedIdAsync("/api/nodes", new { title = "Plain" });

        Assert.Equal([plain, captured], await ListedIdsAsync("/api/nodes?withoutType=true"));
        Assert.Equal([typed], await ListedIdsAsync($"/api/nodes?typeId={book}"));
        Assert.Equal([captured], await ListedIdsAsync("/api/nodes?inInbox=true"));

        using var first = await JsonAsync("/api/nodes?limit=2");
        var cursor = first.RootElement.GetProperty("page").GetProperty("nextCursor").GetString();
        using var second = await JsonAsync($"/api/nodes?limit=2&cursor={cursor}");
        Assert.Equal([typed, await AccountRootIdAsync(_accountA)], second.RootElement.GetProperty("data").EnumerateArray().Select(item => item.GetProperty("id").GetGuid()));
        Assert.Equal(JsonValueKind.Null, second.RootElement.GetProperty("page").GetProperty("nextCursor").ValueKind);

        var archived = await _spa.PostAsync($"/api/nodes/{captured}/archival", new { });
        Assert.Equal(HttpStatusCode.OK, archived.StatusCode);
        Assert.Empty(await ListedIdsAsync("/api/nodes?inInbox=true"));
    }

    [Theory]
    [InlineData("/api/nodes?limit=0")]
    [InlineData("/api/nodes?cursor=not-a-cursor")]
    [InlineData("/api/nodes?cursor=not-a-cursor!")]
    [InlineData("/api/changesets?since=AAAAAAAAAAAAAAAAAAAA*A")]
    [InlineData("/api/nodes?withoutType=true&typeId=0199a7b2-0000-7000-8000-000000000000")]
    [InlineData("/api/changesets?since=AAAAAAAAAAAAAAAAAAAAAA&before=AAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("/api/changesets?before=not-a-cursor")]
    public async Task An_invalid_list_query_is_a_validation_error(string path)
    {
        var response = await _spa.GetAsync(path);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(CommonErrors.ValidationFailed.Code, await ProblemCode.ReadAsync(response));
    }

    // DA-035 and DA-013: hiding is a change like any other, in a GraphChangeSet the person can undo.
    [Fact]
    public async Task Hiding_a_node_and_a_type_from_agents_goes_through_changesets_and_undoes()
    {
        var health = await CreatedIdAsync("/api/types", new { name = "Health" });
        var node = await CreatedIdAsync("/api/nodes", new { title = "Diary" });

        var hidden = await SendAsync(HttpMethod.Patch, $"/api/nodes/{node}", new { version = 1, hiddenFromAgents = true });
        var nodeChange = await ChangeSetIdAsync(hidden);
        using (var inspected = await JsonAsync($"/api/nodes/{node}"))
        {
            Assert.True(inspected.RootElement.GetProperty("hiddenFromAgents").GetBoolean());
            Assert.Equal(2, inspected.RootElement.GetProperty("version").GetInt32());
        }

        var typeChange = await ChangeSetIdAsync(await SendAsync(HttpMethod.Patch, $"/api/types/{health}", new { name = "Saúde", hiddenFromAgents = true }));
        using (var type = await JsonAsync($"/api/types/{health}"))
        {
            Assert.Equal(("Saúde", true), (type.RootElement.GetProperty("name").GetString(), type.RootElement.GetProperty("hiddenFromAgents").GetBoolean()));
        }

        Assert.Equal(HttpStatusCode.OK, (await _spa.PostAsync($"/api/changesets/{typeChange}/undo", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _spa.PostAsync($"/api/changesets/{nodeChange}/undo", new { })).StatusCode);
        using var restoredType = await JsonAsync($"/api/types/{health}");
        using var restoredNode = await JsonAsync($"/api/nodes/{node}");
        Assert.Equal(("Health", false), (restoredType.RootElement.GetProperty("name").GetString(), restoredType.RootElement.GetProperty("hiddenFromAgents").GetBoolean()));
        Assert.False(restoredNode.RootElement.GetProperty("hiddenFromAgents").GetBoolean());
    }

    [Fact]
    public async Task Updating_a_type_needs_a_name_or_the_hidden_flag()
    {
        var health = await CreatedIdAsync("/api/types", new { name = "Health" });

        var response = await SendAsync(HttpMethod.Patch, $"/api/types/{health}", new { });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var problem = await ProblemAsync(response);
        Assert.Equal(CommonErrors.ValidationFailed.Code, problem.RootElement.GetProperty("code").GetString());
        Assert.Equal(["hiddenFromAgents", "name"], problem.RootElement.GetProperty("errors").EnumerateObject().Select(field => field.Name).Order(StringComparer.Ordinal));
    }

    // One request, two operations in one GraphChangeSet: the error names the request field, not operations[n].
    [Fact]
    public async Task Renaming_and_hiding_a_type_names_the_invalid_field_and_writes_nothing()
    {
        var health = await CreatedIdAsync("/api/types", new { name = "Health" });

        var response = await SendAsync(HttpMethod.Patch, $"/api/types/{health}", new { name = "  ", hiddenFromAgents = true });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var problem = await ProblemAsync(response);
        Assert.Equal(["name"], problem.RootElement.GetProperty("errors").EnumerateObject().Select(field => field.Name));
        using var type = await JsonAsync($"/api/types/{health}");
        Assert.Equal(("Health", false), (type.RootElement.GetProperty("name").GetString(), type.RootElement.GetProperty("hiddenFromAgents").GetBoolean()));
    }

    [Fact]
    public async Task Deleting_and_undoing_brings_the_node_back_with_its_relations()
    {
        var book = await CreatedIdAsync("/api/nodes", new { title = "Letters" });
        var author = await CreatedIdAsync("/api/nodes", new { title = "Seneca" });
        var related = await _spa.PostAsync("/api/relations", new { sourceNodeId = book, targetNodeId = author, kind = "written_by" });
        Assert.Equal(HttpStatusCode.Created, related.StatusCode);

        var deleted = await _spa.DeleteAsync($"/api/nodes/{author}?version=1");
        var changeSet = await ChangeSetIdAsync(deleted);
        Assert.Equal(HttpStatusCode.NotFound, (await _spa.GetAsync($"/api/nodes/{author}")).StatusCode);

        var undone = await _spa.PostAsync($"/api/changesets/{changeSet}/undo", new { });
        Assert.Equal(HttpStatusCode.OK, undone.StatusCode);
        using var relations = await JsonAsync($"/api/nodes/{book}/relations");
        var relation = Assert.Single(relations.RootElement.GetProperty("data").EnumerateArray());
        Assert.Equal(("Seneca", "hard", "user"), (relation.GetProperty("targetNodeTitle").GetString(), relation.GetProperty("assertion").GetString(), relation.GetProperty("origin").GetString()));

        var again = await _spa.PostAsync($"/api/changesets/{changeSet}/undo", new { });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, again.StatusCode);
        Assert.Equal(GraphErrors.ChangeSetAlreadyReverted.Code, await ProblemCode.ReadAsync(again));
    }

    [Fact]
    public async Task Restoring_a_deleted_node_and_relation_answers_with_its_own_change()
    {
        var book = await CreatedIdAsync("/api/nodes", new { title = "Letters" });
        var author = await CreatedIdAsync("/api/nodes", new { title = "Seneca" });
        var relation = await EntityIdAsync(await _spa.PostAsync("/api/relations", new { sourceNodeId = book, targetNodeId = author, kind = "written_by" }));
        Assert.Equal(HttpStatusCode.OK, (await _spa.DeleteAsync($"/api/relations/{relation}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _spa.DeleteAsync($"/api/nodes/{author}?version=1")).StatusCode);

        var restoredNode = await _spa.PostAsync($"/api/nodes/{author}/restoration", new { });
        var restoredRelation = await _spa.PostAsync($"/api/relations/{relation}/restoration", new { });

        Assert.Equal(HttpStatusCode.OK, restoredNode.StatusCode);
        Assert.Equal(HttpStatusCode.OK, restoredRelation.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _spa.GetAsync($"/api/nodes/{author}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _spa.PostAsync($"/api/nodes/{Guid.CreateVersion7()}/restoration", new { })).StatusCode);
    }

    [Fact]
    public async Task An_edit_at_a_stale_version_is_a_conflict_and_one_without_version_is_invalid()
    {
        var node = await CreatedIdAsync("/api/nodes", new { title = "Draft" });
        await SendAsync(HttpMethod.Patch, $"/api/nodes/{node}", new { version = 1, title = "Edited" });

        var stale = await SendAsync(HttpMethod.Patch, $"/api/nodes/{node}", new { version = 1, title = "Mine" });
        var unversioned = await SendAsync(HttpMethod.Patch, $"/api/nodes/{node}", new { title = "Mine" });
        var staleDelete = await _spa.DeleteAsync($"/api/nodes/{node}?version=1");

        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal(GraphErrors.NodeVersionConflict.Code, await ProblemCode.ReadAsync(stale));
        Assert.Equal(HttpStatusCode.BadRequest, unversioned.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, staleDelete.StatusCode);
    }

    // DA-117, DA-118: the details the SPA acts on reach the HTTP body, while the global
    // option keeps other details on the server (DA-102).
    [Fact]
    public async Task The_conflict_and_in_use_refusals_carry_their_details_in_the_body()
    {
        var node = await CreatedIdAsync("/api/nodes", new { title = "Draft" });
        var created = await _spa.GetAsync("/api/changesets");
        using var feed = JsonDocument.Parse(await created.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var creation = feed.RootElement.GetProperty("data")[0].GetProperty("id").GetGuid();
        await SendAsync(HttpMethod.Patch, $"/api/nodes/{node}", new { version = 1, title = "Edited" });

        using var stale = await ProblemAsync(await SendAsync(HttpMethod.Patch, $"/api/nodes/{node}", new { version = 1, title = "Mine" }));
        using var undo = await ProblemAsync(await _spa.PostAsync($"/api/changesets/{creation}/undo", new { }));

        Assert.Equal(GraphErrors.NodeVersionConflict.Code, stale.RootElement.GetProperty("code").GetString());
        // Limaj writes the details of a non-validation error under "details", a 422's under "errors".
        Assert.Equal("2", stale.RootElement.GetProperty("details").GetProperty("version")[0].GetString());
        Assert.Equal(GraphErrors.UndoConflict.Code, undo.RootElement.GetProperty("code").GetString());
        Assert.Equal(["entries[0]"], undo.RootElement.GetProperty("details").EnumerateObject().Select(entry => entry.Name));

        var book = await CreatedIdAsync("/api/types", new { name = "Book" });
        var typed = await CreatedIdAsync("/api/nodes", new { title = "Letters", typeId = book });
        Assert.Equal(HttpStatusCode.OK, (await _spa.DeleteAsync($"/api/nodes/{typed}?version=1")).StatusCode);

        var inUse = await _spa.DeleteAsync($"/api/types/{book}");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, inUse.StatusCode);
        using var refusal = await ProblemAsync(inUse);
        var details = refusal.RootElement.GetProperty("errors");
        Assert.Equal(GraphErrors.TypeInUseByDeletedNodes.Code, refusal.RootElement.GetProperty("code").GetString());
        Assert.Equal("1", details.GetProperty("deletedNodeCount")[0].GetString());
        Assert.True(DateTimeOffset.Parse(details.GetProperty("lastPurgeAt")[0].GetString()!, CultureInfo.InvariantCulture) > DateTimeOffset.UtcNow.AddDays(29));
    }

    // Archiving needs no version (DA-117); a deleted Node is not found.
    [Fact]
    public async Task Archiving_a_deleted_node_is_not_found_and_reading_returns_the_moved_version()
    {
        var node = await CreatedIdAsync("/api/nodes", new { title = "Captured", inInbox = true });
        var archived = await _spa.PostAsync($"/api/nodes/{node}/archival", new { });
        var receipt = await archived.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);

        using var read = await JsonAsync($"/api/nodes/{node}");
        Assert.Equal(2, receipt.GetProperty("changes")[0].GetProperty("version").GetInt32());
        Assert.Equal(2, read.RootElement.GetProperty("version").GetInt32());
        Assert.Equal(JsonValueKind.Null, read.RootElement.GetProperty("inboxEnteredAt").ValueKind);

        Assert.Equal(HttpStatusCode.OK, (await _spa.DeleteAsync($"/api/nodes/{node}?version=2")).StatusCode);
        var again = await _spa.PostAsync($"/api/nodes/{node}/archival", new { });
        Assert.Equal(HttpStatusCode.NotFound, again.StatusCode);
    }

    [Fact]
    public async Task A_body_over_the_cap_is_refused_with_413()
    {
        var response = await _spa.PostAsync("/api/nodes", new { title = "Large", body = new string('x', 300 * 1024) });

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Equal(CommonErrors.PayloadTooLarge.Code, await ProblemCode.ReadAsync(response));
    }

    [Fact]
    public async Task Field_errors_name_the_request_fields()
    {
        var response = await _spa.PostAsync("/api/nodes", new { title = "  " });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("\"title\"", body, StringComparison.Ordinal);
        Assert.DoesNotContain("operations[0]", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_ontology_rules_answer_422_with_their_codes()
    {
        var pages = await CreatedIdAsync("/api/property-definitions", new { name = "Pages", valueKind = "number" });
        var book = await CreatedIdAsync("/api/types", new { name = "Book", propertyDefinitionIds = new[] { pages } });
        await CreatedIdAsync("/api/nodes", new { title = "Letters", typeId = book, properties = new Dictionary<Guid, object> { [pages] = 120 } });

        var typeInUse = await _spa.DeleteAsync($"/api/types/{book}");
        var propertyInUse = await _spa.DeleteAsync($"/api/property-definitions/{pages}");
        var kindChange = await SendAsync(HttpMethod.Patch, $"/api/property-definitions/{pages}", new { valueKind = "boolean" });
        var taken = await _spa.PostAsync("/api/types", new { name = "Book" });

        Assert.Equal(GraphErrors.TypeInUse.Code, await ProblemCode.ReadAsync(typeInUse));
        Assert.Equal(GraphErrors.PropertyInUse.Code, await ProblemCode.ReadAsync(propertyInUse));
        Assert.Equal(GraphErrors.PropertyKindChangeBlocked.Code, await ProblemCode.ReadAsync(kindChange));
        Assert.Equal(
            [HttpStatusCode.UnprocessableEntity, HttpStatusCode.UnprocessableEntity, HttpStatusCode.UnprocessableEntity, HttpStatusCode.Conflict],
            [typeInUse.StatusCode, propertyInUse.StatusCode, kindChange.StatusCode, taken.StatusCode]);
    }

    // DA-168: the usage read gives the count the delete confirms; a stale count answers 409.
    [Fact]
    public async Task A_property_delete_confirms_the_count_the_usage_read_gave()
    {
        var pages = await CreatedIdAsync("/api/property-definitions", new { name = "Pages", valueKind = "number" });
        var book = await CreatedIdAsync("/api/types", new { name = "Book", propertyDefinitionIds = new[] { pages } });
        await CreatedIdAsync("/api/nodes", new { title = "Letters", typeId = book, properties = new Dictionary<Guid, object> { [pages] = 120 } });
        Assert.Equal(HttpStatusCode.OK, (await _spa.DeleteAsync($"/api/types/{book}/properties/{pages}")).StatusCode);

        using var usage = await JsonAsync($"/api/property-definitions/{pages}/usage");
        var stale = await _spa.DeleteAsync($"/api/property-definitions/{pages}?expectedValueCount=0");
        var negative = await _spa.DeleteAsync($"/api/property-definitions/{pages}?expectedValueCount=-1");
        var deleted = await _spa.DeleteAsync($"/api/property-definitions/{pages}?expectedValueCount=1");

        Assert.Equal(
            [1, 0, 0],
            [usage.RootElement.GetProperty("nodeCount").GetInt32(), usage.RootElement.GetProperty("typeCount").GetInt32(), usage.RootElement.GetProperty("tombstoneCount").GetInt32()]);
        Assert.Equal(GraphErrors.PropertyUsageChanged.Code, await ProblemCode.ReadAsync(stale));
        Assert.Equal(
            [HttpStatusCode.Conflict, HttpStatusCode.BadRequest, HttpStatusCode.OK],
            [stale.StatusCode, negative.StatusCode, deleted.StatusCode]);
        Assert.Equal(HttpStatusCode.NotFound, (await _spa.GetAsync($"/api/property-definitions/{pages}/usage")).StatusCode);
    }

    [Fact]
    public async Task Types_attach_and_detach_and_select_options_round_trip_as_strings()
    {
        var status = await CreatedIdAsync("/api/property-definitions", new
        {
            name = "Status",
            valueKind = "multi_select",
            options = new[] { new { label = "Reading" }, new { label = "Done" } },
        });
        var book = await CreatedIdAsync("/api/types", new { name = "Book" });

        Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Put, $"/api/types/{book}/properties/{status}", body: null)).StatusCode);
        using var attached = await JsonAsync($"/api/types/{book}");
        Assert.Equal("multi_select", Assert.Single(attached.RootElement.GetProperty("properties").EnumerateArray()).GetProperty("valueKind").GetString());

        Assert.Equal(HttpStatusCode.OK, (await _spa.DeleteAsync($"/api/types/{book}/properties/{status}")).StatusCode);
        using var detached = await JsonAsync($"/api/types/{book}");
        Assert.Empty(detached.RootElement.GetProperty("properties").EnumerateArray());

        using var definition = await JsonAsync($"/api/property-definitions/{status}");
        Assert.Equal(["Reading", "Done"], definition.RootElement.GetProperty("options").EnumerateArray().Select(option => option.GetProperty("label").GetString()));
    }

    [Fact]
    public async Task Another_accounts_graph_is_not_found_and_no_session_is_unauthorized_alike()
    {
        var node = await CreatedIdAsync("/api/nodes", new { title = "Private" });
        var book = await CreatedIdAsync("/api/types", new { name = "Book" });
        using var other = await TestAccounts.SignedInAsync(_factory, EmailB);
        using var anonymous = new SpaClient(_factory.CreateClient());

        var reads = new[] { $"/api/nodes/{node}", $"/api/nodes/{node}/relations", $"/api/types/{book}" };
        foreach (var path in reads)
        {
            Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync(path)).StatusCode);
        }

        var edit = await other.SendAsync(HttpMethod.Patch, $"/api/nodes/{node}", new { version = 1, title = "Mine" });
        var archive = await other.PostAsync($"/api/nodes/{node}/archival", new { });
        var delete = await other.DeleteAsync($"/api/nodes/{node}?version=1");
        Assert.Equal([HttpStatusCode.NotFound, HttpStatusCode.NotFound, HttpStatusCode.NotFound], [edit.StatusCode, archive.StatusCode, delete.StatusCode]);
        Assert.Equal([await AccountRootIdAsync(_accountB)], await ListedIdsAsync("/api/nodes", other));

        var existing = await anonymous.GetAsync($"/api/nodes/{node}");
        var unknown = await anonymous.GetAsync($"/api/nodes/{Guid.CreateVersion7()}");
        Assert.Equal((HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized), (existing.StatusCode, unknown.StatusCode));
        Assert.NotEmpty(existing.Headers.WwwAuthenticate);
    }

    // DA-071, DA-177, DA-184: the suggestions of a Node with why they were inferred, then
    // Accept and Ignore as actions answering the receipt; another Account finds none of them.
    [Fact]
    public async Task Soft_relations_are_listed_with_their_explanation_accepted_and_ignored_and_another_account_finds_none()
    {
        var root = await CreatedIdAsync("/api/nodes", new { title = "Marco Aurélio" });
        var first = await CreatedIdAsync("/api/nodes", new { title = "Estoicismo" });
        var second = await CreatedIdAsync("/api/nodes", new { title = "Sêneca" });
        var toFirst = await EntityIdAsync(await _spa.PostAsync("/api/relations", new { sourceNodeId = root, targetNodeId = first, kind = "related_to" }));
        var toSecond = await EntityIdAsync(await _spa.PostAsync("/api/relations", new { sourceNodeId = root, targetNodeId = second, kind = "related_to" }));
        await MakeSoftAsync(toFirst, toSecond);
        using var other = await TestAccounts.SignedInAsync(_factory, EmailB);

        using var listed = await JsonAsync($"/api/nodes/{root}/soft-relations");
        var suggestion = listed.RootElement.GetProperty("data").EnumerateArray().Single(item => item.GetProperty("id").GetGuid() == toFirst);
        var explanation = suggestion.GetProperty("explanation");
        Assert.Equal(
            (first, Relation.EmbeddingSimilarityMethod, "test-model", 0.9),
            (suggestion.GetProperty("nodeId").GetGuid(), explanation.GetProperty("method").GetString(), explanation.GetProperty("modelId").GetString(), explanation.GetProperty("score").GetDouble()));
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/nodes/{root}/soft-relations")).StatusCode);

        var foreignAccept = await other.PostAsync($"/api/nodes/{root}/soft-relations/{toFirst}/acceptance", new { });
        var foreignIgnore = await other.PostAsync($"/api/nodes/{root}/soft-relations/{toSecond}/dismissal", new { });
        Assert.Equal((HttpStatusCode.NotFound, HttpStatusCode.NotFound), (foreignAccept.StatusCode, foreignIgnore.StatusCode));

        var accepted = await _spa.PostAsync($"/api/nodes/{root}/soft-relations/{toFirst}/acceptance", new { });
        var ignored = await _spa.PostAsync($"/api/nodes/{root}/soft-relations/{toSecond}/dismissal", new { });
        await ChangeSetIdAsync(accepted);
        await ChangeSetIdAsync(ignored);

        Assert.Empty(await ListedIdsAsync($"/api/nodes/{root}/soft-relations"));
        using var relations = await JsonAsync($"/api/nodes/{root}/relations");
        var hard = Assert.Single(relations.RootElement.GetProperty("data").EnumerateArray());
        Assert.Equal(new[] { root, first }.Order(), new[] { hard.GetProperty("sourceNodeId").GetGuid(), hard.GetProperty("targetNodeId").GetGuid() }.Order());
        Assert.Equal(("hard", "user"), (hard.GetProperty("assertion").GetString(), hard.GetProperty("origin").GetString()));
        var again = await _spa.PostAsync($"/api/nodes/{root}/soft-relations/{toFirst}/acceptance", new { });
        Assert.Equal(HttpStatusCode.NotFound, again.StatusCode);
    }

    private Task MakeSoftAsync(params Guid[] relationIds) =>
        database.ExecuteAsMigratorAsync(
            """
            UPDATE relations
            SET assertion = 'soft', origin = 'system', confidence = 0.9, strength = 0.9, kind = @kind,
                inference_method = @method, inference_model_id = 'test-model',
                source_node_id = LEAST(source_node_id, target_node_id),
                target_node_id = GREATEST(source_node_id, target_node_id)
            WHERE id = ANY (@soft)
            """,
            TestContext.Current.CancellationToken,
            new NpgsqlParameter("kind", Relation.SoftKind),
            new NpgsqlParameter("method", Relation.EmbeddingSimilarityMethod),
            new NpgsqlParameter("soft", relationIds));

    // DA-024 and DA-115: the history newest first with its Provenance and the Undo chain,
    // then only what happened since the last read.
    [Fact]
    public async Task The_changeset_feed_shows_the_history_and_then_what_happened_since()
    {
        var node = await CreatedIdAsync("/api/nodes", new { title = "Draft" });
        var edit = await ChangeSetIdAsync(await SendAsync(HttpMethod.Patch, $"/api/nodes/{node}", new { version = 1, title = "Final" }));
        var undo = await ChangeSetIdAsync(await _spa.PostAsync($"/api/changesets/{edit}/undo", new { }));

        using var history = await JsonAsync("/api/changesets");
        var items = history.RootElement.GetProperty("data").EnumerateArray().ToList();
        Assert.Equal(4, items.Count);
        Assert.Equal((undo, edit), (items[0].GetProperty("id").GetGuid(), items[0].GetProperty("revertsChangeSetId").GetGuid()));
        Assert.Equal(("reverted", undo), (items[1].GetProperty("status").GetString(), items[1].GetProperty("revertedByChangeSetId").GetGuid()));
        Assert.Equal(("human", "ui"), (items[2].GetProperty("actorKind").GetString(), items[2].GetProperty("channel").GetString()));
        var created = Assert.Single(items[2].GetProperty("entries").EnumerateArray());
        Assert.Equal(("node", node, "created", "Draft"), (created.GetProperty("entityKind").GetString(), created.GetProperty("entityId").GetGuid(), created.GetProperty("operation").GetString(), created.GetProperty("label").GetString()));
        Assert.Equal(("system", "system"), (items[3].GetProperty("actorKind").GetString(), items[3].GetProperty("channel").GetString()));
        Assert.Equal(await AccountRootIdAsync(_accountA), Assert.Single(items[3].GetProperty("entries").EnumerateArray()).GetProperty("entityId").GetGuid());

        var latest = history.RootElement.GetProperty("latestCursor").GetString();
        await CreatedIdAsync("/api/nodes", new { title = "Later" });
        using var since = await JsonAsync($"/api/changesets?since={latest}");
        var later = Assert.Single(since.RootElement.GetProperty("data").EnumerateArray());
        Assert.Equal("Later", later.GetProperty("entries")[0].GetProperty("label").GetString());

        using var caughtUp = await JsonAsync($"/api/changesets?since={since.RootElement.GetProperty("latestCursor").GetString()}");
        Assert.Empty(caughtUp.RootElement.GetProperty("data").EnumerateArray());
        Assert.Equal(since.RootElement.GetProperty("latestCursor").GetString(), caughtUp.RootElement.GetProperty("latestCursor").GetString());

        using var other = await TestAccounts.SignedInAsync(_factory, EmailB);
        var othersFeed = await other.GetAsync("/api/changesets");
        using var othersPage = JsonDocument.Parse(await othersFeed.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var othersOnly = Assert.Single(othersPage.RootElement.GetProperty("data").EnumerateArray());
        Assert.Equal(await AccountRootIdAsync(_accountB), Assert.Single(othersOnly.GetProperty("entries").EnumerateArray()).GetProperty("entityId").GetGuid());
    }

    // DA-024: older pages of the history, newest first, through "before"; they carry no latestCursor.
    [Fact]
    public async Task The_changeset_history_pages_back_through_before()
    {
        foreach (var title in new[] { "First", "Second", "Third" })
        {
            await CreatedIdAsync("/api/nodes", new { title });
        }

        using var newest = await JsonAsync("/api/changesets?limit=2");
        var newestIds = newest.RootElement.GetProperty("data").EnumerateArray().Select(item => item.GetProperty("id").GetGuid()).ToList();
        var before = newest.RootElement.GetProperty("page").GetProperty("nextCursor").GetString();
        Assert.NotNull(before);
        Assert.NotNull(newest.RootElement.GetProperty("latestCursor").GetString());

        using var older = await JsonAsync($"/api/changesets?limit=2&before={before}");
        var olderItems = older.RootElement.GetProperty("data").EnumerateArray().ToList();
        Assert.Equal(
            ["First", SystemOntology.AccountRootDefaultTitle],
            olderItems.Select(item => item.GetProperty("entries")[0].GetProperty("label").GetString()));
        Assert.True(olderItems[0].GetProperty("id").GetGuid().CompareTo(newestIds[^1]) < 0);
        Assert.Equal(JsonValueKind.Null, older.RootElement.GetProperty("page").GetProperty("nextCursor").ValueKind);
        Assert.Equal(JsonValueKind.Null, older.RootElement.GetProperty("latestCursor").ValueKind);
        Assert.Equal(
            ["Third", "Second"],
            newest.RootElement.GetProperty("data").EnumerateArray().Select(item => item.GetProperty("entries")[0].GetProperty("label").GetString()));
    }

    [Fact]
    public async Task A_write_without_the_csrf_token_is_refused()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/nodes") { Content = JsonContent.Create(new { title = "Forged" }) };

        var response = await _spa.Http.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(CommonErrors.CsrfTokenInvalid.Code, await ProblemCode.ReadAsync(response));
    }

    private static async Task<JsonDocument> ProblemAsync(HttpResponseMessage response)
    {
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    private Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? body) => _spa.SendAsync(method, path, body);

    [Fact]
    public async Task A_person_creates_fills_and_deletes_collections_and_another_account_sees_none()
    {
        var node = await CreatedIdAsync("/api/nodes", new { title = "Meditations" });
        var created = await _spa.PostAsync("/api/collections", new { name = "Favoritos", pinned = true });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var collection = await EntityIdAsync(created);
        Assert.Equal($"/api/collections/{collection}", created.Headers.Location?.OriginalString);
        Assert.Equal(HttpStatusCode.BadRequest, (await _spa.PostAsync("/api/collections", new { name = " ", criteria = new { version = 1, match = "all", criteria = Array.Empty<object>() } })).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Put, $"/api/collections/{collection}/members/{node}", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(HttpMethod.Put, $"/api/collections/{collection}", new { name = "Favoritos" })).StatusCode);
        using (var stillPinned = await JsonAsync($"/api/collections/{collection}"))
        {
            Assert.True(stillPinned.RootElement.GetProperty("pinned").GetBoolean());
        }

        Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Put, $"/api/collections/{collection}", new { name = "Favoritos", pinned = false })).StatusCode);

        using (var read = await JsonAsync($"/api/collections/{collection}"))
        {
            Assert.False(read.RootElement.GetProperty("pinned").GetBoolean());
            Assert.Equal(JsonValueKind.Null, read.RootElement.GetProperty("criteria").ValueKind);
        }

        Assert.Equal([node], await ListedIdsAsync($"/api/collections/{collection}/members", _spa));

        using var other = await TestAccounts.SignedInAsync(_factory, EmailB);
        Assert.Empty(await ListedIdsAsync("/api/collections", other));
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/collections/{collection}/members")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.DeleteAsync($"/api/collections/{collection}")).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await _spa.DeleteAsync($"/api/collections/{collection}/members/{node}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _spa.DeleteAsync($"/api/collections/{collection}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _spa.GetAsync($"/api/collections/{collection}")).StatusCode);
    }

    [Fact]
    public async Task A_person_creates_renames_applies_and_deletes_tags_and_another_account_sees_none()
    {
        var node = await CreatedIdAsync("/api/nodes", new { title = "Meditations" });
        var created = await _spa.PostAsync("/api/tags", new { name = "#Estoicismo" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var tag = await EntityIdAsync(created);
        Assert.Equal($"/api/tags/{tag}", created.Headers.Location?.OriginalString);

        Assert.Equal(HttpStatusCode.Conflict, (await _spa.PostAsync("/api/tags", new { name = "estoicismo" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _spa.PostAsync("/api/tags", new { name = "a#b" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Patch, $"/api/tags/{tag}", new { name = "Stoicism" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Put, $"/api/nodes/{node}/tags/{tag}", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Put, $"/api/nodes/{node}/tags/{tag}", new { })).StatusCode);

        using (var listed = await JsonAsync("/api/tags"))
        {
            var item = Assert.Single(listed.RootElement.GetProperty("data").EnumerateArray());
            Assert.Equal(("Stoicism", 1), (item.GetProperty("name").GetString(), item.GetProperty("nodeCount").GetInt32()));
        }

        using (var inspected = await JsonAsync($"/api/nodes/{node}"))
        {
            Assert.Equal("Stoicism", Assert.Single(inspected.RootElement.GetProperty("tags").EnumerateArray()).GetProperty("name").GetString());
        }

        using var other = await TestAccounts.SignedInAsync(_factory, EmailB);
        Assert.Empty(await ListedIdsAsync("/api/tags", other));
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/tags/{tag}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.DeleteAsync($"/api/tags/{tag}")).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await _spa.DeleteAsync($"/api/nodes/{node}/tags/{tag}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _spa.DeleteAsync($"/api/tags/{tag}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _spa.GetAsync($"/api/tags/{tag}")).StatusCode);
    }

    [Fact]
    public async Task A_person_searches_the_own_graph_and_another_account_finds_nothing_and_short_words_are_refused()
    {
        var stoicism = await CreatedIdAsync("/api/nodes", new { title = "Filosofia estoica" });

        using var found = await JsonAsync("/api/search?q=filosofia&limit=5");
        using var other = await TestAccounts.SignedInAsync(_factory, EmailB);
        using var foundByOther = JsonDocument.Parse(await other.Http.GetStringAsync(new Uri("/api/search?q=filosofia", UriKind.Relative), TestContext.Current.CancellationToken));
        var tooShort = await _spa.GetAsync("/api/search?q=f");

        var item = Assert.Single(found.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal((stoicism, "Filosofia estoica"), (item.GetProperty("id").GetGuid(), item.GetProperty("title").GetString()));
        Assert.Contains(item.GetProperty("match").GetString(), (string[])["text", "semantic", "both"]);
        Assert.Equal(JsonValueKind.Array, foundByOther.RootElement.GetProperty("items").ValueKind);
        Assert.Empty(foundByOther.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal(HttpStatusCode.BadRequest, tooShort.StatusCode);
        using var problem = await ProblemAsync(tooShort);
        Assert.Equal(["q"], problem.RootElement.GetProperty("errors").EnumerateObject().Select(field => field.Name));
    }

    private async Task<Guid> CreatedIdAsync(string path, object body)
    {
        var response = await _spa.PostAsync(path, body);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await EntityIdAsync(response);
    }

    private static async Task<Guid> EntityIdAsync(HttpResponseMessage response)
    {
        var receipt = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        return receipt.GetProperty("changes")[0].GetProperty("entityId").GetGuid();
    }

    private static async Task<Guid> ChangeSetIdAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var receipt = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        return receipt.GetProperty("changeSetId").GetGuid();
    }

    private async Task<JsonDocument> JsonAsync(string path)
    {
        var response = await _spa.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>The Raiz da Conta the sign-in created: every Account's first Node and first GraphChangeSet (DA-201).</summary>
    private Task<Guid> AccountRootIdAsync(Guid accountId) =>
        database.QueryScalarAsMigratorAsync<Guid>(
            "SELECT id FROM nodes WHERE account_id = @account AND system_key = 'account_root'",
            new NpgsqlParameter("account", accountId));

    private async Task<Guid[]> ListedIdsAsync(string path, SpaClient? client = null)
    {
        var response = await (client ?? _spa).GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var page = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        return [.. page.RootElement.GetProperty("data").EnumerateArray().Select(item => item.GetProperty("id").GetGuid())];
    }
}
