using LifeGraph.Infrastructure.Observability;
using Microsoft.AspNetCore.HttpLogging;
using Npgsql;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace LifeGraph.Host.Observability;

public static class ObservabilityExtensions
{
    public const string ServiceName = "LifeGraph";

    /// <summary>ASP.NET Core's "Request starting/finished" lines, with the full URL.</summary>
    public const string HostingDiagnosticsCategory = "Microsoft.AspNetCore.Hosting.Diagnostics";

    /// <summary>"Executing RedirectResult, redirecting to …": the issuer's redirects carry the authorization request.</summary>
    public const string RedirectResultCategory = "Microsoft.AspNetCore.Http.Result.RedirectResult";

    public static WebApplicationBuilder AddLifeGraphObservability(this WebApplicationBuilder builder)
    {
        // Structured JSON logs with UTC timestamps (GEN-040); trace/span ids come in through
        // the activity scope, which is what correlates log lines with traces (GEN-042).
        builder.Logging.ClearProviders();
        builder.Logging.Configure(options => options.ActivityTrackingOptions =
            ActivityTrackingOptions.TraceId | ActivityTrackingOptions.SpanId | ActivityTrackingOptions.ParentId);
        builder.Logging.AddJsonConsole(options =>
        {
            options.IncludeScopes = true;
            options.UseUtcTimestamp = true;
            options.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ";
        });
        builder.Logging.AddLifeGraphRedaction();

        // The host's own request lines and its redirect lines carry the query string, and the
        // issuer's carries the authorization request (state, PKCE challenge, redirect) (DA-123,
        // GEN-043). They stay off; the request log below writes method, path, status and
        // duration, never the query.
        builder.Services.AddLogFloor(HostingDiagnosticsCategory, LogLevel.Warning);
        builder.Services.AddLogFloor(RedirectResultCategory, LogLevel.Warning);
        builder.Services.AddHttpLogging(options => options.LoggingFields =
            HttpLoggingFields.RequestMethod | HttpLoggingFields.RequestPath | HttpLoggingFields.ResponseStatusCode | HttpLoggingFields.Duration);

        // TODO(E13): add exporters. Until then the instrumentation is wired but nothing leaves the process.
        builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(ServiceName))
            .WithTracing(tracing => tracing
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddNpgsql())
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation());

        return builder;
    }
}
