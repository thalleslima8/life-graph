namespace LifeGraph.Accounts.Email;

/// <summary>Port for sending the account e-mails (BE-033). Dev uses SMTP to Mailpit; real delivery comes in E14.</summary>
public interface IAccountMailer
{
    Task SendAsync(AccountEmail email, CancellationToken cancellationToken);
}
