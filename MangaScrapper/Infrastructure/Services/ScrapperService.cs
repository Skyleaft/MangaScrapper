using System.Globalization;
using HtmlAgilityPack;
using MangaScrapper.Infrastructure.Mongo.Collections;
using MangaScrapper.Infrastructure.Repositories;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Webp;
using MangaScrapper.Infrastructure.BackgroundJobs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace MangaScrapper.Infrastructure.Services;

public class ScrapperService
{
    private readonly HttpClient _httpClient;
    private readonly IMangaRepository _mangaRepository;
    private readonly IBackgroundTaskQueue _taskQueue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ScrapperSettings _settings;
    private readonly SemaphoreSlim _semaphore;
    private readonly string _imageStoragePath;

    public ScrapperService(
        HttpClient httpClient, 
        IMangaRepository mangaRepository,
        IBackgroundTaskQueue taskQueue,
        IServiceScopeFactory scopeFactory,
        IOptions<ScrapperSettings> settings,
        SemaphoreSlim semaphore)
    {
        _httpClient = httpClient;
        _mangaRepository = mangaRepository;
        _taskQueue = taskQueue;
        _scopeFactory = scopeFactory;
        _settings = settings.Value;
        _semaphore = semaphore;
        _imageStoragePath = Path.IsPathRooted(_settings.ImageStoragePath) 
            ? _settings.ImageStoragePath 
            : Path.Combine(Directory.GetCurrentDirectory(), _settings.ImageStoragePath);
            
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "minimal-api-httpclient-sample");
        Directory.CreateDirectory(_imageStoragePath);
    }

    public async Task<HtmlDocument> GetHtml(string url)
    {
        var response = await _httpClient.GetAsync(url);
        response.EnsureSuccessStatusCode();
        var str = await response.Content.ReadAsStringAsync();
        var doc = new HtmlDocument();
        doc.LoadHtml(str);
        return doc;
    }

    public async Task<ChapterDocument> GetChapterPage(string mangaTitle, ChapterDocument chapter)
    {
        var url = "https://komiku.org" + chapter.Link;
        var doc = await GetHtml(url);

        var imageNodes = doc.DocumentNode.SelectNodes("//div[@id='Baca_Komik']//img[@src]");
        if (imageNodes == null) return chapter;

        var downloadTasks = imageNodes.Select(async (imgNode, index) =>
        {
            var imageUrl = imgNode.GetAttributeValue("src", null);
            if (string.IsNullOrEmpty(imageUrl)) return (Index: index, Page: null as PageDocument);

            await _semaphore.WaitAsync();
            try
            {
                var localPath = await DownloadAndConvertToWebP(mangaTitle, chapter.Number.ToString(CultureInfo.InvariantCulture), imageUrl);
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
                _semaphore.Release();
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

    private async Task<string> DownloadAndConvertToWebP(string mangaTitle, string chapterNumber, string imageUrl)
    {
        using var response = await _httpClient.GetAsync(imageUrl, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        await using var imageStream = await response.Content.ReadAsStreamAsync();
        
        // Clean paths to avoid illegal characters
        var cleanTitle = string.Concat(mangaTitle.Split(Path.GetInvalidFileNameChars()));
        var subDir = Path.Combine(_imageStoragePath, cleanTitle, chapterNumber);
        Directory.CreateDirectory(subDir);

        var fileName = $"{Guid.NewGuid()}.webp";
        var filePath = Path.Combine(subDir, fileName);

        using var image = await Image.LoadAsync(imageStream);
        await image.SaveAsync(filePath, new WebpEncoder());

        return $"/{cleanTitle}/{chapterNumber}/{fileName}";
    }

    public async Task<MangaDocument> ExtractMangaMetadata(string url, CancellationToken ct, bool scrapChapters = true)
    {
        var doc = await GetHtml(url);
        
        var title = doc.DocumentNode.SelectSingleNode("//td[text()='Judul Komik']/following-sibling::td")?.InnerText.Trim() ?? string.Empty;
        
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
        
        var existingManga = await _mangaRepository.GetByTitleAsync(title,ct);
        
        
        
        if (existingManga != null)
        {
            var maxExistingChapter = existingManga.Chapters.MaxBy(c => c.Number)?.Number ?? 0;
            var newChapters = chapters.Where(c => c.Number > maxExistingChapter).ToList();
            
            if (newChapters.Any())
            {
                existingManga.Chapters.AddRange(newChapters);
                existingManga.UpdatedAt = DateTime.UtcNow;
                await _mangaRepository.UpdateAsync(existingManga, ct);

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
        await _mangaRepository.CreateAsync(manga, ct);

        if (scrapChapters)
        {
            foreach (var chapter in chapters)
            {
                await QueueChapterScraping(manga.Id, manga.Title, chapter);
            }
        }
        
        return manga;
    }

    private async Task QueueChapterScraping(Guid mangaId, string mangaTitle, ChapterDocument chapter)
    {
        await _taskQueue.QueueBackgroundWorkItemAsync(mangaTitle, chapter.Number, async token =>
        {
            using var scope = _scopeFactory.CreateScope();
            var scopedScrapper = scope.ServiceProvider.GetRequiredService<ScrapperService>();
            var scopedRepo = scope.ServiceProvider.GetRequiredService<IMangaRepository>();

            var processedChapter = await scopedScrapper.GetChapterPage(mangaTitle, chapter);
            await scopedRepo.UpdateChapterPagesAsync(mangaId, chapter.Id, processedChapter.Pages, token);
        });
    }
    
}