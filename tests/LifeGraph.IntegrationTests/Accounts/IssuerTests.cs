using System.Net;
using System.Text.Json;
using LifeGraph.Accounts.Issuer;
using LifeGraph.Host.Operations;
using LifeGraph.IntegrationTests.Infrastructure;

namespace LifeGraph.IntegrationTests.Accounts;

/// <summary>The issuer is up and advertises what E3 will rely on, with no client registered yet.</summary>
public sealed class IssuerTests(PostgresDatabase database) : IAsyncLifetime
{
    private readonly LifeGraphApiFactory _factory = new(database);

    public async ValueTask InitializeAsync() => await database.ResetAsync();

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    [Fact]
    public async Task Discovery_document_advertises_authorization_code_with_pkce_and_refresh_tokens()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/.well-known/openid-configuration", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var discovery = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var root = discovery.RootElement;
        Assert.Equal("https://localhost/", root.GetProperty("issuer").GetString());
        Assert.Equal("https://localhost/connect/authorize", root.GetProperty("authorization_endpoint").GetString());
        Assert.Equal("https://localhost/connect/token", root.GetProperty("token_endpoint").GetString());
        Assert.Contains("S256", Strings(root, "code_challenge_methods_supported"));
        Assert.DoesNotContain("plain", Strings(root, "code_challenge_methods_supported"));
        Assert.Contains("authorization_code", Strings(root, "grant_types_supported"));
        Assert.Contains("refresh_token", Strings(root, "grant_types_supported"));
        Assert.DoesNotContain("password", Strings(root, "grant_types_supported"));
        Assert.DoesNotContain("client_credentials", Strings(root, "grant_types_supported"));
    }

    [Fact]
    public async Task The_signing_keys_are_published()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/.well-known/jwks", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var jwks = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.NotEmpty(jwks.RootElement.GetProperty("keys").EnumerateArray());
    }

    [Fact]
    public async Task A_host_with_ephemeral_keys_starts_and_is_live()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(HealthCheckExtensions.LivePath, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task A_host_without_issuer_keys_fails_to_start_with_a_clear_message()
    {
        await using var factoryWithoutKeys = new LifeGraphApiFactory(database, new Dictionary<string, string>
        {
            [$"{IssuerOptions.SectionName}:{nameof(IssuerOptions.UseEphemeralKeys)}"] = "false",
        });

        var startup = Assert.ThrowsAny<Exception>(() => factoryWithoutKeys.CreateClient());

        Assert.Contains(IssuerRegistration.MissingKeysMessage, startup.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task No_client_is_registered_yet()
    {
        Assert.Equal(0L, await database.QueryScalarAsMigratorAsync<long>("SELECT count(*) FROM oidc_applications"));
    }

    private static string?[] Strings(JsonElement root, string property) =>
        [.. root.GetProperty(property).EnumerateArray().Select(value => value.GetString())];
}
