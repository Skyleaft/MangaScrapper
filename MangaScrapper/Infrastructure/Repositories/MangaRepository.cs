using MangaScrapper.Infrastructure.Mongo;
using MangaScrapper.Infrastructure.Mongo.Collections;
using MongoDB.Bson;
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

    public async Task<(List<MangaDocument> Items, long TotalCount)> GetPagedAsync(
        string? search,
        List<string>? genres,
        string? status,
        string? type,
        string sortBy,
        string orderBy,
        int page,
        int pageSize,
        CancellationToken ct)
    {
        var builder = Builders<MangaDocument>.Filter;
        var filter = builder.Empty;

        if (!string.IsNullOrWhiteSpace(search))
        {
            filter &= builder.Regex(m => m.Title, new BsonRegularExpression(search, "i"));
        }

        if (genres != null && genres.Any())
        {
            filter &= builder.All(m => m.Genres, genres);
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            filter &= builder.Eq(m => m.Status, status);
        }

        if (!string.IsNullOrWhiteSpace(type))
        {
            filter &= builder.Eq(m => m.Type, type);
        }

        var totalCount = await _collection.CountDocumentsAsync(filter, cancellationToken: ct);

        List<MangaDocument> items;

        var sortBuilder = Builders<MangaDocument>.Sort;
        SortDefinition<MangaDocument> sortDefinition = sortBy.ToLowerInvariant() switch
        {
            "title" => orderBy == "asc"
                ? sortBuilder.Ascending(m => m.Title)
                : sortBuilder.Descending(m => m.Title),
            "createdat" => orderBy == "asc"
                ? sortBuilder.Ascending(m => m.CreatedAt)
                : sortBuilder.Descending(m => m.CreatedAt),
            "latestchapter" => orderBy == "asc"
                ? sortBuilder.Ascending("Chapters.UploadDate")
                : sortBuilder.Descending("Chapters.UploadDate"),
            "totalview"=> orderBy == "asc"
                ? sortBuilder.Ascending("Chapters.TotalView")
                : sortBuilder.Descending("Chapters.TotalView"),
            _ => orderBy == "asc"
                ? sortBuilder.Ascending(m => m.UpdatedAt)
                : sortBuilder.Descending(m => m.UpdatedAt),
        };
        items = await _collection.Find(filter)
            .Sort(sortDefinition)
            .Skip((page - 1) * pageSize)
            .Limit(pageSize)
            .Project<MangaDocument>(Builders<MangaDocument>.Projection.Exclude("chapters.pages"))
            .ToListAsync(ct);

        return (items, totalCount);
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

    public async Task UpdateChapterPagesAsync(Guid mangaId, Guid chapterId, List<PageDocument> pages,
        CancellationToken ct)
    {
        var filter = Builders<MangaDocument>.Filter.And(
            Builders<MangaDocument>.Filter.Eq(m => m.Id, mangaId),
            Builders<MangaDocument>.Filter.ElemMatch(m => m.Chapters, c => c.Id == chapterId)
        );

        var update = Builders<MangaDocument>.Update
            .Set("chapters.$.pages", pages)
            .Set(m => m.UpdatedAt, DateTime.UtcNow);

        await _collection.UpdateOneAsync(filter, update, cancellationToken: ct);
    }

    public async Task<List<string>> GetAllGenresAsync(CancellationToken ct)
    {
        var result = await _collection.Distinct<string>("Genres", Builders<MangaDocument>.Filter.Empty).ToListAsync(ct);
        return result.OrderBy(g => g).ToList();
    }

    public async Task<List<string>> GetAllTypesAsync(CancellationToken ct)
    {
        var result = await _collection.Distinct<string>("Type", Builders<MangaDocument>.Filter.Empty).ToListAsync(ct);
        return result.OrderBy(t => t).ToList();
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        await _collection.DeleteOneAsync(m => m.Id == id, ct);
    }
}