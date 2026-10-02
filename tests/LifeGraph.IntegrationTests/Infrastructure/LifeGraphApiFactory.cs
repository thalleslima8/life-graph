using LifeGraph.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace LifeGraph.IntegrationTests.Infrastructure;

public sealed class LifeGraphApiFactory(PostgresDatabase database) : WebApplicationFactory<Program>
{
    public const string AccountHeader = "X-Test-Account-Id";

    public HttpClient CreateClientFor(Guid accountId)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add(AccountHeader, accountId.ToString());
        return client;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting($"ConnectionStrings:{PersistenceServiceCollectionExtensions.ConnectionStringName}", database.ApplicationConnectionString);

        builder.ConfigureTestServices(services =>
        {
            services.AddHttpContextAccessor();
            services.AddScoped<IAccountContext, HeaderAccountContext>();
            services.AddSingleton<IStartupFilter, RlsProbes.Endpoints>();
        });
    }

    // Stand-in for authentication until E1 provides the real principal.
    private sealed class HeaderAccountContext(IHttpContextAccessor httpContextAccessor) : IAccountContext
    {
        public Guid? AccountId =>
            Guid.TryParse(httpContextAccessor.HttpContext?.Request.Headers[AccountHeader], out var accountId)
                ? accountId
                : null;
    }
}
