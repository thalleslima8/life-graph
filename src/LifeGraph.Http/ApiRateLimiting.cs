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

    public const string TooManyRequestsMessage = "Too many requests. Wait and try again.";

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
    /// The principal the budget belongs to. Without one, the client address, although the
    /// session challenge answers 401 before (DA-109).
    /// </summary>
    public static string PartitionKeyOf(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        var principal = httpContext.RequestServices.GetService<ICurrentPrincipal>()?.Authenticated;
        return principal is null
            ? $"client:{httpContext.Connection.RemoteIpAddress?.ToString() ?? UnknownClient}"
            : $"{principal.Type}:{principal.AccountId}";
    }

    private static RateLimitPartition<string> Partition(HttpContext context, string policy, int permitLimit, TimeSpan window) =>
        RateLimitPartition.GetFixedWindowLimiter(
            $"{policy}:{PartitionKeyOf(context)}",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permitLimit,
                Window = window,
                QueueLimit = 0,
                AutoReplenishment = true,
            });

    private static bool IsSafeMethod(string method) =>
        HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method);

    // The same contract as every error (DA-102): the code, the V3 body and Retry-After.
    private static async ValueTask WriteRefusalAsync(OnRejectedContext rejected, TimeSpan window)
    {
        var retryAfter = rejected.Lease.TryGetMetadata(MetadataName.RetryAfter, out var leaseRetryAfter) ? leaseRetryAfter : window;
        var wholeSecondsAtLeastOne = TimeSpan.FromSeconds(Math.Max(1, Math.Ceiling(retryAfter.TotalSeconds)));
        var responder = rejected.HttpContext.RequestServices.GetRequiredService<IHttpResultResponder>();
        var refusal = responder.Fail(CommonErrors.TooManyRequests.ToError(TooManyRequestsMessage, retryAfter: wholeSecondsAtLeastOne));
        await refusal.ExecuteAsync(rejected.HttpContext);
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

    [Range(1, 3_600)]
    public int WindowSeconds { get; set; } = 60;
}
