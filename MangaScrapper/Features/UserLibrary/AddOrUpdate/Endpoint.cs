using System.Security.Claims;
using FastEndpoints;
using MangaScrapper.Features.UserLibrary.Services;
using MangaScrapper.Infrastructure.Mongo.Collections;

namespace MangaScrapper.Features.UserLibrary.AddOrUpdate;

public class Request
{
    public Guid MangaId { get; set; }
    public string MangaTitle { get; set; } = string.Empty;
    public string MangaImageUrl { get; set; } = string.Empty;
    public string Type { get; set; } = "Manga";
    public string Status { get; set; } = "Reading";
}

public class Endpoint(IUserLibraryService service) : Endpoint<Request, UserLibraryDocument>
{
    public override void Configure()
    {
        Post("/api/user-library");
    }

    public override async Task HandleAsync(Request r, CancellationToken ct)
    {
        var userId = User.FindFirst(c => c.Type == ClaimTypes.NameIdentifier)?.Value ??
                     throw new Exception("User not found");
        var result = await service.AddOrUpdateLibraryEntryAsync(Guid.Parse(userId), r.MangaId, r.MangaTitle, r.MangaImageUrl,r.Type, r.Status, ct);
        await Send.OkAsync(result, ct);
    }
}
