using FastEndpoints;
using MangaScrapper.Infrastructure.Repositories;

namespace MangaScrapper.Features.Manga.GetPaged;

public class Endpoint(IMangaRepository mangaRepository) : Endpoint<Request, Response>
{
    public override void Configure()
    {
        Get("/api/manga/paged");
        AllowAnonymous();
    }

    public override async Task HandleAsync(Request r, CancellationToken ct)
    {
        var (items, totalCount) = await mangaRepository.GetPagedAsync(
            r.Search,
            r.Genres,
            r.Status,
            r.Type,
            r.Page,
            r.PageSize,
            ct);

        var response = new Response
        {
            Items = items.Select(m => new MangaSummary
            {
                Id = m.Id,
                Title = m.Title,
                Author = m.Author,
                Type = m.Type,
                Genres = m.Genres,
                Description = m.Description,
                ImageUrl = m.ImageUrl,
                LocalImageUrl = m.LocalImageUrl,
                Status = m.Status,
                CreatedAt = m.CreatedAt,
                UpdatedAt = m.UpdatedAt,
                Url = m.Url,
                TotalView = m.Chapters.Sum(c => c.TotalView)
            }).ToList(),
            TotalCount = totalCount,
            Page = r.Page,
            PageSize = r.PageSize
        };

        await Send.OkAsync(response, ct);
    }
}