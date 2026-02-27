using FastEndpoints;
using HtmlAgilityPack;
using MangaScrapper.Infrastructure.Services;

namespace MangaScrapper.Features.Scrapper.Get;

public class Endpoint(ScrapperService scrapperService) : EndpointWithoutRequest
{
    public override void Configure()
    {
        Post("/api/scrapper");
        AllowAnonymous();
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var doc = await scrapperService.GetHtml("https://api.komiku.org/manga/");
        
        var result = new List<MangaItem>();

        var nodes = doc.DocumentNode.SelectNodes("//div[contains(@class,'bge')]");

        foreach (var node in nodes)
        {
            var item = new MangaItem();

            // Title
            item.Title = node.SelectSingleNode(".//h3")?.InnerText.Trim();

            // Detail URL
            var detailLink = node.SelectSingleNode(".//div[@class='kan']//a");
            item.DetailUrl = detailLink?.GetAttributeValue("href", "");

            // Slug
            item.Slug = item.DetailUrl?
                .TrimEnd('/')
                .Split('/')
                .Last();

            // Thumbnail
            item.ThumbnailUrl = node.SelectSingleNode(".//img")
                ?.GetAttributeValue("src", "");

            // Type & Genre
            var typeNode = node.SelectSingleNode(".//div[contains(@class,'tpe1_inf')]");
            if (typeNode != null)
            {
                var bold = typeNode.SelectSingleNode(".//b");
                item.Type = bold?.InnerText.Trim();

                item.Genre = typeNode.InnerText
                    .Replace(item.Type ?? "", "")
                    .Trim();
            }

            // Update Rank
            item.UpdateRank = node.SelectSingleNode(".//span[@class='up']")
                ?.InnerText.Trim();

            // Reader & Last Update
            var infoNode = node.SelectSingleNode(".//span[contains(@class,'judul2')]");
            if (infoNode != null)
            {
                var fullText = infoNode.InnerText.Trim();

                item.ReaderCount = infoNode.SelectSingleNode(".//b")
                    ?.InnerText.Trim();

                item.IsColored = fullText.Contains("Berwarna");
                item.LastUpdated = fullText.Split('|').ElementAtOrDefault(1)?.Trim();
            }

            // Description
            item.Description = node.SelectSingleNode(".//p")
                ?.InnerText.Trim();

            // Chapters
            var chapterNodes = node.SelectNodes(".//div[@class='new1']/a");
            if (chapterNodes != null && chapterNodes.Count >= 2)
            {
                item.FirstChapterTitle = chapterNodes[0].InnerText.Trim();
                item.FirstChapterUrl = chapterNodes[0]
                    .GetAttributeValue("href", "");

                item.LatestChapterTitle = chapterNodes[1].InnerText.Trim();
                item.LatestChapterUrl = chapterNodes[1]
                    .GetAttributeValue("href", "");
            }

            result.Add(item);
        }
        
        await Send.OkAsync(result, ct);
    }
}