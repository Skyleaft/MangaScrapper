using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Product.Infrastructure.Persistence.Documents;

/// <summary>
///     MongoDB POCO document that maps to the <c>products</c> collection.
///     This is a flat read/write model optimised for MongoDB storage —
///     it is intentionally kept separate from the <c>Product.Domain.Aggregates.Product</c>
///     aggregate to avoid EF Core private-setter conflicts and allow a Mongo-friendly shape.
/// </summary>
public sealed class ProductDocument
{
    [BsonId]
    [BsonRepresentation(BsonType.String)]
    public Guid Id { get; set; }

    [BsonElement("name")] public string Name { get; set; } = string.Empty;

    [BsonElement("description")] public string Description { get; set; } = string.Empty;

    [BsonElement("price_amount")] public decimal PriceAmount { get; set; }

    [BsonElement("price_currency")] public string PriceCurrency { get; set; } = string.Empty;

    [BsonElement("stock_quantity")] public int StockQuantity { get; set; }

    [BsonElement("is_active")] public bool IsActive { get; set; } = true;

    [BsonElement("created_at")] public DateTime CreatedAt { get; set; }

    [BsonElement("updated_at")] public DateTime? UpdatedAt { get; set; }

    [BsonElement("created_by")] public string CreatedBy { get; set; } = string.Empty;
}