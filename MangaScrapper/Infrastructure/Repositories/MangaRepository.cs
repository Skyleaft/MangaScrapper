using MangaScrapper.Infrastructure.Mongo;
using MangaScrapper.Infrastructure.Mongo.Collections;
using MongoDB.Driver;

namespace MangaScrapper.Infrastructure.Repositories;

public class MangaRepository : IMangaRepository
{
    private readonly IMongoCollection<MangaDocument> _collection;

    public MangaRepository(MongoContext context)
    {
        _collection = context.Mangas;
    }

    public async Task<MangaDocument?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        return await _collection.Find(m => m.Id == id).FirstOrDefaultAsync(ct);
    }

    public async Task<MangaDocument?> GetByTitleAsync(string title, CancellationToken ct)
    {
        return await _collection.Find(m => m.Title == title).FirstOrDefaultAsync(ct);
    }

    public async Task<List<MangaDocument>> GetAllAsync(CancellationToken ct)
    {
        return await _collection.Find(_ => true).ToListAsync(ct);
    }

    public async Task<Guid> CreateAsync(MangaDocument manga, CancellationToken ct)
    {
        manga.Id = Guid.NewGuid();
        manga.UpdatedAt = DateTime.UtcNow;
        await _collection.InsertOneAsync(manga, cancellationToken: ct);
        return manga.Id;
    }

    public async Task UpdateAsync(MangaDocument manga, CancellationToken ct)
    {
        manga.UpdatedAt = DateTime.UtcNow;
        await _collection.ReplaceOneAsync(m => m.Id == manga.Id, manga, cancellationToken: ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        await _collection.DeleteOneAsync(m => m.Id == id, ct);
    }
}