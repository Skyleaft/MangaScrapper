using System.Globalization;
using System.Text.RegularExpressions;
using System.Web;
using Hangfire;
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
        IBackgroundJobClient jobClient,
        IServiceScopeFactory scopeFactory,
        IOptions<ScrapperSettings> settings,
        SemaphoreSlim semaphore,
        MeilisearchService meilisearchService,
        QdrantService qdrantService) 
        : base(httpClient, mangaRepository, jobClient, scopeFactory, settings, semaphore, meilisearchService, qdrantService)
    {
        LoadProvider("komiku-provider.json");
    }

    protected override MangaDocument ExtractMangaMetadata(HtmlDocument doc)
    {
        return new MangaDocument
        {
            Title = HttpUtility.HtmlDecode(doc.DocumentNode.SelectSingleNode(Provider.MangaSelectors.Title)?.InnerText.Trim() ?? string.Empty),
            Author = doc.DocumentNode.SelectSingleNode(Provider.MangaSelectors.Author)?.InnerText.Trim() ?? string.Empty,
            Description = doc.DocumentNode.SelectSingleNode(Provider.MangaSelectors.Description)?.InnerText.Trim(),
            Type = doc.DocumentNode.SelectSingleNode(Provider.MangaSelectors.Type)?.InnerText.Trim() ?? string.Empty,
            ImageUrl = doc.DocumentNode.SelectSingleNode(Provider.MangaSelectors.Thumbnail)?.GetAttributeValue("src", string.Empty),
            Genres = doc.DocumentNode.SelectNodes(Provider.MangaSelectors.Genres)?.Select(n => n.InnerText.Trim()).ToList()
        };
    }

    protected override Task<List<ChapterDocument>> ExtractChapters(HtmlDocument doc, CancellationToken ct = default)
    {
        var chapters = new List<ChapterDocument>();
        var chapterRows = doc.DocumentNode.SelectNodes(Provider.ChapterSelectors.Rows);
        if (chapterRows == null) return Task.FromResult(chapters);

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

        return Task.FromResult(chapters);
    }

    public override async Task<List<SearchItem>> SearchManga(SearchRequest request, CancellationToken ct)
    {
        var url = $"https://api.komiku.org/manga/page/{request.Page}/?orderby=modified&tipe={request.Type ?? ""}&genre={request.Genres?.FirstOrDefault() ?? ""}&genre2&status={request.Status ?? ""}";
        if (!string.IsNullOrEmpty(request.Keyword))
        {
            url = $"https://api.komiku.org/?post_type=manga&s={HttpUtility.UrlEncode(request.Keyword)}";
        }
        var doc = await GetHtml(url, ct: ct);

        var results = new List<SearchItem>();

        var nodes = doc.DocumentNode.SelectNodes("//div[@class='bge']");
        if (nodes == null) return results;

        foreach (var node in nodes)
        {
            try
            {
                var item = new SearchItem();

                var titleNode = node.SelectSingleNode(".//div[@class='kan']//h3");
                var linkNode = node.SelectSingleNode(".//div[@class='kan']//a[1]");

                item.Title = HttpUtility.HtmlDecode(titleNode?.InnerText.Trim());
                item.DetailUrl = linkNode?.GetAttributeValue("href", "") ?? "";
                if (!string.IsNullOrEmpty(request.Keyword))
                {
                    item.DetailUrl = Provider.BaseUrl+item.DetailUrl;
                }

                var imgNode = node.SelectSingleNode(".//div[@class='bgei']//img");
                item.Thumbnail = imgNode?.GetAttributeValue("src", "") ?? "";

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

                var infoNode = node.SelectSingleNode(".//div[@class='kan']//span[contains(@class,'judul2')]");
                if (infoNode != null)
                {
                    var text = HtmlEntity.DeEntitize(infoNode.InnerText);
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

                var searchmanga = await MeilisearchService.SearchTittleAsync(item.Title!,ct);
                MangaDocument? currentManga = null;
                if (searchmanga != null)
                {
                    if(StringHelper.CalculateSimilarity(searchmanga.Title,item.Title)>=0.8)
                        currentManga = await MangaRepository.GetByIdAsync(Guid.Parse(searchmanga.Id),ct);
                }
                
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
