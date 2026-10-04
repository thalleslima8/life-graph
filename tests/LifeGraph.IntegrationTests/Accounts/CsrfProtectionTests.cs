using System.Net;
using System.Net.Http.Json;
using LifeGraph.Accounts.Csrf;
using LifeGraph.IntegrationTests.Infrastructure;

namespace LifeGraph.IntegrationTests.Accounts;

public sealed class CsrfProtectionTests(PostgresDatabase database) : IAsyncLifetime
{
    private const string Email = "ada@example.test";

    private readonly LifeGraphApiFactory _factory = new(database);

    public async ValueTask InitializeAsync()
    {
        await database.ResetAsync();
        await TestAccounts.ProvisionConfirmedAsync(_factory, Email);
    }

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    [Fact]
    public async Task An_unsafe_request_without_the_token_is_rejected()
    {
        using var client = _factory.CreateClient();

        var login = await client.PostAsJsonAsync("/api/sessions", new { email = Email, password = TestAccounts.Password }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, login.StatusCode);
        Assert.Equal(CsrfProtection.InvalidTokenCode, await ProblemCode.ReadAsync(login));
    }

    [Fact]
    public async Task A_token_from_another_browser_is_rejected()
    {
        using var attacker = new SpaClient(_factory.CreateClient());
        using var victim = new SpaClient(_factory.CreateClient());
        var attackersToken = await attacker.GetCsrfTokenAsync();

        var login = await victim.SendAsync(HttpMethod.Post, "/api/sessions", new { email = Email, password = TestAccounts.Password }, attackersToken);

        Assert.Equal(HttpStatusCode.BadRequest, login.StatusCode);
    }

    [Fact]
    public async Task The_anonymous_token_stops_working_once_signed_in()
    {
        using var spa = new SpaClient(_factory.CreateClient());
        var anonymousToken = await spa.GetCsrfTokenAsync();
        (await spa.SendAsync(HttpMethod.Post, "/api/sessions", new { email = Email, password = TestAccounts.Password }, anonymousToken)).EnsureSuccessStatusCode();

        var logoutWithStaleToken = await spa.SendAsync(HttpMethod.Delete, "/api/sessions/current", body: null, anonymousToken);

        Assert.Equal(HttpStatusCode.BadRequest, logoutWithStaleToken.StatusCode);
    }

    [Fact]
    public async Task The_token_cookie_is_http_only_strict_and_secure()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/csrf-token", TestContext.Current.CancellationToken);

        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"), value => value.StartsWith($"__Host-{CsrfProtection.CookieBaseName}=", StringComparison.Ordinal));
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
    }
}
