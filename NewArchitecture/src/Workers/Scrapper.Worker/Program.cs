using MangaScrapper.Infrastructure.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NovaStack.Infrastructure.Observability;

var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices((hostContext, services) =>
    {
        // 1. Infrastructure (MongoDB, Scrapers, Repositories, Hangfire Server & Jobs)
        services.AddMangaScrapperInfrastructure(hostContext.Configuration);

        // 2. OpenTelemetry Observability
        services.AddNovaStackObservability("Scrapper.Worker");
    })
    .Build();

await host.RunAsync();
