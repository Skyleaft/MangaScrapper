using System.ComponentModel;
using Hangfire;
using MangaScrapper.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MangaScrapper.Infrastructure.BackgroundJobs;

public class MeiliSyncJob(
    IServiceProvider serviceProvider,
    ILogger<MeiliSyncJob> logger)
{
    [Queue("default")]
    [DisplayName("Sync Manga to Meilisearch")]
    public async Task ExecuteAsync(CancellationToken ct)
    {
        logger.LogInformation("Starting Meilisearch sync job...");

        using var scope = serviceProvider.CreateScope();
        var meilisearchService = scope.ServiceProvider.GetRequiredService<MeilisearchService>();

        await meilisearchService.SyncAllAsync(ct);

        logger.LogInformation("Meilisearch sync job completed.");
    }
}
