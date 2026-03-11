using FastEndpoints;
using Hangfire;
using MangaScrapper.Infrastructure.Repositories;
using MangaScrapper.Shared.Models;

namespace MangaScrapper.Features.Dashboard.GetStatistics;

public class Endpoint(IMangaRepository mangaRepository) : EndpointWithoutRequest<DashboardStatistic>
{
    public override void Configure()
    {
        Get("/api/dashboard/statistics");
        AllowAnonymous();
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var stats = await mangaRepository.GetStatisticsAsync(ct);
        
        var monitoringApi = JobStorage.Current.GetMonitoringApi();
        stats.TotalQueue = monitoringApi.EnqueuedCount("default") + monitoringApi.FetchedCount("default");
        
        await Send.OkAsync(stats, ct);
    }
}
