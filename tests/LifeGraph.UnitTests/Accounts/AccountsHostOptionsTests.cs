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
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;
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
        ["Accounts:Issuer:Issuer"] = "https://issuer.lifegraph.test/",
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

    // DA-033: the switch that skips the consent page only exists in the Testing environment.
    [Theory]
    [InlineData("Production")]
    [InlineData("Development")]
    public void Auto_consent_outside_the_testing_environment_fails_the_startup_validation(string environment)
    {
        using var services = BuildServices(new() { [UseEphemeralKeysKey] = "true", ["Accounts:Issuer:AutoConsent"] = "true" }, environment);

        var failure = Assert.Throws<OptionsValidationException>(() => services.GetRequiredService<IStartupValidator>().Validate());

        Assert.Contains(IssuerRegistration.AutoConsentOutsideTestingMessage, failure.Failures);
    }

    [Fact]
    public void Auto_consent_is_accepted_in_the_testing_environment()
    {
        using var services = BuildServices(new() { [UseEphemeralKeysKey] = "true", ["Accounts:Issuer:AutoConsent"] = "true" }, IssuerOptions.TestingEnvironment);

        services.GetRequiredService<IStartupValidator>().Validate();

        Assert.True(services.GetRequiredService<IOptions<IssuerOptions>>().Value.AutoConsent);
    }

    [Fact]
    public void Auto_consent_is_off_by_default() =>
        Assert.False(new IssuerOptions().AutoConsent);

    [Theory]
    [InlineData(null)]
    [InlineData("agents.example.com")]
    [InlineData("http://agents.example.com/")]
    [InlineData("https://agents.example.com/lifegraph/")]
    [InlineData("https://agents.example.com/?x=1")]
    public void An_issuer_that_is_not_an_https_origin_fails_the_startup_validation(string? issuer)
    {
        using var services = BuildServices(new() { [UseEphemeralKeysKey] = "true", ["Accounts:Issuer:Issuer"] = issuer });

        var failure = Assert.ThrowsAny<Exception>(() => services.GetRequiredService<IStartupValidator>().Validate());

        Assert.Contains(IssuerRegistration.InvalidIssuerMessage, failure.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Both_key_sources_at_once_fail_the_startup_validation()
    {
        using var services = BuildServices(new() { [UseEphemeralKeysKey] = "true", ["Accounts:Issuer:KeysDirectory"] = "/tmp/keys" });

        var failure = Assert.Throws<OptionsValidationException>(() => services.GetRequiredService<IStartupValidator>().Validate());

        Assert.Contains(IssuerRegistration.MissingKeysMessage, failure.Failures);
    }

    [Theory]
    [InlineData("https://claude.ai/client.json", "https://claude.ai/api/mcp/auth_callback")]
    [InlineData("claude-ai", "http://claude.ai/api/mcp/auth_callback")]
    [InlineData("claude-ai", "https://claude.ai/callback#fragment")]
    public void A_pre_registered_client_with_a_url_id_or_an_unsafe_redirect_fails_the_startup_validation(string clientId, string redirectUri)
    {
        using var services = BuildServices(new()
        {
            [UseEphemeralKeysKey] = "true",
            ["Accounts:Issuer:Clients:0:ClientId"] = clientId,
            ["Accounts:Issuer:Clients:0:RedirectUris:0"] = redirectUri,
        });

        var failure = Assert.Throws<OptionsValidationException>(() => services.GetRequiredService<IStartupValidator>().Validate());

        Assert.Contains(IssuerRegistration.InvalidClientsMessage, failure.Failures);
    }

    [Fact]
    public void Pre_registered_clients_bind_from_the_issuer_section()
    {
        using var services = BuildServices(new()
        {
            [UseEphemeralKeysKey] = "true",
            ["Accounts:Issuer:Clients:0:ClientId"] = "claude-ai",
            ["Accounts:Issuer:Clients:0:DisplayName"] = "Claude",
            ["Accounts:Issuer:Clients:0:RedirectUris:0"] = "https://claude.ai/api/mcp/auth_callback",
        });

        services.GetRequiredService<IStartupValidator>().Validate();

        var client = Assert.Single(services.GetRequiredService<IOptions<IssuerOptions>>().Value.Clients);
        Assert.Equal("claude-ai", client.ClientId);
        Assert.Equal(new Uri("https://claude.ai/api/mcp/auth_callback"), Assert.Single(client.RedirectUris));
    }

    // DA-028: the tunnel's keys persist across restarts, outside the repository, readable only by the owner.
    [Fact]
    public void Persistent_keys_are_created_once_and_reloaded_with_the_same_ids()
    {
        var directory = Path.Combine(Path.GetTempPath(), "lifegraph-keys-" + Guid.NewGuid().ToString("N"));
        try
        {
            var first = ConfiguredServerOptions(new() { ["Accounts:Issuer:KeysDirectory"] = directory });
            var second = ConfiguredServerOptions(new() { ["Accounts:Issuer:KeysDirectory"] = directory });

            Assert.Equal(Assert.Single(first.SigningCredentials).Key.KeyId, Assert.Single(second.SigningCredentials).Key.KeyId);
            Assert.Equal(Assert.Single(first.EncryptionCredentials).Key.KeyId, Assert.Single(second.EncryptionCredentials).Key.KeyId);
            Assert.NotEqual(first.SigningCredentials[0].Key.KeyId, first.EncryptionCredentials[0].Key.KeyId);
            if (!OperatingSystem.IsWindows())
            {
                Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(Path.Combine(directory, IssuerKeys.SigningKeyFile)));
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Every_token_is_bound_to_the_mcp_endpoint_of_the_issuer()
    {
        var server = ConfiguredServerOptions(new() { [UseEphemeralKeysKey] = "true" });

        Assert.Equal(new Uri("https://issuer.lifegraph.test/"), server.Issuer);
        Assert.Equal(new Uri("https://issuer.lifegraph.test/mcp"), Assert.Single(server.Resources));
        Assert.Equal(TimeSpan.FromMinutes(10), server.AccessTokenLifetime);
    }

    private static ServiceProvider BuildServices(Dictionary<string, string?> settings, string environment = "Production")
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(RequiredSettings)
            .AddInMemoryCollection(settings)
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        services.AddSingleton<IHostEnvironment>(new HostingEnvironment { EnvironmentName = environment });
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
