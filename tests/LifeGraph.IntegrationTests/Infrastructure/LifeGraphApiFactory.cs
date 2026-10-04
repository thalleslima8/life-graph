using LifeGraph.Accounts.Email;
using LifeGraph.Accounts.Issuer;
using LifeGraph.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace LifeGraph.IntegrationTests.Infrastructure;

public sealed class LifeGraphApiFactory : WebApplicationFactory<Program>
{
    public const string SpaBaseUrl = "https://spa.lifegraph.test";

    /// <summary>Not Development, so nothing from appsettings.Development.json leaks into the tests.</summary>
    private const string TestingEnvironment = "Testing";

    private readonly PostgresDatabase _database;
    private readonly IReadOnlyDictionary<string, string> _settings;

    /// <param name="settings">Configuration overrides for this host (e.g. rate limits).</param>
    public LifeGraphApiFactory(PostgresDatabase database, IReadOnlyDictionary<string, string>? settings = null)
    {
        _database = database;
        _settings = settings ?? new Dictionary<string, string>();
        // The session and CSRF cookies are Secure (__Host-); a cookie jar only returns them over HTTPS.
        ClientOptions.BaseAddress = new Uri("https://localhost");
    }

    public CapturingAccountMailer Mailer { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(TestingEnvironment);
        // Throwaway issuer keys; cookies and transport keep their secure defaults.
        builder.UseSetting($"{IssuerOptions.SectionName}:{nameof(IssuerOptions.UseEphemeralKeys)}", "true");
        builder.UseSetting($"ConnectionStrings:{PersistenceServiceCollectionExtensions.ConnectionStringName}", _database.ApplicationConnectionString);
        builder.UseSetting($"{SmtpOptions.SectionName}:Host", "smtp.invalid");
        builder.UseSetting($"{SmtpOptions.SectionName}:Port", "25");
        builder.UseSetting($"{SmtpOptions.SectionName}:From", "no-reply@lifegraph.test");
        builder.UseSetting($"{SpaOptions.SectionName}:BaseUrl", SpaBaseUrl);
        foreach (var (key, value) in _settings)
        {
            builder.UseSetting(key, value);
        }

        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<IStartupFilter, RlsProbes.Endpoints>();
            services.AddSingleton<IAccountMailer>(Mailer);
        });
    }
}
