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
            loggerConfig
                .ReadFrom.Configuration(context.Configuration)
                .ReadFrom.Services(services)
                .MinimumLevel.Information()
                .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
                .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
                .MinimumLevel.Override("System", LogEventLevel.Warning)
                .Enrich.FromLogContext()
                .Enrich.WithMachineName()
                .Enrich.WithEnvironmentName()
                .WriteTo.Console(
                    outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}")
                .WriteTo.File(
                    path: "logs/novastack-.log",
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 7,
                    outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff} {Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}");
        });

    /// <summary>Bootstraps a minimal logger for startup errors.</summary>
    public static void BootstrapLogger() =>
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .WriteTo.Console()
            .CreateBootstrapLogger();
}
