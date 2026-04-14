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

    protected override MangaDocument ExtractMangaMetadata(string url)
    {
        return null;
    }

    protected override async Task<List<ChapterDocument>> ExtractChaptersMetadata(CancellationToken ct = default)
    {
        return null;
    }

    public override async Task<List<SearchItem>> SearchManga(SearchRequest request, CancellationToken ct)
    {
        var baseUrl = "https://be.komikcast.cc/series";
        
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
        
        var fullUrl = QueryHelpers.AddQueryString(baseUrl, queryParams);

        var data = await HttpClient.GetFromJsonAsync<KomikcastResponse<List<KomikcastModel>>>(fullUrl, cancellationToken: ct);

        var resp = new List<SearchItem>();

        foreach (var item in data.Data)
        {
            var sItem = new SearchItem();
            sItem.Title = item.Data.Title;
            sItem.LatestChapterNumber = item.Chapters.First().ChapterIndex;
            sItem.Thumbnail = item.Data.CoverImage;
            sItem.DetailUrl = item.Data.Slug;
            sItem.Type = item.Data.Format;
            sItem.Genre = string.Join(",",item.Data.Genres.Select(x => x.Data.Name));
            sItem.LastUpdateText = item.Chapters.First().CreatedAt.ToTimeAgo();
            
            await EnrichSearchItemAsync(sItem, ct);
            resp.Add(sItem);
        }
        return resp;
    }
}
