using System.Globalization;
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
    
    public async Task<HtmlDocument> GetHtml(string url,string? query=null,MultipartFormDataContent? formData = null)
    {
        if (formData != null)
        {
            var responseForm = await HttpClient.PostAsync(url,formData);
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
                    responseForm = await HttpClient.PostAsync(newUrl, formData);
                }
            }
            responseForm.EnsureSuccessStatusCode();
            var str1 = await responseForm.Content.ReadAsStringAsync();
            var doc1 = new HtmlDocument();
            doc1.LoadHtml(str1);
            return doc1;
        }
        else
        {
            var response = await HttpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();
            var str = await response.Content.ReadAsStringAsync();
            var doc = new HtmlDocument();
            doc.LoadHtml(str);
            return doc; 
        }
        
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

        if (IsWebpUrl(imageUrl))
        {
            await using var output = File.Create(filePath);
            await imageStream.CopyToAsync(output);
            return relativePath.Replace("\\", "/");
        }

        using var image = await Image.LoadAsync(imageStream);
        await image.SaveAsync(filePath, new WebpEncoder());

        return relativePath.Replace("\\", "/");
    }

    private static bool IsWebpUrl(string imageUrl)
    {
        if (!Uri.TryCreate(imageUrl, UriKind.Absolute, out var uri))
        {
            return imageUrl.Contains(".webp", StringComparison.OrdinalIgnoreCase);
        }

        return string.Equals(Path.GetExtension(uri.AbsolutePath), ".webp", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<JikanMangaItem> GetMangaInfo(string title)
    {
        var query = HttpUtility.ParseQueryString(string.Empty);
        query["q"] = title;
        query["type"] = "manga";
        query["limit"] = "1";
        
        var url = $"https://api.jikan.moe/v4/manga?{query}";
        var response = await HttpClient.GetFromJsonAsync<JikanMangaResponse>(url);
        return response?.Data?.FirstOrDefault() ?? new JikanMangaItem();
    }
    
    public async Task<MangaDocument> UpdateMangaDocument(MangaDocument manga)
    {
        var mangaInfo = await GetMangaInfo(manga.Title);
        if (mangaInfo != null)
        {
            var combinedTittleSynonym = string.Join(" ", mangaInfo.TitleSynonyms);
            if (StringHelper.IsSimilar(mangaInfo.Title,manga.Title)||
                StringHelper.IsSimilar(mangaInfo.TitleEnglish,manga.Title)||
                StringHelper.IsSimilar(combinedTittleSynonym,manga.Title)||
                StringHelper.IsSimilar(mangaInfo.TitleJapanese,manga.Title)
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
            }
            
        }

        return manga;
    }

    public async Task<MangaDocument> ExtractManga(string url, CancellationToken ct, bool scrapChapters = true)
    {
        var doc = await GetHtml(url);
        var mangaData = ExtractMangaMetadata(doc);
        mangaData.Url = url;

        var existingManga = await MangaRepository.GetByTitleAsync(mangaData.Title, ct);

        if (!string.IsNullOrWhiteSpace(mangaData.ImageUrl))
        {
            mangaData.ImageUrl = ThumbnailHelper.RemoveResizeParams(mangaData.ImageUrl);
            mangaData.LocalImageUrl = await DownloadThumbnailAndConvertToWebP(mangaData.Title, mangaData.ImageUrl);
        }

        var chapters = await ExtractChapters(doc);

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

            existingManga = await UpdateMangaDocument(existingManga);
            await MangaRepository.UpdateAsync(existingManga, ct);

            return existingManga;
        }

        mangaData.Chapters = chapters;
        mangaData.CreatedAt = chapters.OrderBy(x => x.UploadDate).FirstOrDefault()?.UploadDate ?? DateTime.MinValue;
        mangaData.UpdatedAt = DateTime.UtcNow;

        var manga = await UpdateMangaDocument(mangaData);
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

    protected abstract MangaDocument ExtractMangaMetadata(HtmlDocument doc);
    protected abstract Task<List<ChapterDocument>> ExtractChapters(HtmlDocument doc);

    public async Task<ChapterDocument> GetChapterPage(string mangaTitle, ChapterDocument chapter)
    {
        var url = chapter.Link;
        if (string.IsNullOrWhiteSpace(url)) return chapter;

        if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            url = Provider.BaseUrl.TrimEnd('/') + "/" + url.TrimStart('/');
        }

        var doc = await GetHtml(url);

        var imageNodes = doc.DocumentNode.SelectNodes(Provider.PageSelectors.Images);
        if (imageNodes == null) return chapter;

        var downloadTasks = imageNodes.Select(async (imgNode, index) =>
        {
            var imageUrl = imgNode.GetAttributeValue("src", string.Empty);
            if (string.IsNullOrWhiteSpace(imageUrl)) return (Index: index, Page: null as PageDocument);

            await Semaphore.WaitAsync();
            try
            {
                var localPath = await DownloadAndConvertToWebP(
                    mangaTitle,
                    chapter.Number.ToString(CultureInfo.InvariantCulture),
                    imageUrl,
                    index + 1);
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
        await TaskQueue.QueueBackgroundWorkItemAsync(mangaTitle, chapter.Number, async token =>
        {
            using var scope = ScopeFactory.CreateScope();
            var scopedScrapper = (ScrapperServiceBase)scope.ServiceProvider.GetRequiredService(this.GetType());
            var scopedRepo = scope.ServiceProvider.GetRequiredService<IMangaRepository>();

            var processedChapter = await scopedScrapper.GetChapterPage(mangaTitle, chapter);
            await scopedRepo.UpdateChapterPagesAsync(mangaId, chapter.Id, processedChapter.Pages, token);
        });
    }

    public abstract Task<List<SearchItem>> SearchManga(SearchRequest request, CancellationToken ct);

    public async Task<List<ChapterDocument>> GetAllChapters(string url)
    {
        var doc = await GetHtml(url);
        return await ExtractChapters(doc);
    }

    public async Task<List<PageDocument>> GetAllPages(string url)
    {
        var chapter = new ChapterDocument { Link = url };
        var processedChapter = await GetChapterPage("temp", chapter);
        return processedChapter.Pages;
    }
}
