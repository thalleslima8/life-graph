using System.Collections.Concurrent;
using LifeGraph.Accounts.Email;

namespace LifeGraph.IntegrationTests.Infrastructure;

/// <summary>Keeps every account e-mail in memory so tests can follow the links, as a user would from Mailpit.</summary>
public sealed class CapturingAccountMailer : IAccountMailer
{
    private readonly ConcurrentQueue<AccountEmail> _sent = new();

    public IReadOnlyList<AccountEmail> Sent => [.. _sent];

    /// <summary>When set, sending fails the way an unreachable SMTP server does.</summary>
    public bool IsFailing { get; set; }

    public Task SendAsync(AccountEmail email, CancellationToken cancellationToken)
    {
        if (IsFailing)
        {
            throw new System.Net.Mail.SmtpException("SMTP server unreachable");
        }

        _sent.Enqueue(email);
        return Task.CompletedTask;
    }

    public AccountEmail LastTo(string recipient) =>
        Sent.Last(email => string.Equals(email.To, recipient, StringComparison.OrdinalIgnoreCase));

    public int CountTo(string recipient) =>
        Sent.Count(email => string.Equals(email.To, recipient, StringComparison.OrdinalIgnoreCase));
}
