using MangaScrapper.Domain.Aggregates;
using MangaScrapper.Domain.Repositories;
using MangaScrapper.Domain.ValueObjects;
using MangaScrapper.Infrastructure.Persistence;
using MangaScrapper.Infrastructure.Persistence.Documents;
using MongoDB.Driver;
using NovaStack.SharedKernel.Common;

namespace MangaScrapper.Infrastructure.Repositories;

public class MongoUserLibraryRepository(MangaMongoDbContext dbContext) : IUserLibraryRepository
{
    public async Task<UserLibrary?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var doc = await dbContext.UserLibraries.Find(l => l.Id == id).FirstOrDefaultAsync(ct);
        return doc is null ? null : MapToDomain(doc);
    }

    public async Task<UserLibrary?> GetByUserIdAndMangaIdAsync(string userId, MangaId mangaId, CancellationToken ct = default)
    {
        if (!Guid.TryParse(userId, out var userGuid)) return null;
        var doc = await dbContext.UserLibraries.Find(l => l.UserId == userGuid && l.MangaId == mangaId.Value).FirstOrDefaultAsync(ct);
        return doc is null ? null : MapToDomain(doc);
    }

    public async Task<PagedList<UserLibrary>> GetPagedByUserIdAsync(string userId, int page, int pageSize, CancellationToken ct = default)
    {
        if (!Guid.TryParse(userId, out var userGuid))
            return new PagedList<UserLibrary>([], page, pageSize, 0);

        var filter = Builders<UserLibraryDocument>.Filter.Eq(l => l.UserId, userGuid);
        var totalCount = await dbContext.UserLibraries.CountDocumentsAsync(filter, cancellationToken: ct);
        var docs = await dbContext.UserLibraries.Find(filter)
            .SortByDescending(l => l.AddedAt)
            .Skip((page - 1) * pageSize)
            .Limit(pageSize)
            .ToListAsync(ct);

        var items = docs.Select(MapToDomain).ToList();
        return new PagedList<UserLibrary>(items, page, pageSize, (int)totalCount);
    }

    public async Task AddAsync(UserLibrary userLibrary, CancellationToken ct = default)
    {
        var doc = MapToDocument(userLibrary);
        await dbContext.UserLibraries.InsertOneAsync(doc, cancellationToken: ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await dbContext.UserLibraries.DeleteOneAsync(l => l.Id == id, ct);
    }

    private static UserLibrary MapToDomain(UserLibraryDocument doc)
    {
        return UserLibrary.Reconstitute(doc.Id, doc.UserId.ToString(), MangaId.From(doc.MangaId), doc.AddedAt);
    }

    private static UserLibraryDocument MapToDocument(UserLibrary library)
    {
        Guid.TryParse(library.UserId, out var userGuid);
        return new UserLibraryDocument
        {
            Id = library.Id,
            UserId = userGuid,
            MangaId = library.MangaId.Value,
            AddedAt = library.AddedAt,
            UpdatedAt = DateTime.UtcNow
        };
    }
}

public class MongoUserProgressionRepository(MangaMongoDbContext dbContext) : IUserProgressionRepository
{
    public async Task<UserProgression?> GetByUserIdAndMangaIdAsync(string userId, MangaId mangaId, CancellationToken ct = default)
    {
        if (!Guid.TryParse(userId, out var userGuid)) return null;
        var doc = await dbContext.UserProgressions.Find(p => p.UserId == userGuid && p.MangaId == mangaId.Value).FirstOrDefaultAsync(ct);
        return doc is null ? null : MapToDomain(doc);
    }

    public async Task<List<UserProgression>> GetByUserIdAsync(string userId, CancellationToken ct = default)
    {
        if (!Guid.TryParse(userId, out var userGuid)) return [];
        var docs = await dbContext.UserProgressions.Find(p => p.UserId == userGuid).ToListAsync(ct);
        return docs.Select(MapToDomain).ToList();
    }

    public async Task AddOrUpdateAsync(UserProgression userProgression, CancellationToken ct = default)
    {
        var doc = MapToDocument(userProgression);
        await dbContext.UserProgressions.ReplaceOneAsync(
            p => p.UserId == doc.UserId && p.MangaId == doc.MangaId,
            doc,
            new ReplaceOptions { IsUpsert = true },
            cancellationToken: ct);
    }

    private static UserProgression MapToDomain(UserProgressionDocument doc)
    {
        return UserProgression.Reconstitute(
            doc.Id,
            doc.UserId.ToString(),
            MangaId.From(doc.MangaId),
            ChapterId.From(doc.LastReadChapterId),
            doc.LastReadChapterNumber,
            doc.LastReadAt);
    }

    private static UserProgressionDocument MapToDocument(UserProgression progression)
    {
        Guid.TryParse(progression.UserId, out var userGuid);
        return new UserProgressionDocument
        {
            Id = progression.Id,
            UserId = userGuid,
            MangaId = progression.MangaId.Value,
            LastReadChapterId = progression.LastReadChapterId.Value,
            LastReadChapterNumber = progression.LastReadChapterNumber,
            LastReadAt = progression.LastReadAt
        };
    }
}
