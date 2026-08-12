using MangaScrapper.Domain.Aggregates;
using MangaScrapper.Infrastructure.Persistence.Documents;

namespace MangaScrapper.Infrastructure.Scrapers;

/// <summary>
/// Repository used by scrapers to persist and retrieve Manga Domain Aggregates.
/// Uses Mapster internally for document mapping.
/// </summary>
public interface IScrapperRepository
{
    Task<Manga?> GetDocumentByIdAsync(Guid id, CancellationToken ct = default);
    Task<List<Manga>> GetAllDocumentsAsync(CancellationToken ct = default);
    Task CreateDocumentAsync(Manga manga, CancellationToken ct = default);
    Task UpdateDocumentAsync(Manga manga, CancellationToken ct = default);
    Task UpdateChapterPagesAsync(Guid mangaId, Guid chapterId, List<PageDocument> pages, CancellationToken ct = default);
}
