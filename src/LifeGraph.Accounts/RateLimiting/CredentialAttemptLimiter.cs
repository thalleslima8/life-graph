using System.Globalization;
using System.Threading.RateLimiting;
using LifeGraph.Accounts.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;

namespace LifeGraph.Accounts.RateLimiting;

/// <summary>
/// Fixed-window budgets keyed by client address and by normalized e-mail. It runs inside
/// the endpoint, after the body is bound, because the e-mail key lives in the body. The
/// counters are in memory: one instance in the MVP. Existing and unknown e-mails share the
/// same budget and the same answer, so the limit reveals nothing about which accounts exist.
/// A refusal sets <c>Retry-After</c> (API-030); it depends only on the window, never on the
/// e-mail, so it reveals nothing either.
/// </summary>
internal sealed class CredentialAttemptLimiter : IDisposable
{
    public const string TooManyAttemptsCode = "too_many_attempts";

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

    public static ProblemHttpResult TooManyAttempts() =>
        ApiProblems.Create(
            StatusCodes.Status429TooManyRequests,
            TooManyAttemptsCode,
            "Too many attempts",
            "Wait a few minutes and try again.");

    /// <summary>
    /// Takes one attempt from the client's budget and, when given, the e-mail's. On refusal it
    /// sets the response's <c>Retry-After</c>, so the caller only returns <see cref="TooManyAttempts"/>.
    /// </summary>
    public bool TryAcquire(CredentialAttempt attempt, HttpContext httpContext, string? email = null)
    {
        var client = httpContext.Connection.RemoteIpAddress?.ToString() ?? UnknownClient;
        using var clientLease = _limiter.AttemptAcquire($"{ClientPartition}:{attempt}:{client}");
        if (!clientLease.IsAcquired)
        {
            SetRetryAfter(httpContext, clientLease);
            return false;
        }

        if (email is null)
        {
            return true;
        }

        using var emailLease = _limiter.AttemptAcquire($"{EmailPartition}:{attempt}:{NormalizeEmail(email)}");
        if (!emailLease.IsAcquired)
        {
            SetRetryAfter(httpContext, emailLease);
            return false;
        }

        return true;
    }

    private void SetRetryAfter(HttpContext httpContext, RateLimitLease refusedLease)
    {
        var retryAfter = refusedLease.TryGetMetadata(MetadataName.RetryAfter, out var leaseRetryAfter)
            ? leaseRetryAfter
            : _window;
        var seconds = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds));
        httpContext.Response.Headers.RetryAfter = seconds.ToString(CultureInfo.InvariantCulture);
    }

    // Same normalization as Identity's default lookup, so "ADA@x" and "ada@x " share a budget.
    private static string NormalizeEmail(string email) => email.Trim().ToUpperInvariant();

    public void Dispose() => _limiter.Dispose();
}
