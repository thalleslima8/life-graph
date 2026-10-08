using System.Net.Mail;
using Microsoft.Extensions.Options;

namespace LifeGraph.Accounts.Email;

/// <summary>
/// Plain SMTP without authentication, enough for Mailpit in dev. E13 replaces it with the
/// production provider behind the same port.
/// </summary>
internal sealed class SmtpAccountMailer(IOptions<SmtpOptions> options) : IAccountMailer
{
    public async Task SendAsync(AccountEmail email, CancellationToken cancellationToken)
    {
        var smtp = options.Value;
        using var client = new SmtpClient(smtp.Host, smtp.Port)
        {
            Timeout = (int)TimeSpan.FromSeconds(smtp.TimeoutSeconds).TotalMilliseconds,
        };
        using var message = new MailMessage(smtp.From, email.To, email.Subject, email.TextBody);

        await client.SendMailAsync(message, cancellationToken);
    }
}
