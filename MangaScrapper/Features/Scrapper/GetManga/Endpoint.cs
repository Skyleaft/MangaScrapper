using System.Globalization;
using FastEndpoints;
using HtmlAgilityPack;
using MangaScrapper.Infrastructure.Mongo.Collections;
using MangaScrapper.Infrastructure.Repositories;
using MangaScrapper.Infrastructure.Services;

namespace MangaScrapper.Features.Scrapper.GetManga;

public class Endpoint(ScrapperService scrapperService, IMangaRepository mangaRepository) : EndpointWithoutRequest<MangaDocument>
{
    public override void Configure()
    {
        Get("/api/scrapper/manga");
        AllowAnonymous();
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var url = "https://komiku.org/manga/isshiki-san-wa-koi-wo-shiritai/";
        var doc = await scrapperService.GetHtml(url);
        
        var chapters = new List<ChapterDocument>();
        var chapterRows = doc.DocumentNode.SelectNodes("//table[@id='Daftar_Chapter']//tr[position()>1]");
        foreach (var row in chapterRows)
        {
            var link = row.SelectSingleNode(".//td[@class='judulseries']/a")?.GetAttributeValue("href", null);
            var chapterText = row.SelectSingleNode(".//td[@class='judulseries']/a/span")?.InnerText.Trim();
            var viewText = row.SelectSingleNode(".//td[@class='pembaca']/i")?.InnerText.Trim();
            var dateText = row.SelectSingleNode(".//td[@class='tanggalseries']")?.InnerText.Trim();
                
            if (link != null && chapterText != null)
            {
                var chapterNumber = double.TryParse(chapterText.Replace("Chapter ", ""), out var num) ? num : 0;
                var totalView = int.TryParse(viewText, out var view) ? view : 0;
                var uploadDate = DateTime.TryParseExact(dateText, "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : DateTime.MinValue;
                    
                chapters.Add(new ChapterDocument
                {
                    Number = chapterNumber,
                    Link = link,
                    TotalView = totalView,
                    UploadDate = uploadDate
                });
            }
        }
        
        var title = doc.DocumentNode.SelectSingleNode("//td[text()='Judul Komik']/following-sibling::td")?.InnerText.Trim() ?? string.Empty;
        var existingManga = await mangaRepository.GetByTitleAsync(title, ct);
        
        if (existingManga != null)
        {
            var maxExistingChapter = existingManga.Chapters.MaxBy(c => c.Number)?.Number ?? 0;
            var newChapters = chapters.Where(c => c.Number > maxExistingChapter).ToList();
            
            if (newChapters.Any())
            {
                foreach (var chapter in newChapters)
                {
                    await scrapperService.GetChapterPage(chapter);
                }
                existingManga.Chapters.AddRange(newChapters);
                existingManga.UpdatedAt = DateTime.UtcNow;
                await mangaRepository.UpdateAsync(existingManga, ct);
            }
            
            await Send.OkAsync(existingManga, ct);
            return;
        }

        foreach (var chapter in chapters)
        {
            await scrapperService.GetChapterPage(chapter);
        }
        
        var manga = new MangaDocument()
        {
            Title = title,
            Author = doc.DocumentNode.SelectSingleNode("//td[text()='Pengarang']/following-sibling::td")?.InnerText.Trim() ?? string.Empty,
            Description = doc.DocumentNode.SelectSingleNode("//p[@class='desc']")?.InnerText.Trim(),
            Type = doc.DocumentNode.SelectSingleNode("//td[text()='Jenis Komik']/following-sibling::td")?.InnerText.Trim() ?? string.Empty,
            ImageUrl = doc.DocumentNode.SelectSingleNode("//div[@class='ims']/img")?.GetAttributeValue("src", null),
            Status = doc.DocumentNode.SelectSingleNode("//td[text()='Status']/following-sibling::td")?.InnerText.Trim(),
            Genres = doc.DocumentNode.SelectNodes("//ul[@class='genre']/li/a/span")?.Select(n => n.InnerText.Trim()).ToList(),
            Url = url,
            CreatedAt = chapters.OrderBy(x=>x.UploadDate).FirstOrDefault()?.UploadDate ?? DateTime.MinValue,
            Chapters = chapters
        };

        await mangaRepository.CreateAsync(manga, ct);
        await Send.OkAsync(manga, ct);
    }
}