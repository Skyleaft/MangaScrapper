using Hangfire;
using System.Globalization;
using System.Text.Json;
using System.Web;
using HtmlAgilityPack;
using MangaScrapper.Infrastructure.BackgroundJobs;
using MangaScrapper.Infrastructure.Models;
using MangaScrapper.Infrastructure.Mongo.Collections;
using MangaScrapper.Infrastructure.Repositories;
using MangaScrapper.Infrastructure.Utils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Webp;

namespace MangaScrapper.Infrastructure.Services;

public abstract class ScrapperServiceBase
{
    protected readonly HttpClient HttpClient;
    protected readonly IMangaRepository MangaRepository;
    protected readonly IBackgroundJobClient JobClient;
    protected readonly IServiceScopeFactory ScopeFactory;
    private readonly ScrapperSettings _settings;
    protected readonly SemaphoreSlim Semaphore;
    protected readonly string ImageStoragePath;
    private ScrapperProvider? _provider;

    protected ScrapperServiceBase(
        HttpClient httpClient,
        IMangaRepository mangaRepository,
        IBackgroundJobClient jobClient,
        IServiceScopeFactory scopeFactory,
        IOptions<ScrapperSettings> settings,
        SemaphoreSlim semaphore)
    {
        HttpClient = httpClient;
        MangaRepository = mangaRepository;
        JobClient = jobClient;
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
    
    public async Task<HtmlDocument> GetHtml(string url, string? query = null, MultipartFormDataContent? formData = null, CancellationToken ct = default)
    {
        return await ExecuteWithRetryAsync(async (token) =>
        {
            if (formData != null)
            {
                var responseForm = await HttpClient.PostAsync(url, formData, token);
                if (responseForm.StatusCode == System.Net.HttpStatusCode.MovedPermanently ||
                    responseForm.StatusCode == System.Net.HttpStatusCode.Found)
                {
                    var newUrl = responseForm.Headers.Location;
                    if (newUrl != null)
                    {
                        if (!newUrl.IsAbsoluteUri)
                        {
                            newUrl = new Uri(new Uri(url), newUrl);
                        }

                        responseForm = await HttpClient.PostAsync(newUrl, formData, token);
                    }
                }

                responseForm.EnsureSuccessStatusCode();
                var str1 = await responseForm.Content.ReadAsStringAsync(token);
                var doc1 = new HtmlDocument();
                doc1.LoadHtml(str1);
                return doc1;
            }
            else
            {
                var response = await HttpClient.GetAsync(url, token);
                response.EnsureSuccessStatusCode();
                var str = await response.Content.ReadAsStringAsync(token);
                var doc = new HtmlDocument();
                doc.LoadHtml(str);
                return doc;
            }
        }, ct);
    }

    protected async Task<T> ExecuteWithRetryAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken ct, int maxRetries = 3)
    {
        for (var i = 0; i < maxRetries; i++)
        {
            try
            {
                return await action(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw; // Stop retrying if cancellation was requested by the caller
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Net.Sockets.SocketException)
            {
                if (i == maxRetries - 1) throw;
                await Task.Delay(1000 * (i + 1), ct);
            }
        }

        throw new Exception("Retry failed");
    }
    
    public async Task<string> DownloadAndConvertToWebP(string mangaTitle, string chapterNumber, string imageUrl, int index, CancellationToken ct = default)
    {
        var cleanTitle = GetCleanTitle(mangaTitle);
        var subDir = Path.Combine(ImageStoragePath, cleanTitle, chapterNumber);
        var fileName = $"{index}.webp";
        
        return await SaveImageAsync(imageUrl, subDir, fileName, $"{cleanTitle}/{chapterNumber}/{fileName}", ct);
    }

    public async Task<string> DownloadThumbnailAndConvertToWebP(string mangaTitle, string imageUrl, CancellationToken ct = default)
    {
        try
        {
            var cleanTitle = GetCleanTitle(mangaTitle);
            var subDir = Path.Combine(ImageStoragePath, cleanTitle);
            var fileName = "thumbnail.webp";

            return await SaveImageAsync(imageUrl, subDir, fileName, $"{cleanTitle}/{fileName}", ct);
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

    private async Task<string> SaveImageAsync(string imageUrl, string subDir, string fileName, string relativePath, CancellationToken ct)
    {
        return await ExecuteWithRetryAsync(async (token) =>
        {
            using var response = await HttpClient.GetAsync(imageUrl, HttpCompletionOption.ResponseHeadersRead, token);
            response.EnsureSuccessStatusCode();

            await using var imageStream = await response.Content.ReadAsStreamAsync(token);

            Directory.CreateDirectory(subDir);
            var filePath = Path.Combine(subDir, fileName);

            if (IsWebpUrl(imageUrl))
            {
                await using var output = File.Create(filePath);
                await imageStream.CopyToAsync(output, token);
                return relativePath.Replace("\\", "/");
            }

            using var image = await Image.LoadAsync(imageStream, token);
            await image.SaveAsync(filePath, new WebpEncoder(), token);

            return relativePath.Replace("\\", "/");
        }, ct);
    }

    private static bool IsWebpUrl(string imageUrl)
    {
        if (!Uri.TryCreate(imageUrl, UriKind.Absolute, out var uri))
        {
            return imageUrl.Contains(".webp", StringComparison.OrdinalIgnoreCase);
        }

        return string.Equals(Path.GetExtension(uri.AbsolutePath), ".webp", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<JikanMangaItem?> GetMangaInfo(string title, string type, CancellationToken ct = default)
    {
        var query = HttpUtility.ParseQueryString(string.Empty);
        query["q"] = title;
        query["type"] = type;
        query["limit"] = "1";
        
        var url = $"https://api.jikan.moe/v4/manga?{query}";
        try
        {
            var response = await HttpClient.GetFromJsonAsync<JikanMangaResponse>(url, ct);
            return response?.Data?.FirstOrDefault();
        }
        catch (Exception)
        {
            return null;
        }
    }
    public async Task<JikanMangaItem?> GetMangaInfoById(int malId, CancellationToken ct = default)
    {
        var url = $"https://api.jikan.moe/v4/manga/{malId}";
        try
        {
            var response = await HttpClient.GetFromJsonAsync<JikanMangaSingleResponse>(url, ct);
            return response?.Data;
        }
        catch (Exception)
        {
            return null;
        }
    }
    
    public async Task<MangaDocument> UpdateMangaDocument(MangaDocument manga, CancellationToken ct = default)
    {
        JikanMangaItem? mangaInfo;
        if (manga.MalID != null && manga.MalID != 0)
        {
            mangaInfo = await GetMangaInfoById(manga.MalID, ct);
        }
        else
        {
            mangaInfo = await GetMangaInfo(manga.Title, manga.Type, ct);
        }
        
        if (mangaInfo!=null)
        {
            if (mangaInfo.TitleSynonyms != null)
            {
                var combinedTittleSynonym = string.Join(" ", mangaInfo.TitleSynonyms);
                if (StringHelper.IsSimilar(mangaInfo.Title,manga.Title)||
                    StringHelper.IsSimilar(mangaInfo.TitleEnglish,manga.Title)||
                    StringHelper.IsSimilar(combinedTittleSynonym,manga.Title)||
                    StringHelper.IsSimilar(mangaInfo.TitleJapanese,manga.Title) ||
                    mangaInfo.MalId == manga.MalID
                   )
                {
                    manga.MalID = mangaInfo.MalId;
                    manga.Rating = mangaInfo.Score;
                    manga.Popularity = mangaInfo.Popularity;
                    manga.Members = mangaInfo.Members;
                    manga.ReleaseDate = mangaInfo?.Published?.From;
                    manga.Status = mangaInfo.Status switch
                    {
                        "Complete" => "Completed",
                        "Finished"=> "Completed",
                        "Publishing" => "Ongoing",
                        "Hiatus"=> "On Hiatus",
                        "Discontinued"=>"Discontinued",
                        "Upcoming" => "Upcoming",
                        _ => "Unknown"
                    };
                    if(string.IsNullOrEmpty(manga.Author))
                    {
                        manga.Author = mangaInfo.Authors.FirstOrDefault()?.Name ?? manga.Author;
                    }
                }
            }
        }

        return manga;
    }

    public async Task<MangaDocument> ExtractManga(string url, CancellationToken ct, bool scrapChapters = true)
    {
        try
        {
            var doc = await GetHtml(url, ct: ct);
            var mangaData = ExtractMangaMetadata(doc);
            mangaData.Url = url;

            var existingManga = await MangaRepository.GetByTitleAsync(mangaData.Title, ct);

            if (!string.IsNullOrWhiteSpace(mangaData.ImageUrl))
            {
                mangaData.ImageUrl = ThumbnailHelper.RemoveResizeParams(mangaData.ImageUrl);
                mangaData.LocalImageUrl = await DownloadThumbnailAndConvertToWebP(mangaData.Title, mangaData.ImageUrl, ct);
            }

            var chapters = await ExtractChapters(doc, ct);

            if (existingManga != null)
            {
                existingManga.ImageUrl = mangaData.ImageUrl;
                existingManga.LocalImageUrl = mangaData.LocalImageUrl;

                var maxExistingChapter = existingManga.Chapters.Any() ? existingManga.Chapters.Max(c => c.Number) : 0;
                var newChapters = chapters.Where(c => c.Number > maxExistingChapter).ToList();

                if (newChapters.Any())
                {
                    existingManga.Chapters.AddRange(newChapters);
                    existingManga.UpdatedAt = DateTime.UtcNow;

                    if (scrapChapters)
                    {
                        foreach (var chapter in newChapters)
                        {
                            await QueueChapterScraping(existingManga.Id, existingManga.Title, chapter);
                        }
                    }
                }

                existingManga = await UpdateMangaDocument(existingManga, ct);
                await MangaRepository.UpdateAsync(existingManga, ct);

                return existingManga;
            }

            mangaData.Chapters = chapters;
            mangaData.CreatedAt = chapters.OrderBy(x => x.UploadDate).FirstOrDefault()?.UploadDate ?? DateTime.MinValue;
            mangaData.UpdatedAt = DateTime.UtcNow;

            var manga = await UpdateMangaDocument(mangaData, ct);
            await MangaRepository.CreateAsync(manga, ct);

            if (scrapChapters)
            {
                foreach (var chapter in chapters)
                {
                    await QueueChapterScraping(manga.Id, manga.Title, chapter);
                }
            }

            return manga;
        }
        catch (Exception ex)
        {
            throw;
        }
    }

    protected abstract MangaDocument ExtractMangaMetadata(HtmlDocument doc);
    protected abstract Task<List<ChapterDocument>> ExtractChapters(HtmlDocument doc, CancellationToken ct = default);

    public async Task<ChapterDocument> GetChapterPage(string mangaTitle, ChapterDocument chapter, CancellationToken ct = default)
    {
        var url = chapter.Link;
        if (string.IsNullOrWhiteSpace(url)) return chapter;

        if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            url = Provider.BaseUrl.TrimEnd('/') + "/" + url.TrimStart('/');
        }

        var doc = await GetHtml(url, ct: ct);

        var imageNodes = doc.DocumentNode.SelectNodes(Provider.PageSelectors.Images);
        if (imageNodes == null)
        {
            return chapter;
        }

        var downloadTasks = imageNodes.Select(async (imgNode, index) =>
        {
            var imageUrl = imgNode.GetAttributeValue("src", string.Empty);
            if (string.IsNullOrWhiteSpace(imageUrl)) return (Index: index, Page: null as PageDocument);

            await Semaphore.WaitAsync(ct);
            try
            {
                var localPath = await DownloadAndConvertToWebP(
                    mangaTitle,
                    chapter.Number.ToString(CultureInfo.InvariantCulture),
                    imageUrl,
                    index + 1,
                    ct);
                
                return (Index: index, Page: new PageDocument
                {
                    ImageUrl = imageUrl,
                    LocalImageUrl = localPath
                });
            }
            catch (Exception ex)
            {
                return (Index: index, Page: null as PageDocument);
            }
            finally
            {
                Semaphore.Release();
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

    public async Task QueueChapterScraping(Guid mangaId, string mangaTitle, ChapterDocument chapter)
    {
        JobClient.Enqueue<ChapterScrapingJob>(job => job.ExecuteAsync(
            mangaId,
            mangaTitle,
            chapter.Number,
            chapter.Id.ToString(),
            this.GetType().AssemblyQualifiedName!,
            CancellationToken.None));
        
        await Task.CompletedTask;
    }

    public abstract Task<List<SearchItem>> SearchManga(SearchRequest request, CancellationToken ct);

    public async Task<List<ChapterDocument>> GetAllChapters(string url, CancellationToken ct = default)
    {
        var doc = await GetHtml(url, ct: ct);
        return await ExtractChapters(doc, ct);
    }

    public async Task<List<PageDocument>> GetAllPages(string url, CancellationToken ct = default)
    {
        var chapter = new ChapterDocument { Link = url };
        var processedChapter = await GetChapterPage("temp", chapter, ct);
        return processedChapter.Pages;
    }
    
    public async Task<List<ScrapperProvider>> GetAllProvider()
    {
        var providers = new List<ScrapperProvider>();
        var providerFolder = Path.Combine(Directory.GetCurrentDirectory(), "provider");
        
        if (!Directory.Exists(providerFolder))
        {
            return providers;
        }

        var jsonFiles = Directory.GetFiles(providerFolder, "*.json");
        
        foreach (var file in jsonFiles)
        {
            try
            {
                var jsonContent = await File.ReadAllTextAsync(file);
                var provider = JsonSerializer.Deserialize<ScrapperProvider>(jsonContent);
                
                if (provider != null)
                {
                    providers.Add(provider);
                }
            }
            catch (Exception)
            {
                // Skip invalid JSON files
            }
        }

        return providers;
    }
}
