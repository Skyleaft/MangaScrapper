using System.Web;
using System.Globalization;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using MangaScrapper.Infrastructure.BackgroundJobs;
using MangaScrapper.Infrastructure.Models;
using MangaScrapper.Infrastructure.Mongo.Collections;
using MangaScrapper.Infrastructure.Repositories;
using MangaScrapper.Infrastructure.Services;
using MangaScrapper.Infrastructure.Utils;
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
        
        var thumbnail = doc.DocumentNode.SelectSingleNode(Provider.MangaSelectors.Thumbnail)?.GetAttributeValue("src", string.Empty);
        if (!string.IsNullOrWhiteSpace(thumbnail))
        {
            thumbnail = ThumbnailHelper.RemoveResizeParams(thumbnail);
        }

        var localThumbnail = string.Empty;
        if (!string.IsNullOrWhiteSpace(thumbnail))
        {
            localThumbnail = await DownloadThumbnailAndConvertToWebP(title, thumbnail);
        }

        if (existingManga != null)
        {
            existingManga.ImageUrl = thumbnail;
            existingManga.LocalImageUrl = localThumbnail;
            
            var maxExistingChapter = existingManga.Chapters.MaxBy(c => c.Number)?.Number ?? 0;
            var newChapters = chapters.Where(c => c.Number > maxExistingChapter).ToList();

            if (newChapters.Any())
            {
                existingManga.Chapters.AddRange(newChapters);
                existingManga.UpdatedAt = DateTime.UtcNow;

                if (scrapChapters)
                {
                    foreach (var chapter in newChapters)
                    {
                        await QueueChapterScraping(existingManga.Id, existingManga.Title, chapter);
                    }
                }
            }

            existingManga = await UpdateMangaDocument(existingManga);
            await MangaRepository.UpdateAsync(existingManga, ct);

            return existingManga;
        }
        

        var manga = new MangaDocument
        {
            Title = title,
            Author = doc.DocumentNode.SelectSingleNode(Provider.MangaSelectors.Author)?.InnerText.Trim() ?? string.Empty,
            Description = doc.DocumentNode.SelectSingleNode(Provider.MangaSelectors.Description)?.InnerText.Trim(),
            Type = doc.DocumentNode.SelectSingleNode(Provider.MangaSelectors.Type)?.InnerText.Trim() ?? string.Empty,
            ImageUrl = thumbnail,
            LocalImageUrl = localThumbnail,
            Genres = doc.DocumentNode.SelectNodes(Provider.MangaSelectors.Genres)?.Select(n => n.InnerText.Trim()).ToList(),
            Url = url,
            CreatedAt = chapters.OrderBy(x => x.UploadDate).FirstOrDefault()?.UploadDate ?? DateTime.MinValue,
            Chapters = chapters
        };

        manga = await UpdateMangaDocument(manga);

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

            var chapterNumberText = Regex.Match(chapterText.Replace("Chapter ", "", StringComparison.OrdinalIgnoreCase), @"\d+(\.\d+)?").Value;
            var chapterNumber = double.TryParse(chapterNumberText, NumberStyles.Float, CultureInfo.InvariantCulture, out var num) ? num : 0;
            var totalView = int.TryParse(viewText, out var view) ? view : 0;
            var uploadDate = DateTime.TryParseExact(dateText, "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                ? date
                : DateTime.MinValue;

            chapters.Add(new ChapterDocument
            {
                Number = chapterNumber,
                Link = link,
                ChapterProvider = Provider.ProviderName,
                ChapterProviderIcon = Provider.ProviderIcon,
                TotalView = totalView,
                UploadDate = uploadDate
            });
        }

        return chapters;
    }

    public async Task<List<SearchItem>> SearchPaged(SearchRequest request,CancellationToken ct)
    {
        var url = $"https://api.komiku.org/manga/page/{request.Page}/?orderby=modified&tipe={request.Type ?? ""}&genre={request.Genres?.FirstOrDefault() ?? ""}&genre2&status={request.Status ?? ""}";
        if (!string.IsNullOrEmpty(request.Keyword))
        {
            url = $"https://api.komiku.org/?post_type=manga&s={HttpUtility.UrlEncode(request.Keyword)}";
        }
        var doc = await GetHtml(url);

        var results = new List<SearchItem>();

        var nodes = doc.DocumentNode.SelectNodes("//div[@class='bge']");
        if (nodes == null) return results;

        foreach (var node in nodes)
        {
            try
            {
                var item = new SearchItem();

                // ========================
                // Title + Detail URL
                // ========================
                var titleNode = node.SelectSingleNode(".//div[@class='kan']//h3");
                var linkNode = node.SelectSingleNode(".//div[@class='kan']//a[1]");

                item.Title = titleNode?.InnerText.Trim();
                item.DetailUrl = linkNode?.GetAttributeValue("href", "") ?? "";

                // ========================
                // Thumbnail
                // ========================
                var imgNode = node.SelectSingleNode(".//div[@class='bgei']//img");
                item.Thumbnail = imgNode?.GetAttributeValue("src", "") ?? "";

                // ========================
                // Type & Genre
                // ========================
                var typeNode = node.SelectSingleNode(".//div[contains(@class,'tpe1_inf')]/b");
                var genreNode = node.SelectSingleNode(".//div[contains(@class,'tpe1_inf')]");

                item.Type = typeNode?.InnerText.Trim() ?? "";

                if (genreNode != null)
                {
                    var genreText = HtmlEntity.DeEntitize(genreNode.InnerText)
                        .Replace(item.Type, "")
                        .Trim();
                    item.Genre = genreText;
                }

                // ========================
                // Last Update Text
                // ========================
                var infoNode = node.SelectSingleNode(".//div[@class='kan']//span[contains(@class,'judul2')]");
                if (infoNode != null)
                {
                    var text = HtmlEntity.DeEntitize(infoNode.InnerText);
                    // contoh: "8.5jt pembaca | 6 menit lalu | Berwarna"
                    var parts = text.Split('|', StringSplitOptions.TrimEntries);
                    if (parts.Length >= 2)
                    {
                        item.LastUpdateText = parts[1];
                    }
                }
                else
                {
                    item.LastUpdateText = node.SelectSingleNode(".//div[@class='kan']/p")?.InnerText.Trim();
                }

                // ========================
                // Latest Chapter
                // ========================
                var latestNode = node.SelectSingleNode(".//div[@class='new1'][2]//span[last()]");
                if (latestNode != null)
                {
                    var match = Regex.Match(latestNode.InnerText, @"([\d\.]+)");
                    if (match.Success &&
                        double.TryParse(match.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var chap))
                    {
                        item.LatestChapterNumber = chap;
                    }
                }

                // ========================
                // Scraped time
                // ========================
                var currentManga = await MangaRepository.GetByTitleAsync(item.Title,ct);
                item.LatestScrapped = currentManga?.UpdatedAt ?? null;

                results.Add(item);
            }
            catch
            {
                // skip item error biar tidak crash
            }
        }

        return results;
        }
}
