using LifeGraph.Accounts.Email;
using LifeGraph.Accounts.Issuer;
using LifeGraph.Infrastructure.Jobs;
using LifeGraph.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace LifeGraph.IntegrationTests.Infrastructure;

public sealed class LifeGraphApiFactory : WebApplicationFactory<Program>
{
    public const string SpaBaseUrl = "https://spa.lifegraph.test";

    /// <summary>Where the test server answers, and so the issuer and the MCP resource's origin.</summary>
    public static readonly Uri Origin = new("https://localhost/");

    /// <summary>Not Development, so nothing from appsettings.Development.json leaks into the tests.</summary>
    private const string TestingEnvironment = "Testing";

    private readonly PostgresDatabase _database;
    private readonly IReadOnlyDictionary<string, string> _settings;
    private readonly bool _isProvisioningHost;
    private LifeGraphApiFactory? _provisioning;
    private readonly Action<IServiceCollection>? _configureServices;

    /// <param name="settings">Configuration overrides for this host (e.g. rate limits).</param>
    /// <param name="configureServices">Test doubles for this host (e.g. the CIMD fetcher, which would need the network).</param>
    public LifeGraphApiFactory(
        PostgresDatabase database,
        IReadOnlyDictionary<string, string>? settings = null,
        Action<IServiceCollection>? configureServices = null)
        : this(database, settings, new CapturingAccountMailer(), isProvisioningHost: false)
    {
        _configureServices = configureServices;
    }

    private LifeGraphApiFactory(
        PostgresDatabase database,
        IReadOnlyDictionary<string, string>? settings,
        CapturingAccountMailer mailer,
        bool isProvisioningHost)
    {
        _database = database;
        _settings = settings ?? new Dictionary<string, string>();
        _isProvisioningHost = isProvisioningHost;
        Mailer = mailer;
        // The session and CSRF cookies are Secure (__Host-); a cookie jar only returns them over HTTPS.
        ClientOptions.BaseAddress = Origin;
    }

    public CapturingAccountMailer Mailer { get; }

    public TestClock Clock { get; } = new();

    /// <summary>
    /// The host as the owner's <c>accounts</c> CLI builds it: same services, connected as
    /// <c>lifegraph_provisioner</c> (DA-107), sharing this host's mailer. The web host itself
    /// keeps the application role and cannot create Accounts.
    /// </summary>
    public LifeGraphApiFactory Provisioning => _isProvisioningHost
        ? this
        : _provisioning ??= new LifeGraphApiFactory(_database, _settings, Mailer, isProvisioningHost: true);

    public override async ValueTask DisposeAsync()
    {
        if (_provisioning is not null)
        {
            await _provisioning.DisposeAsync();
        }

        await base.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(TestingEnvironment);
        // Throwaway issuer keys, at the test server's origin; cookies and transport keep their secure defaults.
        builder.UseSetting($"{IssuerOptions.SectionName}:{nameof(IssuerOptions.UseEphemeralKeys)}", "true");
        builder.UseSetting($"{IssuerOptions.SectionName}:{nameof(IssuerOptions.Issuer)}", Origin.AbsoluteUri);
        // The Host picks the connection by its arguments (AccountsCommandLine.ConnectionStringNameFor);
        // a test host has none, so the provisioning host gets the provisioning role here.
        builder.UseSetting(
            $"ConnectionStrings:{PersistenceServiceCollectionExtensions.ConnectionStringName}",
            _isProvisioningHost ? _database.ProvisioningConnectionString : _database.ApplicationConnectionString);
        builder.UseSetting($"{SmtpOptions.SectionName}:Host", "smtp.invalid");
        builder.UseSetting($"{SmtpOptions.SectionName}:Port", "25");
        builder.UseSetting($"{SmtpOptions.SectionName}:From", "no-reply@lifegraph.test");
        builder.UseSetting($"{SpaOptions.SectionName}:BaseUrl", SpaBaseUrl);
        // Tests run due jobs by hand (JobRunner), so nothing runs behind their back.
        builder.UseSetting($"{JobsOptions.SectionName}:{nameof(JobsOptions.RunWorker)}", "false");
        foreach (var (key, value) in _settings)
        {
            builder.UseSetting(key, value);
        }

        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<IStartupFilter, RlsProbes.Endpoints>();
            services.AddSingleton<IAccountMailer>(Mailer);
            services.AddSingleton<TimeProvider>(Clock);
            services.AddScoped<TestAccountContext>();
            services.AddScoped<IAccountContext>(provider => provider.GetRequiredService<TestAccountContext>());
            _configureServices?.Invoke(services);

            // The owner's CLI builds the host but never runs it, so no hosted service starts there.
            if (_isProvisioningHost)
            {
                services.RemoveAll<IHostedService>();
            }
        });
    }
}
