using System.Text.Json;

namespace LifeGraph.IntegrationTests.Infrastructure;

public static class ProblemCode
{
    /// <summary>The machine-readable <c>code</c> of a Problem Details response (API-051).</summary>
    public static async Task<string?> ReadAsync(HttpResponseMessage response)
    {
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        return problem.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }
}
