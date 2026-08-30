using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Web;
using HtmlAgilityPack;
using MangaScrapper.Core.Aggregates;
using MangaScrapper.Core.Configuration;
using MangaScrapper.Core.Repositories;
using MangaScrapper.Core.Services;
using MangaScrapper.Core.Utils;
using MangaScrapper.Core.ValueObjects;
using NovaStack.Infrastructure.Messaging;

namespace MangaScrapper.Core.Scrapers.Manhwadesu;

public class ManhwadesuService : ScrapperServiceBase
{
    protected override string ProviderKey => "manhwadesu";

    public ManhwadesuService(
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
        LoadProvider("manhwadesu-provider.json");
    }

    private HtmlDocument? doc;

    protected override Manga ExtractMangaMetadata(string url)
    {
        doc = GetHtml(url).GetAwaiter().GetResult();

        var title = HttpUtility.HtmlDecode(
            doc.DocumentNode.SelectSingleNode(Provider.MangaSelectors.Title)?.InnerText.Trim()
            ?? doc.DocumentNode.SelectSingleNode("//h1[@itemprop='name']")?.InnerText.Trim()
            ?? string.Empty);

        var author = doc.DocumentNode
            .SelectSingleNode(Provider.MangaSelectors.Author)?.InnerText.Trim()
            ?? doc.DocumentNode.SelectSingleNode("//div[contains(@class,'imptdt') and contains(.,'Author')]//i")?.InnerText.Trim()
            ?? string.Empty;

        var description = doc.DocumentNode
            .SelectSingleNode(Provider.MangaSelectors.Description)?.InnerText.Trim()
            ?? doc.DocumentNode.SelectSingleNode("//div[@itemprop='description']")?.InnerText.Trim();

        var type = doc.DocumentNode
            .SelectSingleNode(Provider.MangaSelectors.Type)?.InnerText.Trim()
            ?? doc.DocumentNode.SelectSingleNode("//div[contains(@class,'imptdt') and contains(.,'Type')]//a")?.InnerText.Trim()
            ?? string.Empty;

        var status = doc.DocumentNode
            .SelectSingleNode(Provider.MangaSelectors.Status)?.InnerText.Trim()
            ?? doc.DocumentNode.SelectSingleNode("//div[contains(@class,'imptdt') and contains(.,'Status')]//i")?.InnerText.Trim()
            ?? "Ongoing";

        var imageUrl = doc.DocumentNode
            .SelectSingleNode(Provider.MangaSelectors.Thumbnail)?.GetAttributeValue("src", string.Empty)
            ?? doc.DocumentNode.SelectSingleNode("//div[contains(@class,'thumb')]//img")?.GetAttributeValue("src", string.Empty);

        // Posted On: parse datetime attribute from <time> tag
        DateTime? releaseDate = null;
        var releaseDateAttr = doc.DocumentNode
            .SelectSingleNode("//div[contains(@class,'imptdt') and contains(.,'Posted On')]//time")
            ?.GetAttributeValue("datetime", null);

        if (!string.IsNullOrEmpty(releaseDateAttr) &&
            DateTimeOffset.TryParse(releaseDateAttr, out var parsedRelease))
        {
            releaseDate = parsedRelease.UtcDateTime;
        }

        var genreNodes = doc.DocumentNode.SelectNodes(Provider.MangaSelectors.Genres);
        var genres = genreNodes?.Select(n => HttpUtility.HtmlDecode(n.InnerText.Trim())).ToList();

        // Parse rating from aggregateRating / ratingValue
        double? rating = null;
        var ratingNode = doc.DocumentNode.SelectSingleNode("//div[@itemprop='ratingValue']")
                         ?? doc.DocumentNode.SelectSingleNode("//div[contains(@class,'rating-prc')]//div[contains(@class,'num')]");
        var ratingText = ratingNode?.GetAttributeValue("content", string.Empty);
        if (string.IsNullOrWhiteSpace(ratingText))
        {
            ratingText = ratingNode?.InnerText.Trim();
        }

        if (!string.IsNullOrEmpty(ratingText) &&
            double.TryParse(ratingText, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedRating))
        {
            rating = parsedRating;
        }

        imageUrl = ThumbnailHelper.RemoveQueryString(imageUrl);

        var isNsfw = true; // Manhwadesu is an 18+ manhwa portal

        return Manga.Create(
            title: title,
            author: author,
            type: type,
            source: ProviderKey,
            genres: genres,
            description: description,
            imageUrl: imageUrl,
            rating: rating,
            status: status,
            releaseDate: releaseDate,
            nsfw: isNsfw);
    }

    protected override Task<List<Chapter>> ExtractChaptersMetadata(CancellationToken ct = default)
    {
        var chapters = new List<Chapter>();
        var chapterRows = doc!.DocumentNode.SelectNodes(Provider.ChapterSelectors.Rows);
        if (chapterRows == null) return Task.FromResult(chapters);

        foreach (var row in chapterRows)
        {
            var linkNode = row.SelectSingleNode(Provider.ChapterSelectors.Link);
            var link = linkNode?.GetAttributeValue("href", string.Empty);
            if (string.IsNullOrWhiteSpace(link)) continue;

            var chapterText = row.SelectSingleNode(Provider.ChapterSelectors.ChapterText)?.InnerText.Trim();
            var dateText = row.SelectSingleNode(Provider.ChapterSelectors.UploadDate)?.InnerText.Trim();

            // First attempt: read from data-num attribute (e.g. data-num="156")
            var dataNum = row.GetAttributeValue("data-num", string.Empty);
            double chapterNumber = 0;
            if (!string.IsNullOrWhiteSpace(dataNum) &&
                double.TryParse(dataNum, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedDataNum))
            {
                chapterNumber = parsedDataNum;
            }
            else
            {
                // Fallback: parse from chapter text (e.g. "Chapter 156", "Chapter 137 - End")
                var chapterNumberText = Regex.Match(
                    chapterText?.Replace("Chapter ", "", StringComparison.OrdinalIgnoreCase) ?? "",
                    @"\d+(\.\d+)?").Value;

                if (double.TryParse(chapterNumberText, NumberStyles.Float, CultureInfo.InvariantCulture, out var num))
                {
                    chapterNumber = num;
                }
            }

            var uploadDate = ParseIndonesianDate(dateText);

            chapters.Add(new Chapter(
                id: ChapterId.New(),
                number: chapterNumber,
                link: link,
                chapterProvider: Provider.ProviderName,
                chapterProviderIcon: Provider.ProviderIcon,
                language: DefaultIndonesianLanguage,
                totalView: 0,
                uploadDate: uploadDate));
        }

        return Task.FromResult(chapters);
    }

    /// <summary>
    /// Parses Indonesian month names used by Manhwadesu (e.g. "Maret 15, 2025", "April 18, 2024").
    /// Falls back to <see cref="DateTime.MinValue"/> when parsing fails.
    /// </summary>
    private static DateTime ParseIndonesianDate(string? dateText)
    {
        if (string.IsNullOrWhiteSpace(dateText)) return DateTime.MinValue;

        var normalized = dateText
            .Replace("Januari", "January")
            .Replace("Februari", "February")
            .Replace("Maret", "March")
            .Replace("April", "April")
            .Replace("Mei", "May")
            .Replace("Juni", "June")
            .Replace("Juli", "July")
            .Replace("Agustus", "August")
            .Replace("September", "September")
            .Replace("Oktober", "October")
            .Replace("November", "November")
            .Replace("Desember", "December");

        return DateTime.TryParse(normalized, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : DateTime.MinValue;
    }

    public override async Task<Chapter> GetChapterPage(
        string mangaTitle,
        Chapter chapter,
        CancellationToken ct = default,
        Func<int, int, Task>? onProgress = null)
    {
        var url = chapter.Link;
        if (string.IsNullOrWhiteSpace(url)) return chapter;
        if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            url = Provider.BaseUrl.TrimEnd('/') + "/" + url.TrimStart('/');

        var chapterDoc = await GetHtml(url, ct: ct);
        var imageUrls = new List<string>();

        // 1. Extract from static <img> tags in #readerarea
        var imageNodes = chapterDoc.DocumentNode.SelectNodes(Provider.PageSelectors.Images);
        if (imageNodes != null && imageNodes.Count > 0)
        {
            foreach (var node in imageNodes)
            {
                var src = node.GetAttributeValue("src", string.Empty);
                if (string.IsNullOrWhiteSpace(src))
                    src = node.GetAttributeValue("data-src", string.Empty);

                if (!string.IsNullOrWhiteSpace(src))
                {
                    imageUrls.Add(src.Trim());
                }
            }
        }

        // 2. Fallback: extract from JavaScript ts_reader.run({...})
        if (imageUrls.Count == 0)
        {
            var scriptNodes = chapterDoc.DocumentNode.SelectNodes("//script");
            if (scriptNodes != null)
            {
                foreach (var script in scriptNodes)
                {
                    var text = script.InnerText;
                    if (string.IsNullOrEmpty(text) || !text.Contains("ts_reader.run")) continue;

                    var match = Regex.Match(text, @"ts_reader\.run\((?<json>\{.+?\})\);", RegexOptions.Singleline);
                    if (match.Success)
                    {
                        try
                        {
                            using var jsonDoc = JsonDocument.Parse(match.Groups["json"].Value);
                            if (jsonDoc.RootElement.TryGetProperty("sources", out var sourcesElement) &&
                                sourcesElement.ValueKind == JsonValueKind.Array)
                            {
                                foreach (var source in sourcesElement.EnumerateArray())
                                {
                                    if (source.TryGetProperty("images", out var imagesElement) &&
                                        imagesElement.ValueKind == JsonValueKind.Array)
                                    {
                                        foreach (var img in imagesElement.EnumerateArray())
                                        {
                                            var imgUrl = img.GetString();
                                            if (!string.IsNullOrWhiteSpace(imgUrl))
                                            {
                                                imageUrls.Add(imgUrl.Trim());
                                            }
                                        }
                                        if (imageUrls.Count > 0) break;
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Logger.LogWarning(ex, "Failed to parse ts_reader.run JSON for chapter at {Url}", url);
                        }
                    }
                }
            }
        }

        if (imageUrls.Count == 0) return chapter;

        var total = imageUrls.Count;
        var completed = 0;
        if (onProgress != null && total > 0)
        {
            await onProgress(0, total);
        }

        var downloadTasks = imageUrls.Select(async (imageUrl, index) =>
        {
            if (string.IsNullOrWhiteSpace(imageUrl)) return (Index: index, Page: null as Page);

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

                return (Index: index, Page: new Page(Guid.CreateVersion7(), imageUrl, result.path, result.size, result.width, result.height));
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Failed to download/convert image at index {Index} for {MangaTitle} (Manhwadesu)", index, mangaTitle);
                throw;
            }
            finally
            {
                Semaphore.Release();
            }
        });

        var results = await Task.WhenAll(downloadTasks);
        var pages = results.OrderBy(r => r.Index).Where(r => r.Page != null).Select(r => r.Page!).ToList();
        chapter.AddPages(pages);
        return chapter;
    }

    public override async Task<List<SearchItem>> SearchManga(SearchRequest request, CancellationToken ct)
    {
        var baseUrl = Provider.BaseUrl.TrimEnd('/');
        string url;

        if (!string.IsNullOrEmpty(request.Keyword))
        {
            // Search query endpoint: https://manhwadesu.wiki/?s=<keyword> or /page/<page>/?s=<keyword>
            var page = request.Page > 1 ? $"page/{request.Page}/" : string.Empty;
            url = $"{baseUrl}/{page}?s={HttpUtility.UrlEncode(request.Keyword)}";
        }
        else if (!string.IsNullOrEmpty(request.Type))
        {
            var page = request.Page > 1 ? $"page/{request.Page}/" : string.Empty;
            url = $"{baseUrl}/komik/{page}?type={HttpUtility.UrlEncode(request.Type)}&order=update";
        }
        else
        {
            // Default latest browse endpoint: https://manhwadesu.wiki/komik/?type=manhwa&order=update
            var page = request.Page > 1 ? $"page/{request.Page}/" : string.Empty;
            url = $"{baseUrl}/komik/{page}?type=manhwa&order=update";
        }

        var searchDoc = await GetHtml(url, ct: ct);
        var results = new List<SearchItem>();

        // Cards: div.bs > div.bsx > a
        var cards = searchDoc.DocumentNode.SelectNodes("//div[contains(@class,'bsx')]/a");
        if (cards == null) return results;

        foreach (var card in cards)
        {
            try
            {
                var detailUrl = card.GetAttributeValue("href", string.Empty);
                var titleText = HttpUtility.HtmlDecode(card.GetAttributeValue("title", string.Empty).Trim());

                if (string.IsNullOrWhiteSpace(titleText))
                    titleText = HttpUtility.HtmlDecode(card.SelectSingleNode(".//div[@class='tt']")?.InnerText.Trim() ?? string.Empty);

                var thumbnail = card.SelectSingleNode(".//img")?.GetAttributeValue("src", string.Empty);

                // Latest chapter number
                var latestChapterText = card.SelectSingleNode(".//div[@class='epxs']")?.InnerText.Trim();
                var chapterNumberText = Regex.Match(
                    latestChapterText?.Replace("Chapter ", "", StringComparison.OrdinalIgnoreCase) ?? "",
                    @"\d+(\.\d+)?").Value;

                var chapterNumber = double.TryParse(
                    chapterNumberText, NumberStyles.Float, CultureInfo.InvariantCulture, out var num) ? num : 0;

                // Rating / Score
                var ratingText = card.SelectSingleNode(".//div[@class='numscore']")?.InnerText.Trim();

                var item = new SearchItem
                {
                    Title = titleText,
                    DetailUrl = detailUrl,
                    Thumbnail = ThumbnailHelper.RemoveQueryString(thumbnail) ?? string.Empty,
                    LatestChapterNumber = chapterNumber,
                    LastUpdateText = ratingText
                };

                results.Add(item);
            }
            catch
            {
                // skip malformed card
            }
        }

        await Task.WhenAll(results.Select(item => EnrichSearchItemAsync(item, ct)));

        return results;
    }
}
