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
            Guid? currentId = null;
            try
            {
                var (workItem, id) = await taskQueue.DequeueAsync(stoppingToken);
                currentId = id;

                taskQueue.UpdateStatus(id, "Running");
                await workItem(stoppingToken);
                taskQueue.Remove(id);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error occurred executing background work item.");
                if (currentId.HasValue)
                {
                    taskQueue.UpdateStatus(currentId.Value, $"Error: {ex.Message}");
                }
            }
        }

        logger.LogInformation("Background Worker is stopping.");
    }
}
