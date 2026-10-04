using LifeGraph.Accounts;
using LifeGraph.Accounts.Csrf;
using LifeGraph.Accounts.Identity;
using LifeGraph.Accounts.Issuer;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenIddict.Server;
using OpenIddict.Server.AspNetCore;

namespace LifeGraph.UnitTests.Accounts;

/// <summary>
/// The insecure host switches (plain-HTTP cookies, HTTP issuer) come only from configuration
/// (GEN-051) and are off unless set. Throwaway issuer keys are the only key source until E3,
/// so a host without them, or with a bad switch value, stops at startup (GEN-052).
/// </summary>
public sealed class AccountsHostOptionsTests
{
    private const string UseEphemeralKeysKey = "Accounts:Issuer:UseEphemeralKeys";

    private static readonly Dictionary<string, string?> RequiredSettings = new()
    {
        ["Email:Smtp:Host"] = "smtp.invalid",
        ["Email:Smtp:Port"] = "25",
        ["Email:Smtp:From"] = "no-reply@lifegraph.test",
        ["Spa:BaseUrl"] = "https://spa.lifegraph.test",
    };

    [Fact]
    public void Without_configuration_every_insecure_switch_is_off()
    {
        using var services = BuildServices(new() { [UseEphemeralKeysKey] = "true" });

        Assert.False(services.GetRequiredService<IOptions<BrowserCookieOptions>>().Value.AllowInsecureHttp);
        Assert.False(services.GetRequiredService<IOptions<IssuerOptions>>().Value.AllowHttp);

        Assert.Equal("__Host-" + IdentityRegistration.SessionCookieBaseName, SessionCookie(services).Name);
        Assert.Equal(CookieSecurePolicy.Always, SessionCookie(services).SecurePolicy);
        Assert.Equal("__Host-" + CsrfProtection.CookieBaseName, CsrfCookie(services).Name);
        Assert.Equal(CookieSecurePolicy.Always, CsrfCookie(services).SecurePolicy);
        Assert.False(services.GetRequiredService<IOptions<OpenIddictServerAspNetCoreOptions>>().Value.DisableTransportSecurityRequirement);
    }

    [Fact]
    public void The_switches_bind_from_the_accounts_section()
    {
        using var services = BuildServices(new()
        {
            ["Accounts:Cookies:AllowInsecureHttp"] = "true",
            [UseEphemeralKeysKey] = "true",
            ["Accounts:Issuer:AllowHttp"] = "true",
        });

        Assert.Equal(IdentityRegistration.SessionCookieBaseName, SessionCookie(services).Name);
        Assert.Equal(CookieSecurePolicy.SameAsRequest, SessionCookie(services).SecurePolicy);
        Assert.Equal(CsrfProtection.CookieBaseName, CsrfCookie(services).Name);
        Assert.Equal(CookieSecurePolicy.SameAsRequest, CsrfCookie(services).SecurePolicy);
        Assert.True(services.GetRequiredService<IOptions<OpenIddictServerAspNetCoreOptions>>().Value.DisableTransportSecurityRequirement);
    }

    [Fact]
    public void Ephemeral_keys_sign_and_encrypt_when_configured()
    {
        var withKeys = ConfiguredServerOptions(new() { [UseEphemeralKeysKey] = "true" });

        Assert.Single(withKeys.SigningCredentials);
        Assert.Single(withKeys.EncryptionCredentials);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("false")]
    public void Without_ephemeral_keys_the_startup_validation_fails_with_a_clear_message(string? useEphemeralKeys)
    {
        using var services = BuildServices(new() { [UseEphemeralKeysKey] = useEphemeralKeys });

        var failure = Assert.Throws<OptionsValidationException>(() => services.GetRequiredService<IStartupValidator>().Validate());

        Assert.Contains(IssuerRegistration.MissingKeysMessage, failure.Failures);
    }

    [Theory]
    [InlineData("Accounts:Cookies:AllowInsecureHttp")]
    [InlineData(UseEphemeralKeysKey)]
    [InlineData("Accounts:Issuer:AllowHttp")]
    public void An_invalid_switch_value_fails_the_startup_validation(string key)
    {
        using var services = BuildServices(new() { [UseEphemeralKeysKey] = "true", [key] = "maybe" });

        Assert.ThrowsAny<InvalidOperationException>(() => services.GetRequiredService<IStartupValidator>().Validate());
    }

    private static ServiceProvider BuildServices(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(RequiredSettings)
            .AddInMemoryCollection(settings)
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        services.AddAccountsModule();
        return services.BuildServiceProvider();
    }

    private static CookieBuilder SessionCookie(IServiceProvider services) =>
        services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(IdentityConstants.ApplicationScheme).Cookie;

    private static CookieBuilder CsrfCookie(IServiceProvider services) =>
        services.GetRequiredService<IOptions<AntiforgeryOptions>>().Value.Cookie;

    /// <summary>
    /// The configure steps only, without OpenIddict's own post-configure validation, which
    /// needs a full host.
    /// </summary>
    private static OpenIddictServerOptions ConfiguredServerOptions(Dictionary<string, string?> settings)
    {
        using var services = BuildServices(settings);
        var serverOptions = new OpenIddictServerOptions();
        foreach (var configure in services.GetServices<IConfigureOptions<OpenIddictServerOptions>>())
        {
            configure.Configure(serverOptions);
        }

        return serverOptions;
    }
}
