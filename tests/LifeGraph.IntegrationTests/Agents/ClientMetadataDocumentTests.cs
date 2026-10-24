using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using LifeGraph.Accounts.ClientMetadata;
using LifeGraph.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Authentication;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Npgsql;

namespace LifeGraph.IntegrationTests.Agents;

/// <summary>
/// Slice 2 (DA-029): a client identified by the URL of its metadata document, as Claude Code
/// connects. In production the document is fetched through the SSRF guard; here a fake
/// stands in for the network.
/// </summary>
public sealed class ClientMetadataDocumentTests : IAsyncLifetime
{
    private const string Email = "ada@example.test";
    private const string DocumentUrl = "https://code.agent.example/oauth/client-metadata.json";
    private const string RegisteredRedirect = "http://localhost/callback";

    private readonly PostgresDatabase _database;
    private readonly FakeDocuments _documents = new();
    private readonly LifeGraphApiFactory _factory;

    public ClientMetadataDocumentTests(PostgresDatabase database)
    {
        _database = database;
        var documents = _documents;
        _factory = new LifeGraphApiFactory(
            database,
            AgentAuthorization.Settings(),
            services => services.AddSingleton<IClientMetadataDocumentFetcher>(documents));
    }

    public async ValueTask InitializeAsync()
    {
        await _database.ResetAsync();
        await TestAccounts.ProvisionConfirmedAsync(_factory, Email);
    }

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    // What Claude Code does: its client_id is the document URL, its redirect a loopback port it picks.
    [Fact]
    public async Task The_mcp_sdk_client_connects_with_its_metadata_document_and_a_loopback_redirect_on_any_port()
    {
        _documents.Serve(DocumentUrl, Document(DocumentUrl, "Claude Code", RegisteredRedirect));
        using var browser = await AgentAuthorization.SignedInBrowserAsync(_factory, Email);
        using var http = _factory.CreateClient();
        var transport = new HttpClientTransport(
            new HttpClientTransportOptions
            {
                Endpoint = AgentAuthorization.McpResource,
                TransportMode = HttpTransportMode.StreamableHttp,
                OAuth = new ClientOAuthOptions
                {
                    ClientMetadataDocumentUri = new Uri(DocumentUrl),
                    RedirectUri = new Uri("http://localhost:53682/callback"),
                    AuthorizationRedirectDelegate = async (authorizationUri, _, cancellationToken) =>
                    {
                        var response = await browser.Http.GetAsync(authorizationUri, cancellationToken);
                        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
                        Assert.StartsWith("http://localhost:53682/callback", response.Headers.Location!.AbsoluteUri, StringComparison.Ordinal);
                        return QueryHelpers.ParseQuery(response.Headers.Location.Query)["code"].ToString();
                    },
                },
            },
            http,
            loggerFactory: null);

        await using var client = await McpClient.CreateAsync(transport, cancellationToken: TestContext.Current.CancellationToken);
        var whoami = await client.CallToolAsync("whoami", cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotEqual(true, whoami.IsError);
        using var result = JsonDocument.Parse(Assert.IsType<TextContentBlock>(whoami.Content[0]).Text);
        Assert.Equal("Claude Code", result.RootElement.GetProperty("name").GetString());
        Assert.Equal(1L, await _database.QueryScalarAsMigratorAsync<long>(
            "SELECT count(*) FROM agent_identities WHERE client_id = @client", new NpgsqlParameter("client", DocumentUrl)));
    }

    [Fact]
    public async Task A_document_that_names_another_client_is_refused_without_redirecting()
    {
        _documents.Serve(DocumentUrl, Document("https://elsewhere.example/client.json", "Copy", RegisteredRedirect));

        var response = await AuthorizeAsync("http://localhost:4000/callback");

        AssertRefusedUnredirected(response);
        Assert.Equal(0L, await _database.QueryScalarAsMigratorAsync<long>(
            "SELECT count(*) FROM oidc_applications WHERE client_id = @client", new NpgsqlParameter("client", DocumentUrl)));
    }

    [Fact]
    public async Task A_document_that_cannot_be_fetched_is_refused_without_redirecting()
    {
        var response = await AuthorizeAsync("http://localhost:4000/callback");

        AssertRefusedUnredirected(response);
    }

    [Fact]
    public async Task A_redirect_the_document_does_not_list_is_refused_without_redirecting()
    {
        _documents.Serve(DocumentUrl, Document(DocumentUrl, "Claude Code", RegisteredRedirect));

        var response = await AuthorizeAsync("http://localhost:4000/elsewhere");

        AssertRefusedUnredirected(response);
    }

    // The document is reused for its max-age, within bounds, then fetched again; a document
    // that stops validating stops the client, even though an older copy was registered.
    [Fact]
    public async Task The_document_is_cached_for_its_max_age_and_checked_again_after_it()
    {
        _documents.Serve(DocumentUrl, Document(DocumentUrl, "Claude Code", RegisteredRedirect), maxAge: TimeSpan.FromMinutes(10));

        AgentAuthorization.CodeFrom(await AuthorizeAsync("http://localhost:4000/callback"));
        AgentAuthorization.CodeFrom(await AuthorizeAsync("http://localhost:4001/callback"));
        Assert.Equal(1, _documents.Fetches(DocumentUrl));

        _documents.Serve(DocumentUrl, "{ \"not\": \"a client\" }");
        _factory.Clock.Advance(TimeSpan.FromMinutes(11));

        AssertRefusedUnredirected(await AuthorizeAsync("http://localhost:4000/callback"));
        Assert.Equal(2, _documents.Fetches(DocumentUrl));
    }

    private async Task<HttpResponseMessage> AuthorizeAsync(string redirectUri)
    {
        using var browser = await AgentAuthorization.SignedInBrowserAsync(_factory, Email);
        return await browser.Http.GetAsync(
            AgentAuthorization.AuthorizePath(AgentAuthorization.NewPkce(), clientId: DocumentUrl, redirectUri: redirectUri),
            TestContext.Current.CancellationToken);
    }

    private static void AssertRefusedUnredirected(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(response.Headers.Location);
    }

    private static string Document(string clientId, string clientName, params string[] redirectUris) =>
        JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["client_id"] = clientId,
            ["client_name"] = clientName,
            ["redirect_uris"] = redirectUris,
            ["grant_types"] = new[] { "authorization_code", "refresh_token" },
            ["response_types"] = new[] { "code" },
            ["token_endpoint_auth_method"] = "none",
        });

    private sealed class FakeDocuments : IClientMetadataDocumentFetcher
    {
        private readonly ConcurrentDictionary<string, FetchedDocument> _served = new(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, int> _fetches = new(StringComparer.Ordinal);

        public void Serve(string url, string json, TimeSpan? maxAge = null) =>
            _served[url] = new FetchedDocument(Encoding.UTF8.GetBytes(json), maxAge);

        public int Fetches(string url) => _fetches.GetValueOrDefault(url);

        public Task<FetchedDocument?> FetchAsync(Uri url, CancellationToken cancellationToken)
        {
            _fetches.AddOrUpdate(url.OriginalString, 1, (_, count) => count + 1);
            return Task.FromResult(_served.GetValueOrDefault(url.OriginalString));
        }
    }
}
