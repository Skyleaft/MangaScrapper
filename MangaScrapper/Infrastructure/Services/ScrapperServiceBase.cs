using Hangfire;
using System.Globalization;
using System.Text.Json;
using System.Web;
using HtmlAgilityPack;
using MangaScrapper.Infrastructure.BackgroundJobs;
using MangaScrapper.Infrastructure.Models;
using MangaScrapper.Shared.Models;
using MangaScrapper.Infrastructure.Mongo.Collections;
using MangaScrapper.Infrastructure.Repositories;
using MangaScrapper.Infrastructure.Utils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Webp;

namespace MangaScrapper.Infrastructure.Services;

public abstract class ScrapperServiceBase : IScrapperService
{
    protected readonly HttpClient HttpClient;
    protected readonly IMangaRepository MangaRepository;
    protected readonly IBackgroundJobClient JobClient;
    protected readonly IServiceScopeFactory ScopeFactory;
    private readonly ScrapperSettings _settings;
    protected readonly SemaphoreSlim Semaphore;
    protected readonly string ImageStoragePath;
    protected readonly MeilisearchService MeilisearchService;
    protected readonly QdrantService QdrantService;
    private ScrapperProvider? _provider;

    protected ScrapperServiceBase(
        HttpClient httpClient,
        IMangaRepository mangaRepository,
        IBackgroundJobClient jobClient,
        IServiceScopeFactory scopeFactory,
        IOptions<ScrapperSettings> settings,
        SemaphoreSlim semaphore,
        MeilisearchService meilisearchService,
        QdrantService qdrantService)
    {
        HttpClient = httpClient;
        MangaRepository = mangaRepository;
        JobClient = jobClient;
        ScopeFactory = scopeFactory;
        _settings = settings.Value;
        Semaphore = semaphore;
        MeilisearchService = meilisearchService;
        QdrantService = qdrantService;
        ImageStoragePath = Path.IsPathRooted(_settings.ImageStoragePath) 
            ? _settings.ImageStoragePath 
            : Path.Combine(Directory.GetCurrentDirectory(), _settings.ImageStoragePath);
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
    
    public async Task<(string path, long size)> DownloadAndConvertToWebP(string mangaTitle, string chapterNumber, string imageUrl, int index, CancellationToken ct = default)
    {
        var cleanTitle = GetCleanTitle(mangaTitle);
        var subDir = Path.Combine(ImageStoragePath, cleanTitle, chapterNumber);
        var fileName = $"{index}.webp";
        
        return await SaveImageAsync(imageUrl, subDir, fileName, $"{cleanTitle}/{chapterNumber}/{fileName}", ct);
    }

    public async Task<(string path, long size)> DownloadThumbnailAndConvertToWebP(string mangaTitle, string imageUrl, CancellationToken ct = default)
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
            return (string.Empty, 0);
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

    private async Task<(string path, long size)> SaveImageAsync(string imageUrl, string subDir, string fileName, string relativePath, CancellationToken ct)
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
                var size = new FileInfo(filePath).Length;
                return (relativePath.Replace("\\", "/"), size);
            }

            using var image = await Image.LoadAsync(imageStream, token);
            await image.SaveAsync(filePath, new WebpEncoder(), token);
            var finalSize = new FileInfo(filePath).Length;

            return (relativePath.Replace("\\", "/"), finalSize);
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

    public async Task<List<JikanMangaItem>> SearchJikan(string title, CancellationToken ct = default)
    {
        var query = HttpUtility.ParseQueryString(string.Empty);
        query["q"] = title;
        query["limit"] = "10";
        
        var url = $"https://api.jikan.moe/v4/manga?{query}";
        try
        {
            var response = await HttpClient.GetFromJsonAsync<JikanMangaResponse>(url, ct);
            return response?.Data ?? new List<JikanMangaItem>();
        }
        catch (Exception)
        {
            return new List<JikanMangaItem>();
        }
    }

    public async Task<JikanMangaItem?> GetMangaInfo(string title, string type, CancellationToken ct = default)
    {
        var results = await SearchJikan(title, ct);
        return results.FirstOrDefault(x => string.Equals(x.Type, type, StringComparison.OrdinalIgnoreCase)) ?? results.FirstOrDefault();
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

    private async Task<MangaDocument> UpdateThumbnail(MangaDocument mangaData,string? imageUrl, CancellationToken ct = default)
    {
        if (!string.IsNullOrWhiteSpace(imageUrl))
        {
            mangaData.ImageUrl = ThumbnailHelper.RemoveResizeParams(imageUrl);
            var thumb = await DownloadThumbnailAndConvertToWebP(mangaData.Title, imageUrl, ct);
            mangaData.LocalImageUrl = thumb.path;
            mangaData.ThumbnailSize = thumb.size;
        }

        return mangaData;
    }

    public async Task<MangaDocument> ExtractManga(string url, CancellationToken ct, bool scrapChapters = true)
    {
        try
        {
            var doc = await GetHtml(url, ct: ct);
            var mangaData = ExtractMangaMetadata(doc);
            mangaData.Url = url;

            if (string.IsNullOrEmpty(mangaData.Title))
            {
                throw new ArgumentException("Missing Manga Title!");
            }
            
            var searchmanga = await MeilisearchService.SearchTittleAsync(mangaData.Title, ct);
            MangaDocument? existingManga = null;
            if (searchmanga is not null)
            {
                if(StringHelper.CalculateSimilarity(searchmanga.Title,mangaData.Title)>=0.8)
                    existingManga = await MangaRepository.GetByIdAsync(Guid.Parse(searchmanga.Id),ct);
            }

            var chapters = await ExtractChapters(doc, ct);

            if (existingManga != null)
            {
                existingManga = await UpdateThumbnail(existingManga,mangaData.ImageUrl, ct);

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
                await MeilisearchService.IndexMangaAsync(existingManga, ct);
                await QdrantService.UpsertMangaAsync(existingManga, ct);

                return existingManga;
            }
            mangaData = await UpdateThumbnail(mangaData,mangaData.ImageUrl, ct);
            mangaData.Chapters = chapters;
            mangaData.CreatedAt = chapters.OrderBy(x => x.UploadDate).FirstOrDefault()?.UploadDate ?? DateTime.MinValue;
            mangaData.UpdatedAt = DateTime.UtcNow;
            if (mangaData.Type.Contains("-"))
                mangaData.Type = "Manga";

            var manga = await UpdateMangaDocument(mangaData, ct);
            manga.Id = Guid.NewGuid();
            await MangaRepository.CreateAsync(manga, ct);
            await MeilisearchService.IndexMangaAsync(manga, ct);
            await QdrantService.UpsertMangaAsync(manga, ct);

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
                var result = await DownloadAndConvertToWebP(
                    mangaTitle,
                    chapter.Number.ToString(CultureInfo.InvariantCulture),
                    imageUrl,
                    index + 1,
                    ct);
                
                return (Index: index, Page: new PageDocument
                {
                    ImageUrl = imageUrl,
                    LocalImageUrl = result.path,
                    Size = result.size
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

    protected async Task EnrichSearchItemAsync(SearchItem item, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(item.Title)) return;
        
        var searchmanga = await MeilisearchService.SearchTittleAsync(item.Title, ct);
        if (searchmanga != null)
        {
            if (StringHelper.CalculateSimilarity(searchmanga.Title, item.Title) >= 0.8)
            {
                var currentManga = await MangaRepository.GetByIdAsync(Guid.Parse(searchmanga.Id), ct);
                item.LatestScrapped = currentManga?.UpdatedAt;
                item.CurrentChapterNumber = currentManga?.Chapters?.Any() == true ? currentManga.Chapters.Max(c => c.Number) : 0;
                item.MangaId = currentManga?.Id;
            }
        }
    }

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
