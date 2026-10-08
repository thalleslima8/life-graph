using System.Net;
using LifeGraph.Http;
using LifeGraph.Infrastructure.Errors;
using LifeGraph.IntegrationTests.Infrastructure;

namespace LifeGraph.IntegrationTests.Http;

/// <summary>
/// The per-principal budgets of the graph API (DA-116, API-082): N+1 requests are a 429 with
/// <c>Retry-After</c> and the common code, and one Account never spends another's budget.
/// </summary>
public sealed class ApiRateLimitTests(PostgresDatabase database) : IAsyncLifetime
{
    private const string EmailA = "ada@example.test";
    private const string EmailB = "grace@example.test";
    private const int ReadLimit = 3;
    private const int WriteLimit = 2;

    private readonly LifeGraphApiFactory _factory = new(database, new Dictionary<string, string>
    {
        [$"{ApiRateLimitOptions.SectionName}:{nameof(ApiRateLimitOptions.ReadPermitLimit)}"] = $"{ReadLimit}",
        [$"{ApiRateLimitOptions.SectionName}:{nameof(ApiRateLimitOptions.WritePermitLimit)}"] = $"{WriteLimit}",
        [$"{ApiRateLimitOptions.SectionName}:{nameof(ApiRateLimitOptions.WindowSeconds)}"] = "600",
    });

    public async ValueTask InitializeAsync()
    {
        await database.ResetAsync();
        await TestAccounts.ProvisionConfirmedAsync(_factory, EmailA);
        await TestAccounts.ProvisionConfirmedAsync(_factory, EmailB);
    }

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    [Fact]
    public async Task The_read_after_the_budget_is_a_429_with_retry_after_and_another_account_keeps_its_own()
    {
        using var ada = await TestAccounts.SignedInAsync(_factory, EmailA);
        using var grace = await TestAccounts.SignedInAsync(_factory, EmailB);

        for (var read = 0; read < ReadLimit; read++)
        {
            Assert.Equal(HttpStatusCode.OK, (await ada.GetAsync("/api/nodes")).StatusCode);
        }

        var refused = await ada.GetAsync("/api/nodes");

        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
        Assert.Equal(CommonErrors.TooManyRequests.Code, await ProblemCode.ReadAsync(refused));
        Assert.InRange(refused.Headers.RetryAfter?.Delta?.TotalSeconds ?? 0, 1, 600);
        Assert.Equal(HttpStatusCode.OK, (await grace.GetAsync("/api/nodes")).StatusCode);
    }

    [Fact]
    public async Task Writes_have_their_own_budget_apart_from_reads()
    {
        using var ada = await TestAccounts.SignedInAsync(_factory, EmailA);

        for (var write = 0; write < WriteLimit; write++)
        {
            Assert.Equal(HttpStatusCode.Created, (await ada.PostAsync("/api/nodes", new { title = $"Note {write}" })).StatusCode);
        }

        var refused = await ada.PostAsync("/api/nodes", new { title = "One too many" });

        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
        Assert.Equal(CommonErrors.TooManyRequests.Code, await ProblemCode.ReadAsync(refused));
        Assert.Equal(HttpStatusCode.OK, (await ada.GetAsync("/api/nodes")).StatusCode);
    }
}
