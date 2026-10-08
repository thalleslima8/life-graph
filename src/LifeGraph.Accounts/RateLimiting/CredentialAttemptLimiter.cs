using System.Threading.RateLimiting;
using Limaj.Framework.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace LifeGraph.Accounts.RateLimiting;

/// <summary>
/// Fixed-window budgets keyed by client address and by normalized e-mail. It runs inside
/// the endpoint, after the body is bound, because the e-mail key lives in the body. The
/// counters are in memory: one instance in the MVP. Existing and unknown e-mails share the
/// same budget and the same answer, so the limit reveals nothing about which accounts exist.
/// A refusal carries <c>Error.RetryAfter</c>, written as <c>Retry-After</c> (API-030, DA-102);
/// it depends only on the window, never on the e-mail, so it reveals nothing either.
/// </summary>
internal sealed class CredentialAttemptLimiter : IDisposable
{
    public const string TooManyAttemptsMessage = "Too many attempts. Wait a few minutes and try again.";

    private const string ClientPartition = "client";
    private const string EmailPartition = "email";
    private const string UnknownClient = "unknown";

    private readonly PartitionedRateLimiter<string> _limiter;
    private readonly TimeSpan _window;

    public CredentialAttemptLimiter(IOptions<CredentialRateLimitOptions> options)
    {
        var limits = options.Value;
        var window = TimeSpan.FromSeconds(limits.WindowSeconds);
        _window = window;
        _limiter = PartitionedRateLimiter.Create<string, string>(key => RateLimitPartition.GetFixedWindowLimiter(
            key,
            partitionKey => new FixedWindowRateLimiterOptions
            {
                PermitLimit = partitionKey.StartsWith(EmailPartition, StringComparison.Ordinal)
                    ? limits.PerEmailPermitLimit
                    : limits.PerClientPermitLimit,
                Window = window,
                QueueLimit = 0,
                AutoReplenishment = true,
            }));
    }

    /// <summary>
    /// Takes one attempt from the client's budget and, when given, the e-mail's. A refusal is
    /// <see cref="AccountsErrors.TooManyAttempts"/> with the wait of the refused budget.
    /// </summary>
    public Result TryAcquire(CredentialAttempt attempt, HttpContext httpContext, string? email = null)
    {
        var client = httpContext.Connection.RemoteIpAddress?.ToString() ?? UnknownClient;
        using var clientLease = _limiter.AttemptAcquire($"{ClientPartition}:{attempt}:{client}");
        if (!clientLease.IsAcquired)
        {
            return Refused(clientLease);
        }

        if (email is null)
        {
            return Result.Ok();
        }

        using var emailLease = _limiter.AttemptAcquire($"{EmailPartition}:{attempt}:{NormalizeEmail(email)}");
        return emailLease.IsAcquired ? Result.Ok() : Refused(emailLease);
    }

    private Result Refused(RateLimitLease refusedLease)
    {
        var retryAfter = refusedLease.TryGetMetadata(MetadataName.RetryAfter, out var leaseRetryAfter)
            ? leaseRetryAfter
            : _window;
        return Result.Fail(AccountsErrors.TooManyAttempts.ToError(TooManyAttemptsMessage, retryAfter: WholeSecondsAtLeastOne(retryAfter)));
    }

    /// <summary>The header carries whole seconds, rounded up and never zero (API-030).</summary>
    internal static TimeSpan WholeSecondsAtLeastOne(TimeSpan retryAfter) =>
        TimeSpan.FromSeconds(Math.Max(1, Math.Ceiling(retryAfter.TotalSeconds)));

    // Same normalization as Identity's default lookup, so "ADA@x" and "ada@x " share a budget.
    private static string NormalizeEmail(string email) => email.Trim().ToUpperInvariant();

    public void Dispose() => _limiter.Dispose();
}
