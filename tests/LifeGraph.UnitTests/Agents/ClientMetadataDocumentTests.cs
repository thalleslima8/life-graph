using System.Text;
using LifeGraph.Accounts.ClientMetadata;

namespace LifeGraph.UnitTests.Agents;

/// <summary>DA-029: a client identified by the URL of its metadata document (CIMD), public and PKCE only.</summary>
public sealed class ClientMetadataDocumentTests
{
    private const string ClientId = "https://claude.ai/oauth/claude-code-client-metadata";

    [Theory]
    [InlineData(ClientId, true)]
    [InlineData("https://agent.example.com/client.json?v=2", true)]
    [InlineData("https://agent.example.com/", false)]
    [InlineData("https://agent.example.com", false)]
    [InlineData("http://agent.example.com/client.json", false)]
    [InlineData("https://agent.example.com/client.json#x", false)]
    [InlineData("https://user@agent.example.com/client.json", false)]
    [InlineData("https://agent.example.com/a/../client.json", false)]
    [InlineData("https://agent.example.com/./client.json", false)]
    [InlineData("https://127.0.0.1/client.json", false)]
    [InlineData("https://[::1]/client.json", false)]
    [InlineData("https://10.0.0.8/client.json", false)]
    [InlineData("claude-ai", false)]
    [InlineData(null, false)]
    public void Only_an_https_url_with_a_path_to_a_public_host_names_a_document(string? clientId, bool expected) =>
        Assert.Equal(expected, ClientMetadataDocument.IsMetadataDocumentClientId(clientId));

    [Fact]
    public void A_client_id_over_the_limit_is_not_a_document() =>
        Assert.False(ClientMetadataDocument.IsMetadataDocumentClientId("https://agent.example.com/" + new string('a', ClientMetadataDocument.ClientIdMaxLength)));

    [Fact]
    public void A_valid_document_gives_the_name_and_the_redirects()
    {
        var (document, problem) = Parse($$"""
            {
              "client_id": "{{ClientId}}",
              "client_name": "Claude Code",
              "redirect_uris": ["http://localhost/callback", "http://127.0.0.1/callback"],
              "grant_types": ["authorization_code", "refresh_token"],
              "response_types": ["code"],
              "token_endpoint_auth_method": "none"
            }
            """);

        Assert.Null(problem);
        Assert.Equal("Claude Code", document!.ClientName);
        Assert.Equal([new Uri("http://localhost/callback"), new Uri("http://127.0.0.1/callback")], document.RedirectUris);
    }

    [Theory]
    [InlineData("""{"client_id": "https://evil.example/client.json", "redirect_uris": ["https://evil.example/cb"]}""")]
    [InlineData("""{"redirect_uris": ["https://claude.ai/cb"]}""")]
    [InlineData("""{"client_id": "CLIENT", "redirect_uris": []}""")]
    [InlineData("""{"client_id": "CLIENT"}""")]
    [InlineData("""{"client_id": "CLIENT", "redirect_uris": ["http://claude.ai/cb"]}""")]
    [InlineData("""{"client_id": "CLIENT", "redirect_uris": ["https://claude.ai/cb#f"]}""")]
    [InlineData("""{"client_id": "CLIENT", "redirect_uris": ["https://claude.ai/cb"], "client_secret": "s"}""")]
    [InlineData("""{"client_id": "CLIENT", "redirect_uris": ["https://claude.ai/cb"], "token_endpoint_auth_method": "client_secret_basic"}""")]
    [InlineData("""{"client_id": "CLIENT", "redirect_uris": ["https://claude.ai/cb"], "grant_types": ["client_credentials"]}""")]
    [InlineData("""{"client_id": "CLIENT", "redirect_uris": ["https://claude.ai/cb"], "response_types": ["token"]}""")]
    [InlineData("""["CLIENT"]""")]
    [InlineData("""not json""")]
    public void An_invalid_document_is_refused_with_a_reason(string json)
    {
        var (document, problem) = Parse(json.Replace("CLIENT", ClientId, StringComparison.Ordinal));

        Assert.Null(document);
        Assert.NotNull(problem);
    }

    [Fact]
    public void More_redirects_than_the_limit_are_refused()
    {
        var redirects = string.Join(',', Enumerable.Range(0, ClientMetadataDocument.MaxRedirectUris + 1).Select(index => $"\"https://claude.ai/cb{index}\""));

        Assert.Null(Parse($$"""{"client_id": "{{ClientId}}", "redirect_uris": [{{redirects}}]}""").Document);
    }

    private static (ClientMetadataDocument? Document, string? Problem) Parse(string json) =>
        ClientMetadataDocument.Parse(ClientId, Encoding.UTF8.GetBytes(json));
}
