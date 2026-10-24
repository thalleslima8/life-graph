using System.Text.Json;

namespace LifeGraph.IntegrationTests.Infrastructure;

/// <summary>The contract as the build generated and the repository commits it (<c>openapi/lifegraph.json</c>).</summary>
internal static class CommittedOpenApi
{
    public static async Task<JsonDocument> ReadAsync(CancellationToken cancellationToken) =>
        JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(RepositoryRoot(), "openapi", "lifegraph.json"), cancellationToken));

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "LifeGraph.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("LifeGraph.sln not found above the test output.");
    }
}
