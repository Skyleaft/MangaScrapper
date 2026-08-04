using MongoDB.Bson;
using MongoDB.Driver;
using NovaStack.SharedKernel.Common;
using Product.Domain.Repositories;
using Product.Domain.ValueObjects;
using Product.Infrastructure.Persistence;
using Product.Infrastructure.Persistence.Documents;
using DomainProduct = Product.Domain.Aggregates.Product;

namespace Product.Infrastructure.Repositories;

/// <summary>
///     MongoDB native-driver implementation of <see cref="IProductRepository" />.
///     Registered when <c>Database:Provider</c> is <c>MongoDB</c>.
///     <para>
///         This repository uses a separate <see cref="ProductDocument" /> POCO to read/write
///         to MongoDB, then maps back to the domain aggregate for application-layer consumers.
///         Domain events raised by the aggregate are NOT persisted to an outbox here —
///         they are published directly via the event bus after each write.
///     </para>
/// </summary>
internal sealed class MongoProductRepository(ProductMongoDbContext context) : IProductRepository
{
    private readonly IMongoCollection<ProductDocument> _collection = context.Products;

    // ── Queries ───────────────────────────────────────────────────────────────

    public async Task<DomainProduct?> GetByIdAsync(ProductId id, CancellationToken ct = default)
    {
        var doc = await _collection
            .Find(ById(id))
            .FirstOrDefaultAsync(ct);

        return doc is null ? null : MapToDomain(doc);
    }

    public async Task<IEnumerable<DomainProduct>> GetAllAsync(CancellationToken ct = default)
    {
        var docs = await _collection
            .Find(ActiveOnly())
            .ToListAsync(ct);

        return docs.Select(MapToDomain);
    }

    public async Task<DomainProduct?> GetByNameAsync(string name, CancellationToken ct = default)
    {
        var doc = await _collection
            .Find(Builders<ProductDocument>.Filter.Eq(d => d.Name, name))
            .FirstOrDefaultAsync(ct);

        return doc is null ? null : MapToDomain(doc);
    }

    public async Task<bool> ExistsAsync(ProductId id, CancellationToken ct = default)
    {
        return await _collection.CountDocumentsAsync(ById(id), cancellationToken: ct) > 0;
    }

    public async Task<bool> ExistsByNameAsync(string name, CancellationToken ct = default)
    {
        return await _collection.CountDocumentsAsync(
            Builders<ProductDocument>.Filter.Eq(d => d.Name, name),
            cancellationToken: ct) > 0;
    }

    // ── Commands ──────────────────────────────────────────────────────────────

    public async Task AddAsync(DomainProduct entity, CancellationToken ct = default)
    {
        await _collection.InsertOneAsync(MapToDocument(entity), cancellationToken: ct);
    }

    public async Task UpdateAsync(DomainProduct entity, CancellationToken ct = default)
    {
        var doc = MapToDocument(entity);
        await _collection.ReplaceOneAsync(ById(entity.Id), doc, cancellationToken: ct);
    }

    public async Task DeleteAsync(DomainProduct entity, CancellationToken ct = default)
    {
        await _collection.DeleteOneAsync(ById(entity.Id), ct);
    }

    // ── Filters ───────────────────────────────────────────────────────────────
    private static FilterDefinition<ProductDocument> ById(ProductId id)
    {
        return Builders<ProductDocument>.Filter.Eq(d => d.Id, id.Value);
    }

    private static FilterDefinition<ProductDocument> ActiveOnly()
    {
        return Builders<ProductDocument>.Filter.Eq(d => d.IsActive, true);
    }

    public async Task<PagedList<DomainProduct>> GetPagedAsync(
        int page,
        int pageSize,
        string? search = null,
        CancellationToken ct = default)
    {
        var filter = string.IsNullOrWhiteSpace(search)
            ? ActiveOnly()
            : Builders<ProductDocument>.Filter.And(
                ActiveOnly(),
                Builders<ProductDocument>.Filter.Or(
                    Builders<ProductDocument>.Filter.Regex(d => d.Name, new BsonRegularExpression(search, "i")),
                    Builders<ProductDocument>.Filter.Regex(d => d.Description,
                        new BsonRegularExpression(search, "i"))));

        var total = await _collection.CountDocumentsAsync(filter, cancellationToken: ct);
        var docs = await _collection
            .Find(filter)
            .SortBy(d => d.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Limit(pageSize)
            .ToListAsync(ct);

        return new PagedList<DomainProduct>(docs.Select(MapToDomain).ToList(), page, pageSize, (int)total);
    }

    // ── Mapping ───────────────────────────────────────────────────────────────

    private static ProductDocument MapToDocument(DomainProduct p)
    {
        return new ProductDocument
        {
            Id = p.Id.Value,
            Name = p.Name,
            Description = p.Description,
            PriceAmount = p.Price.Amount,
            PriceCurrency = p.Price.Currency,
            StockQuantity = p.StockQuantity,
            IsActive = p.IsActive,
            CreatedAt = p.CreatedAt,
            UpdatedAt = p.UpdatedAt,
            CreatedBy = p.CreatedBy
        };
    }

    private static DomainProduct MapToDomain(ProductDocument doc)
    {
        return DomainProduct.Reconstitute(
            ProductId.From(doc.Id),
            doc.Name,
            doc.Description,
            Money.Create(doc.PriceAmount, doc.PriceCurrency),
            doc.StockQuantity,
            doc.IsActive,
            doc.CreatedAt,
            doc.UpdatedAt,
            doc.CreatedBy);
    }
}