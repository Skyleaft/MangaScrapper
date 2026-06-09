using System.Globalization;
using Hangfire;
using MangaScrapper.Infrastructure.Models;
using MangaScrapper.Infrastructure.Mongo.Collections;
using MangaScrapper.Infrastructure.Repositories;
using MangaScrapper.Infrastructure.Services;
using MangaScrapper.Infrastructure.Utils;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MangaScrapper.Features.ScrapperMangadex.Services;

public class MangaDexService : ScrapperServiceBase
{
    private const string BaseApi = "https://api.mangadex.org";
    private const string CoverBaseUrl = "https://uploads.mangadex.org/covers";

    public MangaDexService(
        HttpClient httpClient,
        IMangaRepository mangaRepository,
        IBackgroundJobClient jobClient,
        IServiceScopeFactory scopeFactory,
        IOptions<ScrapperSettings> settings,
        SemaphoreSlim semaphore,
        MeilisearchService meilisearchService,
        QdrantService qdrantService,
        ILoggerFactory loggerFactory)
        : base(httpClient, mangaRepository, jobClient, scopeFactory, settings, semaphore, meilisearchService, qdrantService, loggerFactory)
    {
        LoadProvider("mangadex-provider.json");
    }

    // ─── Abstract overrides (not used for API-based scraper) ─────────────────

    protected override MangaDocument ExtractMangaMetadata(string url)
        => throw new NotSupportedException("MangaDex uses a REST API — HTML scraping is not applicable.");

    protected override Task<List<ChapterDocument>> ExtractChaptersMetadata(CancellationToken ct = default)
        => throw new NotSupportedException("MangaDex uses a REST API — HTML scraping is not applicable.");

    // ─── Search ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Searches for manga using MangaDex.
    /// <para>
    /// When no keyword is supplied the chapter feed endpoint is used
    /// (<c>GET /chapter</c>) so the results are sorted by latest update.
    /// When a keyword is present the manga search endpoint is used
    /// (<c>GET /manga?title=…</c>).
    /// </para>
    /// </summary>
    public override async Task<List<SearchItem>> SearchManga(SearchRequest request, CancellationToken ct)
    {
        return string.IsNullOrWhiteSpace(request.Keyword)
            ? await SearchByLatestUpdate(request, ct)
            : await SearchByKeyword(request, ct);
    }

    // ─── Latest-update feed ───────────────────────────────────────────────────

    private async Task<List<SearchItem>> SearchByLatestUpdate(SearchRequest request, CancellationToken ct)
    {
        const int pageSize = 32;
        var offset = (request.Page - 1) * pageSize;

        // Build the chapter feed URL exactly as requested
        var queryParams = new List<KeyValuePair<string, string?>>
        {
            new("limit", pageSize.ToString()),
            new("offset", offset.ToString()),
            new("translatedLanguage[]", "en"),
            new("translatedLanguage[]", "id"),
            new("translatedLanguage[]", "ja"),
            new("includes[]", "user"),
            new("includes[]", "scanlation_group"),
            new("includes[]", "manga"),
            new("contentRating[]", "safe"),
            new("contentRating[]", "suggestive"),
            new("contentRating[]", "erotica"),
            new("contentRating[]", "pornographic"),
            new("order[readableAt]", "desc"),
        };

        var feedUrl = QueryHelpers.AddQueryString($"{BaseApi}/chapter", queryParams);

        MangaDexResponse<List<MangaDexChapter>>? feedResponse;
        try
        {
            feedResponse = await HttpClient.GetFromJsonAsync<MangaDexResponse<List<MangaDexChapter>>>(feedUrl, ct);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to fetch MangaDex chapter feed");
            return new List<SearchItem>();
        }

        if (feedResponse?.Data == null || feedResponse.Data.Count == 0)
            return new List<SearchItem>();

        // Deduplicate: keep only the most recent chapter per manga
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var chaptersToMap = new List<(MangaDexChapter Chapter, MangaDexRelationship MangaRel)>();

        foreach (var chapter in feedResponse.Data)
        {
            var mangaRel = chapter.Relationships
                .FirstOrDefault(r => r.Type == "manga");

            if (mangaRel == null) continue;
            if (!seen.Add(mangaRel.Id)) continue;   // already handled this manga

            chaptersToMap.Add((chapter, mangaRel));
        }

        // Fetch cover art via manga details API in batch
        var coverMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (chaptersToMap.Count > 0)
        {
            var mangaQueryParams = new List<KeyValuePair<string, string?>>
            {
                new("limit", "100"),
                new("includes[]", "cover_art"),
                new("contentRating[]", "safe"),
                new("contentRating[]", "suggestive"),
                new("contentRating[]", "erotica"),
                new("contentRating[]", "pornographic"),
            };

            foreach (var pair in chaptersToMap)
            {
                mangaQueryParams.Add(new("ids[]", pair.MangaRel.Id));
            }

            var mangaUrl = QueryHelpers.AddQueryString($"{BaseApi}/manga", mangaQueryParams);
            try
            {
                var mangaResponse = await HttpClient.GetFromJsonAsync<MangaDexResponse<List<MangaDexManga>>>(mangaUrl, ct);
                if (mangaResponse?.Data != null)
                {
                    foreach (var manga in mangaResponse.Data)
                    {
                        var coverRel = manga.Relationships.FirstOrDefault(r => r.Type == "cover_art");
                        if (coverRel?.Attributes?.FileName != null)
                        {
                            coverMap[manga.Id] = $"{CoverBaseUrl}/{manga.Id}/{coverRel.Attributes.FileName}.512.jpg";
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Failed to fetch cover art for manga feed");
            }
        }

        var results = new List<SearchItem>();
        foreach (var (chapter, mangaRel) in chaptersToMap)
        {
            coverMap.TryGetValue(mangaRel.Id, out var thumbnail);
            var item = MapChapterToSearchItem(chapter, mangaRel, thumbnail ?? string.Empty);
            await EnrichSearchItemAsync(item, ct);
            results.Add(item);
        }

        return results;
    }

    // ─── Keyword search ───────────────────────────────────────────────────────

    private async Task<List<SearchItem>> SearchByKeyword(SearchRequest request, CancellationToken ct)
    {
        const int pageSize = 20;
        var offset = (request.Page - 1) * pageSize;

        var queryParams = new List<KeyValuePair<string, string?>>
        {
            new("title", request.Keyword),
            new("limit", pageSize.ToString()),
            new("offset", offset.ToString()),
            new("includes[]", "cover_art"),
            new("includes[]", "author"),
            new("contentRating[]", "safe"),
            new("contentRating[]", "suggestive"),
            new("contentRating[]", "erotica"),
            new("contentRating[]", "pornographic"),
            new("order[relevance]", "desc"),
        };

        if (!string.IsNullOrWhiteSpace(request.Status))
            queryParams.Add(new("status[]", request.Status.ToLowerInvariant()));

        if (request.Genres != null)
        {
            foreach (var genre in request.Genres)
                queryParams.Add(new("includedTags[]", genre));
        }

        var searchUrl = QueryHelpers.AddQueryString($"{BaseApi}/manga", queryParams);

        MangaDexResponse<List<MangaDexManga>>? searchResponse;
        try
        {
            searchResponse = await HttpClient.GetFromJsonAsync<MangaDexResponse<List<MangaDexManga>>>(searchUrl, ct);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to fetch MangaDex manga search results for keyword '{Keyword}'", request.Keyword);
            return new List<SearchItem>();
        }

        if (searchResponse?.Data == null || searchResponse.Data.Count == 0)
            return new List<SearchItem>();

        var results = new List<SearchItem>();

        foreach (var manga in searchResponse.Data)
        {
            var item = MapMangaToSearchItem(manga);
            await EnrichSearchItemAsync(item, ct);
            results.Add(item);
        }

        return results;
    }

    // ─── Mappers ─────────────────────────────────────────────────────────────

    /// <summary>Maps a chapter feed entry (with embedded manga relationship) to <see cref="SearchItem"/>.</summary>
    private static SearchItem MapChapterToSearchItem(MangaDexChapter chapter, MangaDexRelationship mangaRel, string thumbnail)
    {
        var attrs = mangaRel.Attributes;

        var title = ResolveTitle(attrs?.Title);
        var chapterNumber = double.TryParse(
            chapter.Attributes.Chapter,
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out var num) ? num : 0;

        var genres = attrs?.Tags?
            .Select(t => t.Attributes.Name.TryGetValue("en", out var n) ? n : string.Empty)
            .Where(n => !string.IsNullOrEmpty(n))
            .ToList() ?? new List<string>();

        var contentType = MapOriginalLanguageToType(attrs?.OriginalLanguage);

        return new SearchItem
        {
            Title = title,
            DetailUrl = $"https://mangadex.org/title/{mangaRel.Id}",
            Thumbnail = thumbnail,
            Type = contentType,
            Genre = string.Join(", ", genres),
            LatestChapterNumber = chapterNumber,
            LastUpdateText = chapter.Attributes.ReadableAt.ToTimeAgo(),
        };
    }

    /// <summary>Maps a manga search result (with cover_art relationship) to <see cref="SearchItem"/>.</summary>
    private static SearchItem MapMangaToSearchItem(MangaDexManga manga)
    {
        var attrs = manga.Attributes;
        var title = ResolveTitle(attrs.Title);

        // Cover art thumbnail
        var coverRel = manga.Relationships.FirstOrDefault(r => r.Type == "cover_art");
        var thumbnail = string.Empty;
        if (coverRel?.Attributes?.FileName != null)
            thumbnail = $"{CoverBaseUrl}/{manga.Id}/{coverRel.Attributes.FileName}.256.jpg";

        var genres = attrs.Tags
            .Select(t => t.Attributes.Name.TryGetValue("en", out var n) ? n : string.Empty)
            .Where(n => !string.IsNullOrEmpty(n))
            .ToList();

        var contentType = MapOriginalLanguageToType(attrs.OriginalLanguage);

        return new SearchItem
        {
            Title = title,
            DetailUrl = $"https://mangadex.org/title/{manga.Id}",
            Thumbnail = thumbnail,
            Type = contentType,
            Genre = string.Join(", ", genres),
            LatestChapterNumber = 0,    // Not available directly from search; enriched separately
            LastUpdateText = attrs.UpdatedAt.ToTimeAgo(),
        };
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static string ResolveTitle(Dictionary<string, string>? titleMap)
    {
        if (titleMap == null || titleMap.Count == 0) return string.Empty;

        foreach (var lang in new[] { "en", "ja-ro", "ja", "ko-ro", "ko", "zh-ro" })
        {
            if (titleMap.TryGetValue(lang, out var t) && !string.IsNullOrWhiteSpace(t))
                return t;
        }

        return titleMap.Values.FirstOrDefault() ?? string.Empty;
    }

    private static string MapOriginalLanguageToType(string? lang) => lang switch
    {
        "ja" => "Manga",
        "ko" => "Manhwa",
        "zh" or "zh-hk" => "Manhua",
        _ => "Comic",
    };
}
