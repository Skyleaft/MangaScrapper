using FastEndpoints;
using MangaScrapper.Infrastructure.Mongo.Collections;
using MangaScrapper.Infrastructure.Repositories;
using MangaScrapper.Infrastructure.Services;

namespace MangaScrapper.Features.ScrapperKomiku.Search;

public class Endpoint(ScrapperService scrapperService, IMangaRepository mangaRepository) : Endpoint<Request,List<SearchItem>>
{
    public override void Configure()
    {
        Get("/api/scrapper/komiku/manga/search");
        AllowAnonymous();
    }

    public override async Task HandleAsync(Request r,CancellationToken ct)
    {
        var url = $"https://api.komiku.org/?post_type=manga&s={r.Query}";
        var doc = await scrapperService.GetHtml(url);
        var result = new List<SearchItem>();
        
        var items = doc.DocumentNode.SelectNodes("//div[@class='bge']");
        if (items == null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        foreach (var node in items)
        {
            var item = new SearchItem();

            // title
            item.Title = node.SelectSingleNode(".//h3").InnerText.Trim();

            // detail url
            item.DetailUrl = "https://komiku.org"+node
                .SelectSingleNode(".//div[@class='bgei']//a")
                ?.GetAttributeValue("href", "");

            // thumbnail
            item.Thumbnail = node
                .SelectSingleNode(".//img")
                ?.GetAttributeValue("src", "");
            if (!string.IsNullOrEmpty(item.Thumbnail))
            {
                item.Thumbnail = item.Thumbnail.Split('?')[0]+"?quality=60;";
            }

            // type
            var typeNode = node.SelectSingleNode(".//div[contains(@class,'tpe1_inf')]/b");
            item.Type = typeNode?.InnerText.Trim();

            // genre
            var genreNode = node.SelectSingleNode(".//div[contains(@class,'tpe1_inf')]");
            if (genreNode != null && item.Type != null)
            {
                item.Genre = genreNode.InnerText
                    .Replace(item.Type, "")
                    .Trim();
            }

            // last update
            item.LastUpdate = node
                .SelectSingleNode(".//div[@class='kan']/p")
                ?.InnerText.Trim();

            // chapters
            var chapters = node.SelectNodes(".//div[@class='new1']/a");
            if (chapters != null && chapters.Count >= 2)
            {
                var span = chapters[1].InnerText.Trim().Split('\n')[1].Replace("Chapter ", "");
                item.LatestChapterNumber = double.TryParse(span, out var num) ? num : 0 ;
            }

            result.Add(item);
        }
        
        await Send.OkAsync(result, ct);
    }
}