using System.Security.Claims;
using FastEndpoints;
using MangaScrapper.Features.UserProgression.Services;
using MangaScrapper.Infrastructure.Mongo.Collections;

namespace MangaScrapper.Features.UserProgression.Update;

public class Request
{
    public Guid MangaId { get; set; }
    public Guid ChapterId { get; set; }
    public double ChapterNumber { get; set; }
    public int LastReadPage { get; set; }
    public int TotalPages { get; set; }
    public int ReadingTimeSeconds { get; set; }
}

public class Endpoint(IUserProgressionService service) : Endpoint<Request, UserProgressionDocument>
{
    public override void Configure()
    {
        Post("/api/user-progression");
    }

    public override async Task HandleAsync(Request r, CancellationToken ct)
    {
        var userId = User.FindFirst(c => c.Type == ClaimTypes.NameIdentifier)?.Value ??
                     throw new Exception("User not found");
        var result = await service.UpdateProgressionAsync(
            Guid.Parse(userId), r.MangaId, r.ChapterId, r.ChapterNumber, r.LastReadPage, r.TotalPages, r.ReadingTimeSeconds, ct);
        await Send.OkAsync(result, ct);
    }
}
