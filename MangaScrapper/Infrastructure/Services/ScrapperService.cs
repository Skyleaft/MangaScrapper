using Hangfire;
using System.Text.Json;
using HtmlAgilityPack;
using MangaScrapper.Infrastructure.BackgroundJobs;
using MangaScrapper.Infrastructure.Models;
using MangaScrapper.Infrastructure.Mongo.Collections;
using MangaScrapper.Infrastructure.Repositories;
using Microsoft.Extensions.Options;

namespace MangaScrapper.Infrastructure.Services;

public class ScrapperService : ScrapperServiceBase
{
    public ScrapperService(
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
    }

    protected override MangaDocument ExtractMangaMetadata(HtmlDocument doc)
    {
        throw new NotImplementedException();
    }

    protected override Task<List<ChapterDocument>> ExtractChapters(HtmlDocument doc, CancellationToken ct = default)
    {
        throw new NotImplementedException();
    }

    public override Task<List<SearchItem>> SearchManga(SearchRequest request, CancellationToken ct)
    {
        throw new NotImplementedException();
    }
}