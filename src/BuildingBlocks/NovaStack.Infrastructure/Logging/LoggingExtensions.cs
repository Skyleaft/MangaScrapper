using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Events;

namespace NovaStack.Infrastructure.Logging;

/// <summary>Serilog bootstrapping extensions for structured logging.</summary>
public static class LoggingExtensions
{
    /// <summary>
    /// Configures Serilog with console + file sinks.
    /// Call early in Program.cs before host is built.
    /// </summary>
    public static IHostBuilder UseNovaStackSerilog(
        this IHostBuilder hostBuilder) =>
        hostBuilder.UseSerilog((context, services, loggerConfig) =>
        {
            var isDevelopment = context.HostingEnvironment.IsDevelopment();

            loggerConfig
                .ReadFrom.Configuration(context.Configuration)
                .ReadFrom.Services(services)
                .MinimumLevel.Information()
                .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
                .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Information)
                .MinimumLevel.Override("Microsoft.AspNetCore.StaticFiles", LogEventLevel.Warning)
                .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
                .MinimumLevel.Override("System", LogEventLevel.Warning)
                .MinimumLevel.Override("System.Net.Http.HttpClient", LogEventLevel.Warning)
                .MinimumLevel.Override("Hangfire", LogEventLevel.Information)
                .MinimumLevel.Override("RabbitMQ.Client", LogEventLevel.Warning)
                .Enrich.FromLogContext()
                .Enrich.WithMachineName()
                .Enrich.WithEnvironmentName()
                .Enrich.WithProcessId()
                .Enrich.WithThreadId()
                .Enrich.WithProperty("Application", context.HostingEnvironment.ApplicationName)
                .WriteTo.Async(wt =>
                {
                    if (isDevelopment)
                    {
                        wt.Console(
                            outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}");
                    }
                    else
                    {
                        wt.Console(new Serilog.Formatting.Compact.CompactJsonFormatter());
                    }

                    wt.File(
                        path: $"logs/{context.HostingEnvironment.ApplicationName}-.log",
                        rollingInterval: RollingInterval.Day,
                        rollOnFileSizeLimit: true,
                        fileSizeLimitBytes: 20 * 1024 * 1024,
                        retainedFileCountLimit: 14,
                        outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff} {Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}");
                });
        });

    /// <summary>Bootstraps a minimal logger for startup errors.</summary>
    public static void BootstrapLogger() =>
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .WriteTo.Console()
            .CreateBootstrapLogger();
}
