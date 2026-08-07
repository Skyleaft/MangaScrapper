using MangaScrapper.Infrastructure.Persistence.Documents;

namespace MangaScrapper.Infrastructure.Scrapers;

/// <summary>
/// Document-level repository used by scrapers for raw persistence operations
/// (bypasses domain layer — scrapers work directly with MangaDocument).
/// </summary>
public interface IScrapperRepository
{
    Task<MangaDocument?> GetDocumentByIdAsync(Guid id, CancellationToken ct = default);
    Task<List<MangaDocument>> GetAllDocumentsAsync(CancellationToken ct = default);
    Task CreateDocumentAsync(MangaDocument document, CancellationToken ct = default);
    Task UpdateDocumentAsync(MangaDocument document, CancellationToken ct = default);
    Task UpdateChapterPagesAsync(Guid mangaId, Guid chapterId, List<PageDocument> pages, CancellationToken ct = default);
}
