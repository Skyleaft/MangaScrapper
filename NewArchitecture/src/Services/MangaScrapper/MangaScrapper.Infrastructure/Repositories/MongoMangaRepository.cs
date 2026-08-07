using MangaScrapper.Domain.Aggregates;
using MangaScrapper.Domain.Repositories;
using MangaScrapper.Domain.ValueObjects;
using MangaScrapper.Infrastructure.Persistence;
using MangaScrapper.Infrastructure.Persistence.Documents;
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

    public async Task<PagedList<Manga>> GetPagedAsync(
        int page,
        int pageSize,
        string? search = null,
        string? type = null,
        string? genre = null,
        CancellationToken ct = default)
    {
        var builder = Builders<MangaDocument>.Filter;
        var filter = builder.Empty;

        if (!string.IsNullOrWhiteSpace(search))
        {
            filter &= builder.Regex(m => m.Title, new MongoDB.Bson.BsonRegularExpression(search, "i"));
        }

        if (!string.IsNullOrWhiteSpace(type))
        {
            filter &= builder.Eq(m => m.Type, type);
        }

        if (!string.IsNullOrWhiteSpace(genre))
        {
            filter &= builder.AnyEq(m => m.Genres, genre);
        }

        var totalCount = await dbContext.Mangas.CountDocumentsAsync(filter, cancellationToken: ct);
        var docs = await dbContext.Mangas.Find(filter)
            .SortByDescending(m => m.UpdatedAt)
            .Skip((page - 1) * pageSize)
            .Limit(pageSize)
            .ToListAsync(ct);

        var items = docs.Select(MapToDomain).ToList();
        return new PagedList<Manga>(items, page, pageSize, (int)totalCount);
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
}
