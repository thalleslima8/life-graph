using System.Net;
using LifeGraph.IntegrationTests.Infrastructure;

namespace LifeGraph.IntegrationTests.Accounts;

/// <summary>Open sign-up is off (DA-011): accounts come only from the owner's CLI.</summary>
public sealed class OpenSignUpTests(PostgresDatabase database) : IAsyncLifetime
{
    private readonly LifeGraphApiFactory _factory = new(database);

    public async ValueTask InitializeAsync() => await database.ResetAsync();

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    [Theory]
    [InlineData("/api/accounts")]
    [InlineData("/api/users")]
    [InlineData("/api/registrations")]
    public async Task There_is_no_endpoint_that_creates_an_account(string path)
    {
        using var spa = new SpaClient(_factory.CreateClient());

        var signUp = await spa.PostAsync(path, new { email = "mallory@example.test", password = TestAccounts.Password });

        // The fallback policy answers 401 before routing can say 404; either way nothing is created.
        Assert.Contains(signUp.StatusCode, new[] { HttpStatusCode.Unauthorized, HttpStatusCode.NotFound });
        Assert.Equal(0L, await database.QueryScalarAsMigratorAsync<long>("SELECT count(*) FROM users"));
    }
}
