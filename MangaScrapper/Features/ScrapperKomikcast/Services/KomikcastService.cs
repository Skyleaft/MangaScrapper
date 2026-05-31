using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Web;
using Hangfire;
using HtmlAgilityPack;
using MangaScrapper.Infrastructure.Models;
using MangaScrapper.Infrastructure.Mongo.Collections;
using MangaScrapper.Infrastructure.Repositories;
using MangaScrapper.Infrastructure.Services;
using MangaScrapper.Infrastructure.Utils;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace MangaScrapper.Features.ScrapperKomikcast.Services;

public class KomikcastService : ScrapperServiceBase
{
    public KomikcastService(
        HttpClient httpClient,
        IMangaRepository mangaRepository,
        IBackgroundJobClient jobClient,
        IServiceScopeFactory scopeFactory,
        IOptions<ScrapperSettings> settings,
        SemaphoreSlim semaphore,
        MeilisearchService meilisearchService,
        QdrantService qdrantService)
        : base(httpClient, mangaRepository, jobClient, scopeFactory, settings, semaphore, meilisearchService, qdrantService)
    {
        LoadProvider("komikcast-provider.json");
    }

    private const string BaseUrl = "https://be.komikcast.cc/series";
    private string _fullUrl = BaseUrl + "/";

    protected override MangaDocument ExtractMangaMetadata(string url)
    {
        _fullUrl = url.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? url
            : $"{BaseUrl}/{url.TrimStart('/')}";
        var seriesData = HttpClient.GetFromJsonAsync<KomikcastResponse<KomikcastModel>>(_fullUrl).GetAwaiter().GetResult();
        var mangaData = new MangaDocument
        {
            Title = seriesData.Data.Data.Title,
            Author = seriesData.Data.Data.Author,
            Description = seriesData.Data.Data.Synopsis,
            Type = CultureInfo.CurrentCulture.TextInfo.ToTitleCase(seriesData.Data.Data.Format),
            ImageUrl = seriesData.Data.Data.CoverImage,
            Genres = seriesData.Data.Data.Genres.Select(x=>x.Data.Name).ToList(),
            Rating = seriesData.Data.Data.Rating,
            Status =  CultureInfo.CurrentCulture.TextInfo.ToTitleCase(seriesData.Data.Data.Status),
        };
        return mangaData;
    }

    protected override async Task<List<ChapterDocument>> ExtractChaptersMetadata(CancellationToken ct = default)
    {
        var chaptersUrl = _fullUrl.EndsWith("/chapters") ? _fullUrl : $"{_fullUrl}/chapters";
        var chapterData = await HttpClient.GetFromJsonAsync<KomikcastResponse<List<KomikcastChapters>>>(chaptersUrl, cancellationToken: ct);
        var chapters = new List<ChapterDocument>();
        foreach (var item in chapterData.Data)
        {
            chapters.Add(new ChapterDocument
            {
                Number = item.Data.Index,
                Link = $"{chaptersUrl}/{item.Data.Index}",
                ChapterProvider = Provider.ProviderName,
                ChapterProviderIcon = Provider.ProviderIcon,
                TotalView = item.Views?.Total??0,
                UploadDate = item.CreatedAt
            });
        }
        return chapters;
    }

    public override async Task<ChapterDocument> GetChapterPage(string mangaTitle, ChapterDocument chapter, CancellationToken ct = default)
    {
        var url = chapter.Link;
        if (string.IsNullOrWhiteSpace(url)) return chapter;

        var response = await HttpClient.GetFromJsonAsync<KomikcastResponse<KomikcastChapterDetails>>(url, cancellationToken: ct);
        if (response?.Data?.Data?.Images == null)
        {
            return chapter;
        }

        var images = response.Data.Data.Images;
        var downloadTasks = images.Select(async (imageUrl, index) =>
        {
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
            catch (Exception)
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

    public override async Task<List<SearchItem>> SearchManga(SearchRequest request, CancellationToken ct)
    {
        
        var queryParams = new List<KeyValuePair<string, string?>>
        {
            new("takeChapter", "1"),
            new("includeMeta", "true"),
            new("sort", "latest"),
            new("sortOrder", "desc"),
            new("take", "12"),
            new("page", request.Page.ToString())
        };
        if (!string.IsNullOrEmpty(request.Keyword))
        {
            queryParams.Add(new("filter",$"title=like={request.Keyword}"));
        }
        if(request.Genres!=null && request.Genres.Count>0)
        {
            foreach (var genre in request.Genres)
            {
                queryParams.Add(new("genreIds", genre));
            }
        }
        
        var fullUrl = QueryHelpers.AddQueryString(BaseUrl, queryParams);

        var data = await HttpClient.GetFromJsonAsync<KomikcastResponse<List<KomikcastModel>>>(fullUrl, cancellationToken: ct);

        var resp = new List<SearchItem>();

        foreach (var item in data.Data)
        {
            var sItem = new SearchItem();
            sItem.Title = item.Data.Title;
            sItem.LatestChapterNumber = item.Chapters.First().ChapterIndex??0;
            sItem.Thumbnail = item.Data.CoverImage;
            sItem.DetailUrl = item.Data.Slug;
            sItem.Type = CultureInfo.CurrentCulture.TextInfo.ToTitleCase(item.Data.Format);
            sItem.Genre = string.Join(",",item.Data.Genres.Select(x => x.Data.Name));
            sItem.LastUpdateText = item.Chapters.First().CreatedAt.ToTimeAgo();
            
            await EnrichSearchItemAsync(sItem, ct);
            resp.Add(sItem);
        }
        return resp;
    }
}
