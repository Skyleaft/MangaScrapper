using Hangfire;
using System.Globalization;
using System.Text.Json;
using System.Web;
using HtmlAgilityPack;
using MangaScrapper.Infrastructure.BackgroundJobs;
using MangaScrapper.Infrastructure.Configuration;
using MangaScrapper.Infrastructure.Persistence.Documents;
using MangaScrapper.Infrastructure.Utils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SkiaSharp;
using NovaStack.Contracts.Responses;
using MangaScrapper.Application.Common.Abstractions;

namespace MangaScrapper.Infrastructure.Scrapers;

public interface IScrapperService
{
    Task<HtmlDocument> GetHtml(string url, string? query = null, HttpContent? formData = null, CancellationToken ct = default);
    Task<(string path, long size)> DownloadAndConvertToWebP(string mangaTitle, string chapterNumber, string imageUrl, int index, CancellationToken ct = default);
    Task<(string path, long size)> DownloadThumbnailAndConvertToWebP(string mangaTitle, string imageUrl, CancellationToken ct = default);
    string GetCleanTitle(string title);
    Task<JikanMangaItem?> GetMangaInfo(string title, string type, CancellationToken ct = default);
    Task<JikanMangaItem?> GetMangaInfoById(int malId, CancellationToken ct = default);
    Task<MangaDocument> UpdateMangaDocument(MangaDocument manga, CancellationToken ct = default);
    Task<MangaDocument> ExtractManga(string url, CancellationToken ct, bool scrapChapters = true, string? linkedId = null);
    Task<MangaDocument> GetDetail(string url, CancellationToken ct);
    Task<ChapterDocument> GetChapterPage(string mangaTitle, ChapterDocument chapter, CancellationToken ct = default);
    Task QueueChapterScraping(Guid mangaId, string mangaTitle, ChapterDocument chapter);
    Task<List<SearchItem>> SearchManga(SearchRequest request, CancellationToken ct);
    Task<List<JikanMangaItem>> SearchJikan(string title, CancellationToken ct = default);
    Task<List<PageDocument>> GetAllPages(string url, CancellationToken ct = default);
    Task<List<ScrapperProvider>> GetAllProvider();
}

public abstract class ScrapperServiceBase : IScrapperService, IProviderScrapperService
{
    protected const string DefaultIndonesianLanguage = "id";

    protected readonly HttpClient HttpClient;
    protected readonly IBackgroundJobClient JobClient;
    protected readonly IServiceScopeFactory ScopeFactory;
    protected readonly SemaphoreSlim Semaphore;
    protected readonly string ImageStoragePath;
    protected readonly MeilisearchService MeilisearchService;
    protected readonly QdrantService QdrantService;
    protected readonly ILogger Logger;
    protected readonly FlareSolverrService FlareSolverrService;
    private ScrapperProvider? _provider;

    // Repository for legacy document-based access used by scrapers
    private readonly IScrapperRepository _scraperRepo;

    protected ScrapperServiceBase(
        HttpClient httpClient,
        IScrapperRepository scraperRepo,
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
        _scraperRepo = scraperRepo;
        JobClient = jobClient;
        ScopeFactory = scopeFactory;
        Semaphore = semaphore;
        MeilisearchService = meilisearchService;
        QdrantService = qdrantService;
        Logger = loggerFactory.CreateLogger(GetType());
        FlareSolverrService = flareSolverrService;
        var path = settings.Value.ImageStoragePath;
        ImageStoragePath = Path.IsPathRooted(path) ? path : Path.Combine(Directory.GetCurrentDirectory(), path);
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
            _provider = JsonSerializer.Deserialize<ScrapperProvider>(json);
        }
    }

    public async Task<HtmlDocument> GetHtml(string url, string? query = null, HttpContent? formData = null, CancellationToken ct = default)
    {
        return await ExecuteWithRetryAsync(async token =>
        {
            if (FlareSolverrService is { IsEnabled: true })
            {
                await FlareSolverrService.EnsureSessionAsync(url, token);
                var html = await FlareSolverrService.GetHtmlAsync(url, formData, token);
                var doc = new HtmlDocument();
                doc.LoadHtml(html);
                return doc;
            }

            if (formData != null)
            {
                var responseForm = await HttpClient.PostAsync(url, formData, token);
                if (responseForm.StatusCode is System.Net.HttpStatusCode.MovedPermanently or System.Net.HttpStatusCode.Found)
                {
                    var newUrl = responseForm.Headers.Location;
                    if (newUrl != null)
                    {
                        if (!newUrl.IsAbsoluteUri) newUrl = new Uri(new Uri(url), newUrl);
                        responseForm = await HttpClient.PostAsync(newUrl, formData, token);
                    }
                }
                responseForm.EnsureSuccessStatusCode();
                var str1 = await responseForm.Content.ReadAsStringAsync(token);
                var doc1 = new HtmlDocument();
                doc1.LoadHtml(str1);
                return doc1;
            }

            var response = await HttpClient.GetAsync(url, token);
            response.EnsureSuccessStatusCode();
            var str = await response.Content.ReadAsStringAsync(token);
            var doc2 = new HtmlDocument();
            doc2.LoadHtml(str);
            return doc2;
        }, ct);
    }

    protected async Task<T?> GetFromJsonAsync<T>(string url, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithRetryAsync<T?>(async token =>
        {
            if (FlareSolverrService is { IsEnabled: true })
            {
                var jsonText = await FlareSolverrService.GetHtmlAsync(url, ct: token);
                if (jsonText.TrimStart().StartsWith('<'))
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
            try { return await action(ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
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
        var ext = IsAvifUrl(imageUrl) ? ".avif" : ".webp";
        var fileName = $"{index}{ext}";
        return await SaveImageAsync(imageUrl, subDir, fileName, $"{cleanTitle}/{chapterNumber}/{fileName}", ct);
    }

    public async Task<(string path, long size)> DownloadThumbnailAndConvertToWebP(string mangaTitle, string imageUrl, CancellationToken ct = default)
    {
        try
        {
            var cleanTitle = GetCleanTitle(mangaTitle);
            var subDir = Path.Combine(ImageStoragePath, cleanTitle);
            var ext = IsAvifUrl(imageUrl) ? ".avif" : ".webp";
            return await SaveImageAsync(imageUrl, subDir, $"thumbnail{ext}", $"{cleanTitle}/thumbnail{ext}", ct);
        }
        catch { return (string.Empty, 0); }
    }

    public string GetCleanTitle(string title)
    {
        var invalidChars = Path.GetInvalidFileNameChars().Union(new[] { '?', '*', ':', '|', '<', '>', '"' }).ToArray();
        var cleaned = string.Concat(title.Split(invalidChars)).TrimEnd('.', ' ');
        return string.IsNullOrEmpty(cleaned) ? "manga" : cleaned;
    }

    private async Task<(string path, long size)> SaveImageAsync(string imageUrl, string subDir, string fileName, string relativePath, CancellationToken ct)
    {
        if (!Uri.TryCreate(imageUrl, UriKind.Absolute, out _))
            throw new ArgumentException($"Image URL must be absolute. Got: {imageUrl}", nameof(imageUrl));

        return await ExecuteWithRetryAsync(async token =>
        {
            Stream? imageStream = null;
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, imageUrl);
                if (_provider != null)
                {
                    if (_provider.ProviderName == "MangaDex")
                    {
                        request.Headers.UserAgent.Clear();
                        request.Headers.TryAddWithoutValidation("User-Agent", "MangaScrapper/1.0");
                    }
                    else { request.Headers.Referrer = new Uri(_provider.BaseUrl); }
                }
                var response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
                response.EnsureSuccessStatusCode();
                imageStream = await response.Content.ReadAsStreamAsync(token);
            }
            catch (Exception) when (FlareSolverrService is { IsEnabled: true })
            {
                await FlareSolverrService.EnsureSessionAsync(imageUrl, token);
                if (Uri.TryCreate(imageUrl, UriKind.Absolute, out var uri) &&
                    FlareSolverrService.TryGetSession(uri.Host, out var userAgent, out var cookieHeader))
                {
                    using var req2 = new HttpRequestMessage(HttpMethod.Get, imageUrl);
                    if (_provider != null) req2.Headers.Referrer = new Uri(_provider.BaseUrl);
                    if (!string.IsNullOrEmpty(userAgent)) { req2.Headers.UserAgent.Clear(); req2.Headers.TryAddWithoutValidation("User-Agent", userAgent); }
                    if (!string.IsNullOrEmpty(cookieHeader)) req2.Headers.TryAddWithoutValidation("Cookie", cookieHeader);
                    var response2 = await HttpClient.SendAsync(req2, HttpCompletionOption.ResponseHeadersRead, token);
                    response2.EnsureSuccessStatusCode();
                    imageStream = await response2.Content.ReadAsStreamAsync(token);
                }
                else throw;
            }

            using (imageStream)
            {
                using var memStream = new MemoryStream();
                await imageStream!.CopyToAsync(memStream, token);
                memStream.Position = 0;
                Directory.CreateDirectory(subDir);
                var filePath = Path.Combine(subDir, fileName);

                if (IsWebpUrl(imageUrl) || IsAvifUrl(imageUrl))
                {
                    await using var output = File.Create(filePath);
                    await memStream.CopyToAsync(output, token);
                    return (relativePath.Replace("\\", "/"), new FileInfo(filePath).Length);
                }

                try
                {
                    using var imageData = SKData.Create(memStream);
                    using var skImage = SKImage.FromEncodedData(imageData)
                        ?? throw new InvalidOperationException($"SkiaSharp could not decode image from: {imageUrl}");
                    using var encoded = skImage.Encode(SKEncodedImageFormat.Webp, 90);
                    await Task.Run(() => { using var out2 = File.Create(filePath); encoded.SaveTo(out2); }, token);
                    return (relativePath.Replace("\\", "/"), new FileInfo(filePath).Length);
                }
                catch (Exception ex)
                {
                    Logger.LogWarning(ex, "SkiaSharp failed to decode {ImageUrl}. Saving raw stream.", imageUrl);
                    memStream.Position = 0;
                    await using var out3 = File.Create(filePath);
                    await memStream.CopyToAsync(out3, token);
                    return (relativePath.Replace("\\", "/"), new FileInfo(filePath).Length);
                }
            }
        }, ct);
    }

    private static bool IsWebpUrl(string imageUrl)
    {
        if (!Uri.TryCreate(imageUrl, UriKind.Absolute, out var uri))
            return imageUrl.Contains(".webp", StringComparison.OrdinalIgnoreCase);
        return string.Equals(Path.GetExtension(uri.AbsolutePath), ".webp", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAvifUrl(string imageUrl)
    {
        if (!Uri.TryCreate(imageUrl, UriKind.Absolute, out var uri))
            return imageUrl.Contains(".avif", StringComparison.OrdinalIgnoreCase);
        return string.Equals(Path.GetExtension(uri.AbsolutePath), ".avif", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<List<JikanMangaItem>> SearchJikan(string title, CancellationToken ct = default)
    {
        var query = HttpUtility.ParseQueryString(string.Empty);
        query["q"] = title;
        query["limit"] = "10";
        try
        {
            var response = await HttpClient.GetFromJsonAsync<JikanMangaResponse>($"https://api.jikan.moe/v4/manga?{query}", ct);
            return response?.Data ?? new List<JikanMangaItem>();
        }
        catch { return new List<JikanMangaItem>(); }
    }

    public async Task<JikanMangaItem?> GetMangaInfo(string title, string type, CancellationToken ct = default)
    {
        var results = await SearchJikan(title, ct);
        return results.FirstOrDefault(x => string.Equals(x.Type, type, StringComparison.OrdinalIgnoreCase)) ?? results.FirstOrDefault();
    }

    public async Task<JikanMangaItem?> GetMangaInfoById(int malId, CancellationToken ct = default)
    {
        try
        {
            var response = await HttpClient.GetFromJsonAsync<JikanMangaSingleResponse>($"https://api.jikan.moe/v4/manga/{malId}", ct);
            return response?.Data;
        }
        catch { return null; }
    }

    public async Task<MangaDocument> UpdateMangaDocument(MangaDocument manga, CancellationToken ct = default)
    {
        JikanMangaItem? mangaInfo = manga.MalID != 0
            ? await GetMangaInfoById(manga.MalID, ct)
            : await GetMangaInfo(manga.Title, manga.Type, ct);

        if (mangaInfo?.TitleSynonyms != null)
        {
            var combinedSynonyms = string.Join(" ", mangaInfo.TitleSynonyms);
            if (StringHelper.IsSimilar(mangaInfo.Title, manga.Title) ||
                StringHelper.IsSimilar(mangaInfo.TitleEnglish ?? "", manga.Title) ||
                StringHelper.IsSimilar(combinedSynonyms, manga.Title) ||
                StringHelper.IsSimilar(mangaInfo.TitleJapanese ?? "", manga.Title) ||
                mangaInfo.MalId == manga.MalID)
            {
                manga.MalID = mangaInfo.MalId;
                manga.Rating = mangaInfo.Score;
                manga.Popularity = mangaInfo.Popularity;
                manga.Members = mangaInfo.Members;
                manga.ReleaseDate = mangaInfo.Published?.From;
                manga.Status = mangaInfo.Status switch
                {
                    "Complete" or "Finished" => "Completed",
                    "Publishing" => "Ongoing",
                    "Hiatus" => "On Hiatus",
                    "Discontinued" => "Discontinued",
                    "Upcoming" => "Upcoming",
                    _ => "Unknown"
                };
                if (string.IsNullOrEmpty(manga.Author))
                    manga.Author = mangaInfo.Authors.FirstOrDefault()?.Name ?? manga.Author;
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

    private void UpdateChapterViews(MangaDocument existingManga, List<ChapterDocument> chapters)
    {
        foreach (var item in existingManga.Chapters)
        {
            var chapIndex = chapters.FirstOrDefault(x => x.Number == item.Number);
            if (chapIndex != null && item.TotalView < chapIndex.TotalView)
                item.TotalView = chapIndex.TotalView;
        }
    }

    public async Task<MangaDocument> ExtractManga(string url, CancellationToken ct, bool scrapChapters = true, string? linkedId = null)
    {
        var mangaData = ExtractMangaMetadata(url);
        mangaData.Url = url;

        if (string.IsNullOrEmpty(mangaData.Title))
            throw new ArgumentException("Missing Manga Title!");

        MangaDocument? existingManga = null;
        if (!string.IsNullOrEmpty(linkedId) && Guid.TryParse(linkedId, out var parsedGuid))
        {
            existingManga = await _scraperRepo.GetDocumentByIdAsync(parsedGuid, ct);
        }
        else
        {
            var searchManga = await MeilisearchService.SearchTitleAsync(mangaData.Title, ct);
            if (searchManga is not null && StringHelper.CalculateSimilarity(searchManga.Title, mangaData.Title) >= 0.8)
                existingManga = await _scraperRepo.GetDocumentByIdAsync(Guid.Parse(searchManga.Id), ct);
        }

        var chapters = await ExtractChaptersMetadata(ct);

        if (existingManga != null)
        {
            existingManga = await UpdateThumbnail(existingManga, mangaData.ImageUrl, ct);
            existingManga.Chapters ??= new List<ChapterDocument>();
            var existingNumbers = existingManga.Chapters.Select(c => c.Number).ToHashSet();
            var newChapters = chapters.Where(c => !existingNumbers.Contains(c.Number)).ToList();

            if (newChapters.Any())
            {
                existingManga.Chapters.AddRange(newChapters);
                existingManga.UpdatedAt = DateTime.UtcNow;

                if (scrapChapters)
                    foreach (var chapter in newChapters)
                        await QueueChapterScraping(existingManga.Id, existingManga.Title, chapter);

                using var scope = ScopeFactory.CreateScope();
                var webhookService = scope.ServiceProvider.GetService<DiscordWebhookService>();
                if (webhookService != null)
                    await webhookService.SendNewChaptersNotificationAsync(existingManga, newChapters, ct);
            }

            existingManga = await UpdateMangaDocument(existingManga, ct);
            UpdateChapterViews(existingManga, chapters);
            await _scraperRepo.UpdateDocumentAsync(existingManga, ct);
            await MeilisearchService.IndexMangaAsync(existingManga, ct);
            await QdrantService.UpsertMangaAsync(existingManga, ct);
            return existingManga;
        }

        mangaData = await UpdateThumbnail(mangaData, mangaData.ImageUrl, ct);
        mangaData.Chapters = chapters;
        mangaData.CreatedAt = chapters.OrderBy(x => x.UploadDate).FirstOrDefault()?.UploadDate ?? DateTime.MinValue;
        mangaData.UpdatedAt = DateTime.UtcNow;
        if (mangaData.Type.Contains('-')) mangaData.Type = "Manga";

        var manga = await UpdateMangaDocument(mangaData, ct);
        manga.Id = Guid.NewGuid();
        await _scraperRepo.CreateDocumentAsync(manga, ct);
        await MeilisearchService.IndexMangaAsync(manga, ct);
        await QdrantService.UpsertMangaAsync(manga, ct);

        if (scrapChapters)
            foreach (var chapter in chapters)
                await QueueChapterScraping(manga.Id, manga.Title, chapter);

        using var webhookScope = ScopeFactory.CreateScope();
        var discord = webhookScope.ServiceProvider.GetService<DiscordWebhookService>();
        if (discord != null)
            await discord.SendNewMangaNotificationAsync(manga, chapters, ct);

        return manga;
    }

    public async Task<MangaDocument> GetDetail(string url, CancellationToken ct)
    {
        var mangaData = ExtractMangaMetadata(url);
        if (string.IsNullOrEmpty(mangaData.Title))
            throw new ArgumentException("Missing Manga Title!");
        var chapters = await ExtractChaptersMetadata(ct);
        mangaData.Chapters = chapters;
        if (mangaData.Type.Contains('-')) mangaData.Type = "Manga";
        return mangaData;
    }

    protected abstract MangaDocument ExtractMangaMetadata(string url);
    protected abstract Task<List<ChapterDocument>> ExtractChaptersMetadata(CancellationToken ct = default);

    public virtual async Task<ChapterDocument> GetChapterPage(string mangaTitle, ChapterDocument chapter, CancellationToken ct = default)
    {
        var url = chapter.Link;
        if (string.IsNullOrWhiteSpace(url)) return chapter;
        if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            url = Provider.BaseUrl.TrimEnd('/') + "/" + url.TrimStart('/');

        var doc = await GetHtml(url, ct: ct);
        var imageNodes = doc.DocumentNode.SelectNodes(Provider.PageSelectors.Images);
        if (imageNodes == null) return chapter;

        var downloadTasks = imageNodes.Select(async (imgNode, index) =>
        {
            var imageUrl = imgNode.GetAttributeValue("src", string.Empty);
            if (string.IsNullOrWhiteSpace(imageUrl)) return (Index: index, Page: null as PageDocument);

            await Semaphore.WaitAsync(ct);
            try
            {
                var result = await DownloadAndConvertToWebP(mangaTitle, chapter.Number.ToString(CultureInfo.InvariantCulture), imageUrl, index + 1, ct);
                return (Index: index, Page: new PageDocument { ImageUrl = imageUrl, LocalImageUrl = result.path, Size = result.size });
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Failed to download/convert image at index {Index} for {MangaTitle}", index, mangaTitle);
                throw;
            }
            finally { Semaphore.Release(); }
        });

        var results = await Task.WhenAll(downloadTasks);
        chapter.Pages.AddRange(results.OrderBy(r => r.Index).Where(r => r.Page != null).Select(r => r.Page!));
        return chapter;
    }

    public async Task QueueChapterScraping(Guid mangaId, string mangaTitle, ChapterDocument chapter)
    {
        JobClient.Enqueue<ChapterScrapingJob>(job => job.ExecuteAsync(
            mangaId, mangaTitle, chapter.Number, chapter.Id.ToString(),
            GetType().AssemblyQualifiedName!, CancellationToken.None));
        await Task.CompletedTask;
    }

    public abstract Task<List<SearchItem>> SearchManga(SearchRequest request, CancellationToken ct);

    protected async Task EnrichSearchItemAsync(SearchItem item, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(item.Title)) return;
        var searchManga = await MeilisearchService.SearchTitleAsync(item.Title, ct);
        if (searchManga != null && StringHelper.CalculateSimilarity(searchManga.Title, item.Title) >= 0.8)
        {
            var doc = await _scraperRepo.GetDocumentByIdAsync(Guid.Parse(searchManga.Id), ct);
            if (doc != null)
            {
                item.CurrentChapterNumber = doc.Chapters?.Any() == true ? doc.Chapters.Max(c => c.Number) : 0;
                item.MangaId = doc.Id.ToString();
            }
        }
    }

    public async Task<List<PageDocument>> GetAllPages(string url, CancellationToken ct = default)
    {
        var chapter = new ChapterDocument { Link = url };
        return (await GetChapterPage("temp", chapter, ct)).Pages;
    }

    public async Task<List<ScrapperProvider>> GetAllProvider()
    {
        var providers = new List<ScrapperProvider>();
        var providerFolder = Path.Combine(Directory.GetCurrentDirectory(), "provider");
        if (!Directory.Exists(providerFolder)) return providers;

        foreach (var file in Directory.GetFiles(providerFolder, "*.json"))
        {
            try
            {
                var json = await File.ReadAllTextAsync(file);
                var provider = JsonSerializer.Deserialize<ScrapperProvider>(json);
                if (provider != null) providers.Add(provider);
            }
            catch { /* skip invalid files */ }
        }
        return providers;
    }

    async Task<ScrapperMangaDocumentResponse> IProviderScrapperService.ExtractManga(string url, CancellationToken ct, bool scrapChapters, string? linkedId)
    {
        var doc = await ExtractManga(url, ct, scrapChapters, linkedId);
        return MapToResponse(doc);
    }

    async Task<ScrapperMangaDocumentResponse> IProviderScrapperService.GetDetail(string url, CancellationToken ct)
    {
        var doc = await GetDetail(url, ct);
        return MapToResponse(doc);
    }

    async Task<List<SearchItemResponse>> IProviderScrapperService.SearchManga(ScrapperSearchRequest request, CancellationToken ct)
    {
        var req = new SearchRequest
        {
            Keyword = request.Keyword,
            Genres = request.Genres.ToList(),
            Status = request.Status,
            Type = request.Type,
            Page = request.Page
        };
        var items = await SearchManga(req, ct);
        return items.Select(i => new SearchItemResponse
        {
            Title = i.Title,
            DetailUrl = i.DetailUrl,
            Thumbnail = i.Thumbnail,
            Type = i.Type,
            Genre = i.Genre,
            LastUpdateText = i.LastUpdateText,
            LatestChapterNumber = i.LatestChapterNumber,
            LatestScrapped = i.LatestScrapped,
            CurrentChapterNumber = i.CurrentChapterNumber,
            MangaId = i.MangaId
        }).ToList();
    }

    async Task<List<ProviderInfoResponse>> IProviderScrapperService.GetAllProvider()
    {
        var providers = await GetAllProvider();
        return providers.Select(p => new ProviderInfoResponse
        {
            ProviderName = p.ProviderName,
            BaseUrl = p.BaseUrl
        }).ToList();
    }

    private static ScrapperMangaDocumentResponse MapToResponse(MangaDocument doc)
    {
        return new ScrapperMangaDocumentResponse
        {
            Id = doc.Id,
            MalID = doc.MalID,
            Title = doc.Title,
            Author = doc.Author,
            Type = doc.Type,
            Rating = doc.Rating,
            Popularity = doc.Popularity,
            Members = doc.Members,
            Genres = doc.Genres,
            Description = doc.Description,
            ImageUrl = doc.ImageUrl,
            LocalImageUrl = doc.LocalImageUrl,
            ThumbnailSize = doc.ThumbnailSize,
            Status = doc.Status,
            ReleaseDate = doc.ReleaseDate,
            TotalView = doc.TotalView,
            CreatedAt = doc.CreatedAt,
            UpdatedAt = doc.UpdatedAt,
            Url = doc.Url,
            Chapters = doc.Chapters?.Select(c => new ScrapperChapterDocumentResponse
            {
                Id = c.Id,
                Number = c.Number,
                Link = c.Link,
                ChapterProvider = c.ChapterProvider,
                ChapterProviderIcon = c.ChapterProviderIcon,
                Language = c.Language,
                TotalView = c.TotalView,
                UploadDate = c.UploadDate,
                Pages = c.Pages?.Select(p => new ScrapperPageDocumentResponse
                {
                    ImageUrl = p.ImageUrl,
                    LocalImageUrl = p.LocalImageUrl,
                    Size = p.Size
                }).ToList() ?? new()
            }).ToList() ?? new()
        };
    }
}
