using MangaScrapper.Infrastructure.Mongo;
using MangaScrapper.Infrastructure.Mongo.Collections;
using MangaScrapper.Infrastructure.Utils;
using MangaScrapper.Shared.Models;
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

    public async Task<List<MangaDocument>> GetByIdsAsync(List<Guid> ids, CancellationToken ct)
    {
        var filter = Builders<MangaDocument>.Filter.In(m => m.Id, ids);
        return await _collection.Find(filter)
            .Project<MangaDocument>(Builders<MangaDocument>.Projection.Exclude("chapters.pages"))
            .ToListAsync(ct);
    }

    public async Task<MangaDocument?> GetByTitleAsync(string title, CancellationToken ct)
    {
        return await _collection.Find(m => m.Title.Contains(title)).FirstOrDefaultAsync(ct);
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
        await _collection.InsertOneAsync(manga, cancellationToken: ct);
        return manga.Id;
    }

    public async Task UpdateAsync(MangaDocument manga, CancellationToken ct)
    {
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

    public async Task<bool> DeleteChapterAsync(Guid mangaId, Guid chapterId, CancellationToken ct)
    {
        var filter = Builders<MangaDocument>.Filter.Eq(m => m.Id, mangaId);
        var update = Builders<MangaDocument>.Update
            .PullFilter(m => m.Chapters, c => c.Id == chapterId)
            .Set(m => m.UpdatedAt, DateTime.UtcNow);

        var result = await _collection.UpdateOneAsync(filter, update, cancellationToken: ct);
        return result.ModifiedCount > 0;
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

    public async Task<DashboardStatistic> GetStatisticsAsync(CancellationToken ct)
    {
        var totalManga = await _collection.CountDocumentsAsync(_ => true, cancellationToken: ct);

        // Get unique providers
        var providers = await _collection.Distinct<string>("Chapters.ChapterProvider", FilterDefinition<MangaDocument>.Empty).ToListAsync(ct);
        var totalSourceProvider = providers.Count;

        var today = DateTime.UtcNow.Date;
        var lastMonth = DateTime.UtcNow.Date.AddDays(-30);

        // ScrappedToday (Count chapters uploaded today)
        var scrappedToday = await _collection.Aggregate()
            .Unwind<MangaDocument, ChapterDocumentUnwound>(m => m.Chapters)
            .Match(c => c.Chapters.UploadDate >= today)
            .Count()
            .FirstOrDefaultAsync(ct)
            .ContinueWith(t => t.Result?.Count ?? 0);

        // ScrappedThisMonth
        var scrappedThisMonth = await _collection.Aggregate()
            .Unwind<MangaDocument, ChapterDocumentUnwound>(m => m.Chapters)
            .Match(c => c.Chapters.UploadDate >= lastMonth)
            .Count()
            .FirstOrDefaultAsync(ct)
            .ContinueWith(t => t.Result?.Count ?? 0);

        var totalUnlinkedMetadata = await _collection.CountDocumentsAsync(m => m.MalID == 0, cancellationToken: ct);

        // Chapters with null or empty Link
        var totalUnavailableMangaChapter = await _collection
            .Find(m => m.Chapters.Any(c => c.Pages == null || c.Pages.Count == 0))
            .CountDocumentsAsync(ct);
        
        // Calculate TotalStorageUsed
        var thumbnailResult = await _collection.Aggregate()
            .Group(new BsonDocument { { "_id", BsonNull.Value }, { "total", new BsonDocument("$sum", "$thumbnailSize") } })
            .FirstOrDefaultAsync(ct);
        var totalThumbnailSize = thumbnailResult != null && thumbnailResult.Contains("total") ? thumbnailResult["total"].ToInt64() : 0;

        var pagesResult = await _collection.Aggregate()
            .Project(m => new {
                totalSize = m.Chapters.Sum(c => c.Pages.Sum(p => p.Size))
            })
            .Group(new BsonDocument 
            { 
                { "_id", BsonNull.Value }, 
                { "total", new BsonDocument("$sum", "$totalSize") } 
            })
            .FirstOrDefaultAsync(ct);
        var totalPagesSize = pagesResult != null && pagesResult.Contains("total") ? pagesResult["total"].ToInt64() : 0;

        var totalStorageUsed = totalThumbnailSize + totalPagesSize;

        // Calculate MonthlyScrap
        var monthlyScrapRaw = await _collection.Aggregate()
            .Unwind<MangaDocument, ChapterDocumentUnwound>(m => m.Chapters)
            .Match(c => c.Chapters.UploadDate >= lastMonth)
            .Group(c => new { Date = c.Chapters.UploadDate.Date }, g => new { Date = g.Key.Date, Count = g.Count() })
            .SortBy(x => x.Date)
            .ToListAsync(ct);

        var monthlyScrap = new List<ScrapStats>();
        for (int i = 0; i <= 30; i++)
        {
            var date = lastMonth.AddDays(i);
            var stats = monthlyScrapRaw.FirstOrDefault(x => x.Date.Date == date.Date);
            monthlyScrap.Add(new ScrapStats
            {
                Date = date,
                TotalScrap = stats?.Count ?? 0
            });
        }

        return new DashboardStatistic
        {
            TotalManga = totalManga,
            TotalSourceProvider = totalSourceProvider,
            ScrappedToday = scrappedToday,
            ScrappedThisMonth = scrappedThisMonth,
            TotalUnlinkedMetadata = totalUnlinkedMetadata,
            TotalUnavailableMangaChapter = totalUnavailableMangaChapter,
            TotalStorageUsed = totalStorageUsed,
            MonthlyScrap = monthlyScrap
        };
    }

    private class ChapterDocumentUnwound
    {
        public ChapterDocument Chapters { get; set; } = null!;
    }
}
