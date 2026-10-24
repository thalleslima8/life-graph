using System.Text.Json;
using System.Text.RegularExpressions;

namespace LifeGraph.ArchitectureTests.Dependencies;

/// <summary>
/// What the type rules cannot see (DA-100, DA-102): a package referenced but never used,
/// mixed Limaj versions, and a suppressed CS0618 that would let <c>Error.HttpStatusCode</c> compile.
/// </summary>
public sealed partial class LimajDependencyTests
{
    // The canary must read the obsolete member to prove its rule; nothing else may.
    private static readonly string[] AllowedSuppressions = ["tests/LifeGraph.ArchitectureTests/Canary/BrokenLimajUsage.cs"];

    [Fact]
    public void Only_limaj_core_and_web_are_in_the_dependency_graph_all_at_one_version()
    {
        // This project references the Host, so its deps file holds the Host's whole graph.
        using var deps = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "LifeGraph.ArchitectureTests.deps.json")));
        var limaj = deps.RootElement.GetProperty("libraries").EnumerateObject()
            .Select(library => library.Name.Split('/'))
            .Where(nameAndVersion => nameAndVersion[0].StartsWith("Limaj.", StringComparison.Ordinal))
            .ToList();

        Assert.Equal(["Limaj.Framework.Core", "Limaj.Framework.Web"], limaj.Select(library => library[0]).Order());
        Assert.Single(limaj.Select(library => library[1]).Distinct());
    }

    [Fact]
    public void No_build_setting_or_source_suppresses_the_obsolete_warning()
    {
        var root = RepositoryRoot();
        var offenders = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(path => path.EndsWith(".cs", StringComparison.Ordinal)
                || path.EndsWith(".csproj", StringComparison.Ordinal)
                || path.EndsWith(".props", StringComparison.Ordinal)
                || path.EndsWith(".targets", StringComparison.Ordinal)
                || Path.GetFileName(path) == ".editorconfig")
            .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'))
            .Where(path => !IsBuildOutput(path) && !AllowedSuppressions.Contains(path))
            .Where(path => ObsoleteWarning().IsMatch(File.ReadAllText(Path.Combine(root, path))))
            .ToList();

        Assert.Empty(offenders);
    }

    private static bool IsBuildOutput(string relativePath) =>
        relativePath.Split('/').Any(segment => segment is "bin" or "obj" or "node_modules" or ".git");

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "LifeGraph.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("LifeGraph.sln not found above the test output.");
    }

    // NoWarn/WarningsNotAsErrors lists, #pragma, SuppressMessage and .editorconfig severities alike.
    [GeneratedRegex(
        @"#pragma\s+warning\s+disable[^\r\n]*\bCS0618\b|<(NoWarn|WarningsNotAsErrors)>[^<]*\b(CS)?0?618\b|SuppressMessage\([^)]*CS0618|dotnet_diagnostic\.CS0618\.",
        RegexOptions.IgnoreCase)]
    private static partial Regex ObsoleteWarning();
}
