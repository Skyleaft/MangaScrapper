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
    public readonly string ImageStoragePath;
    private ScrapperProvider? _provider;

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
        ImageStoragePath = Path.IsPathRooted(_settings.ImageStoragePath) 
            ? _settings.ImageStoragePath 
            : Path.Combine(Directory.GetCurrentDirectory(), _settings.ImageStoragePath);
            
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "minimal-api-httpclient-sample");
        Directory.CreateDirectory(ImageStoragePath);
    }

    private async Task LoadProvider()
    {
        if (_provider != null) return;
        var path = Path.Combine(Directory.GetCurrentDirectory(), "provider", "komiku-provider.json");
        if (File.Exists(path))
        {
            var json = await File.ReadAllTextAsync(path);
            _provider = System.Text.Json.JsonSerializer.Deserialize<ScrapperProvider>(json);
        }
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
        await LoadProvider();
        if (_provider == null) return chapter;

        var url = _provider.BaseUrl + chapter.Link;
        var doc = await GetHtml(url);

        var imageNodes = doc.DocumentNode.SelectNodes(_provider.PageSelectors.Images);
        if (imageNodes == null) return chapter;

        var downloadTasks = imageNodes.Select(async (imgNode, index) =>
        {
            var imageUrl = imgNode.GetAttributeValue("src", null);
            if (string.IsNullOrEmpty(imageUrl)) return (Index: index, Page: null as PageDocument);

            await _semaphore.WaitAsync();
            try
            {
                var localPath = await DownloadAndConvertToWebP(mangaTitle, chapter.Number.ToString(CultureInfo.InvariantCulture), imageUrl, index + 1);
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

    private async Task<string> DownloadAndConvertToWebP(string mangaTitle, string chapterNumber, string imageUrl, int index)
    {
        var cleanTitle = GetCleanTitle(mangaTitle);
        var subDir = Path.Combine(ImageStoragePath, cleanTitle, chapterNumber);
        var fileName = $"{index}.webp";
        
        return await SaveImageAsync(imageUrl, subDir, fileName, $"{cleanTitle}/{chapterNumber}/{fileName}");
    }

    private async Task<string> DownloadThumbnailAndConvertToWebP(string mangaTitle, string imageUrl)
    {
        try
        {
            var cleanTitle = GetCleanTitle(mangaTitle);
            var subDir = Path.Combine(ImageStoragePath, cleanTitle);
            var fileName = "thumbnail.webp";

            return await SaveImageAsync(imageUrl, subDir, fileName, $"{cleanTitle}/{fileName}");
        }
        catch
        {
            return string.Empty;
        }
    }

    public string GetCleanTitle(string title)
    {
        // Remove invalid filename characters for both Windows and Linux filesystems
        var invalidChars = Path.GetInvalidFileNameChars()
            .Union(new[] { '?', '*', ':', '|', '<', '>', '"' })
            .ToArray();
        
        return string.Concat(title.Split(invalidChars));
    }

    private async Task<string> SaveImageAsync(string imageUrl, string subDir, string fileName, string relativePath)
    {
        using var response = await _httpClient.GetAsync(imageUrl, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        await using var imageStream = await response.Content.ReadAsStreamAsync();
        
        Directory.CreateDirectory(subDir);
        var filePath = Path.Combine(subDir, fileName);

        using var image = await Image.LoadAsync(imageStream);
        await image.SaveAsync(filePath, new WebpEncoder());

        return relativePath.Replace("\\", "/");
    }

    public async Task<MangaDocument> ExtractMangaMetadata(string url, CancellationToken ct, bool scrapChapters = true)
    {
        await LoadProvider();
        if (_provider == null) throw new Exception("Provider not found");

        var doc = await GetHtml(url);
        
        var title = doc.DocumentNode.SelectSingleNode(_provider.MangaSelectors.Title)?.InnerText.Trim() ?? string.Empty;
        var cleanTitle = GetCleanTitle(title);
        
        var chapters = new List<ChapterDocument>();
        var chapterRows = doc.DocumentNode.SelectNodes(_provider.ChapterSelectors.Rows);
        if (chapterRows != null)
        {
            foreach (var row in chapterRows)
            {
                var link = row.SelectSingleNode(_provider.ChapterSelectors.Link)?.GetAttributeValue("href", null);
                var chapterText = row.SelectSingleNode(_provider.ChapterSelectors.ChapterText)?.InnerText.Trim();
                var viewText = row.SelectSingleNode(_provider.ChapterSelectors.Views)?.InnerText.Trim();
                var dateText = row.SelectSingleNode(_provider.ChapterSelectors.UploadDate)?.InnerText.Trim();
                    
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
        }
        
        var existingManga = await _mangaRepository.GetByTitleAsync(title,ct);
        
        
        
        if (existingManga != null)
        {
            if (string.IsNullOrEmpty(existingManga.LocalImageUrl) && !string.IsNullOrEmpty(existingManga.ImageUrl))
            {
                existingManga.LocalImageUrl = await DownloadThumbnailAndConvertToWebP(existingManga.Title, existingManga.ImageUrl);
                existingManga.UpdatedAt = DateTime.UtcNow;
                await _mangaRepository.UpdateAsync(existingManga, ct);
            }

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

        var thumbnail = doc.DocumentNode.SelectSingleNode(_provider.MangaSelectors.Thumbnail)?.GetAttributeValue("src", null);
        if (!string.IsNullOrEmpty(thumbnail))
        {
            thumbnail = thumbnail.Replace("?w=500", "");
        }

        var localThumbnail = string.Empty;
        if (!string.IsNullOrEmpty(thumbnail))
        {
            localThumbnail = await DownloadThumbnailAndConvertToWebP(title, thumbnail);
        }

        var manga = new MangaDocument()
        {
            Title = title,
            Author = doc.DocumentNode.SelectSingleNode(_provider.MangaSelectors.Author)?.InnerText.Trim() ?? string.Empty,
            Description = doc.DocumentNode.SelectSingleNode(_provider.MangaSelectors.Description)?.InnerText.Trim(),
            Type = doc.DocumentNode.SelectSingleNode(_provider.MangaSelectors.Type)?.InnerText.Trim() ?? string.Empty,
            ImageUrl = thumbnail,
            LocalImageUrl = localThumbnail,
            Status = doc.DocumentNode.SelectSingleNode(_provider.MangaSelectors.Status)?.InnerText.Trim(),
            Genres = doc.DocumentNode.SelectNodes(_provider.MangaSelectors.Genres)?.Select(n => n.InnerText.Trim()).ToList(),
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

    public async Task QueueChapterScraping(Guid mangaId, string mangaTitle, ChapterDocument chapter)
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