using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using MangaScrapper.Infrastructure.BackgroundJobs;
using MangaScrapper.Infrastructure.Models;
using MangaScrapper.Infrastructure.Mongo.Collections;
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

    public async Task<List<ChapterDocument>> GetAllChapter(int mangaId)
    {
        var data = new List<ChapterDocument>();
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

            data.Add(new ChapterDocument
            {
                Number = number,
                Link = link,
                ChapterProvider = Provider.ProviderName,
                ChapterProviderIcon = Provider.ProviderIcon,
                UploadDate = DateTime.TryParse(time, out var parsedDate) ? parsedDate : DateTime.MinValue,
                TotalView = IntHelper.ParseCount(views ?? string.Empty)
            });
        }

        return data;
    }

    public async Task<List<PageDocument>> GetAllPages(string url)
    {
        var data = new List<PageDocument>();
        var doc = await GetHtml(url);

        var pageNodes = doc.DocumentNode.SelectNodes(Provider.PageSelectors.Images);
        if (pageNodes == null) return data;

        foreach (var node in pageNodes)
        {
            data.Add(new PageDocument
            {
                ImageUrl = node.GetAttributeValue("src", "")
            });
        }

        return data;
    }
    
    public async Task<List<SearchItem>>SearchManga(SearchRequest request)
    {
        var url = $"{Provider.BaseUrl}/wp-admin/admin-ajax.php?action=advanced_search";
        var formData = new MultipartFormDataContent();
        
        if (!string.IsNullOrEmpty(request.Keyword))
            formData.Add(new StringContent(request.Keyword), "query");

        if (!string.IsNullOrEmpty(request.Status))
        {
            var statuses = new List<string>();
            statuses.Add(request.Status);
            formData.Add(new StringContent(JsonSerializer.Serialize(statuses)), "status"); 
        }
        
        if (!string.IsNullOrEmpty(request.Type))
        {
            var types = new List<string>();
            types.Add(request.Type);
            formData.Add(new StringContent(JsonSerializer.Serialize(types)), "type");
        }
        
        formData.Add(new StringContent("updated"), "orderby");
        
        formData.Add(new StringContent(request.Page.ToString()), "page");

        if (request.Genres != null && request.Genres.Any())
        {
            var genresJson = JsonSerializer.Serialize(request.Genres);
            formData.Add(new StringContent(genresJson), "genre");
        }
        
        var doc = await GetHtml(url,null,formData);
        var data = new List<SearchItem>();

        var cards = doc.DocumentNode.SelectNodes(
            "//div[div[contains(@class,'rounded-lg')] and div[contains(@class,'group-data-[mode=horizontal]')]]"
        );
        
        if (cards != null)
        {
            foreach (var card in cards)
            {
                // ===== TITLE =====
                var titleNode = card.SelectSingleNode(".//div[contains(@class,'rounded-lg')]//a[contains(@class,'line-clamp-1')]");
                var title = titleNode?.InnerText.Trim();
                var link = titleNode?.GetAttributeValue("href", "");

                // ===== THUMBNAIL =====
                var thumb = card.SelectSingleNode(".//div[contains(@class,'rounded-lg')]//img")
                    ?.GetAttributeValue("src", "");

                // ===== CHAPTER =====
                var chapter = card.SelectSingleNode(".//span[contains(text(),'Chapter')]")
                    ?.InnerText.Trim();
                
                var chapterNumberText = Regex.Match(chapter.Replace("Chapter ", "", StringComparison.OrdinalIgnoreCase), @"\d+(\.\d+)?").Value;
                var chapterNumber = double.TryParse(chapterNumberText, NumberStyles.Float, CultureInfo.InvariantCulture, out var num) ? num : 0;

                // ===== STATUS =====
                var status = card.SelectSingleNode(".//span[contains(@class,'bg-accent')]")
                    ?.InnerText.Trim();

                // ===== RATING =====
                var rating = card.SelectSingleNode(".//span[contains(@class,'text-yellow-400')]")
                    ?.InnerText.Trim();

                // ===== VIEWS =====
                var views = card.SelectSingleNode("( .//div[contains(@class,'space-x-2')]//span )[2]")
                    ?.InnerText.Trim();

                // ===== BOOKMARK =====
                var bookmark = card.SelectSingleNode("( .//div[contains(@class,'space-x-2')]//span )[3]")
                    ?.InnerText.Trim();

                // ===== LATEST TIME (ambil dari horizontal mode) =====
                var timeNode = card.SelectSingleNode(
                    ".//div[contains(@class,'group-data-[mode=horizontal]')]//a[1]//time"
                );

                var latestTime = timeNode?.GetAttributeValue("datetime", "");
                var latestTimeText = timeNode?.InnerText.Trim();

                var currentManga = await MangaRepository.GetByTitleAsync(title, default);
                
                data.Add(new SearchItem()
                {
                    Title = title,
                    DetailUrl = link,
                    Thumbnail = thumb,
                    LatestChapterNumber = chapterNumber,
                    LastUpdateText = latestTimeText,
                    LatestScrapped = currentManga?.UpdatedAt
                    
                });
            }
        }

        return data;
    }
    
    public async Task<MangaDocument> GetManga(string url)
    {
        var doc = await GetHtml(url);
        var manga = new MangaDocument();
        manga.Url = url;
        manga.Title = doc.DocumentNode
            .SelectSingleNode("//h1[@itemprop='name']")
            ?.InnerText.Trim();

        var rate = doc.DocumentNode
            .SelectSingleNode("//small[normalize-space()='Ratings']/preceding-sibling::span[1]/text()")
            ?.InnerText.Trim();
        if(!string.IsNullOrEmpty(rate))
            manga.Rating = Convert.ToDouble(rate);

        var genreNodes = doc.DocumentNode
            .SelectNodes("//a[@itemprop='genre']/span");

        if (genreNodes != null)
        {
            manga.Genres = new List<string>();
            foreach (var g in genreNodes)
                manga.Genres.Add(g.InnerText.Trim());
        }
        manga.Description = doc.DocumentNode
            .SelectSingleNode("//div[@itemprop='description']")
            ?.InnerText.Trim();

        var infoNodes = doc.DocumentNode
            .SelectNodes("//div[contains(@class,'grid')]//h4");

        if (infoNodes != null)
        {
            foreach (var node in infoNodes)
            {
                var label = node.InnerText.Trim();

                var value = node.ParentNode
                    .SelectSingleNode(".//p")
                    ?.InnerText.Trim();

                if (label.Contains("Author"))
                    manga.Author = value;

                if (label.Contains("Status"))
                    manga.Status = value;
            }
        }

        var  thumb= doc.DocumentNode
            .SelectSingleNode("//div[@itemprop='image']//img")
            ?.GetAttributeValue("src", "")
            ?.Trim();
        manga.ImageUrl = ThumbnailHelper.RemoveResizeParams(thumb);

        var hxNode = doc.DocumentNode
            .SelectSingleNode("//div[contains(@hx-get,'manga_id=')]");

        var hxGet = hxNode?.GetAttributeValue("hx-get", "");

        var match = Regex.Match(hxGet ?? "", @"manga_id=(\d+)");
        var mangaId = match.Success ? int.Parse(match.Groups[1].Value) : 0;
        manga.Chapters = await GetAllChapter(mangaId);
        manga.CreatedAt = manga.Chapters.OrderBy(x => x.UploadDate).FirstOrDefault()?.UploadDate ?? DateTime.MinValue;

        manga = await UpdateMangaDocument(manga);
        
        

        return manga;
    }
}
