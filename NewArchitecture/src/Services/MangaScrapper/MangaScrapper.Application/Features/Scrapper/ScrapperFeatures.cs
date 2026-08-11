using MangaScrapper.Application.Common.Abstractions;
using MangaScrapper.Domain.Aggregates;
using MangaScrapper.Domain.Repositories;
using MangaScrapper.Domain.ValueObjects;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using NovaStack.Contracts.Responses;
using NovaStack.SharedKernel.Results;
using System.Net.Http.Json;

namespace MangaScrapper.Application.Features.Scrapper;

// -- 1. GetAllProvider ---------------------------------------------------------
public record GetAllProviderQuery : IQuery<List<ProviderInfoResponse>>;

internal sealed class GetAllProviderQueryHandler(
    [FromKeyedServices("komiku")] IProviderScrapperService komikuService)
    : IQueryHandler<GetAllProviderQuery, List<ProviderInfoResponse>>
{
    public async Task<Result<List<ProviderInfoResponse>>> Handle(GetAllProviderQuery query, CancellationToken ct)
    {
        // GetAllProvider returns the shared list of all providers; any service returns the same data.
        var providers = await komikuService.GetAllProvider();
        return providers;
    }
}

// -- 2. SearchJikan -----------------------------------------------------------
public record SearchJikanQuery(string Title) : IQuery<List<JikanMangaSearchDto>>;

internal sealed class SearchJikanQueryHandler(IHttpClientFactory httpClientFactory)
    : IQueryHandler<SearchJikanQuery, List<JikanMangaSearchDto>>
{
    public async Task<Result<List<JikanMangaSearchDto>>> Handle(SearchJikanQuery query, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query.Title))
            return new List<JikanMangaSearchDto>();

        try
        {
            var client = httpClientFactory.CreateClient();
            var response = await client.GetFromJsonAsync<JikanMangaResponse>(
                $"https://api.jikan.moe/v4/manga?q={Uri.EscapeDataString(query.Title)}&limit=10", ct);

            var items = response?.Data?.Select(d => new JikanMangaSearchDto(
                d.MalId,
                d.Title,
                d.Images?.Jpg?.ImageUrl ?? d.Images?.Webp?.ImageUrl,
                d.Score)).ToList() ?? [];

            return items;
        }
        catch (Exception ex)
        {
            return Error.Failure("Jikan.SearchFailed", ex.Message);
        }
    }
}

// -- 2b. SearchAnilist -----------------------------------------------------------
public record SearchAnilistQuery(string Title) : IQuery<List<AnilistMedia>>;

internal sealed class SearchAnilistQueryHandler(
    [FromKeyedServices("komiku")] IProviderScrapperService scrapperService)
    : IQueryHandler<SearchAnilistQuery, List<AnilistMedia>>
{
    public async Task<Result<List<AnilistMedia>>> Handle(SearchAnilistQuery query, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query.Title))
            return new List<AnilistMedia>();

        try
        {
            var items = await scrapperService.SearchAnilist(query.Title, ct);
            return items;
        }
        catch (Exception ex)
        {
            return Error.Failure("Anilist.SearchFailed", ex.Message);
        }
    }
}

// -- 3. ScrapChapterPages -----------------------------------------------------
public record ScrapChapterPagesCommand(Guid MangaId) : ICommand<int>;

internal sealed class ScrapChapterPagesCommandHandler(
    IMangaRepository mangaRepository,
    IScrapperQueueService queueService)
    : ICommandHandler<ScrapChapterPagesCommand, int>
{
    public async Task<Result<int>> Handle(ScrapChapterPagesCommand command, CancellationToken ct)
    {
        var manga = await mangaRepository.GetByIdAsync(new MangaId(command.MangaId), ct);
        if (manga is null)
            return Error.NotFound("Manga.NotFound", $"Manga with ID '{command.MangaId}' was not found.");

        var index = 0;
        foreach (var chapter in manga.Chapters.OrderBy(x => x.Number))
        {
            if (chapter.Pages.Count == 0)
            {
                await queueService.QueueChapterScraping(manga.Id.Value, manga.Title, chapter);
            }
            index++;
        }

        return index;
    }
}

// -- 4. GetQueue --------------------------------------------------------------
public record GetQueueQuery : IQuery<List<JobQueueItemResponse>>;

internal sealed class GetQueueQueryHandler(IScrapperQueueService queueService)
    : IQueryHandler<GetQueueQuery, List<JobQueueItemResponse>>
{
    public async Task<Result<List<JobQueueItemResponse>>> Handle(GetQueueQuery query, CancellationToken ct)
    {
        var rawJobs = await queueService.GetQueuedJobsAsync();

        var items = rawJobs.Select(j => new JobQueueItemResponse
        {
            Id = j.Id,
            JobName = j.JobName,
            State = j.State,
            CreatedAt = DateTime.UtcNow
        }).ToList();

        return items;
    }
}

// -- 5. FixFile ---------------------------------------------------------------
public record FixFileCommand : ICommand<FixFileResultResponse>;

internal sealed class FixFileCommandHandler(
    IMangaRepository repo,
    IScrapperSettingsProvider settings)
    : ICommandHandler<FixFileCommand, FixFileResultResponse>
{
    private readonly string _imageStoragePath = settings.ImageStoragePath;

    public async Task<Result<FixFileResultResponse>> Handle(FixFileCommand command, CancellationToken ct)
    {
        var mangas = await repo.GetAllAsync(ct);
        if (mangas is null || mangas.Count == 0)
        {
            return new FixFileResultResponse { Message = "No manga found to fix.", TotalFixed = 0 };
        }

        int totalFixed = 0;
        foreach (var manga in mangas)
        {
            bool thumbnailFixed = FixThumbnailPath(manga);
            int pagesFixed = FixChapterPages(manga);

            if (thumbnailFixed || pagesFixed > 0)
            {
                totalFixed += pagesFixed;
                await repo.UpdateAsync(manga, ct);
            }
        }

        return new FixFileResultResponse { Message = "File fixing complete", TotalFixed = totalFixed };
    }

    private bool FixThumbnailPath(Manga manga)
    {
        if (string.IsNullOrEmpty(manga.LocalImageUrl) || !manga.LocalImageUrl.StartsWith('/'))
            return false;

        var originalPath = manga.LocalImageUrl;
        var trimmedPath = originalPath.TrimStart('/');

        var oldFullPath = Path.Combine(_imageStoragePath, originalPath.Replace('/', Path.DirectorySeparatorChar));
        var newFullPath = Path.Combine(_imageStoragePath, trimmedPath.Replace('/', Path.DirectorySeparatorChar));

        if (File.Exists(oldFullPath) && !File.Exists(newFullPath))
        {
            File.Move(oldFullPath, newFullPath);
        }

        manga.UpdateLocalImage(trimmedPath, manga.ThumbnailSize);
        return true;
    }

    private int FixChapterPages(Manga manga)
    {
        int totalFixed = 0;
        foreach (var chapter in manga.Chapters)
        {
            for (int i = 0; i < chapter.Pages.Count; i++)
            {
                if (FixPage(chapter.Pages[i], i + 1))
                    totalFixed++;
            }
        }
        return totalFixed;
    }

    private bool FixPage(Page page, int index)
    {
        if (string.IsNullOrEmpty(page.LocalImageUrl)) return false;

        bool needsUpdate = false;
        var localPath = page.LocalImageUrl;
        if (localPath.StartsWith('/'))
        {
            localPath = localPath.TrimStart('/');
            needsUpdate = true;
        }

        var ext = Path.GetExtension(localPath);
        if (string.IsNullOrEmpty(ext)) ext = ".webp";
        var expectedFileName = $"{index}{ext}";
        var currentFileName = Path.GetFileName(localPath);
        var currentRelativeDir = Path.GetDirectoryName(localPath);

        if (string.IsNullOrEmpty(currentRelativeDir)) return needsUpdate;

        if (currentFileName != expectedFileName)
        {
            var newRelativePath = Path.Combine(currentRelativeDir, expectedFileName).Replace("\\", "/");
            var oldFullPath = Path.Combine(_imageStoragePath, localPath.Replace('/', Path.DirectorySeparatorChar));
            var newFullPath = Path.Combine(_imageStoragePath, newRelativePath.Replace('/', Path.DirectorySeparatorChar));

            try
            {
                if (File.Exists(oldFullPath))
                {
                    if (!File.Exists(newFullPath))
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(newFullPath)!);
                        File.Move(oldFullPath, newFullPath);
                    }
                    page.UpdateLocalImage(newRelativePath, page.Size);
                    return true;
                }

                if (File.Exists(newFullPath))
                {
                    page.UpdateLocalImage(newRelativePath, page.Size);
                    return true;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error fixing file {oldFullPath}: {ex.Message}");
            }
        }
        else if (needsUpdate)
        {
            page.UpdateLocalImage(localPath, page.Size);
        }

        return needsUpdate;
    }
}

// -- Endpoints Definition ------------------------------------------------------
public sealed class ScrapperEndpoints : IEndpointDefinition
{
    public void DefineEndpoints(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/scrapper").WithTags("Scrapper");

        group.MapGet("/providers", async (ISender sender, CancellationToken ct) =>
        {
            var res = await sender.Send(new GetAllProviderQuery(), ct);
            return res.IsSuccess ? Results.Ok(ApiResponse.Ok(res.Value)) : res.Error.ToHttpResult();
        }).WithName("GetAllProviders");

        group.MapGet("/jikan/search", async (string title, ISender sender, CancellationToken ct) =>
        {
            var res = await sender.Send(new SearchJikanQuery(title), ct);
            return res.IsSuccess ? Results.Ok(ApiResponse.Ok(res.Value)) : res.Error.ToHttpResult();
        }).WithName("SearchJikanManga");

        group.MapGet("/anilist/search", async (string title, ISender sender, CancellationToken ct) =>
        {
            var res = await sender.Send(new SearchAnilistQuery(title), ct);
            return res.IsSuccess ? Results.Ok(ApiResponse.Ok(res.Value)) : res.Error.ToHttpResult();
        }).WithName("SearchAnilistManga");

        group.MapGet("/manga/{mangaId:guid}/chapter-pages", async (Guid mangaId, ISender sender, CancellationToken ct) =>
        {
            var res = await sender.Send(new ScrapChapterPagesCommand(mangaId), ct);
            return res.IsSuccess
                ? Results.Ok(ApiResponse.Ok(new { Message = $"Scraping {res.Value} jobs queued for missing chapters." }))
                : res.Error.ToHttpResult();
        }).WithName("ScrapChapterPages");

        group.MapGet("/queue", async (ISender sender, CancellationToken ct) =>
        {
            var res = await sender.Send(new GetQueueQuery(), ct);
            return res.IsSuccess ? Results.Ok(ApiResponse.Ok(res.Value)) : res.Error.ToHttpResult();
        }).WithName("GetScrapperQueue");

        group.MapGet("/fixfile", async (ISender sender, CancellationToken ct) =>
        {
            var res = await sender.Send(new FixFileCommand(), ct);
            return res.IsSuccess ? Results.Ok(ApiResponse.Ok(res.Value)) : res.Error.ToHttpResult();
        }).WithName("FixFilePaths");
    }
}
