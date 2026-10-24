using System.Net;
using LifeGraph.Accounts;
using LifeGraph.Accounts.Provisioning;
using LifeGraph.Accounts.RateLimiting;
using LifeGraph.Infrastructure.Persistence;
using Limaj.Framework.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace LifeGraph.UnitTests.Accounts;

public sealed class CredentialAttemptLimiterTests
{
    private const string Email = "ada@example.test";
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(900);

    private readonly SteppedClock _clock = new();

    [Fact]
    public void The_sixth_attempt_on_one_email_is_refused_with_a_wait_of_the_whole_window()
    {
        var limiter = LimiterWith(perEmail: 5);
        var client = ClientAt("10.0.0.1");

        for (var attempt = 0; attempt < 5; attempt++)
        {
            Assert.True(limiter.TryAcquire(CredentialAttempt.SignIn, client, Email).IsSuccess);
        }

        var refused = limiter.TryAcquire(CredentialAttempt.SignIn, client, Email);

        Assert.Equal(AccountsErrors.TooManyAttempts.Code, refused.Error!.Code);
        Assert.Equal(Window, refused.Error!.RetryAfter);
    }

    [Fact]
    public void The_wait_does_not_shrink_while_the_window_runs()
    {
        var limiter = LimiterWith(perEmail: 1);
        var client = ClientAt("10.0.0.1");
        limiter.TryAcquire(CredentialAttempt.SignIn, client, Email);

        _clock.Advance(TimeSpan.FromSeconds(600));
        var refused = limiter.TryAcquire(CredentialAttempt.SignIn, client, Email);

        Assert.Equal(Window, refused.Error!.RetryAfter);
    }

    [Fact]
    public void Once_the_window_has_passed_the_budget_is_whole_again()
    {
        var limiter = LimiterWith(perEmail: 5);
        var client = ClientAt("10.0.0.1");
        for (var attempt = 0; attempt < 6; attempt++)
        {
            limiter.TryAcquire(CredentialAttempt.SignIn, client, Email);
        }

        _clock.Advance(Window - TimeSpan.FromSeconds(1));
        var stillInTheWindow = limiter.TryAcquire(CredentialAttempt.SignIn, client, Email);
        _clock.Advance(TimeSpan.FromSeconds(1));
        var afterTheWindow = Enumerable.Range(0, 5).Select(_ => limiter.TryAcquire(CredentialAttempt.SignIn, client, Email)).ToList();
        var sixthAfterTheWindow = limiter.TryAcquire(CredentialAttempt.SignIn, client, Email);

        Assert.False(stillInTheWindow.IsSuccess);
        Assert.All(afterTheWindow, result => Assert.True(result.IsSuccess));
        Assert.False(sixthAfterTheWindow.IsSuccess);
    }

    [Fact]
    public void Each_email_has_its_own_budget_and_ignores_case_and_surrounding_spaces()
    {
        var limiter = LimiterWith(perEmail: 2);
        var client = ClientAt("10.0.0.1");
        limiter.TryAcquire(CredentialAttempt.SignIn, client, " ADA@example.test");
        limiter.TryAcquire(CredentialAttempt.SignIn, client, Email);

        Assert.False(limiter.TryAcquire(CredentialAttempt.SignIn, client, Email).IsSuccess);
        Assert.True(limiter.TryAcquire(CredentialAttempt.SignIn, client, "nobody@example.test").IsSuccess);
    }

    [Fact]
    public void The_client_budget_spans_emails_and_each_client_has_its_own()
    {
        var limiter = LimiterWith(perClient: 3);
        var client = ClientAt("10.0.0.1");
        for (var attempt = 0; attempt < 3; attempt++)
        {
            limiter.TryAcquire(CredentialAttempt.SignIn, client, $"person{attempt}@example.test");
        }

        var refused = limiter.TryAcquire(CredentialAttempt.SignIn, client, "another@example.test");

        Assert.Equal(AccountsErrors.TooManyAttempts.Code, refused.Error!.Code);
        Assert.Equal(Window, refused.Error!.RetryAfter);
        Assert.True(limiter.TryAcquire(CredentialAttempt.SignIn, ClientAt("10.0.0.2"), "another@example.test").IsSuccess);
    }

    [Fact]
    public void Each_kind_of_attempt_has_its_own_budget()
    {
        var limiter = LimiterWith(perEmail: 1);
        var client = ClientAt("10.0.0.1");
        limiter.TryAcquire(CredentialAttempt.SignIn, client, Email);

        Assert.False(limiter.TryAcquire(CredentialAttempt.SignIn, client, Email).IsSuccess);
        Assert.True(limiter.TryAcquire(CredentialAttempt.PasswordResetRequest, client, Email).IsSuccess);
    }

    // The Retry-After rounding of E1, kept by the move to Error.RetryAfter (DA-102).
    [Theory]
    [InlineData(0, 1)]
    [InlineData(0.2, 1)]
    [InlineData(1, 1)]
    [InlineData(1.5, 2)]
    [InlineData(899.1, 900)]
    [InlineData(900, 900)]
    public void The_wait_is_whole_seconds_rounded_up_and_never_zero(double seconds, int expectedSeconds) =>
        Assert.Equal(
            TimeSpan.FromSeconds(expectedSeconds),
            CredentialAttemptLimiter.WholeSecondsAtLeastOne(TimeSpan.FromSeconds(seconds)));

    [Fact]
    public void Only_the_accounts_command_connects_as_the_provisioning_role()
    {
        Assert.Equal(
            PersistenceServiceCollectionExtensions.ProvisioningConnectionStringName,
            AccountsCommandLine.ConnectionStringNameFor(["accounts", "create", "--email", "ada@example.test"]));
        Assert.Equal(PersistenceServiceCollectionExtensions.ConnectionStringName, AccountsCommandLine.ConnectionStringNameFor([]));
        Assert.Equal(PersistenceServiceCollectionExtensions.ConnectionStringName, AccountsCommandLine.ConnectionStringNameFor(["--urls", "http://+:5000"]));
    }

    private CredentialAttemptLimiter LimiterWith(int perEmail = 5, int perClient = 20) =>
        new(
            Options.Create(new CredentialRateLimitOptions
            {
                PerEmailPermitLimit = perEmail,
                PerClientPermitLimit = perClient,
                WindowSeconds = (int)Window.TotalSeconds,
            }),
            _clock);

    private static DefaultHttpContext ClientAt(string address) =>
        new() { Connection = { RemoteIpAddress = IPAddress.Parse(address) } };

    private sealed class SteppedClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 10, 23, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }
}
