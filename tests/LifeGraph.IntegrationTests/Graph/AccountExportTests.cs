using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using LifeGraph.Agents.Mcp;
using LifeGraph.Graph.Application;
using LifeGraph.Graph.Contracts;
using LifeGraph.Infrastructure.Errors;
using LifeGraph.Infrastructure.Identity;
using LifeGraph.Infrastructure.Persistence;
using LifeGraph.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace LifeGraph.IntegrationTests.Graph;

/// <summary>
/// The Account export (DA-085, DA-247, DA-248): asked over HTTP, built by a job inside one
/// snapshot, downloaded once ready and never cached. It carries every kind of the person's data,
/// none of another Account's, and leaves out the tombstones and the derived data. A table with
/// an <c>account_id</c> added later without a place in the export, or a reason to stay out,
/// fails here.
/// </summary>
public sealed class AccountExportTests(PostgresDatabase database) : IAsyncLifetime
{
    private const string Email = "ada@example.test";
    private const string OtherEmail = "grace@example.test";
    private const string ExportPath = "/api/account/export";

    /// <summary>Where each table with an <c>account_id</c> lands in <c>export.json</c>.</summary>
    private static readonly Dictionary<string, string> ExportedTables = new()
    {
        ["nodes"] = "nodes",
        ["node_tags"] = "nodes[].tagIds",
        ["relations"] = "relations",
        ["property_definitions"] = "propertyDefinitions",
        ["types"] = "types",
        ["type_properties"] = "types[].propertyDefinitionIds",
        ["tags"] = "tags",
        ["collections"] = "collections",
        ["collection_members"] = "collections[].nodeIds",
        ["changesets"] = "changeSets",
        ["change_entries"] = "changeSets[].entries",
        ["change_proposals"] = "changeProposals",
        ["agent_identities"] = "agentIdentities",
        ["agent_reads"] = "agentReads",
        ["shared_graph_views"] = "sharedGraphViews",
        ["shared_graph_view_nodes"] = "sharedGraphViews[].nodeIds",
        ["shared_graph_view_properties"] = "sharedGraphViews[].propertyDefinitionIds",
        ["users"] = "account.email",
    };

    /// <summary>The tables with an <c>account_id</c> left out of the export, each with its reason (DA-247).</summary>
    private static readonly Dictionary<string, string> ExcludedTables = new()
    {
        ["jobs"] = "the work queue: system state, not the person's data",
        ["account_exports"] = "the export itself",
        ["node_embeddings"] = "derived and regenerable (DA-247)",
        ["soft_dismissals"] = "about Soft Relations, derived and left out (DA-247)",
        ["shared_graph_view_directory"] = "the links' token lookup: a secret, never exported (DA-247)",
        ["rls_probes"] = "the RLS harness's own table (RlsProbes), only in the test database",
    };

    private readonly LifeGraphApiFactory _factory = new(database, AgentAuthorization.Settings());

    public async ValueTask InitializeAsync() => await database.ResetAsync();

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    [Fact]
    public async Task Every_table_with_account_id_is_exported_or_left_out_with_a_reason()
    {
        var tables = await AccountTablesAsync();

        Assert.Empty(ExportedTables.Keys.Intersect(ExcludedTables.Keys));
        Assert.Equal(tables.Order(), ExportedTables.Keys.Concat(ExcludedTables.Keys).Order());
    }

    [Fact]
    public async Task The_export_holds_every_kind_of_the_accounts_data_and_nothing_of_another_account()
    {
        var seeded = await AccountWithDataAsync(_factory, Email);
        var other = (await TestAccounts.ProvisionConfirmedAsync(_factory, OtherEmail)).AccountId;
        await new GraphWriteHarness(database, _factory).WrittenAsync(other, new CreateNode(Guid.CreateVersion7(), "Nota da Grace"));
        using var spa = await TestAccounts.SignedInAsync(_factory, Email);

        var requested = await spa.PostAsync(ExportPath, new { });
        Assert.Equal(HttpStatusCode.Accepted, requested.StatusCode);
        Assert.Equal("pending", (await JsonOfAsync(requested)).GetProperty("status").GetString());
        Assert.Equal(ExportPath, requested.Headers.Location?.OriginalString);
        await BuildExportsAsync(_factory);

        var status = await JsonOfAsync(await spa.GetAsync(ExportPath));
        Assert.Equal("ready", status.GetProperty("status").GetString());
        var download = await spa.GetAsync($"{ExportPath}/content");
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.True(download.Headers.CacheControl?.NoStore);
        Assert.Equal("application/zip", download.Content.Headers.ContentType?.MediaType);
        Assert.Equal("attachment", download.Content.Headers.ContentDisposition?.DispositionType);

        using var zip = new ZipArchive(await download.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken));
        using var document = await JsonDocument.ParseAsync(zip.GetEntry(AccountExportArchiveWriter.DocumentEntry)!.Open(), cancellationToken: TestContext.Current.CancellationToken);
        var export = document.RootElement;
        var raw = export.GetRawText();

        Assert.Equal(seeded.AccountId, export.GetProperty("account").GetProperty("id").GetGuid());
        Assert.Equal(Email, export.GetProperty("account").GetProperty("email").GetString());
        foreach (var section in ExportedTables.Values.Where(section => !section.Contains('.', StringComparison.Ordinal)))
        {
            Assert.True(SectionOf(export, section).GetArrayLength() > 0, $"{section} is empty.");
        }

        var book = ById(export, "nodes", seeded.BookId);
        Assert.Equal(seeded.BookTypeId, book.GetProperty("typeId").GetGuid());
        Assert.Contains(seeded.TagId, book.GetProperty("tagIds").EnumerateArray().Select(id => id.GetGuid()));
        Assert.Equal(seeded.ReadingOptionId.ToString(), book.GetProperty("properties").EnumerateArray().Single().GetProperty("value").GetString());
        Assert.Equal(seeded.PagesId, ById(export, "nodes", seeded.OldBookId).GetProperty("otherProperties").EnumerateArray().Single().GetProperty("propertyDefinitionId").GetGuid());
        Assert.Contains(seeded.BookId, ById(export, "collections", seeded.ManualCollectionId).GetProperty("nodeIds").EnumerateArray().Select(id => id.GetGuid()));
        Assert.NotEqual(JsonValueKind.Null, ById(export, "collections", seeded.CriteriaCollectionId).GetProperty("criteria").ValueKind);
        Assert.StartsWith(seeded.ResourceUrl, ById(export, "nodes", seeded.ResourceId).GetProperty("canonicalUrl").GetString(), StringComparison.Ordinal);
        Assert.Contains(seeded.BookId, ById(export, "sharedGraphViews", seeded.ShareId).GetProperty("nodeIds").EnumerateArray().Select(id => id.GetGuid()));
        Assert.Contains(
            export.GetProperty("changeSets").EnumerateArray(),
            changeSet => changeSet.GetProperty("provenance").TryGetProperty("agentIdentityName", out var name) && name.ValueKind == JsonValueKind.String);
        Assert.Contains("tombstones", export.GetProperty("omitted").EnumerateArray().Select(item => item.GetString()));

        // Tombstones and another Account's rows stay out (DA-247).
        Assert.DoesNotContain(export.GetProperty("nodes").EnumerateArray(), node => node.GetProperty("id").GetGuid() == seeded.DeletedNodeId);
        Assert.DoesNotContain("Nota da Grace", raw, StringComparison.Ordinal);
        Assert.DoesNotContain(OtherEmail, raw, StringComparison.Ordinal);

        // One readable file per Node, named by its id.
        var markdownFiles = zip.Entries.Where(entry => entry.FullName.StartsWith(AccountExportArchiveWriter.NodesFolder, StringComparison.Ordinal)).Select(entry => entry.FullName).ToList();
        Assert.Equal(export.GetProperty("nodes").GetArrayLength(), markdownFiles.Count);
        Assert.Contains($"{AccountExportArchiveWriter.NodesFolder}{seeded.BookId:D}.md", markdownFiles);
    }

    [Fact]
    public async Task Before_any_request_the_status_is_none_and_nothing_downloads()
    {
        await TestAccounts.ProvisionConfirmedAsync(_factory, Email);
        using var spa = await TestAccounts.SignedInAsync(_factory, Email);

        var status = await spa.GetAsync(ExportPath);
        var download = await spa.GetAsync($"{ExportPath}/content");

        Assert.Equal("none", (await JsonOfAsync(status)).GetProperty("status").GetString());
        Assert.True(status.Headers.CacheControl?.NoStore);
        Assert.Equal(HttpStatusCode.NotFound, download.StatusCode);
        Assert.Equal(GraphErrors.ExportNotFound.Code, await ProblemCode.ReadAsync(download));
    }

    [Fact]
    public async Task A_second_request_while_one_is_pending_is_a_conflict_and_a_ready_one_is_replaced()
    {
        await TestAccounts.ProvisionConfirmedAsync(_factory, Email);
        using var spa = await TestAccounts.SignedInAsync(_factory, Email);

        Assert.Equal(HttpStatusCode.Accepted, (await spa.PostAsync(ExportPath, new { })).StatusCode);
        var again = await spa.PostAsync(ExportPath, new { });
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal(GraphErrors.ExportInProgress.Code, await ProblemCode.ReadAsync(again));
        Assert.Equal(HttpStatusCode.NotFound, (await spa.GetAsync($"{ExportPath}/content")).StatusCode);

        await BuildExportsAsync(_factory);
        Assert.Equal(HttpStatusCode.Accepted, (await spa.PostAsync(ExportPath, new { })).StatusCode);

        Assert.Equal(1L, await database.QueryScalarAsMigratorAsync<long>("SELECT count(*) FROM account_exports"));
        Assert.Equal("pending", (await JsonOfAsync(await spa.GetAsync(ExportPath))).GetProperty("status").GetString());
    }

    [Fact]
    public async Task A_ready_export_expires_after_its_lifetime_and_its_archive_is_dropped()
    {
        await TestAccounts.ProvisionConfirmedAsync(_factory, Email);
        using var spa = await TestAccounts.SignedInAsync(_factory, Email);
        await spa.PostAsync(ExportPath, new { });
        await BuildExportsAsync(_factory);

        _factory.Clock.Advance(AccountExportOptions.MaxLifetime + TimeSpan.FromMinutes(1));

        // The session of a day ago is over too: the person signs in again.
        using var later = await TestAccounts.SignedInAsync(_factory, Email);
        Assert.Equal("expired", (await JsonOfAsync(await later.GetAsync(ExportPath))).GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await later.GetAsync($"{ExportPath}/content")).StatusCode);
        await database.ExecuteAsMigratorAsync("UPDATE jobs SET run_after = now() - interval '1 second' WHERE status = 'pending'", TestContext.Current.CancellationToken);
        await new GraphWriteHarness(database, _factory).RunDueJobsAsync();
        Assert.Equal(0L, await database.QueryScalarAsMigratorAsync<long>("SELECT count(*) FROM account_exports WHERE content IS NOT NULL"));
    }

    [Fact]
    public async Task An_archive_past_the_size_cap_fails_whole_naming_the_first_data_that_passed_it()
    {
        await using var capped = FactoryWith(maxArchiveBytes: 1);
        await AccountWithDataAsync(capped, Email);
        using var spa = await TestAccounts.SignedInAsync(capped, Email);
        await spa.PostAsync(ExportPath, new { });

        await BuildExportsAsync(capped);

        // The Property Definitions are the first data written, so their first batch passes a 1-byte cap.
        var status = await JsonOfAsync(await spa.GetAsync(ExportPath));
        Assert.Equal(
            ("failed", "size_cap", "property_definitions"),
            (status.GetProperty("status").GetString(), status.GetProperty("failure").GetString(), status.GetProperty("oversizedData").GetString()));
        Assert.Equal(HttpStatusCode.NotFound, (await spa.GetAsync($"{ExportPath}/content")).StatusCode);
        Assert.Equal(0L, await database.QueryScalarAsMigratorAsync<long>("SELECT count(*) FROM account_exports WHERE content IS NOT NULL"));
    }

    [Fact]
    public async Task Nodes_too_large_for_the_cap_fail_the_export_naming_nodes_and_keep_no_archive()
    {
        await using var capped = FactoryWith(maxArchiveBytes: 64 * 1024, readBatchSize: 2);
        var account = (await TestAccounts.ProvisionConfirmedAsync(capped, Email)).AccountId;
        var graph = new GraphWriteHarness(database, capped);

        // Random text barely compresses: ten such bodies pass 64 KB, the small data before them does not.
        await graph.WrittenAsync(account, [.. Enumerable.Range(0, 10).Select(index =>
            new CreateNode(Guid.CreateVersion7(), $"Nota {index}", Body: Convert.ToBase64String(RandomNumberGenerator.GetBytes(24 * 1024))))]);
        using var spa = await TestAccounts.SignedInAsync(capped, Email);
        await spa.PostAsync(ExportPath, new { });

        await BuildExportsAsync(capped);

        var status = await JsonOfAsync(await spa.GetAsync(ExportPath));
        Assert.Equal(("failed", "size_cap", "nodes"), (status.GetProperty("status").GetString(), status.GetProperty("failure").GetString(), status.GetProperty("oversizedData").GetString()));
        Assert.Equal(0L, await database.QueryScalarAsMigratorAsync<long>("SELECT count(*) FROM account_exports WHERE content IS NOT NULL"));
    }

    [Fact]
    public async Task Reading_in_batches_smaller_than_every_table_exports_each_row_once_and_many_small_rows_fit_under_the_cap()
    {
        await using var batched = FactoryWith(readBatchSize: 2);
        var seeded = await AccountWithDataAsync(batched, Email);
        var graph = new GraphWriteHarness(database, batched);
        var tagIds = Enumerable.Range(0, 25).Select(_ => Guid.CreateVersion7()).ToList();
        var noteIds = Enumerable.Range(0, 9).Select(_ => Guid.CreateVersion7()).ToList();
        await graph.WrittenAsync(seeded.AccountId, [
            .. tagIds.Select((tagId, index) => (GraphOperation)new CreateTag(tagId, $"tag-{index}")),
            .. noteIds.Select((noteId, index) => (GraphOperation)new CreateNode(noteId, $"Nota {index}")),
            .. tagIds.Take(5).Select(tagId => (GraphOperation)new TagNode(noteIds[0], tagId))]);
        using var spa = await TestAccounts.SignedInAsync(batched, Email);
        await spa.PostAsync(ExportPath, new { });

        await BuildExportsAsync(batched);

        Assert.Equal("ready", (await JsonOfAsync(await spa.GetAsync(ExportPath))).GetProperty("status").GetString());
        var export = await ExportJsonAsync(await spa.GetAsync($"{ExportPath}/content"));
        await AssertExportedOnceAsync(export, "nodes", "SELECT array_agg(id) FROM nodes WHERE account_id = @account AND deleted_at IS NULL", new NpgsqlParameter("account", seeded.AccountId));
        await AssertExportedOnceAsync(export, "tags", "SELECT array_agg(id) FROM tags WHERE account_id = @account", new NpgsqlParameter("account", seeded.AccountId));
        await AssertExportedOnceAsync(export, "changeSets", "SELECT array_agg(id) FROM changesets WHERE account_id = @account", new NpgsqlParameter("account", seeded.AccountId));
        Assert.Equal(
            tagIds.Take(5).Order(),
            ById(export, "nodes", noteIds[0]).GetProperty("tagIds").EnumerateArray().Select(tagId => tagId.GetGuid()).Order());
        var changeEntries = SectionOf(export, "changeSets").EnumerateArray().Sum(changeSet => changeSet.GetProperty("entries").GetArrayLength());
        Assert.Equal(
            await database.QueryScalarAsMigratorAsync<long>("SELECT count(*) FROM change_entries WHERE account_id = @account", new NpgsqlParameter("account", seeded.AccountId)),
            changeEntries);
    }

    [Fact]
    public async Task A_write_made_while_the_export_reads_is_not_in_its_snapshot()
    {
        var account = (await TestAccounts.ProvisionConfirmedAsync(_factory, Email)).AccountId;
        var graph = new GraphWriteHarness(database, _factory);
        var lateNodeId = Guid.CreateVersion7();

        var archive = await graph.AsAsync(new AuthenticatedPrincipal(account, PrincipalType.Human), services =>
        {
            var db = services.GetRequiredService<LifeGraphDbContext>();
            return db.InAccountSnapshotAsync(
                async token =>
                {
                    // The first query fixes the snapshot; the Node is written from another connection after it.
                    await db.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM nodes").SingleAsync(token);
                    await graph.WrittenAsync(account, new CreateNode(lateNodeId, "Escrito durante o export"));
                    return await services.GetRequiredService<AccountExportReader>().BuildArchiveAsync(account, new AccountExportSizeBudget(long.MaxValue), token);
                },
                TestContext.Current.CancellationToken);
        });

        using var zip = new ZipArchive(new MemoryStream(archive!));
        using var export = await JsonDocument.ParseAsync(zip.GetEntry(AccountExportArchiveWriter.DocumentEntry)!.Open(), cancellationToken: TestContext.Current.CancellationToken);
        Assert.DoesNotContain(SectionOf(export.RootElement, "nodes").EnumerateArray(), node => node.GetProperty("id").GetGuid() == lateNodeId);
        Assert.Null(zip.GetEntry($"{AccountExportArchiveWriter.NodesFolder}{lateNodeId:D}.md"));
        Assert.Equal(1L, await database.QueryScalarAsMigratorAsync<long>("SELECT count(*) FROM nodes WHERE id = @id", new NpgsqlParameter("id", lateNodeId)));
    }

    [Fact]
    public async Task Without_a_session_every_export_route_is_unauthorized_and_an_agent_is_forbidden()
    {
        var account = (await TestAccounts.ProvisionConfirmedAsync(_factory, Email)).AccountId;
        using var anonymous = new SpaClient(_factory.CreateClient());

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(ExportPath)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync($"{ExportPath}/content")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.SendAsync(HttpMethod.Post, ExportPath, new { }, csrfToken: "none")).StatusCode);

        var graph = new GraphWriteHarness(database, _factory);
        var agent = new AuthenticatedPrincipal(account, PrincipalType.AgentIdentity, await graph.CreateAgentIdentityAsync(account));
        var refused = await graph.AsAsync(agent, services => services.GetRequiredService<AccountExports>().RequestAsync(TestContext.Current.CancellationToken));
        Assert.Equal(CommonErrors.Forbidden.Code, refused.Error?.Code);
        Assert.Equal(0L, await database.QueryScalarAsMigratorAsync<long>("SELECT count(*) FROM account_exports"));
    }

    // Only the export's job runs: the fetch and the embeddings the seed scheduled stay out of the way.
    private async Task BuildExportsAsync(LifeGraphApiFactory factory)
    {
        await database.ExecuteAsMigratorAsync(
            $"UPDATE jobs SET run_after = now() + interval '1 day' WHERE status = 'pending' AND kind <> '{AccountExportBuild.JobKind}'",
            TestContext.Current.CancellationToken);
        await new GraphWriteHarness(database, factory).RunDueJobsAsync();
    }

    private async Task<SeededAccount> AccountWithDataAsync(LifeGraphApiFactory factory, string email)
    {
        var account = (await TestAccounts.ProvisionConfirmedAsync(factory, email)).AccountId;
        using var browser = await AgentAuthorization.SignedInBrowserAsync(factory, email);
        var tokens = await AgentAuthorization.ConnectAsync(factory, browser);
        var graph = new GraphWriteHarness(database, factory);
        var root = await graph.AccountRootIdAsync(account);

        // An agent's read, Node and proposal (E5): AgentIdentity, audit and proposal rows.
        await using (var agent = await AgentMcp.ConnectAsync(factory, tokens.AccessToken))
        {
            await AgentMcp.CallAsync(agent, GraphReadTools.ListTypesName);
            await AgentMcp.CallAsync(agent, GraphWriteTools.CreateNodeName, new { title = "Pelo agente", anchor = new { nodeId = root } });
            await AgentMcp.CallAsync(agent, GraphWriteTools.SuggestChangesName, new { operations = new[] { new { op = "create_node", title = "Proposto", anchor = new { nodeId = root } } } });
        }

        var seeded = new SeededAccount(account);
        await graph.WrittenAsync(
            account,
            new DefineProperty(seeded.StatusId, "Status", PropertyValueKind.Select, [new SelectOption(seeded.ReadingOptionId, "Lendo")]),
            new DefineProperty(seeded.PagesId, "Páginas", PropertyValueKind.Number),
            new CreateType(seeded.BookTypeId, "Livro", [seeded.StatusId, seeded.PagesId]),
            new CreateTag(seeded.TagId, "leitura"),
            new CreateNode(seeded.BookId, "Sapiens", TypeId: seeded.BookTypeId, Properties: new Dictionary<Guid, JsonElement>
            {
                [seeded.StatusId] = JsonSerializer.SerializeToElement(seeded.ReadingOptionId.ToString()),
            }),
            new CreateNode(seeded.OldBookId, "Livro antigo", TypeId: seeded.BookTypeId, Properties: new Dictionary<Guid, JsonElement>
            {
                [seeded.PagesId] = JsonSerializer.SerializeToElement(320),
            }),
            new CreateNode(seeded.DeletedNodeId, "Rascunho apagado"),
            new CreateRelation(Guid.CreateVersion7(), seeded.BookId, seeded.OldBookId, "related_to"),
            new TagNode(seeded.BookId, seeded.TagId),
            new CreateCollection(seeded.ManualCollectionId, "Favoritos", null, Pinned: false),
            new AddToCollection(seeded.ManualCollectionId, seeded.BookId),
            new CreateCollection(
                seeded.CriteriaCollectionId,
                "Livros",
                new CollectionCriteria(CollectionCriteria.CurrentVersion, CollectionMatch.All, [new CollectionCriterion(CollectionCriterionType.Type, TypeId: seeded.BookTypeId)]),
                Pinned: true),
            new CaptureResource(seeded.ResourceId, seeded.ResourceUrl) { Title = "Artigo" });

        // The page of Pages leaves the Type: the old book keeps it among its Outras propriedades (DA-016).
        await graph.WrittenAsync(account, new DetachProperty(seeded.BookTypeId, seeded.PagesId), new DeleteNode(seeded.DeletedNodeId, 1));

        using var spa = await TestAccounts.SignedInAsync(factory, email);
        var share = await spa.PostAsync(
            "/api/shared-graph-views",
            new { rootNodeId = seeded.BookId, maxDepth = 1, nodeIds = new[] { seeded.BookId, seeded.OldBookId }, propertyIds = new[] { seeded.StatusId }, includeTags = true, legalNoticeAccepted = true });
        Assert.Equal(HttpStatusCode.Created, share.StatusCode);
        return seeded with { ShareId = (await JsonOfAsync(share)).GetProperty("id").GetGuid() };
    }

    private static JsonElement SectionOf(JsonElement export, string section) => export.GetProperty(section);

    private LifeGraphApiFactory FactoryWith(long? maxArchiveBytes = null, int? readBatchSize = null)
    {
        // The seed connects an agent, so the factory keeps the authorization settings of the default one.
        var settings = AgentAuthorization.Settings();
        if (maxArchiveBytes is { } bytes)
        {
            settings[$"{AccountExportOptions.SectionName}:{nameof(AccountExportOptions.MaxArchiveBytes)}"] = bytes.ToString(CultureInfo.InvariantCulture);
        }

        if (readBatchSize is { } size)
        {
            settings[$"{AccountExportOptions.SectionName}:{nameof(AccountExportOptions.ReadBatchSize)}"] = size.ToString(CultureInfo.InvariantCulture);
        }

        return new LifeGraphApiFactory(database, settings);
    }

    private static async Task<JsonElement> ExportJsonAsync(HttpResponseMessage download)
    {
        using var zip = new ZipArchive(await download.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken));
        using var document = await JsonDocument.ParseAsync(zip.GetEntry(AccountExportArchiveWriter.DocumentEntry)!.Open(), cancellationToken: TestContext.Current.CancellationToken);
        return document.RootElement.Clone();
    }

    // Every row of the table is in the section exactly once: no batch boundary repeats or skips one.
    private async Task AssertExportedOnceAsync(JsonElement export, string section, string idsSql, NpgsqlParameter account)
    {
        var exported = SectionOf(export, section).EnumerateArray().Select(item => item.GetProperty("id").GetGuid()).ToList();
        var stored = await database.QueryScalarAsMigratorAsync<Guid[]>(idsSql, account);
        Assert.Equal(exported.Count, exported.Distinct().Count());
        Assert.Equal(stored.Order(), exported.Order());
    }

    private static JsonElement ById(JsonElement export, string section, Guid id) =>
        export.GetProperty(section).EnumerateArray().Single(item => item.GetProperty("id").GetGuid() == id);

    private static async Task<JsonElement> JsonOfAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).RootElement.Clone();

    private async Task<List<string>> AccountTablesAsync()
    {
        var tables = await database.QueryScalarAsMigratorAsync<string[]>(
            """
            SELECT coalesce(array_agg(c.table_name::text ORDER BY c.table_name), '{}') FROM information_schema.columns c
            JOIN information_schema.tables t ON t.table_schema = c.table_schema AND t.table_name = c.table_name
            WHERE c.table_schema = 'public' AND c.column_name = 'account_id' AND t.table_type = 'BASE TABLE'
            """);
        return [.. tables];
    }

    private sealed record SeededAccount(Guid AccountId)
    {
        public Guid StatusId { get; } = Guid.CreateVersion7();

        public Guid ReadingOptionId { get; } = Guid.CreateVersion7();

        public Guid PagesId { get; } = Guid.CreateVersion7();

        public Guid BookTypeId { get; } = Guid.CreateVersion7();

        public Guid TagId { get; } = Guid.CreateVersion7();

        public Guid BookId { get; } = Guid.CreateVersion7();

        public Guid OldBookId { get; } = Guid.CreateVersion7();

        public Guid DeletedNodeId { get; } = Guid.CreateVersion7();

        public Guid ManualCollectionId { get; } = Guid.CreateVersion7();

        public Guid CriteriaCollectionId { get; } = Guid.CreateVersion7();

        public Guid ResourceId { get; } = Guid.CreateVersion7();

        public string ResourceUrl { get; } = "https://example.test/artigo";

        public Guid ShareId { get; init; }
    }
}
