using MangaScrapper.Infrastructure.Mongo.Collections;

namespace MangaScrapper.Infrastructure.Repositories;

public interface IUserProgressionRepository
{
    Task<UserProgressionDocument?> GetByUserAndMangaAsync(Guid userId, Guid mangaId, CancellationToken ct);
    Task<List<UserProgressionDocument>> GetByUserAsync(Guid userId, CancellationToken ct);
    Task<UserProgressionDocument> CreateAsync(UserProgressionDocument document, CancellationToken ct);
    Task UpdateAsync(UserProgressionDocument document, CancellationToken ct);
    Task DeleteAsync(Guid id, CancellationToken ct);
}
