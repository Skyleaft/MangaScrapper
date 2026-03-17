using MangaScrapper.Infrastructure.Mongo;
using MangaScrapper.Infrastructure.Mongo.Collections;
using MongoDB.Driver;

namespace MangaScrapper.Infrastructure.Repositories;

public class UserLibraryRepository : IUserLibraryRepository
{
    private readonly IMongoCollection<UserLibraryDocument> _collection;

    public UserLibraryRepository(MongoContext context)
    {
        _collection = context.UserLibraries;
    }

    public async Task<UserLibraryDocument?> GetByUserAndMangaAsync(Guid userId, Guid mangaId, CancellationToken ct)
    {
        return await _collection.Find(x => x.UserId == userId && x.MangaId == mangaId).FirstOrDefaultAsync(ct);
    }

    public async Task<List<UserLibraryDocument>> GetByUserAsync(Guid userId, CancellationToken ct)
    {
        return await _collection.Find(x => x.UserId == userId).ToListAsync(ct);
    }

    public async Task<UserLibraryDocument> CreateAsync(UserLibraryDocument document, CancellationToken ct)
    {
        await _collection.InsertOneAsync(document, cancellationToken: ct);
        return document;
    }

    public async Task UpdateAsync(UserLibraryDocument document, CancellationToken ct)
    {
        document.UpdatedAt = DateTime.UtcNow;
        await _collection.ReplaceOneAsync(x => x.Id == document.Id, document, cancellationToken: ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        await _collection.DeleteOneAsync(x => x.Id == id, ct);
    }
}
