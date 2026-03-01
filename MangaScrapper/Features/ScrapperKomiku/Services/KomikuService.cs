using System.Globalization;
using HtmlAgilityPack;
using MangaScrapper.Infrastructure.BackgroundJobs;
using MangaScrapper.Infrastructure.Mongo.Collections;
using MangaScrapper.Infrastructure.Repositories;
using MangaScrapper.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace MangaScrapper.Features.ScrapperKomiku.Services;

public class KomikuService : ScrapperServiceBase
{
    public KomikuService(
        HttpClient httpClient,
        IMangaRepository mangaRepository,
        IBackgroundTaskQueue taskQueue,
        IServiceScopeFactory scopeFactory,
        IOptions<ScrapperSettings> settings,
        SemaphoreSlim semaphore)
        : base(httpClient, mangaRepository, taskQueue, scopeFactory, settings, semaphore)
    {
        LoadProvider("komiku-provider.json");
    }

    public async Task<MangaDocument> ExtractMangaMetadata(string url, CancellationToken ct, bool scrapChapters = true)
    {
        var doc = await GetHtml(url);

        var title = doc.DocumentNode.SelectSingleNode(Provider.MangaSelectors.Title)?.InnerText.Trim() ?? string.Empty;
        var chapters = ExtractChapters(doc);
        var existingManga = await MangaRepository.GetByTitleAsync(title, ct);

        if (existingManga != null)
        {
            if (string.IsNullOrEmpty(existingManga.LocalImageUrl) && !string.IsNullOrEmpty(existingManga.ImageUrl))
            {
                existingManga.LocalImageUrl = await DownloadThumbnailAndConvertToWebP(existingManga.Title, existingManga.ImageUrl);
                existingManga.UpdatedAt = DateTime.UtcNow;
                await MangaRepository.UpdateAsync(existingManga, ct);
            }

            var maxExistingChapter = existingManga.Chapters.MaxBy(c => c.Number)?.Number ?? 0;
            var newChapters = chapters.Where(c => c.Number > maxExistingChapter).ToList();

            if (newChapters.Any())
            {
                existingManga.Chapters.AddRange(newChapters);
                existingManga.UpdatedAt = DateTime.UtcNow;
                await MangaRepository.UpdateAsync(existingManga, ct);

                if (scrapChapters)
                {
                    foreach (var chapter in newChapters)
                    {
                        await QueueChapterScraping(existingManga.Id, existingManga.Title, chapter);
                    }
                }
            }

            return existingManga;
        }

        var thumbnail = doc.DocumentNode.SelectSingleNode(Provider.MangaSelectors.Thumbnail)?.GetAttributeValue("src", string.Empty);
        if (!string.IsNullOrWhiteSpace(thumbnail))
        {
            thumbnail = thumbnail.Replace("?w=500", "");
        }

        var localThumbnail = string.Empty;
        if (!string.IsNullOrWhiteSpace(thumbnail))
        {
            localThumbnail = await DownloadThumbnailAndConvertToWebP(title, thumbnail);
        }

        var manga = new MangaDocument
        {
            Title = title,
            Author = doc.DocumentNode.SelectSingleNode(Provider.MangaSelectors.Author)?.InnerText.Trim() ?? string.Empty,
            Description = doc.DocumentNode.SelectSingleNode(Provider.MangaSelectors.Description)?.InnerText.Trim(),
            Type = doc.DocumentNode.SelectSingleNode(Provider.MangaSelectors.Type)?.InnerText.Trim() ?? string.Empty,
            ImageUrl = thumbnail,
            LocalImageUrl = localThumbnail,
            Status = doc.DocumentNode.SelectSingleNode(Provider.MangaSelectors.Status)?.InnerText.Trim(),
            Genres = doc.DocumentNode.SelectNodes(Provider.MangaSelectors.Genres)?.Select(n => n.InnerText.Trim()).ToList(),
            Url = url,
            CreatedAt = chapters.OrderBy(x => x.UploadDate).FirstOrDefault()?.UploadDate ?? DateTime.MinValue,
            Chapters = chapters
        };

        await MangaRepository.CreateAsync(manga, ct);

        if (scrapChapters)
        {
            foreach (var chapter in chapters)
            {
                await QueueChapterScraping(manga.Id, manga.Title, chapter);
            }
        }

        return manga;
    }

    public async Task<ChapterDocument> GetChapterPage(string mangaTitle, ChapterDocument chapter)
    {
        var url = Provider.BaseUrl + chapter.Link;
        var doc = await GetHtml(url);

        var imageNodes = doc.DocumentNode.SelectNodes(Provider.PageSelectors.Images);
        if (imageNodes == null) return chapter;

        var downloadTasks = imageNodes.Select(async (imgNode, index) =>
        {
            var imageUrl = imgNode.GetAttributeValue("src", string.Empty);
            if (string.IsNullOrWhiteSpace(imageUrl)) return (Index: index, Page: null as PageDocument);

            await Semaphore.WaitAsync();
            try
            {
                var localPath = await DownloadAndConvertToWebP(
                    mangaTitle,
                    chapter.Number.ToString(CultureInfo.InvariantCulture),
                    imageUrl,
                    index + 1);
                return (Index: index, Page: new PageDocument
                {
                    ImageUrl = imageUrl,
                    LocalImageUrl = localPath
                });
            }
            catch
            {
                return (Index: index, Page: null as PageDocument);
            }
            finally
            {
                Semaphore.Release();
            }
        });

        var results = await Task.WhenAll(downloadTasks);

        var orderedPages = results
            .OrderBy(r => r.Index)
            .Where(r => r.Page != null)
            .Select(r => r.Page!)
            .ToList();

        chapter.Pages.AddRange(orderedPages);
        return chapter;
    }

    public async Task QueueChapterScraping(Guid mangaId, string mangaTitle, ChapterDocument chapter)
    {
        await TaskQueue.QueueBackgroundWorkItemAsync(mangaTitle, chapter.Number, async token =>
        {
            using var scope = ScopeFactory.CreateScope();
            var scopedScrapper = scope.ServiceProvider.GetRequiredService<KomikuService>();
            var scopedRepo = scope.ServiceProvider.GetRequiredService<IMangaRepository>();

            var processedChapter = await scopedScrapper.GetChapterPage(mangaTitle, chapter);
            await scopedRepo.UpdateChapterPagesAsync(mangaId, chapter.Id, processedChapter.Pages, token);
        });
    }

    private List<ChapterDocument> ExtractChapters(HtmlDocument doc)
    {
        var chapters = new List<ChapterDocument>();
        var chapterRows = doc.DocumentNode.SelectNodes(Provider.ChapterSelectors.Rows);
        if (chapterRows == null) return chapters;

        foreach (var row in chapterRows)
        {
            var link = row.SelectSingleNode(Provider.ChapterSelectors.Link)?.GetAttributeValue("href", string.Empty);
            var chapterText = row.SelectSingleNode(Provider.ChapterSelectors.ChapterText)?.InnerText.Trim();
            var viewText = row.SelectSingleNode(Provider.ChapterSelectors.Views)?.InnerText.Trim();
            var dateText = row.SelectSingleNode(Provider.ChapterSelectors.UploadDate)?.InnerText.Trim();

            if (string.IsNullOrWhiteSpace(link) || chapterText == null) continue;

            var chapterNumber = double.TryParse(chapterText.Replace("Chapter ", ""), out var num) ? num : 0;
            var totalView = int.TryParse(viewText, out var view) ? view : 0;
            var uploadDate = DateTime.TryParseExact(dateText, "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                ? date
                : DateTime.MinValue;

            chapters.Add(new ChapterDocument
            {
                Number = chapterNumber,
                Link = link,
                TotalView = totalView,
                UploadDate = uploadDate
            });
        }

        return chapters;
    }
}
