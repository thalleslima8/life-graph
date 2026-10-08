using LifeGraph.Accounts.Provisioning;
using Microsoft.Extensions.DependencyInjection;

namespace LifeGraph.IntegrationTests.Infrastructure;

public static class TestAccounts
{
    public const string Password = "correct horse battery staple";

    /// <summary>
    /// What the owner's CLI does (DA-095), as its database role (DA-107): a pending account
    /// and a set-password e-mail.
    /// </summary>
    public static async Task<AccountProvisioningOutcome> ProvisionAsync(LifeGraphApiFactory factory, string email)
    {
        await using var scope = factory.Provisioning.Services.CreateAsyncScope();
        var provisioner = scope.ServiceProvider.GetRequiredService<AccountProvisioner>();
        return await provisioner.ProvisionAsync(email, TestContext.Current.CancellationToken);
    }

    /// <summary>Provisions the account and follows the e-mailed link to set <see cref="Password"/>, leaving it ready to sign in.</summary>
    public static async Task<AccountProvisioningOutcome.Created> ProvisionConfirmedAsync(LifeGraphApiFactory factory, string email)
    {
        var created = Assert.IsType<AccountProvisioningOutcome.Created>(await ProvisionAsync(factory, email));

        using var spa = new SpaClient(factory.CreateClient());
        var confirmation = await spa.ConfirmEmailAsync(EmailedLink.Parse(factory.Mailer.LastTo(email)), Password);
        confirmation.EnsureSuccessStatusCode();

        return created;
    }

    public static async Task<SpaClient> SignedInAsync(LifeGraphApiFactory factory, string email)
    {
        var spa = new SpaClient(factory.CreateClient());
        var login = await spa.LoginAsync(email, Password);
        login.EnsureSuccessStatusCode();
        return spa;
    }
}
