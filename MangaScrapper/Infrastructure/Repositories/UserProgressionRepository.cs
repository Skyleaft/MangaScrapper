using MangaScrapper.Infrastructure.Mongo;
using MangaScrapper.Infrastructure.Mongo.Collections;
using MongoDB.Driver;

namespace MangaScrapper.Infrastructure.Repositories;

public class UserProgressionRepository : IUserProgressionRepository
{
    private readonly IMongoCollection<UserProgressionDocument> _collection;

    public UserProgressionRepository(MongoContext context)
    {
        _collection = context.UserProgressions;
    }

    public async Task<UserProgressionDocument?> GetByUserAndMangaAsync(Guid userId, Guid mangaId, CancellationToken ct)
    {
        var doc = await _collection.Find(x => x.UserId == userId && x.MangaId == mangaId).FirstOrDefaultAsync(ct);
        doc?.MigrateIfNeeded();
        return doc;
    }

    public async Task<List<UserProgressionDocument>> GetByUserAsync(Guid userId, CancellationToken ct)
    {
        var docs = await _collection.Find(x => x.UserId == userId).ToListAsync(ct);
        foreach (var doc in docs)
        {
            doc.MigrateIfNeeded();
        }
        return docs;
    }

    public async Task<UserProgressionDocument> CreateAsync(UserProgressionDocument document, CancellationToken ct)
    {
        await _collection.InsertOneAsync(document, cancellationToken: ct);
        return document;
    }

    public async Task UpdateAsync(UserProgressionDocument document, CancellationToken ct)
    {
        document.LastReadAt = DateTime.UtcNow;
        await _collection.ReplaceOneAsync(x => x.Id == document.Id, document, cancellationToken: ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        await _collection.DeleteOneAsync(x => x.Id == id, ct);
    }
}
