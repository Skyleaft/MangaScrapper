using MangaScrapper.Infrastructure.Mongo.Collections;

namespace MangaScrapper.Infrastructure.Repositories;

public interface IMangaRepository
{
    Task<MangaDocument?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<MangaDocument?> GetByTitleAsync(string title, CancellationToken ct);
    Task<List<MangaDocument>> GetAllAsync(CancellationToken ct);
    Task<(List<MangaDocument> Items, long TotalCount)> GetPagedAsync(
        string? search, 
        List<string>? genres, 
        string? status, 
        string? type,
        int page, 
        int pageSize, 
        CancellationToken ct);
    Task<Guid> CreateAsync(MangaDocument manga, CancellationToken ct);
    Task UpdateAsync(MangaDocument manga, CancellationToken ct);
    Task UpdateChapterPagesAsync(Guid mangaId, Guid chapterId, List<PageDocument> pages, CancellationToken ct);
    Task DeleteAsync(Guid id, CancellationToken ct);
}