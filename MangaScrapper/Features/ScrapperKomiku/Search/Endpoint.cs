using FastEndpoints;
using MangaScrapper.Features.ScrapperKomiku.Services;

namespace MangaScrapper.Features.ScrapperKomiku.Search;

public class Endpoint(KomikuService komikuService) : Endpoint<Request, List<SearchItem>>
{
    public override void Configure()
    {
        Get("/api/scrapper/komiku/manga/search");
        AllowAnonymous();
    }

    public override async Task HandleAsync(Request r, CancellationToken ct)
    {
        var url = $"https://api.komiku.org/?post_type=manga&s={r.Query}";
        var doc = await komikuService.GetHtml(url);
        var result = new List<SearchItem>();

        var items = doc.DocumentNode.SelectNodes("//div[@class='bge']");
        if (items == null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        foreach (var node in items)
        {
            var item = new SearchItem
            {
                Title = node.SelectSingleNode(".//h3")?.InnerText.Trim() ?? string.Empty,
                DetailUrl = "https://komiku.org" + node
                    .SelectSingleNode(".//div[@class='bgei']//a")
                    ?.GetAttributeValue("href", ""),
                Thumbnail = node.SelectSingleNode(".//img")?.GetAttributeValue("src", "")
            };

            if (!string.IsNullOrEmpty(item.Thumbnail))
            {
                item.Thumbnail = item.Thumbnail.Split('?')[0] + "?quality=60;";
            }

            var typeNode = node.SelectSingleNode(".//div[contains(@class,'tpe1_inf')]/b");
            item.Type = typeNode?.InnerText.Trim();

            var genreNode = node.SelectSingleNode(".//div[contains(@class,'tpe1_inf')]");
            if (genreNode != null && item.Type != null)
            {
                item.Genre = genreNode.InnerText.Replace(item.Type, "").Trim();
            }

            item.LastUpdate = node.SelectSingleNode(".//div[@class='kan']/p")?.InnerText.Trim();

            var chapters = node.SelectNodes(".//div[@class='new1']/a");
            if (chapters != null && chapters.Count >= 2)
            {
                var chapterText = chapters[1].InnerText.Trim().Split('\n')[1].Replace("Chapter ", "");
                item.LatestChapterNumber = double.TryParse(chapterText, out var num) ? num : 0;
            }

            result.Add(item);
        }

        await Send.OkAsync(result, ct);
    }
}
