using FastEndpoints;
using MangaScrapper.Infrastructure.Repositories;
using MangaScrapper.Infrastructure.Services;

namespace MangaScrapper.Features.Manga.GetPaged;

public class Endpoint(IMangaRepository mangaRepository, MeilisearchService meilisearchService) : Endpoint<Request, Response>
{
    public override void Configure()
    {
        Get("/api/manga/paged");
        AllowAnonymous();
    }

    public override async Task HandleAsync(Request r, CancellationToken ct)
    {
        var (meiliItems, meiliTotalCount) = await meilisearchService.SearchAsync(
            r.Search,
            r.Genres,
            r.Status,
            r.Type,
            r.SortBy ?? "updatedAt",
            r.OrderBy ?? "desc",
            r.Page,
            r.PageSize,
            ct);

        // Collect the IDs from Meilisearch results to fetch full documents from MongoDB
        var ids = meiliItems.Select(m => Guid.Parse(m.Id)).ToList();

        // Fetch full manga documents from MongoDB (with chapter data for LatestChapter)
        var mongoDocs = new Dictionary<Guid, Infrastructure.Mongo.Collections.MangaDocument>();
        if (ids.Count > 0)
        {
            var fullDocs = await mangaRepository.GetByIdsAsync(ids, ct);
            mongoDocs = fullDocs.ToDictionary(m => m.Id);
        }

        var response = new Response
        {
            Items = meiliItems.Select(m =>
            {
                var id = Guid.Parse(m.Id);
                mongoDocs.TryGetValue(id, out var mongoDoc);

                return new MangaSummary
                {
                    Id = id,
                    Title = m.Title,
                    Author = m.Author,
                    Type = m.Type,
                    Genres = m.Genres,
                    Description = m.Description,
                    ImageUrl = m.ImageUrl,
                    LocalImageUrl = m.LocalImageUrl,
                    Status = m.Status,
                    CreatedAt = DateTimeOffset.FromUnixTimeSeconds(m.CreatedAtTimestamp).UtcDateTime,
                    UpdatedAt = DateTimeOffset.FromUnixTimeSeconds(m.UpdatedAtTimestamp).UtcDateTime,
                    Url = mongoDoc?.Url,
                    MalId = mongoDoc?.MalID ?? 0,
                    Members = mongoDoc?.Members ?? 0,
                    Popularity = m.Popularity,
                    Rating = m.Rating,
                    ReleaseDate = mongoDoc?.ReleaseDate,
                    TotalView = m.TotalView > 0 ? m.TotalView : mongoDoc?.Chapters.Sum(c => c.TotalView) ?? 0,
                    LatestChapter = mongoDoc?.Chapters
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
                };
            }).ToList(),
            TotalCount = meiliTotalCount,
            Page = r.Page,
            PageSize = r.PageSize
        };

        await Send.OkAsync(response, ct);
    }
}