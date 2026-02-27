using MangaScrapper.Infrastructure.Mongo.Collections;

namespace MangaScrapper.Infrastructure.Repositories;

public interface IMangaRepository
{
    Task<MangaDocument?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<MangaDocument?> GetByTitleAsync(string title, CancellationToken ct);
    Task<List<MangaDocument>> GetAllAsync(CancellationToken ct);
    Task<Guid> CreateAsync(MangaDocument manga, CancellationToken ct);
    Task UpdateAsync(MangaDocument manga, CancellationToken ct);
    Task DeleteAsync(Guid id, CancellationToken ct);
}