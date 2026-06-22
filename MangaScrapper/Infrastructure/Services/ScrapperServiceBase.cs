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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SkiaSharp;

namespace MangaScrapper.Infrastructure.Services;

public abstract class ScrapperServiceBase : IScrapperService
{
    protected const string DefaultIndonesianLanguage = "id";

    protected readonly HttpClient HttpClient;
    protected readonly IMangaRepository MangaRepository;
    protected readonly IBackgroundJobClient JobClient;
    protected readonly IServiceScopeFactory ScopeFactory;
    private readonly ScrapperSettings _settings;
    protected readonly SemaphoreSlim Semaphore;
    protected readonly string ImageStoragePath;
    protected readonly MeilisearchService MeilisearchService;
    protected readonly QdrantService QdrantService;
    protected readonly ILogger Logger;
    protected readonly FlareSolverrService FlareSolverrService;
    private ScrapperProvider? _provider;

    protected ScrapperServiceBase(
        HttpClient httpClient,
        IMangaRepository mangaRepository,
        IBackgroundJobClient jobClient,
        IServiceScopeFactory scopeFactory,
        IOptions<ScrapperSettings> settings,
        SemaphoreSlim semaphore,
        MeilisearchService meilisearchService,
        QdrantService qdrantService,
        ILoggerFactory loggerFactory,
        FlareSolverrService flareSolverrService)
    {
        HttpClient = httpClient;
        MangaRepository = mangaRepository;
        JobClient = jobClient;
        ScopeFactory = scopeFactory;
        _settings = settings.Value;
        Semaphore = semaphore;
        MeilisearchService = meilisearchService;
        QdrantService = qdrantService;
        Logger = loggerFactory.CreateLogger(GetType());
        FlareSolverrService = flareSolverrService;
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

    public async Task<HtmlDocument> GetHtml(string url, string? query = null, HttpContent? formData = null, CancellationToken ct = default)
    {
        return await ExecuteWithRetryAsync(async (token) =>
        {
            if (FlareSolverrService != null && FlareSolverrService.IsEnabled)
            {
                // EnsureSessionAsync guarantees only one FlareSolverr challenge-solve happens per
                // host at a time — concurrent requests wait and share the solved session.
                await FlareSolverrService.EnsureSessionAsync(url, token);
                var html = await FlareSolverrService.GetHtmlAsync(url, formData, token);
                var doc = new HtmlDocument();
                doc.LoadHtml(html);
                return doc;
            }

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

    protected async Task<T?> GetFromJsonAsync<T>(string url, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithRetryAsync<T?>(async (token) =>
        {
            if (FlareSolverrService != null && FlareSolverrService.IsEnabled)
            {
                var jsonText = await FlareSolverrService.GetHtmlAsync(url, ct: token);
                if (jsonText.TrimStart().StartsWith("<"))
                {
                    var doc = new HtmlDocument();
                    doc.LoadHtml(jsonText);
                    var rawJson = doc.DocumentNode.SelectSingleNode("//pre")?.InnerText
                                  ?? doc.DocumentNode.SelectSingleNode("//body")?.InnerText
                                  ?? doc.DocumentNode.InnerText;
                    jsonText = HtmlEntity.DeEntitize(rawJson).Trim();
                }
                return JsonSerializer.Deserialize<T>(jsonText, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            return await HttpClient.GetFromJsonAsync<T>(url, token);
        }, cancellationToken);
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

        var cleaned = string.Concat(title.Split(invalidChars)).TrimEnd('.', ' ');
        return string.IsNullOrEmpty(cleaned) ? "manga" : cleaned;
    }

    private async Task<(string path, long size)> SaveImageAsync(string imageUrl, string subDir, string fileName, string relativePath, CancellationToken ct)
    {
        // Reject relative URLs early — HttpClient requires an absolute URI
        if (!Uri.TryCreate(imageUrl, UriKind.Absolute, out _))
        {
            throw new ArgumentException($"Image URL must be absolute. Got: {imageUrl}", nameof(imageUrl));
        }

        return await ExecuteWithRetryAsync(async (token) =>
        {
            Stream? imageStream = null;
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, imageUrl);
                if (_provider != null)
                {
                    request.Headers.Referrer = new Uri(_provider.BaseUrl);
                }
                var response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
                response.EnsureSuccessStatusCode();
                imageStream = await response.Content.ReadAsStreamAsync(token);
            }
            catch (Exception) when (FlareSolverrService != null && FlareSolverrService.IsEnabled)
            {
                await FlareSolverrService.EnsureSessionAsync(imageUrl, token);
                if (Uri.TryCreate(imageUrl, UriKind.Absolute, out var uri) &&
                    FlareSolverrService.TryGetSession(uri.Host, out var userAgent, out var cookieHeader))
                {
                    using var request = new HttpRequestMessage(HttpMethod.Get, imageUrl);
                    if (_provider != null)
                    {
                        request.Headers.Referrer = new Uri(_provider.BaseUrl);
                    }
                    if (!string.IsNullOrEmpty(userAgent))
                    {
                        request.Headers.UserAgent.Clear();
                        request.Headers.TryAddWithoutValidation("User-Agent", userAgent);
                    }
                    if (!string.IsNullOrEmpty(cookieHeader))
                    {
                        request.Headers.TryAddWithoutValidation("Cookie", cookieHeader);
                    }
                    var response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
                    response.EnsureSuccessStatusCode();
                    imageStream = await response.Content.ReadAsStreamAsync(token);
                }
                else
                {
                    throw;
                }
            }

            using (imageStream)
            {
                // Buffer into MemoryStream: SkiaSharp requires a fully readable (seekable) stream;
                // raw HTTP network streams are forward-only and can cause SKData.Create to fail.
                using var memStream = new MemoryStream();
                await imageStream.CopyToAsync(memStream, token);
                memStream.Position = 0;

                Directory.CreateDirectory(subDir);
                var filePath = Path.Combine(subDir, fileName);

                if (IsWebpUrl(imageUrl))
                {
                    await using var output = File.Create(filePath);
                    await memStream.CopyToAsync(output, token);
                    var size = new FileInfo(filePath).Length;
                    return (relativePath.Replace("\\", "/"), size);
                }

                using var imageData = SKData.Create(memStream);
                using var skImage = SKImage.FromEncodedData(imageData)
                    ?? throw new InvalidOperationException($"SkiaSharp could not decode image from: {imageUrl}");
                using var encoded = skImage.Encode(SKEncodedImageFormat.Webp, 90);
                await Task.Run(() =>
                {
                    using var output = File.Create(filePath);
                    encoded.SaveTo(output);
                }, token);
                var finalSize = new FileInfo(filePath).Length;

                return (relativePath.Replace("\\", "/"), finalSize);
            }
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

        if (mangaInfo != null)
        {
            if (mangaInfo.TitleSynonyms != null)
            {
                var combinedTittleSynonym = string.Join(" ", mangaInfo.TitleSynonyms);
                if (StringHelper.IsSimilar(mangaInfo.Title, manga.Title) ||
                    StringHelper.IsSimilar(mangaInfo.TitleEnglish, manga.Title) ||
                    StringHelper.IsSimilar(combinedTittleSynonym, manga.Title) ||
                    StringHelper.IsSimilar(mangaInfo.TitleJapanese, manga.Title) ||
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
                        "Finished" => "Completed",
                        "Publishing" => "Ongoing",
                        "Hiatus" => "On Hiatus",
                        "Discontinued" => "Discontinued",
                        "Upcoming" => "Upcoming",
                        _ => "Unknown"
                    };
                    if (string.IsNullOrEmpty(manga.Author))
                    {
                        manga.Author = mangaInfo.Authors.FirstOrDefault()?.Name ?? manga.Author;
                    }
                }
            }
        }

        return manga;
    }

    private async Task<MangaDocument> UpdateThumbnail(MangaDocument mangaData, string? imageUrl, CancellationToken ct = default)
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

    private void UpdateChapterViews(MangaDocument existingManga, List<ChapterDocument> chapterDocuments)
    {
        foreach (var item in existingManga.Chapters)
        {
            var chapIndex = chapterDocuments.FirstOrDefault(x => x.Number == item.Number);
            if (chapIndex != null && item.TotalView < chapIndex.TotalView)
            {
                item.TotalView = chapIndex.TotalView;
            }
        }
    }

    public async Task<MangaDocument> ExtractManga(string url, CancellationToken ct, bool scrapChapters = true, string? linkedId = null)
    {
        try
        {
            var mangaData = ExtractMangaMetadata(url);
            mangaData.Url = url;

            if (string.IsNullOrEmpty(mangaData.Title))
            {
                throw new ArgumentException("Missing Manga Title!");
            }

            MangaDocument? existingManga = null;
            if (!string.IsNullOrEmpty(linkedId) && Guid.TryParse(linkedId, out var parsedGuid))
            {
                existingManga = await MangaRepository.GetByIdAsync(parsedGuid, ct);
            }
            else
            {
                var searchmanga = await MeilisearchService.SearchTittleAsync(mangaData.Title, ct);
                if (searchmanga is not null)
                {
                    if (StringHelper.CalculateSimilarity(searchmanga.Title, mangaData.Title) >= 0.8)
                        existingManga = await MangaRepository.GetByIdAsync(Guid.Parse(searchmanga.Id), ct);
                }
            }

            var chapters = await ExtractChaptersMetadata(ct);

            if (existingManga != null)
            {
                existingManga = await UpdateThumbnail(existingManga, mangaData.ImageUrl, ct);

                existingManga.Chapters ??= new List<ChapterDocument>();
                var existingChapterNumbers = existingManga.Chapters.Select(c => c.Number).ToHashSet();
                var newChapters = chapters.Where(c => !existingChapterNumbers.Contains(c.Number)).ToList();

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

                    using (var scope = ScopeFactory.CreateScope())
                    {
                        var webhookService = scope.ServiceProvider.GetService<DiscordWebhookService>();
                        if (webhookService != null)
                        {
                            await webhookService.SendNewChaptersNotificationAsync(existingManga, newChapters, ct);
                        }
                    }
                }

                existingManga = await UpdateMangaDocument(existingManga, ct);
                UpdateChapterViews(existingManga, chapters);
                await MangaRepository.UpdateAsync(existingManga, ct);
                await MeilisearchService.IndexMangaAsync(existingManga, ct);
                await QdrantService.UpsertMangaAsync(existingManga, ct);

                return existingManga;
            }
            mangaData = await UpdateThumbnail(mangaData, mangaData.ImageUrl, ct);
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

            using (var scope = ScopeFactory.CreateScope())
            {
                var webhookService = scope.ServiceProvider.GetService<DiscordWebhookService>();
                if (webhookService != null)
                {
                    await webhookService.SendNewMangaNotificationAsync(manga, chapters, ct);
                }
            }

            return manga;
        }
        catch (Exception ex)
        {
            throw;
        }
    }

    public async Task<MangaDocument> GetDetail(string url, CancellationToken ct)
    {
        var mangaData = ExtractMangaMetadata(url);
        if (string.IsNullOrEmpty(mangaData.Title))
        {
            throw new ArgumentException("Missing Manga Title!");
        }
        var chapters = await ExtractChaptersMetadata(ct);
        mangaData.Chapters = chapters;
        if (mangaData.Type.Contains("-"))
            mangaData.Type = "Manga";
        return mangaData;
    }

    protected abstract MangaDocument ExtractMangaMetadata(string url);
    protected abstract Task<List<ChapterDocument>> ExtractChaptersMetadata(CancellationToken ct = default);

    public virtual async Task<ChapterDocument> GetChapterPage(string mangaTitle, ChapterDocument chapter, CancellationToken ct = default)
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
                Logger.LogError(ex, "Failed to download/convert image at index {Index} for {MangaTitle}", index, mangaTitle);
                throw;
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
