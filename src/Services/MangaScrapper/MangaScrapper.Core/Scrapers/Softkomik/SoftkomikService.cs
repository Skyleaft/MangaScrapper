using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using MangaScrapper.Core.Aggregates;
using MangaScrapper.Core.Configuration;
using MangaScrapper.Core.Repositories;
using MangaScrapper.Core.Services;
using MangaScrapper.Core.Utils;
using MangaScrapper.Core.ValueObjects;
using Microsoft.AspNetCore.WebUtilities;
using NovaStack.Contracts.Responses;
using NovaStack.Infrastructure.Messaging;

namespace MangaScrapper.Core.Scrapers.Softkomik;

public class SoftkomikService : ScrapperServiceBase
{
    protected override string ProviderKey => "softkomik";

    private const string BaseSiteUrl = "https://softkomik.co";
    private const string BaseApiUrl = "https://api.softkomik.org";
    private const string CoverBaseUrl = "https://cover.softdevices.my.id/softkomik-cover";
    private const string CdnImageBaseUrl = "https://image.komik.im/softkomik";
    private const string ImageTokenId = "T4Kmwztku";
    private const string DefaultUserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36";

    private static readonly Regex NextDataRegex = new(
        @"<script id=""__NEXT_DATA__""[^>]*>(.*?)</script>",
        RegexOptions.Compiled | RegexOptions.Singleline);

    private readonly HttpClient _sessionClient;
    private readonly SemaphoreSlim _sessionLock = new(1, 1);
    private string? _cachedToken;
    private string? _cachedSign;
    private DateTime _tokenExpiry = DateTime.MinValue;

    private string _currentMangaSlug = string.Empty;

    public SoftkomikService(
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
        : base(httpClient, mangaRepo, eventBus, scopeFactory, settings, semaphore, meilisearchService, qdrantService, loggerFactory, flareSolverrService)
    {
        LoadProvider("softkomik-provider.json");

        var cookieContainer = new CookieContainer();
        var handler = new SocketsHttpHandler
        {
            CookieContainer = cookieContainer,
            UseCookies = true,
            AutomaticDecompression = DecompressionMethods.All
        };
        _sessionClient = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
    }

    // ── Session Management ────────────────────────────────────────────────────────

    private async Task<(string Token, string Sign)> EnsureSessionAsync(CancellationToken ct = default)
    {
        if (!string.IsNullOrEmpty(_cachedToken) && !string.IsNullOrEmpty(_cachedSign) && _tokenExpiry > DateTime.UtcNow.AddMinutes(5))
        {
            return (_cachedToken, _cachedSign);
        }

        await _sessionLock.WaitAsync(ct);
        try
        {
            if (!string.IsNullOrEmpty(_cachedToken) && !string.IsNullOrEmpty(_cachedSign) && _tokenExpiry > DateTime.UtcNow.AddMinutes(5))
            {
                return (_cachedToken, _cachedSign);
            }

            // 1. Visit homepage to establish Next.js session cookies (AhyyL, zEm983)
            using var initReq = new HttpRequestMessage(HttpMethod.Get, BaseSiteUrl);
            initReq.Headers.TryAddWithoutValidation("User-Agent", DefaultUserAgent);
            initReq.Headers.TryAddWithoutValidation("Referer", $"{BaseSiteUrl}/");
            using var initRes = await _sessionClient.SendAsync(initReq, ct);

            // 2. Fetch session token from internal Next.js API
            using var sessReq = new HttpRequestMessage(HttpMethod.Get, $"{BaseSiteUrl}/api/session/aksjkas");
            sessReq.Headers.TryAddWithoutValidation("User-Agent", DefaultUserAgent);
            sessReq.Headers.TryAddWithoutValidation("Referer", $"{BaseSiteUrl}/");
            sessReq.Headers.TryAddWithoutValidation("Origin", BaseSiteUrl);
            sessReq.Headers.TryAddWithoutValidation("Accept", "application/json");

            using var sessRes = await _sessionClient.SendAsync(sessReq, ct);
            sessRes.EnsureSuccessStatusCode();

            var sessJson = await sessRes.Content.ReadAsStringAsync(ct);
            var sessObj = JsonSerializer.Deserialize<SoftkomikSessionResponse>(sessJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? throw new InvalidOperationException("Failed to deserialize Softkomik session.");

            _cachedToken = sessObj.Token;
            var rawSign = sessObj.Sign;
            _cachedSign = rawSign.Contains("|oiq&") ? rawSign.Split("|oiq&")[0] : (rawSign.Length > 64 ? rawSign[..64] : rawSign);
            _tokenExpiry = DateTime.UtcNow.AddHours(1);

            return (_cachedToken, _cachedSign);
        }
        finally
        {
            _sessionLock.Release();
        }
    }

    private async Task<T?> SendApiGetAsync<T>(string url, CancellationToken ct = default)
    {
        var (token, sign) = await EnsureSessionAsync(ct);

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("User-Agent", DefaultUserAgent);
        request.Headers.TryAddWithoutValidation("Referer", $"{BaseSiteUrl}/");
        request.Headers.TryAddWithoutValidation("Origin", BaseSiteUrl);
        request.Headers.TryAddWithoutValidation("X-Token", token);
        request.Headers.TryAddWithoutValidation("X-Sign", sign);
        request.Headers.TryAddWithoutValidation("Accept", "application/json, text/plain, */*");

        using var response = await _sessionClient.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            Logger.LogWarning("Softkomik API GET {Url} returned status {StatusCode}", url, response.StatusCode);
            return default;
        }

        var json = await response.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize<T>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    }

    // ── Detail & Metadata ─────────────────────────────────────────────────────────

    protected override Manga ExtractMangaMetadata(string url)
    {
        _currentMangaSlug = ExtractSlugFromUrl(url);

        var mangaPageUrl = $"{BaseSiteUrl}/{_currentMangaSlug}";
        var html = FetchPageHtml(mangaPageUrl);

        var nextDataMatch = NextDataRegex.Match(html);
        if (!nextDataMatch.Success)
            throw new InvalidOperationException($"Could not find __NEXT_DATA__ in Softkomik page '{mangaPageUrl}'.");

        var json = nextDataMatch.Groups[1].Value;
        var wrapper = JsonSerializer.Deserialize<SoftkomikNextDataWrapper<SoftkomikDetailPageProps>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        var detail = wrapper?.Props?.PageProps?.Data
            ?? throw new InvalidOperationException($"Softkomik manga details not found for slug '{_currentMangaSlug}'.");

        var imageUrl = !string.IsNullOrWhiteSpace(detail.Gambar)
            ? $"{CoverBaseUrl}/{detail.Gambar.TrimStart('/')}"
            : null;

        var status = detail.Status?.ToLowerInvariant() switch
        {
            "ongoing" => "Ongoing",
            "tamat" or "completed" => "Completed",
            _ => "Unknown"
        };

        var type = !string.IsNullOrWhiteSpace(detail.Type)
            ? char.ToUpperInvariant(detail.Type[0]) + detail.Type[1..].ToLowerInvariant()
            : "Manga";

        return Manga.Create(
            title: detail.Title,
            author: detail.Author ?? "Unknown",
            type: type,
            source: ProviderKey,
            genres: detail.Genre ?? [],
            description: detail.Sinopsis ?? string.Empty,
            imageUrl: imageUrl,
            url: mangaPageUrl,
            rating: detail.Rating?.Value,
            status: status);
    }

    protected override async Task<List<Chapter>> ExtractChaptersMetadata(CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_currentMangaSlug))
            return [];

        var chapterApiUrl = $"{BaseApiUrl}/komik/{_currentMangaSlug}/chapter?limit=9999999";
        var response = await SendApiGetAsync<SoftkomikChapterListResponse>(chapterApiUrl, ct);

        if (response?.Chapter == null || response.Chapter.Count == 0)
            return [];

        var chapters = new List<Chapter>();
        foreach (var item in response.Chapter)
        {
            var chapterNumber = double.TryParse(item.Chapter, NumberStyles.Float, CultureInfo.InvariantCulture, out var num)
                ? num
                : -1;

            if (chapterNumber < 0) continue;

            chapters.Add(new Chapter(
                id: ChapterId.New(),
                number: chapterNumber,
                link: $"{BaseSiteUrl}/{_currentMangaSlug}/chapter/{item.Chapter}",
                chapterProvider: Provider.ProviderName,
                chapterProviderIcon: Provider.ProviderIcon,
                language: DefaultIndonesianLanguage,
                totalView: 0,
                uploadDate: DateTime.UtcNow));
        }

        return chapters
            .OrderByDescending(c => c.Number)
            .ToList();
    }

    // ── Chapter Pages ─────────────────────────────────────────────────────────────

    public override async Task<Chapter> GetChapterPage(string mangaTitle, Chapter chapter, CancellationToken ct = default, Func<int, int, Task>? onProgress = null)
    {
        var url = chapter.Link;
        if (string.IsNullOrWhiteSpace(url))
            return chapter;

        if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            url = $"{BaseSiteUrl.TrimEnd('/')}/{url.TrimStart('/')}";

        var chapterHtml = await FetchPageHtmlAsync(url, ct);
        var match = NextDataRegex.Match(chapterHtml);
        if (!match.Success)
        {
            Logger.LogWarning("Softkomik: Could not find __NEXT_DATA__ in chapter page {Url}", url);
            return chapter;
        }

        var json = match.Groups[1].Value;
        var wrapper = JsonSerializer.Deserialize<SoftkomikNextDataWrapper<SoftkomikChapterPageProps>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        var chapterData = wrapper?.Props?.PageProps?.Data;

        List<string> imagePaths = [];
        if (chapterData?.ImageSrc != null && chapterData.ImageSrc.Count > 0)
        {
            imagePaths = chapterData.ImageSrc;
        }
        else if (chapterData?.Data != null && !string.IsNullOrWhiteSpace(chapterData.Data.Id))
        {
            var slug = chapterData.Komik?.TitleSlug ?? ExtractSlugFromUrl(url);
            var chapterNum = chapterData.Chapter ?? chapter.Number.ToString(CultureInfo.InvariantCulture);
            var imgsApiUrl = $"{BaseApiUrl}/komik/{slug}/chapter/{chapterNum}/imgs/{chapterData.Data.Id}";

            var imgsResponse = await SendApiGetAsync<SoftkomikChapterImgsResponse>(imgsApiUrl, ct);
            if (imgsResponse?.ImageSrc != null)
            {
                imagePaths = imgsResponse.ImageSrc;
            }
        }

        if (imagePaths.Count == 0)
        {
            Logger.LogWarning("Softkomik: No images found for chapter {ChapterNumber} at {Url}", chapter.Number, url);
            return chapter;
        }

        var imageUrls = imagePaths
            .Select(p => $"{CdnImageBaseUrl}/{p.TrimStart('/')}?id={ImageTokenId}")
            .ToList();

        var total = imageUrls.Count;
        var completed = 0;
        if (onProgress != null && total > 0)
        {
            await onProgress(0, total);
        }

        var downloadTasks = imageUrls.Select(async (imageUrl, index) =>
        {
            if (string.IsNullOrWhiteSpace(imageUrl))
                return (Index: index, Page: null as Page);

            await Semaphore.WaitAsync(ct);
            try
            {
                var result = await DownloadAndConvertToWebP(
                    mangaTitle,
                    chapter.Number.ToString(CultureInfo.InvariantCulture),
                    imageUrl,
                    index + 1,
                    ct);

                var current = Interlocked.Increment(ref completed);
                if (onProgress != null)
                {
                    await onProgress(current, total);
                }

                return (Index: index, Page: new Page(Guid.CreateVersion7(), imageUrl, result.path, result.size, result.width, result.height, result.isFallback));
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Failed to download/convert image at index {Index} for {MangaTitle} (Softkomik)", index, mangaTitle);
                throw;
            }
            finally
            {
                Semaphore.Release();
            }
        });

        var results = await Task.WhenAll(downloadTasks);
        var orderedPages = results
            .Where(r => r.Page != null)
            .OrderBy(r => r.Index)
            .Select(r => r.Page!)
            .ToList();

        chapter.Pages.Clear();
        chapter.AddPages(orderedPages);
        return chapter;
    }

    // ── Search ────────────────────────────────────────────────────────────────────

    public override async Task<List<SearchItem>> SearchManga(SearchRequest request, CancellationToken ct)
    {
        var page = request.Page <= 0 ? 1 : request.Page;
        var queryParams = new List<KeyValuePair<string, string?>>
        {
            new("page", page.ToString(CultureInfo.InvariantCulture)),
            new("limit", "20")
        };

        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            queryParams.Add(new("name", request.Keyword.Trim()));
            queryParams.Add(new("search", "true"));
        }
        else
        {
            queryParams.Add(new("sortBy", "newKomik"));
        }

        var searchUrl = QueryHelpers.AddQueryString($"{BaseApiUrl}/komik", queryParams);
        var searchResponse = await SendApiGetAsync<SoftkomikSearchResponse>(searchUrl, ct);

        if (searchResponse?.Data == null || searchResponse.Data.Count == 0)
            return [];

        var results = new List<SearchItem>();
        foreach (var item in searchResponse.Data)
        {
            var thumbnail = !string.IsNullOrWhiteSpace(item.Gambar)
                ? $"{CoverBaseUrl}/{item.Gambar.TrimStart('/')}"
                : string.Empty;

            var latestChapter = double.TryParse(item.LatestChapter, NumberStyles.Float, CultureInfo.InvariantCulture, out var num)
                ? num
                : 0;

            results.Add(new SearchItem
            {
                Title = item.Title,
                DetailUrl = $"{BaseSiteUrl}/{item.TitleSlug}",
                Thumbnail = thumbnail,
                Type = !string.IsNullOrWhiteSpace(item.Type) ? char.ToUpperInvariant(item.Type[0]) + item.Type[1..] : "Manga",
                LatestChapterNumber = latestChapter
            });
        }

        return results;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────

    private string FetchPageHtml(string url)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.TryAddWithoutValidation("User-Agent", DefaultUserAgent);
        req.Headers.TryAddWithoutValidation("Referer", $"{BaseSiteUrl}/");
        using var res = _sessionClient.Send(req);
        res.EnsureSuccessStatusCode();
        using var reader = new StreamReader(res.Content.ReadAsStream());
        return reader.ReadToEnd();
    }

    private async Task<string> FetchPageHtmlAsync(string url, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.TryAddWithoutValidation("User-Agent", DefaultUserAgent);
        req.Headers.TryAddWithoutValidation("Referer", $"{BaseSiteUrl}/");
        using var res = await _sessionClient.SendAsync(req, ct);
        res.EnsureSuccessStatusCode();
        return await res.Content.ReadAsStringAsync(ct);
    }

    private static string ExtractSlugFromUrl(string url)
    {
        var clean = url.Trim();
        if (Uri.TryCreate(clean, UriKind.Absolute, out var uri))
        {
            var segments = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length > 0)
                return segments[0];
        }
        return clean.Trim('/');
    }
}
