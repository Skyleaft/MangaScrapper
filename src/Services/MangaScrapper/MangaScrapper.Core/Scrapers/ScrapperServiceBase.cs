using System.Globalization;
using System.Text.Json;
using System.Web;
using HtmlAgilityPack;
using MangaScrapper.Core.Aggregates;
using MangaScrapper.Core.Common.Abstractions;
using MangaScrapper.Core.Configuration;
using MangaScrapper.Core.Repositories;
using MangaScrapper.Core.Services;
using MangaScrapper.Core.Utils;
using MangaScrapper.Core.ValueObjects;
using Mapster;
using NovaStack.Contracts.IntegrationEvents;
using NovaStack.Contracts.Responses;
using NovaStack.Infrastructure.Messaging;
using SkiaSharp;

namespace MangaScrapper.Core.Scrapers;

public interface IScrapperService
{
    Task<HtmlDocument> GetHtml(string url, string? query = null, HttpContent? formData = null, CancellationToken ct = default);
    Task<(string path, long size)> DownloadAndConvertToWebP(string mangaTitle, string chapterNumber, string imageUrl, int index, CancellationToken ct = default);
    Task<(string path, long size)> DownloadThumbnailAndConvertToWebP(string mangaTitle, string imageUrl, CancellationToken ct = default);
    string GetCleanTitle(string title);
    Task<Manga> UpdateMangaMetaData(Manga manga, CancellationToken ct = default);
    Task<Manga> ExtractManga(string url, CancellationToken ct, bool scrapChapters = true, string? linkedId = null);
    Task<Manga> GetDetail(string url, CancellationToken ct);
    Task<Chapter> GetChapterPage(string mangaTitle, Chapter chapter, CancellationToken ct = default);
    Task QueueChapterScraping(Guid mangaId, string mangaTitle, Chapter chapter, CancellationToken ct = default);
    Task<List<SearchItem>> SearchManga(SearchRequest request, CancellationToken ct);
    Task<List<Page>> GetAllPages(string url, CancellationToken ct = default);
    Task<List<ScrapperProvider>> GetAllProvider();
}

public abstract class ScrapperServiceBase : IScrapperService, IProviderScrapperService
{
    protected const string DefaultIndonesianLanguage = "id";

    protected readonly HttpClient HttpClient;
    protected readonly IEventBus EventBus;
    protected readonly IServiceScopeFactory ScopeFactory;
    protected readonly SemaphoreSlim Semaphore;
    protected readonly string ImageStoragePath;
    protected readonly MeilisearchService MeilisearchService;
    protected readonly QdrantService QdrantService;
    protected readonly ILogger Logger;
    protected readonly FlareSolverrService FlareSolverrService;

    /// <summary>Provider key used to resolve this scraper from the DI container (e.g. "komiku", "kiryuu").</summary>
    protected abstract string ProviderKey { get; }
    private ScrapperProvider? _provider;

    // Repository for domain aggregate access used by scrapers
    private readonly IMangaRepository _mangaRepo;

    private readonly ScrapperSettings _scrapperSettings;

    protected ScrapperServiceBase(
        HttpClient httpClient,
        IMangaRepository mangaRepo,
        IEventBus eventBus,
        IServiceScopeFactory scopeFactory,
        IOptions<ScrapperSettings> settings,
        SemaphoreSlim semaphore,
        MeilisearchService meilisearchService,
        QdrantService qdrantService,
        ILoggerFactory loggerFactory,
        FlareSolverrService flareSolverrService)
    {
        HttpClient = httpClient;
        _mangaRepo = mangaRepo;
        EventBus = eventBus;
        ScopeFactory = scopeFactory;
        Semaphore = semaphore;
        MeilisearchService = meilisearchService;
        QdrantService = qdrantService;
        Logger = loggerFactory.CreateLogger(GetType());
        FlareSolverrService = flareSolverrService;
        _scrapperSettings = settings.Value;
        var path = _scrapperSettings.ImageStoragePath;
        ImageStoragePath = Path.IsPathRooted(path) ? path : Path.Combine(Directory.GetCurrentDirectory(), path);
        Directory.CreateDirectory(ImageStoragePath);
    }

    protected ScrapperProvider Provider => _provider ?? throw new InvalidOperationException("Provider has not been loaded.");

    protected void LoadProvider(string providerName)
    {
        if (_provider != null) return;

        if (!string.IsNullOrWhiteSpace(_scrapperSettings.ApiBaseUrl))
        {
            var url = $"{_scrapperSettings.ApiBaseUrl.TrimEnd('/')}/api/v1/providers/{providerName}";
            try
            {
                var response = HttpClient.GetAsync(url).GetAwaiter().GetResult();
                if (response.IsSuccessStatusCode)
                {
                    var json = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                    _provider = JsonSerializer.Deserialize<ScrapperProvider>(json);
                    return;
                }
                else
                {
                    Logger.LogWarning("Failed to fetch provider {ProviderName} from API ({StatusCode}). Falling back to local files.", providerName, response.StatusCode);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Exception fetching provider {ProviderName} from API. Falling back to local files.", providerName);
            }
        }

        var pathsToTry = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "provider", providerName),
            Path.Combine(Directory.GetCurrentDirectory(), "provider", providerName)
        };

        foreach (var path in pathsToTry)
        {
            if (File.Exists(path))
            {
                var json = File.ReadAllTextAsync(path).GetAwaiter().GetResult();
                _provider = JsonSerializer.Deserialize<ScrapperProvider>(json);
                return;
            }
        }

        throw new FileNotFoundException($"Provider file {providerName} could not be found via API or local disk.");
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

                var delay = 1000 * (i + 1);
                if (ex is HttpRequestException { StatusCode: System.Net.HttpStatusCode.TooManyRequests })
                {
                    delay = 5000 * (int)Math.Pow(2, i);
                    Logger.LogWarning("Rate limited (429). Retrying in {Delay}ms...", delay);
                }

                await Task.Delay(delay, ct);
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


    public async Task<Manga> UpdateMangaMetaData(Manga manga, CancellationToken ct = default)
    {
        var externalService = ScopeFactory.CreateScope().ServiceProvider.GetRequiredService<IExternalMetadataService>();
        JikanMangaItem? mangaInfo = manga.MalId != 0
            ? await externalService.GetJikanMangaInfoByIdAsync(manga.MalId, ct)
            : await externalService.GetJikanMangaInfoAsync(manga.Title, manga.Type, ct);

        if (mangaInfo?.TitleSynonyms != null)
        {
            var combinedSynonyms = string.Join(" ", mangaInfo.TitleSynonyms);
            if (StringHelper.IsSimilar(mangaInfo.Title, manga.Title) ||
                StringHelper.IsSimilar(mangaInfo.TitleEnglish ?? "", manga.Title) ||
                StringHelper.IsSimilar(combinedSynonyms, manga.Title) ||
                StringHelper.IsSimilar(mangaInfo.TitleJapanese ?? "", manga.Title) ||
                mangaInfo.MalId == manga.MalId)
            {
                var status = mangaInfo.Status switch
                {
                    "Complete" or "Finished" => "Completed",
                    "Publishing" => "Ongoing",
                    "Hiatus" => "On Hiatus",
                    "Discontinued" => "Discontinued",
                    "Upcoming" => "Upcoming",
                    _ => "Unknown"
                };

                manga.UpdateFromScrapper(
                    mangaInfo.MalId,
                    mangaInfo.Score,
                    mangaInfo.Popularity,
                    mangaInfo.Members,
                    mangaInfo.Published?.From,
                    status,
                    mangaInfo.Authors.FirstOrDefault()?.Name);
            }
        }
        return manga;
    }

    private async Task<Manga> UpdateThumbnail(Manga manga, string? imageUrl, CancellationToken ct = default)
    {
        if (!string.IsNullOrWhiteSpace(imageUrl))
        {
            manga.UpdateImageUrl(ThumbnailHelper.RemoveResizeParams(imageUrl));
            var thumb = await DownloadThumbnailAndConvertToWebP(manga.Title, imageUrl, ct);
            manga.UpdateLocalImage(thumb.path, thumb.size);
        }
        return manga;
    }

    private void UpdateChapterViews(Manga existingManga, List<Chapter> chapters)
    {
        foreach (var item in existingManga.Chapters)
        {
            var chapIndex = chapters.FirstOrDefault(x => x.Number == item.Number);
            if (chapIndex != null && item.TotalView < chapIndex.TotalView)
                item.UpdateTotalView(chapIndex.TotalView);
        }
    }

    public async Task<Manga> ExtractManga(string url, CancellationToken ct, bool scrapChapters = true, string? linkedId = null)
    {
        var mangaData = ExtractMangaMetadata(url);
        mangaData.SetUrl(url);

        if (string.IsNullOrEmpty(mangaData.Title))
            throw new ArgumentException("Missing Manga Title!");

        Manga? existingManga = null;
        if (!string.IsNullOrEmpty(linkedId) && Guid.TryParse(linkedId, out var parsedGuid))
        {
            existingManga = await _mangaRepo.GetByIdAsync(MangaId.From(parsedGuid), ct);
        }
        else
        {
            var searchManga = await MeilisearchService.SearchTitleAsync(mangaData.Title, ct);
            if (searchManga is not null && StringHelper.CalculateSimilarity(searchManga.Title, mangaData.Title) >= 0.8)
                existingManga = await _mangaRepo.GetByIdAsync(MangaId.From(Guid.Parse(searchManga.Id)), ct);
        }

        var chapters = await ExtractChaptersMetadata(ct);

        if (existingManga != null)
        {
            existingManga = await UpdateThumbnail(existingManga, mangaData.ImageUrl, ct);
            var existingNumbers = existingManga.Chapters.Select(c => c.Number).ToHashSet();
            var newChapters = chapters.Where(c => !existingNumbers.Contains(c.Number)).ToList();

            if (newChapters.Any())
            {
                existingManga.AddChapters(newChapters);

                if (scrapChapters)
                    foreach (var chapter in newChapters)
                        await QueueChapterScraping(existingManga.Id.Value, existingManga.Title, chapter, ct);

                using var scope = ScopeFactory.CreateScope();
                var webhookService = scope.ServiceProvider.GetService<DiscordWebhookService>();
                if (webhookService != null)
                    await webhookService.SendNewChaptersNotificationAsync(existingManga, newChapters, ct);
            }

            existingManga = await UpdateMangaMetaData(existingManga, ct);
            UpdateChapterViews(existingManga, chapters);
            await _mangaRepo.UpdateAsync(existingManga, ct);
            await MeilisearchService.IndexMangaAsync(existingManga, ct);
            await QdrantService.UpsertMangaAsync(existingManga, ct);
            return existingManga;
        }

        mangaData = await UpdateThumbnail(mangaData, mangaData.ImageUrl, ct);
        mangaData.AddChapters(chapters);
        var createdAt = chapters.OrderBy(x => x.UploadDate).FirstOrDefault()?.UploadDate ?? DateTime.MinValue;
        mangaData.SetDates(createdAt, DateTime.UtcNow);
        if (mangaData.Type.Contains('-')) mangaData.SetType("Manga");

        var manga = await UpdateMangaMetaData(mangaData, ct);
        await _mangaRepo.AddAsync(manga, ct);
        await MeilisearchService.IndexMangaAsync(manga, ct);
        await QdrantService.UpsertMangaAsync(manga, ct);

        if (scrapChapters)
            foreach (var chapter in chapters)
                await QueueChapterScraping(manga.Id.Value, manga.Title, chapter, ct);

        using var webhookScope = ScopeFactory.CreateScope();
        var discord = webhookScope.ServiceProvider.GetService<DiscordWebhookService>();
        if (discord != null)
            await discord.SendNewMangaNotificationAsync(manga, chapters, ct);

        return manga;
    }

    public async Task<Manga> GetDetail(string url, CancellationToken ct)
    {
        var mangaData = ExtractMangaMetadata(url);
        if (string.IsNullOrEmpty(mangaData.Title))
            throw new ArgumentException("Missing Manga Title!");
        var chapters = await ExtractChaptersMetadata(ct);
        mangaData.AddChapters(chapters);
        if (mangaData.Type.Contains('-')) mangaData.SetType("Manga");
        return mangaData;
    }

    protected abstract Manga ExtractMangaMetadata(string url);
    protected abstract Task<List<Chapter>> ExtractChaptersMetadata(CancellationToken ct = default);

    public virtual async Task<Chapter> GetChapterPage(string mangaTitle, Chapter chapter, CancellationToken ct = default)
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
            if (string.IsNullOrWhiteSpace(imageUrl)) return (Index: index, Page: null as Page);

            await Semaphore.WaitAsync(ct);
            try
            {
                var result = await DownloadAndConvertToWebP(mangaTitle, chapter.Number.ToString(CultureInfo.InvariantCulture), imageUrl, index + 1, ct);
                return (Index: index, Page: new Page(Guid.CreateVersion7(), imageUrl, result.path, result.size));
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Failed to download/convert image at index {Index} for {MangaTitle}", index, mangaTitle);
                throw;
            }
            finally { Semaphore.Release(); }
        });

        var results = await Task.WhenAll(downloadTasks);
        chapter.AddPages(results.OrderBy(r => r.Index).Where(r => r.Page != null).Select(r => r.Page!).ToList());
        return chapter;
    }

    public async Task QueueChapterScraping(Guid mangaId, string mangaTitle, Chapter chapter, CancellationToken ct = default)
    {
        var integrationEvent = new ScrapChapterPagesIntegrationEvent(
            mangaId, mangaTitle, chapter.Number, chapter.Id.Value.ToString(), ProviderKey);
        await EventBus.PublishAsync(integrationEvent, "scrape-chapter-pages", ct);
    }

    public abstract Task<List<SearchItem>> SearchManga(SearchRequest request, CancellationToken ct);

    protected async Task EnrichSearchItemAsync(SearchItem item, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(item.Title)) return;
        var searchManga = await MeilisearchService.SearchTitleAsync(item.Title, ct);
        if (searchManga != null && StringHelper.CalculateSimilarity(searchManga.Title, item.Title) >= 0.8)
        {
            var doc = await _mangaRepo.GetByIdAsync(MangaId.From(Guid.Parse(searchManga.Id)), ct);
            if (doc != null)
            {
                item.CurrentChapterNumber = doc.Chapters?.Any() == true ? doc.Chapters.Max(c => c.Number) : 0;
                item.MangaId = doc.Id.Value.ToString();
            }
        }
    }

    public async Task<List<Page>> GetAllPages(string url, CancellationToken ct = default)
    {
        var chapter = new Chapter(ChapterId.New(), 0, url, null, null, DefaultIndonesianLanguage, 0, DateTime.UtcNow);
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
        var manga = await ExtractManga(url, ct, scrapChapters, linkedId);
        return MapToResponse(manga);
    }

    async Task<ScrapperMangaDocumentResponse> IProviderScrapperService.GetDetail(string url, CancellationToken ct)
    {
        var manga = await GetDetail(url, ct);
        return MapToResponse(manga);
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

    private static ScrapperMangaDocumentResponse MapToResponse(Manga manga)
    {
        return new ScrapperMangaDocumentResponse
        {
            Id = manga.Id.Value,
            MalID = manga.MalId,
            Title = manga.Title,
            Author = manga.Author,
            Type = manga.Type,
            Rating = manga.Rating,
            Popularity = manga.Popularity,
            Members = manga.Members,
            Genres = manga.Genres,
            Description = manga.Description,
            ImageUrl = manga.ImageUrl,
            LocalImageUrl = manga.LocalImageUrl,
            ThumbnailSize = manga.ThumbnailSize,
            Status = manga.Status,
            ReleaseDate = manga.ReleaseDate,
            TotalView = manga.TotalView,
            CreatedAt = manga.CreatedAt,
            UpdatedAt = manga.UpdatedAt,
            Url = manga.Url,
            Chapters = manga.Chapters?.Select(c => new ScrapperChapterDocumentResponse
            {
                Id = c.Id.Value,
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

