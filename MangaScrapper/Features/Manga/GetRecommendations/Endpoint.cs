using FastEndpoints;
using MangaScrapper.Features.Manga.GetPaged;
using MangaScrapper.Infrastructure.Repositories;
using MangaScrapper.Infrastructure.Services;

namespace MangaScrapper.Features.Manga.GetRecommendations;

public class Endpoint(QdrantService qdrantService, IMangaRepository mangaRepository) : Endpoint<Request, Response>
{
    public override void Configure()
    {
        Post("/api/manga/recommendations");
        AllowAnonymous();
    }

    public override async Task HandleAsync(Request r, CancellationToken ct)
    {
        if (r.ReadingHistoryIds == null || r.ReadingHistoryIds.Count == 0)
        {
            await Send.OkAsync(new Response { Items = new() }, ct);
            return;
        }

        var recommendedIds = await qdrantService.RecommendAsync(r.ReadingHistoryIds, r.Limit, ct);

        if (recommendedIds.Count == 0)
        {
            await Send.OkAsync(new Response { Items = new() }, ct);
            return;
        }

        var fullDocs = await mangaRepository.GetByIdsAsync(recommendedIds, ct);

        // Map to MangaSummary response
        var items = fullDocs.Select(doc => new MangaSummary
        {
            Id = doc.Id,
            Title = doc.Title,
            Author = doc.Author,
            Type = doc.Type,
            Genres = doc.Genres ?? new List<string>(),
            Description = doc.Description,
            ImageUrl = doc.ImageUrl,
            LocalImageUrl = doc.LocalImageUrl,
            Status = doc.Status,
            CreatedAt = doc.CreatedAt.ToUniversalTime(),
            UpdatedAt = doc.UpdatedAt.ToUniversalTime(),
            Url = doc.Url,
            MalId = doc.MalID,
            Members = doc.Members,
            Popularity = doc.Popularity,
            Rating = doc.Rating,
            ReleaseDate = doc.ReleaseDate,
            TotalView = doc.Chapters?.Sum(c => c.TotalView) ?? 0,
            LatestChapter = doc.Chapters?
                .OrderByDescending(c => c.Number)
                .Select(c => new LatestChapterSummary
                {
                    Id = c.Id,
                    Number = c.Number,
                    TotalView = c.TotalView,
                    UploadDate = c.UploadDate,
                    ChapterProvider = c.ChapterProvider,
                    ChapterProviderIcon = c.ChapterProviderIcon
                }).FirstOrDefault() ?? new()
        }).ToList();

        // Ensure order matches the recommended IDs order
        var orderedItems = items.OrderBy(x => recommendedIds.IndexOf(x.Id)).ToList();

        await Send.OkAsync(new Response { Items = orderedItems }, ct);
    }
}
