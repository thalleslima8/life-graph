using System.Reflection;
using LifeGraph.Accounts.Email;
using LifeGraph.Accounts.Issuer;

namespace LifeGraph.Host.OpenApi;

/// <summary>
/// The build writes <c>openapi/lifegraph.json</c> by starting the host inside the
/// <c>GetDocument.Insider</c> tool (Microsoft.Extensions.ApiDescription.Server), which swaps
/// the server for one that never listens. A clean runner has none of the host's required
/// settings, so the startup validation (GEN-052) would abort the build. Only inside that tool
/// the host gets the placeholder settings below; every other process keeps the secure
/// defaults and refuses to start without real configuration.
/// </summary>
public static class BuildTimeDocumentGeneration
{
    public const string ToolAssemblyName = "GetDocument.Insider";

    /// <summary>
    /// Enough to pass the startup validation. Never reached by traffic: the tool's server does
    /// not listen. <c>.invalid</c> hosts (RFC 2606) cannot resolve.
    /// </summary>
    public static IReadOnlyDictionary<string, string?> PlaceholderSettings { get; } = new Dictionary<string, string?>
    {
        [$"{SmtpOptions.SectionName}:{nameof(SmtpOptions.Host)}"] = "smtp.invalid",
        [$"{SmtpOptions.SectionName}:{nameof(SmtpOptions.Port)}"] = "25",
        [$"{SmtpOptions.SectionName}:{nameof(SmtpOptions.From)}"] = "openapi@build.invalid",
        [$"{SpaOptions.SectionName}:{nameof(SpaOptions.BaseUrl)}"] = "https://spa.build.invalid",
        [$"{IssuerOptions.SectionName}:{nameof(IssuerOptions.UseEphemeralKeys)}"] = "true",
    };

    public static bool IsRunningUnder(Assembly? entryAssembly) =>
        string.Equals(entryAssembly?.GetName().Name, ToolAssemblyName, StringComparison.Ordinal);
}
