using System.ComponentModel.DataAnnotations;
using System.Threading.RateLimiting;
using LifeGraph.Infrastructure.Errors;
using LifeGraph.Infrastructure.Identity;
using Limaj.Framework.Web.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace LifeGraph.Http;

/// <summary>
/// The abuse limits of the authenticated API (DA-116, API-082): a read and a write budget per
/// principal, in fixed windows. They protect the service, never sell anything: the same in
/// every plan, never a Free/Premium quota. The policy names and the partition key live here
/// only, so E5 keys an AgentIdentity the same way and a chatty agent never spends the
/// person's budget, the Undo of what it did included. The counters are in memory: one
/// instance in the MVP.
/// </summary>
public static class ApiRateLimiting
{
    /// <summary>Safe methods: sized for the SPA's own polling and refetching (DA-024).</summary>
    public const string ReadPolicy = "api.read";

    /// <summary>Unsafe methods, Undo and archive included: tighter, still beyond what a person reaches in the UI.</summary>
    public const string WritePolicy = "api.write";

    /// <summary>
    /// Every MCP request of a connected agent: its own budget per AgentIdentity, under a ceiling
    /// shared by all the agents of the Account, so connecting more agents never multiplies the
    /// quota (DA-121).
    /// </summary>
    public const string AgentPolicy = "api.agent";

    public const string TooManyRequestsMessage = "Too many requests. Wait and try again.";

    private const string AgentAccountCeilingPartition = "api.agent-account";

    private const string UnknownClient = "unknown";

    public static IServiceCollection AddLifeGraphApiRateLimiting(this IServiceCollection services)
    {
        services.AddOptions<ApiRateLimitOptions>()
            .BindConfiguration(ApiRateLimitOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddRateLimiter(_ => { });
        services.AddOptions<RateLimiterOptions>().Configure<IOptions<ApiRateLimitOptions>>((limiter, limits) =>
        {
            var window = TimeSpan.FromSeconds(limits.Value.WindowSeconds);
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            limiter.AddPolicy(ReadPolicy, context => Partition(context, ReadPolicy, limits.Value.ReadPermitLimit, window));
            limiter.AddPolicy(WritePolicy, context => Partition(context, WritePolicy, limits.Value.WritePermitLimit, window));
            limiter.AddPolicy(AgentPolicy, context => Partition(context, AgentPolicy, limits.Value.AgentPermitLimit, window));

            // The endpoint policy is per AgentIdentity; the Account's ceiling runs beside it, as
            // the global limiter, and only on endpoints that ask for it.
            limiter.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
                AgentAccountKeyOf(context) is { } accountKey
                    ? FixedWindow($"{AgentAccountCeilingPartition}:{accountKey}", limits.Value.AgentAccountPermitLimit, window)
                    : RateLimitPartition.GetNoLimiter(string.Empty));
            limiter.OnRejected = (rejected, _) => WriteRefusalAsync(rejected, window);
        });

        return services;
    }

    /// <summary>Puts every endpoint of the group under the read or the write budget, by its method.</summary>
    public static RouteGroupBuilder RequirePrincipalRateLimits(this RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        // Finally: the method metadata of each endpoint is known only once it is built.
        ((IEndpointConventionBuilder)group).Finally(endpoint =>
        {
            var methods = endpoint.Metadata.OfType<HttpMethodMetadata>().SelectMany(metadata => metadata.HttpMethods).ToList();
            var isRead = methods.Count > 0 && methods.All(IsSafeMethod);
            endpoint.Metadata.Add(new EnableRateLimitingAttribute(isRead ? ReadPolicy : WritePolicy));
        });
        return group;
    }

    /// <summary>
    /// Puts the endpoints under the agent budgets (DA-121): one per AgentIdentity and a ceiling
    /// per Account across all its agents.
    /// </summary>
    public static TBuilder RequireAgentRateLimits<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Add(endpoint =>
        {
            endpoint.Metadata.Add(new EnableRateLimitingAttribute(AgentPolicy));
            endpoint.Metadata.Add(AgentAccountCeiling.Instance);
        });
        return builder;
    }

    /// <summary>
    /// The Account whose agents' shared ceiling a request takes from: only an agent's request to
    /// an endpoint under <see cref="RequireAgentRateLimits"/>; <c>null</c> for anything else.
    /// </summary>
    public static string? AgentAccountKeyOf(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        if (httpContext.GetEndpoint()?.Metadata.GetMetadata<AgentAccountCeiling>() is null)
        {
            return null;
        }

        return httpContext.RequestServices.GetService<ICurrentPrincipal>()?.Authenticated is { AgentIdentityId: not null } agent
            ? agent.AccountId.ToString()
            : null;
    }

    /// <summary>
    /// The principal the budget belongs to: the AgentIdentity for an agent, the Account for
    /// the person. Without one, the client address, although the session challenge answers
    /// 401 before (DA-109).
    /// </summary>
    public static string PartitionKeyOf(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        var principal = httpContext.RequestServices.GetService<ICurrentPrincipal>()?.Authenticated;
        return principal switch
        {
            null => $"client:{httpContext.Connection.RemoteIpAddress?.ToString() ?? UnknownClient}",

            // Each connected agent has its own budget, apart from the person's (DA-116, DA-030).
            { AgentIdentityId: { } agentIdentityId } => $"{principal.Type}:{agentIdentityId}",
            _ => $"{principal.Type}:{principal.AccountId}",
        };
    }

    private static RateLimitPartition<string> Partition(HttpContext context, string policy, int permitLimit, TimeSpan window) =>
        FixedWindow($"{policy}:{PartitionKeyOf(context)}", permitLimit, window);

    private static RateLimitPartition<string> FixedWindow(string key, int permitLimit, TimeSpan window) =>
        RateLimitPartition.GetFixedWindowLimiter(
            key,
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permitLimit,
                Window = window,
                QueueLimit = 0,
                AutoReplenishment = true,
            });

    private static bool IsSafeMethod(string method) =>
        HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method);

    /// <summary>
    /// The 429 of a budget (DA-116), with the same contract as every error (DA-102): the code,
    /// the V3 body and <c>Retry-After</c> in whole seconds, at least one.
    /// </summary>
    public static Task RefuseAsync(HttpContext httpContext, TimeSpan retryAfter)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        var wholeSecondsAtLeastOne = TimeSpan.FromSeconds(Math.Max(1, Math.Ceiling(retryAfter.TotalSeconds)));
        var responder = httpContext.RequestServices.GetRequiredService<IHttpResultResponder>();
        var refusal = responder.Fail(CommonErrors.TooManyRequests.ToError(TooManyRequestsMessage, retryAfter: wholeSecondsAtLeastOne));
        return refusal.ExecuteAsync(httpContext);
    }

    private static async ValueTask WriteRefusalAsync(OnRejectedContext rejected, TimeSpan window)
    {
        var retryAfter = rejected.Lease.TryGetMetadata(MetadataName.RetryAfter, out var leaseRetryAfter) ? leaseRetryAfter : window;
        await RefuseAsync(rejected.HttpContext, retryAfter);
    }
}

/// <summary>The budgets of <see cref="ApiRateLimiting"/>, per principal and window.</summary>
public sealed class ApiRateLimitOptions
{
    public const string SectionName = "Api:RateLimits";

    /// <summary>Reads per principal in each window: well above the SPA's polling and refetching.</summary>
    [Range(1, 100_000)]
    public int ReadPermitLimit { get; set; } = 600;

    /// <summary>Writes per principal in each window: above what a person reaches by hand in the UI.</summary>
    [Range(1, 100_000)]
    public int WritePermitLimit { get; set; } = 120;

    /// <summary>MCP requests per AgentIdentity in each window: a busy chat, not a script in a loop.</summary>
    [Range(1, 100_000)]
    public int AgentPermitLimit { get; set; } = 300;

    /// <summary>
    /// Calls of read tools per AgentIdentity in each window, inside its MCP budget: a read is
    /// when data leaves for a third-party model, and get_context is the costliest call (E4).
    /// </summary>
    [Range(1, 100_000)]
    public int AgentReadPermitLimit { get; set; } = 120;

    /// <summary>MCP requests of all the agents of one Account in each window (DA-121).</summary>
    [Range(1, 100_000)]
    public int AgentAccountPermitLimit { get; set; } = 600;

    [Range(1, 3_600)]
    public int WindowSeconds { get; set; } = 60;
}

/// <summary>Marks an endpoint whose agent requests also take from the Account's agent ceiling.</summary>
internal sealed class AgentAccountCeiling
{
    public static readonly AgentAccountCeiling Instance = new();

    private AgentAccountCeiling()
    {
    }
}
