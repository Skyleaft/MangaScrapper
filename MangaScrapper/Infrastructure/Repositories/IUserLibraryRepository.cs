using MangaScrapper.Infrastructure.Mongo.Collections;

namespace MangaScrapper.Infrastructure.Repositories;

public interface IUserLibraryRepository
{
    Task<UserLibraryDocument?> GetByUserAndMangaAsync(Guid userId, Guid mangaId, CancellationToken ct);
    Task<List<UserLibraryDocument>> GetByUserAsync(Guid userId, CancellationToken ct);
    Task<UserLibraryDocument> CreateAsync(UserLibraryDocument document, CancellationToken ct);
    Task UpdateAsync(UserLibraryDocument document, CancellationToken ct);
    Task DeleteAsync(Guid id, CancellationToken ct);
}
