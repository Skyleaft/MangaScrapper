using MangaScrapper.Infrastructure.Persistence;
using MangaScrapper.Infrastructure.Persistence.Documents;
using MongoDB.Driver;

namespace MangaScrapper.Infrastructure.Scrapers;

/// <summary>
/// MongoDB implementation of IScrapperRepository — works directly with MangaDocument (no domain mapping).
/// </summary>
public class MongoScrapperRepository(MangaMongoDbContext dbContext) : IScrapperRepository
{
    public async Task<MangaDocument?> GetDocumentByIdAsync(Guid id, CancellationToken ct = default)
        => await dbContext.Mangas.Find(m => m.Id == id).FirstOrDefaultAsync(ct);

    public async Task<List<MangaDocument>> GetAllDocumentsAsync(CancellationToken ct = default)
        => await dbContext.Mangas.Find(_ => true).ToListAsync(ct);

    public async Task CreateDocumentAsync(MangaDocument document, CancellationToken ct = default)
        => await dbContext.Mangas.InsertOneAsync(document, cancellationToken: ct);

    public async Task UpdateDocumentAsync(MangaDocument document, CancellationToken ct = default)
        => await dbContext.Mangas.ReplaceOneAsync(m => m.Id == document.Id, document, cancellationToken: ct);

    public async Task UpdateChapterPagesAsync(Guid mangaId, Guid chapterId, List<PageDocument> pages, CancellationToken ct = default)
    {
        var manga = await GetDocumentByIdAsync(mangaId, ct);
        if (manga is null) return;

        var chapter = manga.Chapters.FirstOrDefault(c => c.Id == chapterId);
        if (chapter is null) return;

        chapter.Pages = pages;
        await UpdateDocumentAsync(manga, ct);
    }
}
