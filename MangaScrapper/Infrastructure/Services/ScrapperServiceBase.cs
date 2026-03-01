using HtmlAgilityPack;
using MangaScrapper.Infrastructure.BackgroundJobs;
using MangaScrapper.Infrastructure.Repositories;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Webp;

namespace MangaScrapper.Infrastructure.Services;

public abstract class ScrapperServiceBase
{
    protected readonly HttpClient HttpClient;
    protected readonly IMangaRepository MangaRepository;
    protected readonly IBackgroundTaskQueue TaskQueue;
    protected readonly IServiceScopeFactory ScopeFactory;
    private readonly ScrapperSettings _settings;
    protected readonly SemaphoreSlim Semaphore;
    protected readonly string ImageStoragePath;
    private ScrapperProvider? _provider;

    protected ScrapperServiceBase(
        HttpClient httpClient,
        IMangaRepository mangaRepository,
        IBackgroundTaskQueue taskQueue,
        IServiceScopeFactory scopeFactory,
        IOptions<ScrapperSettings> settings,
        SemaphoreSlim semaphore)
    {
        HttpClient = httpClient;
        MangaRepository = mangaRepository;
        TaskQueue = taskQueue;
        ScopeFactory = scopeFactory;
        _settings = settings.Value;
        Semaphore = semaphore;
        ImageStoragePath = Path.IsPathRooted(_settings.ImageStoragePath) 
            ? _settings.ImageStoragePath 
            : Path.Combine(Directory.GetCurrentDirectory(), _settings.ImageStoragePath);
        HttpClient.DefaultRequestHeaders.Add("User-Agent", "minimal-api-httpclient-sample");
        Directory.CreateDirectory(ImageStoragePath);
    }

    protected ScrapperProvider Provider => _provider ?? throw new InvalidOperationException("Provider has not been loaded.");

    protected void LoadProvider(string providerName)
    {
        if (_provider != null) return;
        var path = Path.Combine(Directory.GetCurrentDirectory(), "provider", providerName);
        if (File.Exists(path))
        {
            var json = File.ReadAllTextAsync(path).GetAwaiter().GetResult();
            _provider = System.Text.Json.JsonSerializer.Deserialize<ScrapperProvider>(json);
        }
    }
    
    public async Task<HtmlDocument> GetHtml(string url)
    {
        var response = await HttpClient.GetAsync(url);
        response.EnsureSuccessStatusCode();
        var str = await response.Content.ReadAsStringAsync();
        var doc = new HtmlDocument();
        doc.LoadHtml(str);
        return doc;
    }
    
    public async Task<string> DownloadAndConvertToWebP(string mangaTitle, string chapterNumber, string imageUrl, int index)
    {
        var cleanTitle = GetCleanTitle(mangaTitle);
        var subDir = Path.Combine(ImageStoragePath, cleanTitle, chapterNumber);
        var fileName = $"{index}.webp";
        
        return await SaveImageAsync(imageUrl, subDir, fileName, $"{cleanTitle}/{chapterNumber}/{fileName}");
    }

    public async Task<string> DownloadThumbnailAndConvertToWebP(string mangaTitle, string imageUrl)
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
        using var response = await HttpClient.GetAsync(imageUrl, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        await using var imageStream = await response.Content.ReadAsStreamAsync();
        
        Directory.CreateDirectory(subDir);
        var filePath = Path.Combine(subDir, fileName);

        using var image = await Image.LoadAsync(imageStream);
        await image.SaveAsync(filePath, new WebpEncoder());

        return relativePath.Replace("\\", "/");
    }
}
