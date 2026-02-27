using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MangaScrapper.Infrastructure.BackgroundJobs;

public class BackgroundWorker(IBackgroundTaskQueue taskQueue, ILogger<BackgroundWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Background Worker is starting.");

        while (!stoppingToken.IsCancellationRequested)
        {
            var (workItem, id) = await taskQueue.DequeueAsync(stoppingToken);

            try
            {
                taskQueue.UpdateStatus(id, "Running");
                await workItem(stoppingToken);
                taskQueue.Remove(id);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error occurred executing background work item.");
                taskQueue.UpdateStatus(id, $"Error: {ex.Message}");
            }
        }

        logger.LogInformation("Background Worker is stopping.");
    }
}
