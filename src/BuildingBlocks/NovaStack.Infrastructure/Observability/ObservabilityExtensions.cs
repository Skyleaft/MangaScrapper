using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace NovaStack.Infrastructure.Observability;

/// <summary>OpenTelemetry tracing, metrics, and Aspire Dashboard registration.</summary>
public static class ObservabilityExtensions
{
    /// <summary>
    /// Adds OpenTelemetry with ASP.NET Core, HTTP, runtime instrumentation, and Aspire Dashboard support using IConfiguration.
    /// Reads Observability:OtlpEndpoint, Observability:AspireDashboard:*, and Observability:UsePrometheus from configuration.
    /// </summary>
    public static IServiceCollection AddNovaStackObservability(
        this IServiceCollection services,
        IConfiguration configuration,
        string serviceName,
        string serviceVersion = "1.0.0")
    {
        var aspireEnabled = configuration.GetValue<bool>("Observability:AspireDashboard:Enabled")
                            || configuration.GetValue<bool>("Observability:UseAspireDashboard");

        var otlpEndpoint = configuration["Observability:OtlpEndpoint"]
                           ?? configuration["Observability:AspireDashboard:Endpoint"]
                           ?? Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT");

        if (string.IsNullOrWhiteSpace(otlpEndpoint) && aspireEnabled)
        {
            otlpEndpoint = "http://localhost:18889";
        }

        var protocolStr = configuration["Observability:AspireDashboard:Protocol"]
                          ?? configuration["Observability:OtlpProtocol"]
                          ?? Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_PROTOCOL");

        var protocol = string.Equals(protocolStr, "HttpProtobuf", StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(protocolStr, "http/protobuf", StringComparison.OrdinalIgnoreCase)
            ? OtlpExportProtocol.HttpProtobuf
            : OtlpExportProtocol.Grpc;

        var usePrometheus = configuration.GetValue<bool?>("Observability:UsePrometheus:Enabled")
                            ?? configuration.GetValue<bool>("Observability:UsePrometheus", true);

        return services.AddNovaStackObservability(
            serviceName: serviceName,
            serviceVersion: serviceVersion,
            otlpEndpoint: otlpEndpoint,
            usePrometheus: usePrometheus,
            otlpProtocol: protocol);
    }

    /// <summary>
    /// Adds OpenTelemetry with ASP.NET Core, HTTP, and runtime instrumentation.
    /// Configure OTLP endpoint via parameter or environment variable OTEL_EXPORTER_OTLP_ENDPOINT.
    /// </summary>
    public static IServiceCollection AddNovaStackObservability(
        this IServiceCollection services,
        string serviceName,
        string serviceVersion = "1.0.0",
        string? otlpEndpoint = null,
        bool usePrometheus = true,
        OtlpExportProtocol otlpProtocol = OtlpExportProtocol.Grpc)
    {
        var resourceBuilder = ResourceBuilder
            .CreateDefault()
            .AddService(serviceName, serviceVersion: serviceVersion)
            .AddTelemetrySdk()
            .AddEnvironmentVariableDetector();

        var resolvedOtlpEndpoint = otlpEndpoint;
        if (string.IsNullOrWhiteSpace(resolvedOtlpEndpoint))
        {
            resolvedOtlpEndpoint = Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT");
        }

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
                    .AddSource(serviceName)
                    .AddSource("MongoDB.Driver.Core.Extensions.DiagnosticSources");

                if (!string.IsNullOrWhiteSpace(resolvedOtlpEndpoint))
                {
                    tracing.AddOtlpExporter(opt =>
                    {
                        opt.Endpoint = new Uri(resolvedOtlpEndpoint);
                        opt.Protocol = otlpProtocol;
                    });
                }
                else if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT")))
                {
                    tracing.AddOtlpExporter();
                }
            })
            .WithMetrics(metrics =>
            {
                metrics
                    .SetResourceBuilder(resourceBuilder)
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation()
                    .AddProcessInstrumentation();

                if (usePrometheus)
                {
                    metrics.AddPrometheusExporter();
                }

                if (!string.IsNullOrWhiteSpace(resolvedOtlpEndpoint))
                {
                    metrics.AddOtlpExporter(opt =>
                    {
                        opt.Endpoint = new Uri(resolvedOtlpEndpoint);
                        opt.Protocol = otlpProtocol;
                    });
                }
                else if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT")))
                {
                    metrics.AddOtlpExporter();
                }
            });

        return services;
    }

    /// <summary>
    /// Conditionally maps the Prometheus scraping endpoint if Observability:UsePrometheus is enabled.
    /// </summary>
    public static WebApplication MapNovaStackPrometheus(this WebApplication app)
    {
        var usePrometheus = app.Configuration.GetValue<bool?>("Observability:UsePrometheus:Enabled")
                            ?? app.Configuration.GetValue<bool>("Observability:UsePrometheus", true);
        if (usePrometheus)
        {
            app.MapPrometheusScrapingEndpoint();
        }

        return app;
    }
}

