using System.ComponentModel;
using Hangfire;
using MangaScrapper.Infrastructure.Mongo.Collections;
using MangaScrapper.Infrastructure.Repositories;
using MangaScrapper.Infrastructure.Services;
using MangaScrapper.Infrastructure.Models;
using MangaScrapper.Features.ScrapperKomiku.Services;
using MangaScrapper.Features.ScrapperKiryuu.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MangaScrapper.Infrastructure.BackgroundJobs;

public class LatestChapterScrapingJob(
    IServiceProvider serviceProvider,
    ILogger<LatestChapterScrapingJob> logger)
{
    [Queue("default")]
    public async Task ExecuteAsync(int scrapLastTotalPage, string provider, CancellationToken ct)
    {
        using var scope = serviceProvider.CreateScope();
        IScrapperService? scrapperService = provider.ToLower() switch
        {
            "komiku" => scope.ServiceProvider.GetRequiredService<KomikuService>(),
            "kiryuu" => scope.ServiceProvider.GetRequiredService<KiryuuService>(),
            _ => null
        };

        if (scrapperService == null)
        {
            logger.LogWarning("Provider {Provider} is not supported.", provider);
            return;
        }

        for (int p = 1; p <= scrapLastTotalPage; p++)
        {
            var searchRequest = new SearchRequest { Page = p };
            var searchItems = await scrapperService.SearchManga(searchRequest, ct);

            foreach (var item in searchItems)
            {
                if (item.MangaId!=null && item.LatestChapterNumber > item.CurrentChapterNumber)
                {
                    logger.LogInformation("Found new chapter for {Title} on {Provider}", item.Title, provider);
                    try
                    {
                        await scrapperService.ExtractManga(item.DetailUrl, ct, false);
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Failed to extract new chapters for {Title}", item.Title);
                    }
                }
            }
        }
    }
}
