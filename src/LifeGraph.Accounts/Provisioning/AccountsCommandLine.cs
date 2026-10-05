using LifeGraph.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace LifeGraph.Accounts.Provisioning;

/// <summary>
/// The owner's way to create accounts while open sign-up is off (DA-011, DA-095). It runs
/// on the host, as the provisioning database role (DA-107), and has no HTTP surface:
/// <code>
/// ConnectionStrings__Provisioning=... dotnet run --project src/LifeGraph.Host -- accounts create --email ada@example.com
/// dotnet run --project src/LifeGraph.Host -- accounts resend --email ada@example.com
/// </code>
/// It takes no password: the user chooses it through the e-mailed link. The link itself is
/// never printed, so the owner never holds another person's credential.
/// </summary>
public static class AccountsCommandLine
{
    public const string CommandName = "accounts";

    public const int Success = 0;
    public const int Failure = 1;
    public const int UsageError = 2;

    private const string Usage = """
        Usage:
          accounts create --email <email>   Create a pending account and e-mail the set-password link.
          accounts resend --email <email>   E-mail a new set-password link to a pending account.
        """;

    public static bool IsInvocation(IReadOnlyList<string> args) =>
        args.Count > 0 && string.Equals(args[0], CommandName, StringComparison.Ordinal);

    /// <summary>
    /// Only the CLI connects as <c>lifegraph_provisioner</c>, the one role allowed to insert an
    /// Account without an Account in context; the web process keeps the application role (DA-107).
    /// </summary>
    public static string ConnectionStringNameFor(IReadOnlyList<string> args) =>
        IsInvocation(args)
            ? PersistenceServiceCollectionExtensions.ProvisioningConnectionStringName
            : PersistenceServiceCollectionExtensions.ConnectionStringName;

    public static async Task<int> RunAsync(
        IServiceProvider services,
        IReadOnlyList<string> args,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        if (args is not [CommandName, var action, "--email", var email] || string.IsNullOrWhiteSpace(email))
        {
            await output.WriteLineAsync(Usage);
            return UsageError;
        }

        await using var scope = services.CreateAsyncScope();
        var provisioner = scope.ServiceProvider.GetRequiredService<AccountProvisioner>();

        AccountProvisioningOutcome? outcome = action switch
        {
            "create" => await provisioner.ProvisionAsync(email, cancellationToken),
            "resend" => await provisioner.ResendEmailConfirmationAsync(email, cancellationToken),
            _ => null,
        };

        if (outcome is null)
        {
            await output.WriteLineAsync(Usage);
            return UsageError;
        }

        var (message, exitCode) = Describe(outcome);
        await output.WriteLineAsync(message);
        return exitCode;
    }

    private static (string Message, int ExitCode) Describe(AccountProvisioningOutcome outcome) => outcome switch
    {
        AccountProvisioningOutcome.Created { LinkSent: true } created =>
            ($"Account {created.AccountId} created. The set-password link was e-mailed.", Success),
        AccountProvisioningOutcome.Created created =>
            ($"Account {created.AccountId} created, but the e-mail failed. Run 'accounts resend' to send the link.", Failure),
        AccountProvisioningOutcome.Pending { LinkSent: true } pending =>
            ($"Account {pending.AccountId} is still pending. A new set-password link was e-mailed.", Success),
        AccountProvisioningOutcome.Pending pending =>
            ($"Account {pending.AccountId} is still pending, but the e-mail failed. Try again.", Failure),
        AccountProvisioningOutcome.AlreadyActive =>
            ("This e-mail already has an active account. Its user can recover the password from the login page.", Failure),
        AccountProvisioningOutcome.NotFound =>
            ("No account has this e-mail. Use 'accounts create'.", Failure),
        AccountProvisioningOutcome.Rejected rejected =>
            ($"Rejected: {string.Join(", ", rejected.ErrorCodes)}.", Failure),
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "Unknown provisioning outcome."),
    };
}
