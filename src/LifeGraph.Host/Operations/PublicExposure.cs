using LifeGraph.Accounts.Contracts;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;

namespace LifeGraph.Host.Operations;

/// <summary>
/// The dev tunnel's protections inside the host (DA-028), a second layer behind the tunnel's
/// own ingress rules: a request that arrives under a public hostname reaches only MCP, the
/// discovery documents and the issuer's routes (OAuth, sign-in, consent); everything else is
/// 404. And the tunnel's forwarded scheme and client address are trusted only from the
/// networks configured here, so cookies stay Secure and rate limits see the real client.
/// </summary>
public static class PublicExposure
{
    public const string SectionName = "Host:PublicExposure";

    /// <summary>The client's address as Cloudflare saw it; set by the tunnel on every request.</summary>
    public const string ConnectingIpHeader = "CF-Connecting-IP";

    private static readonly string[] ExposedPrefixes = ["/.well-known/", "/connect/"];

    public static IServiceCollection AddLifeGraphPublicExposure(this IServiceCollection services)
    {
        services.AddOptions<PublicExposureOptions>()
            .BindConfiguration(SectionName)
            .Validate(options => options.KnownNetworks.All(network => System.Net.IPNetwork.TryParse(network, out _)), "Host:PublicExposure:KnownNetworks must be CIDR ranges.")
            .ValidateOnStart();

        services.AddOptions<ForwardedHeadersOptions>().Configure<IOptions<PublicExposureOptions>>((forwarded, exposure) =>
        {
            forwarded.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            forwarded.ForwardLimit = 1;
            forwarded.KnownIPNetworks.Clear();
            forwarded.KnownProxies.Clear();
            foreach (var network in exposure.Value.KnownNetworks)
            {
                forwarded.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(network));
            }
        });

        return services;
    }

    /// <summary>Goes first: everything after it sees the real scheme and client, and nothing else is exposed.</summary>
    public static IApplicationBuilder UseLifeGraphPublicExposure(this IApplicationBuilder app)
    {
        var options = app.ApplicationServices.GetRequiredService<IOptions<PublicExposureOptions>>().Value;
        if (options.KnownNetworks.Count > 0)
        {
            var connectors = options.KnownNetworks.Select(System.Net.IPNetwork.Parse).ToList();

            // The real client (DA-123): CF-Connecting-IP, then X-Forwarded-For, and either only
            // when the request comes from the connector's network; from anywhere else both are
            // ignored, so a forged header never changes a rate-limit key.
            app.Use((httpContext, next) =>
            {
                if (ConnectingClientOf(httpContext, connectors) is { } client)
                {
                    httpContext.Items[ConnectingIpHeader] = client;
                }

                return next(httpContext);
            });
            app.UseForwardedHeaders();
            app.Use((httpContext, next) =>
            {
                if (httpContext.Items[ConnectingIpHeader] is System.Net.IPAddress client)
                {
                    httpContext.Connection.RemoteIpAddress = client;
                }

                return next(httpContext);
            });
        }

        if (options.Hosts.Count == 0)
        {
            return app;
        }

        var publicHosts = new HashSet<string>(options.Hosts, StringComparer.OrdinalIgnoreCase);
        return app.Use(async (httpContext, next) =>
        {
            if (publicHosts.Contains(httpContext.Request.Host.Host) && !IsExposed(httpContext.Request.Path))
            {
                httpContext.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            await next(httpContext);
        });
    }

    /// <summary>
    /// The address in <see cref="ConnectingIpHeader"/> when the request comes straight from a
    /// connector network; <c>null</c> otherwise, or when the header is missing or malformed.
    /// </summary>
    public static System.Net.IPAddress? ConnectingClientOf(HttpContext httpContext, IReadOnlyList<System.Net.IPNetwork> connectors)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        var peer = httpContext.Connection.RemoteIpAddress;
        if (peer is null || !connectors.Any(network => network.Contains(peer.IsIPv4MappedToIPv6 ? peer.MapToIPv4() : peer)))
        {
            return null;
        }

        var header = httpContext.Request.Headers[ConnectingIpHeader];
        return header.Count == 1 && System.Net.IPAddress.TryParse(header[0], out var client) ? client : null;
    }

    public static bool IsExposed(PathString path) =>
        path.Equals(AgentAccess.McpPath, StringComparison.OrdinalIgnoreCase)
        || path.StartsWithSegments(AgentAccess.McpPath, StringComparison.OrdinalIgnoreCase)
        || ExposedPrefixes.Any(prefix => path.Value?.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) == true);
}

public sealed class PublicExposureOptions
{
    /// <summary>The tunnel's public hostnames; a request under one of them reaches only the exposed routes.</summary>
    public List<string> Hosts { get; } = [];

    /// <summary>CIDR ranges of the tunnel connector, whose X-Forwarded-For/-Proto are trusted. Empty: none is.</summary>
    public List<string> KnownNetworks { get; } = [];
}
