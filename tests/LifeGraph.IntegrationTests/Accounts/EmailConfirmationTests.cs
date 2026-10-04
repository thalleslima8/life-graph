using System.Net;
using LifeGraph.Accounts.Email;
using LifeGraph.Accounts.Http;
using LifeGraph.Accounts.RateLimiting;
using LifeGraph.Infrastructure.Identity;
using LifeGraph.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace LifeGraph.IntegrationTests.Accounts;

/// <summary>The e-mailed link of a provisioned account confirms the e-mail and sets the first password (DA-095).</summary>
public sealed class EmailConfirmationTests(PostgresDatabase database) : IAsyncLifetime
{
    private const string Email = "grace@example.test";

    private readonly LifeGraphApiFactory _factory = new(database);

    public async ValueTask InitializeAsync()
    {
        await database.ResetAsync();
        await TestAccounts.ProvisionAsync(_factory, Email);
    }

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    [Fact]
    public async Task Sign_in_is_refused_before_the_password_is_set()
    {
        using var spa = new SpaClient(_factory.CreateClient());

        var beforeConfirmation = await spa.LoginAsync(Email, TestAccounts.Password);

        Assert.Equal(HttpStatusCode.BadRequest, beforeConfirmation.StatusCode);
    }

    [Fact]
    public async Task Following_the_link_sets_the_password_confirms_the_email_and_allows_sign_in()
    {
        using var spa = new SpaClient(_factory.CreateClient());
        var link = EmailedLink.Parse(_factory.Mailer.LastTo(Email));

        var confirmation = await spa.ConfirmEmailAsync(link, TestAccounts.Password);

        Assert.Equal(HttpStatusCode.NoContent, confirmation.StatusCode);
        Assert.True(await database.QueryScalarAsMigratorAsync<bool>(
            "SELECT email_confirmed FROM users WHERE id = @id",
            new NpgsqlParameter("id", link.UserId)));
        Assert.Equal(HttpStatusCode.Unauthorized, (await spa.GetAsync("/api/sessions/current")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await spa.LoginAsync(Email, TestAccounts.Password)).StatusCode);
    }

    [Fact]
    public async Task A_used_link_cannot_be_used_again()
    {
        using var spa = new SpaClient(_factory.CreateClient());
        var link = EmailedLink.Parse(_factory.Mailer.LastTo(Email));
        (await spa.ConfirmEmailAsync(link, TestAccounts.Password)).EnsureSuccessStatusCode();

        var reuse = await spa.ConfirmEmailAsync(link, "a password chosen by someone else");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, reuse.StatusCode);
        Assert.Equal(CredentialProblems.InvalidOrExpiredTokenCode, await ProblemCode.ReadAsync(reuse));
        Assert.Equal(HttpStatusCode.NoContent, (await spa.LoginAsync(Email, TestAccounts.Password)).StatusCode);
    }

    [Fact]
    public async Task A_password_below_the_policy_is_explained_and_the_link_stays_usable()
    {
        using var spa = new SpaClient(_factory.CreateClient());
        var link = EmailedLink.Parse(_factory.Mailer.LastTo(Email));

        var rejected = await spa.ConfirmEmailAsync(link, "short");
        var retry = await spa.ConfirmEmailAsync(link, TestAccounts.Password);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, rejected.StatusCode);
        Assert.Equal(CredentialProblems.PasswordRejectedCode, await ProblemCode.ReadAsync(rejected));
        Assert.Equal(HttpStatusCode.NoContent, retry.StatusCode);
    }

    [Fact]
    public async Task A_tampered_token_is_rejected()
    {
        using var spa = new SpaClient(_factory.CreateClient());
        var link = EmailedLink.Parse(_factory.Mailer.LastTo(Email));

        var confirmation = await spa.ConfirmEmailAsync(link with { Token = link.Token + "AA" }, TestAccounts.Password);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, confirmation.StatusCode);
        Assert.Equal(CredentialProblems.InvalidOrExpiredTokenCode, await ProblemCode.ReadAsync(confirmation));
    }

    [Fact]
    public async Task An_unknown_user_gets_the_same_answer_as_a_bad_token()
    {
        using var spa = new SpaClient(_factory.CreateClient());
        var link = EmailedLink.Parse(_factory.Mailer.LastTo(Email));

        var confirmation = await spa.ConfirmEmailAsync(link with { UserId = Guid.CreateVersion7() }, TestAccounts.Password);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, confirmation.StatusCode);
        Assert.Equal(CredentialProblems.InvalidOrExpiredTokenCode, await ProblemCode.ReadAsync(confirmation));
    }

    // The credential directory has no RLS (DA-098): the link must never act on, or reveal, another user.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_link_sent_with_another_users_id_gets_the_same_answer_as_a_bad_token(bool otherUserIsConfirmed)
    {
        const string otherEmail = "ada@example.test";
        if (otherUserIsConfirmed)
        {
            await TestAccounts.ProvisionConfirmedAsync(_factory, otherEmail);
        }
        else
        {
            await TestAccounts.ProvisionAsync(_factory, otherEmail);
        }

        var otherUserId = EmailedLink.Parse(_factory.Mailer.LastTo(otherEmail)).UserId;
        var link = EmailedLink.Parse(_factory.Mailer.LastTo(Email));
        using var spa = new SpaClient(_factory.CreateClient());

        var confirmation = await spa.ConfirmEmailAsync(link with { UserId = otherUserId }, TestAccounts.Password);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, confirmation.StatusCode);
        Assert.Equal(CredentialProblems.InvalidOrExpiredTokenCode, await ProblemCode.ReadAsync(confirmation));
    }

    [Fact]
    public async Task A_password_reset_token_cannot_stand_in_for_the_confirmation_token()
    {
        var link = EmailedLink.Parse(_factory.Mailer.LastTo(Email));
        string resetToken;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<LifeGraphUser>>();
            var user = await userManager.FindByIdAsync(link.UserId.ToString());
            resetToken = AccountEmailLinks.EncodeToken(await userManager.GeneratePasswordResetTokenAsync(user!));
        }

        using var spa = new SpaClient(_factory.CreateClient());

        var confirmation = await spa.ConfirmEmailAsync(link with { Token = resetToken }, TestAccounts.Password);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, confirmation.StatusCode);
        Assert.Equal(CredentialProblems.InvalidOrExpiredTokenCode, await ProblemCode.ReadAsync(confirmation));
    }

    [Fact]
    public async Task Repeated_link_redemptions_from_one_client_are_limited()
    {
        await using var strictClientLimit = new LifeGraphApiFactory(database, new Dictionary<string, string>
        {
            ["Accounts:RateLimits:PerClientPermitLimit"] = "3",
        });
        using var spa = new SpaClient(strictClientLimit.CreateClient());
        var link = EmailedLink.Parse(_factory.Mailer.LastTo(Email));
        var guessedLink = link with { Token = "guessed-token" };
        for (var attempt = 0; attempt < 3; attempt++)
        {
            await spa.ConfirmEmailAsync(guessedLink, TestAccounts.Password);
        }

        var withTheRealLink = await spa.ConfirmEmailAsync(link, TestAccounts.Password);

        Assert.Equal(HttpStatusCode.TooManyRequests, withTheRealLink.StatusCode);
        Assert.Equal(CredentialAttemptLimiter.TooManyAttemptsCode, await ProblemCode.ReadAsync(withTheRealLink));
        Assert.Equal(TimeSpan.FromSeconds(900), withTheRealLink.Headers.RetryAfter?.Delta);
    }

    [Fact]
    public async Task Missing_password_is_a_validation_error()
    {
        using var spa = new SpaClient(_factory.CreateClient());
        var link = EmailedLink.Parse(_factory.Mailer.LastTo(Email));

        var confirmation = await spa.PostAsync("/api/email-confirmations", new { userId = link.UserId, token = link.Token });

        Assert.Equal(HttpStatusCode.BadRequest, confirmation.StatusCode);
        Assert.Equal("validation_failed", await ProblemCode.ReadAsync(confirmation));
    }
}
