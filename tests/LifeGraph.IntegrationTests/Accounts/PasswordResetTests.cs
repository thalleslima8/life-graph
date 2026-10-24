using System.Net;
using LifeGraph.Accounts;
using LifeGraph.Accounts.Email;
using LifeGraph.Accounts.RateLimiting;
using LifeGraph.IntegrationTests.Infrastructure;

namespace LifeGraph.IntegrationTests.Accounts;

public sealed class PasswordResetTests(PostgresDatabase database) : IAsyncLifetime
{
    private const string Email = "ada@example.test";
    private const string NewPassword = "a brand new long passphrase";

    private readonly LifeGraphApiFactory _factory = new(database);

    public async ValueTask InitializeAsync()
    {
        await database.ResetAsync();
        await TestAccounts.ProvisionConfirmedAsync(_factory, Email);
    }

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    [Fact]
    public async Task Resetting_through_the_emailed_link_replaces_the_password()
    {
        using var spa = new SpaClient(_factory.CreateClient());

        var request = await spa.PostAsync("/api/password-resets", new { email = Email });
        var link = EmailedLink.Parse(_factory.Mailer.LastTo(Email));
        var completion = await spa.CompletePasswordResetAsync(link, NewPassword);

        Assert.Equal(HttpStatusCode.Accepted, request.StatusCode);
        Assert.Contains($"/{AccountEmailLinks.PasswordResetPath}#", link.Uri.ToString(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.NoContent, completion.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await spa.LoginAsync(Email, TestAccounts.Password)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await spa.LoginAsync(Email, NewPassword)).StatusCode);
    }

    [Fact]
    public async Task An_unknown_email_gets_the_same_answer_and_no_email_is_sent()
    {
        using var spa = new SpaClient(_factory.CreateClient());
        var sentBefore = _factory.Mailer.Sent.Count;

        var request = await spa.PostAsync("/api/password-resets", new { email = "nobody@example.test" });

        Assert.Equal(HttpStatusCode.Accepted, request.StatusCode);
        Assert.Equal(sentBefore, _factory.Mailer.Sent.Count);
    }

    [Fact]
    public async Task A_new_password_below_the_policy_is_explained()
    {
        using var spa = new SpaClient(_factory.CreateClient());
        await spa.PostAsync("/api/password-resets", new { email = Email });
        var link = EmailedLink.Parse(_factory.Mailer.LastTo(Email));

        var completion = await spa.CompletePasswordResetAsync(link, "short");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, completion.StatusCode);
        Assert.Equal(AccountsErrors.PasswordRejected.Code, await ProblemCode.ReadAsync(completion));
    }

    [Fact]
    public async Task A_used_link_cannot_be_used_again()
    {
        using var spa = new SpaClient(_factory.CreateClient());
        await spa.PostAsync("/api/password-resets", new { email = Email });
        var link = EmailedLink.Parse(_factory.Mailer.LastTo(Email));
        await spa.CompletePasswordResetAsync(link, NewPassword);

        var reuse = await spa.CompletePasswordResetAsync(link, "yet another long passphrase");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, reuse.StatusCode);
        Assert.Equal(AccountsErrors.InvalidOrExpiredToken.Code, await ProblemCode.ReadAsync(reuse));
    }

    [Fact]
    public async Task An_unknown_user_gets_the_same_answer_as_a_bad_token()
    {
        using var spa = new SpaClient(_factory.CreateClient());
        await spa.PostAsync("/api/password-resets", new { email = Email });
        var link = EmailedLink.Parse(_factory.Mailer.LastTo(Email));

        var completion = await spa.CompletePasswordResetAsync(link with { UserId = Guid.CreateVersion7() }, NewPassword);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, completion.StatusCode);
        Assert.Equal(AccountsErrors.InvalidOrExpiredToken.Code, await ProblemCode.ReadAsync(completion));
    }

    // The credential directory has no RLS (DA-098): the link must never act on, or reveal, another user.
    [Fact]
    public async Task A_link_sent_with_another_users_id_gets_the_same_answer_as_a_bad_token()
    {
        const string otherEmail = "grace@example.test";
        await TestAccounts.ProvisionConfirmedAsync(_factory, otherEmail);
        var otherUserId = EmailedLink.Parse(_factory.Mailer.LastTo(otherEmail)).UserId;
        using var spa = new SpaClient(_factory.CreateClient());
        await spa.PostAsync("/api/password-resets", new { email = Email });
        var link = EmailedLink.Parse(_factory.Mailer.LastTo(Email));

        var completion = await spa.CompletePasswordResetAsync(link with { UserId = otherUserId }, NewPassword);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, completion.StatusCode);
        Assert.Equal(AccountsErrors.InvalidOrExpiredToken.Code, await ProblemCode.ReadAsync(completion));
        Assert.Equal(HttpStatusCode.NoContent, (await spa.LoginAsync(otherEmail, TestAccounts.Password)).StatusCode);
    }

    [Fact]
    public async Task Resetting_the_password_ends_every_open_session_of_the_account()
    {
        using var otherBrowser = await TestAccounts.SignedInAsync(_factory, Email);
        using var spa = new SpaClient(_factory.CreateClient());
        await spa.PostAsync("/api/password-resets", new { email = Email });

        (await spa.CompletePasswordResetAsync(EmailedLink.Parse(_factory.Mailer.LastTo(Email)), NewPassword)).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.Unauthorized, (await otherBrowser.GetAsync("/api/sessions/current")).StatusCode);
    }

    [Fact]
    public async Task A_failing_mailer_still_gets_the_neutral_answer()
    {
        using var spa = new SpaClient(_factory.CreateClient());
        _factory.Mailer.IsFailing = true;

        var request = await spa.PostAsync("/api/password-resets", new { email = Email });

        Assert.Equal(HttpStatusCode.Accepted, request.StatusCode);
    }

    [Fact]
    public async Task A_pending_account_gets_the_neutral_answer_and_no_reset_email()
    {
        const string pendingEmail = "grace@example.test";
        await TestAccounts.ProvisionAsync(_factory, pendingEmail);
        using var spa = new SpaClient(_factory.CreateClient());

        var request = await spa.PostAsync("/api/password-resets", new { email = pendingEmail });

        Assert.Equal(HttpStatusCode.Accepted, request.StatusCode);
        Assert.Equal(1, _factory.Mailer.CountTo(pendingEmail));
    }

    [Theory]
    [InlineData(Email)]
    [InlineData("nobody@example.test")]
    public async Task Repeated_requests_for_an_email_are_limited_the_same_whether_it_exists_or_not(string email)
    {
        using var spa = new SpaClient(_factory.CreateClient());
        for (var attempt = 0; attempt < 5; attempt++)
        {
            (await spa.PostAsync("/api/password-resets", new { email })).EnsureSuccessStatusCode();
        }

        var nextRequest = await spa.PostAsync("/api/password-resets", new { email });

        Assert.Equal(HttpStatusCode.TooManyRequests, nextRequest.StatusCode);
        Assert.Equal(AccountsErrors.TooManyAttempts.Code, await ProblemCode.ReadAsync(nextRequest));
        Assert.Equal(TimeSpan.FromSeconds(900), nextRequest.Headers.RetryAfter?.Delta);
    }

    [Fact]
    public async Task Repeated_link_redemptions_from_one_client_are_limited()
    {
        await using var strictClientLimit = new LifeGraphApiFactory(database, new Dictionary<string, string>
        {
            ["Accounts:RateLimits:PerClientPermitLimit"] = "3",
        });
        using var spa = new SpaClient(strictClientLimit.CreateClient());
        var guessedLink = new EmailedLink(new Uri("https://localhost/"), Guid.CreateVersion7(), "guessed-token");
        for (var attempt = 0; attempt < 3; attempt++)
        {
            await spa.CompletePasswordResetAsync(guessedLink, NewPassword);
        }

        var nextAttempt = await spa.CompletePasswordResetAsync(guessedLink, NewPassword);

        Assert.Equal(HttpStatusCode.TooManyRequests, nextAttempt.StatusCode);
        Assert.Equal(AccountsErrors.TooManyAttempts.Code, await ProblemCode.ReadAsync(nextAttempt));
        Assert.Equal(TimeSpan.FromSeconds(900), nextAttempt.Headers.RetryAfter?.Delta);
    }
}
