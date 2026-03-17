using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace MangaScrapper.Infrastructure.Mongo.Collections;

public class UserLibraryDocument
{
    [BsonId]
    [BsonRepresentation(BsonType.String)]
    public Guid Id { get; set; } = Guid.CreateVersion7();

    [BsonRepresentation(BsonType.String)]
    public Guid UserId { get; set; }

    [BsonRepresentation(BsonType.String)]
    public Guid MangaId { get; set; }

    public string MangaTitle { get; set; } = string.Empty;
    public string Type { get; set; } = "Manga";

    public string? MangaImageUrl { get; set; }

    public string Status { get; set; } = "Reading";
    // Reading | Completed | OnHold | Dropped | PlanToRead

    public bool IsFavorite { get; set; }

    public double LastReadChapter { get; set; }

    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime AddedAt { get; set; } = DateTime.UtcNow;

    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}