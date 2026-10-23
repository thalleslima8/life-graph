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
/// A window opens at a budget's first attempt and is read from the injected
/// <see cref="TimeProvider"/>, so a wait of the whole window always outlasts it and tests can
/// step past it (GEN-072). Ended windows are dropped once per window, so the counters stay bounded.
/// </summary>
internal sealed class CredentialAttemptLimiter
{
    public const string TooManyAttemptsMessage = "Too many attempts. Wait a few minutes and try again.";

    private const string ClientPartition = "client";
    private const string EmailPartition = "email";
    private const string UnknownClient = "unknown";

    private readonly Dictionary<string, AttemptWindow> _windows = new(StringComparer.Ordinal);
    private readonly Lock _windowsLock = new();
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _window;
    private readonly int _perClientPermitLimit;
    private readonly int _perEmailPermitLimit;
    private DateTimeOffset _nextSweepAt = DateTimeOffset.MinValue;

    public CredentialAttemptLimiter(IOptions<CredentialRateLimitOptions> options, TimeProvider timeProvider)
    {
        var limits = options.Value;
        _window = TimeSpan.FromSeconds(limits.WindowSeconds);
        _perClientPermitLimit = limits.PerClientPermitLimit;
        _perEmailPermitLimit = limits.PerEmailPermitLimit;
        _timeProvider = timeProvider;
    }

    /// <summary>
    /// Takes one attempt from the client's budget and, when given, the e-mail's. A refusal is
    /// <see cref="AccountsErrors.TooManyAttempts"/> with a wait of the whole window.
    /// </summary>
    public Result TryAcquire(CredentialAttempt attempt, HttpContext httpContext, string? email = null)
    {
        var client = httpContext.Connection.RemoteIpAddress?.ToString() ?? UnknownClient;
        if (!TryTakeOne($"{ClientPartition}:{attempt}:{client}", _perClientPermitLimit))
        {
            return Refused();
        }

        if (email is null)
        {
            return Result.Ok();
        }

        return TryTakeOne($"{EmailPartition}:{attempt}:{NormalizeEmail(email)}", _perEmailPermitLimit) ? Result.Ok() : Refused();
    }

    private bool TryTakeOne(string partitionKey, int permitLimit)
    {
        lock (_windowsLock)
        {
            var now = _timeProvider.GetUtcNow();
            DropEndedWindows(now);
            if (!_windows.TryGetValue(partitionKey, out var window) || now >= window.EndsAt)
            {
                window = new AttemptWindow(now + _window);
                _windows[partitionKey] = window;
            }

            if (window.Attempts >= permitLimit)
            {
                return false;
            }

            window.Attempts++;
            return true;
        }
    }

    private void DropEndedWindows(DateTimeOffset now)
    {
        if (now < _nextSweepAt)
        {
            return;
        }

        foreach (var (partitionKey, window) in _windows)
        {
            if (now >= window.EndsAt)
            {
                _windows.Remove(partitionKey);
            }
        }

        _nextSweepAt = now + _window;
    }

    private Result Refused() =>
        Result.Fail(AccountsErrors.TooManyAttempts.ToError(TooManyAttemptsMessage, retryAfter: WholeSecondsAtLeastOne(_window)));

    /// <summary>The header carries whole seconds, rounded up and never zero (API-030).</summary>
    internal static TimeSpan WholeSecondsAtLeastOne(TimeSpan retryAfter) =>
        TimeSpan.FromSeconds(Math.Max(1, Math.Ceiling(retryAfter.TotalSeconds)));

    // Same normalization as Identity's default lookup, so "ADA@x" and "ada@x " share a budget.
    private static string NormalizeEmail(string email) => email.Trim().ToUpperInvariant();

    private sealed class AttemptWindow(DateTimeOffset endsAt)
    {
        public DateTimeOffset EndsAt { get; } = endsAt;

        public int Attempts { get; set; }
    }
}
