using MangaScrapper.Domain.Aggregates;
using MangaScrapper.Infrastructure.Persistence;
using MangaScrapper.Infrastructure.Persistence.Documents;
using Mapster;
using MongoDB.Driver;

namespace MangaScrapper.Infrastructure.Scrapers;

/// <summary>
/// MongoDB implementation of IScrapperRepository — works with Manga domain aggregate and Mapster mapping.
/// </summary>
public class MongoScrapperRepository(MangaMongoDbContext dbContext) : IScrapperRepository
{
    public async Task<Manga?> GetDocumentByIdAsync(Guid id, CancellationToken ct = default)
    {
        var doc = await dbContext.Mangas.Find(m => m.Id == id).FirstOrDefaultAsync(ct);
        return doc is null ? null : doc.Adapt<Manga>();
    }

    public async Task<List<Manga>> GetAllDocumentsAsync(CancellationToken ct = default)
    {
        var docs = await dbContext.Mangas.Find(_ => true).ToListAsync(ct);
        return docs.Select(d => d.Adapt<Manga>()).ToList();
    }

    public async Task CreateDocumentAsync(Manga manga, CancellationToken ct = default)
    {
        var doc = manga.Adapt<MangaDocument>();
        await dbContext.Mangas.InsertOneAsync(doc, cancellationToken: ct);
    }

    public async Task UpdateDocumentAsync(Manga manga, CancellationToken ct = default)
    {
        var doc = manga.Adapt<MangaDocument>();
        await dbContext.Mangas.ReplaceOneAsync(m => m.Id == doc.Id, doc, cancellationToken: ct);
    }

    public async Task UpdateChapterPagesAsync(Guid mangaId, Guid chapterId, List<PageDocument> pages, CancellationToken ct = default)
    {
        var doc = await dbContext.Mangas.Find(m => m.Id == mangaId).FirstOrDefaultAsync(ct);
        if (doc is null) return;

        var chapter = doc.Chapters?.FirstOrDefault(c => c.Id == chapterId);
        if (chapter is null) return;

        chapter.Pages = pages;
        await dbContext.Mangas.ReplaceOneAsync(m => m.Id == doc.Id, doc, cancellationToken: ct);
    }
}
