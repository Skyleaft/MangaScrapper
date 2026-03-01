using MangaScrapper.Infrastructure.BackgroundJobs;
using MangaScrapper.Infrastructure.Repositories;
using MangaScrapper.Infrastructure.Services;
using MangaScrapper.Infrastructure.Utils;
using Microsoft.Extensions.Options;

namespace MangaScrapper.Features.ScrapperKiryuu.Services;

public class KiryuuService : ScrapperServiceBase
{
    public KiryuuService(
        HttpClient httpClient,
        IMangaRepository mangaRepository,
        IBackgroundTaskQueue taskQueue,
        IServiceScopeFactory scopeFactory,
        IOptions<ScrapperSettings> settings,
        SemaphoreSlim semaphore)
        : base(httpClient, mangaRepository, taskQueue, scopeFactory, settings, semaphore)
    {
        LoadProvider("kiryuu-provider.json");
    }

    public async Task<List<KiryuuChapter>> GetAllChapter(int mangaId)
    {
        var data = new List<KiryuuChapter>();
        var url = $"{Provider.BaseUrl}/wp-admin/admin-ajax.php?manga_id={mangaId}&page=1&action=chapter_list";
        var doc = await GetHtml(url);

        var chapterNodes = doc.DocumentNode.SelectNodes(Provider.ChapterSelectors.Rows);
        if (chapterNodes == null) return data;

        foreach (var node in chapterNodes)
        {
            var number = node.GetAttributeValue("data-chapter-number", 0d);
            var link = node.SelectSingleNode(".//a")?.GetAttributeValue("href", "");
            var time = node.SelectSingleNode(".//time")?.GetAttributeValue("datetime", "");
            var views = node
                .SelectSingleNode(Provider.ChapterSelectors.Views)
                ?.InnerText.Trim();

            data.Add(new KiryuuChapter
            {
                ChapterNumber = number,
                Url = link,
                DateAdded = DateTime.TryParse(time, out var parsedDate) ? parsedDate : DateTime.MinValue,
                View = IntHelper.ParseCount(views ?? string.Empty)
            });
        }

        return data;
    }

    public async Task<List<KiryuuPage>> GetAllPages(string url)
    {
        var data = new List<KiryuuPage>();
        var doc = await GetHtml(url);

        var pageNodes = doc.DocumentNode.SelectNodes(Provider.PageSelectors.Images);
        if (pageNodes == null) return data;

        foreach (var node in pageNodes)
        {
            data.Add(new KiryuuPage
            {
                ImageUrl = node.GetAttributeValue("src", ""),
                Alt = node.GetAttributeValue("alt", "")
            });
        }

        return data;
    }
}
