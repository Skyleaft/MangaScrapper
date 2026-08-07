using MangaScrapper.Domain.Aggregates;
using MangaScrapper.Domain.Repositories;
using MangaScrapper.Domain.ValueObjects;
using MangaScrapper.Infrastructure.Persistence;
using MangaScrapper.Infrastructure.Persistence.Documents;
using MongoDB.Bson;
using MongoDB.Driver;
using NovaStack.SharedKernel.Common;

namespace MangaScrapper.Infrastructure.Repositories;

public class MongoMangaRepository(MangaMongoDbContext dbContext) : IMangaRepository
{
    public async Task<Manga?> GetByIdAsync(MangaId id, CancellationToken ct = default)
    {
        var doc = await dbContext.Mangas.Find(m => m.Id == id.Value).FirstOrDefaultAsync(ct);
        return doc is null ? null : MapToDomain(doc);
    }

    public async Task<Manga?> GetByTitleAsync(string title, CancellationToken ct = default)
    {
        var doc = await dbContext.Mangas.Find(m => m.Title == title).FirstOrDefaultAsync(ct);
        return doc is null ? null : MapToDomain(doc);
    }

    public async Task<PagedList<Manga>> GetPagedAsync(string? search, List<string>? genres, string? status, string? type, string sortBy, string orderBy, int page,
        int pageSize, CancellationToken ct = default)
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

        var totalCount = await dbContext.Mangas.CountDocumentsAsync(filter, cancellationToken: ct);
        
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
            "totalview" => orderBy == "asc"
                ? sortBuilder.Ascending("Chapters.TotalView")
                : sortBuilder.Descending("Chapters.TotalView"),
            _ => orderBy == "asc"
                ? sortBuilder.Ascending(m => m.UpdatedAt)
                : sortBuilder.Descending(m => m.UpdatedAt),
        };
        var docs = await dbContext.Mangas.Find(filter)
            .Sort(sortDefinition)
            .Skip((page - 1) * pageSize)
            .Limit(pageSize)
            .Project<MangaDocument>(Builders<MangaDocument>.Projection.Exclude("chapters.pages"))
            .ToListAsync(ct);
        var items = docs.Select(MapToDomain).ToList();
        return new PagedList<Manga>(items, page, pageSize, (int)totalCount);
    }

    public async Task<List<Manga>> GetByIdsAsync(List<Guid> ids, CancellationToken ct)
    {
        var filter = Builders<MangaDocument>.Filter.In(m => m.Id, ids);
        var doc=  await dbContext.Mangas.Find(filter)
            .Project<MangaDocument>(Builders<MangaDocument>.Projection.Exclude("chapters.pages"))
            .ToListAsync(ct);
        return doc is null ? new List<Manga>() : doc.Select(MapToDomain).ToList();
    }

    public async Task AddAsync(Manga manga, CancellationToken ct = default)
    {
        var doc = MapToDocument(manga);
        await dbContext.Mangas.InsertOneAsync(doc, cancellationToken: ct);
    }

    public async Task UpdateAsync(Manga manga, CancellationToken ct = default)
    {
        var doc = MapToDocument(manga);
        await dbContext.Mangas.ReplaceOneAsync(m => m.Id == doc.Id, doc, cancellationToken: ct);
    }

    public async Task DeleteAsync(MangaId id, CancellationToken ct = default)
    {
        await dbContext.Mangas.DeleteOneAsync(m => m.Id == id.Value, ct);
    }

    public async Task<List<string>> GetAllGenresAsync(CancellationToken ct)
    {
        var result = await dbContext.Mangas.Distinct<string>("Genres", Builders<MangaDocument>.Filter.Empty).ToListAsync(ct);
        return result.OrderBy(g => g).ToList();
    }

    public async Task<List<string>> GetAllTypesAsync(CancellationToken ct)
    {
        var result = await dbContext.Mangas.Distinct<string>("Type", Builders<MangaDocument>.Filter.Empty).ToListAsync(ct);
        return result.OrderBy(t => t).ToList();
    }

    public async Task<DashboardStatistic> GetStatisticsAsync(CancellationToken ct)
    {
        var totalManga = await dbContext.Mangas.CountDocumentsAsync(_ => true, cancellationToken: ct);

        // Get unique providers
        var providers = await dbContext.Mangas.Distinct<string>("Chapters.ChapterProvider", FilterDefinition<MangaDocument>.Empty).ToListAsync(ct);
        var totalSourceProvider = providers.Count;

        var today = DateTime.UtcNow.Date;
        var lastMonth = DateTime.UtcNow.Date.AddDays(-30);

        // ScrappedToday (Count chapters uploaded today)
        var scrappedToday = await dbContext.Mangas.Aggregate()
            .Unwind<MangaDocument, ChapterDocumentUnwound>(m => m.Chapters)
            .Match(c => c.Chapters.UploadDate >= today)
            .Count()
            .FirstOrDefaultAsync(ct)
            .ContinueWith(t => t.Result?.Count ?? 0);

        // ScrappedThisMonth
        var scrappedThisMonth = await dbContext.Mangas.Aggregate()
            .Unwind<MangaDocument, ChapterDocumentUnwound>(m => m.Chapters)
            .Match(c => c.Chapters.UploadDate >= lastMonth)
            .Count()
            .FirstOrDefaultAsync(ct)
            .ContinueWith(t => t.Result?.Count ?? 0);

        var totalUnlinkedMetadata = await dbContext.Mangas.CountDocumentsAsync(m => m.MalID == 0, cancellationToken: ct);

        // Chapters with null or empty Link
        var totalUnavailableMangaChapter = await dbContext.Mangas
            .Find(m => m.Chapters.Any(c => c.Pages == null || c.Pages.Count == 0))
            .CountDocumentsAsync(ct);

        // Calculate TotalStorageUsed
        var thumbnailResult = await dbContext.Mangas.Aggregate()
            .Group(new BsonDocument { { "_id", BsonNull.Value }, { "total", new BsonDocument("$sum", "$thumbnailSize") } })
            .FirstOrDefaultAsync(ct);
        var totalThumbnailSize = thumbnailResult != null && thumbnailResult.Contains("total") ? thumbnailResult["total"].ToInt64() : 0;

        var pagesResult = await dbContext.Mangas.Aggregate()
            .Project(m => new
            {
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
        var monthlyScrapRaw = await dbContext.Mangas.Aggregate()
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

    public async Task<(List<Manga> Items, long TotalCount)> GetTrendingAsync(string? search, List<string>? genres, string? status, string? type, int page, int pageSize,
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

        var twoWeeksAgo = DateTime.UtcNow.AddDays(-14);

        var pipeline = new List<BsonDocument>();

        // 1. Match base filter
        var matchStage = new BsonDocument("$match", filter.Render(new RenderArgs<MangaDocument>(dbContext.Mangas.DocumentSerializer, dbContext.Mangas.Settings.SerializerRegistry)));
        pipeline.Add(matchStage);

        // 2. AddFields stage to calculate TrendingViews
        var addFieldsStage = new BsonDocument("$addFields", new BsonDocument("TrendingViews",
            new BsonDocument("$sum",
                new BsonDocument("$map", new BsonDocument
                {
                    { "input", new BsonDocument("$filter", new BsonDocument
                        {
                            { "input", "$chapters" },
                            { "as", "c" },
                            { "cond", new BsonDocument("$gte", new BsonArray { "$$c.uploadDate", twoWeeksAgo }) }
                        })
                    },
                    { "as", "c" },
                    { "in", "$$c.totalView" }
                })
            )
        ));
        pipeline.Add(addFieldsStage);

        // 3. Match only documents with TrendingViews > 0
        var matchTrending = new BsonDocument("$match", new BsonDocument("TrendingViews", new BsonDocument("$gt", 0)));
        pipeline.Add(matchTrending);

        // 4. Facet for count and paginated data
        var facetStage = new BsonDocument("$facet", new BsonDocument
        {
            { "totalCount", new BsonArray { new BsonDocument("$count", "count") } },
            { "data", new BsonArray
                {
                    new BsonDocument("$sort", new BsonDocument { { "TrendingViews", -1 }, { "updatedAt", -1 } }),
                    new BsonDocument("$skip", (page - 1) * pageSize),
                    new BsonDocument("$limit", pageSize),
                    new BsonDocument("$project", new BsonDocument { { "chapters.pages", 0 }, { "TrendingViews", 0 } })
                }
            }
        });
        pipeline.Add(facetStage);

        var aggregationResult = await dbContext.Mangas.Aggregate<BsonDocument>(pipeline, cancellationToken: ct).FirstOrDefaultAsync(ct);

        long totalCount = 0;
        var items = new List<Manga>();

        if (aggregationResult != null)
        {
            var totalCountArray = aggregationResult["totalCount"].AsBsonArray;
            if (totalCountArray.Count > 0)
            {
                totalCount = totalCountArray[0]["count"].AsInt32;
            }

            var dataArray = aggregationResult["data"].AsBsonArray;
            foreach (var doc in dataArray)
            {
                var mangaDoc = MongoDB.Bson.Serialization.BsonSerializer.Deserialize<Manga>(doc.AsBsonDocument);
                items.Add(mangaDoc);
            }
        }

        return (items, totalCount);
    }

    private static Manga MapToDomain(MangaDocument doc)
    {
        var chapters = doc.Chapters?.Select(c => new Chapter(
            ChapterId.From(c.Id),
            c.Number,
            c.Link,
            c.ChapterProvider,
            c.ChapterProviderIcon,
            c.Language,
            c.TotalView,
            c.UploadDate,
            c.Pages?.Select(p => new Page(p.Id, p.ImageUrl, p.LocalImageUrl, p.Size)).ToList()
        )).ToList();

        return Manga.Reconstitute(
            MangaId.From(doc.Id),
            doc.Title,
            doc.Author,
            doc.Type,
            doc.MalID,
            doc.Genres,
            doc.Description,
            doc.ImageUrl,
            doc.LocalImageUrl,
            doc.ThumbnailSize,
            doc.Rating,
            doc.Popularity,
            doc.Members,
            doc.Status,
            doc.ReleaseDate,
            doc.TotalView,
            doc.CreatedAt,
            doc.UpdatedAt,
            doc.Url,
            chapters);
    }

    private static MangaDocument MapToDocument(Manga manga)
    {
        return new MangaDocument
        {
            Id = manga.Id.Value,
            MalID = manga.MalId,
            Title = manga.Title,
            Author = manga.Author,
            Type = manga.Type,
            Rating = manga.Rating,
            Popularity = manga.Popularity,
            Members = manga.Members,
            Genres = manga.Genres,
            Description = manga.Description,
            ImageUrl = manga.ImageUrl,
            LocalImageUrl = manga.LocalImageUrl,
            ThumbnailSize = manga.ThumbnailSize,
            Status = manga.Status,
            ReleaseDate = manga.ReleaseDate,
            TotalView = manga.TotalView,
            CreatedAt = manga.CreatedAt,
            UpdatedAt = manga.UpdatedAt,
            Url = manga.Url,
            Chapters = manga.Chapters.Select(c => new ChapterDocument
            {
                Id = c.Id.Value,
                Number = c.Number,
                Link = c.Link,
                ChapterProvider = c.ChapterProvider,
                ChapterProviderIcon = c.ChapterProviderIcon,
                Language = c.Language,
                TotalView = c.TotalView,
                UploadDate = c.UploadDate,
                Pages = c.Pages.Select(p => new PageDocument
                {
                    Id = p.Id,
                    ImageUrl = p.ImageUrl,
                    LocalImageUrl = p.LocalImageUrl,
                    Size = p.Size
                }).ToList()
            }).ToList()
        };
    }
    
    private class ChapterDocumentUnwound
    {
        public ChapterDocument Chapters { get; set; } = null!;
    }
}
