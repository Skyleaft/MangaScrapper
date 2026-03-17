using MangaScrapper.Infrastructure.Mongo.Collections;

namespace MangaScrapper.Features.UserLibrary.Services;

public interface IUserLibraryService
{
    Task<UserLibraryDocument> AddOrUpdateLibraryEntryAsync(Guid userId, Guid mangaId, string mangaTitle, string mangaImageUrl,string type, string status, CancellationToken ct);
    Task<List<UserLibraryDocument>> GetUserLibraryAsync(Guid userId, CancellationToken ct);
    Task RemoveFromLibraryAsync(Guid userId, Guid mangaId, CancellationToken ct);
}
