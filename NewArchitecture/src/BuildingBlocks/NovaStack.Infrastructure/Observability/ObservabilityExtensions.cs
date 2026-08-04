using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace NovaStack.Infrastructure.Observability;

/// <summary>OpenTelemetry tracing and metrics registration.</summary>
public static class ObservabilityExtensions
{
    /// <summary>
    /// Adds OpenTelemetry with ASP.NET Core, HTTP, and runtime instrumentation.
    /// Configure OTLP endpoint via environment variable OTEL_EXPORTER_OTLP_ENDPOINT,
    /// or use the console exporter for local development.
    /// </summary>
    public static IServiceCollection AddNovaStackObservability(
        this IServiceCollection services,
        string serviceName,
        string serviceVersion = "1.0.0",
        string? otlpEndpoint = null)
    {
        var resourceBuilder = ResourceBuilder
            .CreateDefault()
            .AddService(serviceName, serviceVersion: serviceVersion)
            .AddTelemetrySdk()
            .AddEnvironmentVariableDetector();

        services.AddOpenTelemetry()
            .WithTracing(tracing =>
            {
                tracing
                    .SetResourceBuilder(resourceBuilder)
                    .AddAspNetCoreInstrumentation(options =>
                    {
                        options.RecordException = true;
                        options.Filter = ctx =>
                            ctx.Request.Path.Value is not null &&
                            !ctx.Request.Path.Value.Contains("/health") &&
                            !ctx.Request.Path.Value.Contains("/metrics");
                    })
                    .AddHttpClientInstrumentation(options =>
                    {
                        options.RecordException = true;
                    })
                    .AddSource(serviceName);

                if (!string.IsNullOrWhiteSpace(otlpEndpoint))
                {
                    tracing.AddOtlpExporter(opt => opt.Endpoint = new Uri(otlpEndpoint));
                }
            })
            .WithMetrics(metrics =>
            {
                metrics
                    .SetResourceBuilder(resourceBuilder)
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddPrometheusExporter()
                    .AddRuntimeInstrumentation();

                if (!string.IsNullOrWhiteSpace(otlpEndpoint))
                {
                    metrics.AddOtlpExporter(opt => opt.Endpoint = new Uri(otlpEndpoint));
                }
            });

        return services;
    }
}

