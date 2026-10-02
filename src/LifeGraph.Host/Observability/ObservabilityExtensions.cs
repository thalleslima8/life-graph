using LifeGraph.Infrastructure.Observability;
using Npgsql;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace LifeGraph.Host.Observability;

public static class ObservabilityExtensions
{
    public const string ServiceName = "LifeGraph";

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
