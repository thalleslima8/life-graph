using System.Net;
using System.Net.Http.Json;
using LifeGraph.Accounts;
using LifeGraph.Accounts.Http;
using LifeGraph.Accounts.RateLimiting;
using LifeGraph.IntegrationTests.Infrastructure;

namespace LifeGraph.IntegrationTests.Accounts;

public sealed class SessionTests(PostgresDatabase database) : IAsyncLifetime
{
    private const string Email = "ada@example.test";
    private static readonly TimeSpan DefaultWindow = TimeSpan.FromSeconds(900);

    private readonly LifeGraphApiFactory _factory = new(database);
    private Guid _accountId;

    public async ValueTask InitializeAsync()
    {
        await database.ResetAsync();
        _accountId = (await TestAccounts.ProvisionConfirmedAsync(_factory, Email)).AccountId;
    }

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    [Fact]
    public async Task Signing_in_sets_an_http_only_strict_secure_session_cookie()
    {
        using var spa = new SpaClient(_factory.CreateClient());

        var login = await spa.LoginAsync(Email, TestAccounts.Password);

        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        var sessionCookie = Assert.Single(login.Headers.GetValues("Set-Cookie"), cookie => cookie.StartsWith("__Host-lifegraph-session=", StringComparison.Ordinal));
        Assert.Contains("httponly", sessionCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", sessionCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", sessionCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/", sessionCookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task The_session_identifies_the_users_account()
    {
        using var spa = await TestAccounts.SignedInAsync(_factory, Email);

        var session = await spa.Http.GetFromJsonAsync<SessionResponse>("/api/sessions/current", TestContext.Current.CancellationToken);

        Assert.Equal(_accountId, session?.AccountId);
        Assert.Equal(Email, session?.Email);
    }

    [Fact]
    public async Task Without_a_session_the_api_answers_unauthorized()
    {
        using var spa = new SpaClient(_factory.CreateClient());

        var session = await spa.GetAsync("/api/sessions/current");

        Assert.Equal(HttpStatusCode.Unauthorized, session.StatusCode);
    }

    [Fact]
    public async Task Signing_out_ends_the_session()
    {
        using var spa = await TestAccounts.SignedInAsync(_factory, Email);

        var logout = await spa.LogoutAsync();
        var session = await spa.GetAsync("/api/sessions/current");

        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, session.StatusCode);
    }

    // An anonymous endpoint never answers 401 (DA-097): the SPA keeps 401 for an expired session.
    [Theory]
    [InlineData(Email, "wrong password for ada")]
    [InlineData("nobody@example.test", TestAccounts.Password)]
    public async Task Wrong_password_and_unknown_email_get_the_same_answer(string email, string password)
    {
        using var spa = new SpaClient(_factory.CreateClient());

        var login = await spa.LoginAsync(email, password);

        Assert.Equal(HttpStatusCode.BadRequest, login.StatusCode);
        Assert.Equal(AccountsErrors.InvalidCredentials.Code, await ProblemCode.ReadAsync(login));
    }

    [Theory]
    [InlineData(Email)]
    [InlineData("nobody@example.test")]
    public async Task Repeated_attempts_on_an_email_are_limited_the_same_whether_it_exists_or_not(string email)
    {
        using var spa = new SpaClient(_factory.CreateClient());
        for (var attempt = 0; attempt < 5; attempt++)
        {
            await spa.LoginAsync(email, "wrong password for ada");
        }

        var nextAttempt = await spa.LoginAsync(email, TestAccounts.Password);

        Assert.Equal(HttpStatusCode.TooManyRequests, nextAttempt.StatusCode);
        Assert.Equal(AccountsErrors.TooManyAttempts.Code, await ProblemCode.ReadAsync(nextAttempt));
        Assert.Equal(DefaultWindow, nextAttempt.Headers.RetryAfter?.Delta);
    }

    [Fact]
    public async Task The_email_budget_ignores_case_and_surrounding_spaces()
    {
        using var spa = new SpaClient(_factory.CreateClient());
        for (var attempt = 0; attempt < 5; attempt++)
        {
            await spa.LoginAsync(attempt % 2 == 0 ? " ADA@example.test" : Email, "wrong password for ada");
        }

        var nextAttempt = await spa.LoginAsync(Email, TestAccounts.Password);

        Assert.Equal(HttpStatusCode.TooManyRequests, nextAttempt.StatusCode);
    }

    // No lockout outlives the window: right after it, an existing and an unknown e-mail get
    // the same answer again, so the end of the 429 reveals nothing either.
    [Theory]
    [InlineData(Email)]
    [InlineData("nobody@example.test")]
    public async Task After_the_window_an_existing_and_an_unknown_email_answer_the_same_again(string email)
    {
        await using var oneSecondWindow = new LifeGraphApiFactory(database, new Dictionary<string, string>
        {
            ["Accounts:RateLimits:WindowSeconds"] = "1",
        });
        using var spa = new SpaClient(oneSecondWindow.CreateClient());
        for (var attempt = 0; attempt < 5; attempt++)
        {
            await spa.LoginAsync(email, "wrong password for ada");
        }

        var limited = await spa.LoginAsync(email, "wrong password for ada");
        await Task.Delay(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        var afterTheWindow = await spa.LoginAsync(email, "wrong password for ada");

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Equal(TimeSpan.FromSeconds(1), limited.Headers.RetryAfter?.Delta);
        Assert.Equal(HttpStatusCode.BadRequest, afterTheWindow.StatusCode);
        Assert.Equal(AccountsErrors.InvalidCredentials.Code, await ProblemCode.ReadAsync(afterTheWindow));
    }

    [Fact]
    public async Task After_the_window_the_user_signs_in_with_the_right_password()
    {
        await using var oneSecondWindow = new LifeGraphApiFactory(database, new Dictionary<string, string>
        {
            ["Accounts:RateLimits:WindowSeconds"] = "1",
        });
        using var spa = new SpaClient(oneSecondWindow.CreateClient());
        for (var attempt = 0; attempt < 6; attempt++)
        {
            await spa.LoginAsync(Email, "wrong password for ada");
        }

        await Task.Delay(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        var login = await spa.LoginAsync(Email, TestAccounts.Password);

        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
    }

    [Fact]
    public async Task Repeated_attempts_from_one_client_are_limited_across_emails()
    {
        await using var strictClientLimit = new LifeGraphApiFactory(database, new Dictionary<string, string>
        {
            ["Accounts:RateLimits:PerClientPermitLimit"] = "3",
        });
        using var spa = new SpaClient(strictClientLimit.CreateClient());
        for (var attempt = 0; attempt < 3; attempt++)
        {
            await spa.LoginAsync($"someone{attempt}@example.test", "wrong password");
        }

        var nextAttempt = await spa.LoginAsync(Email, TestAccounts.Password);

        Assert.Equal(HttpStatusCode.TooManyRequests, nextAttempt.StatusCode);
        Assert.Equal(DefaultWindow, nextAttempt.Headers.RetryAfter?.Delta);
    }

    [Fact]
    public async Task Missing_credentials_are_a_validation_error()
    {
        using var spa = new SpaClient(_factory.CreateClient());

        var login = await spa.PostAsync("/api/sessions", new { email = Email });

        Assert.Equal(HttpStatusCode.BadRequest, login.StatusCode);
        Assert.Equal("validation_failed", await ProblemCode.ReadAsync(login));
    }
}
