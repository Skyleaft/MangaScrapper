using MangaScrapper.Infrastructure.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NovaStack.Infrastructure.Logging;
using NovaStack.Infrastructure.Observability;
using Serilog;

// ── Bootstrap logger (captures startup errors) ───────────────────────────────
LoggingExtensions.BootstrapLogger();

try
{
    Log.Information("Starting Scrapper.Worker...");

    var host = Host.CreateDefaultBuilder(args)
        .UseNovaStackSerilog()
        .ConfigureServices((hostContext, services) =>
        {
            // ── OpenTelemetry Observability ──────────────────────────────────────────
            services.AddNovaStackObservability(
                "Scrapper.Worker",
                otlpEndpoint: hostContext.Configuration["Observability:OtlpEndpoint"]);

            // ── Infrastructure Layer (MongoDB, Scrapers, Repositories, Hangfire Server & Jobs) ────
            services.AddMangaScrapperInfrastructure(
                hostContext.Configuration,
                includeHangfireServer: true,
                includeRabbitMqConsumer: true);
        })
        .Build();

    Log.Information("Scrapper.Worker is running...");

    await host.RunAsync();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "Scrapper.Worker terminated unexpectedly.");
}
finally
{
    Log.CloseAndFlush();
}
