using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Web;
using HtmlAgilityPack;
using MangaScrapper.Infrastructure.BackgroundJobs;
using MangaScrapper.Infrastructure.Models;
using MangaScrapper.Infrastructure.Mongo.Collections;
using MangaScrapper.Infrastructure.Repositories;
using MangaScrapper.Infrastructure.Services;
using MangaScrapper.Infrastructure.Utils;
using Microsoft.Extensions.DependencyInjection;
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

    protected override MangaDocument ExtractMangaMetadata(HtmlDocument doc)
    {
        var manga = new MangaDocument
        {
            Title = HttpUtility.HtmlDecode(doc.DocumentNode.SelectSingleNode("//h1[@itemprop='name']")?.InnerText.Trim() ?? string.Empty),
            ImageUrl = doc.DocumentNode.SelectSingleNode("//div[@itemprop='image']//img")?.GetAttributeValue("src", "")?.Trim(),
            Description = doc.DocumentNode.SelectSingleNode("//div[@itemprop='description']")?.InnerText.Trim()
        };

        var rate = doc.DocumentNode.SelectSingleNode("//small[normalize-space()='Ratings']/preceding-sibling::span[1]/text()")?.InnerText.Trim();
        if (!string.IsNullOrEmpty(rate))
            manga.Rating = Convert.ToDouble(rate);

        var genreNodes = doc.DocumentNode.SelectNodes("//a[@itemprop='genre']/span");
        if (genreNodes != null)
        {
            manga.Genres = genreNodes.Select(g => g.InnerText.Trim()).ToList();
        }
        
        var typeNode = doc.DocumentNode.SelectSingleNode(
            "//h4/span[normalize-space()='Type']/ancestor::div[contains(@class,'flex')][1]//p"
        );

        if (typeNode != null)
        {
            manga.Type = typeNode.InnerText.Trim();
        }

        manga.Status = "Ongoing";

        var infoNodes = doc.DocumentNode.SelectNodes("//div[contains(@class,'grid')]//h4");
        if (infoNodes != null)
        {
            foreach (var node in infoNodes)
            {
                var label = node.InnerText.Trim();
                var value = node.ParentNode.SelectSingleNode(".//p")?.InnerText.Trim();

                if (label.Contains("Author")) manga.Author = value ?? string.Empty;
                if (label.Contains("Status")) manga.Status = value ?? "Ongoing";
            }
        }

        return manga;
    }

    protected override async Task<List<ChapterDocument>> ExtractChapters(HtmlDocument doc, CancellationToken ct = default)
    {
        var hxNode = doc.DocumentNode.SelectSingleNode("//div[contains(@hx-get,'manga_id=')]");
        var hxGet = hxNode?.GetAttributeValue("hx-get", "");
        var match = Regex.Match(hxGet ?? "", @"manga_id=(\d+)");
        var mangaId = match.Success ? int.Parse(match.Groups[1].Value) : 0;

        if (mangaId == 0) return new List<ChapterDocument>();

        var data = new List<ChapterDocument>();
        var url = $"{Provider.BaseUrl}/wp-admin/admin-ajax.php?manga_id={mangaId}&page=1&action=chapter_list";
        var chaptersDoc = await GetHtml(url, ct: ct);

        var chapterNodes = chaptersDoc.DocumentNode.SelectNodes(Provider.ChapterSelectors.Rows);
        if (chapterNodes == null) return data;

        foreach (var node in chapterNodes)
        {
            var number = node.GetAttributeValue("data-chapter-number", 0d);
            var link = node.SelectSingleNode(".//a")?.GetAttributeValue("href", string.Empty);
            var time = node.SelectSingleNode(".//time")?.GetAttributeValue("datetime", "");
            var views = node.SelectSingleNode(Provider.ChapterSelectors.Views)?.InnerText.Trim();
            
            if (string.IsNullOrWhiteSpace(link)) continue;
            
            var uri = new Uri(link);
            var path = uri.PathAndQuery;

            data.Add(new ChapterDocument
            {
                Number = number,
                Link = path,
                ChapterProvider = Provider.ProviderName,
                ChapterProviderIcon = Provider.ProviderIcon,
                UploadDate = DateTime.TryParse(time, out var parsedDate) ? parsedDate : DateTime.MinValue,
                TotalView = IntHelper.ParseCount(views ?? string.Empty)
            });
        }

        return data;
    }

    public async Task<List<ChapterDocument>> GetAllChaptersById(int mangaId)
    {
        var url = $"{Provider.BaseUrl}/wp-admin/admin-ajax.php?manga_id={mangaId}&page=1&action=chapter_list";
        return await GetAllChapters(url);
    }

    public override async Task<List<SearchItem>> SearchManga(SearchRequest request, CancellationToken ct)
    {
        var url = $"{Provider.BaseUrl}/wp-admin/admin-ajax.php?action=advanced_search";
        var formData = new MultipartFormDataContent();

        if (!string.IsNullOrEmpty(request.Keyword))
            formData.Add(new StringContent(request.Keyword), "query");

        if (!string.IsNullOrEmpty(request.Status))
        {
            var statuses = new List<string> { request.Status };
            formData.Add(new StringContent(JsonSerializer.Serialize(statuses)), "status");
        }

        if (!string.IsNullOrEmpty(request.Type))
        {
            var types = new List<string> { request.Type };
            formData.Add(new StringContent(JsonSerializer.Serialize(types)), "type");
        }

        formData.Add(new StringContent("updated"), "orderby");
        formData.Add(new StringContent(request.Page.ToString()), "page");

        if (request.Genres != null && request.Genres.Any())
        {
            var genresJson = JsonSerializer.Serialize(request.Genres);
            formData.Add(new StringContent(genresJson), "genre");
        }

        var doc = await GetHtml(url, null, formData, ct: ct);
        var data = new List<SearchItem>();

        var cards = doc.DocumentNode.SelectNodes("//div[div[contains(@class,'rounded-lg')] and div[contains(@class,'group-data-[mode=horizontal]')]]");

        if (cards != null)
        {
            foreach (var card in cards)
            {
                var titleNode = card.SelectSingleNode(".//div[contains(@class,'rounded-lg')]//a[contains(@class,'line-clamp-1')]");
                var title = HttpUtility.HtmlDecode(titleNode?.InnerText.Trim());
                var link = titleNode?.GetAttributeValue("href", "");
                var thumb = card.SelectSingleNode(".//div[contains(@class,'rounded-lg')]//img")?.GetAttributeValue("src", "");
                var chapter = card.SelectSingleNode(".//span[contains(text(),'Chapter')]")?.InnerText.Trim();

                var chapterNumberText = Regex.Match(chapter?.Replace("Chapter ", "", StringComparison.OrdinalIgnoreCase) ?? "", @"\d+(\.\d+)?").Value;
                var chapterNumber = double.TryParse(chapterNumberText, NumberStyles.Float, CultureInfo.InvariantCulture, out var num) ? num : 0;

                var timeNode = card.SelectSingleNode(".//div[contains(@class,'group-data-[mode=horizontal]')]//a[1]//time");
                var latestTimeText = timeNode?.InnerText.Trim();

                var currentManga = await MangaRepository.GetByTitleAsync(title ?? string.Empty, ct);

                data.Add(new SearchItem
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
}
