using System.Net;
using LifeGraph.Host.Operations;
using LifeGraph.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Hosting;

namespace LifeGraph.IntegrationTests.Operations;

public sealed class HealthEndpointsTests(PostgresDatabase database) : IAsyncDisposable
{
    private readonly LifeGraphApiFactory _factory = new(database);

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    [Theory]
    [InlineData(HealthCheckExtensions.LivePath)]
    [InlineData(HealthCheckExtensions.ReadyPath)]
    public async Task Health_endpoint_reports_healthy(string path)
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(path, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Without_database_the_service_is_alive_but_not_ready()
    {
        await using var factoryWithoutDatabase = _factory.WithWebHostBuilder(builder => builder.UseSetting(
            "ConnectionStrings:Default",
            "Host=127.0.0.1;Port=1;Username=nobody;Password=none;Timeout=1"));
        using var client = factoryWithoutDatabase.CreateClient();

        var live = await client.GetAsync(HealthCheckExtensions.LivePath, TestContext.Current.CancellationToken);
        var ready = await client.GetAsync(HealthCheckExtensions.ReadyPath, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, ready.StatusCode);
    }
}
