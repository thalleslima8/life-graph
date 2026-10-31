using LifeGraph.Graph.Contracts;
using LifeGraph.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace LifeGraph.Sharing.Operations;

/// <summary>
/// The operator's kill-switch per link (DA-217). It runs on the host, as the operator database
/// role, and has no HTTP surface:
/// <code>
/// ConnectionStrings__Operator=... dotnet run --project src/LifeGraph.Host -- shares disable &lt;public_ref&gt;
/// </code>
/// The link is named by the public reference an abuse report carries (DA-218), never by its token
/// or internal id. The block is final for that link; the global switch is <c>Sharing:Enabled</c>.
/// </summary>
public static class SharesCommandLine
{
    public const string CommandName = "shares";

    public const int Success = 0;
    public const int Failure = 1;
    public const int UsageError = 2;

    private const string Usage = """
        Usage:
          shares disable <public_ref>   Block the link with this public reference; its visitors get the same answer as a revoked link.
        """;

    public static bool IsInvocation(IReadOnlyList<string> args) =>
        args.Count > 0 && string.Equals(args[0], CommandName, StringComparison.Ordinal);

    public static async Task<int> RunAsync(
        IServiceProvider services,
        IReadOnlyList<string> args,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        if (args is not [CommandName, "disable", var publicRef] || string.IsNullOrWhiteSpace(publicRef))
        {
            await output.WriteLineAsync(Usage);
            return UsageError;
        }

        await using var scope = services.CreateAsyncScope();
        var killSwitch = scope.ServiceProvider.GetRequiredService<ISharedGraphViewKillSwitch>();
        var (message, exitCode) = await killSwitch.DisableAsync(publicRef, cancellationToken) switch
        {
            SharedGraphViewDisableOutcome.Disabled => ($"Link {publicRef} disabled.", Success),
            SharedGraphViewDisableOutcome.AlreadyDisabled => ($"Link {publicRef} was already disabled.", Success),
            _ => ($"No link has the public reference {publicRef}.", Failure),
        };
        await output.WriteLineAsync(message);
        return exitCode;
    }

    /// <summary>Only this CLI connects as <c>lifegraph_operator</c>; the web process keeps the application role (DA-217).</summary>
    public const string ConnectionStringName = PersistenceServiceCollectionExtensions.OperatorConnectionStringName;
}
