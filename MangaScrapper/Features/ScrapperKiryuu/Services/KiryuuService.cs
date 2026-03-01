using System.Text.RegularExpressions;
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
    
    public async Task<List<KiryuuManga>>SearchManga(string keyword)
    {
        var url = $"{Provider.BaseUrl}/wp-admin/admin-ajax.php?action=advanced_search";
        var formData = new MultipartFormDataContent();
        formData.Add(new StringContent(keyword), "query");
        var doc = await GetHtml(url,null,formData);
        var data = new List<KiryuuManga>();

        var cards = doc.DocumentNode.SelectNodes(
            "//div[contains(@class,'rounded-lg') and .//a[contains(@href,'/manga/')]]"
        );
        
        if (cards != null)
        {
            foreach (var card in cards)
            {
                var title = card
                    .SelectSingleNode(".//img")
                    ?.GetAttributeValue("alt", "")
                    ?.Trim();

                var link = card
                    .SelectSingleNode(".//a[contains(@href,'/manga/')]")
                    ?.GetAttributeValue("href", "");

                var thumb = card
                    .SelectSingleNode(".//img")
                    ?.GetAttributeValue("src", "");

                var rating = card
                    .SelectSingleNode(".//svg[@data-lucide='star']/following-sibling::span[1]")
                    ?.InnerText.Trim();

                var viewsRaw = card
                    .SelectSingleNode(".//svg[contains(@class,'text-gray-400')]/following-sibling::span[1]")
                    ?.InnerText.Trim();
                
                data.Add(new KiryuuManga()
                {
                    Title = title,
                    Link = link,
                    Thumbnail = thumb,
                    Rating = Convert.ToDouble(rating),
                });
            }
        }

        return data;
    }
    
    public async Task<KiryuuManga> GetManga(string url)
    {
        var doc = await GetHtml(url);
        var result = new KiryuuManga();
        result.Link = url;
        result.Title = doc.DocumentNode
            .SelectSingleNode("//h1[@itemprop='name']")
            ?.InnerText.Trim();

        var rate = doc.DocumentNode
            .SelectSingleNode("//small[normalize-space()='Ratings']/preceding-sibling::span[1]/text()")
            ?.InnerText.Trim();
        if(!string.IsNullOrEmpty(rate))
            result.Rating = Convert.ToDouble(rate);
        
        var genreNodes = doc.DocumentNode
            .SelectNodes("//a[@itemprop='genre']/span");
        
        if (genreNodes != null)
        {
            foreach (var g in genreNodes)
                result.Genres.Add(g.InnerText.Trim());
        }
        result.Description = doc.DocumentNode
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
                    result.Author = value;

                if (label.Contains("Artist"))
                    result.Artist = value;

                if (label.Contains("Status"))
                    result.Status = value;
            }
        }
        
        var  thumb= doc.DocumentNode
            .SelectSingleNode("//div[@itemprop='image']//img")
            ?.GetAttributeValue("src", "")
            ?.Trim();
        result.Thumbnail = ThumbnailHelper.RemoveResizeParams(thumb); 
        
        var hxNode = doc.DocumentNode
            .SelectSingleNode("//div[contains(@hx-get,'manga_id=')]");

        var hxGet = hxNode?.GetAttributeValue("hx-get", "");

        var match = Regex.Match(hxGet ?? "", @"manga_id=(\d+)");
        var mangaId = match.Success ? int.Parse(match.Groups[1].Value) : 0;
        result.Id = mangaId;
        
        result.Chapters = await GetAllChapter(result.Id);
        return result;
    }
}
