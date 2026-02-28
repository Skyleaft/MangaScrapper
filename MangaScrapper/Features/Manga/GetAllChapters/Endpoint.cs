using FastEndpoints;
using MangaScrapper.Infrastructure.Repositories;
using MangaScrapper.Infrastructure.Mongo.Collections;

namespace MangaScrapper.Features.Manga.GetAllChapters;

public class Request
{
    public Guid MangaId { get; set; }
}

public class Endpoint(IMangaRepository repo) : Endpoint<Request, List<Response>>
{
    public override void Configure()
    {
        Get("/api/manga/{MangaId}/chapters");
        AllowAnonymous();
    }

    public override async Task HandleAsync(Request r, CancellationToken ct)
    {
        var manga = await repo.GetByIdAsync(r.MangaId, ct);
        
        if (manga == null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        var response = manga.Chapters
            .OrderByDescending(c => c.Number)
            .Select(c => new Response
            {
                Id = c.Id,
                Number = c.Number,
                TotalView = c.TotalView,
                UploadDate = c.UploadDate,
                PageCount = c.Pages?.Count ?? 0,
                IsChapterAvailable = c.Pages?.Any() ?? false
            })
            .ToList();

        await Send.OkAsync(response, ct);
    }
}